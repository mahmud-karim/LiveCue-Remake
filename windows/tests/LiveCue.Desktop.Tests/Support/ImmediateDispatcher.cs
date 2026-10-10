namespace LiveCue.Desktop.Tests.Support;

/// <summary>
/// Runs posted work at once, on the calling thread: the "UI thread" of a view-model test. With
/// <see cref="Queue"/> set it holds the work instead, until the test calls <see cref="RunQueued"/>.
/// </summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    private readonly Lock _gate = new();
    private readonly Queue<Action> _queued = new();

    /// <summary>True: posted work waits for <see cref="RunQueued"/>.</summary>
    public bool Queue { get; set; }

    /// <summary>How many actions were posted.</summary>
    public int Posted { get; private set; }

    public bool CheckAccess() => true;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            Posted++;
            if (Queue)
            {
                _queued.Enqueue(action);
                return;
            }
        }

        action();
    }

    /// <summary>Runs what was held, in the order it was posted.</summary>
    public void RunQueued()
    {
        while (true)
        {
            Action action;
            lock (_gate)
            {
                if (_queued.Count == 0)
                {
                    return;
                }

                action = _queued.Dequeue();
            }

            action();
        }
    }
}
