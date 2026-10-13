using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.Live;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop.Tests.Live;

/// <summary>
/// The Live page in the real window, off-screen, over a fake session (screens section 2; W-design 5.5):
/// every state of screens 2.9 as the window draws it, at the default and at the minimum size. With the
/// environment variable <c>LIVECUE_TEST_SHOTS</c> set, each scene is also written as a PNG for a person
/// to compare with the mockup frames (win-live-stopped, win-live-ready, win-live-thinking,
/// win-live-failed, win-live-prereq, win-min).
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class LivePageTests
{
    private const string Transcript =
        "Thanks for making time. I wanted to go through the launch plan before Thursday.\n"
        + "The main risk is the onboarding flow. Design says they need one more week.\n"
        + "If we hold the March date, what would we have to cut?";

    private const string Answer =
        "Cut the analytics dashboard and the referral banner. Both can ship a sprint later without blocking onboarding, and design gets the extra week it asked for.";

    [Fact]
    public void Stopped_shows_start_and_the_two_placeholders() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();

        Assert.Single(WpfApp.Descendants(app.Page).OfType<LivePage>());
        Assert.Equal(
            [
                "Live", "Start relay",
                "Relay", "Stopped", "iPhone", "iPhone not connected", "PC address", "Shown when the relay is running",
                "Incoming transcript", "Live from your iPhone", "From iPhone", "Your next question will appear here.",
                "Assistant reply", "Powered by Codex on your PC", "Assistant", "Ready when you are.\nTap Assist on your iPhone.",
            ],
            app.Texts());

        // Start is the only control of the header, and the only accent control on the page.
        var start = app.Shown<Button>(AutomationIds.StartRelayButton);
        Assert.True(start.IsEnabled);
        Assert.Equal("Start relay", start.Content);
        Assert.Equal(48d, start.ActualHeight);
        Assert.False(ButtonAssist.GetIsBusy(start));
        Assert.Null(app.Find(AutomationIds.PauseRelayButton));
        Assert.Null(app.Find(AutomationIds.StopRelayButton));
        Assert.Null(app.Find(AutomationIds.PrerequisiteList));
        Assert.Null(app.Find(AutomationIds.ConnectionBanner));
        Assert.Null(app.Find(AutomationIds.ExposureBanner));
        Assert.Null(app.Find(AutomationIds.ReplyTiming));

        // The placeholders are text blocks in the quieter ink; no address means no copy button.
        Assert.IsType<TextBlock>(app.Find(AutomationIds.TranscriptText));
        Assert.IsType<TextBlock>(app.Find(AutomationIds.ReplyText));
        Assert.Same(app.Window.FindResource("InkSecondaryBrush"), app.Shown<TextBlock>(AutomationIds.PcAddressText).Foreground);
        Assert.DoesNotContain(Buttons(app), button => AutomationProperties.GetName(button) == "Copy PC address");

        app.Capture("w09-live-stopped", (_, height) => RequireTwoPanels(app, height));

        // Pressing Start goes to the session; while it checks, the same control is busy.
        start.Command.Execute(null);
        Assert.Equal(1, app.Session.StartCalls);
        app.Session.State = RelayUiState.Checking;
        WpfApp.Settle();
        Assert.Equal("Checking…", start.Content);
        Assert.True(ButtonAssist.GetIsBusy(start));
        Assert.Equal("Checking Codex and Tailscale…", app.Shown<StatusIndicatorView>(AutomationIds.RelayStatusText).Text);
        app.Capture("w09-live-checking");
    });

    [Fact]
    public void A_failed_check_shows_the_list_in_place_of_the_two_panels() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        app.Session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>
        {
            [PrerequisiteId.CodexSignedIn] = PrerequisiteState.Failed,
        });
        app.Session.StateText = PrerequisiteTexts.Failure(PrerequisiteId.CodexSignedIn);
        app.Session.State = RelayUiState.PrerequisiteFailed;
        WpfApp.Settle();

        Assert.Equal(
            [
                "Live", "Start relay",
                "Relay", "Codex is installed but not signed in. Open Codex and sign in with ChatGPT.",
                "iPhone", "iPhone not connected", "PC address", "Shown when the relay is running",
                "Before the relay can start", "Check again", "Open Settings",
                "LiveCue's data folder is ready",
                "Codex CLI found", @"C:\Tools\Codex\codex.exe",
                "Codex is installed but not signed in", "Open Codex and sign in with ChatGPT, then choose Check again.",
                "Tailscale is installed",
                "Tailscale is online",
                "This PC is pc.example.test",
            ],
            app.Texts());

        // Six check rows in the fixed order, each with the mark of its state; the first has no line above it.
        var list = app.Shown<ScrollViewer>(AutomationIds.PrerequisiteList);
        var rows = WpfApp.Descendants(list).OfType<CheckRow>().ToList();
        Assert.Equal(
            [
                PrerequisiteState.Passed, PrerequisiteState.Passed, PrerequisiteState.Failed,
                PrerequisiteState.Passed, PrerequisiteState.Passed, PrerequisiteState.Passed,
            ],
            rows.Select(row => row.State));
        Assert.True(rows[0].IsFirstInList);
        Assert.All(rows.Skip(1), row => Assert.False(row.IsFirstInList));
        Assert.All(rows, row => Assert.True(row.ActualHeight >= 48));

        // The two buttons of the panel go to the session and to Settings.
        var buttons = Buttons(app);
        buttons.Single(button => Equals(button.Content, "Check again")).Command.Execute(null);
        Assert.Equal(1, app.Session.CheckAgainCalls);

        app.Capture("w09-live-prereq", (_, _) =>
        {
            // The panel fills what the two panels would, and its rows fit or scroll inside it.
            var panel = Ancestor<ContentControl>(list, control => control.Style == (Style)app.Window.FindResource("Glass.Panel"));
            Assert.True(app.Bounds(panel).Bottom <= app.Bounds(app.Page).Bottom + 0.5);
            Assert.True(app.Bounds(list).Bottom <= app.Bounds(panel).Bottom);
        });

        // A check that was not made keeps its pass text and says so.
        app.Session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>
        {
            [PrerequisiteId.TailscaleInstalled] = PrerequisiteState.Failed,
            [PrerequisiteId.TailscaleOnline] = PrerequisiteState.NotChecked,
            [PrerequisiteId.TailscaleDnsName] = PrerequisiteState.NotChecked,
        }, codexVersion: "1.3.0");
        app.Session.StateText = PrerequisiteTexts.Failure(PrerequisiteId.TailscaleInstalled);
        WpfApp.Settle();
        var texts = app.Texts();
        Assert.Contains("Tailscale is not installed", texts);
        Assert.Contains("Install Tailscale, sign in, then choose Check again.", texts);
        Assert.Equal(2, texts.Count(text => text == "Not checked"));
        Assert.Contains(PrerequisiteTexts.ContainmentWarning("1.3.0", "1.2.3"), texts);
        app.Capture("w09-live-prereq-not-checked");

        buttons.Single(button => Equals(button.Content, "Open Settings")).Command.Execute(null);
        Assert.Equal(AppPage.Settings, app.Shell.SelectedPage);
    });

    [Fact]
    public void A_request_and_its_reply_are_mirrored_and_can_be_selected() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        app.Run();
        WpfApp.Settle();

        // Running: no accent control. Pause and Stop are secondary buttons, 40 high.
        Assert.Null(app.Find(AutomationIds.StartRelayButton));
        var pause = app.Shown<Button>(AutomationIds.PauseRelayButton);
        var stop = app.Shown<Button>(AutomationIds.StopRelayButton);
        Assert.Equal("Pause relay", pause.Content);
        Assert.Equal("Stop relay", stop.Content);
        Assert.Equal((40d, 40d), (pause.ActualHeight, stop.ActualHeight));
        Assert.Same(app.Window.FindResource("Icon.Pause"), ButtonAssist.GetIcon(pause));
        Assert.Same(app.Window.FindResource("StatusDangerBrush"), ButtonAssist.GetIconBrush(stop));
        Assert.Equal(PageParts.Endpoint, app.Shown<TextBlock>(AutomationIds.PcAddressText).Text);
        Assert.Same(app.Window.FindResource("InkPrimaryBrush"), app.Shown<TextBlock>(AutomationIds.PcAddressText).Foreground);

        // The copy button beside the address.
        var copy = Buttons(app).Single(button => AutomationProperties.GetName(button) == "Copy PC address");
        copy.Command.Execute(null);
        Assert.Equal(PageParts.Endpoint, app.Clipboard.Last);
        Assert.Equal("Copied", app.Shell.ToastText);
        app.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(app.Shell.ToastText);

        // Thinking (win-live-thinking).
        app.Session.State = RelayUiState.Thinking;
        app.Session.RaiseRelayEvent(Request(app));
        WpfApp.Settle();
        Assert.Equal(
            [
                "Live", "Pause relay", "Stop relay",
                "Relay", "Thinking…", "iPhone", "iPhone connected", "PC address", PageParts.Endpoint,
                "Incoming transcript", "Received from your iPhone", "From iPhone",
                Transcript,
                "[In progress] and could the analytics dashboard move to the sprint after" + Environment.NewLine + Environment.NewLine + "Instruction: answer briefly",
                "Assistant reply", "Processing with Codex on your PC", "Assistant", "Thinking…",
            ],
            app.Texts());
        Assert.Single(WpfApp.Descendants(app.Page).OfType<Spinner>(), spinner => spinner.IsVisible && spinner.ActualWidth == 16);
        app.Capture("w09-live-thinking", (_, height) => RequireTwoPanels(app, height));

        // The transcript is text the owner can select and copy, and cannot change.
        var transcript = app.Shown<TextBox>(AutomationIds.TranscriptText);
        Assert.True(transcript.IsReadOnly);
        Assert.False(transcript.IsTabStop);
        Assert.Equal(TextWrapping.Wrap, transcript.TextWrapping);
        transcript.SelectAll();
        Assert.Equal(Transcript, transcript.SelectedText);
        transcript.Select(0, 0);
        Assert.Equal(15d, transcript.FontSize);
        Assert.Equal(new Thickness(0), transcript.BorderThickness);

        // Reply sent (win-live-ready).
        app.Session.State = RelayUiState.Ready;
        app.Session.RaiseRelayEvent(new RequestCompleted(
            app.Now, PageParts.RequestId, AnswerKind.Assist, 912, 850, "gpt-5.6-sol", "low",
            "{\"answer\":\"" + Answer + "\",\"execution\":{\"model\":\"gpt-5.6-sol\",\"reasoningEffort\":\"low\"}}"));
        WpfApp.Settle();
        Assert.Equal(
            [
                "Assistant reply", "Sent to iPhone · gpt-5.6-sol · low · 0.85 s Codex",
                "PC relay total: 0.912 s. Codex includes startup and cloud processing.",
                "Assistant", Answer,
            ],
            app.Texts().SkipWhile(text => text != "Assistant reply"));
        Assert.Equal("Ready", app.Shown<StatusIndicatorView>(AutomationIds.RelayStatusText).Text);
        var reply = app.Shown<TextBox>(AutomationIds.ReplyText);
        Assert.True(reply.IsReadOnly);
        Assert.Equal(18d, reply.FontSize);
        reply.SelectAll();
        Assert.Equal(Answer, reply.SelectedText);
        reply.Select(0, 0);
        Assert.Equal(
            "PC relay total: 0.912 s. Codex includes startup and cloud processing.",
            app.Shown<TextBlock>(AutomationIds.ReplyTiming).Text);
        app.Capture("w09-live-ready", (_, height) => RequireTwoPanels(app, height));

        // Request failed (win-live-failed): the danger banner stands where the reply was.
        app.Session.State = RelayUiState.Thinking;
        app.Session.RaiseRelayEvent(Request(app));
        app.Session.State = RelayUiState.RequestFailed;
        app.Session.RaiseRelayEvent(new RequestFailed(app.Now, PageParts.RequestId, AnswerKind.Assist, RelayCodes.AssistantFailed, "relay text"));
        WpfApp.Settle();
        Assert.Equal(
            [
                "Assistant reply", "Not sent. Try Assist again.", "Assistant",
                "Nothing was sent to the iPhone", "Assistant request failed or timed out. Check Codex sign-in and retry.",
            ],
            app.Texts().SkipWhile(text => text != "Assistant reply"));
        Assert.Equal("Request failed. Ready to retry.", app.Shown<StatusIndicatorView>(AutomationIds.RelayStatusText).Text);
        var failure = WpfApp.Descendants(app.Page).OfType<Banner>().Single(banner => banner.IsVisible);
        Assert.Equal(BannerTone.Danger, failure.Tone);
        Assert.Null(app.Find(AutomationIds.ReplyTiming));
        Assert.Null(app.Find(AutomationIds.ReplyText));
        app.Capture("w09-live-failed", (_, height) => RequireTwoPanels(app, height));

        // Stop relay: the stopped layout with the hints as at the start; the texts and the address stay.
        stop.Command.Execute(null);
        Assert.Equal(1, app.Session.StopCalls);
        app.Session.LastPhoneSeen = null;
        app.Session.Presence = PhonePresence.NotConnected;
        app.Session.State = RelayUiState.Stopped;
        app.Session.RaiseSignal(SessionSignal.RelayEnded);
        WpfApp.Settle();
        var stopped = app.Texts();
        Assert.Contains("Start relay", stopped);
        Assert.Contains("Stopped", stopped);
        Assert.Contains("iPhone not connected", stopped);
        Assert.Contains(PageParts.Endpoint, stopped);
        Assert.Contains("Live from your iPhone", stopped);
        Assert.Contains(Transcript, stopped);
        Assert.Contains("Powered by Codex on your PC", stopped);
        Assert.Contains("Nothing was sent to the iPhone", stopped);
    });

    [Fact]
    public void Paused_and_the_two_connection_warnings() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        app.Run();
        app.Session.IsPaused = true;
        app.Session.State = RelayUiState.Paused;
        app.Session.Connection = PageParts.Inspection(MappingState.Absent);
        WpfApp.Settle();

        // Paused: the play glyph, "Resume relay", and the caption under the status.
        var pause = app.Shown<Button>(AutomationIds.PauseRelayButton);
        Assert.Equal("Resume relay", pause.Content);
        Assert.Same(app.Window.FindResource("Icon.Play"), ButtonAssist.GetIcon(pause));
        var texts = app.Texts();
        Assert.Contains("Paused", texts);
        Assert.Contains("New requests from the iPhone are refused until you resume.", texts);
        pause.Command.Execute(null);
        Assert.Equal(1, app.Session.TogglePauseCalls);

        // Connection not set up: a warning banner in the status strip, whose button opens the dialog.
        var banner = app.Shown<Banner>(AutomationIds.ConnectionBanner);
        Assert.Equal(BannerTone.Warning, banner.Tone);
        Assert.Equal("Connection not set up. Choose Set up connection to let your iPhone reach this PC.", banner.Body);
        var setUp = app.Shown<ButtonBase>(AutomationIds.LiveSetUpConnectionButton);
        Assert.Equal("Set up connection…", ((Button)setUp).Content);
        Assert.True(setUp.IsEnabled);
        Assert.Same(banner, Ancestor<Banner>(setUp, _ => true));
        app.Capture("w09-live-paused-connection", (_, height) => RequireTwoPanels(app, height));

        setUp.Command.Execute(null);
        WpfApp.Settle();
        var dialog = Assert.IsAssignableFrom<IMappingDialogViewModel>(app.Shell.ActiveDialog);
        Assert.Equal(MappingCommand.ApplyPrivate(11443, 47841).DisplayText, dialog.CommandText);
        Assert.Empty(app.Session.ExecutedPlans);
        dialog.CancelCommand.Execute(null);
        WpfApp.Settle();
        Assert.Null(app.Shell.ActiveDialog);

        // Blocked: the reason follows and the button is disabled. An exposure warning stands under it.
        app.Session.Connection = PageParts.Inspection(
            MappingState.Blocked,
            MappingRefusal.PrivatePortHasFunnel,
            exposure: new ExposedHandler(11443, "/", FunnelOn: true, Foreground: false, LedgerEntry: null));
        WpfApp.Settle();
        Assert.EndsWith("Choose another private HTTPS port.", banner.Body, StringComparison.Ordinal);
        Assert.False(app.Shown<ButtonBase>(AutomationIds.LiveSetUpConnectionButton).IsEnabled);
        var exposure = app.Shown<Banner>(AutomationIds.ExposureBanner);
        Assert.Equal(BannerTone.Danger, exposure.Tone);
        Assert.Equal("This relay is reachable from the internet", exposure.Title);
        Assert.Equal("A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode.", exposure.Body);
        Assert.Equal("Open Settings", exposure.ActionLabel);
        Assert.True(app.Bounds(exposure).Top >= app.Bounds(banner).Bottom + 12);

        // With so much in the strip the two cards still fit at the default size. At the minimum size
        // they would fall under their floor of 120, so the page scrolls instead of squeezing them.
        var page = WpfApp.Descendants(app.Page).OfType<ScrollViewer>().First();
        var cardStyle = (Style)app.Window.FindResource("Glass.Card");
        app.Capture("w09-live-blocked-exposure", (width, height) =>
        {
            var cards = WpfApp.Descendants(app.Page).OfType<ContentControl>()
                .Where(control => control.IsVisible && control.Style == cardStyle)
                .ToList();
            Assert.Equal(2, cards.Count);
            Assert.All(cards, card => Assert.True(card.ActualHeight >= 120));
            if (width > 1000)
            {
                Assert.Equal(0d, page.ScrollableHeight);
                RequireTwoPanels(app, height);
            }
            else
            {
                Assert.True(page.ScrollableHeight > 0);
                Assert.Equal(120d, cards.Min(card => card.ActualHeight), 1.0);
            }
        });

        // The exposure warning is shown while the relay is stopped as well; the connection banner is not.
        app.Session.IsPaused = false;
        app.Session.State = RelayUiState.Stopped;
        app.Session.Connection = PageParts.Inspection(
            MappingState.Present,
            exposure: new ExposedHandler(8443, "/other", FunnelOn: false, Foreground: false, LedgerEntry: null));
        WpfApp.Settle();
        Assert.Null(app.Find(AutomationIds.ConnectionBanner));
        exposure = app.Shown<Banner>(AutomationIds.ExposureBanner);
        Assert.Equal(BannerTone.Warning, exposure.Tone);
        Assert.Equal(
            "1 Tailscale mapping that LiveCue does not manage points at this relay. Open Settings to see it and how to remove it.",
            exposure.Body);
        app.Capture("w09-live-stopped-exposure");

        exposure.Command!.Execute(null);
        Assert.Equal(AppPage.Settings, app.Shell.SelectedPage);
    });

    [Fact]
    public void A_summary_has_its_two_lists_and_long_text_scrolls_inside_its_card() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        app.Run();
        app.Session.RaiseRelayEvent(Request(app, AnswerKind.Summary));
        app.Session.RaiseRelayEvent(new RequestCompleted(
            app.Now, PageParts.RequestId, AnswerKind.Summary, 4210, 3984.4, "gpt-5.6-sol", "medium",
            """{"summary":"The launch keeps its March date. The analytics dashboard and the referral banner move to the next sprint.","details":"Design gets the week it asked for; sales hears about the change on Thursday.","keyPoints":["Onboarding is the main risk","Design needs one more week","Two features move a sprint"],"actionItems":["Tell sales about the two features","Book the design review"]}"""));
        WpfApp.Settle();

        Assert.Equal(
            [
                "Assistant reply", "Sent to iPhone · gpt-5.6-sol · medium · 3.98 s Codex",
                "PC relay total: 4.210 s. Codex includes startup and cloud processing.",
                "Assistant",
                "The launch keeps its March date. The analytics dashboard and the referral banner move to the next sprint."
                    + Environment.NewLine + Environment.NewLine
                    + "Design gets the week it asked for; sales hears about the change on Thursday.",
                "Key points", "Onboarding is the main risk", "Design needs one more week", "Two features move a sprint",
                "Action items", "Tell sales about the two features", "Book the design review",
            ],
            app.Texts().SkipWhile(text => text != "Assistant reply"));
        Assert.Contains("Session summary requested", app.Texts());

        // Live speech text: each line is appended, and the newest is scrolled into view.
        for (var line = 1; line <= 60; line++)
        {
            app.Session.RaiseRelayEvent(new SpeechText(app.Now, $"Line {line} of what was said, long enough to wrap in a narrow card at the minimum size."));
        }

        WpfApp.Settle();
        var box = app.Shown<TextBox>(AutomationIds.TranscriptText);
        var scroller = Ancestor<ScrollViewer>(box, _ => true);
        Assert.EndsWith("Line 60 of what was said, long enough to wrap in a narrow card at the minimum size.", box.Text, StringComparison.Ordinal);

        app.Capture("w09-live-summary", (_, height) =>
        {
            RequireTwoPanels(app, height);
            Assert.True(scroller.ScrollableHeight > 0);
            Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
        });
        Assert.Equal(scroller.ScrollableHeight, scroller.VerticalOffset, 1.0);

        // The wheel over the text moves the card: the text box inside does not keep it for itself.
        scroller.ScrollToHome();
        WpfApp.Settle();
        box.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
        WpfApp.Settle();
        Assert.True(scroller.VerticalOffset > 0);

        // One more line brings the end back into view.
        app.Session.RaiseRelayEvent(new SpeechText(app.Now, "The newest line."));
        WpfApp.Settle();
        Assert.Equal(scroller.ScrollableHeight, scroller.VerticalOffset, 1.0);
    });

    [Fact]
    public void A_fault_text_wraps_in_the_strip_and_keeps_the_stopped_layout() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        var text = PrerequisiteTexts.StartFault(RelayFaultCode.PortInUse, 47841);
        app.Session.AdvertisedEndpoint = PageParts.Endpoint;
        app.Session.FaultCode = RelayFaultCode.PortInUse;
        app.Session.StateText = text;
        app.Session.State = RelayUiState.Faulted;
        WpfApp.Settle();

        var status = app.Shown<StatusIndicatorView>(AutomationIds.RelayStatusText);
        Assert.Equal(text, status.Text);
        Assert.Equal(text, status.ToolTip);
        Assert.Equal(StatusMark.Alert, status.Mark);
        Assert.NotNull(app.Find(AutomationIds.StartRelayButton));

        app.Capture("w09-live-fault", (width, height) =>
        {
            // The sentence wraps instead of being cut, and the strip stays inside the page.
            Assert.True(status.ActualHeight >= 40);
            Assert.True(status.ActualWidth <= 440);
            Assert.True(app.Bounds(status).Right <= width - 16);
            RequireTwoPanels(app, height);
        });
    });

    private static RequestStarted Request(PageWindow app, AnswerKind kind = AnswerKind.Assist) => new(
        app.Now,
        PageParts.RequestId,
        kind,
        "gpt-5.6-sol",
        "low",
        Transcript,
        "and could the analytics dashboard move to the sprint after",
        "answer briefly",
        """{"transcript":"If we hold the March date, what would we have to cut?"}""");

    private static List<Button> Buttons(PageWindow app) =>
        WpfApp.Descendants(app.Page).OfType<Button>().Where(button => button.IsVisible).ToList();

    private static T Ancestor<T>(DependencyObject element, Func<T, bool> matches)
        where T : DependencyObject
    {
        for (var current = System.Windows.Media.VisualTreeHelper.GetParent(element);
             current is not null;
             current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is T found && matches(found))
            {
                return found;
            }
        }

        throw new InvalidOperationException($"No {typeof(T).Name} above the element.");
    }

    /// <summary>
    /// Screens 2.1 and section 7: the two panels stand side by side at 0.88 : 1.12, 16 apart, down to the
    /// bottom of the page; each card takes the height that is left and never less than 120.
    /// </summary>
    private static void RequireTwoPanels(PageWindow app, double height)
    {
        var panelStyle = (Style)app.Window.FindResource("Glass.Panel");
        var cardStyle = (Style)app.Window.FindResource("Glass.Card");
        var page = app.Bounds(app.Page);
        var panels = WpfApp.Descendants(app.Page).OfType<ContentControl>()
            .Where(control => control.IsVisible && control.Style == panelStyle)
            .Select(app.Bounds)
            .ToList();
        var cards = WpfApp.Descendants(app.Page).OfType<ContentControl>()
            .Where(control => control.IsVisible && control.Style == cardStyle)
            .Select(app.Bounds)
            .ToList();

        Assert.Equal(2, panels.Count);
        Assert.Equal(2, cards.Count);
        Assert.Equal(16d, panels[1].Left - panels[0].Right, 0.5);
        Assert.Equal(0.88 / 1.12, panels[0].Width / panels[1].Width, 0.01);
        Assert.Equal(page.Left, panels[0].Left, 0.5);
        Assert.Equal(page.Right, panels[1].Right, 0.5);
        Assert.All(panels, panel => Assert.Equal(height - 16, panel.Bottom, 0.5));
        Assert.All(cards, card => Assert.True(card.Height >= 120, $"A card is {card.Height} high."));
        Assert.Equal(panels[0].Bottom - 20, cards[0].Bottom, 0.5);
    }
}
