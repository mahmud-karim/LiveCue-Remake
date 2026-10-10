using System.Text;
using LiveCue.Relay.Answers;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// The two output schemas (protocol 12.5; W-design 4.9): the embedded files are the protocol's text, and
/// every relay start writes them into the data folder again, whatever was there.
/// </summary>
public sealed class SchemaFilesTests
{
    /// <summary>Protocol 12.5, <c>assist.schema.json</c>, character for character.</summary>
    private const string Assist =
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "required": ["detectedQuestion", "answer", "details", "memory"],
          "properties": {
            "detectedQuestion": { "type": "string" },
            "answer": { "type": "string" },
            "details": { "type": "string" },
            "memory": {
              "type": "object",
              "additionalProperties": false,
              "required": ["summary", "facts", "openQuestions", "throughSegmentId"],
              "properties": {
                "summary": { "type": "string" },
                "facts": { "type": "array", "items": { "type": "string" } },
                "openQuestions": { "type": "array", "items": { "type": "string" } },
                "throughSegmentId": { "type": ["string", "null"] }
              }
            }
          }
        }
        """;

    /// <summary>Protocol 12.5, <c>summary.schema.json</c>, character for character.</summary>
    private const string Summary =
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "required": ["title", "summary", "keyPoints", "actionItems"],
          "properties": {
            "title": { "type": "string" },
            "summary": { "type": "string" },
            "keyPoints": { "type": "array", "items": { "type": "string" } },
            "actionItems": { "type": "array", "items": { "type": "string" } }
          }
        }
        """;

    [Fact]
    public async Task The_two_files_equal_the_protocol_text_and_are_rewritten_at_start()
    {
        // The embedded copies are the protocol's text (line ends aside), without a byte-order mark.
        var assist = SchemaFiles.Read(SchemaFiles.AssistFileName);
        var summary = SchemaFiles.Read(SchemaFiles.SummaryFileName);
        Assert.Equal(Lines(Assist), Lines(Encoding.UTF8.GetString(assist)));
        Assert.Equal(Lines(Summary), Lines(Encoding.UTF8.GetString(summary)));
        Assert.Equal((byte)'{', assist[0]);
        Assert.Equal((byte)'{', summary[0]);

        // A start writes both, over whatever a previous run or anyone else left there.
        await using var host = RelayTestHost.Create();
        var folder = host.Data.Combine("schemas");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "assist.schema.json"), """{"type":"object","additionalProperties":true}""");
        await host.StartRelayAsync();

        Assert.Equal(assist, File.ReadAllBytes(Path.Combine(folder, "assist.schema.json")));
        Assert.Equal(summary, File.ReadAllBytes(Path.Combine(folder, "summary.schema.json")));
        Assert.Equal(Path.Combine(folder, "assist.schema.json"), SchemaFiles.PathFor(host.Data.Path, AnswerKind.Assist));
        Assert.Equal(Path.Combine(folder, "summary.schema.json"), SchemaFiles.PathFor(host.Data.Path, AnswerKind.Summary));
    }

    [Fact]
    public void Rewriting_replaces_both_files_and_makes_the_folder()
    {
        using var data = new TempFolder();

        SchemaFiles.Rewrite(data.Path);
        File.WriteAllText(data.Combine("schemas", "summary.schema.json"), "changed");
        File.Delete(data.Combine("schemas", "assist.schema.json"));
        SchemaFiles.Rewrite(data.Path);

        Assert.Equal(SchemaFiles.Read("assist.schema.json"), File.ReadAllBytes(data.Combine("schemas", "assist.schema.json")));
        Assert.Equal(SchemaFiles.Read("summary.schema.json"), File.ReadAllBytes(data.Combine("schemas", "summary.schema.json")));
        Assert.Equal(
            [Path.Combine("schemas", "assist.schema.json"), Path.Combine("schemas", "summary.schema.json")],
            data.FileNames());
    }

    /// <summary>The lines of a text, whatever its line ends, without a last empty one.</summary>
    private static string[] Lines(string text) =>
        text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
}
