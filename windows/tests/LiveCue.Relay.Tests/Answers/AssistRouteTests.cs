using System.Net;
using System.Text;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// Route 7, <c>POST /v1/assist</c>, through a real loopback listener with the fake backend and the fake
/// catalogue source (protocol section 8; statements P2-037, P2-042, P2-043, P2-050 and P2-054).
/// </summary>
public sealed class AssistRouteTests
{
    [Fact]
    public async Task A_valid_backend_answer_is_returned()
    {
        await using var host = await RelayTestHost.StartAsync();

        using var response = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        var reply = await AnswerTesting.ReadJsonAsync(response);
        Assert.Equal("Answer.", reply.GetProperty("answer").GetString());

        // The whole reply: the members of protocol 8.3 in order, the marker as null, then execution.
        Assert.Equal(
            """{"detectedQuestion":"Question?","answer":"Answer.","details":"Details.","memory":{"summary":"Summary.","facts":["Fact."],"openQuestions":[],"throughSegmentId":null},"execution":{"model":"gpt-test","reasoningEffort":"low","codexMs":0,"relayTotalMs":0,"requestReadMs":0,"relayOverheadMs":0}}""",
            await response.Content.ReadAsStringAsync());
        Assert.Single(host.Backend.Runs);
    }

    [Fact]
    public async Task A_failing_catalogue_is_503_and_the_backend_does_not_run()
    {
        // The real clock: the two gaps between the three attempts are 50 ms each.
        await using var host = await RelayTestHost.StartAsync(options => options.UseSystemClock = true);
        host.Catalog.Fails = true;

        using var assist = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist());
        using var summary = await host.PostAsync(AnswerTesting.Summary, Bodies.Summary());

        await AnswerTesting.AssertErrorAsync(assist, RelayErrors.AssistantCatalogUnavailable);
        await AnswerTesting.AssertErrorAsync(summary, RelayErrors.AssistantCatalogUnavailable);
        Assert.Empty(host.Backend.Runs);
        Assert.Equal(2 * ProtocolConstants.CatalogAttempts, host.Catalog.Calls);

