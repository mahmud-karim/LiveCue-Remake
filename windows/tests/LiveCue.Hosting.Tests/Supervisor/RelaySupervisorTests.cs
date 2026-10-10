using System.Net;
using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.TestSupport;

namespace LiveCue.Hosting.Tests.Supervisor;

/// <summary>
/// The supervisor of architecture 3.3 and 3.11 with the step order of W-design 4.16, over the
/// foundation's hosting parts and a fake relay. Rewritten from the spike's supervisor tests for the new
/// contract; work package WP-W06 owns this file.
/// </summary>
public sealed class RelaySupervisorTests
{
    private const string DataDirectory = @"C:\LiveCue-tests\data";
    private const string Endpoint = "https://pc.example.test:11443/";

    [Fact]
    public async Task A_start_publishes_the_events_in_order()
    {
        var hosting = new Hosting(new RelaySettings { RelayPort = 50123, ConnectionMode = ConnectionMode.Public });

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.Started, result.Outcome);
        Assert.Null(result.FailureText);
        Assert.Equal(SupervisorState.Running, hosting.Supervisor.State);
        Assert.Collection(
            events,
            first => Assert.IsType<CheckingStarted>(first),
            second => Assert.True(Assert.IsType<PrerequisitesChecked>(second).Report.Passed),
            third => Assert.Equal(50123, Assert.IsType<RelayStarting>(third).Options.Port),
            fourth => Assert.IsType<RelayStarted>(Assert.IsType<RelayEventForwarded>(fourth).Event));

