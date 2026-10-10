namespace LiveCue.Relay;

/// <summary>Why a relay could not start or had to stop. Wire names are in <see cref="RelayFaultCodes"/>.</summary>
public enum RelayFaultCode
{
    PortInUse,
    PairingStoreUnreadable,
    InvalidEndpoint,
    StartFailed,
    Internal,
}

/// <summary>The upper-case names of architecture 3.4, for JSON lines and logs.</summary>
public static class RelayFaultCodes
{
    public static string Name(RelayFaultCode code) => code switch
    {
        RelayFaultCode.PortInUse => "PORT_IN_USE",
        RelayFaultCode.PairingStoreUnreadable => "PAIRING_STORE_UNREADABLE",
        RelayFaultCode.InvalidEndpoint => "INVALID_ENDPOINT",
        RelayFaultCode.StartFailed => "START_FAILED",
        _ => "INTERNAL",
    };
}

/// <summary>Thrown by <see cref="IRelay.StartAsync"/>. The message is a fixed English sentence per code.</summary>
public sealed class RelayStartException : Exception
{
    public RelayStartException(RelayFaultCode code, string fixedMessage)
        : base(fixedMessage)
    {
        Code = code;
    }

    public RelayFaultCode Code { get; }
}
