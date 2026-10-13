using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LiveCue.Desktop.Activity;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.Pairing;
using LiveCue.Desktop.Session;
using LiveCue.Desktop.Text;
using LiveCue.Hosting;
using LiveCue.Relay;
using static LiveCue.Desktop.SelfTest.VisualChecks;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// The fifteen scenes of the self-test (W-design 5.13), in the order they are shown, and the mockup
/// frame each one corresponds to (docs/design/mockups/windows.html).
/// </summary>
internal static class Scenes
{
    public const string LiveStopped = "live-stopped";
    public const string ActivityEmpty = "activity-empty";
    public const string PairingEmpty = "pairing-empty";
    public const string LiveReady = "live-ready";
    public const string PairingQr = "pairing-qr";
    public const string PairingResetConfirm = "pairing-reset-confirm";
    public const string LiveThinking = "live-thinking";
    public const string LiveReply = "live-reply";
    public const string LivePaused = "live-paused";
    public const string LiveRequestFailed = "live-request-failed";
    public const string ActivityFilled = "activity-filled";
    public const string SettingsPrivate = "settings-private";
    public const string SettingsPublic = "settings-public";
    public const string SettingsBlocked = "settings-blocked";
    public const string FaultPortInUse = "fault-port-in-use";

    /// <summary>Every scene, in the order the steps show them.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        LiveStopped, ActivityEmpty, PairingEmpty, LiveReady, PairingQr, PairingResetConfirm, LiveThinking, LiveReply,
        LivePaused, LiveRequestFailed, ActivityFilled, SettingsPrivate, SettingsPublic, SettingsBlocked, FaultPortInUse,
    ];

    /// <summary>
    /// The mockup frame of each scene that has one. Every <c>-min</c> file corresponds to
    /// <c>win-min</c>. <c>win-live-prereq</c> and <c>win-settings-assistant</c> have no scene: the view
    /// models' own tests cover them.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MockupFrames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [LiveStopped] = "win-live-stopped",
        [LiveReply] = "win-live-ready",
        [LiveThinking] = "win-live-thinking",
        [LiveRequestFailed] = "win-live-failed",
        [PairingQr] = "win-pairing-shown",
        [PairingResetConfirm] = "win-pairing-reset",
        [ActivityFilled] = "win-activity",
        [ActivityEmpty] = "win-activity-empty",
        [SettingsPrivate] = "win-settings-connection",
    };

    /// <summary>The thirty files a run with a screenshot folder writes, besides its log.</summary>
    public static IReadOnlyList<string> FileNames { get; } =
    [
        .. Names.SelectMany(scene => new[]
        {
            SceneRenderer.FileName(scene, SceneRenderer.DefaultSize),
            SceneRenderer.FileName(scene, SceneRenderer.MinimumSize),
        }),
    ];
}

/// <summary>The nineteen steps of W-design 5.13: what is done, what must hold after it, and the scene it leaves.</summary>
internal sealed partial class SelfTestSession
{
    private const string PortInUseText =
        "Port 47841 is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay.";

    private async Task RunStepsAsync()
    {
        // In the script, the Private mapping exists and the Public one was never set up.
        Hosting.Mappings.PrivateState = MappingState.Present;
        Hosting.Mappings.PublicState = MappingState.Absent;

        await WindowShownAsync();
        await ActivityBeforeTheFirstStartAsync();
        await PairingBeforeTheFirstStartAsync();
        await StartAndPhoneAsync();
        await ShowQrAsync();
        await ResetPairingIsAskedFirstAsync();
        await RequestStartedAsync();
        await RequestCompletedAsync();
        await PauseAndResumeAsync();
        await RequestFailedAsync();
        await PcSpeechAsync();
        await ActivityWithEntriesAsync();
        await SettingsPrivateAsync();
        await SettingsPublicAsync();
        await SettingsBlockedAsync();
        await PortInUseAsync();
        await ClearActivityAsync();
        await ActivityBoundAsync();
        End();
    }

    // ---- 1 to 3: nothing started ------------------------------------------------------------------

    private async Task WindowShownAsync()
    {
        Begin(1, "the window is shown, nothing was started");
        await SettleAsync();
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_window);
        _log.Write($"window: dpi scale {dpi.DpiScaleX:0.##}, text family \"{DescribeTextFont(_window)}\"");

