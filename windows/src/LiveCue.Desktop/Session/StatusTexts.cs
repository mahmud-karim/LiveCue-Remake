using System.Globalization;
using LiveCue.Relay;

namespace LiveCue.Desktop.Session;

/// <summary>
/// The status indicators of the relay and the phone (W-design 5.4, "Status texts"; screens 2.3, 2.4 and
/// 1.3). Every state has its own mark shape as well as its own colour, and always a label in words.
/// </summary>
internal static class StatusTexts
{
    public const string Stopped = "Stopped";
    public const string Checking = "Checking Codex and Tailscale…";
    public const string Ready = "Ready";
    public const string Paused = "Paused";
    public const string Thinking = "Thinking…";
    public const string RequestFailed = "Request failed. Ready to retry.";

    /// <summary>The caption under the "Paused" status.</summary>
    public const string PausedCaption = "New requests from the iPhone are refused until you resume.";

    /// <summary>The text of a fault while running (R-08).</summary>
    public const string RelayStoppedByItself = "Relay stopped. Click Start relay to reconnect.";

    /// <summary>What a failed start shows when it has no text of its own.</summary>
    public const string CouldNotStart = "Could not start the relay.";

    public const string PhoneNotConnected = "iPhone not connected";
    public const string PhoneConnected = "iPhone connected";
    public const string PhoneIdle = "iPhone idle";

    /// <summary>The second sentence of the phone tooltip (screens 2.4).</summary>
    public const string IdleIsNotOffline =
        "Idle does not prove the phone is offline: iOS stops the heartbeat when the app is in the background.";

    /// <summary>The full status of the Live page.</summary>
    public static StatusIndicator Relay(RelayUiState state, string? stateText, RelayFaultCode? fault) => state switch
    {
        RelayUiState.Checking or RelayUiState.Starting => new(StatusMark.Spinner, StatusTone.Live, Checking),
        RelayUiState.PrerequisiteFailed => new(StatusMark.Alert, StatusTone.Danger, stateText ?? SidebarCannotStart),
        RelayUiState.Ready => new(StatusMark.Dot, StatusTone.Success, Ready),
        RelayUiState.Paused => new(StatusMark.Pause, StatusTone.Warning, Paused),
        RelayUiState.Thinking => new(StatusMark.Spinner, StatusTone.Live, Thinking),
        RelayUiState.RequestFailed => new(StatusMark.Alert, StatusTone.Danger, RequestFailed),
        RelayUiState.Faulted => fault == RelayFaultCode.Internal
            ? new(StatusMark.Dot, StatusTone.Warning, stateText ?? RelayStoppedByItself)
            : new(StatusMark.Alert, StatusTone.Danger, stateText ?? CouldNotStart),
        _ => new(StatusMark.Ring, StatusTone.Offline, Stopped),
    };

    /// <summary>The short label of the sidebar card, with the mark and colour of the full status.</summary>
    public static StatusIndicator Sidebar(RelayUiState state, RelayFaultCode? fault) =>
        Relay(state, null, fault) with { Text = SidebarLabel(state) };

    /// <summary>The short labels of screens 1.3.</summary>
    public static string SidebarLabel(RelayUiState state) => state switch
    {
        RelayUiState.Checking or RelayUiState.Starting => "Checking…",
        RelayUiState.PrerequisiteFailed => SidebarCannotStart,
        RelayUiState.Ready => Ready,
        RelayUiState.Paused => Paused,
        RelayUiState.Thinking => Thinking,
        RelayUiState.RequestFailed => "Request failed",
        RelayUiState.Faulted => "Relay stopped",
        _ => Stopped,
    };

    public static StatusIndicator Phone(PhonePresence presence, string? toolTip = null) => presence switch
    {
        PhonePresence.Connected => new(StatusMark.Dot, StatusTone.Success, PhoneConnected, toolTip),
        PhonePresence.Idle => new(StatusMark.Dot, StatusTone.Warning, PhoneIdle, toolTip),
        _ => new(StatusMark.Ring, StatusTone.Offline, PhoneNotConnected, toolTip),
    };

    /// <summary>The phone indicator with its tooltip, which exists once a phone was seen.</summary>
    public static StatusIndicator Phone(PhonePresence presence, DateTimeOffset? lastSeen, DateTimeOffset now) =>
        Phone(presence, PhoneToolTip(lastSeen, now));

    /// <summary>
    /// "Last authenticated heartbeat: N seconds ago. Idle does not prove…", or null before the first
    /// authenticated request since the relay started.
    /// </summary>
    public static string? PhoneToolTip(DateTimeOffset? lastSeen, DateTimeOffset now)
    {
        if (lastSeen is not { } seen)
        {
            return null;
        }

        var seconds = Math.Max(0, (long)Math.Floor((now - seen).TotalSeconds));
        var age = seconds == 1 ? "1 second" : seconds.ToString(CultureInfo.InvariantCulture) + " seconds";
        return $"Last authenticated heartbeat: {age} ago. {IdleIsNotOffline}";
    }

    private const string SidebarCannotStart = "Cannot start yet";
}
