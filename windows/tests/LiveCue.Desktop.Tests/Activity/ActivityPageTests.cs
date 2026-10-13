using System.Windows;
using System.Windows.Controls;
using LiveCue.Desktop.Activity;
using LiveCue.Desktop.Tests.Live;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests.Activity;

/// <summary>
/// The Activity page in the real window, off-screen, over a fake session (screens section 4; W-design
/// 5.7), at the default and at the minimum size. With the environment variable
/// <c>LIVECUE_TEST_SHOTS</c> set, each scene is also written as a PNG for a person to compare with the
/// mockup frames (win-activity, win-activity-empty).
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class ActivityPageTests
{
    private const string Footer =
        "Activity stays in memory on this PC. It is cleared when LiveCue closes and is never written to disk.";

    private const string NoneRejected =
        "Rejected since the relay started: 0 malformed · 0 with an unknown token · 0 through Funnel in Private mode";

    [Fact]
    public void Empty_shows_the_empty_state_and_a_disabled_clear() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        app.Show(AppPage.Activity);

        Assert.Single(WpfApp.Descendants(app.Page).OfType<ActivityPage>());
        Assert.Equal(
            ["Activity", "Clear activity", NoneRejected, Footer, "No activity yet", "Start the relay, then tap Assist on your iPhone."],
            app.Texts());

        var clear = app.Shown<Button>(AutomationIds.ClearActivityButton);
        Assert.False(clear.IsEnabled);
        Assert.Equal(40d, clear.ActualHeight);
        Assert.Null(app.Find(AutomationIds.ActivityLog));
        Assert.Equal(NoneRejected, app.Shown<TextBlock>(AutomationIds.ActivityRejectedCounts).Text);

        app.Capture("w09-activity-empty", (_, height) =>
        {
            // One panel fills the page; the empty state stands in its middle.
            var panel = Panel(app);
            Assert.Equal(app.Bounds(app.Page).Left, panel.Left, 0.5);
            Assert.Equal(app.Bounds(app.Page).Right, panel.Right, 0.5);
            Assert.Equal(height - 16, panel.Bottom, 0.5);
            var title = WpfApp.Descendants(app.Page).OfType<TextBlock>().Single(text => text.Text == "No activity yet");
            var middle = app.Bounds(title).Left + (app.Bounds(title).Width / 2);
            Assert.Equal(panel.Left + (panel.Width / 2), middle, 1.0);
        });
    });

    [Fact]
    public void Entries_show_their_time_their_message_and_their_payload() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        Fill(app);
        app.Show(AppPage.Activity);

        var indent = Environment.NewLine + "  ";
        Assert.Equal(
            [
                "Activity", "Clear activity", NoneRejected,
                "7 entries", Footer,
                "09:14:02", "Relay started. Waiting for your iPhone.",
                "09:14:02", "Saved pairing restored. Existing iPhones reconnect automatically; Show QR keeps their pairing valid.",
                "09:14:31", "Secure PC connection verified.",
                "09:16:48", "INCOMING assist [7f3a9c12]",
                "{" + indent + "\"transcript\": \"If we hold the March date, what would we have to cut?\","
                    + indent + "\"partialTranscript\": \"and could the analytics dashboard move to the sprint after\","
                    + indent + "\"instruction\": \"answer briefly\"" + Environment.NewLine + "}",
                "09:16:49", "REPLY [7f3a9c12] 0.91 s",
                "{" + indent + "\"answer\": \"Cut the analytics dashboard and the referral banner.\","
                    + indent + "\"execution\": {" + indent + "  \"model\": \"gpt-5.6-sol\"," + indent + "  \"reasoningEffort\": \"low\"" + indent + "}"
                    + Environment.NewLine + "}",
                "09:21:10", "Loading Nemotron on the PC GPU…",
                "09:21:34", "Nemotron ready · PC GPU · no speech API cost.",
            ],
            app.Texts());

        var clear = app.Shown<Button>(AutomationIds.ClearActivityButton);
        Assert.True(clear.IsEnabled);
        var log = app.Shown<ScrollViewer>(AutomationIds.ActivityLog);

        // Times and payloads are in the quieter ink, messages in the primary one; the messages line up.
        var secondary = app.Window.FindResource("InkSecondaryBrush");
        var entries = WpfApp.Descendants(log).OfType<TextBlock>().Where(text => text.IsVisible).ToList();
        Assert.All(entries.Where(text => text.Text.StartsWith("09:", StringComparison.Ordinal)), text => Assert.Same(secondary, text.Foreground));
        Assert.All(entries.Where(text => text.Text.StartsWith('{')), text => Assert.Same(secondary, text.Foreground));
        var messages = entries.Where(text => !text.Text.StartsWith("09:", StringComparison.Ordinal)).ToList();
        Assert.Single(messages.Select(text => Math.Round(app.Bounds(text).Left)).Distinct());
        Assert.All(entries, text => Assert.Equal("Cascadia Mono, Consolas", text.FontFamily.Source));

        app.Capture("w09-activity", (_, height) =>
        {
            // The card fills the panel between the caption and the footer.
            var panel = Panel(app);
            Assert.Equal(height - 16, panel.Bottom, 0.5);
            var card = app.Bounds(log);
            Assert.True(card.Left >= panel.Left + 20);
            Assert.True(card.Bottom <= panel.Bottom - 20 - 18 - 12 + 0.5);
            Assert.True(card.Height > 300);
        });

        // The rejected counters are numbers in the caption; they add no entry.
        app.Session.RaiseRelayEvent(new EdgeStatistics(app.Now, 12, 3, 1));
        WpfApp.Settle();
        Assert.Equal(
            "Rejected since the relay started: 12 malformed · 3 with an unknown token · 1 through Funnel in Private mode",
            app.Shown<TextBlock>(AutomationIds.ActivityRejectedCounts).Text);
        Assert.Same(secondary, app.Shown<TextBlock>(AutomationIds.ActivityRejectedCounts).Foreground);
        Assert.Contains("7 entries", app.Texts());

        // Clear activity: the empty state again, and Live shows its placeholders.
        clear.Command.Execute(null);
        WpfApp.Settle();
        Assert.Equal(1, app.Session.ClearActivityCalls);
        Assert.False(clear.IsEnabled);
        Assert.Contains("No activity yet", app.Texts());
        Assert.Null(app.Find(AutomationIds.ActivityLog));
        Assert.Contains("12 malformed", app.Shown<TextBlock>(AutomationIds.ActivityRejectedCounts).Text, StringComparison.Ordinal);
        Assert.True(app.Live.IsTranscriptPlaceholder);
        Assert.Equal("Activity cleared", app.Live.ReplyHint);
    });

    [Fact]
    public void A_long_log_follows_new_entries_only_while_it_is_scrolled_to_the_end() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        app.Run();
        app.Show(AppPage.Activity);

        for (var index = 0; index < 300; index++)
        {
            app.Session.RaiseRelayEvent(new RequestCompleted(
                app.Now, "id-" + index, AnswerKind.Assist, 912, 850, "gpt-5.6-sol", "low", """{"answer":"Cut the dashboard.","index":1}"""));
        }

        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        var log = app.Shown<ScrollViewer>(AutomationIds.ActivityLog);
        Assert.Contains("301 entries", app.Texts());

        // Only the entries in view are built, and the newest one is among them.
        Assert.True(log.ScrollableHeight > 0);
        Assert.Equal(log.ScrollableHeight, log.VerticalOffset, 2.0);
        var built = WpfApp.Descendants(log).OfType<TextBlock>().Count();
        Assert.InRange(built, 3, 400);
        Assert.Contains(WpfApp.Descendants(log).OfType<TextBlock>(), text => text.IsVisible && text.Text == "REPLY [id-299] 0.91 s");
        app.Capture("w09-activity-long");

        // Following: a new entry comes into view.
        app.Session.RaiseMessage(new SessionMessage(app.Now, SessionMessageKind.Info, "The newest line."));
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Equal(log.ScrollableHeight, log.VerticalOffset, 2.0);
        Assert.Contains(WpfApp.Descendants(log).OfType<TextBlock>(), text => text.IsVisible && text.Text == "The newest line.");

        // The owner scrolls up to read: new entries no longer move the text.
        log.ScrollToVerticalOffset(log.ScrollableHeight / 2);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        var reading = log.VerticalOffset;
        Assert.True(reading < log.ScrollableHeight - 100);
        app.Session.RaiseMessage(new SessionMessage(app.Now, SessionMessageKind.Info, "Not followed."));
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Equal(reading, log.VerticalOffset, 2.0);

        // Back at the end, the log is followed again.
        log.ScrollToEnd();
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        app.Session.RaiseMessage(new SessionMessage(app.Now, SessionMessageKind.Info, "Followed again."));
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Equal(log.ScrollableHeight, log.VerticalOffset, 2.0);
        Assert.Contains(WpfApp.Descendants(log).OfType<TextBlock>(), text => text.IsVisible && text.Text == "Followed again.");

        // The view is built again each time the page is shown: it opens at the newest entry.
        app.Show(AppPage.Live);
        app.Session.RaiseMessage(new SessionMessage(app.Now, SessionMessageKind.Info, "Written while Live was shown."));
        app.Show(AppPage.Activity);
        var reopened = app.Shown<ScrollViewer>(AutomationIds.ActivityLog);
        Assert.NotSame(log, reopened);
        Assert.True(reopened.ScrollableHeight > 0);
        Assert.Equal(reopened.ScrollableHeight, reopened.VerticalOffset, 2.0);
        Assert.Contains(
            WpfApp.Descendants(reopened).OfType<TextBlock>(),
            text => text.IsVisible && text.Text == "Written while Live was shown.");
    });

    /// <summary>The entries of the mockup frame win-activity, at its times.</summary>
    private static void Fill(PageWindow app)
    {
        var session = app.Session;
        app.Run();
        session.RaiseRelayEvent(new Notice(
            app.Now,
            NoticeKind.PairingRestored,
            "Saved pairing restored. Existing iPhones reconnect automatically; Show QR keeps their pairing valid."));
        app.Clock.Advance(TimeSpan.FromSeconds(29));
        session.RaiseRelayEvent(new SelfCheckCompleted(app.Now, SelfCheckResult.Verified));
        app.Clock.Advance(new TimeSpan(0, 2, 17));
        session.RaiseRelayEvent(new RequestStarted(
            app.Now,
            PageParts.RequestId,
            AnswerKind.Assist,
            "gpt-5.6-sol",
            "low",
            "If we hold the March date, what would we have to cut?",
            "and could the analytics dashboard move to the sprint after",
            "answer briefly",
            """{"transcript":"If we hold the March date, what would we have to cut?","partialTranscript":"and could the analytics dashboard move to the sprint after","instruction":"answer briefly"}"""));
        app.Clock.Advance(TimeSpan.FromSeconds(1));
        session.RaiseRelayEvent(new RequestCompleted(
            app.Now,
            PageParts.RequestId,
            AnswerKind.Assist,
            912,
            850,
            "gpt-5.6-sol",
            "low",
            """{"answer":"Cut the analytics dashboard and the referral banner.","execution":{"model":"gpt-5.6-sol","reasoningEffort":"low"}}"""));
        app.Clock.Advance(new TimeSpan(0, 4, 21));
        session.RaiseRelayEvent(new SpeechStreamChanged(app.Now, SpeechStreamPhase.Loading, "nemotron", null, "Loading Nemotron on the PC GPU…"));
        app.Clock.Advance(TimeSpan.FromSeconds(24));
        session.RaiseRelayEvent(new SpeechStreamChanged(app.Now, SpeechStreamPhase.Ready, "nemotron", null, "Nemotron ready · PC GPU · no speech API cost."));
    }

    /// <summary>The bounds of the page's one panel.</summary>
    private static Rect Panel(PageWindow app)
    {
        var style = (Style)app.Window.FindResource("Glass.Panel");
        return app.Bounds(WpfApp.Descendants(app.Page).OfType<ContentControl>().Single(control => control.IsVisible && control.Style == style));
    }
}
