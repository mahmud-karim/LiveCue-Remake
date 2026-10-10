using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.Infrastructure;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.ViewModels;

/// <summary>
/// The relay as the window shows it (architecture 3.12, "Relay status"): a plain enum, never keyword
/// matching on status text. The spike has four of the nine states; Checking, PrerequisiteFailed,
/// Paused, Thinking and RequestFailed arrive with their features.
/// </summary>
public enum RelayState
{
    Stopped,
    Starting,
    Ready,
    Faulted,
}

/// <summary>Whether a paired phone is talking to the relay (architecture 3.12, "Phone presence").</summary>
public enum PhonePresence
{
    /// <summary>No authenticated request since the relay started.</summary>
    NotConnected,

    /// <summary>The last authenticated request was at most 15 seconds ago.</summary>
    Connected,

    /// <summary>Older than that. It never returns to NotConnected by timeout, only when the relay stops.</summary>
    Idle,
}

/// <summary>
/// Starts and stops the relay through the supervisor and turns relay events into what the window
/// shows. Events arrive on thread-pool threads and are applied on the UI thread through
/// <see cref="IUiDispatcher"/>; every state change here happens on that one thread.
/// </summary>
public sealed partial class RelayViewModel : ObservableObject, IDisposable
{
    /// <summary>A phone is "connected" while its last authenticated request is at most this old.</summary>
    public static readonly TimeSpan ConnectedWindow = TimeSpan.FromSeconds(15);

    private const string PortInUseText =
        "The relay port is already in use. Close the other LiveCue relay, then click Start relay.";

    private const string CouldNotStartText = "Could not start the relay.";

    private readonly RelaySupervisor _supervisor;
    private readonly IUiDispatcher _ui;
    private readonly ActivityLog _activity;
    private readonly TimeProvider _clock;
    private readonly ITimer _idleTimer;
    private DateTimeOffset _lastSeen;

    public RelayViewModel(RelaySupervisor supervisor, IUiDispatcher ui, ActivityLog activity, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(clock);

        _supervisor = supervisor;
        _ui = ui;
        _activity = activity;
        _clock = clock;
        _idleTimer = clock.CreateTimer(_ => _ui.Post(ApplyIdleCheck), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _supervisor.EventReceived += OnRelayEvent;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(ShortStatusText), nameof(StatusKind), nameof(IsReady), nameof(ShowsStart))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    public partial RelayState State { get; private set; }

    /// <summary>Why the last start failed, in the window's words. It stays until the next start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? FaultText { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhoneText), nameof(PhoneKind))]
    public partial PhonePresence Presence { get; private set; }

    /// <summary>The advertised address of the current or last run; null until the relay has started once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PcAddressText), nameof(HasPcAddress))]
    public partial string? PcAddress { get; private set; }

    public bool IsReady => State == RelayState.Ready;

    /// <summary>True while the page header offers "Start relay" (stopped, or failed to start).</summary>
    public bool ShowsStart => State != RelayState.Ready;

    /// <summary>The full status text of the Live page's status strip.</summary>
    public string StatusText => State switch
    {
        RelayState.Starting => "Starting…",
        RelayState.Ready => "Ready",
        RelayState.Faulted => FaultText ?? CouldNotStartText,
        _ => "Stopped",
    };

    /// <summary>The short label of the sidebar's status card.</summary>
    public string ShortStatusText => State switch
    {
        RelayState.Starting => "Starting…",
        RelayState.Ready => "Ready",
        RelayState.Faulted => "Cannot start yet",
        _ => "Stopped",
    };

    public StatusKind StatusKind => State switch
    {
        RelayState.Starting => StatusKind.Live,
        RelayState.Ready => StatusKind.Success,
        RelayState.Faulted => StatusKind.Danger,
        _ => StatusKind.Neutral,
    };

    public string PhoneText => Presence switch
    {
        PhonePresence.Connected => "iPhone connected",
        PhonePresence.Idle => "iPhone idle",
        _ => "iPhone not connected",
    };

    public StatusKind PhoneKind => Presence switch
    {
        PhonePresence.Connected => StatusKind.Success,
        PhonePresence.Idle => StatusKind.Warning,
        _ => StatusKind.Neutral,
    };

    public bool HasPcAddress => PcAddress is not null;

    public string PcAddressText => PcAddress ?? "Shown when the relay is running";

    public void Dispose()
    {
        _supervisor.EventReceived -= OnRelayEvent;
        _idleTimer.Dispose();
    }

    private bool CanStart() => State is RelayState.Stopped or RelayState.Faulted;

    private bool CanStop() => State == RelayState.Ready;

    /// <summary>"Start relay". Starting also clears the activity log.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        _activity.Clear();
        FaultText = null;
        State = RelayState.Starting;
        try
        {
            await _supervisor.StartAsync();
        }
        catch (RelayStartException exception)
        {
            // RelayFaulted normally got here first; this covers a relay that threw without raising it.
            ApplyFault(exception.Code);
        }
    }

    /// <summary>"Stop relay". Keeps the activity log and the PC address.</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        await _supervisor.StopAsync();
        ApplyStopped();
    }

    private void OnRelayEvent(object? sender, RelayEvent relayEvent) => _ui.Post(() => Apply(relayEvent));

    private void Apply(RelayEvent relayEvent)
    {
        switch (relayEvent)
        {
            case RelayStarted started:
                FaultText = null;
                PcAddress = started.Endpoint;
                State = RelayState.Ready;
                _activity.Add(started.At, "Relay started. Waiting for your iPhone.");
                if (started.Mode == ConnectionMode.Public)
                {
                    _activity.Add(
                        started.At,
                        "Public mode: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address.");
                }

                break;

            case RelayFaulted faulted:
                ApplyFault(faulted.Code);
                break;

            case RelayStopped:
                ApplyStopped();
                break;

            case PhoneSeen seen when State == RelayState.Ready:
                _lastSeen = seen.At;
                Presence = PhonePresence.Connected;
                _idleTimer.Change(ConnectedWindow, Timeout.InfiniteTimeSpan);
                break;

            case Diagnostic diagnostic:
                _activity.Add(diagnostic.At, "Relay diagnostic: " + diagnostic.Message);
                break;

            default:
                break;
        }
    }

    private void ApplyFault(RelayFaultCode code)
    {
        if (State == RelayState.Faulted)
        {
            return;
        }

        FaultText = code == RelayFaultCode.PortInUse ? PortInUseText : CouldNotStartText;
        ResetPresence();
        State = RelayState.Faulted;
        _activity.Add(FaultText);
    }

    /// <summary>The stopped layout. A fault text already on screen stays instead of being replaced.</summary>
    private void ApplyStopped()
    {
        ResetPresence();
        if (State != RelayState.Faulted)
        {
            State = RelayState.Stopped;
        }
    }

    private void ResetPresence()
    {
        _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Presence = PhonePresence.NotConnected;
    }

    private void ApplyIdleCheck()
    {
        if (Presence == PhonePresence.Connected && _clock.GetUtcNow() - _lastSeen >= ConnectedWindow)
        {
            Presence = PhonePresence.Idle;
        }
    }
}
