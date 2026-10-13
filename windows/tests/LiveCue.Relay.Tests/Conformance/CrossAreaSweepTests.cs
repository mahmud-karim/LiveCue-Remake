using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.Processes;
using LiveCue.Relay.Tests.Speech;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>Protocol P2-051 across every area at once: what a paused relay refuses and what it serves.</summary>
public sealed class PauseSweepTests
{
    [Fact]
    public async Task Paused_refuses_routes_5_to_8_and_serves_1_2_3_4_and_9()
    {
        await using var relay = await ConformanceRelay.StartAsync();
        await relay.Relay.SetAcceptingAsync(false);

        foreach (var route in Wire.HttpRoutes)
        {
            var (status, body) = await relay.ExchangeAsync(relay.RequestFor(route));
            if (route.ServedWhilePaused)
            {
                Assert.True(
                    status is 200 || (route.Id == RouteId.CancelRequest && status == 404),
                    $"Route {(int)route.Id} was not served while paused: {status} {body}");
                Assert.DoesNotContain("RELAY_PAUSED", body, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(503, status);
                Assert.Equal("""{"error":"PC relay is paused. Resume it in LiveCue Desktop.","code":"RELAY_PAUSED"}""", body);
            }
        }

        // The table and the statement agree on which routes those are.
        Assert.Equal(
            [5, 6, 7, 8],
            Wire.HttpRoutes.Where(route => !route.ServedWhilePaused).Select(route => (int)route.Id));

        // Nothing was started for a refused request: no Codex run, no lab command.
        Assert.Empty(relay.Codex.ExecRequests);
        Assert.Equal(0, relay.Lab.StartCalls + relay.Lab.StopCalls);
        Assert.Empty(relay.Events.OfType<RequestStarted>());

        // Route 10: the upgrade is accepted and refused by a typed frame (P2-109).
        using (var phone = await relay.OpenSpeechAsync())
        {
            var ending = await phone.ReadToEndAsync();
            var error = Assert.Single(ending.Errors);
            Assert.Equal("RELAY_PAUSED", error.GetProperty("code").GetString());
            Assert.Equal(WebSocketCloseStatus.PolicyViolation, ending.Status);
            Assert.Equal("RELAY_PAUSED", ending.Reason);
        }

        Assert.Equal(0, relay.LabSockets.ConnectCalls);

        // Resuming serves them again.
        await relay.Relay.SetAcceptingAsync(true);
        var (resumed, _) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody());
        Assert.Equal(200, resumed);
    }

    [Fact]
    public async Task Pausing_ends_an_open_stream_and_leaves_the_relay_usable()
    {
        // P2-115 with the real controller behind the stream: the lease is given back, so the stop command
        // that was refused while the stream ran is accepted once the relay is resumed.
        await using var relay = await ConformanceRelay.StartAsync();
        using var phone = await relay.OpenReadyStreamAsync();
        await phone.SendAudioAsync(2560);
        Assert.NotNull(await phone.ReceiveTextAsync());

        var pausedAt = DateTime.UtcNow;
        await relay.Relay.SetAcceptingAsync(false);
        var ending = await phone.ReadToEndAsync();

        Assert.True(DateTime.UtcNow - pausedAt < ProtocolConstants.ControlChangeLimit + TimeSpan.FromSeconds(3));
        Assert.Equal("RELAY_PAUSED", Assert.Single(ending.Errors).GetProperty("code").GetString());
        Assert.Equal("RELAY_PAUSED", ending.Reason);
        Assert.NotEqual(WebSocketCloseStatus.NormalClosure, ending.Status);
        await Wire.UntilAsync(() => relay.LabSockets.Current.Disposed, "the lab socket was let go");

        await relay.Relay.SetAcceptingAsync(true);
        var (status, _) = await EndToEndRouteTests.SendUntilNotBusyAsync(relay, "/v1/local-model/stop", Bodies.LocalModel("nemotron"));
        Assert.Equal(202, status);
    }
}

