using System.Globalization;
using System.Net;
using System.Text;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// The notes K-1 to K-10 of W-design 4.3: what the server core (Kestrel) does by itself, proved with
/// requests written to the socket by hand. The notes that need a real timer of the server core are in
/// <c>KestrelTimerTests.cs</c>; K-3 lives in the speech socket and is proved with it; K-4 is
/// <see cref="UnreadBodyTests"/>.
/// </summary>
public sealed class KestrelNotesTests : IAsyncLifetime
{
    private RelayTestHost _host = null!;

    public async Task InitializeAsync() => _host = await RelayTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task K1_every_response_carries_date_and_two_refusals_differ_in_nothing_else()
    {
        using var first = await _host.OpenRawAsync();
        await first.SendAsync(Wire.Head(_host.Port, "GET /v1/health HTTP/1.1"));
        var noToken = await Wire.ReadRawAsync(first);
        using var second = await _host.OpenRawAsync();
        await second.SendAsync(_host.RawHead("GET /v1/health HTTP/1.1", ("Origin", "https://bad.test")));
        var browser = await Wire.ReadRawAsync(second);
        using var third = await _host.OpenRawAsync();
        await third.SendAsync(_host.RawHead("GET /v1/health HTTP/1.1"));
        var served = await Wire.ReadRawAsync(third);

        // The server core adds Date to every response, and nothing else to these.
        foreach (var response in (string?[])[noToken, browser, served])
        {
            var date = Assert.Single(Wire.HeaderLines(response!), line => line.StartsWith("Date: ", StringComparison.Ordinal));
            Assert.True(DateTimeOffset.TryParseExact(
                date["Date: ".Length..], "r", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
            Assert.All(Wire.HeaderNames(response!), name => Assert.Contains(name, Wire.AllowedResponseHeaders));
        }

        // Date follows the real clock, never the step that refused: apart from it the two 401s are equal.
        Assert.Equal(Wire.WithoutDate(noToken!), Wire.WithoutDate(browser!));
    }

    [Theory]
    [InlineData("a request line beyond C-19", 414)]
    [InlineData("a header block beyond C-19", 431)]
    [InlineData("more headers than C-19", 431)]
    [InlineData("a request line that is not HTTP", 400)]
    [InlineData("a Content-Length that is not a number", 400)]
    [InlineData("a negative Content-Length", 400)]
    [InlineData("two different Content-Length headers", 400)]
    [InlineData("a transfer coding that is not chunked", 400)]
    [InlineData("no Host header", 400)]
    [InlineData("an absolute target for another host", 400)]
    public async Task K6_what_breaks_the_parser_limits_never_reaches_the_pipeline(string what, int status)
    {
        // Every one of these carries the accepted token: had it reached the edge, it would have been a
        // phone. The server core answers by itself, with a bare status and no body.
        var request = what switch
        {
            "a request line beyond C-19" => _host.RawHead($"GET /{new string('a', ProtocolConstants.MaxRequestLineBytes)} HTTP/1.1"),
            "a header block beyond C-19" => _host.RawHead("GET /v1/health HTTP/1.1", ("X-Big", new string('a', ProtocolConstants.MaxHeaderBytes))),
            "more headers than C-19" => _host.RawHead(
                "GET /v1/health HTTP/1.1",
                [.. Enumerable.Range(0, ProtocolConstants.MaxHeaderCount).Select(index => ("X-H" + index.ToString(CultureInfo.InvariantCulture), "v"))]),
            "a request line that is not HTTP" => "\u0001\u0002 not a request\r\n\r\n",
            "a Content-Length that is not a number" => _host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "abc")),
            "a negative Content-Length" => _host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "-1")),
            "two different Content-Length headers" =>
                _host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "2"), ("Content-Length", "3")) + "{}",
            "a transfer coding that is not chunked" =>
                _host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Transfer-Encoding", "gzip")) + "{}",
            "no Host header" =>
                $"GET /v1/health HTTP/1.1\r\nAuthorization: {TestTokens.Bearer(_host.Token)}\r\nX-LiveCue-Protocol: 2\r\n\r\n",
            _ => _host.RawHead("GET http://pc.example.test/v1/health HTTP/1.1"),
        };

        using var raw = await _host.OpenRawAsync();
        var reading = Wire.ReadRawAsync(raw);
        try
        {
            await raw.SendAsync(request);
        }
        catch (IOException)
        {
            // The server core stopped reading before the whole request had been sent.
        }

        var response = await reading;
        var closed = await raw.IsClosedByServerAsync(TimeSpan.FromSeconds(10));
        await _host.Events.SettleAsync();

        Assert.NotNull(response);
        Assert.StartsWith($"HTTP/1.1 {status} ", Wire.StatusLine(response), StringComparison.Ordinal);
        Assert.Equal(string.Empty, Wire.BodyOf(response));
        Assert.Contains("Connection: close", Wire.HeaderLines(response));
        Assert.Contains("Content-Length: 0", Wire.HeaderLines(response));
        Assert.Equal(["Connection", "Content-Length", "Date"], Wire.HeaderNames(response).Order(StringComparer.Ordinal));
        Assert.True(closed);

        // The pipeline never saw it: no presence, no counter. And the listener keeps serving.
        Assert.IsType<RelayStarted>(Assert.Single(_host.Events.Snapshot()));
        var edge = ((LiveCue.Relay.Core.RelayHost)_host.Relay).Edge!;
        Assert.Equal((0L, 0L, 0L), (edge.Malformed, edge.TokenNotAccepted, edge.FunnelInPrivateMode));
        using var served = await _host.GetAsync("/v1/health");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    [Fact]
    public async Task K6_exactly_the_limits_of_C19_are_served()
    {
        // A request line of exactly 2,048 bytes with its line break, and 64 headers in all.
        var line = "GET /" + new string('a', ProtocolConstants.MaxRequestLineBytes - "GET / HTTP/1.1\r\n".Length) + " HTTP/1.1";
        Assert.Equal(ProtocolConstants.MaxRequestLineBytes, line.Length + 2);
        using var longest = await _host.OpenRawAsync();
        await longest.SendAsync(_host.RawHead(line));
        var atLineLimit = await Wire.ReadRawAsync(longest);

        // Host, Authorization and the protocol header are three of the 64.
        using var most = await _host.OpenRawAsync();
        await most.SendAsync(_host.RawHead(
            "GET /v1/health HTTP/1.1",
            [.. Enumerable.Range(0, ProtocolConstants.MaxHeaderCount - 3).Select(index => ("X-H" + index.ToString(CultureInfo.InvariantCulture), "v"))]));
        var atCountLimit = await Wire.ReadRawAsync(most);

        Assert.Equal("HTTP/1.1 404 Not Found", Wire.StatusLine(atLineLimit!));
        Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(atCountLimit!));
    }

    [Fact]
    public async Task K7_the_target_is_compared_as_it_was_sent()
    {
        // The decoded path of each of these is /v1/health; the text on the request line is not.
        foreach (var target in (string[])["/v1/%68ealth", "/v1/health%2F", "/v1//health", "/v1/x/../health", "/%76%31/health"])
        {
            using var raw = await _host.OpenRawAsync();
            await raw.SendAsync(_host.RawHead($"GET {target} HTTP/1.1"));
            var response = await raw.ReadResponseAsync();

            Assert.Equal(404, response!.Status);
            Assert.Equal(Wire.NotFound, response.Body);
        }
    }

    [Theory]
    [InlineData("Origin")]
    [InlineData("Sec-Fetch-Site")]
    public async Task K8_a_header_with_an_empty_value_is_still_carried(string header)
    {
        var reference = await Wire.ExchangeAsync(_host.Port, Wire.Head(_host.Port, "GET /v1/health HTTP/1.1"));

        // Written as "Origin:" and as "Origin: " with nothing after it.
        var bare = await Wire.ExchangeAsync(
            _host.Port,
            _host.RawHead("GET /v1/health HTTP/1.1").Replace("\r\n\r\n", $"\r\n{header}:\r\n\r\n", StringComparison.Ordinal));
        var spaced = await Wire.ExchangeAsync(_host.Port, _host.RawHead("GET /v1/health HTTP/1.1", (header, string.Empty)));

        Assert.Equal(reference, bare);
        Assert.Equal(reference, spaced);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task K9_both_length_headers_are_never_framed_by_content_length(bool lengthFirst)
    {
        (string, string) length = ("Content-Length", "4");
        (string, string) chunked = ("Transfer-Encoding", "chunked");
        var head = _host.RawHead("POST /v1/pair/verify HTTP/1.1", lengthFirst ? [length, chunked] : [chunked, length]);
        var second = _host.RawHead("GET /v1/health HTTP/1.1");

        // Framed as chunked, the body is {} and the request is served; the connection is then closed, so
        // that nothing can follow on it. Framed by the length it would have been the four bytes "2\r\n{".
        using var honest = await _host.OpenRawAsync();
        var reading = Wire.ReadRawAsync(honest);
        await honest.SendAsync(head + "2\r\n{}\r\n0\r\n\r\n");
        var first = await reading;
        try
        {
            await honest.SendAsync(second);
        }
        catch (IOException)
        {
            // Closed already.
        }

        var afterwards = await Wire.ReadRawAsync(honest, timeout: TimeSpan.FromSeconds(10));

        // A body that only makes sense by the length (four bytes of JSON, then a second request) is not
        // read by the length: the bytes are not a chunk, the request is refused, and the hidden second
        // request is never answered.
        using var smuggling = await _host.OpenRawAsync();
        var smuggled = Wire.ReadRawAsync(smuggling);
        await smuggling.SendAsync(head + "{  }" + second);
        var refused = await smuggled;
        var hidden = await Wire.ReadRawAsync(smuggling, timeout: TimeSpan.FromSeconds(10));
        await _host.Events.SettleAsync();

        Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(first!));
        Assert.Equal("""{"ok":true}""", Wire.BodyOf(first!));
        Assert.Contains("Connection: close", Wire.HeaderLines(first!));
        Assert.Null(afterwards);

        Assert.StartsWith("HTTP/1.1 400 ", Wire.StatusLine(refused!), StringComparison.Ordinal);
        Assert.Contains("Connection: close", Wire.HeaderLines(refused!));
        Assert.Null(hidden);

        // Two requests reached the edge, one per connection, and no third.
        Assert.Equal(2, _host.Events.Count<PhoneSeen>());
    }

    [Fact]
    public async Task K10_the_server_cores_own_body_count_includes_the_chunk_framing()
    {
        // 131,072 bytes of body is within C-10. Sent in chunks, the server core's count (the second line
        // behind the relay's own) has passed C-10 before the last chunk, because it counts the framing.
        var body = Bodies.ObjectOfSize(ProtocolConstants.MaxRequestBodyBytes);
        var chunks = new StringBuilder();
        for (var offset = 0; offset < body.Length; offset += 16_384)
        {
            chunks.Append("4000\r\n").Append(body.AsSpan(offset, 16_384)).Append("\r\n");
        }

        chunks.Append("0\r\n\r\n");
        using var raw = await _host.OpenRawAsync();
        var reading = Wire.ReadRawAsync(raw);
        try
        {
            await raw.SendAsync(_host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Transfer-Encoding", "chunked")) + chunks);
        }
        catch (IOException)
        {
            // Closed while the rest was on its way.
        }

        var response = await reading;

        // Still the coded answer of the relay, and never more than the limit held.
        Assert.Equal("HTTP/1.1 413 Payload Too Large", Wire.StatusLine(response!));
        Assert.Equal(Wire.PayloadTooLarge, Wire.BodyOf(response!));
    }

    [Fact]
    public async Task An_upgrade_that_declares_a_body_is_not_an_upgrade()
    {
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(_host.RawHead("GET /v1/speech HTTP/1.1", [.. Wire.UpgradeHeaders, ("Content-Length", "2")]) + "{}");

        var response = await raw.ReadResponseAsync();

        Assert.Equal(404, response!.Status);
        Assert.Equal(Wire.NotFound, response.Body);
        Assert.Equal("close", response.Header("Connection"));
    }

    [Fact]
    public async Task Expect_100_continue_gets_no_100_before_authentication()
    {
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(Wire.Head(_host.Port, "POST /v1/assist HTTP/1.1", ("Content-Length", "2"), ("Expect", "100-continue")));

        var response = await Wire.ReadRawAsync(raw);

        // The body is not asked for: the first and only answer is the refusal.
        Wire.AssertFixed401(Wire.WithoutDate(response!), closing: true);
    }

    [Fact]
    public async Task Requests_on_one_connection_are_answered_one_by_one()
    {
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(
            Wire.Head(_host.Port, "GET /v1/health HTTP/1.1")
            + _host.RawHead("GET /v1/health HTTP/1.1")
            + _host.RawHead("GET /v1/unknown HTTP/1.1"));

        var first = await Wire.ReadRawAsync(raw);
        var second = await Wire.ReadRawAsync(raw);
        var third = await Wire.ReadRawAsync(raw);

        Assert.Equal("HTTP/1.1 401 Unauthorized", Wire.StatusLine(first!));
        Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(second!));
        Assert.Equal("HTTP/1.1 404 Not Found", Wire.StatusLine(third!));
    }
}
