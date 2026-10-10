using System.Globalization;
using System.Threading.Channels;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Diagnostics;

/// <summary>
/// The one channel every relay event goes through (architecture 3.4, delivery rules): bounded at W-01
/// events, the oldest dropped when full and the drops counted, so publishing never blocks a request and
/// never throws. Order is preserved. It is also the diagnostic sink of architecture 3.10.
///
/// Drops are reported at most once per interval of W-01, as one <see cref="Relay.Diagnostic"/> that holds
/// a count and nothing else. The report is itself an event; the one it may push out is not reported again.
/// </summary>
internal sealed class EventChannel : IRelayEventSink
{
    /// <summary>The <see cref="Relay.Diagnostic.Source"/> of the drop report.</summary>
    public const string Source = "events";

    private readonly TimeProvider _clock;
    private readonly Channel<RelayEvent> _channel;
    private long _dropped;
    private long _reported;

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

    /// <summary>
    /// Called once per interval of W-01 by the relay's timer: publishes one Diagnostic when events were
    /// dropped since the last report, and nothing otherwise. Returns whether it published.
    /// </summary>
    public bool ReportDrops()
    {
        var dropped = Dropped;
        var reported = Interlocked.Read(ref _reported);
        if (dropped == reported)
        {
            return false;
        }

        Diagnostic(Source, DropText(dropped - reported));

        // Read again: when the channel is still full the report pushed one more event out, and that one
        // must not cause the next report.
        Interlocked.Exchange(ref _reported, Dropped);
        return true;
    }

    /// <summary>No event is accepted afterwards; the reader completes once it has been drained.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>The fixed sentence of the drop report. It carries a count and nothing of any event.</summary>
    internal static string DropText(long count) => string.Create(
        CultureInfo.InvariantCulture,
        $"{count} relay events were dropped because the host did not read them in time.");
}