        _assert.Check("initial-layout", () =>
        {
            Require(Shell.SelectedPage == AppPage.Live, "the Live view to be selected at launch");
            Require(Text(AutomationIds.RelayStatusText) == StatusTexts.Stopped, "the relay status to read \"Stopped\" at launch");
            Require(Text(AutomationIds.SidebarRelayStatus) == StatusTexts.Stopped, "the sidebar to read \"Stopped\" at launch");
            Require(Text(AutomationIds.PhoneStatusText) == StatusTexts.PhoneNotConnected, "the phone status to read \"iPhone not connected\" at launch");
            Require(Text(AutomationIds.PcAddressText) == LiveText.PcAddressPlaceholder, "the PC address placeholder before the first start");
            Require(Button(AutomationIds.StartRelayButton) is { IsVisible: true, IsEnabled: true }, "Start relay to be shown and enabled while stopped");
            Require(Button(AutomationIds.PauseRelayButton) is { IsVisible: false }, "Pause relay not to be shown while stopped");
            Require(Button(AutomationIds.StopRelayButton) is { IsVisible: false }, "Stop relay not to be shown while stopped");
            Require(!Shell.Pairing.CanShowQr && !Shell.Pairing.ShowQrCommand.CanExecute(null), "Show QR to be disabled while stopped");
            Require(Text(AutomationIds.TranscriptText) == LiveText.TranscriptPlaceholder, "the transcript placeholder");
            Require(_script.Created == 0 && Hosting.Services.Supervisor.State == SupervisorState.Stopped, "no relay to exist before Start relay");
        });

        var frame = await SceneAsync(Scenes.LiveStopped);

