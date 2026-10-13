using System.Globalization;
using System.Text;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>What the wire tests share: one hostile request on a connection of its own, and what came back.</summary>
internal static class Hostile
{
    /// <summary>
    /// Sends the bytes, reads one response (null when the server closed without one), then sends a
    /// complete, valid health request and reports whether that second request was ever answered.
    /// </summary>
    public static async Task<(string? First, string? Second, bool Closed)> SendAsync(ConformanceRelay relay, byte[] request, bool hasBody = true)
    {
        using var raw = await relay.OpenRawAsync();
        var reading = Wire.ReadRawAsync(raw, hasBody);
        try
        {
            await raw.SendAsync(request);
        }
        catch (IOException)
        {
            // The server stopped reading before everything had been sent.
        }

        var first = await reading;
        try
        {
            await raw.SendAsync(relay.RawHead("GET /v1/health HTTP/1.1"));
        }
        catch (IOException)
        {
            // Closed already.
        }

        var second = first is null ? null : await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(15));
        var closed = second is not null || await raw.IsClosedByServerAsync(TimeSpan.FromSeconds(15));
        return (first, second, closed);
    }

    public static Task<(string? First, string? Second, bool Closed)> SendAsync(ConformanceRelay relay, string request, bool hasBody = true) =>
        SendAsync(relay, Encoding.Latin1.GetBytes(request), hasBody);

    /// <summary>The relay still runs, raised no diagnostic and serves a correct phone.</summary>
    public static async Task AssertUnharmedAsync(ConformanceRelay relay)
    {
        var (status, body) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/health");
        Assert.Equal(200, status);
        Assert.Equal("""{"ok":true,"protocol":2,"relayVersion":"1.0.0"}""", body);
        await relay.Events.SettleAsync();
        Assert.Equal(RelayStatus.Running, relay.Relay.Status);
        Assert.Empty(relay.Events.OfType<Diagnostic>());
        Assert.Empty(relay.Events.OfType<RelayFaulted>());
        await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == 0, "no request is in flight");
    }
}

