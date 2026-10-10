using LiveCue.Desktop.Activity;
using LiveCue.Desktop.Live;
using LiveCue.Desktop.Pairing;
using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Shell;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.Shell;

/// <summary>
/// The window's view model and the foundation's page view models over a <see cref="FakeSession"/>.
/// Rewritten from the spike's shell and page tests for the new contracts; work packages WP-W08 to
/// WP-W10 own these folders and replace this file with the tests of the test plan (sections 2.6 to 2.8).
/// </summary>
public sealed class ShellViewModelTests
{
    [Fact]
    public void Navigation_and_escape()
    {
        var parts = new Parts();
        var shell = parts.Shell;
        var changed = new List<string?>();
        shell.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        Assert.Equal(AppPage.Live, shell.SelectedPage);
        Assert.IsType<LiveViewModel>(shell.CurrentPage);

        shell.NavigateCommand.Execute(AppPage.Pairing);
        Assert.IsType<PairingViewModel>(shell.CurrentPage);
        shell.SelectedPage = AppPage.Activity;
        Assert.IsType<ActivityViewModel>(shell.CurrentPage);
        shell.Navigate(AppPage.Settings);
        Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        Assert.Equal(AppPage.Settings, shell.Current);

        // A value that is no page, and a command parameter of another type, change nothing.
        shell.SelectedPage = (AppPage)42;
        shell.NavigateCommand.Execute("Live");
        Assert.Equal(AppPage.Settings, shell.SelectedPage);
        Assert.Contains(nameof(IShellViewModel.SelectedPage), changed);
        Assert.Contains(nameof(IShellViewModel.CurrentPage), changed);

        // Escape cancels the open dialog, and does nothing without one.
        shell.EscapeCommand.Execute(null);
        shell.Pairing.ResetPairingCommand.Execute(null);
        var dialog = Assert.IsAssignableFrom<IResetPairingDialogViewModel>(shell.ActiveDialog);
        Assert.Equal("Reset pairing?", dialog.Title);
        shell.EscapeCommand.Execute(null);
        Assert.Null(shell.ActiveDialog);
        Assert.Equal(0, parts.Session.ResetPairingCalls);
    }

    [Fact]
    public async Task The_first_render_starts_the_relay_once_and_closing_stops_it()
    {
        var parts = new Parts();

        await parts.Shell.OnWindowRenderedAsync();
        await parts.Shell.OnWindowRenderedAsync();
        await parts.Shell.OnWindowClosingAsync();

        Assert.Equal(1, parts.Session.StartCalls);
        Assert.Equal(1, parts.Session.StopCalls);
    }

