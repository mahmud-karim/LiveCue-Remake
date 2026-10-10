namespace LiveCue.Relay.Core;

/// <summary>
/// The pause flag and the stopping token of one relay instance (W-design 4.2).
///
/// Every member can be read at any time, also after the instance has stopped: a late reader (a warm-up
/// read, a self-check that is still ending) gets a cancelled token, never an exception.
/// </summary>
internal sealed class AccessState : IAccessState, IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private CancellationTokenSource _whileAcceptingSource = new();
    private CancellationToken _whileAccepting;
    private volatile bool _accepting = true;
    private bool _ended;

    public AccessState()
    {
        // The tokens are kept as values: a token can still be read after its source is gone.
        Stopping = _stopping.Token;
        _whileAccepting = _whileAcceptingSource.Token;
    }

    public bool Accepting => _accepting;

    public CancellationToken WhileAccepting
    {
        get
        {
            lock (_gate)
            {
                return _whileAccepting;
            }
        }
    }

    public CancellationToken Stopping { get; }

    /// <summary>
    /// Pause (false) or resume (true). Returns false, and changes nothing, when that is already the state
    /// or the instance has ended. Pausing flips the flag and then cancels <see cref="WhileAccepting"/>;
    /// resuming issues a fresh token.
    /// </summary>
    public bool SetAccepting(bool accepting)
    {
        CancellationTokenSource? toCancel = null;
        lock (_gate)
        {
            if (_ended || _accepting == accepting)
            {
                return false;
            }

            _accepting = accepting;
            if (accepting)
            {
                // The source of the pause that just ended was cancelled then; it holds nothing to release.
                _whileAcceptingSource = new CancellationTokenSource();
                _whileAccepting = _whileAcceptingSource.Token;
            }
            else
            {
                toCancel = _whileAcceptingSource;
            }
        }

        // Outside the lock: the callbacks of open streams run here.
        toCancel?.Cancel();
        return true;
    }

    /// <summary>StopAsync began, or a fatal fault was raised. Never reset.</summary>
    public void BeginStopping()
    {
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }
        }

        _stopping.Cancel();
    }

    /// <summary>The instance has ended. Both tokens end cancelled and the instance is not accepting.</summary>
    public void Dispose()
    {
        CancellationTokenSource whileAccepting;
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            _accepting = false;
            whileAccepting = _whileAcceptingSource;
        }

        _stopping.Cancel();
        whileAccepting.Cancel();
        _stopping.Dispose();
        whileAccepting.Dispose();
    }
}
