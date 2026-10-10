using System.Reflection;

namespace LiveCue.Relay.Protocol;

/// <summary>
/// Numbers and shapes of relay protocol 2 (docs/architecture/02-relay-protocol.md, section 2), each
/// named by its id there. Only the constants the relay uses so far are present.
/// </summary>
public static class ProtocolConstants
{
    /// <summary>C-01. Protocol version, sent by the phone in <c>X-LiveCue-Protocol</c>.</summary>
    public const int ProtocolVersion = 2;

    /// <summary>C-02. A pairing token is 32 random bytes as 43 unpadded base64url characters.</summary>
    public const int TokenLength = 43;

    /// <summary>C-10. Hard limit of an HTTP request body, in bytes.</summary>
    public const int MaxRequestBodyBytes = 131_072;

    /// <summary>C-19. Longest request line, in bytes.</summary>
    public const int MaxRequestLineBytes = 2_048;

    /// <summary>C-19. Total size of all request headers, in bytes.</summary>
    public const int MaxRequestHeadersBytes = 16_384;

    /// <summary>C-19. Most request headers.</summary>
    public const int MaxRequestHeaderCount = 64;

    /// <summary>C-30. Most TCP connections to the relay at once.</summary>
    public const int MaxConnections = 64;

    /// <summary>C-40. Request headers must arrive within this time.</summary>
    public static readonly TimeSpan RequestHeadersTimeout = TimeSpan.FromSeconds(10);

    /// <summary>C-42. An idle keep-alive connection is closed after this time.</summary>
    public static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Protocol 5.1. A request body slower than this, after the grace period, is aborted.</summary>
    public const double MinRequestBodyBytesPerSecond = 240;

    /// <summary>Protocol 5.1. Grace period before the minimum body rate applies.</summary>
    public static readonly TimeSpan MinRequestBodyRateGrace = TimeSpan.FromSeconds(5);

    /// <summary>Protocol 5.1. The loopback port the relay listens on unless told otherwise.</summary>
    public const int DefaultRelayPort = 47841;

    /// <summary>
    /// The port of the previous LiveCue relay. The remake runs side by side with it, so this port is
    /// never offered, suggested or accepted anywhere.
    /// </summary>
    public const int PreviousRelayPort = 47831;

    /// <summary>Architecture 3.11. Connections still open this long after a stop began are aborted.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2.5);

    /// <summary>The relay's own version text, as reported by <c>GET /v1/health</c> (for example "1.0.0").</summary>
    public static string RelayVersion { get; } = ReadVersion();

    /// <summary>Says whether <paramref name="port"/> is a loopback port the relay may be given.</summary>
    public static bool IsAllowedRelayPort(int port) => port is >= 1024 and <= 65535 and not PreviousRelayPort;

    private static string ReadVersion()
    {
        var informational = typeof(ProtocolConstants).Assembly
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
