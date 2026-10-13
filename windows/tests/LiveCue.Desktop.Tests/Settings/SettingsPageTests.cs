using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Tests.Live;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests.Settings;

/// <summary>
/// The Settings page and the mapping dialog in the real window, off-screen, over a fake session
/// (screens section 5 and 6.2; W-design 5.8 and 5.9), at the default and at the minimum size. With the
/// environment variable <c>LIVECUE_TEST_SHOTS</c> set, each scene is also written as a PNG for a person
/// to compare with the mockup frames (win-settings-connection, win-settings-assistant).
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class SettingsPageTests
{
    private const string PrivateExplanation =
        "Reachable only from devices signed in to your tailnet. The iPhone needs Tailscale connected.";

    private const string AddressCaption =
        "Changing the mode or port changes the PC address. Paired iPhones must scan a new QR to use it.";

    private const string MappingCaption =
        "LiveCue shows the command and its effect here and asks once more before running it. It never changes Tailscale when the relay starts or stops, or when settings are saved.";

    private const string PrivateCommand = "tailscale serve --bg --https=11443 http://127.0.0.1:47841";

    [Fact]
    public void Connection_shows_the_form_and_the_command_before_anything_is_pressed() => WpfApp.Run(() =>
    {
        using var app = new SettingsWindow();
        app.Checked(MappingState.Absent);
        app.Open();

        Assert.Single(WpfApp.Descendants(app.Page).OfType<SettingsPage>());

        // Every text of the Connection panel as screens 5.1 has it, in reading order.
        var connection = SettingsWindow.Texts(app.Named("ConnectionPanel"));
        Assert.Equal(
            [
                "Connection", "Applies the next time the relay starts",
                "Mode", "Private tailnet", "Public Funnel", PrivateExplanation,
                "Relay port", "47841", "HTTPS port", "11443", "Path", "/",
                "The port the relay listens on, on this PC only.",
                "PC address", "https://pc.example.test:11443/", AddressCaption,
                "Save", "Discard",
                "Tailscale mapping", "Not set up", "Set up connection…",
                "What Set up does", PrivateCommand, "Effect of ApplyPrivate.",
                "Check again",
                MappingCaption,
            ],
            connection);

        // The form: the saved values, nothing to save, the mode's own fields.
        Assert.Equal("47841", app.Shown<TextBox>(AutomationIds.RelayPortField).Text);
        Assert.Equal("11443", app.Shown<TextBox>(AutomationIds.PrivateHttpsPortField).Text);
        Assert.Null(app.Find(AutomationIds.PublicHttpsPortSelector));
        Assert.Null(app.Find(AutomationIds.PublicPathPrefixField));
        Assert.Equal(0, app.Shown<SegmentedControl>(AutomationIds.ConnectionModeSelector).SelectedIndex);
        Assert.False(app.Shown<Button>(AutomationIds.SaveSettingsButton).IsEnabled);
        Assert.False(app.Shown<Button>(AutomationIds.DiscardSettingsButton).IsEnabled);
        Assert.Same(app.Window.FindResource("Button.Secondary"), app.Shown<Button>(AutomationIds.SaveSettingsButton).Style);
        Assert.Same(app.Window.FindResource("Button.Quiet"), app.Shown<Button>(AutomationIds.DiscardSettingsButton).Style);
        Assert.Null(app.Find(AutomationIds.RestartNotice));
        Assert.Null(app.Find(AutomationIds.SettingsFileNotice));

        // The mapping: its state, the one accent button, the command as it would run. No Remove…,
        // because there is nothing of LiveCue's to remove.
        Assert.Equal("Not set up", app.Shown<StatusIndicatorView>(AutomationIds.MappingStatus).Text);
        var setUp = app.Shown<Button>(AutomationIds.SetUpMappingButton);
        Assert.True(setUp.IsEnabled);
        Assert.Same(app.Window.FindResource("Button.Primary"), setUp.Style);
        Assert.Equal(PrivateCommand, app.Shown<TextBlock>(AutomationIds.MappingCommandText).Text);
        Assert.True(app.Shown<Button>(AutomationIds.CheckMappingButton).IsEnabled);
        Assert.Null(app.Find(AutomationIds.RemoveMappingButton));
        Assert.Empty(WpfApp.Descendants(app.Named("StaleList")).OfType<Banner>());
        Assert.Empty(WpfApp.Descendants(app.Named("ExposureList")).OfType<Banner>());

        app.Capture("w10-settings-connection", (width, _) =>
        {
            RequirePanels(app);
            var form = app.Bounds(app.Named("ConnectionForm"));
            var mapping = app.Bounds(app.Named("MappingColumn"));
            var panel = app.Bounds(app.Named("ConnectionPanel"));
            if (width >= 1100)
            {
                // Two columns of the same width, 24 apart, 20 inside the panel.
                Assert.Equal(form.Top, mapping.Top, 0.5);
                Assert.Equal(form.Right + 24, mapping.Left, 0.5);
                Assert.Equal(form.Width, mapping.Width, 1.0);
                Assert.Equal(panel.Left + 20, form.Left, 0.5);
                Assert.Equal(panel.Right - 20, mapping.Right, 0.5);
            }
            else
            {
                // Below 1100 the two columns stack.
                Assert.Equal(form.Left, mapping.Left, 0.5);
                Assert.Equal(form.Width, mapping.Width, 0.5);
                Assert.Equal(form.Bottom + 24, mapping.Top, 0.5);
            }

            // The three fields share one line; every field is 44 high.
            var relayPort = app.Bounds(app.Shown<TextBox>(AutomationIds.RelayPortField));
            var httpsPort = app.Bounds(app.Shown<TextBox>(AutomationIds.PrivateHttpsPortField));
            Assert.Equal(relayPort.Top, httpsPort.Top, 0.5);
            Assert.Equal(relayPort.Right + 16, httpsPort.Left, 0.5);
            Assert.Equal(44, relayPort.Height, 0.5);
            Assert.Equal(relayPort.Width, httpsPort.Width, 1.0);
        });

        // "Set up connection…" opens the dialog with the same command. Nothing has run.
        setUp.Command.Execute(setUp.CommandParameter);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        var dialog = Assert.IsType<MappingDialogViewModel>(app.Shell.ActiveDialog);
        Assert.Equal(PrivateCommand, dialog.CommandText);
        Assert.Empty(app.Session.ExecutedPlans);
        dialog.CancelCommand.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));

        // "Check again" only reads.
        var check = app.Shown<Button>(AutomationIds.CheckMappingButton);
        check.Command.Execute(null);
        Assert.Equal(1, app.Session.CheckAgainCalls);
        Assert.Empty(app.Session.ExecutedPlans);
        Assert.Equal(0, app.Processes.Calls);
    });

    [Fact]
    public void Editing_the_form_shows_errors_and_asks_to_save_before_setting_up() => WpfApp.Run(() =>
    {
        using var app = new SettingsWindow();
        app.Checked(MappingState.Absent);
        app.Open();

        // Public Funnel: the three ports as a segmented control and the path as a field.
        app.Shown<SegmentedControl>(AutomationIds.ConnectionModeSelector).SelectedIndex = 1;
        WpfApp.Settle(TimeSpan.FromMilliseconds(300));
        Assert.Equal(ConnectionMode.Public, app.Settings.Mode);
        Assert.Null(app.Find(AutomationIds.PrivateHttpsPortField));
        var ports = app.Shown<SegmentedControl>(AutomationIds.PublicHttpsPortSelector);
        Assert.Equal(["443", "8443", "10000"], ports.Items.Cast<ListBoxItem>().Select(item => (string)item.Content));
        Assert.Equal(2, ports.SelectedIndex);
        Assert.Equal("/livecue", app.Shown<TextBox>(AutomationIds.PublicPathPrefixField).Text);
        var texts = app.Texts();
        Assert.Contains(
            "Reachable from the internet over HTTPS and protected by the pairing token, not by tailnet membership. The iPhone needs only an internet connection.",
            texts);
        Assert.Contains("https://pc.example.test:10000/livecue/", texts);

        // Unsaved: Save and Discard are offered; the mapping is still that of the saved mode, and it
        // cannot be set up until the form is saved.
        Assert.True(app.Shown<Button>(AutomationIds.SaveSettingsButton).IsEnabled);
        Assert.True(app.Shown<Button>(AutomationIds.DiscardSettingsButton).IsEnabled);
        Assert.False(app.Shown<Button>(AutomationIds.SetUpMappingButton).IsEnabled);
        Assert.Contains("Save the settings first.", texts);
        Assert.Contains(PrivateCommand, texts);

        ports.SelectedIndex = 0;
        app.Shown<TextBox>(AutomationIds.PublicPathPrefixField).Text = "/cue";
        WpfApp.Settle(TimeSpan.FromMilliseconds(300));
        Assert.Equal(443, app.Settings.PublicHttpsPort);
        Assert.Contains("https://pc.example.test:443/cue/", app.Texts());
        app.Capture("w10-settings-public-unsaved", (_, _) => RequirePanels(app));

        // Values that are not allowed: each field says why, in words and with the alert glyph, and
        // Save is disabled.
        var relayPort = app.Shown<TextBox>(AutomationIds.RelayPortField);
        var path = app.Shown<TextBox>(AutomationIds.PublicPathPrefixField);
        relayPort.Text = "47831";
        path.Text = "Live Cue";
        WpfApp.Settle();
        texts = app.Texts();
        Assert.Contains("Port 47831 belongs to the old LiveCue and is never used. Choose another relay port.", texts);
        Assert.Contains(
            "Use a path such as /livecue: a slash, then 1 to 32 lowercase letters, digits or hyphens, starting with a letter or digit.",
            texts);
        Assert.True(Validation.GetHasError(relayPort));
        Assert.True(Validation.GetHasError(path));
        Assert.False(app.Shown<Button>(AutomationIds.SaveSettingsButton).IsEnabled);
        Assert.Contains("Shown when Tailscale is connected", texts);
        app.Capture("w10-settings-errors", (_, _) => RequirePanels(app));

        // Fixed and saved: one save, no mapping command, and the page shows the new mode's mapping.
        relayPort.Text = "47841";
        path.Text = "/cue";
        WpfApp.Settle();
        Assert.False(Validation.GetHasError(relayPort));
        var save = app.Shown<Button>(AutomationIds.SaveSettingsButton);
        Assert.True(save.IsEnabled);
        save.Command.Execute(null);
        WpfApp.Settle();
        var written = Assert.Single(app.Session.SavedSettings);
        Assert.Equal((ConnectionMode.Public, 443, "/cue"), (written.ConnectionMode, written.PublicHttpsPort, written.PublicPathPrefix));
        Assert.Empty(app.Session.ExecutedPlans);
        Assert.Null(app.Shell.ActiveDialog);
        Assert.False(save.IsEnabled);
        Assert.Contains("tailscale funnel --bg --https=443 --set-path=/cue http://127.0.0.1:47841", app.Texts());
    });

    [Fact]
    public void Warnings_show_what_else_forwards_to_the_relay_and_offer_only_what_livecue_may_remove() => WpfApp.Run(() =>
    {
        var current = SettingsParts.Owned(ConnectionMode.Private, 11443);
        var otherMode = SettingsParts.Owned(ConnectionMode.Public, 10000, "/livecue");
        using var app = new SettingsWindow(RelaySettings.Default with { OwnedMappings = [current, otherMode] });
        app.Checked(MappingState.Present);
        var funnel = new ExposedHandler(443, "/relay", FunnelOn: true, Foreground: false, LedgerEntry: null);
        var service = new ExposedHandler(9443, "/", FunnelOn: false, Foreground: true, LedgerEntry: null);
        app.Session.Connection = PageParts.Inspection(MappingState.Present, exposure: [funnel, service]) with { StaleOwned = [otherMode] };
        app.Session.State = RelayUiState.Ready;
        app.Session.SettingsDifferFromRunning = true;
        app.Session.SettingsLoad = new SettingsLoadResult(app.Session.Settings, SettingsFileState.Unreadable, []);
        app.Open();

        // The two notices of the panel.
        var restart = app.Shown<Banner>(AutomationIds.RestartNotice);
        Assert.Equal(BannerTone.Warning, restart.Tone);
        Assert.Equal("The relay is still using the previous connection settings. Stop and start it to apply the change.", restart.Body);
        var file = app.Shown<Banner>(AutomationIds.SettingsFileNotice);
        Assert.Equal(BannerTone.Warning, file.Tone);
        Assert.Equal("LiveCue could not read its settings file and is using the defaults. Saving here replaces the file.", file.Body);

        // LiveCue's own mapping: active, and the card has Remove… for it. Set up has nothing to do.
        Assert.Equal("Active", app.Shown<StatusIndicatorView>(AutomationIds.MappingStatus).Text);
        Assert.False(app.Shown<Button>(AutomationIds.SetUpMappingButton).IsEnabled);
        var remove = app.Shown<Button>(AutomationIds.RemoveMappingButton);
        Assert.Same(app.Window.FindResource("Button.Destructive"), remove.Style);
        Assert.Equal("Remove…", remove.Content);
        Assert.Equal(current, remove.CommandParameter);

        // A mapping LiveCue created earlier: a warning with its own Remove….
        var staleList = app.Shown<ItemsControl>(AutomationIds.StaleMappingList);
        var stale = Assert.Single(WpfApp.Descendants(staleList).OfType<Banner>());
        Assert.Equal(BannerTone.Warning, stale.Tone);
        Assert.Equal("LiveCue's Public Funnel mapping at https://pc.example.test:10000/livecue/ still exists.", stale.Body);
        Assert.Equal("Remove…", stale.ActionLabel);
        var staleRemove = Assert.Single(WpfApp.Descendants(stale).OfType<Button>());

        // Mappings LiveCue did not create: the sentence, the command as text to select and copy, and
        // no button anywhere in the list.
        var exposureList = app.Shown<ItemsControl>(AutomationIds.UnownedExposureList);
        var exposures = WpfApp.Descendants(exposureList).OfType<Banner>().ToList();
        Assert.Equal(
            [
                "A Tailscale Funnel mapping on port 443, path /relay, publishes this relay on the internet. LiveCue did not create it and will not remove it.",
                "A Tailscale mapping on port 9443, path /, forwards to this relay inside your tailnet. LiveCue did not create it and will not remove it. It belongs to a running Tailscale session or service; end that to remove it.",
            ],
            exposures.Select(banner => banner.Body));
        Assert.All(exposures, banner => Assert.Null(banner.ActionLabel));
        Assert.DoesNotContain(WpfApp.Descendants(exposureList).OfType<ButtonBase>(), button => button.IsVisible);
        var command = Assert.Single(WpfApp.Descendants(exposureList).OfType<TextBox>(), box => box.IsVisible);
        Assert.Equal("tailscale funnel --https=443 --set-path=/relay off", command.Text);
        Assert.True(command.IsReadOnly);
        Assert.Contains("Remove it with Tailscale's own tools: tailscale serve status", SettingsWindow.Texts(exposureList));

        app.Capture("w10-settings-warnings", (_, _) => RequirePanels(app));
        app.Capture("w10-settings-warnings-lists", scrollTo: "MappingCard");

        // The stale mapping's Remove… opens the dialog with that mapping's removal command.
        staleRemove.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, staleRemove));
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        var dialog = Assert.IsType<MappingDialogViewModel>(app.Shell.ActiveDialog);
        Assert.Equal(MappingAction.RemovePublic, dialog.Action);
        Assert.Equal("tailscale funnel --https=10000 --set-path=/livecue off", dialog.CommandText);
        dialog.CancelCommand.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));

        // The card's Remove… is for the selected mode's own mapping.
        remove.Command.Execute(remove.CommandParameter);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Equal("tailscale serve --https=11443 --set-path=/ off", Assert.IsType<MappingDialogViewModel>(app.Shell.ActiveDialog).CommandText);
        app.Shell.EscapeCommand.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Empty(app.Session.ExecutedPlans);

        // Tailscale's configuration could not be read: unknown is said, not "nothing".
        app.Session.Connection = PageParts.Inspection(MappingState.Unknown, MappingRefusal.StatusUnavailable, TailscaleReadFailure.Failed);
        WpfApp.Settle();
        Assert.Equal("LiveCue could not read Tailscale's configuration", app.Shown<StatusIndicatorView>(AutomationIds.MappingStatus).Text);
        var texts = app.Texts();
        Assert.Contains("Check that Tailscale is running, then choose Check again.", texts);
        Assert.Contains("Tailscale's configuration could not be read, so LiveCue cannot tell what is exposed. Choose Check again.", texts);
        Assert.Null(app.Find(AutomationIds.RemoveMappingButton));
        app.Capture("w10-settings-unknown", scrollTo: "MappingCard");
    });

    [Fact]
    public void Assistant_speech_capture_and_about() => WpfApp.Run(() =>
    {
        using var app = new SettingsWindow();
        app.Files.AddLab(@"C:\LiveCue\asr");
        app.Session.Settings = RelaySettings.Default with { AsrLabFolder = @"C:\LiveCue\asr", CodexContainmentCheckedVersion = "1.2.2" };
        app.Session.Prerequisites = PageParts.Report(
            new Dictionary<PrerequisiteId, PrerequisiteState> { [PrerequisiteId.CodexSignedIn] = PrerequisiteState.Failed },
            codexVersion: "1.2.3");
        app.Session.Connection = PageParts.Inspection(MappingState.Present);
        app.Session.State = RelayUiState.Ready;
        app.Session.RaiseRelayEvent(new LocalModelChanged(
            app.Clock.GetUtcNow(), new LocalModelStatus(LocalModelState.Ready, "nemotron", false, 41, "Nemotron ready."), "Nemotron ready."));
        app.Open();

        // Opening the page asked for no check: one has run already.
        Assert.Equal(0, app.Session.CheckAgainCalls);

        Assert.Equal(
            [
                "Assistant", "Check again",
                "Codex executable path", "Automatic",
                "Leave empty to let LiveCue find Codex. Otherwise the full path of codex.exe.",
                "Codex executable", @"C:\Tools\Codex\codex.exe", "Found",
                "Codex sign-in", "Open Codex and sign in with ChatGPT, then choose Check again.", "Not signed in",
                "Codex version",
                "Codex is version 1.2.3; the containment check was last run for 1.2.2. Run the containment check, then choose Mark as checked in Settings.",
                "1.2.3",
                "Mark as checked",
            ],
            SettingsWindow.Texts(app.Named("AssistantPanel")));
        Assert.Equal(
            [
                "PC speech models",
                "Speech lab folder", @"C:\LiveCue\asr",
                "Leave empty to use the asr folder beside LiveCue.",
                "Lab folder", @"C:\LiveCue\asr", "Found",
                "Loaded model", "As last reported by the relay", "Nemotron · ready",
                "Models are started and stopped from the iPhone: Settings, Transcription, PC speech model. Start opens Docker Desktop if needed; one model runs at a time.",
            ],
            SettingsWindow.Texts(app.Named("SpeechPanel")));
        Assert.Equal(
            [
                "Hide this window from screen capture",
                "Screen sharing, recordings and screenshots show an empty area instead of this window.",
            ],
            SettingsWindow.Texts(app.Named("CapturePanel")));
        Assert.Equal(
            ["Version", "1.0.0", "Licence", "MIT", "Third-party notices", "Source code on GitHub"],
            SettingsWindow.Texts(app.Named("AboutPanel")));

        // The indicators carry the ids, each with its own mark.
        Assert.Equal(StatusMark.Check, app.Shown<StatusIndicatorView>(AutomationIds.CodexPathStatus).Mark);
        Assert.Equal(StatusMark.Alert, app.Shown<StatusIndicatorView>(AutomationIds.CodexLoginStatus).Mark);
        Assert.Equal(StatusTone.Danger, app.Shown<StatusIndicatorView>(AutomationIds.CodexLoginStatus).Tone);
        Assert.Equal(StatusTone.Warning, app.Shown<StatusIndicatorView>(AutomationIds.CodexVersionStatus).Tone);
        Assert.Equal("Found", app.Shown<StatusIndicatorView>(AutomationIds.SpeechLabStatus).Text);
        Assert.Equal("Nemotron · ready", app.Shown<StatusIndicatorView>(AutomationIds.LoadedModelStatus).Text);
        Assert.Equal("Automatic", app.Shown<TextBox>(AutomationIds.CodexPathField).Tag);
        Assert.Equal("Automatic", app.Shown<TextBox>(AutomationIds.SpeechLabFolderField).Tag);
        Assert.True(app.Shown<Button>(AutomationIds.CheckAssistantButton).IsEnabled);
        Assert.Same(app.Window.FindResource("Button.Quiet"), app.Shown<Button>(AutomationIds.ThirdPartyNoticesButton).Style);
        Assert.Same(app.Window.FindResource("Button.Quiet"), app.Shown<Button>(AutomationIds.SourceCodeButton).Style);

        app.Capture(
            "w10-settings-assistant",
            (width, _) =>
            {
                RequirePanels(app);
                var assistant = app.Bounds(app.Named("AssistantPanel"));
                var speech = app.Bounds(app.Named("SpeechPanel"));
                if (width >= 1100)
                {
                    // Side by side, the same width and height, 16 apart.
                    Assert.Equal(assistant.Top, speech.Top, 0.5);
                    Assert.Equal(assistant.Right + 16, speech.Left, 0.5);
                    Assert.Equal(assistant.Width, speech.Width, 1.0);
                    Assert.Equal(assistant.Height, speech.Height, 0.5);
                }
                else
                {
                    Assert.Equal(assistant.Left, speech.Left, 0.5);
                    Assert.Equal(assistant.Bottom + 16, speech.Top, 0.5);
                }
            },
            scrollTo: "CheckColumns");
        app.Capture("w10-settings-about", scrollTo: "CapturePanel");

        // "Mark as checked" records the version; the row then says the check was run, and the button leaves.
        var mark = app.Shown<Button>(AutomationIds.MarkContainmentCheckedButton);
        mark.Command.Execute(null);
        WpfApp.Settle();
        Assert.Equal("1.2.3", Assert.Single(app.Session.SavedSettings).CodexContainmentCheckedVersion);
        Assert.Equal(1, app.Session.CheckAgainCalls);
        Assert.Null(app.Find(AutomationIds.MarkContainmentCheckedButton));
        Assert.Contains("Containment check run for this version.", app.Texts());
        Assert.Equal(StatusTone.Success, app.Shown<StatusIndicatorView>(AutomationIds.CodexVersionStatus).Tone);

        // The capture switch is a form field: switching it saves nothing; Save writes it.
        var toggle = app.Shown<CheckBox>(AutomationIds.HideFromCaptureToggle);
        Assert.True(toggle.IsChecked);
        toggle.IsChecked = false;
        WpfApp.Settle(TimeSpan.FromMilliseconds(200));
        Assert.Single(app.Session.SavedSettings);
        Assert.True(app.Session.Settings.HideFromScreenCapture);
        Assert.True(app.Settings.HasChanges);
        app.Settings.SaveCommand.Execute(null);
        Assert.False(app.Session.Settings.HideFromScreenCapture);
        Assert.Equal("1.2.3", app.Session.Settings.CodexContainmentCheckedVersion);

        // Nothing found: the fixes, and the setup hint for speech.
        app.Session.State = RelayUiState.Stopped;
        app.Session.RaiseSignal(SessionSignal.RelayEnded);
        app.Session.Settings = RelaySettings.Default;
        app.Session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>
        {
            [PrerequisiteId.CodexFound] = PrerequisiteState.Failed,
            [PrerequisiteId.CodexSignedIn] = PrerequisiteState.NotChecked,
        });
        WpfApp.Settle();
        var texts = app.Texts();
        Assert.Contains("Install or update the Codex desktop app, then open it once and sign in.", texts);
        Assert.Contains("Not found", texts);
        Assert.Contains("Not checked", texts);
        Assert.Contains("Unknown", texts);
        Assert.Contains("Not set up", texts);
        Assert.Contains("Not reported yet", texts);
        Assert.Contains("PC speech models are optional. Without the lab folder, choose Meta Muse, Voz or Parakeet on the iPhone.", texts);
        Assert.Null(app.Find(AutomationIds.MarkContainmentCheckedButton));
        app.Capture("w10-settings-assistant-missing", scrollTo: "CheckColumns");
        Assert.Equal(0, app.Processes.Calls);
    });

    [Fact]
    public void The_mapping_dialog_shows_the_command_and_stays_open_on_a_failure() => WpfApp.Run(() =>
    {
        var current = SettingsParts.Owned(ConnectionMode.Private, 11443);
        using var app = new SettingsWindow(RelaySettings.Default with { OwnedMappings = [current] });
        app.Checked(MappingState.Absent);
        app.Open();
        var card = app.DialogCard;

        // Set up: the command in a read-only field, the effect, and "Set up" as the default button.
        var setUp = app.Shown<Button>(AutomationIds.SetUpMappingButton);
        FocusManager.SetFocusedElement(app.Window, setUp);
        setUp.Command.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        var dialog = Assert.IsType<MappingDialogViewModel>(app.Shell.ActiveDialog);
        var view = Assert.Single(WpfApp.Descendants(card).OfType<MappingDialog>());
        Assert.Equal(AutomationIds.SetUpMappingDialog, AutomationProperties.GetAutomationId(view));
        Assert.Equal(
            ["Set up the Tailscale mapping?", "LiveCue will run:", PrivateCommand, "Effect of ApplyPrivate.", "Cancel", "Set up"],
            SettingsWindow.Texts(card));
        var command = Assert.Single(WpfApp.Descendants(card).OfType<TextBox>());
        Assert.True(command.IsReadOnly);
        Assert.Equal(dialog.CommandText, command.Text);
        var buttons = WpfApp.Descendants(card).OfType<Button>().Where(button => button.IsVisible).ToList();
        var confirm = Assert.Single(buttons, button => Equals(button.Content, "Set up"));
        Assert.True(confirm.IsDefault);
        Assert.Same(app.Window.FindResource("Button.Primary"), confirm.Style);
        Assert.Same(confirm, FocusManager.GetFocusedElement(app.Window));
        Assert.Single(buttons, button => button.IsCancel);
        Assert.Equal(480, app.Bounds(card).Width);
        Assert.True(VisualChecks.RequireCrispText(app.Root) > 0);
        VisualChecks.RequireEffectsOnlyOnCasters(app.Root);
        WpfApp.Render(app.Root, "w10-dialog-set-up-1240x660.png");

        // Tailscale did not finish in time: the dialog stays, with the text that points at the command above.
        app.Session.MappingResult = new MappingResult(
            MappingOutcome.CommandTimedOut, MappingRefusal.None, OthersChanged: false, app.Session.Settings, After: null);
        confirm.Command.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Same(dialog, app.Shell.ActiveDialog);
        var failure = Assert.Single(WpfApp.Descendants(card).OfType<Banner>(), banner => banner.IsVisible);
        Assert.Equal(BannerTone.Danger, failure.Tone);
        Assert.Equal(
            "Tailscale did not finish in time. Run the command shown above in a terminal to see Tailscale's own instructions. The first use of HTTPS or Funnel on a tailnet must be approved in the Tailscale admin console.",
            failure.Body);
        Assert.Equal(PrivateCommand, command.Text);
        WpfApp.Render(app.Root, "w10-dialog-set-up-failed-1240x660.png");

        // Done: the dialog closes and the focus returns to the button that opened it.
        app.Session.MappingResult = null;
        confirm.Command.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Null(app.Shell.ActiveDialog);
        Assert.Equal(2, app.Session.ExecutedPlans.Count);
        Assert.Same(setUp, FocusManager.GetFocusedElement(app.Window));

        // Remove: the destructive button is not the default one, and Cancel has the focus.
        app.Session.Connection = PageParts.Inspection(MappingState.Present);
        WpfApp.Settle();
        var remove = app.Shown<Button>(AutomationIds.RemoveMappingButton);
        remove.Command.Execute(remove.CommandParameter);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        var removal = Assert.IsType<MappingDialogViewModel>(app.Shell.ActiveDialog);
        view = Assert.Single(WpfApp.Descendants(card).OfType<MappingDialog>());
        Assert.Equal(AutomationIds.RemoveMappingDialog, AutomationProperties.GetAutomationId(view));
        Assert.Equal(
            [
                "Remove the Tailscale mapping?", "LiveCue will run:", "tailscale serve --https=11443 --set-path=/ off",
                "Effect of RemovePrivate.", "Cancel", "Remove",
            ],
            SettingsWindow.Texts(card));
        buttons = WpfApp.Descendants(card).OfType<Button>().Where(button => button.IsVisible).ToList();
        var destructive = Assert.Single(buttons, button => Equals(button.Content, "Remove"));
        Assert.False(destructive.IsDefault);
        Assert.DoesNotContain(buttons, button => button.IsDefault);
        Assert.Same(app.Window.FindResource("Button.DestructivePrimary"), destructive.Style);
        var cancel = Assert.Single(buttons, button => button.IsCancel);
        Assert.Same(cancel, FocusManager.GetFocusedElement(app.Window));
        WpfApp.Render(app.Root, "w10-dialog-remove-1240x660.png");

        // Cancelled: nothing ran.
        removal.CancelCommand.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Null(app.Shell.ActiveDialog);
        Assert.Equal(2, app.Session.ExecutedPlans.Count);
        Assert.Equal(0, app.Processes.Calls);
    });

    /// <summary>
    /// Every panel of the page is as wide as the page, whatever the scroll bar does, and they are 16
    /// apart (screens 1.4): the bar stands in the window's own inset.
    /// </summary>
    private static void RequirePanels(SettingsWindow app)
    {
        var page = app.Bounds(app.Page);
        var connection = app.Bounds(app.Named("ConnectionPanel"));
        var checks = app.Bounds(app.Named("CheckColumns"));
        var capture = app.Bounds(app.Named("CapturePanel"));
        var about = app.Bounds(app.Named("AboutPanel"));

        foreach (var panel in new[] { connection, checks, capture, about })
        {
            Assert.Equal(page.Left, panel.Left, 0.5);
            Assert.Equal(page.Right, panel.Right, 0.5);
        }

        Assert.Equal(connection.Bottom + 16, checks.Top, 0.5);
        Assert.Equal(checks.Bottom + 16, capture.Top, 0.5);
        Assert.Equal(capture.Bottom + 16, about.Top, 0.5);
    }
}
