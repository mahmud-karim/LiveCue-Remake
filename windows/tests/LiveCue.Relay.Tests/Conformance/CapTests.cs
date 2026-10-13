using System.Globalization;
using System.Text.Json;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.Processes;
using LiveCue.Relay.Tests.Speech;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>
/// Test plan S-25 to S-28: the caps of protocol 2.3 against a client that holds the token and misbehaves.
/// It can make the relay busy up to each cap and no further, and every place it took comes back.
/// </summary>
public sealed class CapTests
{
    [Fact]
    public async Task The_connection_cap_counts_plain_and_upgraded_connections()
    {
        // S-25. Four speech sockets and sixty plain connections are C-30. One more is dropped at once,
        // without a byte; the ones that are open keep working; a closed one gives its place back.
        var clock = new ManualTimeProvider();
        await using var relay = await ConformanceRelay.StartAsync(options => options.Clock = clock);
        var phones = new List<SpeechPhone> { await relay.OpenReadyStreamAsync() };
        var plain = new List<RawHttp>();
        try
        {
            while (phones.Count < ProtocolConstants.MaxSpeechSockets)
            {
                phones.Add(await relay.OpenSpeechAsync());
            }

            while (plain.Count < ProtocolConstants.MaxConnections - ProtocolConstants.MaxSpeechSockets)
            {
                plain.Add(await relay.OpenRawAsync());
            }

            await Wire.UntilAsync(() => relay.Edge.Connections.Open == ProtocolConstants.MaxConnections, "the cap is reached");

            // Ten more, each with a complete, valid request: none is answered.
            for (var extra = 0; extra < 10; extra++)
            {
                using var beyond = await relay.OpenRawAsync();
                var reading = Wire.ReadRawAsync(beyond, timeout: TimeSpan.FromSeconds(15));
                try
                {
                    await beyond.SendAsync(relay.RawHead("GET /v1/health HTTP/1.1"));
                }
                catch (IOException)
                {
                }

                Assert.Null(await reading);
            }

            Assert.Equal(ProtocolConstants.MaxConnections, relay.Edge.Connections.Open);

            // The open ones work: a request on a plain connection, and audio on the stream.
            await plain[0].SendAsync(relay.RawHead("GET /v1/health HTTP/1.1"));
            Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine((await Wire.ReadRawAsync(plain[0]))!));
            await phones[0].SendAudioAsync(2560);
            Assert.Equal("""{"type":"transcript","transcript":"Local words"}""", await phones[0].ReceiveTextAsync());

            // One closes; one more is served.
            plain[^1].Dispose();
            plain.RemoveAt(plain.Count - 1);
            await Wire.UntilAsync(() => relay.Edge.Connections.Open == ProtocolConstants.MaxConnections - 1, "a place is free");
            var (status, _) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/health");
            Assert.Equal(200, status);
        }
        finally
        {
            plain.ForEach(raw => raw.Dispose());
            phones.ForEach(phone => phone.Dispose());
        }

