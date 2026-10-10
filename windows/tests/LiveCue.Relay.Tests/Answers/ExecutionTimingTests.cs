using System.Net;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// The <c>execution</c> member of a reply (protocol 8.3; statement P2-055): the echo of the selection
/// and the timing identities, on both routes. Time is the manual clock, moved by the fakes themselves:
/// the catalogue read takes 20.3 ms and the run takes 600.1 ms.
/// </summary>
public sealed class ExecutionTimingTests
{
    [Theory]
    [InlineData(AnswerTesting.Assist)]
    [InlineData(AnswerTesting.Summary)]
    public async Task Echo_and_timing_identities_hold_on_both_routes(string route)
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Catalog.Loader = () =>
        {
            host.Clock.Advance(TimeSpan.FromMilliseconds(20.3));
            return FakeCatalogSource.Fixture;
        };
        host.Backend.Handler = (run, _) =>
        {
            host.Clock.Advance(TimeSpan.FromMilliseconds(600.1));
            return Task.FromResult(run.Kind == AnswerKind.Summary ? host.Backend.SummaryResult : host.Backend.AssistResult);
        };

        using var response = await host.PostAsync(route, Bodies.Assist());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var execution = (await AnswerTesting.ReadJsonAsync(response)).GetProperty("execution");
        Assert.Equal(
            ["model", "reasoningEffort", "codexMs", "relayTotalMs", "requestReadMs", "relayOverheadMs"],
            execution.EnumerateObject().Select(member => member.Name));

        // The echo of the selection that was used.
        Assert.Equal(Bodies.Model, execution.GetProperty("model").GetString());
        Assert.Equal(Bodies.ReasoningEffort, execution.GetProperty("reasoningEffort").GetString());

        var codexMs = execution.GetProperty("codexMs").GetDouble();
        var relayTotalMs = execution.GetProperty("relayTotalMs").GetDouble();
        var relayOverheadMs = execution.GetProperty("relayOverheadMs").GetDouble();
        var requestReadMs = execution.GetProperty("requestReadMs").GetDouble();

        // The identities of the protocol.
        Assert.True(codexMs >= 0);
        Assert.True(relayTotalMs >= codexMs);
        Assert.True(Math.Abs(relayTotalMs - codexMs - relayOverheadMs) < 0.01);

        // And the values themselves: the run, the run plus the catalogue read, their difference.
        Assert.Equal(600.1, codexMs);
        Assert.Equal(620.4, relayTotalMs);
        Assert.Equal(20.3, relayOverheadMs);
        Assert.Equal(0, requestReadMs);
        Assert.Contains(
            "\"codexMs\":600.1,\"relayTotalMs\":620.4,\"requestReadMs\":0,\"relayOverheadMs\":20.3}",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // The desktop's duration is relayTotalMs.
        var completed = await host.Events.WaitForAsync<RequestCompleted>();
        Assert.Equal((620.4, 600.1), (completed.DurationMs, completed.CodexMs));
    }
}
