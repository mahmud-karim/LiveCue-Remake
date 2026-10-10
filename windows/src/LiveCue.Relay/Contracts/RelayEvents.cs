namespace LiveCue.Relay;

/// <summary>
/// Base of every event (architecture 3.4). Published on thread-pool threads, never under a lock that a
/// request holds. No event has a member that carries the token or an Authorization value.
/// </summary>
public abstract record RelayEvent(DateTimeOffset At);

public enum RelayStopReason { Requested, Fault }

public enum SelfCheckResult { Verified, VerifiedThroughFunnel, NotVerified }

public enum NoticeKind { PairingRestored, PairingReset }

public enum AnswerKind { Assist, Summary }

public enum LocalModelState { Stopped, Starting, Ready, Stopping, Error }

public enum SpeechStreamPhase { Loading, Ready, Stopped, Failed }

/// <summary>The listener is bound.</summary>
public sealed record RelayStarted(
    DateTimeOffset At,
    string Endpoint,
    int Port,
    ConnectionMode Mode,
    int ProtocolVersion,
    string RelayVersion,
    bool PairingWasCreated) : RelayEvent(At);

/// <summary>The listener is closed. Always the last event of an instance that had started.</summary>
public sealed record RelayStopped(DateTimeOffset At, RelayStopReason Reason) : RelayEvent(At);

/// <summary>Start failed, or a fatal runtime fault (then followed by RelayStopped with Reason Fault).</summary>
public sealed record RelayFaulted(DateTimeOffset At, RelayFaultCode Code, string Message) : RelayEvent(At);

/// <summary>A pause or resume took effect.</summary>
public sealed record AcceptingChanged(DateTimeOffset At, bool Accepting) : RelayEvent(At);

/// <summary>Protocol 5.5 presence: one per request that passed E-3, except the self-check.</summary>
public sealed record PhoneSeen(DateTimeOffset At) : RelayEvent(At);

/// <summary>ShowPairingAsync or ResetPairingAsync produced a matrix. The matrix is only in the return value.</summary>
public sealed record PairingShown(DateTimeOffset At, string Endpoint) : RelayEvent(At);

/// <summary>Protocol 4.5. Exactly one per PairingShown.</summary>
public sealed record SelfCheckCompleted(DateTimeOffset At, SelfCheckResult Result) : RelayEvent(At);

/// <summary>Informational. Message is a fixed text of section 4.12.</summary>
public sealed record Notice(DateTimeOffset At, NoticeKind Kind, string Message) : RelayEvent(At);

/// <summary>An answer request passed R-7. PayloadJson is the request body as compact JSON.</summary>
public sealed record RequestStarted(
    DateTimeOffset At,
    string RequestId,
    AnswerKind Kind,
    string Model,
    string ReasoningEffort,
    string? Transcript,
    string? PartialTranscript,
    string? Instruction,
    string PayloadJson) : RelayEvent(At);

/// <summary>An answer request succeeded. DurationMs equals relayTotalMs. PayloadJson is the reply.</summary>
public sealed record RequestCompleted(
    DateTimeOffset At,
    string RequestId,
    AnswerKind Kind,
    double DurationMs,
    double CodexMs,
    string Model,
    string ReasoningEffort,
    string PayloadJson) : RelayEvent(At);

/// <summary>
/// An answer request ended any other way after RequestStarted. Code is a wire code of
/// <see cref="Protocol.RelayCodes"/>, or CLIENT_DISCONNECTED. Message is the relay text of that code.
/// </summary>
public sealed record RequestFailed(
    DateTimeOffset At,
    string RequestId,
    AnswerKind Kind,
    string Code,
    string Message) : RelayEvent(At);

/// <summary>The catalogue could not be read, or a selection was not in it.</summary>
public sealed record AssistantNotice(DateTimeOffset At, string Code, string Message) : RelayEvent(At);

/// <summary>The five-member status object of protocol 9.1.</summary>
public sealed record LocalModelStatus(
    LocalModelState State,
    string? Model,
    bool Busy,
    int ElapsedSeconds,
    string Message);

/// <summary>A model operation began or ended. Message equals Status.Message.</summary>
public sealed record LocalModelChanged(DateTimeOffset At, LocalModelStatus Status, string Message) : RelayEvent(At);

/// <summary>Stream progress. Code is set only for Failed. Message is a fixed text of section 4.12.</summary>
public sealed record SpeechStreamChanged(
    DateTimeOffset At,
    SpeechStreamPhase Phase,
    string Model,
    string? Code,
    string Message) : RelayEvent(At);

/// <summary>The text of one speechComplete frame, cut like the frame (C-22).</summary>
public sealed record SpeechText(DateTimeOffset At, string Text) : RelayEvent(At);

/// <summary>An internal problem: an exception type name and a fixed description, never exception text.</summary>
public sealed record Diagnostic(DateTimeOffset At, string Source, string Message) : RelayEvent(At);
