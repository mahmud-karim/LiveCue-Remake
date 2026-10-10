using System.Text;
using LiveCue.Relay.Processes;

namespace LiveCue.Relay.Tests.Support;

/// <summary>
/// The process seam for tests: records every <see cref="ProcessRequest"/> and plays scripted results
/// and line sessions. Nothing is ever started. Uses public types only, so the hosting tests link this
/// file too.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Lock _gate = new();
    private readonly List<ProcessRequest> _requests = [];
    private readonly Queue<Func<ProcessRequest, ProcessResult>> _results = new();
    private readonly Queue<FakeProcessSession> _sessions = new();

    /// <summary>What a run returns when nothing is queued: exit code 0 and no output.</summary>
    public Func<ProcessRequest, ProcessResult> Default { get; set; } =
        _ => new ProcessResult(ProcessOutcome.Exited, 0, ReadOnlyMemory<byte>.Empty);

    /// <summary>When set, a run waits for it. Cancelling the run ends it with <see cref="ProcessOutcome.Cancelled"/>.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Every request, for runs and for sessions, in order.</summary>
    public IReadOnlyList<ProcessRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Every session that was started, in order.</summary>
    public List<FakeProcessSession> StartedSessions { get; } = [];

    /// <summary>The next run returns this.</summary>
    public void Enqueue(ProcessResult result) => Enqueue(_ => result);

    public void Enqueue(Func<ProcessRequest, ProcessResult> result)
    {
        lock (_gate)
        {
            _results.Enqueue(result);
        }
    }

    /// <summary>The next run exits with this code and this standard output.</summary>
    public void EnqueueExit(int exitCode, string standardOutput = "") =>
        Enqueue(new ProcessResult(ProcessOutcome.Exited, exitCode, Encoding.UTF8.GetBytes(standardOutput)));

    /// <summary>The next session is this one. Without one queued, StartSessionAsync fails with StartFailed.</summary>
    public void EnqueueSession(FakeProcessSession session)
    {
        lock (_gate)
        {
            _sessions.Enqueue(session);
        }
    }

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        Func<ProcessRequest, ProcessResult> result;
        lock (_gate)
        {
            _requests.Add(request);
            result = _results.Count > 0 ? _results.Dequeue() : Default;
        }

        if (Gate is { } gate)
        {
            try
            {
                await gate.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return new ProcessResult(ProcessOutcome.Cancelled, -1, ReadOnlyMemory<byte>.Empty);
            }
        }

        return ct.IsCancellationRequested
            ? new ProcessResult(ProcessOutcome.Cancelled, -1, ReadOnlyMemory<byte>.Empty)
            : result(request);
    }

    public Task<IProcessSession> StartSessionAsync(ProcessRequest request, CancellationToken ct)
    {
        lock (_gate)
        {
            _requests.Add(request);
            if (_sessions.Count == 0)
            {
                throw new ProcessFailedException(ProcessOutcome.StartFailed);
            }

            var session = _sessions.Dequeue();
            StartedSessions.Add(session);
            return Task.FromResult<IProcessSession>(session);
        }
    }
}

/// <summary>
/// A scripted line conversation. The test queues what the "process" prints; every line the relay
/// writes is recorded, and a reply function can answer a written line with more output.
/// </summary>
internal sealed class FakeProcessSession : IProcessSession
{
    private readonly Lock _gate = new();
    private readonly Queue<string?> _output = new();
    private readonly List<string> _written = [];
    private TaskCompletionSource _available = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Called for each written line; what it returns is printed by the "process".</summary>
    public Func<string, IEnumerable<string>>? OnLine { get; set; }

    /// <summary>When set, the next read throws <see cref="ProcessFailedException"/> with this outcome.</summary>
    public ProcessOutcome? FailNextRead { get; set; }

    public bool Disposed { get; private set; }

    /// <summary>Every line the relay wrote, in order.</summary>
    public IReadOnlyList<string> Written
    {
        get
        {
            lock (_gate)
            {
                return [.. _written];
            }
        }
    }

    /// <summary>The "process" prints one line.</summary>
    public FakeProcessSession Print(string line)
    {
        Push(line);
        return this;
    }

    /// <summary>The "process" ends its output.</summary>
    public FakeProcessSession End()
    {
        Push(null);
        return this;
    }

    public ValueTask WriteLineAsync(string line, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _written.Add(line);
        }

        if (OnLine is { } reply)
        {
            foreach (var printed in reply(line))
            {
                Push(printed);
            }
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            Task available;
            lock (_gate)
            {
                if (FailNextRead is { } outcome)
                {
                    FailNextRead = null;
                    throw new ProcessFailedException(outcome);
                }

                if (_output.Count > 0)
                {
                    return _output.Dequeue();
                }

                available = _available.Task;
            }

            await available.WaitAsync(ct);
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    private void Push(string? line)
    {
        TaskCompletionSource available;
        lock (_gate)
        {
            _output.Enqueue(line);
            available = _available;
            _available = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        available.TrySetResult();
    }
}
