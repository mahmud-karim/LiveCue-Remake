using System.Net;
using System.Text.Json;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>Route 8, <c>POST /v1/session-summary</c> (protocol section 8; statement P2-061).</summary>
public sealed class SummaryRouteTests
{
    [Fact]
    public async Task A_valid_summary_is_returned_with_execution()
    {
        await using var host = await RelayTestHost.StartAsync();

        using var response = await host.PostAsync(AnswerTesting.Summary, Bodies.Summary());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"title":"Title","summary":"Summary.","keyPoints":["Point."],"actionItems":["Action."],"execution":{"model":"gpt-test","reasoningEffort":"low","codexMs":0,"relayTotalMs":0,"requestReadMs":0,"relayOverheadMs":0}}""",
            await response.Content.ReadAsStringAsync());

        var reply = await AnswerTesting.ReadJsonAsync(response);
        Assert.Equal(
            ["title", "summary", "keyPoints", "actionItems", "execution"],
            reply.EnumerateObject().Select(member => member.Name));
        Assert.Equal(JsonValueKind.Object, reply.GetProperty("execution").ValueKind);

        // The run was a summary run, and the desktop saw it as one.
        Assert.Equal(AnswerKind.Summary, Assert.Single(host.Backend.Runs).Kind);
        var completed = await host.Events.WaitForAsync<RequestCompleted>();
        Assert.Equal(AnswerKind.Summary, completed.Kind);
    }

    [Fact]
    public async Task An_assist_shaped_output_is_not_a_summary()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Backend.SummaryResult = Bodies.ValidAssistOutput();

        using var response = await host.PostAsync(AnswerTesting.Summary, Bodies.Summary());

        await AnswerTesting.AssertErrorAsync(response, LiveCue.Relay.Protocol.RelayErrors.AssistantFailed);
    }
}
