using LiveCue.Desktop.Activity;

namespace LiveCue.Desktop.Tests.Activity;

/// <summary>How a request or reply payload is written into the log (test plan 2.7; W-design 5.7).</summary>
public sealed class PayloadFormatterTests
{
    private static readonly string NewLine = Environment.NewLine;

    [Fact]
    public void Indentation()
    {
        // Two spaces a level, every member on its own line, in the order it was sent.
        var indented = PayloadFormatter.Indent(
            """{"transcript":"What would we cut?","assistant":{"model":"gpt-5.6-sol","reasoningEffort":"low"},"turns":[1,2],"final":true,"note":null}""");

        Assert.Equal(
            string.Join(
                NewLine,
                "{",
                "  \"transcript\": \"What would we cut?\",",
                "  \"assistant\": {",
                "    \"model\": \"gpt-5.6-sol\",",
                "    \"reasoningEffort\": \"low\"",
                "  },",
                "  \"turns\": [",
                "    1,",
                "    2",
                "  ],",
                "  \"final\": true,",
                "  \"note\": null",
                "}"),
            indented);

        // Text stays readable: an apostrophe, an accent and a symbol are not turned into escapes.
        Assert.Equal(
            "{" + NewLine + "  \"answer\": \"It's a café: 2 < 3 & more…\"" + NewLine + "}",
            PayloadFormatter.Indent("""{"answer":"It's a café: 2 < 3 & more…"}"""));

        // What must be escaped to stay one JSON string still is.
        Assert.Equal(
            "{" + NewLine + "  \"answer\": \"line one\\nline \\\"two\\\"\"" + NewLine + "}",
            PayloadFormatter.Indent("""{"answer":"line one\nline \"two\""}"""));

        // A payload that already has line breaks and spaces comes out the same way.
        Assert.Equal(
            "{" + NewLine + "  \"a\": 1" + NewLine + "}",
            PayloadFormatter.Indent("{\r\n        \"a\"  :  1\r\n}"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("\"only text\"", "\"only text\"")]
    [InlineData("  not json at all  ", "not json at all")]
    [InlineData("{\"cut\":", "{\"cut\":")]
    public void What_is_not_a_json_object_is_shown_as_it_is(string? payload, string shown)
    {
        Assert.Equal(shown, PayloadFormatter.Indent(payload));
    }
}
