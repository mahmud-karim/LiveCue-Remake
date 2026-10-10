using LiveCue.Relay.Core;

namespace LiveCue.Relay.LocalModel;

/// <summary>
/// The lab as the relay sees it (protocol 9.2): the health probe and the two launcher scripts.
///
/// First version (foundation): there is never a lab. The probe finds nothing and no script is run.
/// Work package WP-W04 builds it.
/// </summary>
internal sealed class LabRuntime : ILabRuntime
{
    public LabRuntime(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public Task<LabHealth?> ProbeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<LabHealth?>(null);
    }

    public Task<bool> StartAsync(string model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task<bool> StopAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
