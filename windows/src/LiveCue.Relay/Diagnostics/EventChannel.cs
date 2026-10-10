using System.Threading.Channels;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Diagnostics;

/// <summary>
/// The one channel every relay event goes through (architecture 3.4, delivery rules): bounded at W-01
/// events, the oldest dropped when full and the drops counted, so publishing never blocks a request and
/// never throws. Order is preserved. It is also the diagnostic sink of architecture 3.10.
///
/// First version (foundation): drops are counted; reporting them once per interval as a Diagnostic is
/// work package WP-W01.
/// </summary>
internal sealed class EventChannel : IRelayEventSink
{
    private readonly TimeProvider _clock;
    private readonly Channel<RelayEvent> _channel;
    private long _dropped;

    public EventChannel(TimeProvider clock)
    {
        _clock = clock;
        _channel = Channel.CreateBounded<RelayEvent>(
            new BoundedChannelOptions(ProtocolConstants.EventChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            _ => Interlocked.Increment(ref _dropped));
    }

    public ChannelReader<RelayEvent> Reader => _channel.Reader;

    /// <summary>How many events were dropped because nobody read them in time.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public DateTimeOffset Now => _clock.GetUtcNow();

    public void Publish(RelayEvent relayEvent)
    {
        if (relayEvent is not null)
        {
            _channel.Writer.TryWrite(relayEvent);
        }
    }

    public void Diagnostic(string source, string fixedText, Exception? exception = null) =>
        Publish(new Diagnostic(
            Now,
            source,
            exception is null ? fixedText : $"{fixedText} ({exception.GetType().Name})"));

    /// <summary>No event is accepted afterwards; the reader completes once it has been drained.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
