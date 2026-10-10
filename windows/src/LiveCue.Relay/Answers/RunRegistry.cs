namespace LiveCue.Relay.Answers;

/// <summary>
/// The answer requests in flight, by request id, so that route 9 can cancel one (protocol 6.8). Ids are
/// compared ordinally: the text in a cancel path must be the text of the body, letter case included.
///
/// Cancelling is truthful in both directions. <see cref="TryCancel"/> returns true only while the
/// request can still end as cancelled, and a request that <see cref="Registration.Complete"/> reports
/// as cancelled always ends as REQUEST_CANCELLED, even when its run had just returned. The two calls
/// decide under one lock, so a request is never told "cancelled" and then answered, and never answered
/// "not found" while it is being cancelled.
/// </summary>
internal sealed class RunRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Registration> _runs = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a request under its id. <paramref name="run"/> is cancelled when route 9 names the id.
    /// The caller calls <see cref="Registration.Complete"/> when the request's outcome is decided.
    /// </summary>
    public Registration Register(string requestId, CancellationTokenSource run)
    {
        ArgumentNullException.ThrowIfNull(requestId);
        ArgumentNullException.ThrowIfNull(run);
        var registration = new Registration(this, requestId, run);
        lock (_gate)
        {
            // One request holds the answer slot at a time, so an id is never registered twice.
            _runs[requestId] = registration;
        }

        return registration;
    }

    /// <summary>Route 9. False when no request with exactly that id is in flight.</summary>
    public bool TryCancel(string requestId)
    {
        if (requestId is null)
        {
            return false;
        }

        Registration registration;
        lock (_gate)
        {
            if (!_runs.TryGetValue(requestId, out var found))
            {
                return false;
            }

            registration = found;
            registration.CancelRequested = true;
        }

        // Outside the lock: cancelling runs the callbacks of the token, the kill of the process among them.
        registration.CancelRun();
        return true;
    }

    /// <summary>One registered request.</summary>
    internal sealed class Registration : IDisposable
    {
        private readonly RunRegistry _owner;
        private readonly string _requestId;
        private readonly CancellationTokenSource _run;
        private bool _completed;

        public Registration(RunRegistry owner, string requestId, CancellationTokenSource run)
        {
            _owner = owner;
            _requestId = requestId;
            _run = run;
        }

        /// <summary>Set under the registry's lock.</summary>
        public bool CancelRequested { get; set; }

        /// <summary>
        /// Ends the registration: from now on route 9 no longer finds the id. True when route 9 cancelled
        /// the request before this call, however the run itself ended.
        /// </summary>
        public bool Complete()
        {
            lock (_owner._gate)
            {
                if (!_completed)
                {
                    _completed = true;
                    if (_owner._runs.TryGetValue(_requestId, out var current) && ReferenceEquals(current, this))
                    {
                        _owner._runs.Remove(_requestId);
                    }
                }

                return CancelRequested;
            }
        }

        public void CancelRun()
        {
            try
            {
                _run.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The request ended in the same moment; it already knows it was cancelled.
            }
        }

        public void Dispose() => Complete();
    }
}
