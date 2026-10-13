using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop.Tests.Live;

/// <summary>
/// The Live page's view model over a <see cref="FakeSession"/> (test plan 2.7; W-design 5.5; screens
/// section 2): what each state and each event of the session does to the page.
/// </summary>
public sealed class LiveViewModelTests
{
    private static readonly string NewLine = Environment.NewLine;

    [Fact]
    public void Reaction_cases()
    {
        var parts = new PageParts();
        var live = parts.Live;
        var changed = new List<string?>();
        live.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        // Start of the app: both cards show their placeholders.
        Assert.Equal("Live from your iPhone", live.TranscriptHint);
        Assert.Equal("Your next question will appear here.", live.TranscriptText);
        Assert.Equal(string.Empty, live.TranscriptSecondaryText);
        Assert.True(live.IsTranscriptPlaceholder);
        Assert.Equal("Powered by Codex on your PC", live.ReplyHint);
        Assert.Equal("Ready when you are.\nTap Assist on your iPhone.", live.ReplyText);
        Assert.True(live.IsReplyPlaceholder);
        Assert.False(live.IsReplyThinking);
        Assert.Null(live.ReplyFailure);
        Assert.Null(live.ReplyTiming);

        // RequestStarted: the transcript, then the two quieter blocks, each only when present.
        parts.Run();
        parts.Raise(parts.Request());
        Assert.Equal("Received from your iPhone", live.TranscriptHint);
        Assert.Equal("If we hold the March date, what would we have to cut?", live.TranscriptText);
        Assert.Equal(
            "[In progress] and could the analytics dashboard move to the sprint after" + NewLine + NewLine + "Instruction: answer briefly",
            live.TranscriptSecondaryText);
        Assert.False(live.IsTranscriptPlaceholder);
        Assert.Equal("Processing with Codex on your PC", live.ReplyHint);
        Assert.True(live.IsReplyThinking);
        Assert.False(live.IsReplyPlaceholder);
        Assert.Equal(string.Empty, live.ReplyText);
        Assert.Null(live.ReplyFailure);
        Assert.Null(live.ReplyTiming);
        Assert.Contains(nameof(ILiveViewModel.TranscriptText), changed);
        Assert.Contains(nameof(ILiveViewModel.TranscriptSecondaryText), changed);
        Assert.Contains(nameof(ILiveViewModel.TranscriptHint), changed);
        Assert.Contains(nameof(ILiveViewModel.IsTranscriptPlaceholder), changed);
        Assert.Contains(nameof(ILiveViewModel.IsReplyThinking), changed);
        Assert.Contains(nameof(ILiveViewModel.ReplyHint), changed);

        parts.Raise(parts.Request(partial: null, instruction: "answer briefly"));
        Assert.Equal("Instruction: answer briefly", live.TranscriptSecondaryText);
        parts.Raise(parts.Request(partial: "and then", instruction: " "));
        Assert.Equal("[In progress] and then", live.TranscriptSecondaryText);
        parts.Raise(parts.Request(transcript: null));
        Assert.Equal(string.Empty, live.TranscriptText);
        Assert.False(live.IsTranscriptPlaceholder);

        // A summary request with none of the three: the payload as indented JSON.
        parts.Raise(parts.Request(AnswerKind.Summary, transcript: "", partial: null, instruction: null, payloadJson: """{"sessionId":"s-1","turns":2}"""));
        Assert.Equal("Session summary requested", live.TranscriptHint);
        Assert.Equal("{" + NewLine + "  \"sessionId\": \"s-1\"," + NewLine + "  \"turns\": 2" + NewLine + "}", live.TranscriptText);
        Assert.Equal(string.Empty, live.TranscriptSecondaryText);

        // RequestCompleted: the answer, a blank line, the details. The transcript is unchanged.
        changed.Clear();
        parts.Raise(parts.Reply("""{"answer":"Cut the analytics dashboard.","details":"Both can ship a sprint later.","execution":{"model":"gpt-5.6-sol"}}"""));
        Assert.Equal("Cut the analytics dashboard." + NewLine + NewLine + "Both can ship a sprint later.", live.ReplyText);
        Assert.Empty(live.ReplyKeyPoints);
        Assert.Empty(live.ReplyActionItems);
        Assert.False(live.IsReplyThinking);
        Assert.False(live.IsReplyPlaceholder);
        Assert.Null(live.ReplyFailure);
        Assert.Equal("Sent to iPhone · gpt-5.6-sol · low · 0.85 s Codex", live.ReplyHint);
        Assert.Equal("PC relay total: 0.912 s. Codex includes startup and cloud processing.", live.ReplyTiming);
        Assert.Equal("Session summary requested", live.TranscriptHint);
        Assert.DoesNotContain(nameof(ILiveViewModel.TranscriptText), changed);
        Assert.Contains(nameof(ILiveViewModel.ReplyText), changed);
        Assert.Contains(nameof(ILiveViewModel.ReplyTiming), changed);

        // A summary: the summary stands where the answer would, then the two lists.
        parts.Raise(parts.Reply(
            """{"summary":"The launch moves a week.","keyPoints":["Onboarding is the risk","Design needs a week"],"actionItems":["Tell sales"," "]}""",
            kind: AnswerKind.Summary));
        Assert.Equal("The launch moves a week.", live.ReplyText);
        Assert.Equal(["Onboarding is the risk", "Design needs a week"], live.ReplyKeyPoints);
        Assert.Equal(["Tell sales"], live.ReplyActionItems);

        // An answer wins over a summary; only lists is still a reply; nothing at all is shown as JSON.
        parts.Raise(parts.Reply("""{"answer":"Yes.","summary":"Ignored."}"""));
        Assert.Equal("Yes.", live.ReplyText);
        parts.Raise(parts.Reply("""{"keyPoints":["One point"]}"""));
        Assert.Equal(string.Empty, live.ReplyText);
        Assert.Equal(["One point"], live.ReplyKeyPoints);
        parts.Raise(parts.Reply("""{"execution":{"model":"gpt-5.6-sol"}}"""));
        Assert.Equal("{" + NewLine + "  \"execution\": {" + NewLine + "    \"model\": \"gpt-5.6-sol\"" + NewLine + "  }" + NewLine + "}", live.ReplyText);
        Assert.False(live.IsReplyPlaceholder);

        // RequestFailed: the danger banner in place of the reply.
        parts.Raise(parts.Request());
        parts.Raise(parts.Failure(RelayCodes.AssistantFailed));
        Assert.Equal(
            new BannerModel(BannerTone.Danger, "Nothing was sent to the iPhone", "Assistant request failed or timed out. Check Codex sign-in and retry."),
            live.ReplyFailure);
        Assert.Equal("Not sent. Try Assist again.", live.ReplyHint);
        Assert.False(live.IsReplyThinking);
        Assert.False(live.IsReplyPlaceholder);
        Assert.Equal(string.Empty, live.ReplyText);
        Assert.Null(live.ReplyTiming);
        Assert.Equal("If we hold the March date, what would we have to cut?", live.TranscriptText);

        // The next request takes the banner away.
        parts.Raise(parts.Request());
        Assert.Null(live.ReplyFailure);
        Assert.True(live.IsReplyThinking);
        parts.Raise(parts.Reply("""{"answer":"Cut the dashboard."}"""));

        // Notices and speech status: the transcript hint shows the message; the reply is unchanged.
        parts.Raise(new AssistantNotice(parts.Now, "MODEL_CATALOG_UNAVAILABLE", "The assistant list could not be read."));
        Assert.Equal("The assistant list could not be read.", live.TranscriptHint);
        parts.Raise(new LocalModelChanged(parts.Now, new LocalModelStatus(LocalModelState.Starting, "nemotron", true, 0, "Starting Docker and loading the PC model."), "Starting Docker and loading the PC model."));
        Assert.Equal("Starting Docker and loading the PC model.", live.TranscriptHint);
        parts.Raise(new SpeechStreamChanged(parts.Now, SpeechStreamPhase.Ready, "nemotron", null, "Nemotron ready · PC GPU · no speech API cost."));
        Assert.Equal("Nemotron ready · PC GPU · no speech API cost.", live.TranscriptHint);
        Assert.Equal("Cut the dashboard.", live.ReplyText);
        Assert.Equal("Sent to iPhone · gpt-5.6-sol · low · 0.85 s Codex", live.ReplyHint);

        // SpeechText: a new line at the end; the hint and the reply stay.
        parts.Raise(new SpeechText(parts.Now, "Local words"));
        parts.Raise(new SpeechText(parts.Now, "and more"));
        Assert.Equal(
            "If we hold the March date, what would we have to cut?" + NewLine + "Local words" + NewLine + "and more",
            live.TranscriptText);
        Assert.Equal("Nemotron ready · PC GPU · no speech API cost.", live.TranscriptHint);
        Assert.Equal("Cut the dashboard.", live.ReplyText);

        // RelayEnded: both hints as at the start, no timing; what was said and answered stays.
        parts.Session.RaiseSignal(SessionSignal.RelayEnded);
        Assert.Equal("Live from your iPhone", live.TranscriptHint);
        Assert.EndsWith("and more", live.TranscriptText, StringComparison.Ordinal);
        Assert.Equal("Powered by Codex on your PC", live.ReplyHint);
        Assert.Equal("Cut the dashboard.", live.ReplyText);
        Assert.Null(live.ReplyTiming);

        // A start clears the activity log only (ActivityViewModelTests.Clearing): this page keeps its text.
        parts.Session.RaiseSignal(SessionSignal.RelayStarting);
        Assert.Equal("Cut the dashboard.", live.ReplyText);

        // ActivityCleared: both placeholders again, and the reply hint says why.
        parts.Session.RaiseSignal(SessionSignal.ActivityCleared);
        Assert.Equal("Live from your iPhone", live.TranscriptHint);
        Assert.Equal("Your next question will appear here.", live.TranscriptText);
        Assert.Equal(string.Empty, live.TranscriptSecondaryText);
        Assert.True(live.IsTranscriptPlaceholder);
        Assert.Equal("Activity cleared", live.ReplyHint);
        Assert.Equal("Ready when you are.\nTap Assist on your iPhone.", live.ReplyText);
        Assert.True(live.IsReplyPlaceholder);
        Assert.Null(live.ReplyTiming);

        // Speech text into an empty card starts the text; it is not a second line.
        parts.Raise(new SpeechText(parts.Now, "First words"));
        Assert.Equal("First words", live.TranscriptText);
        Assert.False(live.IsTranscriptPlaceholder);
    }

