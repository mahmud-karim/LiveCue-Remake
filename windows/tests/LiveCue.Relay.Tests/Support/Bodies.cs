using System.Text;

namespace LiveCue.Relay.Tests.Support;

/// <summary>
/// The standard request bodies and backend outputs of the conformance tests (protocol section 15, "Test
/// conventions"): a valid <c>requestId</c> and the selection <c>gpt-test</c> / <c>low</c> of the fixture
/// catalogue.
/// </summary>
internal static class Bodies
{
    public const string RequestId = "request-0001";
    public const string Model = "gpt-test";
    public const string ReasoningEffort = "low";

    /// <summary>The transcript of protocol P2-052.</summary>
    public const string Transcript = "Fixture question";

    /// <summary>A valid assist request.</summary>
    public static string Assist(string requestId = RequestId, string transcript = Transcript) =>
        $$"""{"requestId":"{{requestId}}","assistant":{"model":"{{Model}}","reasoningEffort":"{{ReasoningEffort}}"},"transcript":"{{transcript}}"}""";

    /// <summary>A valid summary request.</summary>
    public static string Summary(string requestId = RequestId, string transcript = Transcript) =>
        $$"""{"requestId":"{{requestId}}","assistant":{"model":"{{Model}}","reasoningEffort":"{{ReasoningEffort}}"},"transcript":"{{transcript}}"}""";

    /// <summary>The body of a start or stop of a PC speech model.</summary>
    public static string LocalModel(string model) => $$"""{"model":"{{model}}"}""";

    /// <summary>Backend output that passes the assist schema of protocol 12.5; its answer is <c>Answer.</c></summary>
    public static byte[] ValidAssistOutput(string answer = "Answer.") => Utf8(string.Concat(
        """{"detectedQuestion":"Question?","answer":""",
        "\"",
        answer,
        "\"",
        ""","details":"Details.","memory":{"summary":"Summary.","facts":["Fact."],"openQuestions":[],"throughSegmentId":null}}"""));

    /// <summary>Backend output that passes the summary schema of protocol 12.5.</summary>
    public static byte[] ValidSummaryOutput() => Utf8(
        """{"title":"Title","summary":"Summary.","keyPoints":["Point."],"actionItems":["Action."]}""");

    /// <summary>A JSON object of exactly <paramref name="bytes"/> bytes, for the body limit.</summary>
    public static string ObjectOfSize(int bytes)
    {
        const string head = "{\"pad\":\"";
        const string tail = "\"}";
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, head.Length + tail.Length);
        return string.Concat(head, new string('a', bytes - head.Length - tail.Length), tail);
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>JSON content without a charset parameter, as the phone sends it.</summary>
    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
