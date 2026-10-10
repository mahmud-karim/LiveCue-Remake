using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Speech;

/// <summary>
/// The timers of one stream (protocol 10.5), all created from the relay's clock: the <c>loading</c>
/// interval (C-50), the load limit (C-51) and the socket lifetime (C-55), both counted from the upgrade,
/// the audio idle limit (C-52), the finalization limit (C-53) and the stream length (C-54), counted from
/// <c>ready</c>.
///
/// A timer decides nothing. When it fires it posts its signal to the stream's mailbox, and the stream's
/// loop decides what that means in the state it is in. Every method is called from that loop only.
/// </summary>
internal sealed class StreamWatchdogs : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly long _upgradeTimestamp;
    private readonly Action<StreamSignal> _post;

    private ITimer? _loading;
    private ITimer? _loadLimit;
    private ITimer? _lifetime;
    private ITimer? _idle;
    private ITimer? _finalize;
    private ITimer? _stream;
    private long _idleSince;
    private bool _disposed;

    public StreamWatchdogs(TimeProvider clock, long upgradeTimestamp, Action<StreamSignal> post)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(post);
        _clock = clock;
        _upgradeTimestamp = upgradeTimestamp;
        _post = post;
    }

    /// <summary>Time since the upgrade, never negative.</summary>
    public TimeSpan SinceUpgrade
    {
        get
        {
            var elapsed = _clock.GetElapsedTime(_upgradeTimestamp);
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }
    }

    /// <summary>
    /// True when C-52 has really passed since the last audio frame. An idle signal that was already on
    /// its way when a frame restarted the timer finds this false.
    /// </summary>
    public bool IdleLimitPassed =>
        _idle is not null && _clock.GetElapsedTime(_idleSince) >= ProtocolConstants.AudioIdleLimit;

    /// <summary>Admission: the <c>loading</c> interval, the load limit and the lifetime begin.</summary>
    public void StartLoading()
    {
        _loading = Start(
            StreamSignal.LoadingTimer, ProtocolConstants.LoadingFrameInterval, ProtocolConstants.LoadingFrameInterval);
        _loadLimit = Start(StreamSignal.LoadLimitTimer, Remaining(ProtocolConstants.ModelLoadLimit));
        _lifetime = Start(StreamSignal.LifetimeTimer, Remaining(ProtocolConstants.SocketLifetime));
    }

    /// <summary>The model is ready, or the stream failed: no more <c>loading</c> frames and no load limit.</summary>
    public void StopLoading()
    {
        Stop(ref _loading);
        Stop(ref _loadLimit);
    }

    /// <summary><c>ready</c>: the idle limit and the stream length begin.</summary>
    public void StartStreaming()
    {
        _idleSince = _clock.GetTimestamp();
        _idle = Start(StreamSignal.IdleTimer, ProtocolConstants.AudioIdleLimit);
        _stream = Start(StreamSignal.StreamTimer, ProtocolConstants.StreamLimit);
    }

    /// <summary>An audio frame arrived.</summary>
    public void RestartIdle()
    {
        _idleSince = _clock.GetTimestamp();
        _idle?.Change(ProtocolConstants.AudioIdleLimit, Timeout.InfiniteTimeSpan);
    }

    /// <summary><c>endStream</c>: the idle limit no longer applies, and finalization is bounded instead.</summary>
    public void StartFinalizing()
    {
        Stop(ref _idle);
        _finalize = Start(StreamSignal.FinalizeTimer, ProtocolConstants.FinalizeLimit);
    }

    /// <summary>The stream is being closed: only the lifetime still counts.</summary>
    public void StopAllButLifetime()
    {
        Stop(ref _loading);
        Stop(ref _loadLimit);
        Stop(ref _idle);
        Stop(ref _finalize);
        Stop(ref _stream);
    }

    public void Dispose()
    {
        _disposed = true;
        StopAllButLifetime();
        Stop(ref _lifetime);
    }

    private static void Stop(ref ITimer? timer)
    {
        timer?.Dispose();
        timer = null;
    }

    /// <summary>What is left of a limit that is counted from the upgrade.</summary>
    private TimeSpan Remaining(TimeSpan limit)
    {
        var left = limit - SinceUpgrade;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    private ITimer? Start(StreamSignal signal, TimeSpan due) => Start(signal, due, Timeout.InfiniteTimeSpan);

    private ITimer? Start(StreamSignal signal, TimeSpan due, TimeSpan period) => _disposed
        ? null
        : _clock.CreateTimer(
            static state =>
            {
                var (post, fired) = ((Action<StreamSignal>, StreamSignal))state!;
                post(fired);
            },
            (_post, signal),
            due,
            period);
}
