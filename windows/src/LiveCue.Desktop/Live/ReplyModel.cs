using System.Text.Json;
using LiveCue.Desktop.Activity;

namespace LiveCue.Desktop.Live;

/// <summary>
/// The reply card: answer or summary, details, key points and action items (W-design 5.5; screens 2.8).
/// </summary>
/// <param name="Text">The answer, or the summary when there is no answer; then, after a blank line, the
/// details. When the reply has none of the five members, the reply itself as indented JSON.</param>
/// <param name="KeyPoints">The lines under the "Key points" heading; empty when there are none.</param>
/// <param name="ActionItems">The lines under the "Action items" heading; empty when there are none.</param>
internal sealed record ReplyModel(string Text, IReadOnlyList<string> KeyPoints, IReadOnlyList<string> ActionItems)
{
    /// <summary>Nothing to show: the card's placeholder, its spinner or its failure banner stands instead.</summary>
    public static ReplyModel Empty { get; } = new(string.Empty, [], []);

    /// <summary>Reads the reply that was sent to the phone. Never throws: a payload it cannot read is shown as it is.</summary>
    public static ReplyModel FromPayload(string? payloadJson)
    {
        var answer = string.Empty;
        var details = string.Empty;
        List<string> keyPoints = [];
        List<string> actionItems = [];

        try
        {
            using var document = JsonDocument.Parse(payloadJson ?? string.Empty);
            if (document.RootElement is { ValueKind: JsonValueKind.Object } reply)
            {
                answer = ReadText(reply, "answer");
                if (answer.Length == 0)
                {
                    answer = ReadText(reply, "summary");
                }

                details = ReadText(reply, "details");
                keyPoints = ReadLines(reply, "keyPoints");
                actionItems = ReadLines(reply, "actionItems");
            }
        }
        catch (JsonException)
        {
            // Not JSON: the fallback below shows the text that arrived.
        }

        var text = (answer.Length, details.Length) switch
        {
            (> 0, > 0) => answer + Environment.NewLine + Environment.NewLine + details,
            (> 0, _) => answer,
            _ => details,
        };

        if (text.Length == 0 && keyPoints.Count == 0 && actionItems.Count == 0)
        {
            text = PayloadFormatter.Indent(payloadJson);
        }

        return new ReplyModel(text, keyPoints, actionItems);
    }

    private static string ReadText(JsonElement reply, string name) =>
        reply.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim()
            : string.Empty;

    private static List<string> ReadLines(JsonElement reply, string name)
    {
        if (!reply.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var lines = new List<string>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } line && !string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line.Trim());
            }
        }

        return lines;
    }
}
