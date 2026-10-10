using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Support;

/// <summary>
/// The "prepare stub" of protocol 15.6: a whole PC model controller for tests of the speech bridge.
/// It records the models a prepare was asked for and answers as the test scripted it.
/// </summary>
internal sealed class FakeLocalModelController : ILocalModelController
{
    /// <summary>The text of a failing prepare in protocol P2-105; no frame may contain it.</summary>
    public const string PrivateText = "private diagnostic must not escape";

    private int _prepareCalls;

    public bool IsConfigured { get; set; } = true;

    public bool OperationRunning { get; set; }

    /// <summary>What route 4 reports.</summary>
    public LocalModelStatus Status { get; set; } =
        new(LocalModelState.Ready, "nemotron", Busy: false, ElapsedSeconds: 0, FixedTexts.ModelReady);

    /// <summary>What a prepare returns.</summary>
    public PrepareOutcome PrepareResult { get; set; } = PrepareOutcome.Ready;

    /// <summary>When set, a prepare waits for it (a load that is still running). Cancelling ends only the wait.</summary>
    public TaskCompletionSource? PrepareGate { get; set; }

    /// <summary>True: a prepare throws an exception whose message is <see cref="PrivateText"/>.</summary>
    public bool PrepareThrowsPrivateText { get; set; }

    public int PrepareCalls => Volatile.Read(ref _prepareCalls);

    /// <summary>The models a prepare was asked for, in order.</summary>
    public List<string> PreparedModels { get; } = [];

    public Task<LocalModelStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(Status);

    public Task<LocalModelCommand> StartAsync(string model, CancellationToken ct) =>
        Task.FromResult(new LocalModelCommand(null, Status, Began: false));

    public Task<LocalModelCommand> StopAsync(string model, CancellationToken ct) =>
        Task.FromResult(new LocalModelCommand(null, Status, Began: false));

    public async Task<PrepareOutcome> PrepareAsync(string model, CancellationToken ct)
    {
        Interlocked.Increment(ref _prepareCalls);
        lock (PreparedModels)
        {
            PreparedModels.Add(model);
        }

        if (PrepareGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        if (PrepareThrowsPrivateText)
        {
            throw new InvalidOperationException(PrivateText);
        }

        return PrepareResult;
    }
}
