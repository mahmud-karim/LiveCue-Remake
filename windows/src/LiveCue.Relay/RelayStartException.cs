namespace LiveCue.Relay;

/// <summary>Why the relay could not start.</summary>
public enum RelayStartFailure
{
    /// <summary>Another process (or another relay) already listens on the port.</summary>
    PortInUse,

    /// <summary>Anything else; see the inner exception.</summary>
    Other,
}

/// <summary>The relay could not bind its port. The host is back in the Stopped state.</summary>
public sealed class RelayStartException : Exception
{
    public RelayStartException()
        : this("The relay could not start.")
    {
    }

    public RelayStartException(string message)
        : base(message)
    {
    }

    public RelayStartException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public RelayStartException(RelayStartFailure failure, int port, Exception innerException)
        : base(Describe(failure, port), innerException)
    {
        Failure = failure;
        Port = port;
    }

    /// <summary>The kind of failure, for hosts that word their own message.</summary>
    public RelayStartFailure Failure { get; } = RelayStartFailure.Other;

    /// <summary>The port that was requested.</summary>
    public int Port { get; }

    private static string Describe(RelayStartFailure failure, int port) => failure switch
    {
        RelayStartFailure.PortInUse => $"Port {port} is already in use on this PC.",
        _ => $"The relay could not start on port {port}.",
    };
}
