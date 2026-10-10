using System.Threading.Channels;
using LiveCue.Desktop.Session;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.Session;

/// <summary>
/// An <see cref="IRelaySupervisor"/> the test scripts: it publishes the events it is told to, answers a
/// start with the result the test chose, and remembers which thread each call arrived on. Nothing here
/// checks anything, starts anything or touches a file.
/// </summary>
internal sealed class FakeSupervisor : IRelaySupervisor
{
    public const string Endpoint = "https://pc.example.test:11443/";
    public const string DnsName = "pc.example.test";

    private readonly Channel<HostEvent> _events = Channel.CreateUnbounded<HostEvent>();
    private readonly TimeProvider _clock;

    public FakeSupervisor(TimeProvider clock)
    {
        _clock = clock;
        OnStart = StartsNormally;
    }

    public SupervisorState State { get; set; } = SupervisorState.Stopped;

    public IRelay? Relay { get; set; }

    public RelaySettings Settings { get; set; } = RelaySettings.Default;

    public RelayOptions? RunningOptions { get; set; }

    public ChannelReader<HostEvent> Events => _events.Reader;

    /// <summary>What a start does: publish events, then return its result.</summary>
    public Func<FakeSupervisor, Task<StartResult>> OnStart { get; set; }

    /// <summary>When set, a save throws it.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>When set, a stored-pairing reset throws it.</summary>
    public Exception? ResetFailure { get; set; }

    /// <summary>The report a check publishes.</summary>
    public PrerequisiteReport Report { get; set; } = PassingReport();

    /// <summary>The inspection that is published on demand and at the end of a normal start.</summary>
    public ConnectionInspection Inspection { get; set; } = InspectionOf(MappingState.Present);

    /// <summary>Whether the relay of the next normal start says it created its pairing.</summary>
    public bool PairingWasCreated { get; set; }

    public int StartCalls => Volatile.Read(ref _startCalls);

    public int StopCalls => Volatile.Read(ref _stopCalls);

    public int CheckCalls => Volatile.Read(ref _checkCalls);

    public int InspectCalls => Volatile.Read(ref _inspectCalls);

    public int ResetCalls => Volatile.Read(ref _resetCalls);

    public List<RelaySettings> Saved { get; } = [];

    /// <summary>The managed thread of every call that the session must make off the UI thread.</summary>
    public System.Collections.Concurrent.ConcurrentBag<int> CallThreads { get; } = [];

    private int _startCalls;
    private int _stopCalls;
    private int _checkCalls;
    private int _inspectCalls;
    private int _resetCalls;

    public DateTimeOffset Now => _clock.GetUtcNow();

    public static PrerequisiteReport PassingReport(RelaySettings? settings = null, string? codexVersion = null, string? codexPath = null) =>
        new(
            [
                new(PrerequisiteId.DataFolder, PrerequisiteState.Passed, null),
                new(PrerequisiteId.CodexFound, PrerequisiteState.Passed, codexPath),
                new(PrerequisiteId.CodexSignedIn, PrerequisiteState.Passed, null),
                new(PrerequisiteId.TailscaleInstalled, PrerequisiteState.Passed, null),
                new(PrerequisiteId.TailscaleOnline, PrerequisiteState.Passed, null),
                new(PrerequisiteId.TailscaleDnsName, PrerequisiteState.Passed, DnsName),
            ],
            new SettingsLoadResult(settings ?? RelaySettings.Default, SettingsFileState.Loaded, []),
            codexPath,
            DnsName,
            codexVersion);

    public static PrerequisiteReport FailingReport(PrerequisiteId failed) =>
        new(
            Enum.GetValues<PrerequisiteId>()
                .Select(id => new PrerequisiteResult(
                    id,
                    id < failed ? PrerequisiteState.Passed : id == failed ? PrerequisiteState.Failed : PrerequisiteState.NotChecked,
                    null))
                .ToList(),
            new SettingsLoadResult(RelaySettings.Default, SettingsFileState.Loaded, []),
            CodexExecutablePath: null,
            DnsName: null);

    public static ConnectionInspection InspectionOf(
        MappingState state, MappingRefusal blockedBy = MappingRefusal.None, IReadOnlyList<ExposedHandler>? exposure = null) =>
        new(
            new MappingInspection(ConnectionMode.Private, state, blockedBy, state == MappingState.Present, 11443, "/"),
            new MappingInspection(ConnectionMode.Public, MappingState.Absent, MappingRefusal.None, false, 10000, "/livecue"),
            StaleOwned: [],
            TailscaleReadFailure.None,
            ManualTimeProvider.DefaultStart,
            exposure ?? []);

    public RelayOptions OptionsFor(RelaySettings settings) =>
        new(settings.RelayPort, Endpoint, settings.ConnectionMode, @"C:\LiveCue-tests\data", null, null, settings.AsrLabPort, "1.0.0");

    public void Publish(HostEvent hostEvent) => _events.Writer.TryWrite(hostEvent);

    /// <summary>Publishes one relay event the way the supervisor forwards it.</summary>
    public void Forward(RelayEvent relayEvent) => Publish(new RelayEventForwarded(relayEvent.At, relayEvent));

