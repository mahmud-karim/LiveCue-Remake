namespace LiveCue.TestSupport;

/// <summary>
/// A <see cref="TimeProvider"/> whose time moves only when the test says so, with timers. A test that
/// waits for a protocol limit advances this clock; it never sleeps for the limit.
///
/// <see cref="Advance"/> runs every timer that comes due, in due order, on the calling thread and
/// outside the clock's lock; a periodic timer is re-armed before its callback runs. Timestamps
/// (<see cref="GetTimestamp"/>) move with the same clock, so elapsed-time measurements follow it too.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    /// <summary>The time a new clock starts at unless told otherwise.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly DateTimeOffset _start;
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset? start = null, TimeZoneInfo? localTimeZone = null)
    {
        _start = start ?? DefaultStart;
        _now = _start;
        LocalTimeZone = localTimeZone ?? TimeZoneInfo.Utc;
    }

    public override TimeZoneInfo LocalTimeZone { get; }

    /// <summary>One timestamp unit is one tick (100 ns).</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>Timers that are scheduled to fire.</summary>
    public int ScheduledTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(timer => timer.Due is not null);
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return (_now - _start).Ticks;
        }
    }

    /// <summary>Moves the clock forward by <paramref name="by"/> and fires what comes due on the way.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        DateTimeOffset target;
        lock (_gate)
        {
            target = _now + by;
        }

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(timer => timer.Due <= target).MinBy(timer => timer.Due);
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.Due!.Value;
                next.Due = next.Period > TimeSpan.Zero ? _now + next.Period : null;
            }

            next.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        /// <summary>When the timer fires next; null while it is not scheduled. Guarded by the clock's lock.</summary>
        public DateTimeOffset? Due { get; set; }

        /// <summary>Zero or negative for a one-shot timer. Guarded by the clock's lock.</summary>
        public TimeSpan Period { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                return true;
            }
        }

        public void Dispose()
        {
            lock (clock._gate)
            {
                _disposed = true;
                Due = null;
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
