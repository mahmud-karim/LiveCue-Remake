namespace LiveCue.Relay.Core;

/// <summary>The one speech lease (C-34). Complete as written; no package changes it.</summary>
internal sealed class SpeechLease
{
    private int _held;

    /// <summary>True while a stream holds the lease. Read by route 4 (busy) and by M-6.</summary>
    public bool IsHeld => Volatile.Read(ref _held) == 1;

    /// <summary>Step S-6. Returns a handle whose Dispose releases the lease exactly once, or null.</summary>
    public IDisposable? TryAcquire()
        => Interlocked.CompareExchange(ref _held, 1, 0) == 0 ? new Handle(this) : null;

    private sealed class Handle(SpeechLease owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Volatile.Write(ref owner._held, 0);
            }
        }
    }
}
