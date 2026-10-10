namespace LiveCue.Relay.Edge;

/// <summary>
/// Counts the authenticated HTTP requests in flight (C-31, step E-7): the requests that passed step P-10
/// and have not ended. A request leaves exactly once, however it ends.
/// </summary>
internal sealed class RequestGauge(int capacity)
{
    private int _inFlight;

    public int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>False, and nothing is counted, when the cap is already reached.</summary>
    public bool TryEnter()
    {
        if (Interlocked.Increment(ref _inFlight) <= capacity)
        {
            return true;
        }

        Interlocked.Decrement(ref _inFlight);
        return false;
    }

    public void Leave() => Interlocked.Decrement(ref _inFlight);
}
