using LiveCue.Relay.Diagnostics;
using LiveCue.Relay.Protocol;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Host;

/// <summary>The bounded event channel and the diagnostic sink (W-design W-01, architecture 3.10).</summary>
public sealed class EventChannelTests
{
    [Fact]
    public void A_full_channel_drops_the_oldest_and_counts()
    {
        var clock = new ManualTimeProvider();
        var channel = new EventChannel(clock);
        var start = clock.GetUtcNow();

        for (var index = 0; index <= ProtocolConstants.EventChannelCapacity; index++)
        {
            channel.Publish(new PhoneSeen(start.AddSeconds(index)));
        }

        Assert.Equal(1, channel.Dropped);
        Assert.True(channel.Reader.TryRead(out var oldestKept));
        Assert.Equal(new PhoneSeen(start.AddSeconds(1)), oldestKept);
    }

    [Fact]
    public void A_diagnostic_carries_the_fixed_text_and_the_exception_type_name_only()
    {
        var clock = new ManualTimeProvider();
        var channel = new EventChannel(clock);

        channel.Diagnostic("edge", "A request failed unexpectedly.", new InvalidOperationException("secret detail"));
        channel.Diagnostic("edge", "Something else.");

        Assert.True(channel.Reader.TryRead(out var first));
        Assert.True(channel.Reader.TryRead(out var second));
        Assert.Equal(
            new Diagnostic(clock.GetUtcNow(), "edge", "A request failed unexpectedly. (InvalidOperationException)"),
            first);
        Assert.Equal(new Diagnostic(clock.GetUtcNow(), "edge", "Something else."), second);
        Assert.Equal(clock.GetUtcNow(), channel.Now);
    }

    [Fact]
    public void Publishing_after_completion_does_nothing_and_does_not_throw()
    {
        var channel = new EventChannel(new ManualTimeProvider());

        channel.Complete();
        channel.Publish(new PhoneSeen(DateTimeOffset.UnixEpoch));

        Assert.False(channel.Reader.TryRead(out _));
        Assert.True(channel.Reader.Completion.IsCompleted);
    }
}
