using LiveCue.Relay.Speech;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-113, the formula of C-72: cumulative bytes since <c>ready</c> may not pass
/// <c>160000 + 35 × (milliseconds since ready)</c>. On the manual clock.
/// </summary>
public sealed class AudioLimiterTests
{
    [Fact]
    public void The_allowance_formula_holds()
    {
        Assert.Equal(160_000, AudioLimiter.AllowedBytes(TimeSpan.Zero));
        Assert.Equal(160_000, AudioLimiter.AllowedBytes(TimeSpan.FromMilliseconds(-5)));
        Assert.Equal(160_000, AudioLimiter.AllowedBytes(TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond - 1)));
        Assert.Equal(160_035, AudioLimiter.AllowedBytes(TimeSpan.FromMilliseconds(1)));
        Assert.Equal(160_000 + (35 * 80), AudioLimiter.AllowedBytes(TimeSpan.FromMilliseconds(80)));
        Assert.Equal(160_000 + (35 * 60_000), AudioLimiter.AllowedBytes(TimeSpan.FromSeconds(60)));
        Assert.Equal(160_000 + (35L * 1_800_000), AudioLimiter.AllowedBytes(TimeSpan.FromMinutes(30)));

        var clock = new ManualTimeProvider();
        var limiter = new AudioLimiter(clock);
        limiter.Start();

        // At "ready": ten seconds of audio at once, and not one frame more.
        Assert.True(limiter.TryAccept(160_000));
        Assert.False(limiter.TryAccept(2));
        Assert.Equal(160_000, limiter.AcceptedBytes);

        // One millisecond earns 35 bytes; a refused frame was not counted.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(limiter.TryAccept(36));
        Assert.True(limiter.TryAccept(34));
        Assert.False(limiter.TryAccept(2));
        Assert.True(limiter.TryAccept(0));

        // t milliseconds after "ready" the total may be 160000 + 35 t and no more.
        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.True(limiter.TryAccept(160_000 + 35_000 - 160_034));
        Assert.Equal(195_000, limiter.AcceptedBytes);
        Assert.False(limiter.TryAccept(2));
    }

    [Fact]
    public void Real_time_audio_never_reaches_the_allowance()
    {
        var clock = new ManualTimeProvider();
        var limiter = new AudioLimiter(clock);
        limiter.Start();

        // 32,000 bytes per second in 80 ms frames, for the whole 30 minutes of C-54.
        for (var frame = 0; frame < 30 * 60 * 1000 / 80; frame++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(80));
            Assert.True(limiter.TryAccept(2560));
        }

        Assert.Equal(57_600_000, limiter.AcceptedBytes);
    }

    [Fact]
    public void The_count_starts_again_at_start()
    {
        var clock = new ManualTimeProvider();
        var limiter = new AudioLimiter(clock);
        clock.Advance(TimeSpan.FromSeconds(100));
        limiter.Start();

        // Time before "ready" earned nothing.
        Assert.True(limiter.TryAccept(160_000));
        Assert.False(limiter.TryAccept(2));
    }
}
