using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.LocalModel;

/// <summary>Result of a start or stop command. Exactly one of Refusal and Status is set.</summary>
/// <param name="Began">True: an operation began (202). False with a Status: nothing needed doing (200).</param>
internal readonly record struct LocalModelCommand(RelayError? Refusal, LocalModelStatus? Status, bool Began);

internal enum PrepareOutcome
{
    Ready,

    /// <summary>The lab reports a running session (MODEL_BUSY).</summary>
    Busy,

    /// <summary>Another operation began between step S-5 and this call (MODEL_OPERATION_IN_PROGRESS).</summary>
    OperationInProgress,

    /// <summary>The load this call waited for failed (MODEL_LOAD_FAILED).</summary>
    LoadFailed,
}

/// <summary>Protocol 9.3. The caller has already validated the model name against C-04.</summary>
internal interface ILocalModelController
{
    /// <summary>True when a lab folder is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>True while a start or stop operation runs (C-35; steps M-5 and S-5).</summary>
    bool OperationRunning { get; }

    /// <summary>Route 4. Never fails for lab reasons.</summary>
    Task<LocalModelStatus> GetStatusAsync(CancellationToken ct);

    /// <summary>Steps M-4 to M-6 and rule 5. The operation it begins is not bound to <paramref name="ct"/>.</summary>
    Task<LocalModelCommand> StartAsync(string model, CancellationToken ct);

    /// <summary>Steps M-4 to M-7 and rule 6.</summary>
    Task<LocalModelCommand> StopAsync(string model, CancellationToken ct);

    /// <summary>
    /// Rule 7. <paramref name="ct"/> ends the caller's wait only: an operation that was begun continues
    /// as an ordinary operation (decision A-07). The caller checks OperationRunning before calling.
    /// </summary>
    Task<PrepareOutcome> PrepareAsync(string model, CancellationToken ct);
}
