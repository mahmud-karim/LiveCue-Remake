namespace LiveCue.Desktop.Session;

/// <summary>
/// The phone presence machine of architecture 3.12. A phone is "connected" when its last authenticated
/// request is at most <see cref="IdleAfter"/> old and "idle" when it is older. Time alone never makes
/// it "not connected": iOS stops the heartbeat while the app is in the background, so a silent phone is
/// not an absent one. Only the end of the relay resets it.
/// </summary>
internal sealed class PresenceTracker
{
    /// <summary>Architecture 3.12: the age of the last authenticated request above which a phone is idle.</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(15);

    public PhonePresence Presence { get; private set; } = PhonePresence.NotConnected;

    /// <summary>The PC clock at the last authenticated request since the relay started; null before the first.</summary>
    public DateTimeOffset? LastSeen { get; private set; }

    /// <summary>An authenticated request arrived.</summary>
    public void Seen(DateTimeOffset now)
    {
        LastSeen = now;
        Presence = PhonePresence.Connected;
    }

    /// <summary>The clock tick (W-10). Returns true when the presence changed.</summary>
    public bool Tick(DateTimeOffset now)
    {
        if (Presence != PhonePresence.Connected || LastSeen is not { } lastSeen || now - lastSeen <= IdleAfter)
        {
            return false;
        }

        Presence = PhonePresence.Idle;
        return true;
    }

    /// <summary>The relay started or ended: nothing is known about a phone.</summary>
    public void Reset()
    {
        LastSeen = null;
        Presence = PhonePresence.NotConnected;
    }
}
