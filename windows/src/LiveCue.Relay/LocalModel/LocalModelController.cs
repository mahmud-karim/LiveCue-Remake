using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.LocalModel;

/// <summary>
/// The PC model state machine of protocol 9.3.
///
/// First version (foundation): PC speech is always "not configured". The status is MSG-2, start and stop
/// are refused with LAB_NOT_CONFIGURED and a prepare fails. Work package WP-W04 builds the controller.
/// </summary>
internal sealed class LocalModelController : ILocalModelController
{
    private static readonly LocalModelStatus NotSetUp = new(
        LocalModelState.Stopped, Model: null, Busy: false, ElapsedSeconds: 0, FixedTexts.ModelNotSetUp);

    private readonly ILabRuntime _runtime;
    private readonly SpeechLease _lease;

    public LocalModelController(RelayContext context, SpeechLease lease)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lease);
        _lease = lease;
        _runtime = context.Dependencies.LabRuntime ?? new LabRuntime(context);
    }

    public bool IsConfigured => false;

    public bool OperationRunning => false;

    public Task<LocalModelStatus> GetStatusAsync(CancellationToken ct)
    {
        _ = _runtime;
        _ = _lease;
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(NotSetUp);
    }

    public Task<LocalModelCommand> StartAsync(string model, CancellationToken ct) => RefuseAsync(ct);

    public Task<LocalModelCommand> StopAsync(string model, CancellationToken ct) => RefuseAsync(ct);

    public Task<PrepareOutcome> PrepareAsync(string model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(PrepareOutcome.LoadFailed);
    }

    private static Task<LocalModelCommand> RefuseAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new LocalModelCommand(RelayErrors.LabNotConfigured, Status: null, Began: false));
    }
}
