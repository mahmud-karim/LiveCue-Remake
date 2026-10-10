using LiveCue.Relay.Answers;

namespace LiveCue.Relay.Tests.Support;

/// <summary>
/// The answer seam for tests: returns fixed bytes, records every <see cref="AnswerRun"/>, can block on
/// a gate, can never return, and can fail with the text <c>PRIVATE DIAGNOSTIC</c>.
/// </summary>
internal sealed class FakeAnswerBackend : IAnswerBackend
{
    /// <summary>The text a failing collaborator carries; no response, frame or event may contain it.</summary>
    public const string PrivateText = "PRIVATE DIAGNOSTIC";

    private readonly Lock _gate = new();
    private readonly List<AnswerRun> _runs = [];
    private int _cancelled;

    /// <summary>What an assist run returns. Null means "no usable file".</summary>
    public byte[]? AssistResult { get; set; } = Bodies.ValidAssistOutput();

    /// <summary>What a summary run returns. Null means "no usable file".</summary>
    public byte[]? SummaryResult { get; set; } = Bodies.ValidSummaryOutput();

    /// <summary>When set, a run waits for it before returning. Cancelling the run ends the wait.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>True: a run never returns by itself; only its cancellation ends it.</summary>
    public bool NeverReturns { get; set; }

    /// <summary>True: a run throws an exception whose message is <see cref="PrivateText"/>.</summary>
    public bool FailsWithPrivateText { get; set; }

    /// <summary>Replaces everything above for one test.</summary>
    public Func<AnswerRun, CancellationToken, Task<byte[]?>>? Handler { get; set; }

    /// <summary>Every run that was started, in order.</summary>
    public IReadOnlyList<AnswerRun> Runs
    {
        get
        {
            lock (_gate)
            {
                return [.. _runs];
            }
        }
    }

    /// <summary>Runs that ended because their token fired: the "process was killed" of the real backend.</summary>
    public int Cancelled => Volatile.Read(ref _cancelled);

    /// <summary>Completes when the first run has started.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<byte[]?> RunAsync(AnswerRun run, CancellationToken ct)
    {
        lock (_gate)
        {
            _runs.Add(run);
        }

        Started.TrySetResult();
        try
        {
            if (Handler is { } handler)
            {
                return await handler(run, ct);
            }

            if (FailsWithPrivateText)
            {
                throw new InvalidOperationException(PrivateText);
            }

            if (NeverReturns)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(ct);
            }

            ct.ThrowIfCancellationRequested();
            return run.Kind == AnswerKind.Summary ? SummaryResult : AssistResult;
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelled);
            throw;
        }
    }
}
