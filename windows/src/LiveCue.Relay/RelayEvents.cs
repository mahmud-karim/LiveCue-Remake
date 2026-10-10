namespace LiveCue.Relay;

/// <summary>
/// Something the relay tells its host (architecture 3.4). Every event says when it happened. No event
/// ever contains the pairing token or an <c>Authorization</c> value.
///
/// The spike raises the four kinds below; the others of 3.4 are added as records beside them when
/// their features are built. Hosts switch on the type and ignore kinds they do not know.
/// </summary>
public abstract record RelayEvent(DateTimeOffset At);

/// <summary>The listener is bound.</summary>
/// <param name="At">When it happened.</param>
/// <param name="Endpoint">The advertised endpoint the relay was started with.</param>
/// <param name="Port">The loopback port actually bound.</param>
/// <param name="Mode">The connection mode the edge rules apply.</param>
/// <param name="ProtocolVersion">The protocol version the relay speaks.</param>
/// <param name="RelayVersion">The relay's version text.</param>
/// <param name="PairingWasCreated">True when this start created the pairing rather than restoring one.</param>
public sealed record RelayStarted(
    DateTimeOffset At,
    string Endpoint,
    int Port,
    ConnectionMode Mode,
    int ProtocolVersion,
    string RelayVersion,
    bool PairingWasCreated) : RelayEvent(At);

/// <summary>Why the listener closed.</summary>
public enum RelayStopReason
{
    /// <summary>The host asked for it.</summary>
    Requested,

    /// <summary>A fatal fault ended the relay.</summary>
    Fault,
}

/// <summary>The listener is closed.</summary>
public sealed record RelayStopped(DateTimeOffset At, RelayStopReason Reason) : RelayEvent(At);

/// <summary>Why the relay could not start, or stopped by itself.</summary>
public enum RelayFaultCode
{
    /// <summary>PORT_IN_USE: another process already listens on the relay port.</summary>
    PortInUse,

    /// <summary>PAIRING_STORE_UNREADABLE: the saved pairing exists and could not be read.</summary>
    PairingStoreUnreadable,

    /// <summary>INVALID_ENDPOINT: the advertised endpoint is not in the canonical form.</summary>
    InvalidEndpoint,

    /// <summary>START_FAILED: the listener could not be started for another reason.</summary>
    StartFailed,

    /// <summary>INTERNAL: a fatal fault while running.</summary>
    Internal,
}

/// <summary>Start failed, or a fatal runtime fault. <paramref name="Message"/> is a fixed sentence, never exception text.</summary>
public sealed record RelayFaulted(DateTimeOffset At, RelayFaultCode Code, string Message) : RelayEvent(At);

/// <summary>A request passed authentication just now (protocol 5.5, presence).</summary>
public sealed record PhoneSeen(DateTimeOffset At) : RelayEvent(At);

/// <summary>
/// Something went wrong that the user did not cause and cannot act on directly (architecture 3.10).
/// <paramref name="Message"/> is a fixed description plus, at most, an exception type name: never
/// exception text, a path, an address or anything a phone sent.
/// </summary>
public sealed record Diagnostic(DateTimeOffset At, string Source, string Message) : RelayEvent(At);
