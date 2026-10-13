using System.Text;
using LiveCue.Relay.Answers;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// Validation of Codex's output and the rebuilt reply (protocol 8.3 and 12.5; statements P2-060 and
/// P2-065). The reply is an allowlist: only the members of the protocol, written new.
/// </summary>
public sealed class AnswerValidatorTests
{
    private static readonly ExecutionTimings Timings = ExecutionTimings.From(600.14, 620.44, 2.04);

    [Theory]

    // Statement P2-060.
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D"}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":7,"details":"D","memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":null}}""")]
    [InlineData("not json")]

    // Everything else that is not the shape of protocol 12.5.
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":null}} trailing""")]
    [InlineData("""{"answer":"A","details":"D","memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":null}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":null}}""")]
    [InlineData("""{"detectedQuestion":null,"answer":"A","details":"D","memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":null}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":["D"],"memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":null}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":null}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":"S"}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"facts":[],"openQuestions":[],"throughSegmentId":null}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","openQuestions":[],"throughSegmentId":null}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","facts":"F","openQuestions":[],"throughSegmentId":null}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","facts":["F",7],"openQuestions":[],"throughSegmentId":null}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","facts":[],"openQuestions":[null],"throughSegmentId":null}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","facts":[],"openQuestions":[]}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":7}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":{}}}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"\ud800","details":"D","memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":null}}""")]
    public void Missing_memory_a_numeric_answer_and_non_json_fail(string output)
    {
        Assert.False(AnswerValidator.TryBuildReply(
            AnswerKind.Assist, Bodies.Utf8(output), "gpt-test", "low", Timings, out var reply));
        Assert.Empty(reply);
    }

    [Theory]
    [InlineData("""{"summary":"S","keyPoints":[],"actionItems":[]}""")]
    [InlineData("""{"title":"T","keyPoints":[],"actionItems":[]}""")]
    [InlineData("""{"title":"T","summary":"S","actionItems":[]}""")]
    [InlineData("""{"title":"T","summary":"S","keyPoints":[]}""")]
    [InlineData("""{"title":7,"summary":"S","keyPoints":[],"actionItems":[]}""")]
    [InlineData("""{"title":"T","summary":"S","keyPoints":"K","actionItems":[]}""")]
    [InlineData("""{"title":"T","summary":"S","keyPoints":[],"actionItems":[{}]}""")]
    [InlineData("""{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":null}}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void A_summary_that_lacks_a_member_or_has_a_wrong_type_fails(string output)
    {
        Assert.False(AnswerValidator.TryBuildReply(
            AnswerKind.Summary, Bodies.Utf8(output), "gpt-test", "low", Timings, out var reply));
        Assert.Empty(reply);
    }

    [Fact]
    public void An_extra_member_is_dropped()
    {
        const string AssistOutput =
            """
            {
              "extra": "PRIVATE",
              "answer": "A \"quoted\" näive answer ☃",
              "detectedQuestion": "Q",
              "details": "",
              "execution": { "model": "gpt-other", "codexMs": -1 },
              "memory": {
                "path": "C:\\PRIVATE",
                "openQuestions": ["Q1", "Q2"],
                "facts": [],
                "throughSegmentId": null,
                "summary": "S",
                "memory": { "nested": true }
              },
              "tools": [ { "name": "PRIVATE" } ]
            }
            """;
        const string SummaryOutput =
            """{"actionItems":["A1"],"keyPoints":["K1","K2"],"extra":{"deep":["PRIVATE"]},"summary":"S","title":"T","execution":"PRIVATE"}""";

        Assert.True(AnswerValidator.TryBuildReply(
            AnswerKind.Assist, Bodies.Utf8(AssistOutput), "gpt-test", "low", Timings, out var assist));
        Assert.True(AnswerValidator.TryBuildReply(
            AnswerKind.Summary, Bodies.Utf8(SummaryOutput), "gpt-test", "low", Timings, out var summary));

        // Exactly the members of protocol 8.3, in its order, compact, non-ASCII text as UTF-8, and the
        // relay's own execution member, never the model's.
        Assert.Equal(
            """{"detectedQuestion":"Q","answer":"A \"quoted\" näive answer ☃","details":"","memory":{"summary":"S","facts":[],"openQuestions":["Q1","Q2"],"throughSegmentId":null},"execution":{"model":"gpt-test","reasoningEffort":"low","codexMs":600.1,"relayTotalMs":620.4,"requestReadMs":2,"relayOverheadMs":20.3}}""",
            Encoding.UTF8.GetString(assist));
        Assert.Equal(
            """{"title":"T","summary":"S","keyPoints":["K1","K2"],"actionItems":["A1"],"execution":{"model":"gpt-test","reasoningEffort":"low","codexMs":600.1,"relayTotalMs":620.4,"requestReadMs":2,"relayOverheadMs":20.3}}""",
            Encoding.UTF8.GetString(summary));
        Assert.DoesNotContain("PRIVATE", Encoding.UTF8.GetString(assist), StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", Encoding.UTF8.GetString(summary), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"0B5C1C7A-51E2-4D0B-9D43-2F1E7A9C4D11\"")]
    [InlineData("\"not-a-uuid\"")]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"PRIVATE text the model put where the marker belongs\"")]
    public void The_marker_is_always_written_as_null(string marker)
    {
        var output = """{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","facts":["F"],"openQuestions":[],"throughSegmentId":"""
            + marker + "}}";

        Assert.True(AnswerValidator.TryBuildReply(
            AnswerKind.Assist, Bodies.Utf8(output), "gpt-test", "low", Timings, out var reply));

        var text = Encoding.UTF8.GetString(reply);
        Assert.Contains(""","openQuestions":[],"throughSegmentId":null},"execution":""", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0B5C1C7A", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-uuid", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fixture_outputs_of_the_test_host_are_valid()
    {
        Assert.True(AnswerValidator.TryBuildReply(
            AnswerKind.Assist, Bodies.ValidAssistOutput(), "gpt-test", "low", Timings, out _));
        Assert.True(AnswerValidator.TryBuildReply(
            AnswerKind.Summary, Bodies.ValidSummaryOutput(), "gpt-test", "low", Timings, out _));

        // A valid assist output is not a valid summary, and the other way round.
        Assert.False(AnswerValidator.TryBuildReply(
            AnswerKind.Summary, Bodies.ValidAssistOutput(), "gpt-test", "low", Timings, out _));
        Assert.False(AnswerValidator.TryBuildReply(
            AnswerKind.Assist, Bodies.ValidSummaryOutput(), "gpt-test", "low", Timings, out _));
    }

    [Theory]
    [InlineData(600.14, 620.44, 2.04, 600.1, 620.4, 2.0, 20.3)]
    [InlineData(0, 0, 0, 0, 0, 0, 0)]
    [InlineData(10.06, 10.04, 0.05, 10.1, 10.1, 0.1, 0)]
    [InlineData(59999.96, 60000.01, 9999.99, 60000.0, 60000.0, 10000.0, 0)]
    [InlineData(-5, -1, -0.5, 0, 0, 0, 0)]
    [InlineData(double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, 0, 0, 0)]
    [InlineData(100.2, 100.4, 1, 100.2, 100.4, 1, 0.2)]
    public void Timings_are_rounded_to_one_decimal_and_keep_their_identities(
        double codexMs,
        double relayTotalMs,
        double requestReadMs,
        double expectedCodex,
        double expectedTotal,
        double expectedRead,
        double expectedOverhead)
    {
        var timings = ExecutionTimings.From(codexMs, relayTotalMs, requestReadMs);

        Assert.Equal(expectedCodex, timings.CodexMs);
        Assert.Equal(expectedTotal, timings.RelayTotalMs);
        Assert.Equal(expectedRead, timings.RequestReadMs);
        Assert.Equal(expectedOverhead, timings.RelayOverheadMs);

        // Statement P2-055, on what is written.
        Assert.True(timings.CodexMs >= 0);
        Assert.True(timings.RelayTotalMs >= timings.CodexMs);
        Assert.True(Math.Abs(timings.RelayTotalMs - timings.CodexMs - timings.RelayOverheadMs) < 0.01);
    }
}
