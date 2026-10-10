using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.LocalModel;

/// <summary>
/// The PC model state machine of protocol 9.3, as the tables of W-design 4.10.
///
/// It holds two things: the operation that is running, if any (C-35: at most one), and the error latch of
/// a failed operation (C-48). Everything else is derived from a probe each time it is asked. A semaphore
/// of one guards every change of the operation; commands evaluate their checks under it, in order. A
/// status request takes no semaphore, so phones that poll never queue behind each other's probes.
///
/// An operation runs in the background and belongs to no request and to no phone. When the relay stops,
/// the controller stops waiting for a running script and does not end it.
/// </summary>
#pragma warning disable CA1001 // A relay's areas have no dispose path (RelayServices). The semaphore is only ever awaited, so it never creates the wait handle that disposing would release.
internal sealed class LocalModelController : ILocalModelController
#pragma warning restore CA1001
{
    private const string DiagnosticSource = "local-model";

    private static readonly LocalModelStatus NotSetUp = new(
        LocalModelState.Stopped, Model: null, Busy: false, ElapsedSeconds: 0, FixedTexts.ModelNotSetUp);

    private readonly RelayContext _context;
    private readonly ILabRuntime _runtime;
    private readonly SpeechLease _lease;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Operation? _operation;
    private Latch? _latch;

    public LocalModelController(RelayContext context, SpeechLease lease)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lease);
        _context = context;
        _lease = lease;
        _runtime = context.Dependencies.LabRuntime ?? new LabRuntime(context);
        IsConfigured = LabRuntime.IsUsableLabFolder(context.Options.LabFolder);
    }

    private enum OperationKind
    {
        Start,
        Stop,
    }

    public bool IsConfigured { get; }

    public bool OperationRunning => Volatile.Read(ref _operation) is not null;

    public async Task<LocalModelStatus> GetStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsConfigured)
        {
            return NotSetUp;
        }

        // Rule 1: while an operation runs, the stored status is returned without a probe.
        if (Volatile.Read(ref _operation) is { } running)
        {
            return StatusOf(running);
        }

        // Rule 2: with no operation running, every status request probes.
        var latch = Volatile.Read(ref _latch);
        var health = await ProbeAsync(ct).ConfigureAwait(false);

        // An operation that began while the probe was on its way is the newer fact.
        return Volatile.Read(ref _operation) is { } began ? StatusOf(began) : Derive(health, latch);
    }

    public Task<LocalModelCommand> StartAsync(string model, CancellationToken ct) =>
        CommandAsync(OperationKind.Start, model, ct);

    public Task<LocalModelCommand> StopAsync(string model, CancellationToken ct) =>
        CommandAsync(OperationKind.Stop, model, ct);

    public async Task<PrepareOutcome> PrepareAsync(string model, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!IsConfigured)
        {
            return PrepareOutcome.LoadFailed;
        }

        Operation operation;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_operation is not null)
            {
                return PrepareOutcome.OperationInProgress;
            }

            var health = await ProbeAsync(ct).ConfigureAwait(false);
            if (health is { Busy: true })
            {
                return PrepareOutcome.Busy;
            }

            if (IsLoaded(health, model))
            {
                return PrepareOutcome.Ready;
            }

            operation = Begin(OperationKind.Start, model).Operation;
        }
        finally
        {
            _gate.Release();
        }

        // Outside the semaphore. Cancelling ends this wait and nothing else: the load goes on (decision A-07).
        return await operation.Completion.Task.WaitAsync(ct).ConfigureAwait(false)
            ? PrepareOutcome.Ready
            : PrepareOutcome.LoadFailed;
    }

    /// <summary>The commands table of W-design 4.10: each row under the semaphore, in order.</summary>
    private async Task<LocalModelCommand> CommandAsync(OperationKind kind, string model, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // M-4.
            if (!IsConfigured)
            {
                return Refuse(RelayErrors.LabNotConfigured);
            }

            // M-5.
            if (_operation is not null)
            {
                return Refuse(RelayErrors.ModelOperationInProgress);
            }

            // M-6: a phone speech stream holds the lease, or the lab reports a running session.
            if (_lease.IsHeld)
            {
                return Refuse(RelayErrors.ModelBusy);
            }

            var latch = Volatile.Read(ref _latch);
            var health = await ProbeAsync(ct).ConfigureAwait(false);
            if (health is { Busy: true })
            {
                return Refuse(RelayErrors.ModelBusy);
            }

            var usable = LabHealthParser.IsUsable(health, out var loaded);
            if (kind == OperationKind.Start && usable && string.Equals(loaded, model, StringComparison.Ordinal))
            {
                // Nothing needs doing: the script is not run (200).
                return new LocalModelCommand(Refusal: null, Derive(health, latch), Began: false);
            }

            // M-7.
            if (kind == OperationKind.Stop && usable && !string.Equals(loaded, model, StringComparison.Ordinal))
            {
                return Refuse(RelayErrors.ModelMismatch);
            }

            return new LocalModelCommand(Refusal: null, Begin(kind, model).Status, Began: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The last row of the commands table. The caller holds the semaphore. The beginning status has
    /// <c>busy</c> false and <c>elapsedSeconds</c> 0 (protocol 6.6).
    /// </summary>
    private (Operation Operation, LocalModelStatus Status) Begin(OperationKind kind, string model)
    {
        var operation = new Operation(kind, model, _context.Clock.GetTimestamp());

        // The operation first, the latch second: a status request in between sees the operation.
        Volatile.Write(ref _operation, operation);
        Volatile.Write(ref _latch, null);

        var status = StatusOf(operation);
        Publish(status);
        _ = Task.Run(() => RunAsync(operation));
        return (operation, status);
    }

    /// <summary>The background part of an operation. It never throws and always completes the operation.</summary>
    private async Task RunAsync(Operation operation)
    {
        var succeeded = false;
        try
        {
            var stopping = _context.Access.Stopping;
            LabHealth? health = null;
            var abandoned = false;
            try
            {
                // The script is not given the stop token: a relay that stops leaves a running script alone
                // (architecture 3.8) and only stops waiting for it.
                var script = operation.Kind == OperationKind.Start
                    ? _runtime.StartAsync(operation.Model, CancellationToken.None)
                    : _runtime.StopAsync(CancellationToken.None);
                Observe(script);
                succeeded = await script.WaitAsync(stopping).ConfigureAwait(false);
                if (succeeded && operation.Kind == OperationKind.Start)
                {
                    // Rule 5: probe again and require the requested model to be usable.
                    health = await ProbeAsync(stopping).ConfigureAwait(false);
                    succeeded = IsLoaded(health, operation.Model);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                succeeded = false;
                abandoned = true;
            }
#pragma warning disable CA1031 // Protocol 9.2: whatever went wrong is reduced to "failed".
            catch (Exception exception)
#pragma warning restore CA1031
            {
                succeeded = false;
                _context.Events.Diagnostic(DiagnosticSource, "A PC model operation failed unexpectedly.", exception);
            }

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (abandoned)
                {
                    // The relay is stopping: nothing is latched and nothing is reported.
                    Volatile.Write(ref _operation, null);
                }
                else if (succeeded)
                {
                    Volatile.Write(ref _operation, null);
                    // No latch exists here: beginning the operation cleared it.
                    Publish(Derive(operation.Kind == OperationKind.Start ? health : null, latchBeforeProbe: null));
                }
                else
                {
                    // The latch first, the operation second: no status request sees neither.
                    var latch = new Latch(operation.Kind, operation.Model, _context.Clock.GetTimestamp());
                    Volatile.Write(ref _latch, latch);
                    Volatile.Write(ref _operation, null);
                    Publish(StatusOf(latch));
                }
            }
            finally
            {
                _gate.Release();
            }
        }
#pragma warning disable CA1031 // Nothing may leave a background task; the operation is cleared below either way.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            succeeded = false;
            Interlocked.CompareExchange(ref _operation, null, operation);
            _context.Events.Diagnostic(DiagnosticSource, "A PC model operation could not be completed.", exception);
        }
        finally
        {
            operation.Completion.TrySetResult(succeeded);
        }
    }

    /// <summary>A probe that never fails for lab reasons, whatever the runtime does.</summary>
    private async Task<LabHealth?> ProbeAsync(CancellationToken ct)
    {
        try
        {
            return await _runtime.ProbeAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Protocol 9.2: any failure of the probe means "no lab".
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _context.Events.Diagnostic(DiagnosticSource, "The lab probe failed unexpectedly.", exception);
            return null;
        }
    }

    /// <summary>
    /// The probe-derived rows of the status table: a usable probe wins and clears the latch; otherwise a
    /// latch younger than C-48 is the error; otherwise the model is stopped and an expired latch is gone.
    /// </summary>
    /// <param name="health">What the probe found.</param>
    /// <param name="latchBeforeProbe">The latch as it was when that probe began. A usable probe clears
    /// only this one: a failure that happened while the probe was on its way is newer than the probe.</param>
    private LocalModelStatus Derive(LabHealth? health, Latch? latchBeforeProbe)
    {
        var leaseHeld = _lease.IsHeld;
        if (LabHealthParser.IsUsable(health, out var model))
        {
            if (latchBeforeProbe is not null)
            {
                Interlocked.CompareExchange(ref _latch, null, latchBeforeProbe);
            }

            var busy = health!.Busy || leaseHeld;
            return new LocalModelStatus(
                LocalModelState.Ready, model, busy, ElapsedSeconds: 0, busy ? FixedTexts.ModelInUse : FixedTexts.ModelReady);
        }

        if (Volatile.Read(ref _latch) is { } latch)
        {
            if (_context.Clock.GetElapsedTime(latch.FailedAt) < ProtocolConstants.ModelErrorLatch)
            {
                return StatusOf(latch);
            }

            // Only this latch: one that a newer failure set in the meantime stays.
            Interlocked.CompareExchange(ref _latch, null, latch);
        }

        return new LocalModelStatus(
            LocalModelState.Stopped, Model: null, leaseHeld, ElapsedSeconds: 0, FixedTexts.ModelStopped);
    }

    private LocalModelStatus StatusOf(Operation operation)
    {
        var seconds = Math.Floor(_context.Clock.GetElapsedTime(operation.StartedAt).TotalSeconds);
        var elapsed = (int)Math.Clamp(seconds, 0, int.MaxValue);
        return operation.Kind == OperationKind.Start
            ? new LocalModelStatus(LocalModelState.Starting, operation.Model, Busy: false, elapsed, FixedTexts.ModelStarting)
            : new LocalModelStatus(LocalModelState.Stopping, operation.Model, Busy: false, elapsed, FixedTexts.ModelStopping);
    }

    private LocalModelStatus StatusOf(Latch latch) => new(
        LocalModelState.Error,
        latch.Model,
        _lease.IsHeld,
        ElapsedSeconds: 0,
        latch.Kind == OperationKind.Start ? FixedTexts.ModelStartFailed : FixedTexts.ModelStopFailed);

    private void Publish(LocalModelStatus status) =>
        _context.Events.Publish(new LocalModelChanged(_context.Events.Now, status, status.Message));

    private static bool IsLoaded(LabHealth? health, string model) =>
        LabHealthParser.IsUsable(health, out var loaded) && string.Equals(loaded, model, StringComparison.Ordinal);

    private static LocalModelCommand Refuse(RelayError error) => new(error, Status: null, Began: false);

    /// <summary>A script the controller stopped waiting for must not end as an unobserved failure.</summary>
    private static void Observe(Task task) => task.ContinueWith(
        static completed => _ = completed.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private sealed class Operation(OperationKind kind, string model, long startedAt)
    {
        public OperationKind Kind { get; } = kind;

        public string Model { get; } = model;

        /// <summary>Clock timestamp of the beginning.</summary>
        public long StartedAt { get; } = startedAt;

        /// <summary>True when the operation succeeded. Completed after the operation was cleared.</summary>
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record Latch(OperationKind Kind, string Model, long FailedAt);
}
