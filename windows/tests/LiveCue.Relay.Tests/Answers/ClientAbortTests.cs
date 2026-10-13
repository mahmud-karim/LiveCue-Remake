using System.Diagnostics;
using System.Globalization;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// Protocol 5.5 for an answer request (statement P2-062): when the caller's connection goes away the
/// run is killed at once and the slot is freed. The request is written to a socket by hand, so that the
/// test decides when the connection closes.
/// </summary>
public sealed class ClientAbortTests
{
    [Theory]
    [InlineData("/v1/assist", AnswerKind.Assist)]
    [InlineData("/v1/session-summary", AnswerKind.Summary)]
    public async Task Closing_the_connection_kills_the_run_and_frees_the_slot_within_a_second(string target, AnswerKind kind)
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Backend.NeverReturns = true;
        var body = Bodies.Assist("goes-away");

        var raw = await host.OpenRawAsync();
        await raw.SendAsync(host.RawHead(
            $"POST {target} HTTP/1.1",
            ("Content-Type", "application/json"),
            ("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture))) + body);
        await AnswerTesting.RunsStartedAsync(host, 1);
        using (var busy = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist("while-held")))
        {
            await AnswerTesting.AssertErrorAsync(busy, RelayErrors.AssistantBusy);
        }

        var closed = Stopwatch.StartNew();
        raw.Dispose();
        await Waiting.UntilAsync(() => host.Backend.Cancelled == 1);
        var killedAfter = closed.Elapsed;
        var freeAfter = killedAfter + await AnswerTesting.UntilTheSlotIsFreeAsync(host);

        Assert.True(killedAfter < TimeSpan.FromSeconds(1), $"The run was killed after {killedAfter.TotalMilliseconds:F0} ms.");
        Assert.True(freeAfter < TimeSpan.FromSeconds(1), $"The slot was free after {freeAfter.TotalMilliseconds:F0} ms.");

        // The desktop is told that the phone went away; that is not a wire code and has its own text.
        var failed = await host.Events.WaitForAsync<RequestFailed>();
        await host.Events.SettleAsync();
        Assert.Equal(("goes-away", kind, RelayCodes.ClientDisconnected), (failed.RequestId, failed.Kind, failed.Code));
        Assert.Equal("The phone disconnected before the answer was ready.", failed.Message);
        Assert.Single(host.Events.OfType<RequestFailed>());
        Assert.Equal(0, host.Events.Count<RequestCompleted>());
        Assert.Equal(0, host.Events.Count<Diagnostic>());

        // And the relay goes on serving.
        host.Backend.NeverReturns = false;
        using var next = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist("next"));
        Assert.Equal(System.Net.HttpStatusCode.OK, next.StatusCode);
    }
}
