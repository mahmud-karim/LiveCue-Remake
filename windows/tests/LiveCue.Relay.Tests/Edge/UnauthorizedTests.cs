using System.Net;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Protocol P2-001 to P2-005: a caller without the token gets one answer, whatever it sent and whichever
/// step refused it, and causes no work. Requests are written to the socket by hand, and the answers are
/// compared byte for byte with the value of <c>Date</c> replaced (W-design K-1).
/// </summary>
public sealed class UnauthorizedTests : IAsyncLifetime
{
    private RelayTestHost _host = null!;

    public async Task InitializeAsync() => _host = await RelayTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    public static TheoryData<string> AnyMethodAndTarget()
    {
        var lines = new TheoryData<string>();
        foreach (var route in LiveCue.Relay.Edge.RouteTable.Rows)
        {
            lines.Add($"{route.Method} {Wire.TargetOf(route)} HTTP/1.1");
        }

        foreach (var line in (string[])
        [
            "GET /v1/unknown HTTP/1.1",
            "POST /desktop/pair HTTP/1.1",
            "DELETE / HTTP/1.1",
            "GET /v1/health?token=x HTTP/1.1",
            "GET /v1/%68ealth HTTP/1.1",
            "GET /livecue/v1/health HTTP/1.1",
            "GET http://{HOST}/v1/health HTTP/1.1",
            "OPTIONS /v1/health HTTP/1.1",
            "OPTIONS * HTTP/1.1",
            "PUT /v1/assist HTTP/1.1",
            "PATCH /v1/assist HTTP/1.1",
            "TRACE /v1/health HTTP/1.1",
            "get /v1/health HTTP/1.1",
            "BREW /pot HTTP/1.1",
        ])
        {
            lines.Add(line);
        }

        return lines;
    }

    [Theory]
    [MemberData(nameof(AnyMethodAndTarget))]
    public async Task No_authorization_is_the_fixed_401_on_any_method_and_target(string requestLine)
    {
        // An absolute-form target names the host of the Host header (anything else is the server core's bare 400).
        requestLine = requestLine.Replace("{HOST}", $"127.0.0.1:{_host.Port}", StringComparison.Ordinal);
        var reference = await Wire.ExchangeAsync(_host.Port, Wire.Head(_host.Port, "GET /v1/health HTTP/1.1"));

        var response = await Wire.ExchangeAsync(_host.Port, Wire.Head(_host.Port, requestLine));

        Wire.AssertFixed401(response);
        Assert.Equal(reference, response);
    }

    [Fact]
    public async Task A_well_formed_unknown_token_is_the_same_401()
    {
        using var viaClient = await _host.SendAsync(
            _host.Request(HttpMethod.Get, "/v1/health", TestTokens.Bearer(TestTokens.WellFormedUnknown)));
        var reference = await Wire.ExchangeAsync(_host.Port, Wire.Head(_host.Port, "GET /v1/health HTTP/1.1"));
        var raw = await Wire.ExchangeAsync(_host.Port, Wire.Head(
            _host.Port,
            "GET /v1/health HTTP/1.1",
            ("Authorization", TestTokens.Bearer(TestTokens.WellFormedUnknown)),
            ("X-LiveCue-Protocol", "2")));

        Assert.Equal(HttpStatusCode.Unauthorized, viaClient.StatusCode);
        Assert.Equal(Wire.Unauthorized, await viaClient.Content.ReadAsStringAsync());
        Wire.AssertFixed401(raw);
        Assert.Equal(reference, raw);
    }

