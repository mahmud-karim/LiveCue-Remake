using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Support;

/// <summary>
/// The fake runtime of protocol 15.5: the probe returns <c>{ready, model, busy, relayProtocol: 1}</c>
/// from test state; start and stop count their calls; start waits on a gate, then marks the model
/// loaded.
/// </summary>
internal sealed class FakeLabRuntime : ILabRuntime
{
    /// <summary>The text of a failing start in protocol P2-086; nothing may leak it.</summary>
    public const string PrivateText = "PRIVATE API SECRET";

    private readonly Lock _gate = new();
    private int _probeCalls;
    private int _startCalls;
    private int _stopCalls;

    /// <summary>The model the lab has loaded, or null: then the probe finds no lab.</summary>
    public string? LoadedModel { get; set; }

    /// <summary>What the probe reports as "busy".</summary>
    public bool Busy { get; set; }

    /// <summary>What the probe reports as "relayProtocol".</summary>
    public int RelayProtocol { get; set; } = ProtocolConstants.LabRelayProtocol;

    /// <summary>True: every probe finds no lab, whatever is loaded.</summary>
    public bool ProbeFails { get; set; }

    /// <summary>When set, a start waits for it before it marks the model loaded.</summary>
    public TaskCompletionSource? StartGate { get; set; }

    /// <summary>False: a start returns "failed" and loads nothing.</summary>
    public bool StartSucceeds { get; set; } = true;

    /// <summary>True: a start throws an exception whose message is <see cref="PrivateText"/>.</summary>
    public bool StartThrowsPrivateText { get; set; }

    /// <summary>False: a stop returns "failed" and unloads nothing.</summary>
    public bool StopSucceeds { get; set; } = true;

    public int ProbeCalls => Volatile.Read(ref _probeCalls);

    public int StartCalls => Volatile.Read(ref _startCalls);

    public int StopCalls => Volatile.Read(ref _stopCalls);

    /// <summary>The models that were asked for, in order.</summary>
    public List<string> StartedModels { get; } = [];

    public Task<LabHealth?> ProbeAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _probeCalls);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ProbeFails || LoadedModel is null
                ? null
                : new LabHealth(Ready: true, LoadedModel, Busy, RelayProtocol));
        }
    }

    public async Task<bool> StartAsync(string model, CancellationToken ct)
    {
        Interlocked.Increment(ref _startCalls);
        lock (_gate)
        {
            StartedModels.Add(model);
        }

        if (StartGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        if (StartThrowsPrivateText)
        {
            throw new InvalidOperationException(PrivateText);
        }

        if (!StartSucceeds)
        {
            return false;
        }

        lock (_gate)
        {
            LoadedModel = model;
        }

        return true;
    }

    public Task<bool> StopAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _stopCalls);
        ct.ThrowIfCancellationRequested();
        if (!StopSucceeds)
        {
            return Task.FromResult(false);
        }

        lock (_gate)
        {
            LoadedModel = null;
        }

        return Task.FromResult(true);
    }
}