        // The desktop is told about the catalogue, and about no request: none passed R-7.
        await host.Events.WaitForAsync<AssistantNotice>(notice => notice.Code == RelayCodes.AssistantCatalogUnavailable);
        await host.Events.SettleAsync();
        Assert.Equal(2, host.Events.Count<AssistantNotice>());
        Assert.Equal(0, host.Events.Count<RequestStarted>());
        Assert.Equal(0, host.Events.Count<RequestFailed>());
    }

    [Fact]
    public async Task An_unknown_model_is_selection_unavailable()
    {
        await using var host = await RelayTestHost.StartAsync();
        const string Body = """{"requestId":"request-0001","assistant":{"model":"not-a-model","reasoningEffort":"low"},"transcript":"x"}""";

        using var unknownModel = await host.PostAsync(AnswerTesting.Assist, Body);
        using var unknownEffort = await host.PostAsync(
            AnswerTesting.Summary,
            """{"requestId":"request-0002","assistant":{"model":"gpt-test","reasoningEffort":"high"},"transcript":"x"}""");

        await AnswerTesting.AssertErrorAsync(unknownModel, RelayErrors.AssistantSelectionUnavailable);
        await AnswerTesting.AssertErrorAsync(unknownEffort, RelayErrors.AssistantSelectionUnavailable);
        Assert.Empty(host.Backend.Runs);

        // A rejected selection is never replaced by another model; the desktop is told.
        var notice = await host.Events.WaitForAsync<AssistantNotice>();
        Assert.Equal(RelayCodes.AssistantSelectionUnavailable, notice.Code);
        Assert.Equal(RelayErrors.AssistantSelectionUnavailable.Text, notice.Message);
        Assert.Equal(0, host.Events.Count<RequestStarted>());
    }

    [Fact]
    public async Task A_missing_assistant_is_invalid_on_both_routes_and_nothing_runs()
    {
        await using var host = await RelayTestHost.StartAsync();
        string[] bodies =
        [
            """{"requestId":"request-0001","transcript":"x"}""",
            """{"requestId":"request-0001","assistant":null,"transcript":"x"}""",
            """{"requestId":"request-0001","assistant":"gpt-test","transcript":"x"}""",
            """{"requestId":"request-0001","assistant":["gpt-test","low"],"transcript":"x"}""",
            """{"requestId":"request-0001","assistant":{"model":"gpt-test"},"transcript":"x"}""",
            """{"requestId":"request-0001","assistant":{"reasoningEffort":"low"},"transcript":"x"}""",
            """{"requestId":"request-0001","assistant":{"model":7,"reasoningEffort":"low"},"transcript":"x"}""",
            """{"requestId":"request-0001","model":"gpt-test","reasoningEffort":"low","transcript":"x"}""",
        ];

        foreach (var route in (string[])[AnswerTesting.Assist, AnswerTesting.Summary])
        {
            foreach (var body in bodies)
            {
                using var response = await host.PostAsync(route, body);
                await AnswerTesting.AssertErrorAsync(response, RelayErrors.InvalidAssistant);
            }
        }

        // The relay has no default model: nothing ran, and the catalogue was not even asked.
        Assert.Empty(host.Backend.Runs);
        Assert.Equal(0, host.Catalog.Calls);
        await host.Events.SettleAsync();
        Assert.Equal(0, host.Events.Count<RequestStarted>());
        Assert.Equal(0, host.Events.Count<AssistantNotice>());
    }

    [Fact]
    public async Task The_backend_receives_exactly_the_selection()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Catalog.Loader = () =>
        [
            new RawCatalogEntry("gpt-test", "Test", Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-5.6-sol", "GPT-5.6 Sol", Hidden: false, ["low", "medium", "high"]),
        ];

        // Written with spaces and line breaks, a repeated member and members the relay does not know.
        const string Body =
            """
            {
              "requestId": "6F9619FF-8B86-4011-B42D-00C04FC964FF",
              "sessionId": "5B0E8C7E-3C1B-4E6B-9C0A-0E6C1F2A3B4C",
              "assistant": { "model": "gpt-5.6-sol", "reasoningEffort": "none", "provider": "codex" },
              "instruction": "Answer briefly",
              "memory": { "summary": "Earlier", "facts": [], "openQuestions": [] },
              "transcript": "naïve ☃ line one\nline two",
              "transcript": "the second transcript",
              "partialTranscript": "latest words"
            }
            """;

        using var response = await host.PostAsync(AnswerTesting.Assist, Body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = Assert.Single(host.Backend.Runs);
        Assert.Equal(("gpt-5.6-sol", "none"), (run.Model, run.ReasoningEffort));
        Assert.Equal(AnswerKind.Assist, run.Kind);
        Assert.Equal("6F9619FF-8B86-4011-B42D-00C04FC964FF", run.RequestId);

        // The body reaches the backend whole, as one compact line: member order and the repeated member
        // kept, non-ASCII text as UTF-8, the line break still an escape.
        Assert.Equal(
            """{"requestId":"6F9619FF-8B86-4011-B42D-00C04FC964FF","sessionId":"5B0E8C7E-3C1B-4E6B-9C0A-0E6C1F2A3B4C","assistant":{"model":"gpt-5.6-sol","reasoningEffort":"none","provider":"codex"},"instruction":"Answer briefly","memory":{"summary":"Earlier","facts":[],"openQuestions":[]},"transcript":"naïve ☃ line one\nline two","transcript":"the second transcript","partialTranscript":"latest words"}""",
            Encoding.UTF8.GetString(run.RequestBodyUtf8.Span));

        // And the reply echoes the selection that was used.
        var execution = (await AnswerTesting.ReadJsonAsync(response)).GetProperty("execution");
        Assert.Equal("gpt-5.6-sol", execution.GetProperty("model").GetString());
        Assert.Equal("none", execution.GetProperty("reasoningEffort").GetString());
    }

    [Theory]
    [InlineData("""{"requestId":"request-0001","assistant":{"model":"gpt-test","reasoningEffort":"low"},"transcript":"\ud800"}""")]
    [InlineData("""{"requestId":"request-0001","assistant":{"model":"gpt-test","reasoningEffort":"low"},"\udc00":"x"}""")]
    [InlineData("""{"requestId":"\ud800","assistant":{"model":"gpt-test","reasoningEffort":"low"},"transcript":"x"}""")]
    [InlineData("""{"requestId":"request-0001","assistant":{"model":"\ud800","reasoningEffort":"low"},"transcript":"x"}""")]
    public async Task A_body_that_holds_half_a_surrogate_pair_is_answered_and_never_an_internal_error(string body)
    {
        await using var host = await RelayTestHost.StartAsync();

        using var response = await host.PostAsync(AnswerTesting.Assist, body);
        using var next = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist());

        // Such text cannot be written again as JSON: the body is refused as invalid, nothing runs for
        // it, no diagnostic is raised, and the slot is free for the next request.
        await AnswerTesting.AssertErrorAsync(response, RelayErrors.InvalidJson);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Single(host.Backend.Runs);
        await host.Events.SettleAsync();
        Assert.Equal(0, host.Events.Count<Diagnostic>());
    }
}
