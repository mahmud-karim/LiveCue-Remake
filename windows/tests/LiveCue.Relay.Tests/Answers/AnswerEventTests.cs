using System.Net;
using System.Text.Json;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// What the desktop is told about an answer request (protocol 8.2; statements P2-052 and P2-053): one
/// "started" after R-7 passes and before the run, then exactly one outcome.
/// </summary>
public sealed class AnswerEventTests
{
    [Fact]
    public async Task Started_then_completed_with_the_transcript_and_no_token()
    {
        await using var host = await RelayTestHost.StartAsync();
        const string Body =
            """{"requestId":"request-0001","assistant":{"model":"gpt-test","reasoningEffort":"low"},"instruction":"Be brief","transcript":"Fixture question","partialTranscript":"latest words","memory":{"summary":"Earlier"}}""";

        using var response = await host.PostAsync(AnswerTesting.Assist, Body);
        var reply = await response.Content.ReadAsStringAsync();
        var completed = await host.Events.WaitForAsync<RequestCompleted>();
        await host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var started = Assert.Single(host.Events.OfType<RequestStarted>());
        Assert.Equal("request-0001", started.RequestId);
        Assert.Equal(AnswerKind.Assist, started.Kind);
        Assert.Equal(("gpt-test", "low"), (started.Model, started.ReasoningEffort));
        Assert.Equal("Fixture question", started.Transcript);
        Assert.Equal("latest words", started.PartialTranscript);
        Assert.Equal("Be brief", started.Instruction);
        Assert.Equal(Body, started.PayloadJson);

        Assert.Single(host.Events.OfType<RequestCompleted>());
        Assert.Equal("request-0001", completed.RequestId);
        Assert.Equal(AnswerKind.Assist, completed.Kind);
        Assert.Equal(("gpt-test", "low"), (completed.Model, completed.ReasoningEffort));
        Assert.Equal(reply, completed.PayloadJson);
        using var payload = JsonDocument.Parse(completed.PayloadJson);
        var execution = payload.RootElement.GetProperty("execution");
        Assert.Equal(execution.GetProperty("relayTotalMs").GetDouble(), completed.DurationMs);
        Assert.Equal(execution.GetProperty("codexMs").GetDouble(), completed.CodexMs);
        Assert.Equal(0, host.Events.Count<RequestFailed>());

        // Started comes first, and the run began only after it was published.
        var events = host.Events.Snapshot();
        Assert.True(events.ToList().IndexOf(started) < events.ToList().IndexOf(completed));

        // Neither carries the token or an Authorization value, in any member.
        Assert.All((RelayEvent[])[started, completed], relayEvent =>
        {
            var text = JsonSerializer.Serialize(relayEvent, relayEvent.GetType()) + relayEvent;
            Assert.DoesNotContain(host.Token, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Members_that_are_not_strings_are_not_shown_as_text()
    {
        await using var host = await RelayTestHost.StartAsync();
        const string Body =
            """{"requestId":"request-0001","assistant":{"model":"gpt-test","reasoningEffort":"low"},"instruction":7,"transcript":["a","b"],"partialTranscript":null}""";

        using var response = await host.PostAsync(AnswerTesting.Summary, Body);
        var started = await host.Events.WaitForAsync<RequestStarted>();

        // The relay does not validate these members: the request runs, and the body is shown whole.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(started.Transcript);
        Assert.Null(started.PartialTranscript);
        Assert.Null(started.Instruction);
        Assert.Equal(Body, started.PayloadJson);
        Assert.Equal(AnswerKind.Summary, started.Kind);
    }

    [Theory]
    [InlineData("no-output")]
    [InlineData("invalid-output")]
    [InlineData("not-json")]
    [InlineData("backend-throws")]
    public async Task A_failure_after_the_start_raises_one_failed_event_with_the_code(string situation)
    {
        await using var host = await RelayTestHost.StartAsync();
        switch (situation)
        {
            case "no-output":
                host.Backend.AssistResult = null;
                break;
            case "invalid-output":
                host.Backend.AssistResult = Bodies.Utf8("""{"detectedQuestion":"Q","answer":"A","details":"D"}""");
                break;
            case "not-json":
                host.Backend.AssistResult = Bodies.Utf8("PRIVATE DIAGNOSTIC: the model wrote prose");
                break;
            default:
                host.Backend.FailsWithPrivateText = true;
                break;
        }

        using var response = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist());
        var failed = await host.Events.WaitForAsync<RequestFailed>();
        await host.Events.SettleAsync();

        await AnswerTesting.AssertErrorAsync(response, RelayErrors.AssistantFailed);
        Assert.Single(host.Events.OfType<RequestStarted>());
        Assert.Single(host.Events.OfType<RequestFailed>());
        Assert.Equal(0, host.Events.Count<RequestCompleted>());
        Assert.Equal(Bodies.RequestId, failed.RequestId);
        Assert.Equal(AnswerKind.Assist, failed.Kind);
        Assert.Equal(RelayCodes.AssistantFailed, failed.Code);
        Assert.Equal("The local assistant request failed.", failed.Message);

        // Nothing of what the backend said or threw reaches the phone or the desktop.
        Assert.All(host.Events.Snapshot(), relayEvent =>
            Assert.DoesNotContain("PRIVATE", relayEvent.ToString(), StringComparison.Ordinal));
        if (situation == "backend-throws")
        {
            var diagnostic = Assert.Single(host.Events.OfType<Diagnostic>());
            Assert.Equal("answers", diagnostic.Source);
            Assert.Equal("The answer run failed unexpectedly. (InvalidOperationException)", diagnostic.Message);
        }
        else
        {
            Assert.Equal(0, host.Events.Count<Diagnostic>());
        }
    }
}
