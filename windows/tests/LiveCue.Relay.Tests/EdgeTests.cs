using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LiveCue.Relay.Tests;

/// <summary>
/// Protocol section 15.1 (edge) and 15.2 (health), relay side, for what the spike has built. Each
/// test names the conformance statement it covers.
/// </summary>
public sealed class EdgeTests : IAsyncLifetime
{
    private const string Unauthorized = """{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}""";
    private const string NotFound = """{"error":"Endpoint not found.","code":"NOT_FOUND"}""";

    private RelayTestHost _host = null!;

    public async Task InitializeAsync() => _host = await RelayTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task P2_020_health_is_200_with_exactly_ok_protocol_and_relay_version()
    {
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        using var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"ok":true,"protocol":2,"relayVersion":"1.0.0"}""",
            await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("GET", "/v1/health")]
    [InlineData("POST", "/v1/assist")]
    [InlineData("GET", "/v1/unknown")]
    [InlineData("DELETE", "/")]
    public async Task P2_001_no_authorization_header_is_the_same_401_for_any_method_and_target(string method, string target)
    {
        using var request = RelayTestHost.Request(new HttpMethod(method), target, authorization: null);
        using var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Unauthorized, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P2_002_a_well_formed_token_that_is_not_accepted_is_the_same_401()
    {
        using var request = RelayTestHost.Request(
            HttpMethod.Get, "/v1/health", "Bearer " + RelayTestHost.OtherToken);
        using var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Unauthorized, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Origin", "https://bad.test")]
    [InlineData("Origin", "")]
    [InlineData("Sec-Fetch-Site", "same-origin")]
    [InlineData("Sec-Fetch-Site", "none")]
    public async Task P2_003_a_browser_header_beside_the_valid_token_is_the_same_401(string header, string value)
    {
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        request.Headers.TryAddWithoutValidation(header, value);
        using var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Unauthorized, await response.Content.ReadAsStringAsync());
    }

    public static TheoryData<string> MalformedAuthorization => new()
    {
        "bearer " + RelayTestHost.Token,                    // scheme in lower case
        "BEARER " + RelayTestHost.Token,
        "Bearer  " + RelayTestHost.Token,                   // two spaces
        "Bearer\t" + RelayTestHost.Token,
        "Bearer " + RelayTestHost.Token[..42],              // 42 characters
        "Bearer " + RelayTestHost.Token + "t",              // 44 characters
        "Bearer " + RelayTestHost.Token[..42] + "=",
        "Bearer " + RelayTestHost.Token[..42] + "+",
        "Bearer " + RelayTestHost.Token[..42] + "/",
        "Bearer",
        "Basic " + RelayTestHost.Token,
        RelayTestHost.Token,
    };

    [Theory]
    [MemberData(nameof(MalformedAuthorization))]
    public async Task P2_004_every_malformed_authorization_is_the_same_401(string authorization)
    {
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health", authorization);
        using var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Unauthorized, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P2_004_two_authorization_headers_or_only_a_cookie_are_the_same_401()
    {
        using var twice = RelayTestHost.Request(HttpMethod.Get, "/v1/health", authorization: null);
        twice.Headers.TryAddWithoutValidation(
            "Authorization", ["Bearer " + RelayTestHost.Token, "Bearer " + RelayTestHost.Token]);
        using var refusedTwice = await _host.Client.SendAsync(twice);

        using var cookie = RelayTestHost.Request(HttpMethod.Get, "/v1/health", authorization: null);
        cookie.Headers.TryAddWithoutValidation("Cookie", "session=" + RelayTestHost.Token);
        using var refusedCookie = await _host.Client.SendAsync(cookie);

        Assert.Equal(Unauthorized, await refusedTwice.Content.ReadAsStringAsync());
        Assert.Equal(Unauthorized, await refusedCookie.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P2_005_a_401_leaves_the_presence_count_unchanged()
    {
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health", "Bearer " + RelayTestHost.OtherToken);
        using var response = await _host.Client.SendAsync(request);

        Assert.DoesNotContain(_host.Events, relayEvent => relayEvent is PhoneSeen);
    }

    [Theory]
    [InlineData("GET", "/v1/admin")]
    [InlineData("GET", "/v1/health?token=x")]
    [InlineData("GET", "/v1/health/")]
    [InlineData("POST", "/v1/health")]
    [InlineData("GET", "/livecue/v1/health")]
    [InlineData("POST", "/desktop/pair")]
    [InlineData("GET", "/V1/HEALTH")]
    [InlineData("GET", "/")]
    [InlineData("OPTIONS", "/v1/health")]
    public async Task P2_006_with_valid_credentials_anything_but_a_route_is_404(string method, string target)
    {
        using var request = RelayTestHost.Request(new HttpMethod(method), target);
        using var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFound, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("GET /v1/%68ealth HTTP/1.1")]
    [InlineData("GET /v1/health%20 HTTP/1.1")]
    [InlineData("GET //v1/health HTTP/1.1")]
    [InlineData("GET /v1/../v1/health HTTP/1.1")]
    [InlineData("HEAD /v1/health HTTP/1.1")]
    [InlineData("get /v1/health HTTP/1.1")]
    public async Task P2_006_spellings_an_http_client_would_tidy_are_404_too(string requestLine)
    {
        // Written to the socket by hand, because HttpClient normalises the request target before sending.
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, _host.Port);
        await using var stream = socket.GetStream();
        var request =
            $"{requestLine}\r\nHost: 127.0.0.1:{_host.Port}\r\nAuthorization: Bearer {RelayTestHost.Token}\r\n" +
            "X-LiveCue-Protocol: 2\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        using var reader = new StreamReader(stream, Encoding.ASCII);
        var statusLine = await reader.ReadLineAsync();

        Assert.Equal("HTTP/1.1 404 Not Found", statusLine);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("2.0")]
    [InlineData("")]
    public async Task P2_007_a_wrong_protocol_header_is_400_on_a_route_and_still_404_off_one(string? protocol)
    {
        using var onRoute = RelayTestHost.Request(HttpMethod.Get, "/v1/health", protocol: protocol);
        using var mismatch = await _host.Client.SendAsync(onRoute);

        using var offRoute = RelayTestHost.Request(HttpMethod.Get, "/v1/unknown", protocol: protocol);
        using var notFound = await _host.Client.SendAsync(offRoute);

        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Equal(
            """{"error":"Protocol version mismatch.","code":"PROTOCOL_MISMATCH"}""",
            await mismatch.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/v1/health", true, "2")]
    [InlineData("GET", "/v1/health", false, "2")]
    [InlineData("GET", "/v1/unknown", true, "2")]
    [InlineData("POST", "/v1/unknown", false, "2")]
    [InlineData("GET", "/v1/health", true, "1")]
    public async Task P2_008_every_response_has_the_three_fixed_headers_and_no_others_of_note(
        string method, string target, bool authorised, string protocol)
    {
        using var request = RelayTestHost.Request(
            new HttpMethod(method), target, authorised ? "" : null, protocol);
        request.Headers.TryAddWithoutValidation("Cookie", "a=b");
        request.Headers.TryAddWithoutValidation("X-Unknown", "ignored");
        using var response = await _host.Client.SendAsync(request);

        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("no-store", Assert.Single(response.Headers.GetValues("Cache-Control")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));

        var names = response.Headers.Select(header => header.Key)
            .Concat(response.Content.Headers.Select(header => header.Key))
            .ToList();
        Assert.DoesNotContain("Server", names);
        Assert.DoesNotContain("Set-Cookie", names);
        Assert.DoesNotContain("WWW-Authenticate", names);
        Assert.DoesNotContain(names, name => name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task P2_009_health_sent_with_a_body_is_400_invalid_request()
    {
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Request is not valid for this endpoint.","code":"INVALID_REQUEST"}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P2_016_a_funnel_request_is_401_in_private_mode_and_served_in_public_mode()
    {
        using var privateRequest = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        privateRequest.Headers.TryAddWithoutValidation("Tailscale-Funnel-Request", "?1");
        using var refused = await _host.Client.SendAsync(privateRequest);

        await using var publicHost = await RelayTestHost.StartAsync(ConnectionMode.Public);
        using var publicRequest = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        publicRequest.Headers.TryAddWithoutValidation("Tailscale-Funnel-Request", "?1");
        using var served = await publicHost.Client.SendAsync(publicRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    [Fact]
    public async Task P2_017_each_authenticated_request_produces_exactly_one_presence_event_even_when_404()
    {
        using var health = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        using var first = await _host.Client.SendAsync(health);
        using var unknown = RelayTestHost.Request(HttpMethod.Get, "/v1/unknown");
        using var second = await _host.Client.SendAsync(unknown);

        Assert.Equal(2, _host.Events.Count(relayEvent => relayEvent is PhoneSeen));
    }
}
