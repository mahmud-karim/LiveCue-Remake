using System.Net;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.LocalModel;

/// <summary>
/// Protocol P2-051 for the PC model routes: while the relay is paused, routes 5 and 6 are refused at M-1,
/// before the body is read, and route 4 is served.
/// </summary>
public sealed class LocalModelPauseTests
{
    [Fact]
    public async Task Paused_refuses_start_and_stop_and_serves_status()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Lab.LoadedModel = "nemotron";
        await host.Relay.SetAcceptingAsync(false);

        using var start = await host.PostAsync("/v1/local-model/start", Bodies.LocalModel("qwen3"));
        using var stop = await host.PostAsync("/v1/local-model/stop", Bodies.LocalModel("nemotron"));

        // M-1 comes before M-2 and M-3: neither a broken body nor a bad model name changes the answer.
        using var brokenBody = await host.PostAsync("/v1/local-model/start", "{");
        using var badModel = await host.PostAsync("/v1/local-model/stop", Bodies.LocalModel("../evil"));
        using var status = await host.GetAsync("/v1/local-model");

        await LocalModelRouteTests.AssertErrorAsync(start, RelayErrors.RelayPaused);
        await LocalModelRouteTests.AssertErrorAsync(stop, RelayErrors.RelayPaused);
        await LocalModelRouteTests.AssertErrorAsync(brokenBody, RelayErrors.RelayPaused);
        await LocalModelRouteTests.AssertErrorAsync(badModel, RelayErrors.RelayPaused);
        Assert.Equal(
            """{"error":"PC relay is paused. Resume it in LiveCue Desktop.","code":"RELAY_PAUSED"}""",
            await start.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(
            """{"state":"ready","model":"nemotron","busy":false,"elapsedSeconds":0,"message":"Model loaded on the PC GPU. Ready for live transcription."}""",
            await status.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Lab.StartCalls);
        Assert.Equal(0, host.Lab.StopCalls);

        // Only the status route reached the lab.
        Assert.Equal(1, host.Lab.ProbeCalls);
    }

    [Fact]
    public async Task An_operation_that_began_before_the_pause_continues_and_resume_serves_commands_again()
    {
        await using var host = await RelayTestHost.StartAsync();
        var gate = host.Lab.StartGate = LocalModelHarness.NewGate();

        using var start = await host.PostAsync("/v1/local-model/start", Bodies.LocalModel("nemotron"));
        await host.Relay.SetAcceptingAsync(false);
        gate.SetResult();
        await host.Events.WaitForAsync<LocalModelChanged>(changed => changed.Status.State == LocalModelState.Ready);
        using var whilePaused = await host.GetAsync("/v1/local-model");
        await host.Relay.SetAcceptingAsync(true);
        using var stop = await host.PostAsync("/v1/local-model/stop", Bodies.LocalModel("nemotron"));
        await host.Events.WaitForAsync<LocalModelChanged>(changed => changed.Status.State == LocalModelState.Stopped);

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Contains("\"state\":\"ready\"", await whilePaused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Accepted, stop.StatusCode);
        Assert.Equal(1, host.Lab.StopCalls);
    }
}
