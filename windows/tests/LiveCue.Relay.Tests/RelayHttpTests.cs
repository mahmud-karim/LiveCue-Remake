using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LiveCue.Relay.Tests;

/// <summary>The HTTP contract, exercised over a real loopback socket.</summary>
public sealed class RelayHttpTests : IAsyncLifetime
{
    private RelayTestHost _host = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await RelayTestHost.StartAsync(Cancellation);

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Health_with_the_pairing_token_returns_the_exact_contract_body()
    {
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        using var response = await _host.Client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            """{"ok":true,"protocol":2,"relayVersion":"1.0.0"}""",
            await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearer not-the-token")]
    [InlineData("Bearer relay-test-token-and-more")]
    [InlineData("Basic relay-test-token")]
    [InlineData("relay-test-token")]
    [InlineData("Bearerrelay-test-token")]
    public async Task Health_without_a_valid_token_is_401(string? authorization)
    {
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health", authorization);
        using var response = await _host.Client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            """{"error":"Invalid pairing token."}""",
            await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Theory]
    [InlineData("bearer relay-test-token")]
    [InlineData("BEARER relay-test-token")]
    [InlineData("Bearer    relay-test-token")]
    [InlineData("Bearer\trelay-test-token")]
    public async Task The_bearer_scheme_ignores_case_and_allows_extra_whitespace(string authorization)
    {
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health", authorization);
        using var response = await _host.Client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Two_authorization_headers_are_refused()
    {
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health", authorization: null);
        request.Headers.TryAddWithoutValidation("Authorization", ["Bearer " + RelayTestHost.Token, "Bearer other"]);
        using var response = await _host.Client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("GET", "/v1")]
    [InlineData("GET", "/v1/health/")]
    [InlineData("GET", "/v1/health/extra")]
    [InlineData("GET", "/v1/health?token=x")]
    [InlineData("GET", "/V1/HEALTH")]
    [InlineData("OPTIONS", "/v1/health")]
    [InlineData("GET", "/v1/unknown")]
    [InlineData("GET", "/favicon.ico")]
    [InlineData("POST", "/v1/health")]
    [InlineData("PUT", "/v1/health")]
    [InlineData("DELETE", "/v1/health")]
    [InlineData("POST", "/desktop/pair")]
    public async Task Everything_else_is_404_with_the_token_and_401_without(string method, string path)
    {
        using var authorised = RelayTestHost.Request(new HttpMethod(method), path);
        using var found = await _host.Client.SendAsync(authorised, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, found.StatusCode);
        Assert.Equal(
            """{"error":"Endpoint not found."}""",
            await found.Content.ReadAsStringAsync(Cancellation));

        using var anonymous = RelayTestHost.Request(new HttpMethod(method), path, authorization: null);
        using var refused = await _host.Client.SendAsync(anonymous, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Theory]
    [InlineData("GET /v1/%68ealth HTTP/1.1")]
    [InlineData("GET /v1/health%20 HTTP/1.1")]
    [InlineData("GET //v1/health HTTP/1.1")]
    [InlineData("GET /v1/../v1/health HTTP/1.1")]
    [InlineData("HEAD /v1/health HTTP/1.1")]
    [InlineData("get /v1/health HTTP/1.1")]
    public async Task Spellings_an_http_client_would_normalise_are_404_too(string requestLine)
    {
        // Written to the socket by hand, because HttpClient tidies the request target before sending.
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, _host.Port, Cancellation);
        await using var stream = socket.GetStream();
        var request =
            $"{requestLine}\r\nHost: 127.0.0.1:{_host.Port}\r\nAuthorization: Bearer {RelayTestHost.Token}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), Cancellation);

        using var reader = new StreamReader(stream, Encoding.ASCII);
        var statusLine = await reader.ReadLineAsync(Cancellation);

        Assert.Equal("HTTP/1.1 404 Not Found", statusLine);
    }

    [Theory]
    [InlineData("GET", "/v1/health", true)]
    [InlineData("GET", "/v1/health", false)]
    [InlineData("GET", "/v1/unknown", true)]
    [InlineData("POST", "/v1/unknown", false)]
    public async Task Every_response_carries_the_fixed_headers(string method, string path, bool authorised)
    {
        using var request = RelayTestHost.Request(
            new HttpMethod(method), path, authorised ? "Bearer " + RelayTestHost.Token : null);
        using var response = await _host.Client.SendAsync(request, Cancellation);

        Assert.Equal("no-store", Assert.Single(response.Headers.GetValues("Cache-Control")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.False(response.Headers.Contains("Server"), "The relay must not announce its server software.");
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"), "The relay never sends CORS headers.");
    }

    [Fact]
    public async Task A_401_names_the_bearer_scheme()
    {
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health", authorization: null);
        using var response = await _host.Client.SendAsync(request, Cancellation);

        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }
}
