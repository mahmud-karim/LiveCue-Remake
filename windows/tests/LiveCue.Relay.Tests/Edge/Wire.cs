using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// What the edge tests share: the exact error bodies, raw request heads without credentials, and the
/// bytes of a response exactly as the server wrote them, so that two answers can be compared byte for
/// byte.
/// </summary>
internal static partial class Wire
{
    /// <summary>Per connection: bytes that were read from the socket beyond the response that was asked for.</summary>
    private static readonly ConditionalWeakTable<RawHttp, List<byte>> Unread = [];

    public const string Unauthorized = """{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}""";
    public const string NotFound = """{"error":"Endpoint not found.","code":"NOT_FOUND"}""";
    public const string ProtocolMismatch = """{"error":"Protocol version mismatch.","code":"PROTOCOL_MISMATCH"}""";
    public const string InvalidRequest = """{"error":"Request is not valid for this endpoint.","code":"INVALID_REQUEST"}""";
    public const string InvalidJson = """{"error":"Body must be valid JSON.","code":"INVALID_JSON"}""";
    public const string PayloadTooLarge = """{"error":"Request exceeds 128 KB.","code":"PAYLOAD_TOO_LARGE"}""";
    public const string RequestTimeout = """{"error":"Request body did not arrive in time.","code":"REQUEST_TIMEOUT"}""";
    public const string TooManyRequests = """{"error":"Too many requests in progress.","code":"TOO_MANY_REQUESTS"}""";
    public const string DeadlineExceeded = """{"error":"The PC took too long to answer.","code":"DEADLINE_EXCEEDED"}""";
    public const string AssistantTimeout = """{"error":"The local assistant request timed out.","code":"ASSISTANT_TIMEOUT"}""";
    public const string InternalError = """{"error":"The PC relay hit an internal error.","code":"INTERNAL_ERROR"}""";

    /// <summary>The allowlist of W-design K-1: what relay code sets, the length, and what Kestrel itself adds.</summary>
    public static readonly string[] AllowedResponseHeaders =
        ["Content-Type", "Cache-Control", "X-Content-Type-Options", "Content-Length", "Date", "Connection"];

    /// <summary>
    /// The header lines of the fixed 401, in any order, with the value of <c>Date</c> replaced by a dash.
    /// A request that declared a body gets one more: <c>Connection: close</c> (W-design K-4).
    /// </summary>
    public static readonly string[] Fixed401Headers =
    [
        "Cache-Control: no-store",
        "Content-Length: 56",
        "Content-Type: application/json; charset=utf-8",
        "Date: -",
        "X-Content-Type-Options: nosniff",
    ];

    public const string Fixed401StatusLine = "HTTP/1.1 401 Unauthorized";

    /// <summary>U+2028 LINE SEPARATOR: a line break that can travel inside a header value (test plan ST-13).</summary>
    public const string LineSeparator = "\x2028";

    private const string Break = "\r\n";
    private const string BlankLine = "\r\n\r\n";

    /// <summary>The nine HTTP routes of protocol 6.1 (route 10 is the speech upgrade).</summary>
    public static IEnumerable<LiveCue.Relay.Edge.RouteEntry> HttpRoutes =>
        LiveCue.Relay.Edge.RouteTable.Rows.Where(row => row.Id != LiveCue.Relay.Edge.RouteId.Speech);

    /// <summary>The target of a row as a request sends it: route 9 gets an id.</summary>
    public static string TargetOf(LiveCue.Relay.Edge.RouteEntry route) =>
        route.Id == LiveCue.Relay.Edge.RouteId.CancelRequest ? route.Target + Bodies.RequestId : route.Target;

    /// <summary>A request head with a Host header and exactly the given headers: no credentials are added.</summary>
    public static string Head(int port, string requestLine, params (string Name, string Value)[] headers) =>
        RawHttp.Head(requestLine, port, headers);

