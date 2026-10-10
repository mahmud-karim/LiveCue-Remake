using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Hosting;

/// <summary>One entry of the ledger of Tailscale mappings LiveCue created (architecture 3.9).</summary>
public sealed record OwnedMapping(string Mode, int HttpsPort, string Path, string Target, DateTimeOffset CreatedAt);

/// <summary>
/// The contents of settings.json (architecture 3.5). Nothing secret is stored here. A missing file
/// means these defaults.
/// </summary>
public sealed partial record HostSettings
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>The port the speech lab's own page uses; the lab port setting may not take it.</summary>
    private const int LabPagePort = 8765;

    private static readonly int[] FunnelPorts = [443, 8443, 10000];

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Loopback port of the relay. 1024 to 65535, never 47831, not equal to <see cref="AsrLabPort"/>.</summary>
    public int RelayPort { get; init; } = ProtocolConstants.DefaultRelayPort;

    /// <summary>How phones reach this PC.</summary>
    public ConnectionMode ConnectionMode { get; init; } = ConnectionMode.Private;

    /// <summary>HTTPS port of the Private tailnet mapping. 1 to 65535, not equal to <see cref="PublicHttpsPort"/>.</summary>
    public int PrivateHttpsPort { get; init; } = 11443;

    /// <summary>HTTPS port of the Public Funnel mapping: 443, 8443 or 10000, the ports Tailscale Funnel can use.</summary>
    public int PublicHttpsPort { get; init; } = 10000;

    /// <summary>Path prefix of the Public Funnel mapping, like "/livecue". Never "/".</summary>
    public string PublicPathPrefix { get; init; } = "/livecue";

    /// <summary>Null, or the absolute path of codex.exe. Null means: find it.</summary>
    public string? CodexExecutablePath { get; init; }

    /// <summary>Null, or the absolute path of the speech lab folder.</summary>
    public string? AsrLabFolder { get; init; }

    /// <summary>Loopback port of the speech lab. 1024 to 65535, never 47831 or 8765, not equal to <see cref="RelayPort"/>.</summary>
    public int AsrLabPort { get; init; } = 47842;

    /// <summary>Tailscale mappings LiveCue created and may therefore remove.</summary>
    public IReadOnlyList<OwnedMapping> OwnedMappings { get; init; } = [];

    /// <summary>
    /// Every rule of architecture 3.5 this record breaks, in words; empty when the settings are valid.
    /// (Whether the Codex file and the lab folder exist is a prerequisite check, not a settings rule here.)
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (SchemaVersion != CurrentSchemaVersion)
        {
            problems.Add($"schemaVersion must be {CurrentSchemaVersion}.");
        }

        if (!ProtocolConstants.IsAllowedRelayPort(RelayPort))
        {
            problems.Add($"relayPort must be between 1024 and 65535 and never {ProtocolConstants.PreviousRelayPort}.");
        }

        if (!Enum.IsDefined(ConnectionMode))
        {
            problems.Add("connectionMode must be private or public.");
        }

        if (PrivateHttpsPort is < 1 or > 65535 || PrivateHttpsPort == PublicHttpsPort)
        {
            problems.Add("privateHttpsPort must be between 1 and 65535 and differ from publicHttpsPort.");
        }

        if (!FunnelPorts.Contains(PublicHttpsPort))
        {
            problems.Add("publicHttpsPort must be 443, 8443 or 10000.");
        }

        if (PublicPathPrefix is null || !PathPrefixPattern().IsMatch(PublicPathPrefix))
        {
            problems.Add("publicPathPrefix must look like /livecue: a slash, then 1 to 32 lower-case letters, digits or hyphens.");
        }

        if (CodexExecutablePath is not null
            && (!Path.IsPathFullyQualified(CodexExecutablePath)
                || !string.Equals(Path.GetFileName(CodexExecutablePath), "codex.exe", StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add("codexExecutablePath must be null or the absolute path of codex.exe.");
        }

        if (AsrLabFolder is not null && !Path.IsPathFullyQualified(AsrLabFolder))
        {
            problems.Add("asrLabFolder must be null or an absolute folder path.");
        }

        if (!ProtocolConstants.IsAllowedRelayPort(AsrLabPort) || AsrLabPort == LabPagePort || AsrLabPort == RelayPort)
        {
            problems.Add($"asrLabPort must be between 1024 and 65535, never {ProtocolConstants.PreviousRelayPort} or {LabPagePort}, and differ from relayPort.");
        }

        if (OwnedMappings is null)
        {
            problems.Add("ownedMappings must be a list.");
        }

        return problems;
    }

    [GeneratedRegex("^/[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex PathPrefixPattern();
}

/// <summary>Writes <see cref="ConnectionMode"/> as "private" or "public" and reads nothing else.</summary>
internal sealed class ConnectionModeJsonConverter : JsonConverter<ConnectionMode>
{
    public override ConnectionMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString() switch
            {
                "private" => ConnectionMode.Private,
                "public" => ConnectionMode.Public,
                _ => throw new JsonException("connectionMode must be private or public."),
            }
            : throw new JsonException("connectionMode must be a string.");

    public override void Write(Utf8JsonWriter writer, ConnectionMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == ConnectionMode.Public ? "public" : "private");
}

/// <summary>settings.json is UTF-8, two-space indented, camel case, in the member order above.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters = [typeof(ConnectionModeJsonConverter)])]
[JsonSerializable(typeof(HostSettings))]
internal sealed partial class HostingJsonContext : JsonSerializerContext;
