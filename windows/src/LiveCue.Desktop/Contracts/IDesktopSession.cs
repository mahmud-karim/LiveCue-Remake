using System.ComponentModel;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop;

/// <summary>The relay status machine of architecture 3.12.</summary>
public enum RelayUiState
{
    Stopped,
    Checking,
    PrerequisiteFailed,
    Starting,
    Ready,
    Paused,
    Thinking,
    RequestFailed,
    Faulted,
}

/// <summary>The phone presence machine of architecture 3.12.</summary>
public enum PhonePresence { NotConnected, Connected, Idle }

/// <summary>Moments that more than one page reacts to.</summary>
public enum SessionSignal
{
    /// <summary>A start passed its checks. The activity log is cleared; transcript and reply are kept.</summary>
    RelayStarting,

    /// <summary>The relay stopped or faulted: stopped layout (QR cleared, hints reset).</summary>
    RelayEnded,

    /// <summary>"Clear activity": log, transcript and reply are emptied.</summary>
    ActivityCleared,
}

public enum SessionMessageKind { Info, Warning, Error }

/// <summary>An activity line produced by the desktop itself (mapping results, settings problems, a failed
/// command), as opposed to one derived from a relay event.</summary>
public sealed record SessionMessage(DateTimeOffset At, SessionMessageKind Kind, string Text);

/// <summary>
/// The desktop's one model of the supervisor and the relay. It reads the supervisor's events on a
/// background task, posts each to the UI thread, updates its own state, then raises its events. An
/// exception thrown by a handler of one event is caught, reported as a "Relay diagnostic: " activity line
/// and never stops later events (WIN-10). Every member is used on the UI thread; every command method is
/// single-flight (a second call while one runs returns at once) and never throws.
/// </summary>
public interface IDesktopSession : INotifyPropertyChanged
{
    RelayUiState State { get; }

    /// <summary>The text of PrerequisiteFailed and Faulted; null in every other state. A fault text stays
    /// until the next start.</summary>
    string? StateText { get; }

    RelayFaultCode? FaultCode { get; }

    /// <summary>True in Ready, Paused, Thinking and RequestFailed.</summary>
    bool IsRelayRunning { get; }

    bool IsPaused { get; }

    PhonePresence Presence { get; }

    /// <summary>Time of the last PhoneSeen since the relay started; null before the first.</summary>
    DateTimeOffset? LastPhoneSeen { get; }

    /// <summary>Endpoint of the last RelayStarted. Kept after the relay stops.</summary>
    string? AdvertisedEndpoint { get; }

    /// <summary>The report of the most recent check, or null before the first.</summary>
    PrerequisiteReport? Prerequisites { get; }

    /// <summary>The most recent mapping inspection, or null before the first.</summary>
    ConnectionInspection? Connection { get; }

    /// <summary>True when the relay runs and the selected mode's mapping is Present or PresentNotOwned.</summary>
    bool IsConnectionReady { get; }

    /// <summary>The settings as last loaded or saved.</summary>
    RelaySettings Settings { get; }

    /// <summary>State of settings.json at the last load.</summary>
    SettingsLoadResult SettingsLoad { get; }

    /// <summary>True when the relay runs with a port, mode or endpoint that differs from Settings.</summary>
    bool SettingsDifferFromRunning { get; }

    /// <summary>Every relay event, after the session updated its own state.</summary>
    event EventHandler<RelayEvent>? RelayEventReceived;

    /// <summary>A matrix returned by ShowPairing or ResetPairing, including the automatic first-start one.</summary>
    event EventHandler<PairingQr>? PairingQrProduced;

    event EventHandler<SessionSignal>? Signal;

    event EventHandler<SessionMessage>? MessageRaised;

    /// <summary>Checks, then starts. Called once by the shell when the window has rendered, and by Start.</summary>
    Task StartAsync();

    Task StopAsync();

    /// <summary>Pause when accepting, resume when paused. State changes only on AcceptingChanged.</summary>
    Task TogglePauseAsync();

    /// <summary>Checks only ("Check again"). Also refreshes the mapping inspection.</summary>
    Task CheckAgainAsync();

    /// <summary>One ShowPairingAsync. Does nothing unless IsConnectionReady.</summary>
    Task ShowPairingAsync();

    /// <summary>Reset through the running relay, or through the supervisor when stopped. True on success.</summary>
    Task<bool> ResetPairingAsync();

    /// <summary>Pure: the plan that sets up the selected mode's mapping, or null with the refusal.</summary>
    MappingPlan? PlanSetUp(out MappingRefusal refusal);

    /// <summary>Pure: the plan that removes one mapping of the ledger.</summary>
    MappingPlan PlanRemoval(OwnedMapping entry);

    /// <summary>Runs a confirmed plan off the UI thread. Called by the mapping dialog only (TS-9).</summary>
    Task<MappingResult> ExecuteMappingAsync(MappingPlan plan);

    /// <summary>Validates and saves. Returns the problems; empty means saved.</summary>
    IReadOnlyList<SettingsProblem> SaveSettings(RelaySettings settings);

    /// <summary>Raises ActivityCleared.</summary>
    void ClearActivity();
}