    /// <summary>The headers of a WebSocket upgrade as a client sends them (RFC 6455).</summary>
    public static (string Name, string Value)[] UpgradeHeaders =>
    [
        ("Connection", "Upgrade"),
        ("Upgrade", "websocket"),
        ("Sec-WebSocket-Version", "13"),
        // Any sixteen bytes in base64 are a key; these are sixteen zero bytes.
        ("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16])),
    ];

    /// <summary>
    /// Reads one response from the connection and returns every byte of it, as text in which one
    /// character is one byte. Null when the server closed the connection without answering.
    /// </summary>
    /// <param name="hasBody">False for the answer to a HEAD request: the length is declared and no body follows.</param>
    public static async Task<string?> ReadRawAsync(RawHttp raw, bool hasBody = true, TimeSpan? timeout = null)
    {
        // The connection is read in large pieces, not byte by byte: a read that is already waiting when
        // the answer arrives receives the whole of it, also when the server resets the connection right
        // after (a reset discards what the client has not read yet). Bytes read beyond this response are
        // kept for the next call on the same connection.
        using var limit = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        var bytes = Unread.GetOrCreateValue(raw);
        var piece = new byte[65_536];

        async Task<bool> FillAsync()
        {
            int read;
            try
            {
                read = await raw.Stream.ReadAsync(piece, limit.Token);
            }
            catch (IOException)
            {
                read = 0;
            }

            bytes.AddRange(piece.AsSpan(0, read));
            return read > 0;
        }

        int headEnd;
        while ((headEnd = EndOfHead(bytes)) < 0)
        {
            if (!await FillAsync())
            {
                return bytes.Count == 0 ? null : Take(bytes, bytes.Count);
            }
        }

        var head = Encoding.Latin1.GetString(CollectionsMarshal.AsSpan(bytes)[..headEnd]);
        var length = ContentLengthLine().Match(head) is { Success: true } match && hasBody
            ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : 0;
        while (bytes.Count < headEnd + length && await FillAsync())
        {
        }

        return Take(bytes, Math.Min(bytes.Count, headEnd + length));
    }

    /// <summary>One request on a connection of its own; the raw response with the value of Date replaced.</summary>
    public static async Task<string?> ExchangeAsync(int port, string request, bool hasBody = true)
    {
        using var raw = await RawHttp.ConnectAsync(port);
        await raw.SendAsync(request);
        var response = await ReadRawAsync(raw, hasBody);
        return response is null ? null : WithoutDate(response);
    }

    /// <summary>Replaces the value of the Date header, the one thing that differs with the clock (K-1).</summary>
    public static string WithoutDate(string response) => DateLine().Replace(response, "\r\nDate: -\r\n", 1);

    /// <summary>The names of the response headers, as sent.</summary>
    public static IReadOnlyList<string> HeaderNames(string response) =>
    [
        .. response[..response.IndexOf("\r\n\r\n", StringComparison.Ordinal)]
            .Split("\r\n")
            .Skip(1)
            .Select(line => line[..line.IndexOf(':', StringComparison.Ordinal)]),
    ];

    /// <summary>The status line of a raw response.</summary>
    public static string StatusLine(string response) => response[..response.IndexOf(Break, StringComparison.Ordinal)];

    /// <summary>The header lines of a raw response, sorted, so that their order does not matter.</summary>
    public static IReadOnlyList<string> HeaderLines(string response) =>
    [
        .. response[..response.IndexOf(BlankLine, StringComparison.Ordinal)]
            .Split(Break)
            .Skip(1)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The body of a raw response.</summary>
    public static string BodyOf(string response) =>
        response[(response.IndexOf(BlankLine, StringComparison.Ordinal) + BlankLine.Length)..];

    /// <summary>
    /// Asserts that a raw response (Date replaced) is the fixed 401 of protocol 5.2: the status line, the
    /// headers of protocol 5.3 and nothing else, and the one body.
    /// </summary>
    public static void AssertFixed401(string? response, bool closing = false, bool hasBody = true)
    {
        Assert.NotNull(response);
        Assert.Equal(Fixed401StatusLine, StatusLine(response));
        Assert.Equal(
            closing ? [.. Fixed401Headers.Append("Connection: close").Order(StringComparer.Ordinal)] : Fixed401Headers,
            HeaderLines(response));
        Assert.Equal(hasBody ? Unauthorized : string.Empty, BodyOf(response));
    }

    /// <summary>Asserts a response of status 400 or above as protocol 5.3 defines it, and returns its code.</summary>
    public static async Task<string> AssertErrorAsync(HttpResponseMessage response, int status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(status, (int)response.StatusCode);
        var expected = RelayErrors.ForCode(code);
        Assert.NotNull(expected);
        Assert.Equal(Encoding.UTF8.GetString(RelayErrors.Body(expected.Value).Span), text);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Waits, in real time, until the condition holds; fails the test when it never does.</summary>
    public static async Task UntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "Timed out waiting until " + what + ".");
            await Task.Delay(5);
        }
    }

    /// <summary>The index just after the blank line that ends the head, or -1 when it has not arrived.</summary>
    private static int EndOfHead(List<byte> bytes)
    {
        var index = CollectionsMarshal.AsSpan(bytes).IndexOf("\r\n\r\n"u8);
        return index < 0 ? -1 : index + 4;
    }

    private static string Take(List<byte> bytes, int count)
    {
        var text = Encoding.Latin1.GetString(CollectionsMarshal.AsSpan(bytes)[..count]);
        bytes.RemoveRange(0, count);
        return text;
    }

    [GeneratedRegex(@"\r\nContent-Length: (\d+)\r\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ContentLengthLine();

    [GeneratedRegex(@"\r\nDate: [^\r\n]*\r\n", RegexOptions.CultureInvariant)]
    private static partial Regex DateLine();
}
