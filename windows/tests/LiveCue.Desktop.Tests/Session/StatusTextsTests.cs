using LiveCue.Desktop.Session;
using LiveCue.Relay;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.Session;

/// <summary>
/// The status and sidebar texts of W-design 5.4 ("Status texts"; screens 2.3, 2.4 and 1.3). Every state
/// has its words, its mark and its colour; no state is told by colour alone.
/// </summary>
public sealed class StatusTextsTests
{
    private const string FailureText = "Tailscale is not online. Open Tailscale and connect first.";

    [Fact]
    public void Each_state()
    {
        (RelayUiState State, RelayFaultCode? Fault, StatusMark Mark, StatusTone Tone, string Live, string Sidebar)[] rows =
        [
            (RelayUiState.Stopped, null, StatusMark.Ring, StatusTone.Offline, "Stopped", "Stopped"),
            (RelayUiState.Checking, null, StatusMark.Spinner, StatusTone.Live, "Checking Codex and Tailscale…", "Checking…"),
            (RelayUiState.Starting, null, StatusMark.Spinner, StatusTone.Live, "Checking Codex and Tailscale…", "Checking…"),
            (RelayUiState.PrerequisiteFailed, null, StatusMark.Alert, StatusTone.Danger, FailureText, "Cannot start yet"),
            (RelayUiState.Ready, null, StatusMark.Dot, StatusTone.Success, "Ready", "Ready"),
            (RelayUiState.Paused, null, StatusMark.Pause, StatusTone.Warning, "Paused", "Paused"),
            (RelayUiState.Thinking, null, StatusMark.Spinner, StatusTone.Live, "Thinking…", "Thinking…"),
            (RelayUiState.RequestFailed, null, StatusMark.Alert, StatusTone.Danger, "Request failed. Ready to retry.", "Request failed"),
            (RelayUiState.Faulted, RelayFaultCode.PortInUse, StatusMark.Alert, StatusTone.Danger, FailureText, "Relay stopped"),
            (RelayUiState.Faulted, null, StatusMark.Alert, StatusTone.Danger, FailureText, "Relay stopped"),
            (RelayUiState.Faulted, RelayFaultCode.Internal, StatusMark.Dot, StatusTone.Warning, FailureText, "Relay stopped"),
        ];

        // Every state of the machine has a row.
        Assert.Equal(Enum.GetValues<RelayUiState>().Order(), rows.Select(row => row.State).Distinct().Order());

        foreach (var row in rows)
        {
            // Only the two states that carry a text of their own show it.
            var live = StatusTexts.Relay(row.State, FailureText, row.Fault);
            Assert.Equal(new StatusIndicator(row.Mark, row.Tone, row.Live), live);

            // The sidebar says it shorter, with the same mark and colour.
            Assert.Equal(new StatusIndicator(row.Mark, row.Tone, row.Sidebar), StatusTexts.Sidebar(row.State, row.Fault));
            Assert.Equal(row.Sidebar, StatusTexts.SidebarLabel(row.State));
        }

        // A state that should carry a text but has none still says something.
        Assert.Equal("Cannot start yet", StatusTexts.Relay(RelayUiState.PrerequisiteFailed, null, null).Text);
        Assert.Equal("Could not start the relay.", StatusTexts.Relay(RelayUiState.Faulted, null, RelayFaultCode.StartFailed).Text);
        Assert.Equal(
            "Relay stopped. Click Start relay to reconnect.",
            StatusTexts.Relay(RelayUiState.Faulted, null, RelayFaultCode.Internal).Text);

        Assert.Equal("New requests from the iPhone are refused until you resume.", StatusTexts.PausedCaption);

        // The phone.
        Assert.Equal(
            new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "iPhone not connected"),
            StatusTexts.Phone(PhonePresence.NotConnected));
        Assert.Equal(
            new StatusIndicator(StatusMark.Dot, StatusTone.Success, "iPhone connected"),
            StatusTexts.Phone(PhonePresence.Connected));
        Assert.Equal(
            new StatusIndicator(StatusMark.Dot, StatusTone.Warning, "iPhone idle"),
            StatusTexts.Phone(PhonePresence.Idle));

        // Its tooltip exists once a phone was seen, and counts whole seconds.
        var seen = ManualTimeProvider.DefaultStart;
        const string idleNote =
            "Idle does not prove the phone is offline: iOS stops the heartbeat when the app is in the background.";
        Assert.Null(StatusTexts.Phone(PhonePresence.NotConnected, null, seen).ToolTip);
        Assert.Equal(
            "Last authenticated heartbeat: 0 seconds ago. " + idleNote,
            StatusTexts.Phone(PhonePresence.Connected, seen, seen).ToolTip);
        Assert.Equal(
            "Last authenticated heartbeat: 1 second ago. " + idleNote,
            StatusTexts.Phone(PhonePresence.Connected, seen, seen.AddSeconds(1.9)).ToolTip);
        Assert.Equal(
            "Last authenticated heartbeat: 42 seconds ago. " + idleNote,
            StatusTexts.Phone(PhonePresence.Idle, seen, seen.AddSeconds(42)).ToolTip);

        // A clock that went backwards never shows a negative age.
        Assert.Equal(
            "Last authenticated heartbeat: 0 seconds ago. " + idleNote,
            StatusTexts.PhoneToolTip(seen, seen.AddSeconds(-5)));
    }
}