    public static TheoryData<string, string, string> BrowserHeaderOnEveryRoute()
    {
        var cases = new TheoryData<string, string, string>();
        foreach (var route in Wire.HttpRoutes)
        {
            foreach (var (name, value) in BrowserHeaders)
            {
                cases.Add($"{route.Method} {Wire.TargetOf(route)} HTTP/1.1", name, value);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(BrowserHeaderOnEveryRoute))]
    public async Task Browser_headers_are_the_same_401_on_every_route_and_on_the_upgrade(
        string requestLine, string header, string value)
    {
        var reference = await Wire.ExchangeAsync(_host.Port, Wire.Head(_host.Port, "GET /v1/health HTTP/1.1"));

        // The accepted token and the protocol header are there: only the browser header is wrong.
        var response = await Wire.ExchangeAsync(_host.Port, _host.RawHead(requestLine, (header, value)));
        var upgrade = await Wire.ExchangeAsync(_host.Port, _host.RawHead(
            "GET /v1/speech HTTP/1.1",
            [.. Wire.UpgradeHeaders, ("X-LiveCue-Speech-Model", "nemotron"), (header, value)]));

        Wire.AssertFixed401(response);
        Assert.Equal(reference, response);
        Assert.Equal(reference, upgrade);
    }

    public static TheoryData<string, string> MalformedAuthorization => new()
    {
        { "the scheme in lower case", "bearer {T}" },
        { "two spaces after Bearer", "Bearer  {T}" },
        { "a token of 42 characters", "Bearer {T42}" },
        { "a token of 44 characters", "Bearer {T}t" },
        { "a token containing =", "Bearer {T42}=" },
        { "a token containing +", "Bearer {T42}+" },
        { "a token containing /", "Bearer {T42}/" },
        { "two Authorization headers", "TWICE" },
        { "only a Cookie header", "COOKIE" },
    };

    [Theory]
    [MemberData(nameof(MalformedAuthorization))]
    public async Task Malformed_authorization_is_the_same_401(string what, string form)
    {
        Assert.NotEmpty(what);
        var reference = await Wire.ExchangeAsync(_host.Port, Wire.Head(_host.Port, "GET /v1/health HTTP/1.1"));

        // The accepted token, spoiled only in the way the case says.
        var value = form
            .Replace("{T42}", _host.Token[..42], StringComparison.Ordinal)
            .Replace("{T}", _host.Token, StringComparison.Ordinal);
        (string Name, string Value)[] headers = form switch
        {
            "TWICE" =>
            [
                ("Authorization", TestTokens.Bearer(_host.Token)),
                ("Authorization", TestTokens.Bearer(_host.Token)),
                ("X-LiveCue-Protocol", "2"),
            ],
            "COOKIE" => [("Cookie", "token=" + _host.Token), ("X-LiveCue-Protocol", "2")],
            _ => [("Authorization", value), ("X-LiveCue-Protocol", "2")],
        };

        var response = await Wire.ExchangeAsync(_host.Port, Wire.Head(_host.Port, "GET /v1/health HTTP/1.1", headers));

        Wire.AssertFixed401(response);
        Assert.Equal(reference, response);
    }

    [Fact]
    public async Task A_401_does_no_work()
    {
        var filesBefore = _host.Data.FileNames();
        var readsBefore = _host.PairingFiles.Reads;
        var writesBefore = _host.PairingFiles.Writes;
        var unprotectBefore = _host.Protector.UnprotectCalls;

        // A whole assist request with a well-formed token that is not accepted.
        using var withBody = await _host.SendAsync(_host.Request(
            HttpMethod.Post, "/v1/assist", TestTokens.Bearer(TestTokens.WellFormedUnknown), json: Bodies.Assist()));

        // A declared body of which no byte is ever sent: the answer does not wait for it.
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(Wire.Head(_host.Port, "POST /v1/assist HTTP/1.1", ("Content-Length", "1000")));
        var unsent = await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(5));
        await _host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, withBody.StatusCode);
        Wire.AssertFixed401(Wire.WithoutDate(unsent!), closing: true);

        // Presence is unchanged, and so is everything else: the one event is the start of the relay.
        Assert.Equal(0, _host.Events.Count<PhoneSeen>());
        Assert.IsType<RelayStarted>(Assert.Single(_host.Events.Snapshot()));
        Assert.Empty(_host.Backend.Runs);
        Assert.Equal(0, _host.Catalog.Calls);
        Assert.Equal(0, _host.Lab.ProbeCalls);
        Assert.Equal(0, _host.Lab.StartCalls);
        Assert.Empty(_host.Processes.Requests);
        Assert.Empty(_host.SelfCheck.Calls);

