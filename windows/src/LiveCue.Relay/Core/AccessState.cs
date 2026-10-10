namespace LiveCue.Relay.Core;

/// <summary>The pause flag and the stopping token of one relay instance (W-design 4.2).</summary>
internal sealed class AccessState : IAccessState, IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private CancellationTokenSource _whileAccepting = new();
    private volatile bool _accepting = true;

    public bool Accepting => _accepting;

    public CancellationToken WhileAccepting
    {
        get
        {
            lock (_gate)
            {
                return _whileAccepting.Token;
            }
        }
    }

    public CancellationToken Stopping => _stopping.Token;

    /// <summary>
    /// Pause (false) or resume (true). Returns false, and changes nothing, when that is already the state.
    /// Pausing cancels <see cref="WhileAccepting"/>; resuming issues a fresh token.
    /// </summary>
    public bool SetAccepting(bool accepting)
    {
        CancellationTokenSource? toCancel = null;
        lock (_gate)
        {
            if (_accepting == accepting)
            {
                return false;
            }

            _accepting = accepting;
            if (accepting)
            {
                _whileAccepting = new CancellationTokenSource();
            }
            else
            {
                toCancel = _whileAccepting;
            }
        }

        toCancel?.Cancel();
        return true;
    }

    /// <summary>StopAsync began, or a fatal fault was raised. Never reset.</summary>
    public void BeginStopping()
    {
        if (!_stopping.IsCancellationRequested)
        {
            _stopping.Cancel();
        }
    }

    /// <summary>Called when the instance has stopped. Both tokens end cancelled.</summary>
    public void Dispose()
    {
        CancellationTokenSource whileAccepting;
        lock (_gate)
        {
            _accepting = false;
            whileAccepting = _whileAccepting;
        }

        _stopping.Cancel();
        whileAccepting.Cancel();
        _stopping.Dispose();
        whileAccepting.Dispose();
    }
}
