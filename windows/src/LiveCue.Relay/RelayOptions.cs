using LiveCue.Relay.Authentication;

namespace LiveCue.Relay;

/// <summary>What a host passes to <see cref="IRelayHost.StartAsync"/>.</summary>
public sealed record RelayOptions
{
    /// <summary>Decides whether a presented bearer token belongs to a paired phone.</summary>
    public required IRelayAuthenticator Authenticator { get; init; }

    /// <summary>
    /// Loopback TCP port to listen on. 0 asks the system for a free port (used by tests and the
    /// desktop self-test); the port actually bound is reported by <see cref="RelayEvent.Started"/>.
    /// </summary>
    public int Port { get; init; } = RelayDefaults.Port;

    /// <summary>How long <see cref="IRelayHost.StopAsync"/> waits for requests in flight.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Says whether <paramref name="port"/> is one the relay may bind.</summary>
    public static bool IsAllowedPort(int port) =>
        port is 0 or (>= 1024 and <= 65535) && port != RelayDefaults.LegacyRelayPort;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Authenticator);

        if (Port == RelayDefaults.LegacyRelayPort)
        {
            throw new ArgumentException(
                $"Port {RelayDefaults.LegacyRelayPort} belongs to the previous LiveCue relay and is never used by this one.",
                nameof(Port));
        }

        if (!IsAllowedPort(Port))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Port), Port, "The relay port must be 0 (any free port) or between 1024 and 65535.");
        }

        if (ShutdownTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownTimeout), ShutdownTimeout, "The shutdown timeout cannot be negative.");
        }
    }
}