/// <summary>Protocol P2-091 and P2-092 with the real stream and the real controller sharing the lease.</summary>
public sealed class LeaseSweepTests
{
    [Fact]
    public async Task An_open_stream_refuses_stop_and_reports_busy_then_stop_works()
    {
        await using var relay = await ConformanceRelay.StartAsync();
        using var phone = await relay.OpenReadyStreamAsync();

        // The lab does not report busy; the relay's own lease does.
        Assert.False(relay.Lab.Busy);
        var (status, body) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/stop", Bodies.LocalModel("nemotron"));
        Assert.Equal(409, status);
        Assert.Equal("""{"error":"The PC model is in use.","code":"MODEL_BUSY"}""", body);
        (status, body) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/start", Bodies.LocalModel("qwen3"));
        Assert.Equal(409, status);
        Assert.Contains("\"code\":\"MODEL_BUSY\"", body, StringComparison.Ordinal);

        (status, body) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model");
        Assert.Equal(200, status);
        using (var busy = JsonDocument.Parse(body))
        {
            Assert.True(busy.RootElement.GetProperty("busy").GetBoolean());
            Assert.Equal("nemotron", busy.RootElement.GetProperty("model").GetString());
        }

        Assert.Equal(0, relay.Lab.StopCalls);
        Assert.Equal(0, relay.Lab.StartCalls);

        // The stream ends cleanly; then stop is accepted and runs once.
        await phone.SendEndStreamAsync();
        Assert.Equal(WebSocketCloseStatus.NormalClosure, (await phone.ReadToEndAsync()).Status);
        (status, body) = await EndToEndRouteTests.SendUntilNotBusyAsync(relay, "/v1/local-model/stop", Bodies.LocalModel("nemotron"));
        Assert.Equal(202, status);
        Assert.Equal("stopping", EndToEndRouteTests.StateOf(body));
        await EndToEndRouteTests.UntilStateAsync(relay, "stopped");
        Assert.Equal(1, relay.Lab.StopCalls);
    }
}

/// <summary>Protocol P2-106 with the real controller: a load that a stream began is what route 4 reports.</summary>
public sealed class LoadSweepTests
{
    [Fact]
    public async Task Route_4_reports_starting_while_the_second_stream_is_refused()
    {
        await using var relay = await ConformanceRelay.StartAsync();
        relay.Lab.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The first phone asks for a model that is not loaded: the stream starts the load, the phone sees
        // "loading" and goes away.
        using (var first = await relay.OpenSpeechAsync("nemotron"))
        {
            await first.ExpectAsync("loading");
            await Wire.UntilAsync(() => relay.Lab.StartCalls == 1, "the load began");
            await first.CloseAsync();
        }

        // The load goes on without it.
        var (status, body) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model");
        Assert.Equal(200, status);
        Assert.Equal("starting", EndToEndRouteTests.StateOf(body));

        // A second attempt is accepted and refused by a typed frame, while route 4 still says "starting".
        using (var second = await relay.OpenSpeechAsync("nemotron"))
        {
            var ending = await second.ReadToEndAsync();
            Assert.Equal("MODEL_OPERATION_IN_PROGRESS", Assert.Single(ending.Errors).GetProperty("code").GetString());
            Assert.Equal(WebSocketCloseStatus.PolicyViolation, ending.Status);
        }

        (_, body) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model");
        Assert.Equal("starting", EndToEndRouteTests.StateOf(body));
        Assert.Equal(1, relay.Lab.StartCalls);

        // The load completes; a third attempt is admitted and becomes ready without a second start.
        relay.Lab.StartGate.SetResult();
        await EndToEndRouteTests.UntilStateAsync(relay, "ready");
        using var third = await relay.OpenSpeechAsync("nemotron");
        await third.WaitForAsync("ready");
        Assert.Equal(1, relay.Lab.StartCalls);
    }
}

/// <summary>
/// Protocol P2-107, P2-143, P2-149 and P2-115 at one reset, with real DPAPI and a real Codex stand-in in
/// flight: the old token stops working everywhere at once, and what it had admitted ends.
/// </summary>
public sealed class ResetSweepTests
{
    [Fact]
    public async Task After_a_reset_the_old_token_fails_on_http_and_on_the_upgrade()
    {
        await using var relay = await ConformanceRelay.StartAsync();
        var old = relay.Token;

        // What the old token admitted: an open stream and an answer run that hangs.
        using var phone = await relay.OpenReadyStreamAsync();
        relay.Codex.ExecScenario = "hang";
        var inFlight = relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody());
        var report = await relay.Codex.ReadExecReportAsync();

        await relay.Relay.ResetPairingAsync(CancellationToken.None);
        var fresh = await relay.ReadTokenAsync();
        Assert.NotEqual(old, fresh);

        // The stream ends with PAIRING_CHANGED (P2-115).
        var ending = await phone.ReadToEndAsync();
        Assert.Equal("PAIRING_CHANGED", Assert.Single(ending.Errors).GetProperty("code").GetString());
        Assert.Equal("PAIRING_CHANGED", ending.Reason);

        // The answer in flight is 401 and its process is killed (P2-149).
        var (status, body) = await inFlight.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(401, status);
        Assert.Equal(Wire.Unauthorized, body);
        await TestChild.WaitUntilGoneAsync(report.ProcessId);
        Assert.False(Directory.Exists(report.WorkingFolder));

