using LiveCue.Desktop.Session;
using LiveCue.Relay;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.Session;

/// <summary>
/// The phone presence machine of architecture 3.12 (test plan 2.6): first by itself, then inside the
/// session, where a clock timer drives it while the relay runs.
/// </summary>
public sealed class PresenceTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Presence_cases()
    {
        // ---- The tracker alone -------------------------------------------------------------------
        var start = ManualTimeProvider.DefaultStart;
        var tracker = new PresenceTracker();
        Assert.Equal(Threshold, PresenceTracker.IdleAfter);
        Assert.Equal(PhonePresence.NotConnected, tracker.Presence);
        Assert.Null(tracker.LastSeen);

        // Time alone does nothing to a phone that was never seen.
        Assert.False(tracker.Tick(start.AddHours(1)));
        Assert.Equal(PhonePresence.NotConnected, tracker.Presence);

        // Connected by an authenticated request, and for as long as the last one is at most 15 s old.
        tracker.Seen(start);
        Assert.Equal(PhonePresence.Connected, tracker.Presence);
        Assert.Equal(start, tracker.LastSeen);
        Assert.False(tracker.Tick(start + Threshold));
        Assert.Equal(PhonePresence.Connected, tracker.Presence);

        // Idle after the threshold: told once, and never "not connected" however long it lasts.
        Assert.True(tracker.Tick(start + Threshold + TimeSpan.FromMilliseconds(1)));
        Assert.Equal(PhonePresence.Idle, tracker.Presence);
        Assert.False(tracker.Tick(start.AddDays(1)));
        Assert.Equal(PhonePresence.Idle, tracker.Presence);
        Assert.Equal(start, tracker.LastSeen);

        // The next request makes it connected again; only a reset forgets the phone.
        tracker.Seen(start.AddMinutes(5));
        Assert.Equal(PhonePresence.Connected, tracker.Presence);
        tracker.Reset();
        Assert.Equal(PhonePresence.NotConnected, tracker.Presence);
        Assert.Null(tracker.LastSeen);

        // ---- Inside the session ------------------------------------------------------------------
        using var h = new SessionHarness();
        var session = h.Session;

        // No relay, no tick: the timer runs only while the relay runs.
        Assert.Equal(0, h.Clock.ScheduledTimers);
        await h.StartAsync();
        Assert.Equal(1, h.Clock.ScheduledTimers);
        Assert.Equal(PhonePresence.NotConnected, session.Presence);
        await h.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(PhonePresence.NotConnected, session.Presence);

        await h.Forward(new PhoneSeen(h.Clock.GetUtcNow()));
        var seenAt = h.Clock.GetUtcNow();
        Assert.Equal(PhonePresence.Connected, session.Presence);
        Assert.Equal(seenAt, session.LastPhoneSeen);

        await h.Advance(Threshold);
        Assert.Equal(PhonePresence.Connected, session.Presence);

        h.Changes.Clear();
        await h.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PhonePresence.Idle, session.Presence);
        Assert.Equal([nameof(IDesktopSession.Presence)], h.Changes);

        await h.Advance(TimeSpan.FromHours(2));
        Assert.Equal(PhonePresence.Idle, session.Presence);
        Assert.Equal(seenAt, session.LastPhoneSeen);
        Assert.Equal([nameof(IDesktopSession.Presence)], h.Changes);

        await h.Forward(new PhoneSeen(h.Clock.GetUtcNow()));
        Assert.Equal(PhonePresence.Connected, session.Presence);
        Assert.Equal(h.Clock.GetUtcNow(), session.LastPhoneSeen);

        // Reset on stop, and the tick stops with the relay.
        await h.OnUi(s => s.StopAsync());
        Assert.Equal(PhonePresence.NotConnected, session.Presence);
        Assert.Null(session.LastPhoneSeen);
        Assert.Equal(0, h.Clock.ScheduledTimers);

        // A new start begins with no phone, whatever the last run saw.
        await h.StartAsync();
        Assert.Equal(PhonePresence.NotConnected, session.Presence);
        await h.Forward(new PhoneSeen(h.Clock.GetUtcNow()));
        Assert.Equal(PhonePresence.Connected, session.Presence);

        // A fault forgets the phone as a stop does.
        await h.Forward(new RelayFaulted(h.Clock.GetUtcNow(), RelayFaultCode.Internal, "The relay stopped unexpectedly."));
        Assert.Equal(PhonePresence.NotConnected, session.Presence);
        Assert.Equal(0, h.Clock.ScheduledTimers);

        // A heartbeat that arrives after the relay ended is not a phone.
        await h.Forward(new PhoneSeen(h.Clock.GetUtcNow()));
        Assert.Equal(PhonePresence.NotConnected, session.Presence);

        Assert.All(h.RaisedOn, thread => Assert.Equal(h.Ui.ThreadId, thread));
    }
}
