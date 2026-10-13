using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using LiveCue.Relay;

namespace LiveCue.Hosting;

/// <summary>
/// The text format of <c>settings.json</c> (architecture 3.5, W-design 4.14): UTF-8 without a byte-order
/// mark, two-space indent, line feeds, and the twelve members in the order of the architecture's example:
///
/// <code>
/// {
///   "schemaVersion": 1,
///   "relayPort": 47841,
///   "connectionMode": "private",
///   "privateHttpsPort": 11443,
///   "publicHttpsPort": 10000,
///   "publicPathPrefix": "/livecue",
///   "codexExecutablePath": null,
///   "codexContainmentCheckedVersion": null,
///   "asrLabFolder": null,
///   "asrLabPort": 47842,
///   "hideFromScreenCapture": true,
///   "ownedMappings": []
/// }
/// </code>
///
/// A ledger entry is <c>{"mode","httpsPort","path","target","createdAt"}</c>, the mode as
/// <c>private</c> or <c>public</c> and the time as UTC to the second.
///
/// Reading separates two things. A file that is not this JSON (not an object, a member missing, a
/// member of the wrong JSON type) is not read at all: the store calls it unreadable. A file of the right
/// types whose values break a rule is read as it is, and the validator names the problems. For that, a
/// value that has the right JSON type but cannot be held by the settings record is read as a value that
/// no rule accepts: a number that is not a 32-bit integer becomes -1, and a mode that is neither word
/// becomes <see cref="NotAMode"/>.
/// </summary>
internal static class SettingsJson
{
    public const string PrivateMode = "private";
    public const string PublicMode = "public";

    /// <summary>What a mode text other than the two words is read as. No rule accepts it.</summary>
    public const ConnectionMode NotAMode = (ConnectionMode)(-1);

    /// <summary>What a number that is not a 32-bit integer is read as. No rule accepts it.</summary>
    public const int NotAnInteger = -1;

    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    // settings.json is an object that holds one array of flat objects. Anything deeper is not one.
    private const int MaxDepth = 8;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",