    [Fact]
    public void A_request_that_is_still_running_when_the_relay_ends_stops_spinning()
    {
        var parts = new PageParts();
        parts.Run();
        parts.Raise(parts.Request());
        Assert.True(parts.Live.IsReplyThinking);

        parts.Session.RaiseSignal(SessionSignal.RelayEnded);

        Assert.False(parts.Live.IsReplyThinking);
        Assert.True(parts.Live.IsReplyPlaceholder);
        Assert.Equal("Powered by Codex on your PC", parts.Live.ReplyHint);
        Assert.Equal("If we hold the March date, what would we have to cut?", parts.Live.TranscriptText);
    }

    [Fact]
    public void Timing_texts()
    {
        var parts = new PageParts();
        parts.Run();
        var live = parts.Live;

        // 850 ms of Codex inside 912 ms of relay: two decimals in the hint, three in the caption.
        parts.Raise(parts.Reply("""{"answer":"Yes."}""", durationMs: 912, codexMs: 850));
        Assert.Contains("0.85", live.ReplyHint, StringComparison.Ordinal);
        Assert.Equal("Sent to iPhone · gpt-5.6-sol · low · 0.85 s Codex", live.ReplyHint);
        Assert.Equal("PC relay total: 0.912 s. Codex includes startup and cloud processing.", live.ReplyTiming);

        parts.Raise(parts.Reply("""{"answer":"Yes."}""", durationMs: 12_345.6789, codexMs: 11_999.4));
        Assert.Equal("Sent to iPhone · gpt-5.6-sol · low · 12.00 s Codex", live.ReplyHint);
        Assert.Equal("PC relay total: 12.346 s. Codex includes startup and cloud processing.", live.ReplyTiming);

        // Numbers are written the same way whatever the culture of the PC is.
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            parts.Raise(parts.Reply("""{"answer":"Yes."}""", durationMs: 1500, codexMs: 1250));
            Assert.Equal("Sent to iPhone · gpt-5.6-sol · low · 1.25 s Codex", live.ReplyHint);
            Assert.Equal("PC relay total: 1.500 s. Codex includes startup and cloud processing.", live.ReplyTiming);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(RelayCodes.RequestCancelled, "Cancelled by the iPhone.")]
    [InlineData(RelayCodes.ClientDisconnected, "Cancelled by the iPhone.")]
    [InlineData(RelayCodes.AssistantFailed, "Assistant request failed or timed out. Check Codex sign-in and retry.")]
    [InlineData("SOME_OTHER_CODE", "Assistant request failed or timed out. Check Codex sign-in and retry.")]
    public void Failure_texts_by_code(string code, string body)
    {
        var parts = new PageParts();
        parts.Run();
        parts.Raise(parts.Request());

        parts.Raise(parts.Failure(code));

        // The body is the desktop's fixed text for the outcome, never the relay's message (ruling R-12).
        Assert.Equal(new BannerModel(BannerTone.Danger, "Nothing was sent to the iPhone", body), parts.Live.ReplyFailure);
        Assert.Equal("Not sent. Try Assist again.", parts.Live.ReplyHint);
    }