    /// <summary>Every event of a start that works, in the supervisor's order, ending with the inspection.</summary>
    public Task<StartResult> StartsNormally(FakeSupervisor supervisor)
    {
        var options = OptionsFor(Report.SettingsLoad.Settings);
        Publish(new CheckingStarted(Now));
        Publish(new PrerequisitesChecked(Now, Report));
        Publish(new RelayStarting(Now, options));
        Relay ??= new StubRelay(this);
        RunningOptions = options;
        State = SupervisorState.Running;
        Forward(new RelayStarted(Now, Endpoint, options.Port, options.Mode, 2, "1.0.0", PairingWasCreated));
        Publish(new ConnectionInspected(Now, Inspection));
        return Task.FromResult(new StartResult(StartOutcome.Started, Report, null, null));
    }

    public Task<StartResult> StartAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _startCalls);
        CallThreads.Add(Environment.CurrentManagedThreadId);
        return OnStart(this);
    }

    public Task StopAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _stopCalls);
        CallThreads.Add(Environment.CurrentManagedThreadId);
        if (State == SupervisorState.Running)
        {
            State = SupervisorState.Stopped;
            Relay = null;
            RunningOptions = null;
            Forward(new RelayStopped(Now, RelayStopReason.Requested));
        }

        return Task.CompletedTask;
    }

    public Task<PrerequisiteReport> CheckAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _checkCalls);
        CallThreads.Add(Environment.CurrentManagedThreadId);
        Publish(new PrerequisitesChecked(Now, Report));
        return Task.FromResult(Report);
    }

    public Task<ConnectionInspection> InspectConnectionAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _inspectCalls);
        CallThreads.Add(Environment.CurrentManagedThreadId);
        Publish(new ConnectionInspected(Now, Inspection));
        return Task.FromResult(Inspection);
    }

    public void SaveSettings(RelaySettings settings)
    {
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        Saved.Add(settings);
        Settings = settings;
    }

    public Task ResetStoredPairingAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _resetCalls);
        CallThreads.Add(Environment.CurrentManagedThreadId);
        return ResetFailure is null ? Task.CompletedTask : Task.FromException(ResetFailure);
    }

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>The running relay as far as the desktop uses it: pause and pairing.</summary>
internal sealed class StubRelay(FakeSupervisor supervisor) : IRelay
{
    private readonly Channel<RelayEvent> _events = Channel.CreateUnbounded<RelayEvent>();

    public RelayStatus Status => RelayStatus.Running;

    public bool Accepting { get; private set; } = true;

    public int BoundPort => 47841;

    public ChannelReader<RelayEvent> Events => _events.Reader;

    public int ShowPairingCalls => Volatile.Read(ref _showPairingCalls);

    public int ResetPairingCalls => Volatile.Read(ref _resetPairingCalls);

    /// <summary>When set, the pairing calls wait for it.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>When set, every call throws it.</summary>
    public Exception? Failure { get; set; }

    private int _showPairingCalls;
    private int _resetPairingCalls;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public Task SetAcceptingAsync(bool accepting)
    {
        supervisor.CallThreads.Add(Environment.CurrentManagedThreadId);
        if (Failure is not null)
        {
            throw Failure;
        }

        Accepting = accepting;
        supervisor.Forward(new AcceptingChanged(supervisor.Now, accepting));
        return Task.CompletedTask;
    }

    public async Task<PairingQr> ShowPairingAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _showPairingCalls);
        supervisor.CallThreads.Add(Environment.CurrentManagedThreadId);
        if (Gate is not null)
        {
            await Gate.Task.ConfigureAwait(false);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        return Qr();
    }

    public async Task<PairingQr> ResetPairingAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _resetPairingCalls);
        supervisor.CallThreads.Add(Environment.CurrentManagedThreadId);
        if (Gate is not null)
        {
            await Gate.Task.ConfigureAwait(false);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        return Qr();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PairingQr Qr() =>
        new(FakeSupervisor.Endpoint, [.. Enumerable.Range(0, 21).Select(row => Enumerable.Range(0, 21).Select(column => (row + column) % 3 == 0).ToArray())]);
}

/// <summary>A mapping manager that plans from the settings alone and returns the result the test chose.</summary>
internal sealed class FakeMappings : IMappingManager
{
    public Func<MappingPlan, MappingResult>? OnExecute { get; set; }

    public List<MappingPlan> Executed { get; } = [];

    public List<string?> PlannedWithDnsName { get; } = [];

    public System.Collections.Concurrent.ConcurrentBag<int> CallThreads { get; } = [];

    public Task<ConnectionInspection> InspectAsync(RelaySettings settings, CancellationToken ct) =>
        Task.FromResult(FakeSupervisor.InspectionOf(MappingState.Present));