        // No file was touched.
        Assert.Equal(filesBefore, _host.Data.FileNames());
        Assert.Equal(readsBefore, _host.PairingFiles.Reads);
        Assert.Equal(writesBefore, _host.PairingFiles.Writes);
        Assert.Equal(unprotectBefore, _host.Protector.UnprotectCalls);
    }

    /// <summary>
    /// The contract behind P2-001 to P2-004 and P2-016, in one place: every refusal before authentication
    /// is the same bytes. Each row is one shape of request; within a row only the reason for the refusal
    /// changes, and the answers must not.
    /// </summary>
    [Theory]
    [InlineData("GET /v1/health HTTP/1.1", null, false, true)]
    [InlineData("POST /v1/assist HTTP/1.1", null, false, true)]
    [InlineData("POST /v1/assist HTTP/1.1", "{\"a\":1}", true, true)]
    [InlineData("DELETE /v1/requests/abc HTTP/1.1", null, false, true)]
    [InlineData("GET /nothing/here HTTP/1.1", null, false, true)]
    [InlineData("HEAD /v1/health HTTP/1.1", null, false, false)]
    public async Task Every_refusal_before_authentication_is_the_same_bytes(
        string requestLine, string? body, bool closing, bool hasBody)
    {
        var good = TestTokens.Bearer(_host.Token);
        (string Name, string Value)[][] reasons =
        [
            [],                                                                               // E-2: no Authorization
            [("Authorization", "Basic " + _host.Token)],                                      // E-2: another scheme
            [("Authorization", good + Wire.LineSeparator)],                                             // E-2: a trailing line separator
            [("Authorization", good), ("Authorization", good)],                               // E-2: two headers
            [("Authorization", TestTokens.Bearer(TestTokens.WellFormedUnknown))],             // E-3: not accepted
            [("Authorization", good), ("Origin", "https://bad.test")],                        // E-1
            [("Authorization", good), ("Origin", string.Empty)],                              // E-1, empty value (K-8)
            [("Authorization", good), ("Sec-Fetch-Site", "same-origin")],                     // E-1
            [("Authorization", good), ("Tailscale-Funnel-Request", "?1")],                    // E-1, Private mode
            [("Authorization", good), ("Tailscale-Funnel-Request", "?1"), ("Origin", "x")],   // E-1, both
        ];

        var answers = new List<string?>();
        foreach (var reason in reasons)
        {
            (string Name, string Value)[] headers = body is null
                ? [.. reason, ("X-LiveCue-Protocol", "2")]
                : [.. reason, ("X-LiveCue-Protocol", "2"), ("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))];
            answers.Add(await ExchangeUtf8Async(Wire.Head(_host.Port, requestLine, headers) + body, hasBody));
        }

        Wire.AssertFixed401(answers[0], closing, hasBody);
        Assert.All(answers, answer => Assert.Equal(answers[0], answer));
    }

    [Fact]
    public async Task The_refusal_of_an_upgrade_is_the_same_bytes_as_any_other()
    {
        var reference = await Wire.ExchangeAsync(_host.Port, Wire.Head(_host.Port, "GET /v1/health HTTP/1.1"));

        var noToken = await Wire.ExchangeAsync(_host.Port, Wire.Head(_host.Port, "GET /v1/speech HTTP/1.1", Wire.UpgradeHeaders));
        var unknownToken = await Wire.ExchangeAsync(_host.Port, Wire.Head(
            _host.Port,
            "GET /v1/speech HTTP/1.1",
            [.. Wire.UpgradeHeaders, ("Authorization", TestTokens.Bearer(TestTokens.WellFormedUnknown))]));

        Assert.Equal(reference, noToken);
        Assert.Equal(reference, unknownToken);
    }

    private static readonly (string Name, string Value)[] BrowserHeaders =
    [
        ("Origin", "https://bad.test"),
        ("Origin", string.Empty),
        ("Sec-Fetch-Site", "same-origin"),
        ("Sec-Fetch-Site", string.Empty),
    ];

    /// <summary>Like <see cref="Wire.ExchangeAsync"/>, for a request that holds a character outside ASCII.</summary>
    private async Task<string?> ExchangeUtf8Async(string request, bool hasBody)
    {
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(System.Text.Encoding.UTF8.GetBytes(request));
        var response = await Wire.ReadRawAsync(raw, hasBody);
        return response is null ? null : Wire.WithoutDate(response);
    }
}
