using System.Diagnostics;
using System.Globalization;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.Speech;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;
using Xunit.Abstractions;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>
/// Test plan 4.1 and the edge rows of 4.5, against a relay in which every area is the production one.
/// The caller here is hostile: without the token it must learn nothing, not even what the relay is doing;
/// with the token it must find no route beyond the ten, no method beyond the table's and no way to make
/// the relay say more than a code and a fixed sentence.
/// </summary>
public sealed class EdgeSecurityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_preflight_is_401_without_cors_headers(bool withToken)
    {
        // S-03. A browser asks before it sends: the answer is the fixed 401 and grants nothing.
        await using var relay = await ConformanceRelay.StartAsync();
        (string, string)[] browser =
        [
            ("Origin", "https://bad.test"),
            ("Access-Control-Request-Method", "POST"),
            ("Access-Control-Request-Headers", "authorization, x-livecue-protocol"),
        ];

        foreach (var target in (string[])["/v1/assist", "/v1/health", "/v1/speech", "/v1/nowhere", "*"])
        {
            var head = withToken
                ? relay.RawHead($"OPTIONS {target} HTTP/1.1", browser)
                : Wire.Head(relay.Port, $"OPTIONS {target} HTTP/1.1", browser);
            var response = await Wire.ExchangeAsync(relay.Port, head);

            Wire.AssertFixed401(response);
            Assert.DoesNotContain(Wire.HeaderNames(response!), name => name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(Wire.HeaderNames(response!), name => name.Equals("Allow", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(Wire.HeaderNames(response!), name => name.Equals("Vary", StringComparison.OrdinalIgnoreCase));
        }

        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>());
    }

    [Theory]
    [InlineData("Origin", "https://bad.test")]
    [InlineData("Origin", "null")]
    [InlineData("Origin", "")]
    [InlineData("Origin", "http://127.0.0.1")]
    [InlineData("Sec-Fetch-Site", "same-origin")]
    [InlineData("Sec-Fetch-Site", "none")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site", "")]
    [InlineData("origin", "https://bad.test")]
    [InlineData("SEC-FETCH-SITE", "none")]
    public async Task A_browser_header_alone_with_any_value_is_the_fixed_401_on_every_route(string name, string value)
    {
        // S-03 with the valid token: the header is what is refused, whatever it says and however it is cased.
        await using var relay = await ConformanceRelay.StartAsync();
        foreach (var route in RouteTable.Rows)
        {
            var line = $"{route.Method} {Wire.TargetOf(route)} HTTP/1.1";
            (string, string)[] extra = route.Id == RouteId.Speech ? [.. Wire.UpgradeHeaders, (name, value)] : [(name, value)];
            Wire.AssertFixed401(await Wire.ExchangeAsync(relay.Port, relay.RawHead(line, extra)));
        }

        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>());
        Assert.Equal(RouteTable.Rows.Count, relay.Edge.Malformed);
    }

    [Fact]
    public async Task The_three_401_answers_are_identical()
    {
        // S-05: E-1, E-2 and E-3 compared byte for byte, Date replaced, on a known route, an unknown one,
        // the upgrade, and a request that declares a body.
        await using var relay = await ConformanceRelay.StartAsync();
        var accepted = ("Authorization", TestTokens.Bearer(relay.Token));
        (string What, (string, string)[] Headers)[] causes =
        [
            ("E-1 Origin", [accepted, ("Origin", "https://bad.test")]),
            ("E-1 Sec-Fetch-Site", [accepted, ("Sec-Fetch-Site", "same-origin")]),
            ("E-1 Funnel in Private mode", [accepted, ("Tailscale-Funnel-Request", "?1")]),
            ("E-2 no header", []),
            ("E-2 malformed", [("Authorization", "Bearer short")]),
            ("E-3 unknown token", [("Authorization", TestTokens.Bearer(TestTokens.WellFormedUnknown))]),
        ];

        (string Line, (string, string)[] Headers, string Body)[] shapes =
        [
            ("GET /v1/health HTTP/1.1", [("X-LiveCue-Protocol", "2")], string.Empty),
            ("GET /v1/nowhere HTTP/1.1", [("X-LiveCue-Protocol", "2")], string.Empty),
            ("DELETE /v1/requests/request-0001 HTTP/1.1", [], string.Empty),
            ("GET /v1/speech HTTP/1.1", [.. Wire.UpgradeHeaders, ("X-LiveCue-Speech-Model", "nemotron")], string.Empty),
            ("POST /v1/assist HTTP/1.1", [("X-LiveCue-Protocol", "2"), ("Content-Length", "2")], "{}"),
            ("POST /desktop/pair HTTP/1.1", [("Content-Length", "2")], "{}"),
        ];

        foreach (var shape in shapes)
        {
            var answers = new List<string>();
            foreach (var cause in causes)
            {
                var response = await Wire.ExchangeAsync(
                    relay.Port, Wire.Head(relay.Port, shape.Line, [.. cause.Headers, .. shape.Headers]) + shape.Body);
                Wire.AssertFixed401(response, closing: shape.Body.Length > 0);
                answers.Add(response!);
            }

            Assert.Single(answers.Distinct(StringComparer.Ordinal));
        }

        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>());
    }

    [Fact]
    public async Task An_unauthenticated_caller_sees_the_same_bytes_whatever_the_relay_is_doing()
    {
        // The property the reviewer asked for, wider than S-05: not only which step refused is hidden,
        // but everything about the relay's state. The same requests are sent while the relay is idle,
        // paused, busy with an answer, full at the gauge, full at the speech cap, and after a reset (with
        // the token that was accepted a moment ago). Every answer is the same bytes.
        var clock = new ManualTimeProvider();
        await using var relay = await ConformanceRelay.StartAsync(options => options.Clock = clock);
        var old = relay.Token;

        async Task<List<string>> ProbeAsync(string token)
        {
            (string, string)[][] credentials =
            [
                [],
                [("Authorization", "Bearer short")],
                [("Authorization", "bearer " + token)],
                [("Authorization", TestTokens.Bearer(TestTokens.WellFormedUnknown))],
                [("Authorization", TestTokens.Bearer(token)), ("Origin", "https://bad.test")],
                [("Cookie", "token=" + token)],
            ];
            (string Line, (string, string)[] Headers, string Body)[] shapes =
            [
                ("GET /v1/health HTTP/1.1", [("X-LiveCue-Protocol", "2")], string.Empty),
                ("GET /v1/models HTTP/1.1", [("X-LiveCue-Protocol", "2")], string.Empty),
                ("GET /v1/local-model HTTP/1.1", [("X-LiveCue-Protocol", "2")], string.Empty),
                ("GET /v1/nowhere HTTP/1.1", [], string.Empty),
                ("DELETE /v1/requests/request-0001 HTTP/1.1", [("X-LiveCue-Protocol", "2")], string.Empty),
                ("GET /v1/speech HTTP/1.1", [.. Wire.UpgradeHeaders, ("X-LiveCue-Speech-Model", "nemotron")], string.Empty),
                ("POST /v1/assist HTTP/1.1", [("X-LiveCue-Protocol", "2"), ("Content-Length", "0")], string.Empty),
                ("POST /v1/local-model/start HTTP/1.1", [("X-LiveCue-Protocol", "2"), ("Content-Length", "0")], string.Empty),
            ];

            var answers = new List<string>();
            foreach (var shape in shapes)
            {
                foreach (var headers in credentials)
                {
                    var response = await Wire.ExchangeAsync(
                        relay.Port, Wire.Head(relay.Port, shape.Line, [.. headers, .. shape.Headers]) + shape.Body);
                    Wire.AssertFixed401(response);
                    answers.Add(response!);
                }
            }

            return answers;
        }

        var idle = await ProbeAsync(relay.Token);
        Assert.Single(idle.Distinct(StringComparer.Ordinal));

        // Paused.
        await relay.Relay.SetAcceptingAsync(false);
        Assert.Equal(idle, await ProbeAsync(relay.Token));
        await relay.Relay.SetAcceptingAsync(true);

        // The answer slot is held by a run that hangs, and the speech cap is full: one stream is admitted,
        // the others are refused and never answer their close.
        relay.Codex.ExecScenario = "hang";
        var hanging = relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody());
        await relay.Codex.ReadExecReportAsync();
        var phones = new List<SpeechPhone> { await relay.OpenReadyStreamAsync() };
        while (phones.Count < ProtocolConstants.MaxSpeechSockets)
        {
            phones.Add(await relay.OpenSpeechAsync());
        }

        using (var fifth = await relay.OpenRawAsync())
        {
            await fifth.SendAsync(relay.RawHead("GET /v1/speech HTTP/1.1", [.. Wire.UpgradeHeaders, ("X-LiveCue-Speech-Model", "nemotron")]));
            Assert.Equal(429, (await fifth.ReadResponseAsync())?.Status);
        }

        Assert.Equal(idle, await ProbeAsync(relay.Token));

        // The gauge is full: C-31 requests whose bodies never arrive.
        var stalled = new List<RawHttp>();
        try
        {
            while (stalled.Count < ProtocolConstants.MaxRequestsInFlight - 1)
            {
                var raw = await relay.OpenRawAsync();
                stalled.Add(raw);
                await raw.SendAsync(relay.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "64")));
            }

            await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == ProtocolConstants.MaxRequestsInFlight, "the gauge is full");
            var (full, _) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/health");
            Assert.Equal(429, full);
            Assert.Equal(idle, await ProbeAsync(relay.Token));
            Assert.Equal(ProtocolConstants.MaxRequestsInFlight, relay.Edge.Gauge.InFlight);
        }
        finally
        {
            stalled.ForEach(raw => raw.Dispose());
        }

        // After a reset the old token is one more token that is not accepted.
        await relay.Relay.ResetPairingAsync(CancellationToken.None);
        Assert.Equal(idle, await ProbeAsync(old));
        var (afterReset, _) = await hanging.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(401, afterReset);
        phones.ForEach(phone => phone.Dispose());
    }

    [Fact]
    public async Task A_401_touches_nothing()
    {
        // S-06. 2,000 refusals of each kind over kept connections: no file, no process, no lab call and no
        // event; exactly the two counters. The medians are recorded, not gated.
        await using var relay = await ConformanceRelay.StartAsync();
        await relay.Events.SettleAsync();
        relay.Events.Clear();
        var filesBefore = Snapshot(relay.DataDirectory);
        const int Samples = 2000;

        async Task<double> MedianAsync(string authorization)
        {
            var request = Wire.Head(relay.Port, "GET /v1/health HTTP/1.1", ("Authorization", authorization), ("X-LiveCue-Protocol", "2"));
            using var raw = await relay.OpenRawAsync();
            var times = new double[Samples];
            for (var sample = 0; sample < Samples; sample++)
            {
                var started = Stopwatch.GetTimestamp();
                await raw.SendAsync(request);
                var response = await Wire.ReadRawAsync(raw);
                times[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Assert.NotNull(response);
                Assert.Equal(Wire.Unauthorized, Wire.BodyOf(response));
            }

            Array.Sort(times);
            return times[Samples / 2];
        }

        var firstCharacterWrong = (relay.Token[0] == 'A' ? "B" : "A") + relay.Token[1..];
        var lastCharacterWrong = relay.Token[..^1] + (relay.Token[^1] == 'A' ? "B" : "A");
        var wrongShape = await MedianAsync("Bearer " + new string('x', 42));
        var rightShape = await MedianAsync(TestTokens.Bearer(TestTokens.WellFormedUnknown));
        var nearMissAtTheStart = await MedianAsync(TestTokens.Bearer(firstCharacterWrong));
        var nearMissAtTheEnd = await MedianAsync(TestTokens.Bearer(lastCharacterWrong));
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Median 401, ms: wrong shape {wrongShape:F4}; well-formed unknown {rightShape:F4}; first character wrong {nearMissAtTheStart:F4}; last character wrong {nearMissAtTheEnd:F4}."));

        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.Snapshot());
        Assert.Empty(relay.Codex.Requests);
        Assert.Equal(0, relay.Lab.ProbeCalls + relay.Lab.StartCalls + relay.Lab.StopCalls);
        Assert.Equal(0, relay.LabSockets.ConnectCalls);
        Assert.Empty(relay.SelfCheck.Calls);
        Assert.Equal(filesBefore, Snapshot(relay.DataDirectory));
        Assert.Equal(Samples, relay.Edge.Malformed);
        Assert.Equal(3 * Samples, relay.Edge.TokenNotAccepted);
        Assert.Equal(0, relay.Edge.Gauge.InFlight);

        // No lockout and no delay either: the accepted token still works at once.
        var (status, _) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/health");
        Assert.Equal(200, status);
    }

    [Theory]
    [InlineData("GET /v1/health?x=1 HTTP/1.1")]
    [InlineData("GET /v1/health? HTTP/1.1")]
    [InlineData("GET /v1/models?cursor=2 HTTP/1.1")]
    [InlineData("POST /v1/assist?model=gpt-stand-in HTTP/1.1")]
    [InlineData("GET /v1/health/ HTTP/1.1")]
    [InlineData("GET /v1/health#x HTTP/1.1")]
    [InlineData("GET /v1/%68ealth HTTP/1.1")]
    [InlineData("GET /v1%2Fhealth HTTP/1.1")]
    [InlineData("GET /V1/Health HTTP/1.1")]
    [InlineData("GET //v1/health HTTP/1.1")]
    [InlineData("GET /v1//health HTTP/1.1")]
    [InlineData("GET /v1/../v1/health HTTP/1.1")]
    [InlineData("GET /livecue/v1/health HTTP/1.1")]
    [InlineData("GET /v1 HTTP/1.1")]
    [InlineData("GET / HTTP/1.1")]
    [InlineData("GET http://{HOST}/v1/health HTTP/1.1")]
    [InlineData("CONNECT {HOST} HTTP/1.1")]
    [InlineData("OPTIONS * HTTP/1.1")]
    [InlineData("HEAD /v1/health HTTP/1.1")]
    [InlineData("PUT /v1/assist HTTP/1.1")]
    [InlineData("PATCH /v1/local-model HTTP/1.1")]
    [InlineData("TRACE /v1/health HTTP/1.1")]
    [InlineData("get /v1/health HTTP/1.1")]
    [InlineData("Post /v1/assist HTTP/1.1")]
    [InlineData("DELETE /v1/requests/ HTTP/1.1")]
    [InlineData("DELETE /v1/requests/a%2Fb HTTP/1.1")]
    [InlineData("DELETE /v1/requests/..%2F..%2Fpair HTTP/1.1")]
    [InlineData("GET /v1/requests/request-0001 HTTP/1.1")]
    [InlineData("POST /v1/speech HTTP/1.1")]
    public async Task Odd_targets_and_methods_are_404(string requestLine)
    {
        // S-07, with the token: never a 405, never a redirect, never a body other than the coded 404.
        await using var relay = await ConformanceRelay.StartAsync();
        requestLine = requestLine.Replace("{HOST}", $"127.0.0.1:{relay.Port}", StringComparison.Ordinal);
        var head = requestLine.StartsWith("HEAD", StringComparison.Ordinal);

        using var raw = await relay.OpenRawAsync();
        await raw.SendAsync(relay.RawHead(requestLine));
        var response = await Wire.ReadRawAsync(raw, hasBody: !head);

        Assert.NotNull(response);
        Assert.Equal("HTTP/1.1 404 Not Found", Wire.StatusLine(response));
        Assert.Equal(head ? string.Empty : Wire.NotFound, Wire.BodyOf(response));
        Assert.All(Wire.HeaderNames(response), name => Assert.Contains(name, Wire.AllowedResponseHeaders));
        Assert.Empty(relay.Codex.Requests);
        Assert.Equal(0, relay.Lab.ProbeCalls);
    }

    [Theory]
    [InlineData("POST", "/desktop/pair")]
    [InlineData("POST", "/pause")]
    [InlineData("POST", "/shutdown")]
    [InlineData("POST", "/v1/pair/reset")]
    [InlineData("POST", "/v1/pair/show")]
    [InlineData("GET", "/v1/pair")]
    [InlineData("GET", "/v1/pairing")]
    [InlineData("POST", "/v1/pause")]
    [InlineData("POST", "/v1/resume")]
    [InlineData("POST", "/v1/stop")]
    [InlineData("POST", "/v1/shutdown")]
    [InlineData("GET", "/v1/events")]
    [InlineData("GET", "/v1/diagnostics")]
    [InlineData("GET", "/v1/settings")]
    [InlineData("PUT", "/v1/settings")]
    [InlineData("DELETE", "/v1/pair")]
    public async Task No_control_route_exists(string method, string target)
    {
        // S-08: what the desktop can do, the network cannot. The token holder gets a 404 and the relay
        // is as it was.
        await using var relay = await ConformanceRelay.StartAsync();
        var token = relay.Token;
        var pairing = await File.ReadAllBytesAsync(Path.Combine(relay.DataDirectory, "pairing.json"));

        var response = await Wire.ExchangeAsync(
            relay.Port,
            relay.RawHead($"{method} {target} HTTP/1.1", ("Content-Length", "2")) + "{}");

        Assert.NotNull(response);
        Assert.Equal("HTTP/1.1 404 Not Found", Wire.StatusLine(response));
        Assert.Equal(Wire.NotFound, Wire.BodyOf(response));
        Assert.True(relay.Relay.Accepting);
        Assert.Equal(RelayStatus.Running, relay.Relay.Status);
        Assert.Equal(token, await relay.ReadTokenAsync());
        Assert.Equal(pairing, await File.ReadAllBytesAsync(Path.Combine(relay.DataDirectory, "pairing.json")));
        await relay.Events.SettleAsync();
        Assert.DoesNotContain(relay.Events.Snapshot(), item => item is AcceptingChanged or PairingShown or Notice or RelayStopped);
    }

    [Theory]
    [InlineData("two headers")]
    [InlineData("2, 2")]
    [InlineData("2,2")]
    [InlineData("02")]
    [InlineData("2.0")]
    [InlineData("+2")]
    [InlineData("")]
    [InlineData("22")]
    [InlineData("1")]
    [InlineData("3")]
    public async Task A_repeated_protocol_header_is_a_mismatch(string form)
    {
        // S-09: exactly one header whose value is exactly "2".
        await using var relay = await ConformanceRelay.StartAsync();
        (string, string)[] protocol = form == "two headers"
            ? [("X-LiveCue-Protocol", "2"), ("X-LiveCue-Protocol", "2")]
            : [("X-LiveCue-Protocol", form)];
        var bearer = ("Authorization", TestTokens.Bearer(relay.Token));

        foreach (var route in Wire.HttpRoutes)
        {
            using var raw = await relay.OpenRawAsync();
            var head = RawHttp.Head($"{route.Method} {Wire.TargetOf(route)} HTTP/1.1", relay.Port, [bearer, .. protocol]);
            await raw.SendAsync(System.Text.Encoding.UTF8.GetBytes(head));
            var response = await Wire.ReadRawAsync(raw);

            Assert.NotNull(response);
            Assert.Equal("HTTP/1.1 400 Bad Request", Wire.StatusLine(response));
            Assert.Equal(Wire.ProtocolMismatch, Wire.BodyOf(response));
        }

        // An unknown route stays 404, whatever the header says (P2-007).
        var unknown = await Wire.ExchangeAsync(relay.Port, RawHttp.Head("GET /v1/nowhere HTTP/1.1", relay.Port, [bearer, .. protocol]));
        Assert.Equal("HTTP/1.1 404 Not Found", Wire.StatusLine(unknown!));
        Assert.Empty(relay.Codex.Requests);
    }

    [Fact]
    public async Task The_self_check_header_is_no_credential_and_a_wrong_nonce_is_a_phone()
    {
        // S-10. While a self-check is under way its nonce is "current". Knowing it buys nothing: without
        // the token the answer is the fixed 401, and with the token a wrong or stale nonce is a phone.
        await using var relay = await ConformanceRelay.StartAsync();
        relay.SelfCheck.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await relay.Relay.ShowPairingAsync(CancellationToken.None);
        await Wire.UntilAsync(() => relay.SelfCheck.Calls.Count == 1, "the self-check began");
        var nonce = relay.SelfCheck.Calls[0].Nonce;
        await relay.Events.SettleAsync();
        relay.Events.Clear();

        // The current nonce without the token, and with a token that is not accepted.
        Wire.AssertFixed401(await Wire.ExchangeAsync(
            relay.Port, Wire.Head(relay.Port, "GET /v1/health HTTP/1.1", ("X-LiveCue-Self-Check", nonce), ("X-LiveCue-Protocol", "2"))));
        Wire.AssertFixed401(await Wire.ExchangeAsync(
            relay.Port,
            Wire.Head(
                relay.Port,
                "GET /v1/health HTTP/1.1",
                ("Authorization", TestTokens.Bearer(TestTokens.WellFormedUnknown)),
                ("X-LiveCue-Self-Check", nonce),
                ("X-LiveCue-Protocol", "2"))));
        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>());

        // The current nonce with the token: the one request that is not a phone.
        var current = await Wire.ExchangeAsync(relay.Port, relay.RawHead("GET /v1/health HTTP/1.1", ("X-LiveCue-Self-Check", nonce)));
        Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(current!));
        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>());

        // A wrong nonce, the nonce twice, and the nonce on another route's refusal: each is a phone.
        (string, string)[][] others =
        [
            [("X-LiveCue-Self-Check", new string('0', nonce.Length))],
            [("X-LiveCue-Self-Check", nonce + "x")],
            [("X-LiveCue-Self-Check", nonce), ("X-LiveCue-Self-Check", nonce)],
            [("X-LiveCue-Self-Check", string.Empty)],
        ];
        foreach (var headers in others)
        {
            var response = await Wire.ExchangeAsync(relay.Port, relay.RawHead("GET /v1/health HTTP/1.1", headers));
            Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(response!));
        }

        await relay.Events.SettleAsync();
        Assert.Equal(others.Length, relay.Events.Count<PhoneSeen>());

        // Once the check is over the nonce is stale: a request that carries it is a phone again.
        relay.SelfCheck.Gate.SetResult();
        await relay.Events.WaitForAsync<SelfCheckCompleted>();
        relay.Events.Clear();
        var stale = await Wire.ExchangeAsync(relay.Port, relay.RawHead("GET /v1/health HTTP/1.1", ("X-LiveCue-Self-Check", nonce)));
        Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(stale!));
        await relay.Events.WaitForAsync<PhoneSeen>();
    }

    [Fact]
    public async Task An_unhandled_exception_is_the_fixed_500()
    {
        // S-41. A collaborator that breaks in a way no area expects: here the process runner itself. The
        // caller gets the fixed 500; the diagnostic names the exception's type and nothing of its text;
        // nothing is left held.
        await using var relay = await ConformanceRelay.StartAsync();
        relay.Codex.Fault = _ => new InvalidOperationException(PrivateText.Canary + " at " + relay.DataDirectory);

        foreach (var (target, json) in ((string, string?)[])
        [
            ("/v1/models", null),
            ("/v1/assist", ConformanceRelay.AnswerBody()),
            ("/v1/session-summary", ConformanceRelay.AnswerBody()),
        ])
        {
            var response = await Wire.ExchangeAsync(
                relay.Port,
                json is null ? relay.RawHead($"GET {target} HTTP/1.1") : relay.RawPost(target, json));
            Assert.NotNull(response);
            Assert.Equal("HTTP/1.1 500 Internal Server Error", Wire.StatusLine(response));
            Assert.Equal(Wire.InternalError, Wire.BodyOf(response));
            Assert.All(Wire.HeaderNames(response), name => Assert.Contains(name, Wire.AllowedResponseHeaders));
        }

        await relay.Events.SettleAsync();
        var diagnostics = relay.Events.OfType<Diagnostic>();
        Assert.Equal(3, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal("A request failed unexpectedly. (InvalidOperationException)", diagnostic.Message));
        PrivateText.AssertAbsent(relay, relay.Events.Snapshot().Select(PrivateText.Serialise));

        // The slot and the gauge are free, and the relay still runs: the next request is served.
        relay.Codex.Fault = null;
        Assert.Equal(0, relay.Edge.Gauge.InFlight);
        var (status, _) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody());
        Assert.Equal(200, status);
        Assert.Equal(RelayStatus.Running, relay.Relay.Status);
    }

    [Fact]
    public async Task Forwarding_headers_and_cookies_are_ignored()
    {
        // S-42. What a proxy or a caller may add changes nothing: not who the caller is, not the route,
        // not the answer; and nothing of it reaches an event, the prompt or the Codex process.
        await using var relay = await ConformanceRelay.StartAsync();
        const string Marker = "forwarded-canary-7f3a";
        (string, string)[] noise =
        [
            ("X-Forwarded-For", "203.0.113.9, " + Marker),
            ("X-Forwarded-Host", Marker + ".example.test"),
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-Prefix", "/" + Marker),
            ("Forwarded", "for=203.0.113.9;host=" + Marker),
            ("X-Real-IP", "203.0.113.9"),
            ("X-Original-URL", "/v1/pair/reset?" + Marker),
            ("X-Rewrite-URL", "/desktop/pair"),
            ("X-HTTP-Method-Override", "DELETE"),
            ("Tailscale-User-Login", Marker + "@example.test"),
            ("Tailscale-User-Name", Marker),
            ("Cookie", "session=" + Marker),
            ("User-Agent", Marker),
            ("Referer", "https://" + Marker + ".example.test/"),
            ("X-LiveCue-Speech-Model", "../" + Marker),
            ("Accept-Language", Marker),
        ];

        var plain = await Wire.ExchangeAsync(relay.Port, relay.RawPost("/v1/assist", ConformanceRelay.AnswerBody("request-0001")));
        var noisy = await Wire.ExchangeAsync(relay.Port, relay.RawPost("/v1/assist", ConformanceRelay.AnswerBody("request-0002"), noise));
        var withoutToken = await Wire.ExchangeAsync(
            relay.Port, Wire.Head(relay.Port, "GET /v1/health HTTP/1.1", [("X-LiveCue-Protocol", "2"), .. noise]));
        var health = await Wire.ExchangeAsync(relay.Port, relay.RawHead("GET /v1/health HTTP/1.1", noise));

        Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(plain!));
        Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(noisy!));
        Assert.Equal(Wire.HeaderNames(plain!), Wire.HeaderNames(noisy!));
        Wire.AssertFixed401(withoutToken);
        Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(health!));

        // Nothing of it in the two Codex runs: same arguments but for the folder, same prompt but for the id.
        var first = await relay.Codex.ReadExecReportAsync(0);
        var second = await relay.Codex.ReadExecReportAsync(1);
        Assert.Equal(first.Prompt.Replace("request-0001", "request-0002", StringComparison.Ordinal), second.Prompt);
        Assert.Equal(first.EnvironmentNames, second.EnvironmentNames);
        Assert.DoesNotContain(second.Arguments, argument => argument.Contains(Marker, StringComparison.Ordinal));
        Assert.Equal(first.Arguments.Count, second.Arguments.Count);

        // Nothing of it in an event, and no client address anywhere.
        await relay.Events.SettleAsync();
        foreach (var text in relay.Events.Snapshot().Select(PrivateText.Serialise))
        {
            Assert.DoesNotContain(Marker, text, StringComparison.Ordinal);
            Assert.DoesNotContain("203.0.113.9", text, StringComparison.Ordinal);
            Assert.DoesNotContain("127.0.0.1", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(ConnectionMode.Private)]
    [InlineData(ConnectionMode.Public)]
    public async Task A_funnel_marked_request_follows_the_mode_on_every_route(ConnectionMode mode)
    {
        // S-04 over the whole table: Private refuses with the fixed 401 and counts; Public serves.
        await using var relay = await ConformanceRelay.StartAsync(options => options.Mode = mode);
        foreach (var value in (string[])["?1", "?0", "", "1"])
        {
            foreach (var route in Wire.HttpRoutes.Where(route => !route.ReadsBody))
            {
                var response = await Wire.ExchangeAsync(
                    relay.Port,
                    relay.RawHead($"{route.Method} {Wire.TargetOf(route)} HTTP/1.1", ("Tailscale-Funnel-Request", value)));
                if (mode == ConnectionMode.Private)
                {
                    Wire.AssertFixed401(response);
                }
                else
                {
                    Assert.DoesNotMatch("^HTTP/1.1 401", Wire.StatusLine(response!));
                }
            }
        }

        var refused = 4 * Wire.HttpRoutes.Count(route => !route.ReadsBody);
        Assert.Equal(mode == ConnectionMode.Private ? refused : 0, relay.Edge.FunnelInPrivateMode);

        // Without the token the two modes answer alike: a caller from outside cannot tell them apart.
        Wire.AssertFixed401(await Wire.ExchangeAsync(
            relay.Port, Wire.Head(relay.Port, "GET /v1/health HTTP/1.1", ("Tailscale-Funnel-Request", "?1"))));
    }

    /// <summary>Every file under a folder with its length and its last write, for "nothing was touched".</summary>
    private static List<string> Snapshot(string folder) =>
    [
        .. Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetRelativePath(folder, path)}|{(File.Exists(path) ? new FileInfo(path).Length : -1)}|{File.GetLastWriteTimeUtc(path).Ticks}")),
    ];
}