    [Fact]
    public void Controls_per_state()
    {
        var parts = new PageParts();
        var session = parts.Session;
        var live = parts.Live;

        // Stopped (AC-WIN-001): Start is offered, nothing else.
        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "Stopped"), live.RelayStatus);
        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "iPhone not connected"), live.PhoneStatus);
        Assert.True(live.IsStartVisible);
        Assert.False(live.IsStartBusy);
        Assert.Equal("Start relay", live.StartLabel);
        Assert.False(live.AreRunningControlsVisible);
        Assert.False(live.ArePrerequisitesVisible);
        Assert.Null(live.RelayStatusCaption);
        Assert.Equal("Shown when the relay is running", live.PcAddress);
        Assert.False(live.HasPcAddress);
        Assert.False(live.CopyPcAddressCommand.CanExecute(null));

        // Checking: the same control, busy.
        live.StartRelayCommand.Execute(null);
        Assert.Equal(1, session.StartCalls);
        session.State = RelayUiState.Checking;
        Assert.True(live.IsStartVisible);
        Assert.True(live.IsStartBusy);
        Assert.Equal("Checking…", live.StartLabel);
        Assert.Equal(new StatusIndicator(StatusMark.Spinner, StatusTone.Live, "Checking Codex and Tailscale…"), live.RelayStatus);
        session.State = RelayUiState.Starting;
        Assert.True(live.IsStartBusy);
        Assert.False(live.AreRunningControlsVisible);

        // Check failed: Start again, and the list takes the place of the two panels.
        session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>
        {
            [PrerequisiteId.CodexSignedIn] = PrerequisiteState.Failed,
        });
        session.StateText = PrerequisiteTexts.Failure(PrerequisiteId.CodexSignedIn);
        session.State = RelayUiState.PrerequisiteFailed;
        Assert.True(live.IsStartVisible);
        Assert.False(live.IsStartBusy);
        Assert.Equal("Start relay", live.StartLabel);
        Assert.True(live.ArePrerequisitesVisible);
        Assert.Equal(6, live.Prerequisites.Count);
        Assert.Equal(StatusMark.Alert, live.RelayStatus.Mark);
        Assert.Equal(StatusTone.Danger, live.RelayStatus.Tone);
        Assert.Equal("Codex is installed but not signed in. Open Codex and sign in with ChatGPT.", live.RelayStatus.Text);
        Assert.Equal(live.RelayStatus.Text, live.RelayStatus.ToolTip);
        live.CheckAgainCommand.Execute(null);
        Assert.Equal(1, session.CheckAgainCalls);
        live.OpenSettingsCommand.Execute(null);
        Assert.Equal([AppPage.Settings], parts.Navigator.Visited);

        // Ready: Pause and Stop; no accent control; the address is shown and can be copied.
        session.StateText = null;
        session.AdvertisedEndpoint = PageParts.Endpoint;
        session.State = RelayUiState.Ready;
        Assert.False(live.IsStartVisible);
        Assert.True(live.AreRunningControlsVisible);
        Assert.False(live.ArePrerequisitesVisible);
        Assert.Equal("Pause relay", live.PauseResumeLabel);
        Assert.False(live.IsPaused);
        Assert.Equal(new StatusIndicator(StatusMark.Dot, StatusTone.Success, "Ready"), live.RelayStatus);
        Assert.Equal(PageParts.Endpoint, live.PcAddress);
        Assert.True(live.HasPcAddress);
        live.CopyPcAddressCommand.Execute(null);
        Assert.Equal(PageParts.Endpoint, parts.Clipboard.Last);
        Assert.Equal(["Copied"], parts.Toasts.Texts);

        // A clipboard that is not available shows no toast.
        parts.Clipboard.Available = false;
        live.CopyPcAddressCommand.Execute(null);
        Assert.Single(parts.Toasts.Texts);

        // Paused: the label changes only when the session says so; the caption explains the state.
        live.PauseResumeCommand.Execute(null);
        Assert.Equal(1, session.TogglePauseCalls);
        Assert.Equal("Pause relay", live.PauseResumeLabel);
        session.IsPaused = true;
        session.State = RelayUiState.Paused;
        Assert.Equal("Resume relay", live.PauseResumeLabel);
        Assert.True(live.IsPaused);
        Assert.True(live.AreRunningControlsVisible);
        Assert.Equal(new StatusIndicator(StatusMark.Pause, StatusTone.Warning, "Paused"), live.RelayStatus);
        Assert.Equal("New requests from the iPhone are refused until you resume.", live.RelayStatusCaption);

        // Thinking and Request failed: the running controls stay.
        session.IsPaused = false;
        session.State = RelayUiState.Thinking;
        Assert.Equal(new StatusIndicator(StatusMark.Spinner, StatusTone.Live, "Thinking…"), live.RelayStatus);
        Assert.Null(live.RelayStatusCaption);
        Assert.True(live.AreRunningControlsVisible);
        Assert.Equal("Pause relay", live.PauseResumeLabel);
        session.State = RelayUiState.RequestFailed;
        Assert.Equal(new StatusIndicator(StatusMark.Alert, StatusTone.Danger, "Request failed. Ready to retry."), live.RelayStatus);
        Assert.True(live.AreRunningControlsVisible);
        Assert.False(live.IsStartVisible);

        // Stop: the stopped layout; the address stays.
        live.StopRelayCommand.Execute(null);
        Assert.Equal(1, session.StopCalls);
        session.State = RelayUiState.Stopped;
        Assert.True(live.IsStartVisible);
        Assert.False(live.AreRunningControlsVisible);
        Assert.Equal(PageParts.Endpoint, live.PcAddress);

        // A start fault: the stopped layout, and its text stays as the status, whole in the tooltip.
        var portInUse = PrerequisiteTexts.StartFault(RelayFaultCode.PortInUse, 47841);
        session.FaultCode = RelayFaultCode.PortInUse;
        session.StateText = portInUse;
        session.State = RelayUiState.Faulted;
        Assert.Equal(new StatusIndicator(StatusMark.Alert, StatusTone.Danger, portInUse, portInUse), live.RelayStatus);
        Assert.True(live.IsStartVisible);
        Assert.False(live.ArePrerequisitesVisible);

        // A fault while running: a warning, not a danger.
        session.FaultCode = RelayFaultCode.Internal;
        session.StateText = "Relay stopped. Click Start relay to reconnect.";
        Assert.Equal(StatusMark.Dot, live.RelayStatus.Mark);
        Assert.Equal(StatusTone.Warning, live.RelayStatus.Tone);
        Assert.Equal("Relay stopped. Click Start relay to reconnect.", live.RelayStatus.Text);
    }

    [Fact]
    public void Each_change_of_the_session_is_announced_for_what_it_changes()
    {
        var parts = new PageParts();
        var changed = new List<string?>();
        parts.Live.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        parts.Session.State = RelayUiState.Ready;
        Assert.Contains(nameof(ILiveViewModel.RelayStatus), changed);
        Assert.Contains(nameof(ILiveViewModel.IsStartVisible), changed);
        Assert.Contains(nameof(ILiveViewModel.AreRunningControlsVisible), changed);
        Assert.Contains(nameof(ILiveViewModel.ArePrerequisitesVisible), changed);
        Assert.Contains(nameof(ILiveViewModel.ConnectionBanner), changed);

        changed.Clear();
        parts.Session.IsPaused = true;
        Assert.Equal([nameof(ILiveViewModel.IsPaused), nameof(ILiveViewModel.PauseResumeLabel)], changed);

        changed.Clear();
        parts.Session.AdvertisedEndpoint = PageParts.Endpoint;
        Assert.Equal([nameof(ILiveViewModel.PcAddress), nameof(ILiveViewModel.HasPcAddress)], changed);

        changed.Clear();
        parts.Session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>());
        Assert.Equal([nameof(ILiveViewModel.Prerequisites)], changed);

        changed.Clear();
        parts.Session.Connection = PageParts.Inspection(MappingState.Absent);
        Assert.Contains(nameof(ILiveViewModel.ConnectionBanner), changed);
        Assert.Contains(nameof(ILiveViewModel.ExposureBanner), changed);

        changed.Clear();
        parts.Session.Settings = RelaySettings.Default with { ConnectionMode = ConnectionMode.Public };
        Assert.Contains(nameof(ILiveViewModel.ConnectionBanner), changed);
        Assert.Contains(nameof(ILiveViewModel.ExposureBanner), changed);

        changed.Clear();
        parts.Session.Presence = PhonePresence.Connected;
        Assert.Equal([nameof(ILiveViewModel.PhoneStatus)], changed);
    }

    [Fact]
    public void The_phone_tooltip_tells_the_age_of_the_last_heartbeat()
    {
        var parts = new PageParts();
        var live = parts.Live;
        var announced = 0;
        live.PropertyChanged += (_, change) => announced += change.PropertyName == nameof(ILiveViewModel.PhoneStatus) ? 1 : 0;

        // No phone yet: no tooltip, and nothing ticks.
        parts.Run();
        Assert.Null(live.PhoneStatus.ToolTip);
        parts.Clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(0, announced);

        parts.Session.LastPhoneSeen = parts.Now;
        parts.Session.Presence = PhonePresence.Connected;
        Assert.Equal("iPhone connected", live.PhoneStatus.Text);
        Assert.Equal(
            "Last authenticated heartbeat: 0 seconds ago. Idle does not prove the phone is offline: iOS stops the heartbeat when the app is in the background.",
            live.PhoneStatus.ToolTip);

        // The age is told again once a tick, so the tooltip (and the sidebar's) is never stale.
        announced = 0;
        parts.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, announced);
        Assert.StartsWith("Last authenticated heartbeat: 1 second ago.", live.PhoneStatus.ToolTip, StringComparison.Ordinal);

        parts.Clock.Advance(TimeSpan.FromSeconds(19));
        parts.Session.Presence = PhonePresence.Idle;
        Assert.Equal(new StatusIndicator(StatusMark.Dot, StatusTone.Warning, "iPhone idle", live.PhoneStatus.ToolTip), live.PhoneStatus);
        Assert.StartsWith("Last authenticated heartbeat: 20 seconds ago.", live.PhoneStatus.ToolTip, StringComparison.Ordinal);

        // The relay stopped: nothing is known about a phone, and the ticking ends.
        parts.Session.LastPhoneSeen = null;
        parts.Session.Presence = PhonePresence.NotConnected;
        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "iPhone not connected"), live.PhoneStatus);
        announced = 0;
        parts.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(0, announced);
    }

    [Fact]
    public void Connection_banner_cases()
    {
        const string NotSetUp = "Connection not set up. Choose Set up connection to let your iPhone reach this PC.";
        var parts = new PageParts();
        var session = parts.Session;
        var live = parts.Live;
        var canExecuteChanged = 0;
        live.SetUpConnectionCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;

        // The relay is not running: no banner, whatever the mapping is.
        session.Connection = PageParts.Inspection(MappingState.Absent);
        Assert.Null(live.ConnectionBanner);
        Assert.False(live.SetUpConnectionCommand.CanExecute(null));

        // Running before the first inspection: nothing is known, so nothing is claimed.
        session.Connection = null;
        parts.Run();
        Assert.Null(live.ConnectionBanner);

        // Absent: the banner, and the button opens the dialog that shows the command (it runs nothing).
        session.Connection = PageParts.Inspection(MappingState.Absent);
        Assert.Equal(new BannerModel(BannerTone.Warning, null, NotSetUp, "Set up connection…"), live.ConnectionBanner);
        Assert.True(live.SetUpConnectionCommand.CanExecute(null));
        Assert.True(canExecuteChanged > 0);
        live.SetUpConnectionCommand.Execute(null);
        var dialog = Assert.IsType<MappingDialogViewModel>(Assert.Single(parts.Dialogs.Shown));
        Assert.Equal(MappingAction.ApplyPrivate, dialog.Action);
        Assert.Equal(MappingCommand.ApplyPrivate(11443, 47841).DisplayText, dialog.CommandText);
        Assert.Empty(session.ExecutedPlans);

        // A plan that cannot be made opens nothing.
        dialog.CancelCommand.Execute(null);
        session.PlanRefusal = MappingRefusal.InvalidSettings;
        live.SetUpConnectionCommand.Execute(null);
        Assert.Single(parts.Dialogs.Shown);
        session.PlanRefusal = MappingRefusal.None;

        // Blocked: the reason follows, and the button is disabled.
        session.Connection = PageParts.Inspection(MappingState.Blocked, MappingRefusal.PrivatePortHasFunnel);
        Assert.Equal(
            new BannerModel(
                BannerTone.Warning,
                null,
                NotSetUp + " Port 11443 has Funnel turned on. A private listener there would turn Funnel off for the whole port. Choose another private HTTPS port.",
                "Set up connection…"),
            live.ConnectionBanner);
        Assert.False(live.SetUpConnectionCommand.CanExecute(null));
        live.SetUpConnectionCommand.Execute(null);
        Assert.Single(parts.Dialogs.Shown);

        // Unknown: Tailscale's configuration could not be read.
        session.Connection = PageParts.Inspection(MappingState.Unknown, MappingRefusal.StatusUnavailable, TailscaleReadFailure.Failed);
        Assert.Equal(
            NotSetUp + " LiveCue could not read Tailscale's configuration, so it changed nothing. Check that Tailscale is running, then choose Check again.",
            live.ConnectionBanner?.Body);
        Assert.Equal(BannerTone.Warning, live.ConnectionBanner?.Tone);
        Assert.False(live.SetUpConnectionCommand.CanExecute(null));

        // Unknown without a reason: the sentence alone.
        session.Connection = PageParts.Inspection(MappingState.Unknown);
        Assert.Equal(NotSetUp, live.ConnectionBanner?.Body);

        // Present, ours or not: the banner goes.
        session.Connection = PageParts.Inspection(MappingState.Present);
        Assert.Null(live.ConnectionBanner);
        session.Connection = PageParts.Inspection(MappingState.PresentNotOwned);
        Assert.Null(live.ConnectionBanner);

        // The mode the settings select decides which mapping counts: Public is absent here.
        session.Settings = RelaySettings.Default with { ConnectionMode = ConnectionMode.Public };
        Assert.Equal(NotSetUp, live.ConnectionBanner?.Body);
        Assert.True(live.SetUpConnectionCommand.CanExecute(null));
        live.SetUpConnectionCommand.Execute(null);
        Assert.Equal(MappingAction.ApplyPublic, Assert.IsType<MappingDialogViewModel>(parts.Dialogs.Shown[^1]).Action);

        // The relay stops: the banner goes with it.
        session.State = RelayUiState.Stopped;
        Assert.Null(live.ConnectionBanner);
        Assert.False(live.SetUpConnectionCommand.CanExecute(null));
    }

    [Fact]
    public void Exposure_banner_cases()
    {
        var parts = new PageParts();
        var session = parts.Session;
        var live = parts.Live;
        var ours = new OwnedMapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:47841", DateTimeOffset.UnixEpoch);
        var oursLive = new ExposedHandler(11443, "/", FunnelOn: false, Foreground: false, ours);
        var strangerInTailnet = new ExposedHandler(8443, "/other", FunnelOn: false, Foreground: false, LedgerEntry: null);
        var strangerOnInternet = new ExposedHandler(10000, "/livecue", FunnelOn: true, Foreground: false, LedgerEntry: null);

        // No inspection yet: nothing is known, nothing is shown.
        Assert.Null(live.ExposureBanner);

        // Only our own mapping forwards to the relay: nothing to warn about.
        session.Connection = PageParts.Inspection(MappingState.Present, exposure: oursLive);
        Assert.Null(live.ExposureBanner);

        // Private mode and something publishes the relay through Funnel: danger. Shown while stopped too.
        Assert.Equal(RelayUiState.Stopped, session.State);
        session.Connection = PageParts.Inspection(MappingState.Present, exposure: [oursLive, strangerOnInternet]);
        Assert.Equal(
            new BannerModel(
                BannerTone.Danger,
                "This relay is reachable from the internet",
                "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode.",
                "Open Settings"),
            live.ExposureBanner);

        // The same in Public mode is a mapping LiveCue does not manage: a warning with the count.
        session.Settings = RelaySettings.Default with { ConnectionMode = ConnectionMode.Public };
        Assert.Equal(
            new BannerModel(
                BannerTone.Warning,
                null,
                "1 Tailscale mapping that LiveCue does not manage points at this relay. Open Settings to see it and how to remove it.",
                "Open Settings"),
            live.ExposureBanner);
        session.Settings = RelaySettings.Default;

        // Mappings inside the tailnet that are not ours: a warning in either mode.
        session.Connection = PageParts.Inspection(
            MappingState.Present, exposure: [oursLive, strangerInTailnet, strangerInTailnet with { HttpsPort = 9443 }]);
        Assert.Equal(
            new BannerModel(
                BannerTone.Warning,
                null,
                "2 Tailscale mappings that LiveCue does not manage point at this relay. Open Settings to see them and how to remove them.",
                "Open Settings"),
            live.ExposureBanner);

        // Our own public mapping left behind in Private mode is still the danger case: first match wins.
        var oursPublic = new ExposedHandler(
            10000, "/livecue", FunnelOn: true, Foreground: false,
            new OwnedMapping(ConnectionMode.Public, 10000, "/livecue", "http://127.0.0.1:47841", DateTimeOffset.UnixEpoch));
        session.Connection = PageParts.Inspection(MappingState.Present, exposure: [oursPublic, strangerInTailnet]);
        Assert.Equal(BannerTone.Danger, live.ExposureBanner?.Tone);

        // The serve status could not be read: exposure is unknown, not absent.
        foreach (var failure in new[] { TailscaleReadFailure.Failed, TailscaleReadFailure.TimedOut, TailscaleReadFailure.Unparseable })
        {
            session.Connection = PageParts.Inspection(MappingState.Unknown, MappingRefusal.StatusUnavailable, failure);
            Assert.Equal(
                new BannerModel(
                    BannerTone.Warning,
                    null,
                    "Tailscale's configuration could not be read, so LiveCue cannot tell what is exposed. Choose Check again.",
                    "Open Settings"),
                live.ExposureBanner);
        }

        // Without Tailscale there is nothing that could expose the relay.
        session.Connection = PageParts.Inspection(
            MappingState.Unknown, MappingRefusal.TailscaleNotInstalled, TailscaleReadFailure.NotInstalled);
        Assert.Null(live.ExposureBanner);

        // The banner's one action opens Settings; it runs nothing.
        live.OpenSettingsCommand.Execute(null);
        Assert.Equal([AppPage.Settings], parts.Navigator.Visited);
        Assert.Empty(parts.Dialogs.Shown);
        Assert.Empty(session.ExecutedPlans);
    }
}
