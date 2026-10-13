using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LiveCue.Desktop.Activity;

/// <summary>
/// Indents a request or reply payload by two spaces for the activity log and for the two cards of the
/// Live page (W-design 5.5 and 5.7). The payload is shown in full, as it was sent: every member, in
/// its order, with text readable rather than escaped. Text that is not JSON is shown as it is.
/// </summary>
internal static class PayloadFormatter
{
    private static readonly JsonWriterOptions Indented = new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,

        // For reading in a window, not for a browser: an apostrophe or an accented letter stays itself.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>The payload as indented JSON; the trimmed text itself when it is not JSON.</summary>
    public static string Indent(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson, Lenient);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, Indented))
            {
                document.RootElement.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (JsonException)
        {
            return payloadJson.Trim();
        }
    }
}
