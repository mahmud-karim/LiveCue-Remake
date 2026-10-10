using LiveCue.Relay.Protocol;
using LiveCue.Relay.Speech;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// The timers of a stream by themselves, on the manual clock: what each limit is counted from, and
/// which of them are still set in each phase.
/// </summary>
public sealed class StreamWatchdogsTests
{
    private static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(1);

    [Fact]
    public void The_load_limit_and_the_lifetime_count_from_the_upgrade_not_from_admission()
    {
        var clock = new ManualTimeProvider();
        var fired = new List<StreamSignal>();
        var upgrade = clock.GetTimestamp();

        // Twenty seconds pass between the upgrade and admission (they never do; it makes the origin visible).
        var before = TimeSpan.FromSeconds(20);
        clock.Advance(before);
        using var watchdogs = new StreamWatchdogs(clock, upgrade, fired.Add);
        watchdogs.StartLoading();
        Assert.Equal(before, watchdogs.SinceUpgrade);

        // C-51 after the upgrade, to the millisecond.
        clock.Advance(ProtocolConstants.ModelLoadLimit - before - Moment);
        Assert.DoesNotContain(StreamSignal.LoadLimitTimer, fired);
        clock.Advance(Moment);
        Assert.Single(fired, signal => signal == StreamSignal.LoadLimitTimer);

        // "loading" every C-50 since admission, for as long as nobody stops it.
        Assert.Equal(
            (int)((ProtocolConstants.ModelLoadLimit - before) / ProtocolConstants.LoadingFrameInterval),
            fired.Count(signal => signal == StreamSignal.LoadingTimer));

        // C-55 after the upgrade, to the millisecond, and once.
        watchdogs.StopLoading();
        fired.Clear();
        clock.Advance(ProtocolConstants.SocketLifetime - ProtocolConstants.ModelLoadLimit - Moment);
        Assert.Empty(fired);
        clock.Advance(Moment);
        Assert.Equal([StreamSignal.LifetimeTimer], fired);
        clock.Advance(ProtocolConstants.SocketLifetime);
        Assert.Equal([StreamSignal.LifetimeTimer], fired);
    }

    [Fact]
    public void Only_the_lifetime_is_still_set_while_a_stream_closes_and_nothing_after_it_ended()
    {
        var clock = new ManualTimeProvider();
        var fired = new List<StreamSignal>();
        var watchdogs = new StreamWatchdogs(clock, clock.GetTimestamp(), fired.Add);

        watchdogs.StartLoading();
        Assert.Equal(3, clock.ScheduledTimers);
        watchdogs.StopLoading();
        watchdogs.StartStreaming();
        Assert.Equal(3, clock.ScheduledTimers);
        watchdogs.StartFinalizing();
        Assert.Equal(3, clock.ScheduledTimers);

        // Closing: the last line stays, everything else goes.
        watchdogs.StopAllButLifetime();
        Assert.Equal(1, clock.ScheduledTimers);
        clock.Advance(ProtocolConstants.SocketLifetime);
        Assert.Equal([StreamSignal.LifetimeTimer], fired);

        // Closed: nothing is left, and nothing can be started again.
        watchdogs.Dispose();
        watchdogs.StartLoading();
        watchdogs.StartStreaming();
        watchdogs.StartFinalizing();
        watchdogs.RestartIdle();
        Assert.Equal(0, clock.ScheduledTimers);
        Assert.False(watchdogs.IdleLimitPassed);
    }

    [Fact]
    public void The_idle_limit_counts_from_the_last_audio_and_ends_with_end_stream()
    {
        var clock = new ManualTimeProvider();
        var fired = new List<StreamSignal>();
        using var watchdogs = new StreamWatchdogs(clock, clock.GetTimestamp(), fired.Add);
        watchdogs.StartStreaming();

        clock.Advance(ProtocolConstants.AudioIdleLimit - Moment);
        Assert.False(watchdogs.IdleLimitPassed);
        watchdogs.RestartIdle();
        clock.Advance(ProtocolConstants.AudioIdleLimit - Moment);
        Assert.Empty(fired);
        Assert.False(watchdogs.IdleLimitPassed);

        clock.Advance(Moment);
        Assert.Equal([StreamSignal.IdleTimer], fired);
        Assert.True(watchdogs.IdleLimitPassed);

        // A frame that arrives while the signal is on its way makes the signal stale.
        watchdogs.RestartIdle();
        Assert.False(watchdogs.IdleLimitPassed);

        // After "endStream" there is no idle limit; C-53 bounds the rest, and C-54 still counts.
        fired.Clear();
        watchdogs.StartFinalizing();
        clock.Advance(ProtocolConstants.FinalizeLimit - Moment);
        Assert.Empty(fired);
        clock.Advance(Moment);
        Assert.Equal([StreamSignal.FinalizeTimer], fired);
        clock.Advance(ProtocolConstants.StreamLimit);
        Assert.Equal([StreamSignal.FinalizeTimer, StreamSignal.StreamTimer], fired);
        Assert.False(watchdogs.IdleLimitPassed);
    }
}