    [Fact]
    public void The_sidebar_card_follows_the_session()
    {
        var parts = new Parts();
        var shell = parts.Shell;

        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "Stopped"), shell.SidebarRelayStatus);
        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "iPhone not connected"), shell.SidebarPhoneStatus);

        parts.Session.State = RelayUiState.Ready;
        parts.Session.Presence = PhonePresence.Connected;
        Assert.Equal(new StatusIndicator(StatusMark.Dot, StatusTone.Success, "Ready"), shell.SidebarRelayStatus);
        Assert.Equal(new StatusIndicator(StatusMark.Dot, StatusTone.Success, "iPhone connected"), shell.SidebarPhoneStatus);

        parts.Session.State = RelayUiState.PrerequisiteFailed;
        Assert.Equal("Cannot start yet", shell.SidebarRelayStatus.Text);
        parts.Session.State = RelayUiState.Faulted;
        Assert.Equal("Relay stopped", shell.SidebarRelayStatus.Text);
    }

    [Fact]
    public void The_live_page_shows_the_controls_of_each_state_and_forwards_its_commands()
    {
        var parts = new Parts();
        var live = parts.Shell.Live;

        Assert.Equal("Stopped", live.RelayStatus.Text);
        Assert.True(live.IsStartVisible);
        Assert.False(live.AreRunningControlsVisible);
        Assert.Equal("Start relay", live.StartLabel);
        Assert.Equal("Shown when the relay is running", live.PcAddress);
        Assert.False(live.HasPcAddress);
        Assert.Equal("Your next question will appear here.", live.TranscriptText);

        live.StartRelayCommand.Execute(null);
        parts.Session.State = RelayUiState.Checking;
        Assert.True(live.IsStartBusy);
        Assert.Equal("Checking Codex and Tailscale…", live.RelayStatus.Text);

        parts.Session.AdvertisedEndpoint = "https://pc.example.test:11443/";
        parts.Session.State = RelayUiState.Ready;
        Assert.False(live.IsStartVisible);
        Assert.True(live.AreRunningControlsVisible);
        Assert.Equal("Pause relay", live.PauseResumeLabel);
        Assert.Equal("https://pc.example.test:11443/", live.PcAddress);

        live.PauseResumeCommand.Execute(null);
        parts.Session.IsPaused = true;
        parts.Session.State = RelayUiState.Paused;
        Assert.Equal("Resume relay", live.PauseResumeLabel);
        Assert.Equal("New requests from the iPhone are refused until you resume.", live.RelayStatusCaption);

        live.CopyPcAddressCommand.Execute(null);
        live.StopRelayCommand.Execute(null);
        live.OpenSettingsCommand.Execute(null);

        Assert.Equal((1, 1, 1), (parts.Session.StartCalls, parts.Session.TogglePauseCalls, parts.Session.StopCalls));
        Assert.Equal("https://pc.example.test:11443/", parts.Clipboard.Last);
        Assert.Equal("Copied", parts.Shell.ToastText);
        Assert.Equal(AppPage.Settings, parts.Shell.SelectedPage);

        // A toast leaves by itself.
        parts.Clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Null(parts.Shell.ToastText);
    }

    [Fact]
    public void A_fault_text_is_the_status_and_stays_with_the_stopped_layout()
    {
        var parts = new Parts();
        var live = parts.Shell.Live;

        parts.Session.FaultCode = Relay.RelayFaultCode.PortInUse;
        parts.Session.StateText = "Port 47841 is already in use.";
        parts.Session.State = RelayUiState.Faulted;

        Assert.Equal(new StatusIndicator(StatusMark.Alert, StatusTone.Danger, "Port 47841 is already in use."), live.RelayStatus);
        Assert.True(live.IsStartVisible);
    }

    [Fact]
    public void Show_qr_waits_for_the_relay_and_then_for_the_connection()
    {
        var parts = new Parts();
        var pairing = parts.Shell.Pairing;

        Assert.False(pairing.CanShowQr);
        Assert.False(pairing.ShowQrCommand.CanExecute(null));
        Assert.Equal(QrTileFace.RelayNotRunning, pairing.TileFace);
        Assert.Equal("Start the relay to show the QR", pairing.PlaceholderLine);
        Assert.Equal("Start the relay first.", pairing.ShowQrCaption);
        Assert.Equal("Pair your iPhone once. Restarts and showing this QR keep existing pairings valid.", pairing.Hint);
        Assert.Null(pairing.QrModules);

        parts.Session.State = RelayUiState.Ready;
        Assert.Equal(QrTileFace.ConnectionNotSetUp, pairing.TileFace);
        Assert.Equal("Set up the connection first.", pairing.ShowQrCaption);

        parts.Session.IsConnectionReady = true;
        Assert.True(pairing.ShowQrCommand.CanExecute(null));
        Assert.Equal(QrTileFace.Placeholder, pairing.TileFace);
        Assert.Null(pairing.ShowQrCaption);
        pairing.ShowQrCommand.Execute(null);
        Assert.Equal(1, parts.Session.ShowPairingCalls);
        Assert.False(pairing.HideQrCommand.CanExecute(null));

        parts.Session.State = RelayUiState.Starting;
        Assert.False(pairing.ResetPairingCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_reset_dialog_resets_through_the_session_and_stays_open_on_a_failure()
    {
        var session = new FakeSession { ResetSucceeds = false };
        var dialog = new ResetPairingDialogViewModel(session);
        var closed = 0;
        dialog.Closed += (_, _) => closed++;

        dialog.ConfirmCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(0, closed);
        Assert.Equal(
            "The pairing could not be reset. Check that LiveCue can write its data folder, then retry.",
            dialog.Failure?.Body);

        session.ResetSucceeds = true;
        dialog.ConfirmCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(1, closed);
        Assert.Equal(2, session.ResetPairingCalls);
    }

    [Fact]
    public async Task The_mapping_dialog_shows_the_planned_command_and_is_the_one_place_that_runs_it()
    {
        var session = new FakeSession();
        var plan = session.PlanSetUp(out _)!;
        var dialog = new MappingDialogViewModel(session, plan);
        var closed = 0;
        dialog.Closed += (_, _) => closed++;

        Assert.Equal("Set up the Tailscale mapping?", dialog.Title);
        Assert.Equal("LiveCue will run:", dialog.Intro);
        Assert.Equal(MappingCommand.ApplyPrivate(11443, 47841).DisplayText, dialog.CommandText);
        Assert.Equal("Set up", dialog.ConfirmLabel);
        Assert.False(dialog.IsDestructive);
        Assert.Empty(session.ExecutedPlans);

        session.MappingResult = new MappingResult(
            MappingOutcome.Refused, MappingRefusal.TailscaleNotInstalled, false, session.Settings, null);
        dialog.ConfirmCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(0, closed);
        Assert.Equal("Tailscale is not installed in Program Files.", dialog.Failure?.Body);

        session.MappingResult = null;
        dialog.ConfirmCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(1, closed);
        Assert.Equal([plan, plan], session.ExecutedPlans);

        var removal = new MappingDialogViewModel(session, session.PlanRemoval(
            new OwnedMapping(Relay.ConnectionMode.Private, 11443, "/", "http://127.0.0.1:47841", DateTimeOffset.UnixEpoch)));
        Assert.Equal("Remove the Tailscale mapping?", removal.Title);
        Assert.Equal("Remove", removal.ConfirmLabel);
        Assert.True(removal.IsDestructive);
        Assert.Equal("tailscale serve --https=11443 --set-path=/ off", removal.CommandText);
    }

    [Fact]
    public void The_activity_page_logs_the_start_messages_and_diagnostics_and_clears()
    {
        var parts = new Parts();
        var activity = parts.Shell.Activity;
        var at = parts.Clock.GetUtcNow();

        Assert.True(activity.IsEmpty);
        Assert.Equal(string.Empty, activity.EntryCountText);
        Assert.False(activity.ClearActivityCommand.CanExecute(null));

        parts.Session.RaiseRelayEvent(new Relay.RelayStarted(at, "https://pc.example.test:10000/livecue/", 47841, Relay.ConnectionMode.Public, 2, "1.0.0", false));
        parts.Session.RaiseRelayEvent(new Relay.Diagnostic(at, "edge", "A request failed unexpectedly. (InvalidOperationException)"));
        parts.Session.RaiseMessage(new SessionMessage(at, SessionMessageKind.Error, "The desktop command failed. Please retry."));

        Assert.Equal(
            [
                "Relay started. Waiting for your iPhone.",
                "Public mode: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address.",
                "Relay diagnostic: A request failed unexpectedly. (InvalidOperationException)",
                "The desktop command failed. Please retry.",
            ],
            activity.Entries.Select(entry => entry.Message));
        Assert.All(activity.Entries, entry => Assert.Equal("09:00:00", entry.TimeText));
        Assert.Equal("4 entries", activity.EntryCountText);
        Assert.StartsWith("09:00:00 Relay started.", activity.Text, StringComparison.Ordinal);

        activity.ClearActivityCommand.Execute(null);
        Assert.True(activity.IsEmpty);
        Assert.Equal(1, parts.Session.ClearActivityCalls);

        parts.Session.RaiseMessage(new SessionMessage(at, SessionMessageKind.Info, "one"));
        Assert.Equal("1 entry", activity.EntryCountText);
        parts.Session.RaiseSignal(SessionSignal.RelayStarting);
        Assert.True(activity.IsEmpty);
    }

    [Fact]
    public void The_activity_log_is_bounded_and_keeps_the_newest_entries()
    {
        var log = new ActivityLog(TimeProvider.System);

        for (var index = 0; index < ActivityLog.Capacity + 25; index++)
        {
            log.Add("entry " + index);
        }

        Assert.Equal(ActivityLog.Capacity, log.Entries.Count);
        Assert.Equal("entry 25", log.Entries[0].Message);
        Assert.Equal("entry " + (ActivityLog.Capacity + 24), log.Entries[^1].Message);
    }

    [Fact]
    public void The_settings_page_shows_the_saved_settings_and_the_command_that_would_run()
    {
        var parts = new Parts();
        var settings = parts.Shell.Settings;

        Assert.Equal(Relay.ConnectionMode.Private, settings.Mode);
        Assert.Equal("47841", settings.RelayPortText);
        Assert.Equal("11443", settings.PrivateHttpsPortText);
        Assert.Equal(10000, settings.PublicHttpsPort);
        Assert.Equal("/livecue", settings.PublicPathPrefix);
        Assert.Equal("1.0.0", settings.Version);
        Assert.Equal(MappingCommand.ApplyPrivate(11443, 47841).DisplayText, settings.MappingCommandText);
        Assert.False(settings.HasChanges);
        Assert.False(settings.HasErrors);
        Assert.False(settings.SaveCommand.CanExecute(null));

        // Nothing is written to Tailscale by opening the dialog; only its confirm button does that.
        settings.SetUpMappingCommand.Execute(null);
        Assert.IsAssignableFrom<IMappingDialogViewModel>(parts.Shell.ActiveDialog);
        Assert.Empty(parts.Session.ExecutedPlans);
        Assert.Empty(parts.Session.SavedSettings);
    }

    /// <summary>The shell and its four page view models over a fake session, wired as the composition root does.</summary>
    private sealed class Parts
    {
        public Parts()
        {
            var ui = new ImmediateDispatcher();
            var navigator = new Navigator();
            var dialogs = new DialogHost();
            var toasts = new ToastHost(ui, Clock);
            var hosting = HostingFactory.Create(
                new SupervisorOptions(@"C:\LiveCue-tests\data", "1.0.0"),
                new HostingOverrides
                {
                    Files = new LiveCue.Desktop.SelfTest.RecordingFiles(),
                    Processes = new LiveCue.Desktop.SelfTest.RecordingProcesses(),
                });

            Shell = new ShellViewModel(
                Session,
                new LiveViewModel(Session, navigator, dialogs, Clipboard, toasts, Clock, ui),
                new PairingViewModel(Session, navigator, dialogs, Clipboard, toasts, Clock, ui),
                new ActivityViewModel(Session, Clock),
                new SettingsViewModel(Session, dialogs, Clipboard, toasts, Launcher, hosting, ui),
                navigator,
                dialogs,
                toasts);
        }

        public FakeSession Session { get; } = new();

        public ManualTimeProvider Clock { get; } = new();

        public FakeClipboard Clipboard { get; } = new();

        public FakeLauncher Launcher { get; } = new();

        public ShellViewModel Shell { get; }
    }
}