/// <summary>
/// Test plan S-11, S-12, S-13 and ST-21: what the server core refuses by itself, sent with the accepted
/// token so that anything that got through would be served. The answer is bare (no body, no code) or the
/// connection ends; nothing reaches the pipeline; the listener keeps serving.
/// </summary>
public sealed class ParserLimitTests
{
    [Theory]
    [InlineData("a request line beyond C-19", 414)]
    [InlineData("a request line far beyond C-19", 414)]
    [InlineData("a header block beyond C-19", 431)]
    [InlineData("one header far beyond C-19", 431)]
    [InlineData("more headers than C-19", 431)]
    [InlineData("a thousand headers", 431)]
    public async Task Oversized_request_line_and_headers(string what, int status)
    {
        await using var relay = await ConformanceRelay.StartAsync();
        var request = what switch
        {
            "a request line beyond C-19" => relay.RawHead($"GET /{new string('a', ProtocolConstants.MaxRequestLineBytes)} HTTP/1.1"),
            "a request line far beyond C-19" => relay.RawHead($"GET /v1/health?{new string('a', 100_000)} HTTP/1.1"),
            "a header block beyond C-19" => relay.RawHead("GET /v1/health HTTP/1.1", ("X-Big", new string('a', ProtocolConstants.MaxHeaderBytes))),
            "one header far beyond C-19" => relay.RawHead("GET /v1/health HTTP/1.1", ("Cookie", new string('a', 100_000))),
            "more headers than C-19" => relay.RawHead(
                "GET /v1/health HTTP/1.1",
                [.. Enumerable.Range(0, ProtocolConstants.MaxHeaderCount).Select(index => ("X-H" + index.ToString(CultureInfo.InvariantCulture), "v"))]),
            _ => relay.RawHead(
                "GET /v1/health HTTP/1.1",
                [.. Enumerable.Range(0, 1000).Select(index => ("X-H" + index.ToString(CultureInfo.InvariantCulture), "v"))]),
        };

        // Ten in a row: the limit is per request and leaves nothing behind.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var (first, second, closed) = await Hostile.SendAsync(relay, request);
            Assert.NotNull(first);
            Assert.StartsWith($"HTTP/1.1 {status} ", Wire.StatusLine(first), StringComparison.Ordinal);
            Assert.Equal(string.Empty, Wire.BodyOf(first));
            Assert.Contains("Connection: close", Wire.HeaderLines(first));
            Assert.Equal(["Connection", "Content-Length", "Date"], Wire.HeaderNames(first).Order(StringComparer.Ordinal));
            Assert.Null(second);
            Assert.True(closed);
        }

        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>());
        Assert.Equal(0, relay.Edge.Connections.Open);
        await Hostile.AssertUnharmedAsync(relay);
    }

    public static TheoryData<string, string> Garbage()
    {
        const string Auth = "Authorization: Bearer {TOKEN}\r\nX-LiveCue-Protocol: 2\r\n";
        const string Host = "Host: 127.0.0.1:{PORT}\r\n";
        return new TheoryData<string, string>
        {
            { "control bytes", "\u0001\u0002\u0003\u0004 not a request\r\n\r\n" },
            { "zero bytes", new string('\0', 4096) },
            { "random high bytes", string.Concat(Enumerable.Range(0, 2048).Select(index => (char)(128 + ((index * 37) % 128)))) },
            { "an HTTP/2 preface", "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n\0\0\0\u0004\0\0\0\0\0" },
            { "a TLS hello", "\u0016\u0003\u0001\u0002\0\u0001\0\u0001\u00fc\u0003\u0003" + new string('\u0042', 512) },
            { "an SSH banner", "SSH-2.0-OpenSSH_9.6\r\n" },
            { "a SOCKS greeting", "\u0005\u0001\0" },
            { "an unknown HTTP version", "GET /v1/health HTTP/9.9\r\n" + Host + Auth + "\r\n" },
            { "HTTP/0.9", "GET /v1/health\r\n\r\n" },
            { "a method with a space", "G ET /v1/health HTTP/1.1\r\n" + Host + Auth + "\r\n" },
            { "a target with a space", "GET /v1/ health HTTP/1.1\r\n" + Host + Auth + "\r\n" },
            { "a target with a NUL", "GET /v1/health\0 HTTP/1.1\r\n" + Host + Auth + "\r\n" },
            { "a target that is not a path", "GET v1/health HTTP/1.1\r\n" + Host + Auth + "\r\n" },
            { "no Host header", "GET /v1/health HTTP/1.1\r\n" + Auth + "\r\n" },
            { "two Host headers", "GET /v1/health HTTP/1.1\r\n" + Host + Host + Auth + "\r\n" },
            { "an absolute target for another host", "GET http://pc.example.test/v1/health HTTP/1.1\r\n" + Host + Auth + "\r\n" },
            { "a header name with a space before the colon", "GET /v1/health HTTP/1.1\r\n" + Host + "Authorization : Bearer {TOKEN}\r\nX-LiveCue-Protocol: 2\r\n\r\n" },
            { "a folded header", "GET /v1/health HTTP/1.1\r\n" + Host + "Authorization: Bearer\r\n {TOKEN}\r\nX-LiveCue-Protocol: 2\r\n\r\n" },
            { "a header without a colon", "GET /v1/health HTTP/1.1\r\n" + Host + Auth + "NoColonHere\r\n\r\n" },
            { "a header value with a NUL", "GET /v1/health HTTP/1.1\r\n" + Host + Auth + "X-Note: a\0b\r\n\r\n" },
            { "a header value with a bare carriage return", "GET /v1/health HTTP/1.1\r\n" + Host + Auth + "X-Note: a\rb\r\n\r\n" },
            { "an empty header name", "GET /v1/health HTTP/1.1\r\n" + Host + Auth + ": value\r\n\r\n" },
        };
    }

    [Theory]
    [MemberData(nameof(Garbage))]
    public async Task Garbage_is_rejected_by_the_server_core(string what, string bytes)
    {
        await using var relay = await ConformanceRelay.StartAsync();
        var request = bytes
            .Replace("{TOKEN}", relay.Token, StringComparison.Ordinal)
            .Replace("{PORT}", relay.Port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var (first, second, closed) = await Hostile.SendAsync(relay, request);

        // A bare answer of the server core, or no HTTP/1.1 answer at all; then the connection ends.
        if (first is not null && first.StartsWith("HTTP/1.1 ", StringComparison.Ordinal))
        {
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(first, "^HTTP/1.1 (400|414|431|505) "), what + ": " + first);
            Assert.Equal(string.Empty, Wire.BodyOf(first));
            Assert.Contains("Connection: close", Wire.HeaderLines(first));
        }
        else
        {
            // The HTTP/2 preface is answered with the frame that refuses it, never with a response.
            Assert.True(first is null || what == "an HTTP/2 preface", $"Unexpected bytes for {what}: {first}");
        }

        Assert.True(second is null || !second.StartsWith("HTTP/1.1 200", StringComparison.Ordinal), "A request after garbage was served.");
        Assert.True(closed, "The connection stayed open after " + what + ".");
        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>());
        Assert.Equal((0L, 0L, 0L), (relay.Edge.Malformed, relay.Edge.TokenNotAccepted, relay.Edge.FunnelInPrivateMode));
        await Hostile.AssertUnharmedAsync(relay);
    }

    [Theory]
    [InlineData("Content-Length: abc")]
    [InlineData("Content-Length: -1")]
    [InlineData("Content-Length: 2.0")]
    [InlineData("Content-Length: 0x2")]
    [InlineData("Content-Length: 2 2")]
    [InlineData("Content-Length: 2, 2")]
    [InlineData("Content-Length: 99999999999999999999999999")]
    [InlineData("Content-Length: 2\r\nContent-Length: 3")]
    [InlineData("Content-Length: 3\r\nContent-Length: 2")]
    [InlineData("Content-Length: 2\r\nContent-Length: 2")]
    [InlineData("Content-Length:")]
    [InlineData("Transfer-Encoding: gzip")]
    [InlineData("Transfer-Encoding: chunked, gzip")]
    [InlineData("Transfer-Encoding: xchunked")]
    [InlineData("Transfer-Encoding: identity")]
    public async Task Bad_framing_is_400(string framing)
    {
        // S-13. The body that follows is a complete second request: however the first is framed, the
        // second must never be answered.
        await using var relay = await ConformanceRelay.StartAsync();
        var hidden = relay.RawPost("/v1/local-model/stop", Bodies.LocalModel("nemotron"));
        var request =
            relay.RawHead("POST /v1/pair/verify HTTP/1.1").Replace("\r\n\r\n", "\r\n" + framing + "\r\n\r\n", StringComparison.Ordinal)
            + "{}" + hidden;

        var (first, second, closed) = await Hostile.SendAsync(relay, request);

        Assert.NotNull(first);
        Assert.StartsWith("HTTP/1.1 400 ", Wire.StatusLine(first), StringComparison.Ordinal);
        Assert.Contains("Connection: close", Wire.HeaderLines(first));
        Assert.Null(second);
        Assert.True(closed);
        Assert.Equal(0, relay.Lab.StopCalls + relay.Lab.ProbeCalls);
        await Hostile.AssertUnharmedAsync(relay);
    }

    [Theory]
    [InlineData("+2", 2)]
    [InlineData("+0", 0)]
    [InlineData("-0", 0)]
    [InlineData("002", 2)]
    [InlineData("  2  ", 2)]
    public async Task A_content_length_the_server_core_reads_leniently_is_framed_by_its_number_and_nothing_else(string text, int length)
    {
        // A finding of this sweep, recorded as it is (see the coverage notes: K-13). The server core
        // accepts a sign in front of Content-Length, which the HTTP grammar (digits only) does not allow,
        // and hands the pipeline only the number, so relay code cannot refuse it. What the test pins down
        // is that the reading is at least consistent: the request is framed by exactly that number, and
        // the bytes after it are the next request, never part of a body and never lost. A proxy that
        // forwarded such a header unchanged and framed it differently could be desynchronised; the proxy
        // the relay runs behind parses and rewrites every request and refuses this header itself. Should
        // a later server core refuse it (a bare 400), that is the better outcome and passes as well.
        await using var relay = await ConformanceRelay.StartAsync();
        var body = length == 0 ? string.Empty : "{}";
        var request =
            relay.RawHead("POST /v1/pair/verify HTTP/1.1").Replace("\r\n\r\n", "\r\nContent-Length: " + text + "\r\n\r\n", StringComparison.Ordinal)
            + body;

        var (first, second, _) = await Hostile.SendAsync(relay, request);

        Assert.NotNull(first);
        if (Wire.StatusLine(first).StartsWith("HTTP/1.1 400 ", StringComparison.Ordinal) && Wire.BodyOf(first).Length == 0)
        {
            Assert.Null(second);
        }
        else
        {
            Assert.Equal(length == 0 ? Wire.InvalidJson : """{"ok":true}""", Wire.BodyOf(first));
            Assert.NotNull(second);
            Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(second));
        }

        await Hostile.AssertUnharmedAsync(relay);
    }

    [Theory]
    [InlineData("Content-Length: 4\r\nTransfer-Encoding: chunked")]
    [InlineData("Transfer-Encoding: chunked\r\nContent-Length: 4")]
    [InlineData("Content-Length: 4\r\nTransfer-Encoding: Chunked")]
    [InlineData("Content-Length: 4\r\ntransfer-encoding: chunked")]
    [InlineData("Content-Length: 0\r\nTransfer-Encoding: chunked")]
    [InlineData("Content-Length: 4\r\nContent-Length: 4\r\nTransfer-Encoding: chunked")]
    public async Task Both_length_headers_are_never_framed_by_content_length(string framing)
    {
        // ST-21 (W-design K-9). Either a bare 400, or the request is read as chunked and the connection
        // is closed after its response. What a proxy that framed by the length would take for the next
        // request (here a command that stops the PC model) is never answered and never runs.
        await using var relay = await ConformanceRelay.StartAsync();
        relay.Lab.LoadedModel = "nemotron";
        var head = relay.RawHead("POST /v1/pair/verify HTTP/1.1").Replace("\r\n\r\n", "\r\n" + framing + "\r\n\r\n", StringComparison.Ordinal);
        var smuggled = relay.RawPost("/v1/local-model/stop", Bodies.LocalModel("nemotron"));

        // Honest chunked framing.
        var (first, second, closed) = await Hostile.SendAsync(relay, head + "2\r\n{}\r\n0\r\n\r\n" + smuggled);
        Assert.NotNull(first);
        if (Wire.StatusLine(first).StartsWith("HTTP/1.1 200", StringComparison.Ordinal))
        {
            Assert.Equal("""{"ok":true}""", Wire.BodyOf(first));
        }
        else
        {
            Assert.StartsWith("HTTP/1.1 400 ", Wire.StatusLine(first), StringComparison.Ordinal);
        }

        Assert.Contains("Connection: close", Wire.HeaderLines(first));
        Assert.Null(second);
        Assert.True(closed);

        // Framing that only makes sense by the length: four bytes of JSON, then the smuggled request.
        (first, second, closed) = await Hostile.SendAsync(relay, head + "{  }" + smuggled);
        Assert.NotNull(first);
        Assert.StartsWith("HTTP/1.1 400 ", Wire.StatusLine(first), StringComparison.Ordinal);
        Assert.Contains("Connection: close", Wire.HeaderLines(first));
        Assert.Null(second);
        Assert.True(closed);

        // The stop command never ran, and the model is still loaded.
        Assert.Equal(0, relay.Lab.StopCalls);
        Assert.Equal("nemotron", relay.Lab.LoadedModel);
        await Hostile.AssertUnharmedAsync(relay);
    }

    [Theory]
    [InlineData("zz\r\n{}\r\n0\r\n\r\n")]
    [InlineData("-2\r\n{}\r\n0\r\n\r\n")]
    [InlineData("2;" + "x=y" + "\r\n{}\r\n0\r\n\r\n")]
    [InlineData("ffffffffffffffffffff\r\n{}\r\n0\r\n\r\n")]
    [InlineData("2\r\n{}X\r\n0\r\n\r\n")]
    [InlineData("2\n{}\n0\n\n")]
    [InlineData("0\r\nTrailer: " + "x" + "\r\n\r\n")]
    public async Task A_broken_chunked_body_is_never_a_second_request(string body)
    {
        // The chunk framing a proxy and the relay might read differently: a bad size, an extension, a
        // size that overflows, a missing line break, bare line feeds, a trailer. The answer may be the
        // served request (where the framing is legal), a coded or a bare 400, 408 or 413; what follows on
        // the connection is never taken for a request unless the first was served whole.
        await using var relay = await ConformanceRelay.StartAsync();
        relay.Lab.LoadedModel = "nemotron";
        var head = relay.RawHead("POST /v1/pair/verify HTTP/1.1", ("Transfer-Encoding", "chunked"));
        var smuggled = relay.RawPost("/v1/local-model/stop", Bodies.LocalModel("nemotron"));

        using var raw = await relay.OpenRawAsync();
        var reading = Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(20));
        await raw.SendAsync(head + body);
        var first = await reading;

        if (first is not null && Wire.StatusLine(first).StartsWith("HTTP/1.1 200", StringComparison.Ordinal))
        {
            // Legal framing (an extension, an empty body with a trailer is not legal JSON and is 400).
            Assert.Equal("""{"ok":true}""", Wire.BodyOf(first));
        }
        else if (first is not null)
        {
            Assert.Matches("^HTTP/1.1 (400|408|413) ", Wire.StatusLine(first));
            var served = first.Contains("\"code\":\"INVALID_JSON\"", StringComparison.Ordinal);
            if (!served)
            {
                // The body was not read to its end: the connection ends here.
                Assert.Contains("Connection: close", Wire.HeaderLines(first));
                try
                {
                    await raw.SendAsync(smuggled);
                }
                catch (IOException)
                {
                }

                Assert.Null(await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(15)));
            }
        }

        Assert.Equal(0, relay.Lab.StopCalls);
        await Hostile.AssertUnharmedAsync(relay);
    }
}

