using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// The format of <c>pairing.json</c> (architecture 3.5, W-design 4.7): UTF-8 without a byte-order mark,
/// two-space indent, line feeds, and exactly these four members in this order:
///
/// <code>
/// {
///   "schemaVersion": 1,
///   "tokenHash": "64 lower-case hex characters: SHA-256 of the UTF-8 token",
///   "protectedToken": "base64 of the DPAPI-protected token",
///   "createdAt": "2026-10-10T08:15:30Z"
/// }
/// </code>
///
/// There is no plaintext member, no previous-token member and no legacy format. Every value written
/// here consists of characters that need no JSON escaping, so the text is written by hand and is exact.
/// </summary>
internal static class PairingFile
{
    /// <summary>The file's name inside the data folder.</summary>
    public const string Name = "pairing.json";

    private const int SchemaVersion = 1;

    // A pairing file is a flat object. Anything nested deeper than this is not one.
    private const int MaxDepth = 8;

    /// <summary>The absolute path of the pairing file of a data folder.</summary>
    public static string PathIn(string dataDirectory) => Path.Combine(dataDirectory, Name);

    /// <summary>The bytes of a pairing file. <paramref name="createdAt"/> is written as UTC to the second.</summary>
    public static byte[] Write(ReadOnlySpan<byte> tokenHash, ReadOnlySpan<byte> protectedToken, DateTimeOffset createdAt)
    {
        var text = string.Concat(
            "{\n  \"schemaVersion\": ",
            SchemaVersion.ToString(CultureInfo.InvariantCulture),
            ",\n  \"tokenHash\": \"",
            PairingToken.HashText(tokenHash),
            "\",\n  \"protectedToken\": \"",
            Convert.ToBase64String(protectedToken),
            "\",\n  \"createdAt\": \"",
            createdAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            "\"\n}\n");
        return Encoding.ASCII.GetBytes(text);
    }

    /// <summary>
    /// Reads what a load needs from the bytes of a pairing file: the hash and the protected copy. False
    /// unless the text is one JSON object whose <c>schemaVersion</c> is the number 1, whose
    /// <c>tokenHash</c> is 64 lower-case hex characters as a whole text, and whose <c>protectedToken</c>
    /// is Base64. Members the relay does not know are ignored.
    /// </summary>
    public static bool TryRead(ReadOnlyMemory<byte> utf8, Span<byte> tokenHash, out byte[] protectedToken)
    {
        protectedToken = [];
        try
        {
            using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions
            {
                MaxDepth = MaxDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var versionNumber)
                || versionNumber != SchemaVersion
                || !root.TryGetProperty("tokenHash", out var hash)
                || hash.ValueKind != JsonValueKind.String
                || !PairingToken.TryParseHashText(hash.GetString(), tokenHash)
                || !root.TryGetProperty("protectedToken", out var copy)
                || copy.ValueKind != JsonValueKind.String
                || !copy.TryGetBytesFromBase64(out var bytes)
                || bytes.Length == 0)
            {
                return false;
            }

            protectedToken = bytes;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // Not JSON, a byte-order mark, invalid UTF-8, text after the object, nested too deep, or a
            // text value with an escape that is not a character.
            return false;
        }
    }
}
