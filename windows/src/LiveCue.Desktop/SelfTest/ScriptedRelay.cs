using System.Threading.Channels;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// The relay the self-test runs the window against (architecture 3.13): an <see cref="IRelay"/> that
/// binds no port, touches no file and uses no network, and raises the events it is told to.
/// </summary>
internal sealed class ScriptedRelay(RelayOptions options, TimeProvider clock, RelayFaultCode? startFault) : IRelay
{
    private readonly Channel<RelayEvent> _events = Channel.CreateBounded<RelayEvent>(256);

    public RelayOptions Options { get; } = options;

    public RelayStatus Status { get; private set; } = RelayStatus.Stopped;

    public int BoundPort => Status == RelayStatus.Running ? Options.Port : 0;

    public ChannelReader<RelayEvent> Events => _events.Reader;

    /// <summary>Raises a synthetic event, as the real relay would from a request thread.</summary>
    public void Raise(RelayEvent relayEvent) => _events.Writer.TryWrite(relayEvent);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (startFault is { } code)
        {
            const string message = "The relay port is already in use.";
            Status = RelayStatus.Faulted;
            Raise(new RelayFaulted(clock.GetUtcNow(), code, message));
            throw new RelayStartException(code, message);
        }

        Status = RelayStatus.Running;
        Raise(new RelayStarted(
            clock.GetUtcNow(),
            Options.AdvertisedEndpoint,
            Options.Port,
            Options.Mode,
            ProtocolConstants.ProtocolVersion,
            Options.RelayVersion,
            PairingWasCreated: false));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Status == RelayStatus.Running)
        {
            Status = RelayStatus.Stopped;
            Raise(new RelayStopped(clock.GetUtcNow(), RelayStopReason.Requested));
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Hands the supervisor one scripted relay per start, and lets the script choose how the next start goes.</summary>
internal sealed class RelayScript(TimeProvider clock)
{
    /// <summary>When set, the next start fails with this fault. It applies to one start.</summary>
    public RelayFaultCode? NextStartFault { get; set; }

    /// <summary>The relay of the current or most recent run.</summary>
    public ScriptedRelay? Current { get; private set; }

    /// <summary>How many relay instances were created: one per start.</summary>
    public int Created { get; private set; }

    public IRelay Create(RelayOptions options)
    {
        Current = new ScriptedRelay(options, clock, NextStartFault);
        NextStartFault = null;
        Created++;
        return Current;
    }

    /// <summary>A request with the pairing token arrived just now.</summary>
    public void PhoneSeen() => Current?.Raise(new PhoneSeen(clock.GetUtcNow()));

    public void Diagnostic(string message) => Current?.Raise(new Diagnostic(clock.GetUtcNow(), "self-test", message));
}

/// <summary>
/// A clock the script moves by hand, with timers that fire when it is moved past them. It makes the
/// times in the screenshots the same on every run and lets "15 seconds later" happen at once.
/// </summary>
internal sealed class ManualClock(DateTimeOffset start, TimeZoneInfo? zone = null) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    public override TimeZoneInfo LocalTimeZone { get; } = zone ?? TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <summary>Moves the clock forward and runs every timer that has come due, in due order.</summary>
    public void Advance(TimeSpan by)
    {
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
                next.Due = null;
            }

            next.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        /// <summary>When the timer fires next; null while it is not scheduled. Guarded by the clock's lock.</summary>
        public DateTimeOffset? Due { get; set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                // One-shot timers are all the app uses; a period is not modelled.
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
            }

            return true;
        }

        public void Dispose()
        {
            lock (clock._gate)
            {
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
