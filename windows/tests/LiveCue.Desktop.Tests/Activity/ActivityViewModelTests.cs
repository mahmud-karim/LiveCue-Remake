using System.Text.RegularExpressions;
using LiveCue.Desktop.Activity;
using LiveCue.Desktop.Tests.Live;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop.Tests.Activity;

/// <summary>
/// The Activity page's view model over a <see cref="FakeSession"/> (test plan 2.7 and ST-19; W-design
/// 5.7; screens section 4): which line each event writes, what clears the log, and the three
/// rejected-request counters.
/// </summary>
public sealed partial class ActivityViewModelTests
{
    private static readonly string NewLine = Environment.NewLine;

    [Fact]
    public void Line_cases()
    {
        var parts = new PageParts();
        var session = parts.Session;
        var activity = parts.Activity;

        Assert.True(activity.IsEmpty);
        Assert.Equal(string.Empty, activity.EntryCountText);
        Assert.Equal(string.Empty, activity.Text);
        Assert.False(activity.ClearActivityCommand.CanExecute(null));

        // RelayStarted: one line in Private mode.
        parts.Run();
        Assert.Equal(["Relay started. Waiting for your iPhone."], Messages(activity));
        Assert.Equal("1 entry", activity.EntryCountText);
        Assert.False(activity.IsEmpty);
        Assert.True(activity.ClearActivityCommand.CanExecute(null));

        // In Public mode a second line says what that means for the phone.
        parts.Run(ConnectionMode.Public);
        Assert.Equal(
            [
                "Relay started. Waiting for your iPhone.",
                "Public mode: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address.",
            ],
            Messages(activity));

        // Notice: its message.
        parts.Raise(new Notice(
            parts.Now,
            NoticeKind.PairingRestored,
            "Saved pairing restored. Existing iPhones reconnect automatically; Show QR keeps their pairing valid."));
        Assert.Equal(
            "Saved pairing restored. Existing iPhones reconnect automatically; Show QR keeps their pairing valid.",
            Last(activity).Message);

        // SelfCheckCompleted: exactly one line per event (AC-WIN-024), one of three texts.
        var before = activity.Entries.Count;
        parts.Raise(new PairingShown(parts.Now, PageParts.Endpoint));
        Assert.Equal(before, activity.Entries.Count);
        parts.Raise(new SelfCheckCompleted(parts.Now, SelfCheckResult.Verified));
        Assert.Equal(before + 1, activity.Entries.Count);
        Assert.Equal("Secure PC connection verified.", Last(activity).Message);
        parts.Raise(new SelfCheckCompleted(parts.Now, SelfCheckResult.VerifiedThroughFunnel));
        Assert.Equal("Secure Funnel connection verified. The iPhone VPN is not required.", Last(activity).Message);
        parts.Raise(new SelfCheckCompleted(parts.Now, SelfCheckResult.NotVerified));
        Assert.Equal(
            "Pairing code created, but the advertised connection could not be verified. Check the PC tunnel before pairing.",
            Last(activity).Message);
        Assert.Equal(before + 3, activity.Entries.Count);

        // RequestStarted: the kind and the id, and the payload indented by two spaces.
        parts.Clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(46));
        parts.Raise(parts.Request());
        Assert.Equal("INCOMING assist [7f3a9c12]", Last(activity).Message);
        Assert.Equal("09:02:46", Last(activity).TimeText);
        Assert.Equal(
            string.Join(
                NewLine,
                "{",
                "  \"transcript\": \"If we hold the March date, what would we have to cut?\",",
                "  \"partialTranscript\": \"and could the analytics dashboard move to the sprint after\",",
                "  \"instruction\": \"answer briefly\"",
                "}"),
            Last(activity).Payload);
        parts.Raise(parts.Request(AnswerKind.Summary, payloadJson: """{"turns":2}"""));
        Assert.Equal("INCOMING summary [7f3a9c12]", Last(activity).Message);
        Assert.Equal("{" + NewLine + "  \"turns\": 2" + NewLine + "}", Last(activity).Payload);

