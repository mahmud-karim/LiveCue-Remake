using LiveCue.Relay.Pairing;

namespace LiveCue.Relay.Tests.Support;

/// <summary>One request the self-check made: where to, and with which nonce.</summary>
internal sealed record SelfCheckCall(Uri HealthUrl, string Token, string Nonce);

/// <summary>
/// The self-check seam for tests: returns a scripted result and records each call. A handler can call
/// back into the relay with the nonce, as the edge proxy would.
/// </summary>
internal sealed class FakeSelfCheckClient : ISelfCheckClient
{
    private readonly Lock _gate = new();
    private readonly List<SelfCheckCall> _calls = [];

    /// <summary>What a check reports: true for "200 with ok true and protocol 2".</summary>
    public bool Result { get; set; } = true;

    /// <summary>When set, a check waits for it; cancelling the check (the C-49 limit) ends the wait.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Replaces the scripted result for one test.</summary>
    public Func<SelfCheckCall, CancellationToken, Task<bool>>? Handler { get; set; }

    public IReadOnlyList<SelfCheckCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public async Task<bool> GetHealthAsync(Uri healthUrl, string token, string nonce, CancellationToken ct)
    {
        var call = new SelfCheckCall(healthUrl, token, nonce);
        lock (_gate)
        {
            _calls.Add(call);
        }

        if (Handler is { } handler)
        {
            return await handler(call, ct);
        }

        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        return Result;
    }
}