/// <summary>Test plan S-14 and S-15: bodies that lie about their size, and JSON written to hurt a parser.</summary>
public sealed class BodySecurityTests
{
    public static TheoryData<string> BodyRoutes() =>
        [.. Wire.HttpRoutes.Where(route => route.ReadsBody).Select(route => route.Target)];

    [Theory]
    [MemberData(nameof(BodyRoutes))]
    public async Task A_lying_length_cannot_exceed_the_cap(string target)
    {
        await using var relay = await ConformanceRelay.StartAsync();
        var over = ProtocolConstants.MaxRequestBodyBytes + 1;

        // A truthful length one byte over: refused from the header alone, before a byte of body is sent.
        using (var truthful = await relay.OpenRawAsync())
        {
            await truthful.SendAsync(relay.RawHead($"POST {target} HTTP/1.1", ("Content-Length", over.ToString(CultureInfo.InvariantCulture))));
            var refused = await Wire.ReadRawAsync(truthful);
            Assert.Equal("HTTP/1.1 413 Payload Too Large", Wire.StatusLine(refused!));
            Assert.Equal(Wire.PayloadTooLarge, Wire.BodyOf(refused!));
            Assert.Contains("Connection: close", Wire.HeaderLines(refused!));
        }

        // A length that claims far more than any body could be: the same, at once.
        using (var absurd = await relay.OpenRawAsync())
        {
            await absurd.SendAsync(relay.RawHead($"POST {target} HTTP/1.1", ("Content-Length", "9223372036854775807")));
            var refused = await Wire.ReadRawAsync(absurd);
            Assert.Equal("HTTP/1.1 413 Payload Too Large", Wire.StatusLine(refused!));
        }

        // A false small length with megabytes behind it: the body is the declared bytes and no more.
        // What follows is not a request, so the connection ends; it is never read as a body.
        // (Sent in two steps: a relay that closes on what follows may reset the connection, and a reset
        // can take an answer that was still on its way with it.)
        using (var lying = await relay.OpenRawAsync())
        {
            await lying.SendAsync(relay.RawHead($"POST {target} HTTP/1.1", ("Content-Length", "2")) + "{}");
            var first = await Wire.ReadRawAsync(lying);
            Assert.NotNull(first);
            Assert.DoesNotMatch("^HTTP/1.1 (413|500) ", Wire.StatusLine(first));

            var reading = Wire.ReadRawAsync(lying, timeout: TimeSpan.FromSeconds(15));
            try
            {
                await lying.SendAsync(new string('x', 4 * 1024 * 1024));
            }
            catch (IOException)
            {
                // The relay stopped reading.
            }

            var second = await reading;
            Assert.True(second is null || System.Text.RegularExpressions.Regex.IsMatch(second, "^HTTP/1.1 4(00|14) "), second);
            Assert.True(second is null || Wire.BodyOf(second).Length == 0);
            Assert.True(await lying.IsClosedByServerAsync(TimeSpan.FromSeconds(15)));
        }

        // Chunked, with no length at all: the relay answers 413 once the count passes C-10, while the
        // caller is still sending. It never waits for, or holds, what the caller would go on to send.
        using (var endless = await relay.OpenRawAsync())
        {
            var reading = Wire.ReadRawAsync(endless, timeout: TimeSpan.FromSeconds(30));
            await endless.SendAsync(relay.RawHead($"POST {target} HTTP/1.1", ("Transfer-Encoding", "chunked")));
            var chunk = Encoding.ASCII.GetBytes("4000\r\n" + new string('y', 0x4000) + "\r\n");
            long sent = 0;
            try
            {
                while (!reading.IsCompleted && sent < 256L * 1024 * 1024)
                {
                    await endless.SendAsync(chunk);
                    sent += chunk.Length;
                }
            }
            catch (IOException)
            {
                // The relay closed the connection under the flood.
            }

            var refused = await reading;
            Assert.True(sent < 64L * 1024 * 1024, $"The relay let {sent} bytes be sent before it answered.");
            if (refused is not null)
            {
                Assert.Equal("HTTP/1.1 413 Payload Too Large", Wire.StatusLine(refused));
                Assert.Equal(Wire.PayloadTooLarge, Wire.BodyOf(refused));
            }
        }

        // Nothing was started for any of it.
        Assert.Empty(relay.Codex.ExecRequests);
        Assert.Equal(0, relay.Lab.StartCalls + relay.Lab.StopCalls);
        await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == 0, "the gauge is empty");
        await Hostile.AssertUnharmedAsync(relay);
    }

    [Fact]
    public async Task A_hundred_thousand_members_do_not_fit_a_body()
    {
        // S-15 names 100,000 members. The shortest such object is half a megabyte, four times C-10: it is
        // refused by its size, on the header alone, and never reaches a parser. (What does fit, some
        // twenty-six thousand members, is among the cases below.)
        await using var relay = await ConformanceRelay.StartAsync();
        var body = "{" + string.Join(',', Enumerable.Repeat("\"\":0", 100_000)) + "}";
        Assert.True(body.Length > 3 * ProtocolConstants.MaxRequestBodyBytes);

        foreach (var route in Wire.HttpRoutes.Where(route => route.ReadsBody))
        {
            using var raw = await relay.OpenRawAsync();
            var reading = Wire.ReadRawAsync(raw);
            await raw.SendAsync(relay.RawHead($"POST {route.Target} HTTP/1.1", ("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture))));
            var refused = await reading;
            Assert.Equal("HTTP/1.1 413 Payload Too Large", Wire.StatusLine(refused!));
            Assert.Equal(Wire.PayloadTooLarge, Wire.BodyOf(refused!));
        }

        await Hostile.AssertUnharmedAsync(relay);
    }

    public static TheoryData<string, byte[], bool?> HostileJson()
    {
        static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
        var limit = ProtocolConstants.MaxJsonDepth;
        var cases = new TheoryData<string, byte[], bool?>
        {
            // Name, body, "is valid JSON for the body step" (null: the route decides).
            { "nested one deeper than the limit", Utf8(Nested(limit + 1)), false },
            { "nested to the limit", Utf8(Nested(limit)), true },
            { "ten thousand levels of arrays", Utf8("{\"a\":" + new string('[', 10_000) + new string(']', 10_000) + "}"), false },
            { "sixty thousand open brackets and nothing else", Utf8(new string('[', 60_000)), false },
            { "twenty-six thousand members of one empty name", Utf8("{" + string.Join(',', Enumerable.Repeat("\"\":0", 26_000)) + "}"), true },
            { "twelve thousand different members", Utf8("{" + string.Join(',', Enumerable.Range(0, 12_000).Select(index => "\"" + index.ToString(CultureInfo.InvariantCulture) + "\":0")) + "}"), true },
            { "a thousand duplicate members", Utf8("{" + string.Join(',', Enumerable.Repeat("\"requestId\":\"a\"", 1000)) + "}"), true },
            { "invalid UTF-8 in a string", [.. Utf8("{\"a\":\""), 0xC3, 0x28, .. Utf8("\"}")], false },
            { "an overlong encoding", [.. Utf8("{\"a\":\""), 0xC0, 0xAF, .. Utf8("\"}")], false },
            { "an encoded surrogate", [.. Utf8("{\"a\":\""), 0xED, 0xA0, 0x80, .. Utf8("\"}")], false },
            { "invalid UTF-8 in a member name", [.. Utf8("{\""), 0xFF, .. Utf8("\":1}")], false },
            { "a byte-order mark", [0xEF, 0xBB, 0xBF, .. Utf8("{}")], false },
            { "UTF-16", Encoding.Unicode.GetBytes("{}"), false },
            { "trailing bytes after the object", Utf8("{} x"), false },
            { "a second object", Utf8("{}{}"), false },
            { "a trailing NUL", [.. Utf8("{}"), 0], false },
            { "trailing white space", Utf8("{} \r\n\t"), true },
            { "a comment", Utf8("{/* x */}"), false },
            { "a trailing comma", Utf8("{\"a\":1,}"), false },
            { "single quotes", Utf8("{'a':1}"), false },
            { "an unquoted name", Utf8("{a:1}"), false },
            { "NaN", Utf8("{\"a\":NaN}"), false },
            { "a number beyond any range", Utf8("{\"a\":1e999999999}"), true },
            { "a number of sixty thousand digits", Utf8("{\"a\":" + new string('9', 60_000) + "}"), true },
            { "an unterminated string", Utf8("{\"a\":\"" + new string('x', 60_000)), false },
            { "a raw control character in a string", Utf8("{\"a\":\"\u0001\"}"), false },
            // Valid JSON that is not text: route 2 reads no member and serves it; the answer routes cannot
            // write it again as one line and refuse it as invalid JSON. Never an internal error.
            { "half a surrogate pair as an escape", Utf8("{\"a\":\"\\ud83d\"}"), null },
            { "a member name of a hundred thousand characters", Utf8("{\"" + new string('n', 100_000) + "\":1}"), true },
            { "an array at the top", Utf8("[{}]"), false },
            { "a string at the top", Utf8("\"{}\""), false },
            { "only white space", Utf8("   "), false },
        };
        return cases;

        static string Nested(int depth) =>
            string.Concat(Enumerable.Repeat("{\"a\":", depth - 1)) + "{}" + new string('}', depth - 1);
    }

    [Theory]
    [MemberData(nameof(HostileJson))]
    public async Task Hostile_json_cases(string what, byte[] body, bool? valid)
    {
        // S-15, on every route that reads a body. A body the JSON rule refuses is 400 INVALID_JSON; one it
        // accepts goes on to the route's own checks. Never a 500, never unbounded work, never a process.
        await using var relay = await ConformanceRelay.StartAsync();
        Assert.True(body.Length <= ProtocolConstants.MaxRequestBodyBytes, what + " does not fit a body at all.");

        foreach (var route in Wire.HttpRoutes.Where(route => route.ReadsBody))
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            using var request = relay.Request(HttpMethod.Post, route.Target);
            request.Content = new ByteArrayContent(body);
            var (status, text) = await relay.ExchangeAsync(request);
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"{what} on {route.Target} took {started.Elapsed}.");

            if (valid == false)
            {
                Assert.Equal(400, status);
                Assert.Equal(Wire.InvalidJson, text);
            }
            else
            {
                Assert.True(valid is null || text != Wire.InvalidJson, $"{what} on {route.Target} was refused as invalid JSON.");
                Assert.True(
                    status == 200 || (status == 400 && !text.Contains("INTERNAL", StringComparison.Ordinal)),
                    $"{what} on {route.Target}: {status} {text}");
            }
        }

        Assert.Empty(relay.Codex.ExecRequests);
        Assert.Equal(0, relay.Lab.StartCalls + relay.Lab.StopCalls);
        await Hostile.AssertUnharmedAsync(relay);
    }
}