        // RequestCompleted: the id, the relay's total with two decimals, and the reply.
        parts.Raise(parts.Reply("""{"answer":"Cut the dashboard."}""", durationMs: 912));
        Assert.Equal("REPLY [7f3a9c12] 0.91 s", Last(activity).Message);
        Assert.Equal("{" + NewLine + "  \"answer\": \"Cut the dashboard.\"" + NewLine + "}", Last(activity).Payload);
        Assert.EndsWith(
            "09:02:46 REPLY [7f3a9c12] 0.91 s" + NewLine + "{" + NewLine + "  \"answer\": \"Cut the dashboard.\"" + NewLine + "}",
            activity.Text,
            StringComparison.Ordinal);
        parts.Raise(parts.Reply("""{"answer":"Yes."}""", durationMs: 12_345.6));
        Assert.Equal("REPLY [7f3a9c12] 12.35 s", Last(activity).Message);

        // RequestFailed: the same text as the Live banner's body, and no payload.
        parts.Raise(parts.Failure(RelayCodes.AssistantFailed));
        Assert.Equal("Assistant request failed or timed out. Check Codex sign-in and retry.", Last(activity).Message);
        Assert.Null(Last(activity).Payload);
        Assert.Equal(parts.Live.ReplyFailure?.Body, Last(activity).Message);
        parts.Raise(parts.Failure(RelayCodes.RequestCancelled));
        Assert.Equal("Cancelled by the iPhone.", Last(activity).Message);
        parts.Raise(parts.Failure(RelayCodes.ClientDisconnected));
        Assert.Equal("Cancelled by the iPhone.", Last(activity).Message);

        // AssistantNotice, LocalModelChanged, SpeechStreamChanged: the message.
        parts.Raise(new AssistantNotice(parts.Now, "MODEL_CATALOG_UNAVAILABLE", "The assistant list could not be read."));
        Assert.Equal("The assistant list could not be read.", Last(activity).Message);
        parts.Raise(new LocalModelChanged(
            parts.Now,
            new LocalModelStatus(LocalModelState.Starting, "nemotron", true, 0, "Starting Docker and loading the PC model."),
            "Starting Docker and loading the PC model."));
        Assert.Equal("Starting Docker and loading the PC model.", Last(activity).Message);
        parts.Raise(new SpeechStreamChanged(parts.Now, SpeechStreamPhase.Loading, "nemotron", null, "Loading Nemotron on the PC GPU…"));
        Assert.Equal("Loading Nemotron on the PC GPU…", Last(activity).Message);

        // Speech text is the transcript's, not the log's.
        before = activity.Entries.Count;
        parts.Raise(new SpeechText(parts.Now, "Local words"));
        parts.Raise(new PhoneSeen(parts.Now));
        parts.Raise(new AcceptingChanged(parts.Now, Accepting: false));
        parts.Raise(new RelayStopped(parts.Now, RelayStopReason.Requested));
        Assert.Equal(before, activity.Entries.Count);

        // RelayFaulted: the session's text for the fault (with the port), not the relay's own message.
        session.StateText = PrerequisiteTexts.StartFault(RelayFaultCode.PortInUse, 47841);
        parts.Raise(new RelayFaulted(parts.Now, RelayFaultCode.PortInUse, "The relay port is already in use."));
        Assert.Equal(
            "Port 47841 is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay.",
            Last(activity).Message);
        session.StateText = null;
        parts.Raise(new RelayFaulted(parts.Now, RelayFaultCode.Internal, "The relay stopped after an internal error."));
        Assert.Equal("The relay stopped after an internal error.", Last(activity).Message);

        // Diagnostic: prefixed (AC-WIN-034).
        parts.Raise(new Diagnostic(parts.Now, "edge", "A request failed unexpectedly. (InvalidOperationException)"));
        Assert.Equal("Relay diagnostic: A request failed unexpectedly. (InvalidOperationException)", Last(activity).Message);