    public MappingPlan? PlanApply(ConnectionMode mode, RelaySettings settings, string? dnsName, out MappingRefusal refusal)
    {
        refusal = MappingRefusal.None;
        PlannedWithDnsName.Add(dnsName);
        var isPublic = mode == ConnectionMode.Public;
        var action = isPublic ? MappingAction.ApplyPublic : MappingAction.ApplyPrivate;
        var command = isPublic
            ? MappingCommand.ApplyPublic(settings.PublicHttpsPort, settings.PublicPathPrefix, settings.RelayPort)
            : MappingCommand.ApplyPrivate(settings.PrivateHttpsPort, settings.RelayPort);
        return new MappingPlan(action, command, "Effect of " + action + ".", Entry: null);
    }

    public MappingPlan PlanRemoval(OwnedMapping entry, string? dnsName)
    {
        PlannedWithDnsName.Add(dnsName);
        var isPublic = entry.Mode == ConnectionMode.Public;
        var action = isPublic ? MappingAction.RemovePublic : MappingAction.RemovePrivate;
        var command = isPublic
            ? MappingCommand.RemovePublic(entry.HttpsPort, entry.Path)
            : MappingCommand.RemovePrivate(entry.HttpsPort);
        return new MappingPlan(action, command, "Effect of " + action + ".", entry);
    }

    public Task<MappingResult> ExecuteAsync(MappingPlan plan, CancellationToken ct)
    {
        CallThreads.Add(Environment.CurrentManagedThreadId);
        Executed.Add(plan);
        if (OnExecute is null)
        {
            throw new InvalidOperationException("No result was scripted.");
        }

        return Task.FromResult(OnExecute(plan));
    }
}

/// <summary>The private and public address forms, without the validation of the real resolver.</summary>
internal sealed class FakeEndpoints : IEndpointResolver
{
    public string? Resolve(RelaySettings settings, string dnsName) => settings.ConnectionMode == ConnectionMode.Private
        ? $"https://{dnsName}:{settings.PrivateHttpsPort}/"
        : $"https://{dnsName}:{settings.PublicHttpsPort}{settings.PublicPathPrefix}/";
}

/// <summary>
/// One real <see cref="DesktopSession"/> over the fakes above, on a real UI thread and a clock the test
/// moves by hand. It records everything the session raises, in order.
/// </summary>
internal sealed class SessionHarness : IDisposable
{
    public SessionHarness()
    {
        Supervisor = new FakeSupervisor(Clock);
        Session = new DesktopSession(Supervisor, Mappings, new FakeEndpoints(), Ui, Clock);
        Session.PropertyChanged += (_, change) => Changes.Add(change.PropertyName!);
        Session.RelayEventReceived += (_, relayEvent) => RelayEvents.Add(relayEvent);
        Session.Signal += (_, signal) => Signals.Add(signal);
        Session.MessageRaised += (_, message) => Messages.Add(message);
        Session.PairingQrProduced += (_, qr) => Qrs.Add(qr);
    }

    public UiThread Ui { get; } = new();

    public ManualTimeProvider Clock { get; } = new();

    public FakeSupervisor Supervisor { get; }

    public FakeMappings Mappings { get; } = new();

    public DesktopSession Session { get; }

    public List<string> Changes { get; } = [];

    public List<RelayEvent> RelayEvents { get; } = [];

    public List<SessionSignal> Signals { get; } = [];

    public List<SessionMessage> Messages { get; } = [];

    public List<PairingQr> Qrs { get; } = [];

    public StubRelay Relay => (StubRelay)Supervisor.Relay!;

    /// <summary>Calls a command on the UI thread, as a view model does, and waits for it there.</summary>
    public Task OnUi(Func<DesktopSession, Task> command) => Ui.RunAsync(() => command(Session));

    public Task<T> OnUi<T>(Func<DesktopSession, Task<T>> command) => Ui.RunAsync(() => command(Session));

    /// <summary>Reads something from the session on the UI thread.</summary>
    public Task<T> Read<T>(Func<DesktopSession, T> read) => Ui.RunAsync(() => read(Session));

    /// <summary>Publishes a host event and waits until the session has handled it.</summary>
    public async Task Publish(HostEvent hostEvent)
    {
        var handled = Ui.Posted;
        Supervisor.Publish(hostEvent);
        await Handled(handled);
    }

    /// <summary>Publishes a relay event and waits until the session has handled it.</summary>
    public Task Forward(RelayEvent relayEvent) => Publish(new RelayEventForwarded(relayEvent.At, relayEvent));

    /// <summary>A started relay, with the connection the test asked for.</summary>
    public async Task StartAsync(MappingState mapping = MappingState.Present)
    {
        Supervisor.Inspection = FakeSupervisor.InspectionOf(mapping);
        await OnUi(session => session.StartAsync());
    }

    public void Dispose()
    {
        Session.Dispose();
        Ui.Dispose();
    }

    /// <summary>The event was posted to the UI thread after <paramref name="postedBefore"/>, and has run.</summary>
    private async Task Handled(int postedBefore)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (Ui.Posted == postedBefore)
        {
            Assert.True(DateTime.UtcNow < until, "The session did not take the event in time.");
            await Task.Delay(1);
        }

        // Whatever was posted before this marker has run when the marker runs.
        await Ui.RunAsync(() => { });
    }
}
