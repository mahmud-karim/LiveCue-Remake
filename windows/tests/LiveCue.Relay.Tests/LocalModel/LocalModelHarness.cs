using LiveCue.Relay.Core;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.LocalModel;

/// <summary>
/// What the PC model area receives at construction, built by hand: the manual clock, the fake lab runtime
/// of protocol 15.5, the fake process runner, a temporary data folder, a lease the test can take itself,
/// and an event sink that keeps what was published. No listener, no relay.
/// </summary>
internal sealed class LocalModelHarness : IDisposable
{
    /// <summary>A lab folder of the form the relay accepts. Nothing exists there and nothing is read.</summary>
    public const string LabFolder = @"C:\LiveCue-tests\asr";

    private readonly Lazy<LocalModelController> _controller;

    public LocalModelHarness(string? labFolder = LabFolder, int labPort = 47842, ILabRuntime? runtime = null)
    {
        Events = new RecordingEvents(Clock);
        Options = new RelayOptions(
            Port: 0,
            AdvertisedEndpoint: RelayTestHost.Endpoint,
            Mode: ConnectionMode.Private,
            DataDirectory: Data.Path,
            CodexExecutablePath: null,
            LabFolder: labFolder,
            LabPort: labPort,
            RelayVersion: RelayTestHost.RelayVersion,
            WarmCatalogOnStart: false);
        Context = new RelayContext(
            Options,
            new RelayDependencies { Clock = Clock, Processes = Processes, LabRuntime = runtime ?? Lab },
            Events,
            Access,
            Processes,
            RelayTestHost.Endpoint);
        _controller = new Lazy<LocalModelController>(() => new LocalModelController(Context, Lease));
    }

    public ManualTimeProvider Clock { get; } = new();

    public TempFolder Data { get; } = new();

    public FakeLabRuntime Lab { get; } = new();

    public FakeProcessRunner Processes { get; } = new();

    public SpeechLease Lease { get; } = new();

    public StoppableAccess Access { get; } = new();

    public RecordingEvents Events { get; }

    public RelayOptions Options { get; }

    public RelayContext Context { get; }

    /// <summary>The real controller over <see cref="Lab"/> (or the runtime the test gave).</summary>
    public LocalModelController Controller => _controller.Value;

    /// <summary>A gate for <see cref="FakeLabRuntime.StartGate"/>: the start waits until the test opens it.</summary>
    public static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Waits until no operation runs and every operation that began has published its ending status. (An
    /// operation is cleared a moment before its ending is published, so both are awaited.)
    /// </summary>
    public Task WaitForIdleAsync() => Waiting.UntilAsync(() =>
    {
        var statuses = Events.Statuses;
        var begun = statuses.Count(status => status.State is LocalModelState.Starting or LocalModelState.Stopping);
        return !Controller.OperationRunning && statuses.Count == 2 * begun;
    });

    public void Dispose()
    {
        Access.Dispose();
        Data.Dispose();
    }
}

/// <summary>An event sink that keeps every event, as the relay's channel would deliver it.</summary>
internal sealed class RecordingEvents(TimeProvider clock) : IRelayEventSink
{
    private readonly Lock _gate = new();
    private readonly List<RelayEvent> _events = [];

    public DateTimeOffset Now => clock.GetUtcNow();

    public IReadOnlyList<RelayEvent> Snapshot()
    {
        lock (_gate)
        {
            return [.. _events];
        }
    }

    public IReadOnlyList<T> OfType<T>()
        where T : RelayEvent => [.. Snapshot().OfType<T>()];

    /// <summary>The model statuses that were published, in order.</summary>
    public IReadOnlyList<LocalModelStatus> Statuses => [.. OfType<LocalModelChanged>().Select(changed => changed.Status)];

    public void Publish(RelayEvent relayEvent)
    {
        lock (_gate)
        {
            _events.Add(relayEvent);
        }
    }

    public void Diagnostic(string source, string fixedText, Exception? exception = null) => Publish(new Diagnostic(
        Now, source, exception is null ? fixedText : $"{fixedText} ({exception.GetType().Name})"));
}

/// <summary>The access state of a relay that accepts, with a stop the test can trigger.</summary>
internal sealed class StoppableAccess : IAccessState, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();

    public bool Accepting { get; set; } = true;

    public CancellationToken WhileAccepting => CancellationToken.None;

    public CancellationToken Stopping => _stopping.Token;

    /// <summary>What <c>StopAsync</c> does first: the stopping token fires.</summary>
    public void BeginStopping() => _stopping.Cancel();

    public void Dispose() => _stopping.Dispose();
}

/// <summary>Waiting for something another thread does, in real time and never for a protocol limit.</summary>
internal static class Waiting
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Limit;
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The awaited condition did not come true in time.");
            }

            await Task.Delay(2);
        }
    }
}
