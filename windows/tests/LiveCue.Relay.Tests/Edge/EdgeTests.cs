using System.Net;
using System.Text;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Protocol section 15.1 (edge) and 15.2 (health), relay side, for what the foundation's edge does.
/// Each test names the conformance statement it covers. Carried over from the spike; work package
/// WP-W01 replaces this file with the named tests of the test plan (section 1.2).
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
        using var response = await _host.GetAsync("/v1/health");

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
        using var response = await _host.SendAsync(_host.Request(new HttpMethod(method), target, authorization: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Unauthorized, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P2_002_a_well_formed_token_that_is_not_accepted_is_the_same_401()
    {
        using var response = await _host.SendAsync(
            _host.Request(HttpMethod.Get, "/v1/health", TestTokens.Bearer(TestTokens.WellFormedUnknown)));

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
        var request = _host.Request(HttpMethod.Get, "/v1/health");
        request.Headers.TryAddWithoutValidation(header, value);
        using var response = await _host.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Unauthorized, await response.Content.ReadAsStringAsync());
    }

    public static TheoryData<string, string> MalformedAuthorization => new()
    {
        { "bearer ", string.Empty },      // scheme in lower case
        { "BEARER ", string.Empty },
        { "Bearer  ", string.Empty },     // two spaces
        { "Bearer\t", string.Empty },
        { "Bearer ", "=" },               // a character outside the alphabet at the end
        { "Bearer ", "+" },
        { "Bearer ", "/" },
        { "Basic ", string.Empty },
        { string.Empty, string.Empty },   // the token alone
    };

    [Theory]
    [MemberData(nameof(MalformedAuthorization))]
    public async Task P2_004_every_malformed_authorization_is_the_same_401(string scheme, string lastCharacter)
    {
        // The accepted token, spoiled only in the way the case says.
        var token = lastCharacter.Length == 0 ? _host.Token : _host.Token[..42] + lastCharacter;
        using var response = await _host.SendAsync(_host.Request(HttpMethod.Get, "/v1/health", scheme + token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Unauthorized, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P2_004_a_token_of_42_or_44_characters_two_headers_or_only_a_cookie_are_the_same_401()
    {
        using var shorter = await _host.SendAsync(_host.Request(HttpMethod.Get, "/v1/health", "Bearer " + _host.Token[..42]));
        using var longer = await _host.SendAsync(_host.Request(HttpMethod.Get, "/v1/health", "Bearer " + _host.Token + "t"));
        using var bare = await _host.SendAsync(_host.Request(HttpMethod.Get, "/v1/health", "Bearer"));

        var twice = _host.Request(HttpMethod.Get, "/v1/health", authorization: null);
        twice.Headers.TryAddWithoutValidation(
            "Authorization", [TestTokens.Bearer(_host.Token), TestTokens.Bearer(_host.Token)]);
        using var refusedTwice = await _host.SendAsync(twice);

        var cookie = _host.Request(HttpMethod.Get, "/v1/health", authorization: null);
        cookie.Headers.TryAddWithoutValidation("Cookie", "session=" + _host.Token);
        using var refusedCookie = await _host.SendAsync(cookie);

        Assert.Equal(Unauthorized, await shorter.Content.ReadAsStringAsync());
        Assert.Equal(Unauthorized, await longer.Content.ReadAsStringAsync());
        Assert.Equal(Unauthorized, await bare.Content.ReadAsStringAsync());
        Assert.Equal(Unauthorized, await refusedTwice.Content.ReadAsStringAsync());
        Assert.Equal(Unauthorized, await refusedCookie.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P2_005_a_401_leaves_the_presence_count_unchanged_and_does_no_work()
    {
        using var response = await _host.SendAsync(
            _host.Request(HttpMethod.Post, "/v1/assist", TestTokens.Bearer(TestTokens.WellFormedUnknown), json: Bodies.Assist()));
        await _host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _host.Events.Count<PhoneSeen>());
        Assert.Empty(_host.Backend.Runs);
        Assert.Equal(0, _host.Catalog.Calls);
        Assert.Equal(0, _host.Lab.ProbeCalls);
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
    [InlineData("GET", "/v1/speech")]
    public async Task P2_006_with_valid_credentials_anything_but_a_route_is_404(string method, string target)
    {
        using var response = await _host.SendAsync(_host.Request(new HttpMethod(method), target));

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
    [InlineData("DELETE /v1/requests/a%2Fb HTTP/1.1")]
    public async Task P2_006_spellings_an_http_client_would_tidy_are_404_too(string requestLine)
    {
        // Written to the socket by hand, because HttpClient normalises the request target before sending.
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(_host.RawHead(requestLine, ("Connection", "close")));

        var response = await raw.ReadResponseAsync();

        Assert.NotNull(response);
        Assert.Equal("HTTP/1.1 404 Not Found", response.StatusLine);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("2.0")]
    [InlineData("")]
    public async Task P2_007_a_wrong_protocol_header_is_400_on_a_route_and_still_404_off_one(string? protocol)
    {
        using var mismatch = await _host.SendAsync(_host.Request(HttpMethod.Get, "/v1/health", protocol: protocol));
        using var notFound = await _host.SendAsync(_host.Request(HttpMethod.Get, "/v1/unknown", protocol: protocol));

        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Equal(
            """{"error":"Protocol version mismatch.","code":"PROTOCOL_MISMATCH"}""",
            await mismatch.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Theory]
    [InlineData("GET /v1/health HTTP/1.1", true, 200)]
    [InlineData("GET /v1/health HTTP/1.1", false, 401)]
    [InlineData("GET /v1/unknown HTTP/1.1", true, 404)]
    [InlineData("POST /v1/unknown HTTP/1.1", false, 401)]
    public async Task P2_008_every_response_has_only_the_allowed_headers(string requestLine, bool authorised, int status)
    {
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(authorised
            ? _host.RawHead(requestLine, ("Cookie", "a=b"), ("X-Unknown", "ignored"), ("Connection", "close"))
            : RawHttp.Head(requestLine, _host.Port, ("Cookie", "a=b"), ("X-Unknown", "ignored"), ("Connection", "close")));

        var response = await raw.ReadResponseAsync();

        // The allowlist of W-design K-1: the three fixed headers, the length, and what Kestrel itself adds.
        Assert.NotNull(response);
        Assert.Equal(status, response.Status);
        Assert.Equal("application/json; charset=utf-8", response.Header("Content-Type"));
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Equal("nosniff", response.Header("X-Content-Type-Options"));
        Assert.All(response.HeaderNames, name => Assert.Contains(
            name,
            (string[])["Content-Type", "Cache-Control", "X-Content-Type-Options", "Content-Length", "Date", "Connection"]));
    }

    [Fact]
    public async Task P2_009_health_sent_with_a_body_is_400_invalid_request()
    {
        var request = _host.Request(HttpMethod.Get, "/v1/health");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await _host.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Request is not valid for this endpoint.","code":"INVALID_REQUEST"}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P2_012_one_byte_over_the_body_limit_is_413_and_the_limit_itself_passes()
    {
        using var atLimit = await _host.PostAsync("/v1/pair/verify", Bodies.ObjectOfSize(131_072));
        using var over = await _host.PostAsync("/v1/pair/verify", Bodies.ObjectOfSize(131_073));

        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, over.StatusCode);
        Assert.Equal(
            """{"error":"Request exceeds 128 KB.","code":"PAYLOAD_TOO_LARGE"}""",
            await over.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"x\"")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("{} {}")]
    public async Task P2_013_a_body_that_is_not_one_json_object_is_400_invalid_json(string body)
    {
        using var response = await _host.PostAsync("/v1/pair/verify", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Body must be valid JSON.","code":"INVALID_JSON"}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P2_016_a_funnel_request_is_401_in_private_mode_and_served_in_public_mode()
    {
        var privateRequest = _host.Request(HttpMethod.Get, "/v1/health");
        privateRequest.Headers.TryAddWithoutValidation("Tailscale-Funnel-Request", "?1");
        using var refused = await _host.SendAsync(privateRequest);

        await using var publicHost = await RelayTestHost.StartAsync(options => options.Mode = ConnectionMode.Public);
        var publicRequest = publicHost.Request(HttpMethod.Get, "/v1/health");
        publicRequest.Headers.TryAddWithoutValidation("Tailscale-Funnel-Request", "?1");
        using var served = await publicHost.SendAsync(publicRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    [Fact]
    public async Task P2_017_each_authenticated_request_produces_exactly_one_presence_event_even_when_404()
    {
        using var first = await _host.GetAsync("/v1/health");
        using var second = await _host.GetAsync("/v1/unknown");
        await _host.Events.SettleAsync();

        Assert.Equal(2, _host.Events.Count<PhoneSeen>());
    }

    [Fact]
    public async Task P2_021_pair_verify_answers_ok_for_any_object_and_records_nothing()
    {
        using var empty = await _host.PostAsync("/v1/pair/verify", "{}");
        using var named = await _host.PostAsync("/v1/pair/verify", """{"device":"x"}""");
        await _host.Events.SettleAsync();

        Assert.Equal("""{"ok":true}""", await empty.Content.ReadAsStringAsync());
        Assert.Equal("""{"ok":true}""", await named.Content.ReadAsStringAsync());
        Assert.All(_host.Events.Snapshot(), relayEvent => Assert.True(relayEvent is RelayStarted or PhoneSeen));
    }
}