        relay.DropClientConnections();
        await Wire.UntilAsync(() => relay.Edge.Connections.Open == 0, "the connections are gone");
        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<Diagnostic>());
    }

    [Fact]
    public async Task The_gauge_never_leaks()
    {
        // S-26. A thousand requests of every kind, eight at a time: served, refused at every step, broken
        // off by the caller before the body, in the middle of it and during a run, cancelled, failing.
        // Afterwards nothing is held: no place at the gauge, no answer slot, no connection, no process.
        await using var relay = await ConformanceRelay.StartAsync();
        var bearer = TestTokens.Bearer(relay.Token);
        var kinds = 0;

        // What a request of the storm must be answered with. When callers that went away are still being
        // noticed, the gauge can be full for a moment: its 429 is a correct answer to any of them.
        var atTheGauge = 0;
        void Expect(int status, (int Status, string Body) answer)
        {
            if (answer.Status == 429 && answer.Body == Wire.TooManyRequests)
            {
                Interlocked.Increment(ref atTheGauge);
                return;
            }

            Assert.Equal(status, answer.Status);
        }

        async Task OneAsync(int index)
        {
            switch (index % 20)
            {
                case 0:
                    Expect(200, await relay.ExchangeAsync(HttpMethod.Get, "/v1/health"));
                    break;
                case 1:
                    Expect(200, await relay.ExchangeAsync(HttpMethod.Post, "/v1/pair/verify", "{}"));
                    break;
                case 2:
                    Expect(200, await relay.ExchangeAsync(HttpMethod.Get, "/v1/models"));
                    break;
                case 3:
                    Expect(200, await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model"));
                    break;
                case 4:
                    Expect(404, await relay.ExchangeAsync(HttpMethod.Get, "/v1/nowhere"));
                    break;
                case 5:
                    Expect(404, await relay.ExchangeAsync(HttpMethod.Delete, "/v1/requests/nobody"));
                    break;
                case 6:
                    Expect(400, await relay.ExchangeAsync(relay.Request(HttpMethod.Get, "/v1/health", protocol: "1")));
                    break;
                case 7:
                    Expect(400, await relay.ExchangeAsync(HttpMethod.Post, "/v1/pair/verify", "["));
                    break;
                case 8:
                    Expect(400, await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/start", """{"model":"../x"}"""));
                    break;
                case 9:
                    // Refused for its id, or before that because another request holds the answer slot.
                    var refused = await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", """{"requestId":"a b"}""");
                    Assert.True(refused.Status is 400 or 429, $"A bad request id ended {refused.Status}.");
                    break;
                case 10:
                    Expect(401, await relay.ExchangeAsync(relay.Request(HttpMethod.Get, "/v1/models", token: TestTokens.WellFormedUnknown)));
                    break;
                case 11:
                    Expect(400, await relay.ExchangeAsync(HttpMethod.Get, "/v1/health", "{}"));
                    break;
                case 12:
                    // A declared length over the limit: refused from the header.
                    using (var raw = await relay.OpenRawAsync())
                    {
                        await raw.SendAsync(relay.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "200000")));
                        Assert.Equal(413, (await raw.ReadResponseAsync())?.Status);
                    }

                    break;
                case 13:
                    // The caller goes away with half a body sent.
                    using (var raw = await relay.OpenRawAsync())
                    {
                        await raw.SendAsync(relay.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "64")) + "{\"a\":");
                    }

                    break;
                case 14:
                    // The caller goes away with only half a head sent.
                    using (var raw = await relay.OpenRawAsync())
                    {
                        await raw.SendAsync($"POST /v1/assist HTTP/1.1\r\nHost: 127.0.0.1:{relay.Port}\r\nAuthorization: {bearer}\r\n");
                    }

                    break;
                case 15:
                    // The caller sends its request and never reads the answer.
                    using (var raw = await relay.OpenRawAsync())
                    {
                        await raw.SendAsync(relay.RawHead("GET /v1/models HTTP/1.1"));
                    }

                    break;
                case 16:
                    // A chunked body that breaks off in the middle of a chunk.
                    using (var raw = await relay.OpenRawAsync())
                    {
                        await raw.SendAsync(relay.RawHead("POST /v1/local-model/stop HTTP/1.1", ("Transfer-Encoding", "chunked")) + "40\r\n{\"model\":");
                    }

                    break;
                case 17:
                    // An upgrade that is refused before it is one, and one that is not an upgrade at all.
                    using (var raw = await relay.OpenRawAsync())
                    {
                        await raw.SendAsync(relay.RawHead("GET /v1/speech HTTP/1.1"));
                        Assert.Equal(404, (await raw.ReadResponseAsync())?.Status);
                    }

                    break;
                case 18:
                    // Twenty requests on one connection, the last of them refused with a body unread.
                    using (var raw = await relay.OpenRawAsync())
                    {
                        for (var repeat = 0; repeat < 19; repeat++)
                        {
                            await raw.SendAsync(relay.RawHead("GET /v1/health HTTP/1.1"));
                            Assert.Equal(200, (await raw.ReadResponseAsync())?.Status);
                        }

                        await raw.SendAsync(relay.RawHead("POST /v1/nowhere HTTP/1.1", ("Content-Length", "10")));
                        Assert.Equal(404, (await raw.ReadResponseAsync())?.Status);
                    }

                    break;
                default:
                    // An answer request: served, or refused because another one holds the slot.
                    var (status, _) = await relay.ExchangeAsync(
                        HttpMethod.Post,
                        index % 40 == 19 ? "/v1/assist" : "/v1/session-summary",
                        ConformanceRelay.AnswerBody("request-" + index.ToString(CultureInfo.InvariantCulture)));
                    Assert.True(status is 200 or 429, $"An answer request ended {status}.");
                    break;
            }

            Interlocked.Increment(ref kinds);
        }

        var next = -1;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            int index;
            while ((index = Interlocked.Increment(ref next)) < 1000)
            {
                await OneAsync(index);
            }
        }));
        Assert.Equal(1000, kinds);
        Assert.InRange(atTheGauge, 0, 100);

        // A run that is cancelled, a run whose caller goes away, and a run that fails.
        relay.Codex.ExecScenario = "hang";
        var first = relay.Codex.ExecReportFiles.Count;
        var cancelled = relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody("request-cancel"));
        var report = await relay.Codex.ReadExecReportAsync(first);
        Assert.Equal(200, (await relay.ExchangeAsync(HttpMethod.Delete, "/v1/requests/request-cancel")).Status);
        Assert.Equal(409, (await cancelled).Status);
        await TestChild.WaitUntilGoneAsync(report.ProcessId);

        using (var leaving = await relay.OpenRawAsync())
        {
            await leaving.SendAsync(relay.RawPost("/v1/assist", ConformanceRelay.AnswerBody("request-leave")));
            report = await relay.Codex.ReadExecReportAsync(first + 1);
        }

        await TestChild.WaitUntilGoneAsync(report.ProcessId);
        relay.Codex.ExecScenario = "fail";
        await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == 0, "the gauge is empty");
        Assert.Equal(500, (await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody("request-fail"))).Status);

        // Nothing is held.
        relay.Codex.ExecScenario = "ok";
        await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == 0, "the gauge is empty");
        relay.DropClientConnections();
        await Wire.UntilAsync(() => relay.Edge.Connections.Open == 0, "the connections are gone", TimeSpan.FromSeconds(30));
        Assert.Equal(200, (await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody("request-last"))).Status);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(relay.DataDirectory, "work")));
        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<Diagnostic>());
        Assert.Equal(RelayStatus.Running, relay.Relay.Status);
    }

    [Fact]
    public async Task One_request_beyond_the_gauge_is_429_whatever_route_it_names()
    {
        // S-26, the cap itself: C-31 requests held in flight by bodies that do not arrive. Every further
        // request is 429 on every route, with the connection closed when it declared a body; one place
        // back, one request served.
        await using var relay = await ConformanceRelay.StartAsync();
        var held = new List<RawHttp>();
        try
        {
            while (held.Count < ProtocolConstants.MaxRequestsInFlight)
            {
                var raw = await relay.OpenRawAsync();
                held.Add(raw);
                await raw.SendAsync(relay.RawHead("POST /v1/local-model/start HTTP/1.1", ("Content-Length", "64")));
            }

            await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == ProtocolConstants.MaxRequestsInFlight, "the gauge is full");
            foreach (var route in Wire.HttpRoutes)
            {
                var (status, body) = await relay.ExchangeAsync(relay.RequestFor(route));
                Assert.Equal(429, status);
                Assert.Equal(Wire.TooManyRequests, body);
            }

            // An unknown route is still 404 and a caller without the token still gets the fixed 401: the
            // gauge is the seventh step, not the first.
            Assert.Equal(404, (await relay.ExchangeAsync(HttpMethod.Get, "/v1/nowhere")).Status);
            Wire.AssertFixed401(await Wire.ExchangeAsync(relay.Port, Wire.Head(relay.Port, "GET /v1/health HTTP/1.1")));
            Assert.Empty(relay.Codex.Requests);

            held[0].Dispose();
            held.RemoveAt(0);
            await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight < ProtocolConstants.MaxRequestsInFlight, "a place is free");
            Assert.Equal(200, (await relay.ExchangeAsync(HttpMethod.Get, "/v1/health")).Status);
        }
        finally
        {
            held.ForEach(raw => raw.Dispose());
        }

        await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == 0, "the gauge is empty");
    }

    [Fact]
    public async Task Refused_sockets_count_until_closed()
    {
        // S-27. Four sockets are refused (the relay is paused) and never answer the close: they are open
        // sockets all the same, so a fifth upgrade is HTTP 429. They get their places back when the relay
        // drops them at C-58.
        var clock = new ManualTimeProvider();
        await using var relay = await ConformanceRelay.StartAsync(options => options.Clock = clock);
        await relay.Relay.SetAcceptingAsync(false);
        var phones = new List<SpeechPhone>();
        try
        {
            while (phones.Count < ProtocolConstants.MaxSpeechSockets)
            {
                var phone = await relay.OpenSpeechAsync();
                phones.Add(phone);
                Assert.Equal("RELAY_PAUSED", (await phone.ExpectAsync("error")).GetProperty("code").GetString());
            }

            (string, string)[] upgrade = [.. Wire.UpgradeHeaders, ("X-LiveCue-Speech-Model", "nemotron")];
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var refused = await Wire.ExchangeAsync(relay.Port, relay.RawHead("GET /v1/speech HTTP/1.1", upgrade));
                Assert.Equal("HTTP/1.1 429 Too Many Requests", Wire.StatusLine(refused!));
                Assert.Equal(Wire.TooManyRequests, Wire.BodyOf(refused!));
            }

            // Without the token the cap cannot even be seen.
            Wire.AssertFixed401(await Wire.ExchangeAsync(relay.Port, Wire.Head(relay.Port, "GET /v1/speech HTTP/1.1", upgrade)));

            // The HTTP routes do not share this cap.
            Assert.Equal(200, (await relay.ExchangeAsync(HttpMethod.Get, "/v1/health")).Status);

            await relay.Relay.SetAcceptingAsync(true);
            clock.Advance(ProtocolConstants.CloseHandshakeWait);
            var patience = System.Diagnostics.Stopwatch.StartNew();
            SpeechPhone? admitted = null;
            while (admitted is null)
            {
                try
                {
                    admitted = await relay.OpenReadyStreamAsync();
                }
                catch (System.Net.WebSockets.WebSocketException)
                {
                    Assert.True(patience.Elapsed < TimeSpan.FromSeconds(15), "The places were not given back.");
                    await Task.Delay(10);
                }
            }

            admitted.Dispose();
        }
        finally
        {
            phones.ForEach(phone => phone.Dispose());
        }

        await Wire.UntilAsync(() => relay.LabSockets.Sockets.All(socket => socket.Disposed), "every lab socket was let go");
    }

    [Fact]
    public async Task A_storm_of_answer_requests_and_cancels_runs_one_process_at_a_time()
    {
        // S-28. One answer request runs (one real process). Sixty more, twelve at a time, are each
        // ASSISTANT_BUSY. A storm of cancels for ids nobody holds changes nothing; cancels for the running
        // id end it once. (Twelve at a time, so that the storm itself stays below the caps C-30 and C-31
        // and it is the answer slot that refuses.)
        await using var relay = await ConformanceRelay.StartAsync();
        relay.Codex.ExecScenario = "hang";
        const string RunningId = "storm-running";
        var running = relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody(RunningId));
        var report = await relay.Codex.ReadExecReportAsync();

        static async Task<List<T>> StormAsync<T>(int count, Func<int, Task<T>> one)
        {
            var results = new System.Collections.Concurrent.ConcurrentBag<T>();
            var next = -1;
            await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            {
                int index;
                while ((index = Interlocked.Increment(ref next)) < count)
                {
                    results.Add(await one(index));
                }
            }));
            return [.. results];
        }

        var refused = await StormAsync(60, index => relay.ExchangeAsync(
            HttpMethod.Post,
            index % 2 == 0 ? "/v1/assist" : "/v1/session-summary",
            ConformanceRelay.AnswerBody(index % 5 == 0 ? RunningId : "storm-" + index.ToString(CultureInfo.InvariantCulture))));
        Assert.All(refused, answer =>
        {
            Assert.Equal(429, answer.Status);
            Assert.Equal("""{"error":"The PC is processing another request. Try again shortly.","code":"ASSISTANT_BUSY"}""", answer.Body);
        });
        Assert.Single(relay.Codex.ExecRequests);
        Assert.False(running.IsCompleted);

        // Cancels for ids that were refused, are unknown, or are close to the running one.
        var misses = await StormAsync(200, index => relay.ExchangeAsync(
            HttpMethod.Delete,
            "/v1/requests/" + (index % 3) switch
            {
                0 => RunningId + "x",
                1 => RunningId.ToUpperInvariant(),
                _ => "storm-" + index.ToString(CultureInfo.InvariantCulture),
            }));
        Assert.All(misses, miss => Assert.Equal(404, miss.Status));
        Assert.All(misses, miss => Assert.Contains("REQUEST_NOT_FOUND", miss.Body, StringComparison.Ordinal));
        Assert.False(running.IsCompleted);
        Assert.False(TestChild.IsGone(report.ProcessId));

        // Twenty cancels for the running id: at least one says "cancelled", the rest say so too or find
        // nothing; the request ends 409 once and the process is gone.
        var hits = await StormAsync(20, _ => relay.ExchangeAsync(HttpMethod.Delete, "/v1/requests/" + RunningId));
        Assert.Contains(hits, hit => hit.Status == 200);
        Assert.All(hits, hit => Assert.True(hit.Status is 200 or 404));
        Assert.Equal(409, (await running.WaitAsync(TimeSpan.FromSeconds(30))).Status);
        await TestChild.WaitUntilGoneAsync(report.ProcessId);

        // The slot is free; the events tell of one request, started once and failed once.
        relay.Codex.ExecScenario = "ok";
        Assert.Equal(200, (await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody("after-the-storm"))).Status);
        await relay.Events.SettleAsync();
        Assert.Equal(2, relay.Events.Count<RequestStarted>());
        Assert.Equal("REQUEST_CANCELLED", Assert.Single(relay.Events.OfType<RequestFailed>()).Code);
        Assert.Equal(2, relay.Codex.ExecRequests.Count);
        await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == 0, "the gauge is empty");
    }
}

/// <summary>
/// Protocol 6.9: each route answers only with the statuses and codes listed for it, whatever state the
/// relay is in and whatever the request holds.
/// </summary>
public sealed class RouteStatusSweepTests
{
    private static readonly string[] Common =
        ["401 UNAUTHORIZED", "404 NOT_FOUND", "400 PROTOCOL_MISMATCH", "400 INVALID_REQUEST", "429 TOO_MANY_REQUESTS", "504 DEADLINE_EXCEEDED"];

    private static readonly string[] Body = ["413 PAYLOAD_TOO_LARGE", "408 REQUEST_TIMEOUT", "400 INVALID_JSON"];

    private static readonly string[] ModelCommand =
    [
        "202", "200", "503 RELAY_PAUSED", "400 INVALID_MODEL", "503 LAB_NOT_CONFIGURED", "409 MODEL_OPERATION_IN_PROGRESS", "409 MODEL_BUSY", .. Body,
    ];

    private static readonly string[] Answer =
    [
        "200", "503 RELAY_PAUSED", "429 ASSISTANT_BUSY", "400 INVALID_REQUEST_ID", "400 INVALID_ASSISTANT", "503 ASSISTANT_CATALOG_UNAVAILABLE",
        "400 ASSISTANT_SELECTION_UNAVAILABLE", "504 ASSISTANT_TIMEOUT", "409 REQUEST_CANCELLED", "500 ASSISTANT_FAILED", .. Body,
    ];

    private static readonly Dictionary<RouteId, string[]> Listed = new()
    {
        [RouteId.Health] = ["200"],
        [RouteId.PairVerify] = ["200", .. Body],
        [RouteId.Models] = ["200", "503 ASSISTANT_CATALOG_UNAVAILABLE"],
        [RouteId.LocalModelStatus] = ["200"],
        [RouteId.LocalModelStart] = ModelCommand,
        [RouteId.LocalModelStop] = [.. ModelCommand, "409 MODEL_MISMATCH"],
        [RouteId.Assist] = Answer,
        [RouteId.SessionSummary] = Answer,
        [RouteId.CancelRequest] = ["200", "404 REQUEST_NOT_FOUND"],
    };

    [Theory]
    [InlineData("idle")]
    [InlineData("paused")]
    [InlineData("an answer in flight")]
    [InlineData("a model operation in flight")]
    [InlineData("a stream open")]
    [InlineData("another model loaded")]
    [InlineData("the lab busy")]
    [InlineData("the catalogue failing")]
    [InlineData("codex failing")]
    [InlineData("nothing installed")]
    public async Task Each_route_returns_only_the_statuses_listed_for_it(string state)
    {
        await using var relay = await ConformanceRelay.StartAsync(options =>
        {
            if (state == "nothing installed")
            {
                options.CodexFound = false;
                options.LabConfigured = false;
            }
        });

        Task<(int Status, string Body)>? inFlight = null;
        SpeechPhone? phone = null;
        switch (state)
        {
            case "paused":
                await relay.Relay.SetAcceptingAsync(false);
                break;
            case "an answer in flight":
                relay.Codex.ExecScenario = "hang";
                inFlight = relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody("request-held"));
                await relay.Codex.ReadExecReportAsync();
                break;
            case "a model operation in flight":
                relay.Lab.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Assert.Equal(202, (await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/start", Bodies.LocalModel("qwen3"))).Status);
                break;
            case "a stream open":
                phone = await relay.OpenReadyStreamAsync();
                break;
            case "another model loaded":
                relay.Lab.LoadedModel = "qwen3";
                break;
            case "the lab busy":
                relay.Lab.LoadedModel = "nemotron";
                relay.Lab.Busy = true;
                break;
            case "the catalogue failing":
                relay.Codex.CatalogScenario = "early-exit";
                break;
            case "codex failing":
                relay.Codex.ExecScenario = "fail";
                break;
        }

        var seen = new Dictionary<RouteId, HashSet<string>>();
        using (phone)
        {
            foreach (var route in Wire.HttpRoutes)
            {
                var outcomes = seen[route.Id] = [];
                var method = new HttpMethod(route.Method);
                var target = Wire.TargetOf(route);
                var requests = new List<HttpRequestMessage>
                {
                    relay.RequestFor(route),
                    relay.Request(method, target, route.ReadsBody ? "{}" : null, protocol: "3"),
                    relay.Request(method, target, route.ReadsBody ? "{}" : null, token: TestTokens.WellFormedUnknown),
                };
                if (route.ReadsBody)
                {
                    requests.AddRange(
                    [
                        relay.Request(method, target, "{}"),
                        relay.Request(method, target, "[]"),
                        relay.Request(method, target, string.Empty),
                        relay.Request(method, target, """{"model":"qwen3"}"""),
                        relay.Request(method, target, """{"model":"../evil"}"""),
                        relay.Request(method, target, """{"requestId":"a b","assistant":7}"""),
                        relay.Request(method, target, """{"requestId":"request-0002","assistant":7}"""),
                        relay.Request(method, target, """{"requestId":"request-0002","assistant":{"model":"gpt-other","reasoningEffort":"low"}}"""),
                        relay.Request(method, target, ConformanceRelay.AnswerBody("request-0003")),
                    ]);
                }
                else
                {
                    requests.Add(relay.Request(method, target, "{}"));
                }

                foreach (var request in requests)
                {
                    if (route.Id is RouteId.LocalModelStart or RouteId.LocalModelStop && state != "a model operation in flight")
                    {
                        // One model command at a time, so that what a command meets is the state and not
                        // the tail of the command before it.
                        await EndToEndRouteTests.UntilSettledAsync(relay);
                    }

                    var (status, body) = await relay.ExchangeAsync(request);
                    var outcome = status.ToString(CultureInfo.InvariantCulture);
                    if (status >= 400)
                    {
                        using var error = JsonDocument.Parse(body);
                        outcome += " " + error.RootElement.GetProperty("code").GetString();
                    }

                    outcomes.Add(outcome);
                    Assert.True(
                        Listed[route.Id].Contains(outcome) || Common.Contains(outcome),
                        $"Route {(int)route.Id} answered '{outcome}' while {state}; protocol 6.9 does not list it.");
                }

                // What each route always does, whatever the state.
                Assert.Contains("400 PROTOCOL_MISMATCH", outcomes);
                Assert.Contains("401 UNAUTHORIZED", outcomes);
            }
        }

        // The state did show: its own code was seen where the protocol puts it.
        var (where, expected) = state switch
        {
            "paused" => (RouteId.Assist, "503 RELAY_PAUSED"),
            "an answer in flight" => (RouteId.SessionSummary, "429 ASSISTANT_BUSY"),
            "a model operation in flight" => (RouteId.LocalModelStop, "409 MODEL_OPERATION_IN_PROGRESS"),
            "a stream open" => (RouteId.LocalModelStop, "409 MODEL_BUSY"),
            "another model loaded" => (RouteId.LocalModelStop, "409 MODEL_MISMATCH"),
            "the lab busy" => (RouteId.LocalModelStart, "409 MODEL_BUSY"),
            "the catalogue failing" => (RouteId.Models, "503 ASSISTANT_CATALOG_UNAVAILABLE"),
            "codex failing" => (RouteId.Assist, "500 ASSISTANT_FAILED"),
            "nothing installed" => (RouteId.LocalModelStart, "503 LAB_NOT_CONFIGURED"),
            _ => (RouteId.Assist, "200"),
        };
        Assert.Contains(expected, seen[where]);

        if (inFlight is not null)
        {
            Assert.Equal(200, (await relay.ExchangeAsync(HttpMethod.Delete, "/v1/requests/request-held")).Status);
            Assert.Equal(409, (await inFlight.WaitAsync(TimeSpan.FromSeconds(30))).Status);
        }

        relay.Lab.StartGate?.TrySetResult();
        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<Diagnostic>());
    }
}