        // MessageRaised: the session's own lines, whatever their kind.
        session.RaiseMessage(new SessionMessage(parts.Now, SessionMessageKind.Error, "The desktop command failed. Please retry."));
        Assert.Equal("The desktop command failed. Please retry.", Last(activity).Message);
        session.RaiseMessage(new SessionMessage(parts.Now, SessionMessageKind.Info, "Tailscale mapping set up."));
        Assert.Equal("Tailscale mapping set up.", Last(activity).Message);
        session.RaiseMessage(new SessionMessage(parts.Now, SessionMessageKind.Warning, MappingTexts.OthersChanged));
        Assert.Equal(MappingTexts.OthersChanged, Last(activity).Message);

        // The footer counts the entries, and the entries are the read-only collection of the contract.
        Assert.Equal(activity.Entries.Count + " entries", activity.EntryCountText);
        Assert.All(activity.Entries, entry => Assert.Matches("^[0-9]{2}:[0-9]{2}:[0-9]{2}$", entry.TimeText));
        Assert.StartsWith("09:00:00 Relay started. Waiting for your iPhone.", activity.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Clearing()
    {
        var parts = new PageParts();
        var session = parts.Session;
        var activity = parts.Activity;
        var live = parts.Live;
        var changed = new List<string?>();
        activity.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        parts.Run();
        parts.Raise(parts.Request());
        parts.Raise(parts.Reply("""{"answer":"Cut the dashboard."}"""));
        Assert.Equal(3, activity.Entries.Count);
        Assert.Contains(nameof(IActivityViewModel.IsEmpty), changed);
        Assert.Contains(nameof(IActivityViewModel.EntryCountText), changed);
        Assert.Contains(nameof(IActivityViewModel.Text), changed);

        // A start clears the log only: the transcript and the reply of the last run stay on Live.
        session.RaiseSignal(SessionSignal.RelayStarting);
        Assert.True(activity.IsEmpty);
        Assert.Equal(string.Empty, activity.EntryCountText);
        Assert.False(activity.ClearActivityCommand.CanExecute(null));
        Assert.Equal("If we hold the March date, what would we have to cut?", live.TranscriptText);
        Assert.Equal("Cut the dashboard.", live.ReplyText);
        Assert.False(live.IsReplyPlaceholder);

        // What the session says after that signal stays: a start's own lines are not lost.
        session.RaiseMessage(new SessionMessage(parts.Now, SessionMessageKind.Warning, "Codex is version 1.3.0."));
        session.RaiseRelayEvent(new RelayStarted(parts.Now, PageParts.Endpoint, 47841, ConnectionMode.Private, 2, "1.0.0", false));
        Assert.Equal(["Codex is version 1.3.0.", "Relay started. Waiting for your iPhone."], Messages(activity));

        // The relay ending clears nothing here.
        session.RaiseSignal(SessionSignal.RelayEnded);
        Assert.Equal(2, activity.Entries.Count);

        // "Clear activity" goes through the session and empties the log, the transcript and the reply.
        var canExecuteChanged = 0;
        activity.ClearActivityCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;
        Assert.True(activity.ClearActivityCommand.CanExecute(null));
        activity.ClearActivityCommand.Execute(null);
        Assert.Equal(1, session.ClearActivityCalls);
        Assert.True(activity.IsEmpty);
        Assert.Equal(string.Empty, activity.Text);
        Assert.False(activity.ClearActivityCommand.CanExecute(null));
        Assert.True(canExecuteChanged > 0);
        Assert.Equal("Your next question will appear here.", live.TranscriptText);
        Assert.True(live.IsTranscriptPlaceholder);
        Assert.Equal("Live from your iPhone", live.TranscriptHint);
        Assert.Equal("Ready when you are.\nTap Assist on your iPhone.", live.ReplyText);
        Assert.True(live.IsReplyPlaceholder);
        Assert.Equal("Activity cleared", live.ReplyHint);
        Assert.Null(live.ReplyTiming);

        // The log goes on after a clear.
        session.RaiseMessage(new SessionMessage(parts.Now, SessionMessageKind.Info, "one"));
        Assert.Equal("1 entry", activity.EntryCountText);
    }

    [Fact]
    public void A_trimmed_log_starts_with_the_marker_which_is_not_counted()
    {
        var parts = new PageParts();
        var activity = parts.Activity;
        parts.Run();

        // Replies of about 10,000 characters each: the log passes its upper bound more than once.
        var answer = new string('a', 10_000);
        for (var index = 0; index < 60; index++)
        {
            parts.Raise(parts.Reply("{\"answer\":\"" + answer + "\"}"));
            Assert.True(activity.Text.Length <= 200_000);
        }

        Assert.True(activity.Entries[0].IsTrimMarker);
        Assert.Equal("[Older activity removed from memory]", activity.Entries[0].Message);
        Assert.StartsWith("[Older activity removed from memory]" + NewLine + "09:00:00 REPLY [", activity.Text, StringComparison.Ordinal);
        Assert.Equal((activity.Entries.Count - 1) + " entries", activity.EntryCountText);
        Assert.DoesNotContain(activity.Entries, entry => entry.Message == "Relay started. Waiting for your iPhone.");
    }

    [Fact]
    public void No_token()
    {
        // The token never reaches the desktop: no event carries it, and the window receives a matrix only.
        // So whatever the relay reports, no line of the log holds it or anything shaped like it.
        var token = new string('A', ProtocolConstants.TokenLength);
        var parts = new PageParts();
        var session = parts.Session;

        parts.Run(ConnectionMode.Public);
        parts.Raise(new Notice(parts.Now, NoticeKind.PairingRestored, "Saved pairing restored. Existing iPhones reconnect automatically; Show QR keeps their pairing valid."));
        parts.Raise(new PairingShown(parts.Now, PageParts.Endpoint));
        session.RaisePairingQr(new PairingQr(PageParts.Endpoint, [.. Enumerable.Range(0, 21).Select(_ => new bool[21])]));
        parts.Raise(new SelfCheckCompleted(parts.Now, SelfCheckResult.Verified));
        parts.Raise(new PhoneSeen(parts.Now));
        parts.Raise(parts.Request());
        parts.Raise(parts.Reply("""{"answer":"Cut the dashboard.","execution":{"model":"gpt-5.6-sol","reasoningEffort":"low"}}"""));
        parts.Raise(parts.Request(AnswerKind.Summary));
        parts.Raise(parts.Failure(RelayCodes.ClientDisconnected));
        parts.Raise(new Notice(parts.Now, NoticeKind.PairingReset, "Pairing reset. Every iPhone must pair again."));
        parts.Raise(new EdgeStatistics(parts.Now, 3, 2, 1));
        parts.Raise(new Diagnostic(parts.Now, "pairing", "The pairing file could not be written. (IOException)"));
        parts.Raise(new RelayFaulted(parts.Now, RelayFaultCode.PairingStoreUnreadable, "The saved pairing could not be read."));
        session.RaiseMessage(new SessionMessage(parts.Now, SessionMessageKind.Info, "Pairing reset. Every iPhone must pair again."));

        Assert.True(parts.Activity.Entries.Count >= 10);
        var lines = parts.Activity.Text.Split(NewLine);
        Assert.All(lines, line => Assert.DoesNotContain(token, line, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.DoesNotMatch(TokenShape(), line));
        Assert.All(parts.Activity.Entries, entry => Assert.DoesNotMatch(TokenShape(), entry.Message + "\n" + entry.Payload));
        Assert.DoesNotMatch(TokenShape(), parts.Activity.RejectedCountsText);
        Assert.DoesNotMatch(TokenShape(), parts.Live.TranscriptText + "\n" + parts.Live.TranscriptSecondaryText + "\n" + parts.Live.ReplyText);
    }

    [Fact]
    public void Rejected_counts_follow_the_event_and_add_no_entry()
    {
        var parts = new PageParts();
        var activity = parts.Activity;
        var changed = new List<string?>();
        activity.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        // Before the first event: three zeros, in one fixed sentence.
        Assert.Equal((0L, 0L, 0L), (activity.RejectedMalformed, activity.RejectedTokenNotAccepted, activity.RejectedFunnelInPrivateMode));
        Assert.Equal(
            "Rejected since the relay started: 0 malformed · 0 with an unknown token · 0 through Funnel in Private mode",
            activity.RejectedCountsText);

        parts.Run();
        var entries = activity.Entries.Count;
        changed.Clear();

        // The event's totals are taken as they are. No entry is written, whatever the values.
        parts.Raise(new EdgeStatistics(parts.Now, Malformed: 12, TokenNotAccepted: 3, FunnelInPrivateMode: 1));
        Assert.Equal((12L, 3L, 1L), (activity.RejectedMalformed, activity.RejectedTokenNotAccepted, activity.RejectedFunnelInPrivateMode));
        Assert.Equal(
            "Rejected since the relay started: 12 malformed · 3 with an unknown token · 1 through Funnel in Private mode",
            activity.RejectedCountsText);
        Assert.Equal(entries, activity.Entries.Count);
        Assert.Equal(
            [
                nameof(IActivityViewModel.RejectedMalformed),
                nameof(IActivityViewModel.RejectedTokenNotAccepted),
                nameof(IActivityViewModel.RejectedFunnelInPrivateMode),
                nameof(ActivityViewModel.RejectedCountsText),
            ],
            changed);

        // Totals, not increments: the next event replaces them. Only what changed is announced.
        changed.Clear();
        parts.Raise(new EdgeStatistics(parts.Now, 12, 1_234_567, 1));
        Assert.Equal((12L, 1_234_567L, 1L), (activity.RejectedMalformed, activity.RejectedTokenNotAccepted, activity.RejectedFunnelInPrivateMode));
        Assert.Equal([nameof(IActivityViewModel.RejectedTokenNotAccepted), nameof(ActivityViewModel.RejectedCountsText)], changed);

        // Plain numbers in the invariant culture: no grouping, whatever the PC is set to.
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(
                "Rejected since the relay started: 12 malformed · 1234567 with an unknown token · 1 through Funnel in Private mode",
                activity.RejectedCountsText);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }

        // No alert at any value: nothing was logged, nothing was shown as a toast, no dialog opened.
        parts.Raise(new EdgeStatistics(parts.Now, long.MaxValue, long.MaxValue, long.MaxValue));
        Assert.Equal(entries, activity.Entries.Count);
        Assert.Empty(parts.Toasts.Texts);
        Assert.Empty(parts.Dialogs.Shown);
        Assert.Empty(parts.Navigator.Visited);
    }

    [Fact]
    public void The_counts_return_to_zero_at_a_start_and_survive_clear_activity()
    {
        var parts = new PageParts();
        var activity = parts.Activity;
        parts.Run();
        parts.Raise(new EdgeStatistics(parts.Now, 5, 4, 3));

        // "Clear activity" empties the log; the counters are not part of it.
        activity.ClearActivityCommand.Execute(null);
        Assert.True(activity.IsEmpty);
        Assert.Equal((5L, 4L, 3L), (activity.RejectedMalformed, activity.RejectedTokenNotAccepted, activity.RejectedFunnelInPrivateMode));

        // The relay stopping keeps the last totals on the page: they describe the run that ended.
        parts.Raise(new EdgeStatistics(parts.Now, 6, 4, 3));
        parts.Raise(new RelayStopped(parts.Now, RelayStopReason.Requested));
        parts.Session.RaiseSignal(SessionSignal.RelayEnded);
        Assert.Equal((6L, 4L, 3L), (activity.RejectedMalformed, activity.RejectedTokenNotAccepted, activity.RejectedFunnelInPrivateMode));

        // A new relay instance counts from zero.
        parts.Session.RaiseSignal(SessionSignal.RelayStarting);
        Assert.Equal((0L, 0L, 0L), (activity.RejectedMalformed, activity.RejectedTokenNotAccepted, activity.RejectedFunnelInPrivateMode));
        Assert.Equal(
            "Rejected since the relay started: 0 malformed · 0 with an unknown token · 0 through Funnel in Private mode",
            activity.RejectedCountsText);
    }

    private static List<string> Messages(ActivityViewModel activity) => activity.Entries.Select(entry => entry.Message).ToList();

    private static ActivityEntry Last(ActivityViewModel activity) => activity.Entries[^1];

    /// <summary>Anything that could be a pairing token: 43 characters of its alphabet in a row.</summary>
    [GeneratedRegex("[A-Za-z0-9_-]{43}")]
    private static partial Regex TokenShape();
}
