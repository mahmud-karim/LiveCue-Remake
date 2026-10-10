using System.Threading.Channels;
using LiveCue.Relay;

namespace LiveCue.Hosting;

/// <summary>Creates relay instances. The production factory calls RelayFactory.Create; the desktop's
/// self-test and the tests substitute a scripted relay.</summary>
public interface IRelayFactory
{
    IRelay Create(RelayOptions options);
}

public enum SupervisorState { Stopped, Checking, Starting, Running, Stopping }

/// <summary>Per-run overrides of the console host (architecture 3.14). Null members mean "the setting".</summary>
public sealed record RelayOverrides(
    string? Endpoint = null,
    int? Port = null,
    string? CodexPath = null,
    string? LabFolder = null,
    ConnectionMode? Mode = null);

public sealed record SupervisorOptions(
    string DataDirectory,
    string RelayVersion,
    RelayOverrides? Overrides = null);

/// <summary>Everything a host observes, in order, on one bounded channel (W-01).</summary>
public abstract record HostEvent(DateTimeOffset At);

/// <summary>A start began: the checks are running.</summary>
public sealed record CheckingStarted(DateTimeOffset At) : HostEvent(At);

/// <summary>The checks finished, passed or not.</summary>
public sealed record PrerequisitesChecked(DateTimeOffset At, PrerequisiteReport Report) : HostEvent(At);

/// <summary>The checks passed and a relay instance is about to start. Hosts clear their activity here.</summary>
public sealed record RelayStarting(DateTimeOffset At, RelayOptions Options) : HostEvent(At);

/// <summary>One event of the current relay instance, unchanged.</summary>
public sealed record RelayEventForwarded(DateTimeOffset At, RelayEvent Event) : HostEvent(At);

/// <summary>A read-only mapping inspection finished (step 8 of architecture 3.11, or on demand).</summary>
public sealed record ConnectionInspected(DateTimeOffset At, ConnectionInspection Inspection) : HostEvent(At);

public enum StartOutcome { Started, PrerequisiteFailed, RelayFault, AlreadyRunning }

/// <param name="FailureText">The status-line text of architecture 3.11 for the failure, or null.</param>
public sealed record StartResult(
    StartOutcome Outcome,
    PrerequisiteReport? Report,
    RelayFaultCode? Fault,
    string? FailureText);

/// <summary>
/// Architecture 3.3 and 3.11: one relay instance per start. Thread-safe; Start, Stop and
/// ResetStoredPairing are serialised. No member ever runs a Tailscale write command.
/// </summary>
public interface IRelaySupervisor : IAsyncDisposable
{
    SupervisorState State { get; }

    /// <summary>The running instance, or null. The desktop uses it for pause and pairing only.</summary>
    IRelay? Relay { get; }

    /// <summary>The settings of the last load: at construction, at each start and after each save.</summary>
    RelaySettings Settings { get; }

    /// <summary>The options of the running instance, or null. Lets a host tell whether saved settings
    /// differ from what the relay uses.</summary>
    RelayOptions? RunningOptions { get; }

    ChannelReader<HostEvent> Events { get; }

    /// <summary>
    /// Steps 1 to 8 of architecture 3.11. Never throws for an expected failure: the result and the events
    /// carry it. With State other than Stopped it returns AlreadyRunning and does nothing.
    /// </summary>
    Task<StartResult> StartAsync(CancellationToken ct);

    /// <summary>Stops and disposes the instance. Idempotent. Bounded by W-02 plus one second.</summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>Steps 1 to 6 only. Publishes PrerequisitesChecked. Does not change State.</summary>
    Task<PrerequisiteReport> CheckAsync(CancellationToken ct);

    /// <summary>Step 8 on demand. Publishes ConnectionInspected.</summary>
    Task<ConnectionInspection> InspectConnectionAsync(CancellationToken ct);

    /// <summary>Validates and saves, then reloads Settings. Does not restart the relay.</summary>
    void SaveSettings(RelaySettings settings);

    /// <summary>PairingMaintenance for the data folder. Throws InvalidOperationException unless Stopped.</summary>
    Task ResetStoredPairingAsync(CancellationToken ct);
}
