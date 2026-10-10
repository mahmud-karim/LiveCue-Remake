using System.Reflection;

namespace LiveCue.Relay;

/// <summary>Constants of the relay that both hosts and tests need to agree on.</summary>
public static class RelayDefaults
{
    /// <summary>The loopback port the relay listens on unless told otherwise.</summary>
    public const int Port = 47841;

    /// <summary>
    /// The port of the previous LiveCue relay. The remake runs side by side with it, so this relay
    /// refuses to bind it.
    /// </summary>
    public const int LegacyRelayPort = 47831;

    /// <summary>Version of the phone-to-relay protocol this relay speaks.</summary>
    public const int ProtocolVersion = 2;

    /// <summary>The relay's own version, as reported to the phone (for example "1.0.0").</summary>
    public static string RelayVersion { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(RelayDefaults).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0";
        }

        // Drop build metadata ("1.0.0+abc123") if a build ever adds it.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
