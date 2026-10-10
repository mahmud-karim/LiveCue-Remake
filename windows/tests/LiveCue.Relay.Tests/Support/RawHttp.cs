using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LiveCue.Relay.Tests.Support;

/// <summary>What came back on a raw connection: the status line, the headers as sent, and the body.</summary>
internal sealed record RawResponse(int Status, string StatusLine, IReadOnlyList<KeyValuePair<string, string>> Headers, string Body)
{
    /// <summary>The value of a header that appears exactly once; null when it is absent.</summary>
    public string? Header(string name)
    {
        var values = Headers.Where(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return values.Count == 1 ? values[0].Value : null;
    }

    /// <summary>The header names, as sent.</summary>
    public IReadOnlyList<string> HeaderNames => [.. Headers.Select(header => header.Key)];
}

/// <summary>
/// HTTP/1.1 written to a socket by hand, so that the test, and not a client library, decides what is
/// on the wire: malformed, duplicated, tidied-away and slow requests can all be sent.
/// </summary>
internal sealed class RawHttp : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    private RawHttp(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    /// <summary>The underlying stream, for tests that write byte by byte.</summary>
    public NetworkStream Stream => _stream;

    public static async Task<RawHttp> ConnectAsync(int port)
    {
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(IPAddress.Loopback, port);
        return new RawHttp(client);
    }

    /// <summary>
    /// A request head: the request line, a Host header, then the given headers in order (a name may
    /// repeat), then the blank line. Nothing is added or tidied.
    /// </summary>
    public static string Head(string requestLine, int port, params (string Name, string Value)[] headers)
    {
        var text = new StringBuilder();
        text.Append(requestLine).Append("\r\n");
        text.Append(CultureInfo.InvariantCulture, $"Host: 127.0.0.1:{port}\r\n");
        foreach (var (name, value) in headers)
        {
            text.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        return text.Append("\r\n").ToString();
    }

    /// <summary>Writes text as ASCII.</summary>
    public Task SendAsync(string text) => SendAsync(Encoding.ASCII.GetBytes(text));

    public async Task SendAsync(ReadOnlyMemory<byte> bytes)
    {
        await _stream.WriteAsync(bytes);
        await _stream.FlushAsync();
    }

    /// <summary>Half-closes the connection: nothing more will be sent.</summary>
    public void FinishSending() => _client.Client.Shutdown(SocketShutdown.Send);

    /// <summary>
    /// Reads one response: the head, then a body of Content-Length bytes. Returns null when the server
    /// closed the connection without answering.
    /// </summary>
    public async Task<RawResponse?> ReadResponseAsync(TimeSpan? timeout = null)
    {
        using var limit = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        var head = new List<byte>();
        var one = new byte[1];
        while (!EndsWithBlankLine(head))
        {
            if (await _stream.ReadAsync(one, limit.Token) == 0)
            {
                return null;
            }

            head.Add(one[0]);
        }

        var lines = Encoding.ASCII.GetString([.. head]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var headers = lines.Skip(1)
            .Select(line => line.Split(':', 2))
            .Select(parts => KeyValuePair.Create(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : string.Empty))
            .ToList();
        var status = int.Parse(lines[0].Split(' ')[1], CultureInfo.InvariantCulture);

        var length = headers
            .Where(header => string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(header => int.Parse(header.Value, CultureInfo.InvariantCulture))
            .FirstOrDefault();
        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await _stream.ReadAsync(body.AsMemory(read), limit.Token);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return new RawResponse(status, lines[0], headers, Encoding.UTF8.GetString(body, 0, read));
    }

    /// <summary>True when the server has closed the connection (a read returns nothing) within the timeout.</summary>
    public async Task<bool> IsClosedByServerAsync(TimeSpan timeout)
    {
        using var limit = new CancellationTokenSource(timeout);
        try
        {
            return await _stream.ReadAsync(new byte[1], limit.Token) == 0;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
    }

    private static bool EndsWithBlankLine(List<byte> bytes) =>
        bytes.Count >= 4
        && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n';
}
