using System.Text.Json;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Answers;

/// <summary>
/// Codex's output file is checked by hand against the fixed shapes of protocol 12.5 (no schema library),
/// and the reply is then written new, member by member, in the order of protocol 8.3: an allowlist.
/// A missing or wrongly typed member fails the whole output. Any other member Codex wrote is dropped.
///
/// The model's own memory marker is never forwarded: it is checked for its type and then discarded, and
/// every assist reply carries <c>memory.throughSegmentId</c> as JSON <c>null</c> (protocol 8.3). The
/// phone owns the marker.
/// </summary>
internal static class AnswerValidator
{
    /// <summary>
    /// Builds the reply from <paramref name="output"/>, with <c>execution</c> added. False when the
    /// output is not valid JSON, is not an object, or does not have the shape of its kind.
    /// </summary>
    public static bool TryBuildReply(
        AnswerKind kind,
        ReadOnlyMemory<byte> output,
        string model,
        string reasoningEffort,
        ExecutionTimings timings,
        out byte[] reply)
    {
        reply = [];
        if (!WireJson.TryParseObject(output, out var root))
        {
            return false;
        }

        if (kind == AnswerKind.Summary)
        {
            if (!TryReadSummary(root, out var summary))
            {
                return false;
            }

            reply = WireJson.Write(writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("title", summary.Title);
                writer.WriteString("summary", summary.Summary);
                WriteTexts(writer, "keyPoints", summary.KeyPoints);
                WriteTexts(writer, "actionItems", summary.ActionItems);
                timings.Write(writer, model, reasoningEffort);
                writer.WriteEndObject();
            });
            return true;
        }

        if (!TryReadAssist(root, out var assist))
        {
            return false;
        }

        reply = WireJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("detectedQuestion", assist.DetectedQuestion);
            writer.WriteString("answer", assist.Answer);
            writer.WriteString("details", assist.Details);
            writer.WriteStartObject("memory");
            writer.WriteString("summary", assist.MemorySummary);
            WriteTexts(writer, "facts", assist.Facts);
            WriteTexts(writer, "openQuestions", assist.OpenQuestions);

            // Always null, whatever the model wrote there.
            writer.WriteNull("throughSegmentId");
            writer.WriteEndObject();
            timings.Write(writer, model, reasoningEffort);
            writer.WriteEndObject();
        });
        return true;
    }

    private static bool TryReadAssist(JsonElement root, out AssistOutput assist)
    {
        assist = default;
        if (!TryText(root, "detectedQuestion", out var detectedQuestion)
            || !TryText(root, "answer", out var answer)
            || !TryText(root, "details", out var details)
            || !root.TryGetProperty("memory", out var memory)
            || memory.ValueKind != JsonValueKind.Object
            || !TryText(memory, "summary", out var summary)
            || !TryTexts(memory, "facts", out var facts)
            || !TryTexts(memory, "openQuestions", out var openQuestions)
            || !memory.TryGetProperty("throughSegmentId", out var marker)
            || marker.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return false;
        }

        assist = new AssistOutput(detectedQuestion, answer, details, summary, facts, openQuestions);
        return true;
    }

    private static bool TryReadSummary(JsonElement root, out SummaryOutput summary)
    {
        summary = default;
        if (!TryText(root, "title", out var title)
            || !TryText(root, "summary", out var text)
            || !TryTexts(root, "keyPoints", out var keyPoints)
            || !TryTexts(root, "actionItems", out var actionItems))
        {
            return false;
        }

        summary = new SummaryOutput(title, text, keyPoints, actionItems);
        return true;
    }

    private static bool TryText(JsonElement parent, string name, out string text)
    {
        text = string.Empty;
        return parent.TryGetProperty(name, out var member) && AssistantSelection.TryReadText(member, out text);
    }

    private static bool TryTexts(JsonElement parent, string name, out List<string> texts)
    {
        texts = [];
        if (!parent.TryGetProperty(name, out var member) || member.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in member.EnumerateArray())
        {
            if (!AssistantSelection.TryReadText(item, out var text))
            {
                return false;
            }

            texts.Add(text);
        }

        return true;
    }

    private static void WriteTexts(Utf8JsonWriter writer, string name, List<string> texts)
    {
        writer.WriteStartArray(name);
        foreach (var text in texts)
        {
            writer.WriteStringValue(text);
        }

        writer.WriteEndArray();
    }

    private readonly record struct AssistOutput(
        string DetectedQuestion,
        string Answer,
        string Details,
        string MemorySummary,
        List<string> Facts,
        List<string> OpenQuestions);

    private readonly record struct SummaryOutput(
        string Title,
        string Summary,
        List<string> KeyPoints,
        List<string> ActionItems);
}