        var options = hosting.Relays.Current.Options;
        Assert.Same(hosting.Relays.Current, hosting.Supervisor.Relay);
        Assert.Equal(options, hosting.Supervisor.RunningOptions);
        Assert.Equal(50123, options.Port);
        Assert.Equal(ConnectionMode.Public, options.Mode);
        Assert.Equal(Endpoint, options.AdvertisedEndpoint);
        Assert.Equal(DataDirectory, options.DataDirectory);
        Assert.Equal("1.0.0", options.RelayVersion);
        Assert.Null(options.CodexExecutablePath);
        Assert.Null(options.LabFolder);
        Assert.Equal(47842, options.LabPort);
        Assert.True(hosting.Files.DirectoryExists(DataDirectory), "Check 1 creates the data folder.");
    }

    [Fact]
    public async Task Events_of_the_running_relay_are_forwarded_unchanged_and_stop_ends_with_relay_stopped()
    {
        var hosting = new Hosting();
        await hosting.Supervisor.StartAsync(CancellationToken.None);
        hosting.DrainEvents();
        var relay = hosting.Relays.Current;

        relay.Raise(new PhoneSeen(FakeRelay.Time.AddSeconds(1)));
        relay.Raise(new PhoneSeen(FakeRelay.Time.AddSeconds(2)));
        await hosting.Supervisor.StopAsync(CancellationToken.None);
        await hosting.Supervisor.StopAsync(CancellationToken.None);
        var forwarded = hosting.DrainEvents().Cast<RelayEventForwarded>().Select(hostEvent => hostEvent.Event).ToList();

        Assert.Equal(
            [new PhoneSeen(FakeRelay.Time.AddSeconds(1)), new PhoneSeen(FakeRelay.Time.AddSeconds(2)), new RelayStopped(FakeRelay.Time, RelayStopReason.Requested)],
            forwarded);
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Null(hosting.Supervisor.Relay);
        Assert.Null(hosting.Supervisor.RunningOptions);
        Assert.True(relay.Disposed);
        Assert.Equal(1, relay.StopCalls);
    }

    [Fact]
    public async Task A_failed_check_creates_no_relay()
    {
        // No endpoint override: the foundation's Tailscale CLI reports "not installed", so check 4 fails.
        var hosting = new Hosting(overrides: new RelayOverrides());

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.PrerequisiteFailed, result.Outcome);
        Assert.Equal("Tailscale is not installed in Program Files.", result.FailureText);
        Assert.Equal(PrerequisiteId.TailscaleInstalled, result.Report!.FirstFailure!.Id);
        Assert.Equal(
            [PrerequisiteState.Passed, PrerequisiteState.Passed, PrerequisiteState.Passed, PrerequisiteState.Failed, PrerequisiteState.NotChecked, PrerequisiteState.NotChecked],
            result.Report.Results.Select(check => check.State));
        Assert.Equal(Enumerable.Range(1, 6), result.Report.Results.Select(check => (int)check.Id));
        Assert.Empty(hosting.Relays.Created);
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Equal([typeof(CheckingStarted), typeof(PrerequisitesChecked)], events.Select(hostEvent => hostEvent.GetType()));
    }

    [Fact]
    public async Task A_data_folder_that_cannot_be_opened_fails_check_1_and_leaves_the_rest_unchecked()
    {
        var hosting = new Hosting();
        hosting.Files.CreateDirectoryFails = true;

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);

        Assert.Equal(StartOutcome.PrerequisiteFailed, result.Outcome);
        Assert.Equal("LiveCue could not open its data folder.", result.FailureText);
        Assert.All(result.Report!.Results.Skip(1), check => Assert.Equal(PrerequisiteState.NotChecked, check.State));
    }

    [Theory]
    [InlineData(RelayFaultCode.PortInUse, "Port 50123 is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay.")]
    [InlineData(RelayFaultCode.PairingStoreUnreadable, "The saved pairing could not be read. Reset pairing to create a new one.")]
    [InlineData(RelayFaultCode.StartFailed, "Could not start the relay.")]
    [InlineData(RelayFaultCode.InvalidEndpoint, "Could not start the relay.")]
    public async Task Start_faults_map_to_their_texts(RelayFaultCode code, string text)
    {
        var hosting = new Hosting(new RelaySettings { RelayPort = 50123 });
        hosting.Relays.NextStartFault = code;

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.RelayFault, result.Outcome);
        Assert.Equal(code, result.Fault);
        Assert.Equal(text, result.FailureText);
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Null(hosting.Supervisor.Relay);
        Assert.True(hosting.Relays.Current.Disposed);
        Assert.Equal(code, Assert.IsType<RelayFaulted>(Assert.IsType<RelayEventForwarded>(events[^1]).Event).Code);
    }

    [Fact]
    public async Task Forbidden_port_never_reaches_the_relay()
    {
        // A store without a validator, so the test can hold what no file could.
        var hosting = new Hosting(new RelaySettings { RelayPort = 47831 });

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.RelayFault, result.Outcome);
        Assert.Equal(RelayFaultCode.StartFailed, result.Fault);
        Assert.Empty(hosting.Relays.Created);
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Equal(RelayFaultCode.StartFailed, Assert.IsType<RelayFaulted>(Assert.IsType<RelayEventForwarded>(events[^1]).Event).Code);
    }

    [Fact]
    public async Task One_instance_per_start()
    {
        var hosting = new Hosting();

        var first = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var second = await hosting.Supervisor.StartAsync(CancellationToken.None);
        await hosting.Supervisor.StopAsync(CancellationToken.None);
        var third = await hosting.Supervisor.StartAsync(CancellationToken.None);

        Assert.Equal(StartOutcome.Started, first.Outcome);
        Assert.Equal(StartOutcome.AlreadyRunning, second.Outcome);
        Assert.Equal(StartOutcome.Started, third.Outcome);
        Assert.Equal(2, hosting.Relays.Created.Count);
        Assert.NotSame(hosting.Relays.Created[0], hosting.Relays.Created[1]);
        Assert.Equal(SupervisorState.Running, hosting.Supervisor.State);
    }

    [Fact]
    public async Task A_runtime_fault_stops_and_does_not_restart()
    {
        var hosting = new Hosting();
        await hosting.Supervisor.StartAsync(CancellationToken.None);
        var relay = hosting.Relays.Current;

        relay.FaultWhileRunning();
        await WaitForAsync(() => hosting.Supervisor.State == SupervisorState.Stopped && relay.Disposed);
        var forwarded = hosting.DrainEvents().OfType<RelayEventForwarded>().Select(hostEvent => hostEvent.Event).ToList();

        Assert.Equal(RelayFaultCode.Internal, forwarded.OfType<RelayFaulted>().Single().Code);
        Assert.Equal(RelayStopReason.Fault, forwarded.OfType<RelayStopped>().Single().Reason);
        Assert.Null(hosting.Supervisor.Relay);
        Assert.Single(hosting.Relays.Created);
    }

    [Fact]
    public async Task Stored_reset_needs_a_stopped_relay()
    {
        var hosting = new Hosting();

        await hosting.Supervisor.ResetStoredPairingAsync(CancellationToken.None);
        await hosting.Supervisor.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hosting.Supervisor.ResetStoredPairingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Saving_settings_reloads_them_and_does_not_touch_the_running_relay()
    {
        var hosting = new Hosting();
        await hosting.Supervisor.StartAsync(CancellationToken.None);

        hosting.Supervisor.SaveSettings(new RelaySettings { RelayPort = 50999 });

        Assert.Equal(50999, hosting.Supervisor.Settings.RelayPort);
        Assert.Equal(47841, hosting.Supervisor.RunningOptions!.Port);
        Assert.Equal(1, hosting.Store.Saves);
        Assert.Single(hosting.Relays.Created);
    }

    [Fact]
    public async Task A_check_and_an_inspection_publish_their_events_and_do_not_change_the_state()
    {
        var hosting = new Hosting(overrides: new RelayOverrides());

        var report = await hosting.Supervisor.CheckAsync(CancellationToken.None);
        var inspection = await hosting.Supervisor.InspectConnectionAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.False(report.Passed);
        Assert.Equal(MappingState.Unknown, inspection.Private.State);
        Assert.Equal(MappingRefusal.TailscaleNotInstalled, inspection.Private.BlockedBy);
        Assert.Equal([typeof(PrerequisitesChecked), typeof(ConnectionInspected)], events.Select(hostEvent => hostEvent.GetType()));
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
    }

    [Fact]
    public async Task No_flow_runs_a_tailscale_write()
    {
        var processes = new LiveCue.Relay.Tests.Support.FakeProcessRunner();
        var hosting = new Hosting(processes: processes);

        await hosting.Supervisor.StartAsync(CancellationToken.None);
        hosting.Relays.Current.FaultWhileRunning();
        await WaitForAsync(() => hosting.Supervisor.State == SupervisorState.Stopped);
        await hosting.Supervisor.StartAsync(CancellationToken.None);
        hosting.Supervisor.SaveSettings(new RelaySettings { ConnectionMode = ConnectionMode.Public });
        await hosting.Supervisor.InspectConnectionAsync(CancellationToken.None);
        await hosting.Supervisor.StopAsync(CancellationToken.None);
        await hosting.Supervisor.DisposeAsync();

        // Rule TS-9: start, stop, a fault, a settings save and dispose start no process at all.
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public async Task With_the_real_relay_the_supervisor_binds_loopback_and_stops_cleanly()
    {
        using var data = new TempFolder();
        var services = HostingFactory.Create(new SupervisorOptions(
            data.Path, "1.0.0", new RelayOverrides(Endpoint: Endpoint, Port: 0)));
        await using var supervisor = services.Supervisor;

        var result = await supervisor.StartAsync(CancellationToken.None);
        var port = supervisor.Relay!.BoundPort;
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        using var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));
        await supervisor.StopAsync(CancellationToken.None);

        // Nobody here knows the token, so the honest answer is 401; it proves Kestrel is serving.
        Assert.Equal(StartOutcome.Started, result.Outcome);
        Assert.InRange(port, 1024, 65535);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SupervisorState.Stopped, supervisor.State);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "The supervisor did not reach the expected state in time.");
            await Task.Delay(10);
        }
    }

    /// <summary>The foundation's hosting library over fakes: in-memory settings, a fake file system and fake relays.</summary>
    private sealed class Hosting
    {
        public Hosting(
            RelaySettings? settings = null,
            RelayOverrides? overrides = null,
            LiveCue.Relay.Tests.Support.FakeProcessRunner? processes = null)
        {
            Store = new InMemorySettingsStore(settings);
            Services = HostingFactory.Create(
                new SupervisorOptions(DataDirectory, "1.0.0", overrides ?? new RelayOverrides(Endpoint: Endpoint)),
                new HostingOverrides
                {
                    Files = Files,
                    Settings = Store,
                    RelayFactory = Relays,
                    Processes = processes,
                });
        }

        public HostingServices Services { get; }

        public IRelaySupervisor Supervisor => Services.Supervisor;

        public InMemorySettingsStore Store { get; }

        public FakeHostFileSystem Files { get; } = new();

        public FakeRelayFactory Relays { get; } = new();

        /// <summary>Every host event published so far and not yet drained.</summary>
        public List<HostEvent> DrainEvents()
        {
            var events = new List<HostEvent>();
            while (Supervisor.Events.TryRead(out var hostEvent))
            {
                events.Add(hostEvent);
            }

            return events;
        }
    }
}
