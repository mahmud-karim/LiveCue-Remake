using System.Threading.Channels;

namespace LiveCue.Relay.Diagnostics;

/// <summary>
/// The one channel every relay event goes through (architecture 3.4, delivery rules): bounded at
/// 4,096 events, the oldest dropped when full and the drops counted, so raising an event never blocks
/// a request. Order is preserved.
/// </summary>
internal sealed class EventChannel
{
    public const int Capacity = 4096;

    private readonly Channel<RelayEvent> _channel;
    private long _dropped;

    public EventChannel()
    {
        _channel = Channel.CreateBounded<RelayEvent>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,
                SingleWriter = false,
            },
            _ => Interlocked.Increment(ref _dropped));
    }

    public ChannelReader<RelayEvent> Reader => _channel.Reader;

    /// <summary>How many events were dropped because nobody read them in time.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public void Raise(RelayEvent relayEvent) => _channel.Writer.TryWrite(relayEvent);

    public void Complete() => _channel.Writer.TryComplete();
}