        // The look of the window (design system, checklist item 1): once, on the first scene.
        _assert.Check("look-ground-and-glass", () => CheckGroundAndGlass(frame));
        var (ground, panel) = await RenderOnFlatGroundAsync();
        _assert.Check("look-reference-composites", () =>
        {
            Require(IsWithin(ground, System.Windows.Media.Color.FromRgb(0xE6, 0xEC, 0xF5), 1), $"the flat ground to be #E6ECF5, not {Hex(ground)}");
            Require(IsWithin(panel, System.Windows.Media.Color.FromRgb(0xEB, 0xEE, 0xF4), 2), $"a panel on the flat ground to composite to #EBEEF4 within 2, not {Hex(panel)}");
        });
        await CheckLayoutBeyondDesktopAsync();
        _assert.Check("layout-beyond-desktop", () => Require(
            Math.Abs(_root.ActualWidth - SceneRenderer.DefaultSize.Width) < 0.01,
            "the window to come back to its default size after being laid out larger than any desktop"));
    }

    private async Task ActivityBeforeTheFirstStartAsync()
    {
        Begin(2, "the Activity page before the first start");
        await ShowAsync(AppPage.Activity);

        _assert.Check("activity-empty", () =>
        {
            Require(Shell.Activity.IsEmpty && Shows(ActivityText.EmptyTitle), "the empty state of the activity view");
            Require(Button(AutomationIds.ClearActivityButton) is { IsVisible: true, IsEnabled: false }, "Clear activity to be disabled while the log is empty");
        });
        await SceneAsync(Scenes.ActivityEmpty);
    }

    private async Task PairingBeforeTheFirstStartAsync()
    {
        Begin(3, "the Pairing page before the first start");
        await ShowAsync(AppPage.Pairing);

        _assert.Check("pairing-placeholder", () =>
        {
            Require(Shell.Pairing.TileFace == QrTileFace.RelayNotRunning && Shows(PairingText.TileRelayNotRunning), "the QR tile's placeholder while the relay is stopped");
            Require(Shell.Pairing.QrModules is null, "no modules before a QR was asked for");
            Require(Find(AutomationIds.PairingQr) is QrCodeView { Modules: null, ActualWidth: 0 }, "the QR element to draw nothing");
            Require(Button(AutomationIds.ShowQrButton) is { IsVisible: true, IsEnabled: false }, "Show QR to be disabled until the relay is ready");
        });
        await SceneAsync(Scenes.PairingEmpty);
    }

    // ---- 4 to 6: started, paired ------------------------------------------------------------------

    private async Task StartAndPhoneAsync()
    {
        Begin(4, "Start relay with passing checks, an existing pairing and a present mapping; then a phone is seen");
        await ShowAsync(AppPage.Live);
        Press(AutomationIds.StartRelayButton);
        await WaitForAsync(() => Session.State == RelayUiState.Ready, "the relay state to become Ready after Start relay");
        _script.PhoneSeen();
        await WaitForAsync(() => Session.Presence == PhonePresence.Connected, "a phone-seen event to reach the session");

        _assert.Check("ready-layout", () =>
        {
            Require(_script.Current is not null && Hosting.Services.Supervisor.State == SupervisorState.Running, "the supervisor to be running the scripted relay");
            Require(Text(AutomationIds.RelayStatusText) == StatusTexts.Ready, "the relay status to read \"Ready\"");
            Require(Text(AutomationIds.SidebarRelayStatus) == StatusTexts.Ready, "the sidebar to read \"Ready\"");
            Require(Button(AutomationIds.StartRelayButton) is { IsVisible: false }, "Start relay to be replaced while the relay runs");
            Require(Button(AutomationIds.PauseRelayButton) is { IsVisible: true, IsEnabled: true, Content: LiveText.PauseRelay }, "the pause button to read \"Pause relay\"");
            Require(Button(AutomationIds.StopRelayButton) is { IsVisible: true, IsEnabled: true }, "Stop relay to be enabled while the relay runs");
            Require(Text(AutomationIds.PcAddressText) == SyntheticData.Endpoint, "the PC address to be the advertised endpoint");
            Require(Text(AutomationIds.PhoneStatusText) == StatusTexts.PhoneConnected, "a phone-seen event to show \"iPhone connected\"");
            Require(Text(AutomationIds.SidebarPhoneStatus) == StatusTexts.PhoneConnected, "the sidebar to show \"iPhone connected\"");
            Require(HasEntry(ActivityText.RelayStarted), "the activity to contain \"Relay started. Waiting for your iPhone.\"");
        });
        _assert.Check("show-qr-enabled", () => Require(
            Session.IsConnectionReady && Shell.Pairing.CanShowQr && Shell.Pairing.ShowQrCommand.CanExecute(null),
            "Show QR to be enabled once the relay runs and its mapping is present"));
        await SceneAsync(Scenes.LiveReady);
    }

    private async Task ShowQrAsync()
    {
        Begin(5, "Show QR: the scripted matrix, and the check of the address succeeds");
        await AdvanceToAsync(9, 14, 31);
        await ShowAsync(AppPage.Pairing);
        Press(AutomationIds.ShowQrButton);
        await WaitForAsync(
            () => Shell.Pairing is { QrState: QrState.Shown, VerifyNotice: not null },
            "the QR and the result of the address check to be shown");

        // Eighteen seconds on, the countdown reads as the mockup draws it.
        await AdvanceAsync(TimeSpan.FromSeconds(18));
        Require(Text(AutomationIds.PairingHint) == PairingText.ShownHint, "the hint that says how to scan the QR");
        Require(Shows(PairingText.HidesIn(TimeSpan.FromSeconds(102))), "the countdown to read \"Hides in 1:42\"");
        Require(Shows(PairingText.Verified), "the verification notice beside the QR");

        var frame = await SceneAsync(Scenes.PairingQr);
        var view = Find(AutomationIds.PairingQr) as QrCodeView;
        _assert.Check("qr-drawn", () =>
        {
            Require(view is { IsVisible: true, Modules: not null }, "the QR element to hold the matrix");
            var (dark, wrong) = CompareWithMatrix(frame, view!);
            Require(dark > 0, "the tile's pixels to contain dark modules");
            Require(wrong == 0, $"every module of the matrix to be drawn as it is ({wrong} are not)");
            Require(view!.Modules!.Length == SyntheticData.PairingMatrix().Length, "the matrix to be the scripted one");
        });
        _assert.Check("qr-quiet-zone", () =>
        {
            var notWhite = QuietZoneModulesNotWhite(frame, view!);
            Require(notWhite == 0, $"a white border four modules wide around the code ({notWhite} of its modules are not white)");
        });
        _assert.Check("qr-notice", () => Require(
            CountEntries(ActivityText.SelfCheckVerified) == 1,
            "exactly one verification line in the activity"));
    }

    private async Task ResetPairingIsAskedFirstAsync()
    {
        Begin(6, "Reset pairing asks first; Escape cancels");

        // Two minutes after it appeared the QR hides by itself: the state the mockup shows behind the dialog.
        await AdvanceToAsync(9, 16, 31);
        Require(
            Shell.Pairing is { QrState: QrState.Hidden, HiddenReason: QrHiddenReason.Timer, QrModules: null },
            "the QR to hide two minutes after it was shown");
        Require(Text(AutomationIds.PairingHint) == PairingText.HiddenByTimerHint, "the hint to say the QR was hidden for privacy");

        // The button is pressed by someone who is on it: that is where the focus returns to.
        var opener = Button(AutomationIds.ResetPairingButton);
        Require(opener is { IsVisible: true, IsEnabled: true }, "Reset pairing… to be there");
        FocusManager.SetFocusedElement(_window, opener);
        Press(AutomationIds.ResetPairingButton);
        await SettleAsync();
        await SceneAsync(Scenes.PairingResetConfirm);

        _assert.Check("reset-dialog", () =>
        {
            Require(Shell.ActiveDialog is IResetPairingDialogViewModel, "the reset confirmation to be open");
            Require(Find(AutomationIds.ResetPairingDialog) is { IsVisible: true }, "the reset confirmation to be shown");
            Require(Shows(PairingText.ResetBodyRunning), "the dialog to say what a reset does while the relay runs");
            Require(
                FocusManager.GetFocusedElement(_window) is System.Windows.Controls.Button { IsCancel: true, IsVisible: true },
                "the focus to be on Cancel");
            Require(
                Button(AutomationIds.ConfirmResetPairingButton) is System.Windows.Controls.Button { IsVisible: true, IsDefault: false },
                "the destructive button not to be the default button");

            // Escape, as the window binds it.
            var escape = _window.InputBindings.OfType<KeyBinding>().Single(binding => binding is { Key: Key.Escape, Modifiers: ModifierKeys.None });
            Require(escape.Command.CanExecute(null), "Escape to be able to cancel the dialog");
            escape.Command.Execute(null);

            Require(Shell.ActiveDialog is null, "Escape to close the dialog");
            Require(ReferenceEquals(FocusManager.GetFocusedElement(_window), opener), "the focus to return to the button that opened the dialog");
            Require(
                _script.Current is { Resets: 0, PairingsShown: 1 } && !HasEntry(SyntheticData.PairingReset),
                "the pairing to be unchanged after the dialog was cancelled");
            Require(Shell.Pairing.QrState == QrState.Hidden, "no new QR after the dialog was cancelled");
        });
        await SettleAsync();
        Require(Find(AutomationIds.ResetPairingDialog) is not { IsVisible: true }, "the dialog to be gone from the window");
    }

    // ---- 7 to 12: requests, speech, the log -------------------------------------------------------

    private async Task RequestStartedAsync()
    {
        Begin(7, "an assist request starts");
        await ShowAsync(AppPage.Live);
        await AdvanceToAsync(9, 16, 48);
        _script.RequestStarted(SyntheticData.RequestId);
        await WaitForAsync(() => Session.State == RelayUiState.Thinking && Shell.Live.IsReplyThinking, "the request to reach the window");

        _assert.Check("thinking", () =>
        {
            Require(Text(AutomationIds.RelayStatusText) == StatusTexts.Thinking, "the relay status to read \"Thinking…\"");
            Require(Text(AutomationIds.SidebarRelayStatus) == StatusTexts.Thinking, "the sidebar to read \"Thinking…\"");
            Require(Text(AutomationIds.TranscriptHint) == LiveText.TranscriptReceived, "the transcript hint to say a request was received");
            Require(Text(AutomationIds.TranscriptText)?.Contains(SyntheticData.Question, StringComparison.Ordinal) == true, "the transcript to contain the question");
            Require(Shows(LiveText.InProgressPrefix + SyntheticData.PartialTranscript), "the unfinished sentence to be shown as in progress");
            Require(Text(AutomationIds.ReplyHint) == LiveText.ReplyProcessing, "the reply hint to say the request is being processed");
            Require(Shell.Live.IsReplyThinking && Shows(LiveText.Thinking), "the reply to show \"Thinking…\"");
        });
        await SceneAsync(Scenes.LiveThinking);
    }

    private async Task RequestCompletedAsync()
    {
        Begin(8, "the request is answered");
        await AdvanceToAsync(9, 16, 49);
        _script.RequestCompleted(SyntheticData.RequestId);
        await WaitForAsync(() => Session.State == RelayUiState.Ready && !Shell.Live.IsReplyThinking, "the reply to reach the window");

        _assert.Check("reply-text", () =>
        {
            Require(Text(AutomationIds.ReplyText)?.Contains(SyntheticData.Answer, StringComparison.Ordinal) == true, "the reply to contain the answer");
            Require(Shell.Activity.Text.Contains(SyntheticData.Answer, StringComparison.Ordinal), "the activity to contain the answer");
        });
        _assert.Check("timing-hint", () =>
        {
            Require(Text(AutomationIds.ReplyHint)?.Contains("0.85", StringComparison.Ordinal) == true, "the reply hint to contain \"0.85\"");
            Require(Text(AutomationIds.ReplyHint) == LiveText.ReplySent(SyntheticData.Model, SyntheticData.ReasoningEffort, SyntheticData.CodexMs), "the reply hint to name the model, the effort and the Codex time");
            Require(Text(AutomationIds.ReplyTiming) == LiveText.ReplyTiming(SyntheticData.DurationMs), "the caption under the reply to give the relay's total time");
            Require(Text(AutomationIds.RelayStatusText) == StatusTexts.Ready, "the relay status to read \"Ready\" again");
        });
        await SceneAsync(Scenes.LiveReply);
    }

    private async Task PauseAndResumeAsync()
    {
        Begin(9, "Pause relay, then Resume relay");
        Press(AutomationIds.PauseRelayButton);
        await WaitForAsync(() => Session.State == RelayUiState.Paused, "Pause relay to pause the relay");

        _assert.Check("pause", () =>
        {
            Require(Session.IsPaused && Shell.Live.IsPaused && _script.Current is { Accepting: false }, "the paused flag to be set");
            Require(Button(AutomationIds.PauseRelayButton) is { Content: LiveText.ResumeRelay }, "the button to read \"Resume relay\" while paused");
            Require(Text(AutomationIds.RelayStatusText) == StatusTexts.Paused, "the relay status to read \"Paused\"");
            Require(Shows(StatusTexts.PausedCaption), "the caption that says requests are refused while paused");
        });
        await SceneAsync(Scenes.LivePaused);

        await PressUntilAsync(AutomationIds.PauseRelayButton, () => _script.Current is { Accepting: true }, "Resume relay to reach the relay");
        await WaitForAsync(() => Session.State == RelayUiState.Ready, "Resume relay to resume the relay");
        _assert.Check("resume", () =>
        {
            Require(!Session.IsPaused && !Shell.Live.IsPaused && _script.Current is { Accepting: true }, "the paused flag to be cleared");
            Require(Button(AutomationIds.PauseRelayButton) is { Content: LiveText.PauseRelay }, "the button to read \"Pause relay\" again");
        });
    }

    private async Task RequestFailedAsync()
    {
        Begin(10, "a request starts and fails with ASSISTANT_FAILED");
        await AdvanceToAsync(9, 18, 20);
        _script.RequestStarted(SyntheticData.FailedRequestId);
        await WaitForAsync(() => Session.State == RelayUiState.Thinking, "the second request to reach the window");
        await AdvanceToAsync(9, 18, 23);
        _script.RequestFailed(SyntheticData.FailedRequestId);
        await WaitForAsync(() => Session.State == RelayUiState.RequestFailed && Shell.Live.ReplyFailure is not null, "the failure to reach the window");

        _assert.Check("request-failed", () =>
        {
            Require(Text(AutomationIds.RelayStatusText) == StatusTexts.RequestFailed, "the relay status to read \"Request failed. Ready to retry.\"");
            Require(Shows(LiveText.FailureTitle), "the banner's title, \"Nothing was sent to the iPhone\"");
            Require(Shows(LiveText.FailureBody), "the banner's body");
            Require(Text(AutomationIds.ReplyHint) == LiveText.ReplyNotSent, "the reply hint to read \"Not sent. Try Assist again.\"");
            Require(Button(AutomationIds.PauseRelayButton) is { IsVisible: true, IsEnabled: true }, "the relay to keep running after a failed request");
        });
        await SceneAsync(Scenes.LiveRequestFailed);
    }

    private async Task PcSpeechAsync()
    {
        Begin(11, "the PC speech model loads, is ready, and hears words");
        await AdvanceToAsync(9, 21, 10);
        _script.Speech(SpeechStreamPhase.Loading, SyntheticData.SpeechLoading);
        await WaitForAsync(() => Shell.Live.TranscriptHint == SyntheticData.SpeechLoading, "the loading message to reach the window");
        await AdvanceToAsync(9, 21, 34);
        _script.Speech(SpeechStreamPhase.Ready, SyntheticData.SpeechReady);
        _script.SpeechText(SyntheticData.SpeechWords);
        await WaitForAsync(
            () => Shell.Live.TranscriptText.EndsWith(SyntheticData.SpeechWords, StringComparison.Ordinal),
            "the words to reach the transcript");

        _assert.Check("speech", () =>
        {
            Require(Text(AutomationIds.TranscriptHint) == SyntheticData.SpeechReady, "the transcript hint to equal the last message");
            Require(Text(AutomationIds.TranscriptText)?.EndsWith(SyntheticData.SpeechWords, StringComparison.Ordinal) == true, "the transcript to end with \"Local words\"");
            Require(Text(AutomationIds.TranscriptText)?.Contains(SyntheticData.Question, StringComparison.Ordinal) == true, "the words to be added to the transcript, not to replace it");
            Require(HasEntry(SyntheticData.SpeechLoading) && HasEntry(SyntheticData.SpeechReady), "both messages to be in the activity");
        });
        _assert.Observe(_root);
    }

    private async Task ActivityWithEntriesAsync()
    {
        Begin(12, "the Activity page with entries");
        await ShowAsync(AppPage.Activity);

        var incoming = ActivityText.Incoming(AnswerKind.Assist, SyntheticData.RequestId);
        var reply = ActivityText.Reply(SyntheticData.RequestId, SyntheticData.DurationMs);
        _assert.Check("activity-lines", () =>
        {
            Require(incoming == "INCOMING assist [selftest-0001]" && reply == "REPLY [selftest-0001] 0.91 s", "the two lines to be worded as the screens say");
            Require(HasEntry(incoming) && Shows(incoming), "\"INCOMING assist [selftest-0001]\" to be in the log and on the page");
            Require(HasEntry(reply) && Shows(reply), "\"REPLY [selftest-0001] 0.91 s\" to be in the log and on the page");
            Require(Shows("09:16:48") && Shows("09:16:49"), "each entry to start with its time");
            Require(Shows(ActivityText.EntryCount(Shell.Activity.Entries.Count)), "the footer to count the entries");
            Require(Button(AutomationIds.ClearActivityButton) is { IsVisible: true, IsEnabled: true }, "Clear activity to be enabled while the log has entries");
        });
        await SceneAsync(Scenes.ActivityFilled);
    }

    // ---- 13 to 15: settings -----------------------------------------------------------------------

    private async Task SettingsPrivateAsync()
    {
        Begin(13, "the Settings page: Private, mapping present");
        await ShowAsync(AppPage.Settings);
        await WaitForAsync(() => !Shell.Settings.IsCheckingAssistant, "the Settings page to be at rest");

        _assert.Check("settings-command", () =>
        {
            Require(Shell.Settings.Mode == ConnectionMode.Private && Shows(SettingsText.PrivateMode), "the Private tailnet mode to be selected");
            Require(Text(AutomationIds.RelayPortField) == "47841", "the relay port field to show 47841");
            Require(Text(AutomationIds.MappingStatus) == SettingsText.MappingActive, "the mapping status to read \"Active\"");
            Require(
                Text(AutomationIds.MappingCommandText) == MappingCommand.ApplyPrivate(11443, 47841).DisplayText,
                "the command text to be the private apply command for ports 11443 and 47841");
            Require(Button(AutomationIds.SetUpMappingButton) is { IsEnabled: false }, "Set up connection… to be disabled while the mapping exists");
            Require(Shell.Settings.RestartNotice is null && Shell.Settings.SettingsFileNotice is null, "no notice above the form");
            Require(Shell.Settings.UnownedExposures.Count == 0 && Shell.Settings.ExposureUnknownText is null, "no exposure warning");
            Require(!Shell.Settings.CanMarkContainmentChecked, "no containment warning: the scripted Codex version is the recorded one");
        });
        await SceneAsync(Scenes.SettingsPrivate);
    }

    private async Task SettingsPublicAsync()
    {
        Begin(14, "the mode is switched to Public Funnel and saved; the Public mapping is absent");
        var selector = Find(AutomationIds.ConnectionModeSelector) as Selector;
        Require(selector is { IsVisible: true, IsEnabled: true }, "the mode selector to be there");
        selector!.SelectedValue = ConnectionMode.Public;
        await SettleAsync();
        Require(Shell.Settings is { Mode: ConnectionMode.Public, HasChanges: true }, "the form to hold the Public mode, unsaved");

        // A save reads the mapping again; the new inspection is another object than the one before.
        var inspected = Session.Connection;
        Press(AutomationIds.SaveSettingsButton);
        await WaitForAsync(
            () => Session.Settings.ConnectionMode == ConnectionMode.Public
                && !ReferenceEquals(Session.Connection, inspected)
                && !Shell.Settings.IsCheckingAssistant
                && Session.Connection?.Public.State == MappingState.Absent,
            "the saved Public mode to be inspected");

        _assert.Check("settings-public", () =>
        {
            Require(!Shell.Settings.HasChanges, "the form to be saved");
            Require(Text(AutomationIds.MappingStatus) == SettingsText.MappingNotSetUp, "the mapping status to read \"Not set up\"");
            Require(
                Text(AutomationIds.MappingCommandText) == MappingCommand.ApplyPublic(10000, "/livecue", 47841).DisplayText,
                "the command text to be the public apply command for port 10000 and /livecue");
            Require(Button(AutomationIds.SetUpMappingButton) is { IsVisible: true, IsEnabled: true }, "Set up connection… to be enabled");
            Require(Hosting.Mappings.Executions == 0, "no write command to have reached the mapping manager");
            Require(Shell.Settings.RestartNotice is not null, "the notice that the running relay still uses the previous settings");
        });
        await SceneAsync(Scenes.SettingsPublic);
    }

    private async Task SettingsBlockedAsync()
    {
        Begin(15, "the Public port would publish other mappings: blocked");
        Hosting.Mappings.PublicState = MappingState.Blocked;
        Hosting.Mappings.PublicBlockedBy = MappingRefusal.PublicPortWouldPublishOthers;
        var inspections = Hosting.Mappings.Inspections;
        await PressUntilAsync(AutomationIds.CheckMappingButton, () => Hosting.Mappings.Inspections > inspections, "Check again to read the mapping");
        await WaitForAsync(
            () => !Shell.Settings.IsCheckingAssistant && Session.Connection?.Public.State == MappingState.Blocked,
            "the blocked mapping to be inspected");

        var reason = MappingTexts.Refusal(MappingRefusal.PublicPortWouldPublishOthers, 10000, "/livecue");
        _assert.Check("settings-blocked", () =>
        {
            Require(Text(AutomationIds.MappingStatus) == SettingsText.MappingBlocked, "the mapping status to read \"Blocked\"");
            Require(reason.Length > 0 && Shows(reason), "the reason to be shown");
            Require(Button(AutomationIds.SetUpMappingButton) is { IsVisible: true, IsEnabled: false }, "Set up connection… to be disabled");
            Require(Hosting.Mappings.Executions == 0, "no write command to have reached the mapping manager");
        });
        await SceneAsync(Scenes.SettingsBlocked);
    }

    // ---- 16 to 19: the end of a relay, the log's bounds, the end ----------------------------------

    private async Task PortInUseAsync()
    {
        Begin(16, "Stop relay; then a start that finds the port in use");
        await ShowAsync(AppPage.Live);
        Press(AutomationIds.StopRelayButton);
        await WaitForAsync(
            () => Session.State == RelayUiState.Stopped && Hosting.Services.Supervisor.State == SupervisorState.Stopped,
            "Stop relay to stop the relay");

        _script.NextStartFault = RelayFaultCode.PortInUse;
        await PressUntilAsync(AutomationIds.StartRelayButton, () => _script.Created == 2, "Start relay to ask for a new relay");
        await WaitForAsync(() => Session.State == RelayUiState.Faulted, "a port-in-use fault to leave the relay faulted");

        _assert.Check("fault", () =>
        {
            Require(Button(AutomationIds.StartRelayButton) is { IsVisible: true, IsEnabled: true }, "the stopped layout: Start relay shown and enabled");
            Require(Button(AutomationIds.PauseRelayButton) is { IsVisible: false } && Button(AutomationIds.StopRelayButton) is { IsVisible: false }, "the stopped layout: no Pause and no Stop");
            Require(Shell.Pairing is { QrModules: null, TileFace: QrTileFace.RelayNotRunning, VerifyNotice: null }, "the QR to be cleared");
            Require(Text(AutomationIds.PhoneStatusText) == StatusTexts.PhoneNotConnected, "the phone to be reset to \"iPhone not connected\"");
            Require(Text(AutomationIds.RelayStatusText) == PortInUseText, "the status to show the port-in-use text with the port number");
            Require(Text(AutomationIds.SidebarRelayStatus) == "Relay stopped", "the sidebar to read \"Relay stopped\"");
            Require(HasEntry(PortInUseText), "the fault to be recorded in the activity");
            Require(_script.Created == 2, "the failed start to have been a new relay instance");
        });
        await SceneAsync(Scenes.FaultPortInUse);
    }

    private async Task ClearActivityAsync()
    {
        Begin(17, "Clear activity");
        await ShowAsync(AppPage.Activity);
        Press(AutomationIds.ClearActivityButton);
        await SettleAsync();
        var emptyPage = Shell.Activity.IsEmpty && Shows(ActivityText.EmptyTitle);
        await ShowAsync(AppPage.Live);

        _assert.Check("clear", () =>
        {
            Require(emptyPage && Shell.Activity.Entries.Count == 0 && Shell.Activity.Text.Length == 0, "the log to be empty");
            Require(Shell.Live.IsTranscriptPlaceholder && Text(AutomationIds.TranscriptText) == LiveText.TranscriptPlaceholder, "the transcript to show its placeholder");
            Require(Shell.Live.IsReplyPlaceholder && Shows("Ready when you are."), "the reply to show its placeholder");
            Require(Text(AutomationIds.ReplyHint) == LiveText.ReplyCleared, "the reply hint to read \"Activity cleared\"");
        });
        _assert.Observe(_root);
    }

    private async Task ActivityBoundAsync()
    {
        Begin(18, string.Create(
            CultureInfo.InvariantCulture,
            $"{SyntheticData.FillerEntries} entries of {SyntheticData.FillerLength} characters go into the log"));

        // Through the session, as every line the desktop writes itself: synchronously, so the size of
        // the log is known after each one.
        var session = (DesktopSession)_services.Session;
        var largest = 0;
        for (var index = 0; index < SyntheticData.FillerEntries; index++)
        {
            session.Report(SessionMessageKind.Info, SyntheticData.Filler(index));
            largest = Math.Max(largest, Shell.Activity.Entries.Sum(ActivityLog.Measure));
        }

        await SettleAsync();
        _assert.Check("activity-cap", () =>
        {
            Require(largest > ActivityLog.LowerBound, "the entries to have filled the log past its lower bound");
            Require(largest <= ActivityLog.UpperBound, $"the log never to exceed {ActivityLog.UpperBound} characters (it reached {largest})");
            Require(Shell.Activity.Text.Length <= ActivityLog.UpperBound, "the log's text to be within the bound");
            Require(Shell.Activity.Entries[0].IsTrimMarker && Shell.Activity.Entries[0].Message == ActivityText.TrimMarker, "entry 0 to be the trim marker");
            Require(Shell.Activity.Entries[^1].Message == SyntheticData.Filler(SyntheticData.FillerEntries - 1), "the newest entry to be kept");
        });
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"activity: at most {largest} characters, {Shell.Activity.Entries.Count - 1} entries kept"));
        _assert.Observe(_root);
    }

    private void End()
    {
        Begin(19, "the end");
        _assert.Check("scenes", () =>
        {
            Require(_scenes.SequenceEqual(Scenes.Names), "every scene to have been shown, in order");
            Require(
                !_renderer.WritesFiles || _renderer.Written.SequenceEqual(Scenes.FileNames),
                $"the {Scenes.FileNames.Count} screenshot files to have been written");
        });
        _assert.CheckNoToken();
        _assert.Check("no-side-effects", () =>
        {
            Require(Hosting.Processes.Calls == 0, "no process to be started");
            Require(Hosting.Files.Touches == 0, "no file to be read or written");
            Require(Clipboard.Texts.Count == 0, "the clipboard to be left alone");
            Require(Launcher.Opened.Count == 0, "nothing to be opened outside the app");
            Require(Hosting.Mappings.Executions == 0, "no Tailscale mapping command to be asked for");
            Require(!Directory.Exists(ScriptedHosting.UnusedDataDirectory), "nothing to be written where the data folder would be");
        });
        _assert.CheckCrispText();
    }

    // ---- What the steps read ----------------------------------------------------------------------

    private bool HasEntry(string message) => CountEntries(message) > 0;

    private int CountEntries(string message) => Shell.Activity.Entries.Count(entry => entry.Message == message);

    /// <summary>Moves the script's clock to that time of its one day.</summary>
    private Task AdvanceToAsync(int hour, int minute, int second)
    {
        var now = _clock.GetUtcNow();
        var target = new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, second, TimeSpan.Zero);
        Require(target > now, "the script's clock to move forwards only");
        return AdvanceAsync(target - now);
    }

    /// <summary>
    /// Compares the drawn tile with the matrix, module by module, at each module's centre. Returns how
    /// many dark modules were seen and how many modules are not drawn as the matrix has them.
    /// </summary>
    private (int Dark, int Wrong) CompareWithMatrix(BitmapSource frame, QrCodeView view)
    {
        var modules = view.Modules!;
        var dark = 0;
        var wrong = 0;
        for (var row = 0; row < modules.Length; row++)
        {
            for (var column = 0; column < modules.Length; column++)
            {
                var luminance = Luminance(ModulePixel(frame, view, column + QrGeometry.QuietZoneModules, row + QrGeometry.QuietZoneModules));
                var isDark = luminance < 0.2;
                var isLight = luminance > 0.8;
                dark += isDark ? 1 : 0;
                wrong += modules[row][column] ? (isDark ? 0 : 1) : (isLight ? 0 : 1);
            }
        }

        return (dark, wrong);
    }

    /// <summary>How many modules of the four-module border around the code are not white.</summary>
    private int QuietZoneModulesNotWhite(BitmapSource frame, QrCodeView view)
    {
        var count = view.Modules!.Length;
        var drawn = QrGeometry.DrawnModules(count);
        var notWhite = 0;
        for (var row = 0; row < drawn; row++)
        {
            for (var column = 0; column < drawn; column++)
            {
                var inCode = row >= QrGeometry.QuietZoneModules && row < count + QrGeometry.QuietZoneModules
                    && column >= QrGeometry.QuietZoneModules && column < count + QrGeometry.QuietZoneModules;
                if (!inCode && !IsWithin(ModulePixel(frame, view, column, row), System.Windows.Media.Colors.White, 2))
                {
                    notWhite++;
                }
            }
        }

        return notWhite;
    }

    /// <summary>The pixel at the centre of one module of the drawn square, quiet zone included.</summary>
    private System.Windows.Media.Color ModulePixel(BitmapSource frame, QrCodeView view, int column, int row)
    {
        var bounds = view.TransformToAncestor(_root).TransformBounds(new Rect(view.RenderSize));
        var module = bounds.Width / QrGeometry.DrawnModules(view.Modules!.Length);
        Require(module >= QrGeometry.MinimumModulePixels && Math.Abs(bounds.Width - bounds.Height) < 0.01, "the code to be drawn as a square of whole modules");
        return PixelAt(frame, (int)(bounds.Left + ((column + 0.5) * module)), (int)(bounds.Top + ((row + 0.5) * module)));
    }
}
