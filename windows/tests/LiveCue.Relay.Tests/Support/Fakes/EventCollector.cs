using System.Threading.Channels;

namespace LiveCue.Relay.Tests.Support;

/// <summary>
/// Drains <see cref="IRelay.Events"/> into a list. It is the channel's single reader: a test that uses
/// a collector never reads the channel itself.
/// </summary>
internal sealed class EventCollector
{
    private readonly Lock _gate = new();
    private readonly List<RelayEvent> _events = [];
    private readonly ChannelReader<RelayEvent> _reader;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public EventCollector(ChannelReader<RelayEvent> reader)
    {
        _reader = reader;
        Completion = Task.Run(PumpAsync);
    }

    /// <summary>Completes when the relay has completed its channel and everything was collected.</summary>
    public Task Completion { get; }

    /// <summary>Every event collected so far, in order.</summary>
    public IReadOnlyList<RelayEvent> Snapshot()
    {
        lock (_gate)
        {
            return [.. _events];
        }
    }

    public IReadOnlyList<T> OfType<T>()
        where T : RelayEvent => [.. Snapshot().OfType<T>()];

    public int Count<T>()
        where T : RelayEvent => Snapshot().OfType<T>().Count();

    /// <summary>Forgets what was collected so far.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }

    /// <summary>The first collected event of that type that matches, waiting up to the timeout for it.</summary>
    public async Task<T> WaitForAsync<T>(Func<T, bool>? match = null, TimeSpan? timeout = null)
        where T : RelayEvent
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                var found = _events.OfType<T>().FirstOrDefault(candidate => match is null || match(candidate));
                if (found is not null)
                {
                    return found;
                }

                changed = _changed.Task;
            }

            var left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || Completion.IsCompleted)
            {
                throw new TimeoutException($"No {typeof(T).Name} event arrived in time.");
            }

            await Task.WhenAny(changed, Completion, Task.Delay(left));
        }
    }

    /// <summary>Waits until the events published so far have been collected (the channel is empty).</summary>
    public async Task SettleAsync()
    {
        for (var attempt = 0; attempt < 200 && _reader.CanCount && _reader.Count > 0; attempt++)
        {
            await Task.Delay(5);
        }

        await Task.Yield();
    }

    private async Task PumpAsync()
    {
        while (await _reader.WaitToReadAsync())
        {
            while (_reader.TryRead(out var relayEvent))
            {
                TaskCompletionSource changed;
                lock (_gate)
                {
                    _events.Add(relayEvent);
                    changed = _changed;
                    _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                changed.TrySetResult();
            }
        }
    }
}
