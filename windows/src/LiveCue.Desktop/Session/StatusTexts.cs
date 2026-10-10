using LiveCue.Relay;

namespace LiveCue.Desktop.Session;

/// <summary>
/// The status indicators of the relay and the phone (W-design 5.4, "Status texts"; screens 2.3 and 1.3).
/// </summary>
internal static class StatusTexts
{
    public const string PausedCaption = "New requests from the iPhone are refused until you resume.";

    /// <summary>The text of a fault while running (R-08).</summary>
    public const string RelayStoppedByItself = "Relay stopped. Click Start relay to reconnect.";

    /// <summary>The full status of the Live page.</summary>
    public static StatusIndicator Relay(RelayUiState state, string? stateText, RelayFaultCode? fault) => state switch
    {
        RelayUiState.Checking or RelayUiState.Starting =>
            new(StatusMark.Spinner, StatusTone.Live, "Checking Codex and Tailscale…"),
        RelayUiState.PrerequisiteFailed => new(StatusMark.Alert, StatusTone.Danger, stateText ?? "Cannot start yet"),
        RelayUiState.Ready => new(StatusMark.Dot, StatusTone.Success, "Ready"),
        RelayUiState.Paused => new(StatusMark.Pause, StatusTone.Warning, "Paused"),
        RelayUiState.Thinking => new(StatusMark.Spinner, StatusTone.Live, "Thinking…"),
        RelayUiState.RequestFailed => new(StatusMark.Alert, StatusTone.Danger, "Request failed. Ready to retry."),
        RelayUiState.Faulted => fault == RelayFaultCode.Internal
            ? new(StatusMark.Dot, StatusTone.Warning, stateText ?? RelayStoppedByItself)
            : new(StatusMark.Alert, StatusTone.Danger, stateText ?? "Could not start the relay."),
        _ => new(StatusMark.Ring, StatusTone.Offline, "Stopped"),
    };

    /// <summary>The short label of the sidebar card, with the mark and colour of the full status.</summary>
    public static StatusIndicator Sidebar(RelayUiState state, RelayFaultCode? fault)
    {
        var full = Relay(state, null, fault);
        return full with
        {
            Text = state switch
            {
                RelayUiState.Checking or RelayUiState.Starting => "Checking…",
                RelayUiState.PrerequisiteFailed => "Cannot start yet",
                RelayUiState.Ready => "Ready",
                RelayUiState.Paused => "Paused",
                RelayUiState.Thinking => "Thinking…",
                RelayUiState.RequestFailed => "Request failed",
                RelayUiState.Faulted => "Relay stopped",
                _ => "Stopped",
            },
        };
    }

    public static StatusIndicator Phone(PhonePresence presence, string? toolTip = null) => presence switch
    {
        PhonePresence.Connected => new(StatusMark.Dot, StatusTone.Success, "iPhone connected", toolTip),
        PhonePresence.Idle => new(StatusMark.Dot, StatusTone.Warning, "iPhone idle", toolTip),
        _ => new(StatusMark.Ring, StatusTone.Offline, "iPhone not connected", toolTip),
    };
}
