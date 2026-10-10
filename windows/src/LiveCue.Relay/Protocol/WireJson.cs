using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LiveCue.Relay.Protocol;

/// <summary>
/// JSON as the protocol wants it (section 1): written compact, as UTF-8, with non-ASCII text left as
/// UTF-8 rather than escaped; read with the depth limit of protocol 5.4 and nothing lenient.
/// </summary>
internal static class WireJson
{
    /// <summary>Not indented, relaxed escaping: only what JSON itself requires is escaped.</summary>
    public static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The depth limit of protocol 5.4 rule 4; no comments, no trailing commas.</summary>
    public static readonly JsonReaderOptions ReaderOptions = new()
    {
        MaxDepth = ProtocolConstants.MaxJsonDepth,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>The bytes of one JSON document produced by <paramref name="write"/>.</summary>
    public static byte[] Write(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            write(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Parses one JSON document whose top level is an object. False for invalid UTF-8, invalid JSON, an
    /// empty input, anything but white space after the document, nesting beyond the limit, or another
    /// top-level kind. <paramref name="root"/> is detached from its document and may be kept.
    /// </summary>
    public static bool TryParseObject(ReadOnlyMemory<byte> utf8, out JsonElement root)
    {
        root = default;
        if (utf8.IsEmpty)
        {
            return false;
        }

        try
        {
            var reader = new Utf8JsonReader(utf8.Span, ReaderOptions);
            if (!JsonElement.TryParseValue(ref reader, out var element)
                || element.Value.ValueKind != JsonValueKind.Object
                || reader.Read())
            {
                return false;
            }

            root = element.Value.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The value re-written compactly, member order and duplicates kept.</summary>
    public static byte[] Compact(JsonElement value) => Write(value.WriteTo);
}