        // Paths and version texts stay readable: only what JSON itself requires is escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        MaxDepth = MaxDepth,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>The bytes of a settings file. Members the application does not know are never written.</summary>
    public static byte[] Write(RelaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", settings.SchemaVersion);
            writer.WriteNumber("relayPort", settings.RelayPort);
            writer.WriteString("connectionMode", ModeText(settings.ConnectionMode));
            writer.WriteNumber("privateHttpsPort", settings.PrivateHttpsPort);
            writer.WriteNumber("publicHttpsPort", settings.PublicHttpsPort);
            writer.WriteString("publicPathPrefix", settings.PublicPathPrefix);
            writer.WriteString("codexExecutablePath", settings.CodexExecutablePath);
            writer.WriteString("codexContainmentCheckedVersion", settings.CodexContainmentCheckedVersion);
            writer.WriteString("asrLabFolder", settings.AsrLabFolder);
            writer.WriteNumber("asrLabPort", settings.AsrLabPort);
            writer.WriteBoolean("hideFromScreenCapture", settings.HideFromScreenCapture);

            writer.WriteStartArray("ownedMappings");
            foreach (var mapping in settings.OwnedMappings)
            {
                writer.WriteStartObject();
                writer.WriteString("mode", ModeText(mapping.Mode));
                writer.WriteNumber("httpsPort", mapping.HttpsPort);
                writer.WriteString("path", mapping.Path);
                writer.WriteString("target", mapping.Target);
                writer.WriteString("createdAt", mapping.CreatedAt.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var text = buffer.WrittenSpan;
        var bytes = new byte[text.Length + 1];
        text.CopyTo(bytes);
        bytes[^1] = (byte)'\n';
        return bytes;
    }

    /// <summary>
    /// Reads the bytes of a settings file. False unless the text is one JSON object that has the ten
    /// members of the first format with their JSON types, and <c>ownedMappings</c> as an array of objects
    /// with their five members. The two members the review added may be absent: an absent
    /// <c>codexContainmentCheckedVersion</c> is null and an absent <c>hideFromScreenCapture</c> is true.
    /// Members the application does not know are ignored. A byte-order mark in front, as some editors
    /// write it, is accepted.
    /// </summary>
    public static bool TryRead(ReadOnlyMemory<byte> utf8, [NotNullWhen(true)] out RelaySettings? settings)
    {
        settings = null;
        if (utf8.Span.StartsWith(ByteOrderMark))
        {
            utf8 = utf8[ByteOrderMark.Length..];
        }

        try
        {
            using var document = JsonDocument.Parse(utf8, ReaderOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryInteger(root, "schemaVersion", out var schemaVersion)
                || !TryInteger(root, "relayPort", out var relayPort)
                || !TryText(root, "connectionMode", out var connectionMode)
                || !TryInteger(root, "privateHttpsPort", out var privateHttpsPort)
                || !TryInteger(root, "publicHttpsPort", out var publicHttpsPort)
                || !TryText(root, "publicPathPrefix", out var publicPathPrefix)
                || !TryTextOrNull(root, "codexExecutablePath", mayBeAbsent: false, out var codexExecutablePath)
                || !TryTextOrNull(root, "codexContainmentCheckedVersion", mayBeAbsent: true, out var checkedVersion)
                || !TryTextOrNull(root, "asrLabFolder", mayBeAbsent: false, out var asrLabFolder)
                || !TryInteger(root, "asrLabPort", out var asrLabPort)
                || !TryBoolean(root, "hideFromScreenCapture", whenAbsent: true, out var hideFromScreenCapture)
                || !TryLedger(root, out var ownedMappings))
            {
                return false;
            }

            settings = new RelaySettings
            {
                SchemaVersion = schemaVersion,
                RelayPort = relayPort,
                ConnectionMode = ParseMode(connectionMode),
                PrivateHttpsPort = privateHttpsPort,
                PublicHttpsPort = publicHttpsPort,
                PublicPathPrefix = publicPathPrefix,
                CodexExecutablePath = codexExecutablePath,
                CodexContainmentCheckedVersion = checkedVersion,
                AsrLabFolder = asrLabFolder,
                AsrLabPort = asrLabPort,
                HideFromScreenCapture = hideFromScreenCapture,
                OwnedMappings = ownedMappings,
            };
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // Not JSON, invalid UTF-8, text after the object, nested too deep, or a text value with an
            // escape that is not a character.
            return false;
        }
    }

    private static string ModeText(ConnectionMode mode) => mode == ConnectionMode.Public ? PublicMode : PrivateMode;

    private static ConnectionMode ParseMode(string text) => text switch
    {
        PrivateMode => ConnectionMode.Private,
        PublicMode => ConnectionMode.Public,
        _ => NotAMode,
    };

    private static bool TryLedger(JsonElement root, [NotNullWhen(true)] out OwnedMapping[]? ledger)
    {
        ledger = null;
        if (!root.TryGetProperty("ownedMappings", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var entries = new List<OwnedMapping>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !TryText(element, "mode", out var mode)
                || !TryInteger(element, "httpsPort", out var httpsPort)
                || !TryText(element, "path", out var path)
                || !TryText(element, "target", out var target)
                || !element.TryGetProperty("createdAt", out var createdAtMember)
                || createdAtMember.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            // A text that is not an ISO 8601 time makes this one entry invalid, not the file
            // unreadable, so that the other entries of the ledger stay known (architecture 3.5).
            var timeIsReadable = createdAtMember.TryGetDateTimeOffset(out var createdAt);
            entries.Add(new OwnedMapping(
                timeIsReadable ? ParseMode(mode) : NotAMode,
                httpsPort,
                path,
                target,
                timeIsReadable ? createdAt.ToUniversalTime() : default));
        }

        ledger = [.. entries];
        return true;
    }

    /// <summary>A JSON number. One that is not a 32-bit integer is read as <see cref="NotAnInteger"/>.</summary>
    private static bool TryInteger(JsonElement parent, string name, out int value)
    {
        value = NotAnInteger;
        if (!parent.TryGetProperty(name, out var member) || member.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (!member.TryGetInt32(out value))
        {
            value = NotAnInteger;
        }

        return true;
    }

    private static bool TryText(JsonElement parent, string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var member) || member.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = member.GetString();
        return value is not null;
    }

    private static bool TryTextOrNull(JsonElement parent, string name, bool mayBeAbsent, out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var member))
        {
            return mayBeAbsent;
        }

        if (member.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (member.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = member.GetString();
        return value is not null;
    }

    private static bool TryBoolean(JsonElement parent, string name, bool whenAbsent, out bool value)
    {
        value = whenAbsent;
        if (!parent.TryGetProperty(name, out var member))
        {
            return true;
        }

        if (member.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = member.GetBoolean();
        return true;
    }
}
