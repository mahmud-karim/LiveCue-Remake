namespace LiveCue.Relay.Answers;

/// <summary>
/// The one slot assist and summary requests share (C-33): an interlocked flag. The foundation's
/// coordinator does not use it yet; work package WP-W03 does.
/// </summary>
internal sealed class AnswerSlot
{
    private int _held;

    public bool IsHeld => Volatile.Read(ref _held) == 1;

    /// <summary>Step R-2. False when another request holds the slot.</summary>
    public bool TryEnter() => Interlocked.CompareExchange(ref _held, 1, 0) == 0;

    public void Leave() => Volatile.Write(ref _held, 0);
}
