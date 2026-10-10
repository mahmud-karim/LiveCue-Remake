namespace LiveCue.Relay.Edge;

/// <summary>
/// Counts the authenticated HTTP requests in flight (C-31, step E-7). The foundation's pipeline does not
/// use it yet; work package WP-W01 does.
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
