using System.Collections.Concurrent;

namespace LiveCue.Desktop.Tests.Session;

/// <summary>
/// A real "UI thread" for session tests: one thread that runs what is posted to it, in order, with a
/// synchronisation context, so an <c>await</c> made on it comes back to it, as on the WPF dispatcher.
/// The test puts its own code on that thread with <see cref="RunAsync(Func{Task})"/>; everything the
/// session does then happens where it happens in the app, and a test can see whether a command left
/// the thread free.
/// </summary>
internal sealed class UiThread : IUiDispatcher, IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;

    public UiThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Test UI thread" };
        _thread.Start();
    }

    /// <summary>The managed id of the one thread everything posted here runs on.</summary>
    public int ThreadId => _thread.ManagedThreadId;

    /// <summary>How many actions were posted.</summary>
    public int Posted => Volatile.Read(ref _posted);

    private int _posted;

    public bool CheckAccess() => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Interlocked.Increment(ref _posted);
        if (!_queue.IsAddingCompleted)
        {
            _queue.Add(action);
        }
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread and completes when it has completed.</summary>
    public Task RunAsync(Func<Task> body) => RunAsync(async () =>
    {
        await body();
        return true;
    });

    public Task<T> RunAsync<T>(Func<Task<T>> body)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => Start(body, done));
        return done.Task;
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread.</summary>
    public Task RunAsync(Action body) => RunAsync(() =>
    {
        body();
        return Task.CompletedTask;
    });

    public Task<T> RunAsync<T>(Func<T> body) => RunAsync(() => Task.FromResult(body()));

    /// <summary>
    /// Waits, on the UI thread, until <paramref name="condition"/> holds. Events reach the session from a
    /// background task, so a test that published one waits for its effect rather than for a time.
    /// </summary>
    public Task UntilAsync(Func<bool> condition, string what = "the expected state") => RunAsync(async () =>
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, $"The session did not reach {what} in time.");
            await Task.Delay(2);
        }
    });

    public void Dispose()
    {
        _queue.CompleteAdding();
        if (!CheckAccess())
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        _queue.Dispose();
    }

    private static async void Start<T>(Func<Task<T>> body, TaskCompletionSource<T> done)
    {
        try
        {
            done.TrySetResult(await body());
        }
#pragma warning disable CA1031 // Carried to the awaiting test.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            done.TrySetException(exception);
        }
    }

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new Context(this));
        try
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // Disposed while idle.
        }
    }

    private sealed class Context(UiThread owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => owner.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (owner.CheckAccess())
            {
                d(state);
            }
            else
            {
                owner.RunAsync(() => d(state)).GetAwaiter().GetResult();
            }
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
