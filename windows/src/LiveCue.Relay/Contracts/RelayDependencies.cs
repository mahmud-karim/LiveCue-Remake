using LiveCue.Relay.Answers;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Speech;

namespace LiveCue.Relay;

/// <summary>
/// The seams (architecture 3.2). Production code passes null or a default instance. A null member means
/// "use the production implementation". Used by tests, and by the hosting library for Clock and Processes.
/// </summary>
public sealed class RelayDependencies
{
    /// <summary>The only source of time and timers.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Runs Codex and the lab scripts.</summary>
    public IProcessRunner? Processes { get; init; }

    /// <summary>Parent of the per-request Codex folders. Null means DataDirectory\work (protocol 12.2),
    /// which the relay empties at each start. A folder given here is never emptied by the relay.</summary>
    public string? TempRoot { get; init; }

    internal IPairingProtector? PairingProtector { get; init; }

    internal IPairingFiles? PairingFiles { get; init; }

    internal ISelfCheckClient? SelfCheckClient { get; init; }

    internal ICatalogSource? CatalogSource { get; init; }

    internal IAnswerBackend? AnswerBackend { get; init; }

    internal ILabRuntime? LabRuntime { get; init; }

    internal ILabSocketConnector? LabSocketConnector { get; init; }

    /// <summary>Tests of the speech bridge only: replaces the whole PC model controller (the "prepare
    /// stub" of protocol 15.6). Null everywhere else.</summary>
    internal ILocalModelController? LocalModelController { get; init; }
}