/// <summary>
/// Test plan ST-20, the two cases that need the real answer area: RELAY_PAUSED on routes 7 and 8 and
/// ASSISTANT_BUSY are written before the body is read, so each must close the connection. The client
/// declares N bytes, sends half, then a complete second request; exactly one response arrives.
/// </summary>
public sealed class UnreadBodyTests
{
    [Theory]
    [InlineData("RELAY_PAUSED", "/v1/assist", 503)]
    [InlineData("RELAY_PAUSED", "/v1/session-summary", 503)]
    [InlineData("ASSISTANT_BUSY", "/v1/assist", 429)]
    [InlineData("ASSISTANT_BUSY", "/v1/session-summary", 429)]
    public async Task An_answer_before_the_body_closes_the_connection(string code, string target, int status)
    {
        await using var relay = await ConformanceRelay.StartAsync();
        Task<(int Status, string Body)>? holder = null;
        if (code == "RELAY_PAUSED")
        {
            await relay.Relay.SetAcceptingAsync(false);
        }
        else
        {
            relay.Codex.ExecScenario = "hang";
            holder = relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody("request-0005"));
            await relay.Codex.ReadExecReportAsync();
        }

        await relay.Events.SettleAsync();
        var seenBefore = relay.Events.Count<PhoneSeen>();