        // The old token: 401 on every HTTP route, and an HTTP 401 instead of an upgrade (P2-107, P2-143).
        foreach (var route in Wire.HttpRoutes)
        {
            using var request = relay.RequestFor(route);
            request.Headers.Remove("Authorization");
            request.Headers.TryAddWithoutValidation("Authorization", TestTokens.Bearer(old));
            using var response = await relay.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(Wire.Unauthorized, await response.Content.ReadAsStringAsync());
        }

        var refused = await Assert.ThrowsAsync<WebSocketException>(() => relay.OpenSpeechAsync(token: old));
        Assert.Contains("401", refused.Message, StringComparison.Ordinal);

        // The new one works on both, without a restart.
        relay.Codex.ExecScenario = "ok";
        (status, _) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/health");
        Assert.Equal(200, status);
        (status, _) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody("request-0002"));
        Assert.Equal(200, status);
        using var again = await relay.OpenReadyStreamAsync();

        // The pairing file on disk never holds either token.
        var file = await File.ReadAllTextAsync(Path.Combine(relay.DataDirectory, "pairing.json"));
        Assert.DoesNotContain(old, file, StringComparison.Ordinal);
        Assert.DoesNotContain(fresh, file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_relay_on_the_same_folder_accepts_only_the_token_of_the_last_reset()
    {
        // The token survives a restart through DPAPI, and a reset survives it too: the token that was
        // replaced is not accepted by the next instance either.
        using var data = new LiveCue.TestSupport.TempFolder();
        string old;
        string fresh;
        await using (var first = await ConformanceRelay.StartAsync(options => options.DataDirectory = data.Path))
        {
            old = first.Token;
            await first.Relay.ResetPairingAsync(CancellationToken.None);
            fresh = await first.ReadTokenAsync();
        }

        await using var second = await ConformanceRelay.StartAsync(options => options.DataDirectory = data.Path);
        Assert.Equal(fresh, second.Token);
        var (status, _) = await second.ExchangeAsync(second.Request(HttpMethod.Get, "/v1/health", token: old));
        Assert.Equal(401, status);
        (status, _) = await second.ExchangeAsync(HttpMethod.Get, "/v1/health");
        Assert.Equal(200, status);
        Assert.Single(second.Events.OfType<Notice>(), notice => notice.Kind == NoticeKind.PairingRestored);
    }
}

/// <summary>
/// W-design I-03: a stop ends a real hanging Codex run and an open stream within the stop budget, and
/// nothing is left listening.
/// </summary>
public sealed class StopSweepTests
{
    [Fact]
    public async Task Stop_ends_a_hanging_run_and_an_open_stream_within_the_budget_and_frees_the_port()
    {
        var relay = await ConformanceRelay.StartAsync();
        try
        {
            var port = relay.Port;
            using var phone = await relay.OpenReadyStreamAsync();
            relay.Codex.ExecScenario = "hang";
            var inFlight = relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody());
            var report = await relay.Codex.ReadExecReportAsync();
            var reading = phone.ReadToEndAsync();

            var before = DateTime.UtcNow;
            await relay.Relay.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
            var took = DateTime.UtcNow - before;

            // Within the budget (W-02), with room for a loaded machine.
            Assert.True(took < ProtocolConstants.StopBudget + TimeSpan.FromSeconds(5), $"The stop took {took}.");
            Assert.Equal(RelayStatus.Stopped, relay.Relay.Status);

            // The stream was told why (P2-115) and closed with 1001.
            var ending = await reading.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("RELAY_STOPPING", Assert.Single(ending.Errors).GetProperty("code").GetString());
            Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, ending.Status);

            // The request in flight is not answered: its connection ends. Its process and folder are gone.
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => inFlight.WaitAsync(TimeSpan.FromSeconds(30)));
            await TestChild.WaitUntilGoneAsync(report.ProcessId);
            Assert.False(Directory.Exists(report.WorkingFolder));
            Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(relay.DataDirectory, "work")));

            // Nothing listens: the port can be bound for exclusive use by somebody else.
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                ExclusiveAddressUse = true,
            };
            probe.Bind(new IPEndPoint(IPAddress.Loopback, port));

            // The last event is RelayStopped, and the failed request was reported with the stopping code.
            await relay.Events.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            var events = relay.Events.Snapshot();
            Assert.IsType<RelayStopped>(events[^1]);
            Assert.Equal("RELAY_STOPPING", Assert.Single(events.OfType<RequestFailed>()).Code);
        }
        finally
        {
            await relay.DisposeAsync();
        }
    }
}
