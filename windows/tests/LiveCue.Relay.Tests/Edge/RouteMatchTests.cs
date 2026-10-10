using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Protocol P2-006 and W-design K-7: with valid credentials, anything that is not exactly a row of the
/// route table is 404 NOT_FOUND. The request line is written by hand, because the comparison is with the
/// raw target and a client library would tidy it first.
/// </summary>
public sealed class RouteMatchTests : IAsyncLifetime
{
    private RelayTestHost _host = null!;

    public async Task InitializeAsync() => _host = await RelayTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData("GET /v1/admin HTTP/1.1")]
    [InlineData("GET /v1/health?token=x HTTP/1.1")]
    [InlineData("GET /v1/%68ealth HTTP/1.1")]
    [InlineData("GET /v1/health/ HTTP/1.1")]
    [InlineData("POST /v1/health HTTP/1.1")]
    [InlineData("GET /livecue/v1/health HTTP/1.1")]
    [InlineData("POST /desktop/pair HTTP/1.1")]
    [InlineData("GET /v1/speech HTTP/1.1")]
    public async Task Unknown_targets_are_404(string requestLine)
    {
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(_host.RawHead(requestLine));

        var response = await raw.ReadResponseAsync();

        Assert.NotNull(response);
        Assert.Equal("HTTP/1.1 404 Not Found", response.StatusLine);
        Assert.Equal(Wire.NotFound, response.Body);
    }

    [Theory]
    [InlineData("GET /V1/HEALTH HTTP/1.1")]
    [InlineData("GET //v1/health HTTP/1.1")]
    [InlineData("GET /v1/../v1/health HTTP/1.1")]
    [InlineData("GET /v1/./health HTTP/1.1")]
    [InlineData("GET /v1/health%20 HTTP/1.1")]
    [InlineData("GET /v1/health;x=1 HTTP/1.1")]
    [InlineData("GET http://{HOST}/v1/health HTTP/1.1")]
    [InlineData("OPTIONS * HTTP/1.1")]
    [InlineData("OPTIONS /v1/health HTTP/1.1")]
    [InlineData("CONNECT {HOST} HTTP/1.1")]
    [InlineData("HEAD /v1/health HTTP/1.1")]
    [InlineData("PUT /v1/assist HTTP/1.1")]
    [InlineData("PATCH /v1/assist HTTP/1.1")]
    [InlineData("TRACE /v1/health HTTP/1.1")]
    [InlineData("get /v1/health HTTP/1.1")]
    [InlineData("DELETE /v1/requests/ HTTP/1.1")]
    [InlineData("DELETE /v1/requests/a%2Fb HTTP/1.1")]
    [InlineData("DELETE /v1/requests/a/b HTTP/1.1")]
    [InlineData("POST /v1/pair/reset HTTP/1.1")]
    [InlineData("POST /pause HTTP/1.1")]
    [InlineData("POST /shutdown HTTP/1.1")]
    public async Task The_raw_target_and_the_method_are_compared_exactly(string requestLine)
    {
        // An absolute-form or authority-form target must name the host of the Host header, or the server
        // core refuses the request itself with a bare 400. With the same host it reaches the edge.
        requestLine = requestLine.Replace("{HOST}", $"127.0.0.1:{_host.Port}", StringComparison.Ordinal);
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(_host.RawHead(requestLine));

        var response = await Wire.ReadRawAsync(raw, hasBody: !requestLine.StartsWith("HEAD", StringComparison.Ordinal));

        // Never a 405, never a redirect: the same coded 404 for all of them.
        Assert.NotNull(response);
        Assert.Equal("HTTP/1.1 404 Not Found", Wire.StatusLine(response));
        Assert.All(Wire.HeaderNames(response), name => Assert.Contains(name, Wire.AllowedResponseHeaders));
    }

    [Fact]
    public async Task A_request_id_of_eighty_characters_is_a_route_and_one_of_eighty_one_is_not()
    {
        using var known = await _host.SendAsync(_host.Request(HttpMethod.Delete, "/v1/requests/" + new string('a', 80)));
        using var unknown = await _host.SendAsync(_host.Request(HttpMethod.Delete, "/v1/requests/" + new string('a', 81)));

        // Route 9 answers REQUEST_NOT_FOUND for an id nobody is running; the edge answers NOT_FOUND.
        await Wire.AssertErrorAsync(known, 404, "REQUEST_NOT_FOUND");
        await Wire.AssertErrorAsync(unknown, 404, "NOT_FOUND");
    }
}