        // The second request is a complete, valid cancel of the run that holds the slot: on a kept
        // connection it would be answered, and it would end that run.
        var second = relay.RawHead("DELETE /v1/requests/request-0005 HTTP/1.1");
        const int Declared = 128;
        Assert.True(second.Length > Declared / 2);

        using var raw = await relay.OpenRawAsync();
        var reading = Wire.ReadRawAsync(raw);
        await raw.SendAsync(
            relay.RawHead($"POST {target} HTTP/1.1", ("Content-Length", Declared.ToString(CultureInfo.InvariantCulture)))
            + new string('x', Declared / 2));
        var first = await reading;
        try
        {
            await raw.SendAsync(second);
        }
        catch (IOException)
        {
            // The server has closed already.
        }

        var afterwards = await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(15));
        var closed = await raw.IsClosedByServerAsync(TimeSpan.FromSeconds(15));
        await relay.Events.SettleAsync();

        Assert.NotNull(first);
        Assert.StartsWith($"HTTP/1.1 {status} ", Wire.StatusLine(first), StringComparison.Ordinal);
        Assert.Contains($"\"code\":\"{code}\"", Wire.BodyOf(first), StringComparison.Ordinal);
        Assert.Contains("Connection: close", Wire.HeaderLines(first));
        Assert.Null(afterwards);
        Assert.True(closed);
        Assert.Equal(seenBefore + 1, relay.Events.Count<PhoneSeen>());

        if (holder is not null)
        {
            // The run that held the slot was not cancelled by the bytes that followed.
            Assert.False(holder.IsCompleted);
            using var cancel = await relay.DeleteAsync("/v1/requests/request-0005");
            Assert.Equal(200, (int)cancel.StatusCode);
            Assert.Equal(409, (await holder.WaitAsync(TimeSpan.FromSeconds(30))).Status);
        }
    }
}
