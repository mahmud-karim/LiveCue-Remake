using LiveCue.Relay.Core;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// What the catalogue and answer areas receive at construction, built by hand: the manual clock, a
/// temporary data folder, the fake process runner (or the runner a test gives), the fake catalogue
/// source, the fake answer backend, and an event sink that keeps what was published. No listener.
/// </summary>
internal sealed class AreaHarness : IDisposable
{
    /// <param name="codexPath">The Codex path of the options; null says "none was found".</param>
    /// <param name="processes">Replaces the fake process runner.</param>
    /// <param name="systemClock">True gives the areas the real clock instead of the manual one.</param>
    /// <param name="tempRoot">The seam for the parent of the work folders; null means <c>work\</c> in the data folder.</param>
    /// <param name="manualClock">A manual clock the test shares with something else, a real process runner for one.</param>
    /// <param name="productionBackend">True leaves the answer seam empty, so that the coordinator builds the Codex backend.</param>
    public AreaHarness(
        string? codexPath = RelayTestHost.FakeCodexPath,
        IProcessRunner? processes = null,
        bool systemClock = false,
        string? tempRoot = null,
        ManualTimeProvider? manualClock = null,
        bool productionBackend = false)
    {
        Clock = manualClock ?? new ManualTimeProvider();
        TimeProvider clock = systemClock ? TimeProvider.System : Clock;
        Events = new RecordingEvents(clock);
        Options = new RelayOptions(
            Port: 0,
            AdvertisedEndpoint: RelayTestHost.Endpoint,
            Mode: ConnectionMode.Private,
            DataDirectory: Data.Path,
            CodexExecutablePath: codexPath,
            LabFolder: null,
            LabPort: 47842,
            RelayVersion: RelayTestHost.RelayVersion,
            WarmCatalogOnStart: false);
        var runner = processes ?? Processes;
        Context = new RelayContext(
            Options,
            new RelayDependencies
            {
                Clock = clock,
                Processes = runner,
                TempRoot = tempRoot,
                CatalogSource = Catalog,
                AnswerBackend = productionBackend ? null : Backend,
            },
            Events,
            Access,
            runner,
            RelayTestHost.Endpoint);
    }

    public ManualTimeProvider Clock { get; }

    public TempFolder Data { get; } = new();

    public FakeProcessRunner Processes { get; } = new();

    public FakeCatalogSource Catalog { get; } = new();

    public FakeAnswerBackend Backend { get; } = new();

    public StoppableAccess Access { get; } = new();

    public RecordingEvents Events { get; }

    public RelayOptions Options { get; }

    public RelayContext Context { get; }

    /// <summary>A gate a fake waits on until the test opens it.</summary>
    public static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Moves the manual clock forward in small steps until <paramref name="task"/> has ended: for work
    /// whose only waits are short pauses on the relay's clock (the gap between catalogue attempts, the
    /// gap between tries at deleting a folder). Never used to reach a protocol limit.
    /// </summary>
    public async Task<T> PumpAsync<T>(Task<T> task, TimeSpan? step = null)
    {
        await PumpAsync((Task)task, step);
        return await task;
    }

    public async Task PumpAsync(Task task, TimeSpan? step = null)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The awaited work did not end in time.");
            }

            Clock.Advance(step ?? TimeSpan.FromMilliseconds(10));
            await Task.Delay(2);
        }
    }

    public void Dispose()
    {
        Access.Dispose();
        Data.Dispose();
    }
}
