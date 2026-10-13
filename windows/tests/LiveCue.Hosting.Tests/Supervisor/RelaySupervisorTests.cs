using System.Net;
using System.Threading.Channels;
using LiveCue.Hosting.Tests.Prerequisites;
using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Hosting.Tests.Supervisor;

/// <summary>
/// The supervisor of architecture 3.3 and 3.11 with the step order of W-design 4.16 (test plan 2.3, and
/// ST-08 and ST-10 of section 4.6). The relay is a fake that binds nothing; the prerequisite checks and
/// the mapping manager are scripted, so each test says exactly what the outside world looks like.
/// </summary>
public sealed class RelaySupervisorTests
{
    private const string DataDirectory = @"C:\LiveCue-tests\data";
    private const string GivenEndpoint = "https://given.example.test/";
    private const string PrivateEndpoint = "https://pc.example.test:11443/";

    private static readonly OwnedMapping PrivateEntry = new(
        ConnectionMode.Private, 11443, "/", "http://127.0.0.1:47841", new DateTimeOffset(2026, 10, 10, 8, 15, 30, TimeSpan.Zero));

    [Fact]
    public async Task A_start_publishes_the_events_in_order()
    {
        var settings = new RelaySettings { RelayPort = 50123, ConnectionMode = ConnectionMode.Public, AsrLabPort = 50124 };
        var hosting = new Hosting(settings);

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.Started, result.Outcome);
        Assert.Null(result.FailureText);
        Assert.Null(result.Fault);
        Assert.True(result.Report!.Passed);
        Assert.Equal(SupervisorState.Running, hosting.Supervisor.State);
        Assert.Collection(
            events,
            first => Assert.IsType<CheckingStarted>(first),
            second => Assert.Same(result.Report, Assert.IsType<PrerequisitesChecked>(second).Report),
            third => Assert.Equal(hosting.Relays.Current.Options, Assert.IsType<RelayStarting>(third).Options),
            fourth => Assert.IsType<RelayStarted>(Assert.IsType<RelayEventForwarded>(fourth).Event),
            fifth => Assert.Equal(TailscaleReadFailure.None, Assert.IsType<ConnectionInspected>(fifth).Inspection.StatusFailure));

        // Step 4: what the relay is told.
        var options = hosting.Relays.Current.Options;
        Assert.Same(hosting.Relays.Current, hosting.Supervisor.Relay);
        Assert.Equal(options, hosting.Supervisor.RunningOptions);
        Assert.Equal(50123, options.Port);
        Assert.Equal(ConnectionMode.Public, options.Mode);
        Assert.Equal("https://pc.example.test:10000/livecue/", options.AdvertisedEndpoint);
        Assert.Equal(DataDirectory, options.DataDirectory);
        Assert.Equal(ScriptedPrerequisites.CodexPath, options.CodexExecutablePath);
        Assert.Null(options.LabFolder);
        Assert.Equal(50124, options.LabPort);
        Assert.Equal("1.0.0", options.RelayVersion);

        // Step 2: the loaded settings were adopted, and the checks ran once, with Tailscale.
        Assert.True(hosting.Supervisor.Settings.SameValues(settings));
        var check = Assert.Single(hosting.Prerequisites.Calls);
        Assert.Equal(new PrerequisiteOptions(DataDirectory, CodexPathOverride: null, SkipTailscale: false), check);

        // Step 8 is the only inspection in Public mode, and it is about the settings in force.
        Assert.True(Assert.Single(hosting.Mappings.Inspections).SameValues(settings));
        Assert.Equal(0, hosting.Mappings.Executions);
    }

    [Fact]
    public async Task A_private_start_inspects_before_the_relay_exists_and_publishes_only_the_last_inspection()
    {
        var hosting = new Hosting();

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.Started, result.Outcome);
        Assert.Equal(PrivateEndpoint, hosting.Relays.Current.Options.AdvertisedEndpoint);
        Assert.Equal(ConnectionMode.Private, hosting.Relays.Current.Options.Mode);

        // Check 6a and step 8 both inspect; a check 6a that finds nothing publishes nothing.
        Assert.Equal(2, hosting.Mappings.Inspections.Count);
        Assert.Equal(
            [typeof(CheckingStarted), typeof(PrerequisitesChecked), typeof(RelayStarting), typeof(RelayEventForwarded), typeof(ConnectionInspected)],
            events.Select(hostEvent => hostEvent.GetType()));
    }

    [Fact]
    public async Task The_overrides_of_a_run_replace_the_settings_and_an_endpoint_leaves_tailscale_out()
    {
        var files = new FakeHostFileSystem()
            .AddFile(@"C:\Given\asr\Start-ASR-Lab.ps1")
            .AddFile(@"C:\Given\asr\Stop-ASR-Lab.ps1");
        var hosting = new Hosting(
            overrides: new RelayOverrides(GivenEndpoint, 50200, @"C:\Given\codex.exe", @"C:\Given\asr", ConnectionMode.Public),
            files: files);

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.Started, result.Outcome);
        var options = hosting.Relays.Current.Options;
        Assert.Equal(GivenEndpoint, options.AdvertisedEndpoint);
        Assert.Equal(50200, options.Port);
        Assert.Equal(ConnectionMode.Public, options.Mode);
        Assert.Equal(@"C:\Given\codex.exe", options.CodexExecutablePath);
        Assert.Equal(@"C:\Given\asr", options.LabFolder);

        // With an endpoint the Tailscale checks are skipped and no mapping is inspected or reported.
        Assert.Equal(
            new PrerequisiteOptions(DataDirectory, @"C:\Given\codex.exe", SkipTailscale: true),
            Assert.Single(hosting.Prerequisites.Calls));
        Assert.Empty(hosting.Mappings.Inspections);
        Assert.DoesNotContain(events, hostEvent => hostEvent is ConnectionInspected);

        // A lab folder given for the run is held to the same rule as the setting.
        var offDrive = new Hosting(
            overrides: new RelayOverrides(GivenEndpoint, LabFolder: @"\\server\share\asr"),
            files: new FakeHostFileSystem()
                .AddFile(@"\\server\share\asr\Start-ASR-Lab.ps1")
                .AddFile(@"\\server\share\asr\Stop-ASR-Lab.ps1"));
        await offDrive.Supervisor.StartAsync(CancellationToken.None);
        Assert.Null(offDrive.Relays.Current.Options.LabFolder);
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
        var forwarded = hosting.DrainEvents().Cast<RelayEventForwarded>().ToList();

        Assert.Equal(
            [new PhoneSeen(FakeRelay.Time.AddSeconds(1)), new PhoneSeen(FakeRelay.Time.AddSeconds(2)), new RelayStopped(FakeRelay.Time, RelayStopReason.Requested)],
            forwarded.Select(hostEvent => hostEvent.Event));
        Assert.All(forwarded, hostEvent => Assert.Equal(hostEvent.Event.At, hostEvent.At));
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Null(hosting.Supervisor.Relay);
        Assert.Null(hosting.Supervisor.RunningOptions);
        Assert.True(relay.Disposed);
        Assert.Equal(1, relay.StopCalls);
    }

    [Theory]
    [InlineData(PrerequisiteId.DataFolder)]
    [InlineData(PrerequisiteId.CodexFound)]
    [InlineData(PrerequisiteId.CodexSignedIn)]
    [InlineData(PrerequisiteId.TailscaleInstalled)]
    [InlineData(PrerequisiteId.TailscaleOnline)]
    [InlineData(PrerequisiteId.TailscaleDnsName)]
    public async Task A_failed_check_creates_no_relay(PrerequisiteId failing)
    {
        var hosting = new Hosting();
        hosting.Prerequisites.Fails = failing;

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.PrerequisiteFailed, result.Outcome);
        Assert.Equal(PrerequisiteTexts.Failure(failing), result.FailureText);
        Assert.Equal(failing, result.Report!.FirstFailure!.Id);
        Assert.Null(result.Fault);
        Assert.Empty(hosting.Relays.Created);
        Assert.Null(hosting.Supervisor.Relay);
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Equal([typeof(CheckingStarted), typeof(PrerequisitesChecked)], events.Select(hostEvent => hostEvent.GetType()));

        // Nothing of Tailscale's configuration is read for a start that ends at the checks.
        Assert.Empty(hosting.Mappings.Inspections);

        // The next start checks again and, with the cause gone, runs.
        hosting.Prerequisites.Fails = null;
        Assert.Equal(StartOutcome.Started, (await hosting.Supervisor.StartAsync(CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData(RelayFaultCode.PortInUse, "Port 50123 is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay.")]
    [InlineData(RelayFaultCode.PairingStoreUnreadable, "The saved pairing could not be read. Reset pairing to create a new one.")]
    [InlineData(RelayFaultCode.StartFailed, "Could not start the relay.")]
    [InlineData(RelayFaultCode.InvalidEndpoint, "Could not start the relay.")]
    [InlineData(RelayFaultCode.Internal, "Could not start the relay.")]
    public async Task Start_faults_map_to_their_texts(RelayFaultCode code, string text)
    {
        var hosting = new Hosting(new RelaySettings { RelayPort = 50123 }, new RelayOverrides(GivenEndpoint));
        hosting.Relays.NextStartFault = code;

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.RelayFault, result.Outcome);
        Assert.Equal(code, result.Fault);
        Assert.Equal(text, result.FailureText);
        Assert.NotNull(result.Report);
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Null(hosting.Supervisor.Relay);
        Assert.Null(hosting.Supervisor.RunningOptions);
        Assert.True(hosting.Relays.Current.Disposed);

        // The relay's own fault event is forwarded, after RelayStarting and as the last event.
        Assert.Equal(
            [typeof(CheckingStarted), typeof(PrerequisitesChecked), typeof(RelayStarting), typeof(RelayEventForwarded)],
            events.Select(hostEvent => hostEvent.GetType()));
        Assert.Equal(code, Assert.IsType<RelayFaulted>(Assert.IsType<RelayEventForwarded>(events[^1]).Event).Code);

        // A fault is not sticky: the next start makes a new instance.
        Assert.Equal(StartOutcome.Started, (await hosting.Supervisor.StartAsync(CancellationToken.None)).Outcome);
        Assert.Equal(2, hosting.Relays.Created.Count);
    }

    [Fact]
    public async Task Forbidden_port_never_reaches_the_relay()
    {
        // From the settings (a store without a validator can hold what no file could) and from an override.
        var fromSettings = new Hosting(new RelaySettings { RelayPort = 47831 });
        var fromOverride = new Hosting(overrides: new RelayOverrides(Port: 47831));
        var withEndpoint = new Hosting(overrides: new RelayOverrides(GivenEndpoint, 47831));

        foreach (var hosting in (Hosting[])[fromSettings, fromOverride, withEndpoint])
        {
            var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
            var events = hosting.DrainEvents();

            Assert.Equal(StartOutcome.RelayFault, result.Outcome);
            Assert.Equal(RelayFaultCode.StartFailed, result.Fault);
            Assert.Equal("Could not start the relay.", result.FailureText);
            Assert.Empty(hosting.Relays.Created);
            Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);

            // A host can follow events alone: the fault is published in the shape a relay's own has.
            var faulted = Assert.IsType<RelayFaulted>(Assert.IsType<RelayEventForwarded>(events[^1]).Event);
            Assert.Equal(RelayFaultCode.StartFailed, faulted.Code);
            Assert.Equal("Could not start the relay.", faulted.Message);
            Assert.DoesNotContain(events, hostEvent => hostEvent is RelayStarting);
        }
    }

    [Fact]
    public async Task A_start_without_a_valid_endpoint_is_a_fault_and_creates_no_relay()
    {
        // A prefix that no file could hold: the endpoint built from it fails the grammar of protocol 4.3.
        var hosting = new Hosting(new RelaySettings { ConnectionMode = ConnectionMode.Public, PublicPathPrefix = "/not a prefix" });

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.RelayFault, result.Outcome);
        Assert.Equal(RelayFaultCode.InvalidEndpoint, result.Fault);
        Assert.Equal("Could not start the relay.", result.FailureText);
        Assert.Empty(hosting.Relays.Created);
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Equal(RelayFaultCode.InvalidEndpoint, Assert.IsType<RelayFaulted>(Assert.IsType<RelayEventForwarded>(events[^1]).Event).Code);
    }

    [Fact]
    public async Task No_flow_runs_a_tailscale_write()
    {
        // The whole hosting library as it is composed, over a scripted Tailscale CLI that records every
        // command and a process runner that records every process. Rule TS-9: start, stop, a fault, a
        // settings save and dispose run zero write commands.
        using var data = new TempFolder();
        var files = new FakeHostFileSystem().SetEnvironment("PATH", @"C:\Tools").AddFile(@"C:\Tools\codex.exe");
        var processes = new FakeProcessRunner();
        var tailscale = new ScriptedTailscaleCli();
        var relays = new FakeRelayFactory();
        var services = HostingFactory.Create(
            new SupervisorOptions(data.Path, "1.0.0"),
            new HostingOverrides
            {
                Files = files,
                Settings = new InMemorySettingsStore(),
                Processes = processes,
                Tailscale = tailscale,
                RelayFactory = relays,
            });
        var supervisor = services.Supervisor;

        Assert.Equal(StartOutcome.Started, (await supervisor.StartAsync(CancellationToken.None)).Outcome);
        relays.Current.FaultWhileRunning();
        await WaitForAsync(() => supervisor.State == SupervisorState.Stopped);
        relays.NextStartFault = RelayFaultCode.PortInUse;
        Assert.Equal(StartOutcome.RelayFault, (await supervisor.StartAsync(CancellationToken.None)).Outcome);
        Assert.Equal(StartOutcome.Started, (await supervisor.StartAsync(CancellationToken.None)).Outcome);
        supervisor.SaveSettings(new RelaySettings { ConnectionMode = ConnectionMode.Public });
        await supervisor.CheckAsync(CancellationToken.None);
        await supervisor.InspectConnectionAsync(CancellationToken.None);
        await supervisor.StopAsync(CancellationToken.None);
        await supervisor.ResetStoredPairingAsync(CancellationToken.None);
        Assert.Equal(StartOutcome.Started, (await supervisor.StartAsync(CancellationToken.None)).Outcome);
        await supervisor.DisposeAsync();

        Assert.Empty(tailscale.Writes);

        // Every process that was asked for is one of the two Codex questions; none is a Tailscale command.
        Assert.NotEmpty(processes.Requests);
        Assert.All(processes.Requests, request =>
        {
            Assert.Equal(@"C:\Tools\codex.exe", request.FileName);
            Assert.Contains(string.Join(' ', request.Arguments), (string[])["login status", "--version"]);
        });

        // The same flows over a scripted mapping manager: it was asked to inspect, never to execute.
        var hosting = new Hosting();
        await hosting.Supervisor.StartAsync(CancellationToken.None);
        hosting.Relays.Current.FaultWhileRunning();
        await WaitForAsync(() => hosting.Supervisor.State == SupervisorState.Stopped);
        hosting.Relays.NextStartFault = RelayFaultCode.PortInUse;
        await hosting.Supervisor.StartAsync(CancellationToken.None);
        await hosting.Supervisor.StartAsync(CancellationToken.None);
        hosting.Supervisor.SaveSettings(new RelaySettings { ConnectionMode = ConnectionMode.Public });
        await hosting.Supervisor.InspectConnectionAsync(CancellationToken.None);
        await hosting.Supervisor.StopAsync(CancellationToken.None);
        await hosting.Supervisor.DisposeAsync();
        Assert.NotEmpty(hosting.Mappings.Inspections);
        Assert.Equal(0, hosting.Mappings.Executions);
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
        Assert.Null(hosting.Supervisor.RunningOptions);

        // Nothing restarts it: there is still one instance, and a stop has nothing to do.
        await Task.Delay(50);
        await hosting.Supervisor.StopAsync(CancellationToken.None);
        Assert.Single(hosting.Relays.Created);
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Equal(0, relay.StopCalls);

        // A start asked for by the user makes a new instance.
        Assert.Equal(StartOutcome.Started, (await hosting.Supervisor.StartAsync(CancellationToken.None)).Outcome);
        Assert.Equal(2, hosting.Relays.Created.Count);
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
        Assert.Equal(new StartResult(StartOutcome.AlreadyRunning, null, null, null), second);
        Assert.Equal(StartOutcome.Started, third.Outcome);
        Assert.Equal(2, hosting.Relays.Created.Count);
        Assert.NotSame(hosting.Relays.Created[0], hosting.Relays.Created[1]);
        Assert.True(hosting.Relays.Created[0].Disposed);
        Assert.True(hosting.Relays.Current.Accepting, "A restarted relay is a new instance and is always accepting.");
        Assert.Equal(SupervisorState.Running, hosting.Supervisor.State);

        // The second start did nothing at all: no check, no event.
        Assert.Equal(2, hosting.Prerequisites.Calls.Count);
    }

    [Fact]
    public async Task A_second_start_while_the_first_is_still_checking_is_already_running()
    {
        var hosting = new Hosting();
        hosting.Prerequisites.Gate = new TaskCompletionSource();

        var first = hosting.Supervisor.StartAsync(CancellationToken.None);
        await WaitForAsync(() => hosting.Supervisor.State == SupervisorState.Checking && hosting.Prerequisites.Calls.Count == 1);
        var second = await hosting.Supervisor.StartAsync(CancellationToken.None);
        hosting.Prerequisites.Gate.SetResult();

        Assert.Equal(StartOutcome.AlreadyRunning, second.Outcome);
        Assert.Equal(StartOutcome.Started, (await first).Outcome);
        Assert.Single(hosting.Relays.Created);
        Assert.Single(hosting.Prerequisites.Calls);
    }

    [Fact]
    public async Task Stored_reset_needs_a_stopped_relay()
    {
        using var data = new TempFolder();
        var pairingFile = data.Combine("pairing.json");
        var hosting = new Hosting(dataDirectory: data.Path);

        // Stopped: the stored pairing is replaced (here: created) in the data folder.
        await hosting.Supervisor.ResetStoredPairingAsync(CancellationToken.None);
        var first = await File.ReadAllBytesAsync(pairingFile);

        await hosting.Supervisor.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hosting.Supervisor.ResetStoredPairingAsync(CancellationToken.None));
        Assert.Equal(first, await File.ReadAllBytesAsync(pairingFile));
        Assert.Equal(SupervisorState.Running, hosting.Supervisor.State);

        await hosting.Supervisor.StopAsync(CancellationToken.None);
        await hosting.Supervisor.ResetStoredPairingAsync(CancellationToken.None);
        Assert.NotEqual(first, await File.ReadAllBytesAsync(pairingFile));
        Assert.Equal(["pairing.json"], data.FileNames());
    }

    public static TheoryData<string, int, string, bool> FunnelHandlers => new()
    {
        // (a) At the configured public port and prefix, and not in the ledger.
        { "the public mount, not in the ledger", 10000, "/livecue", false },

        // (b) On another port and mount.
        { "another port and mount", 8443, "/elsewhere", false },

        // (c) On the private port, after Funnel was switched on there. The ledger still has the entry.
        { "the private port with Funnel on", 11443, "/", true },
    };

    [Theory]
    [MemberData(nameof(FunnelHandlers))]
    public async Task Private_mode_does_not_start_while_a_funnel_handler_targets_the_port(
        string name, int httpsPort, string mount, bool inLedger)
    {
        Assert.NotEmpty(name);
        var hosting = new Hosting(new RelaySettings { OwnedMappings = inLedger ? [PrivateEntry] : [] });
        var handler = new ExposedHandler(httpsPort, mount, FunnelOn: true, Foreground: false, inLedger ? PrivateEntry : null);

        // A tailnet-only handler to the relay port is listed with it; it is not what refuses the start.
        hosting.Mappings.Exposure = [new ExposedHandler(12443, "/", FunnelOn: false, Foreground: false, null), handler];

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.ExposedInPrivateMode, result.Outcome);
        Assert.Equal(
            "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode.",
            result.FailureText);
        Assert.Null(result.Fault);
        Assert.True(result.Report!.Passed);

        // No relay instance was created, and the supervisor is stopped.
        Assert.Empty(hosting.Relays.Created);
        Assert.Null(hosting.Supervisor.Relay);
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);

        // The inspection that showed the exposure is published, and it is the last event.
        Assert.Equal(
            [typeof(CheckingStarted), typeof(PrerequisitesChecked), typeof(ConnectionInspected)],
            events.Select(hostEvent => hostEvent.GetType()));
        var inspection = Assert.IsType<ConnectionInspected>(events[^1]).Inspection;
        Assert.True(inspection.HasFunnelExposure);
        Assert.Contains(handler, inspection.Exposure);
        Assert.Single(hosting.Mappings.Inspections);

        // Once the handler is gone the same supervisor starts.
        hosting.Mappings.Exposure = [new ExposedHandler(12443, "/", FunnelOn: false, Foreground: false, null)];
        Assert.Equal(StartOutcome.Started, (await hosting.Supervisor.StartAsync(CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Public_mode_and_an_endpoint_override_skip_the_check()
    {
        ExposedHandler[] exposure = [new(10000, "/livecue", FunnelOn: true, Foreground: false, null)];

        // Public mode: a Funnel handler to the relay port is the point of the mode.
        var publicMode = new Hosting(new RelaySettings { ConnectionMode = ConnectionMode.Public });
        publicMode.Mappings.Exposure = exposure;
        Assert.Equal(StartOutcome.Started, (await publicMode.Supervisor.StartAsync(CancellationToken.None)).Outcome);
        Assert.Single(publicMode.Mappings.Inspections);

        // Public by an override of the run, over Private in the settings.
        var publicByOverride = new Hosting(overrides: new RelayOverrides(Mode: ConnectionMode.Public));
        publicByOverride.Mappings.Exposure = exposure;
        Assert.Equal(StartOutcome.Started, (await publicByOverride.Supervisor.StartAsync(CancellationToken.None)).Outcome);

        // An endpoint override: no mapping is involved, so none is inspected, in either mode.
        var withEndpoint = new Hosting(overrides: new RelayOverrides(GivenEndpoint));
        withEndpoint.Mappings.Exposure = exposure;
        Assert.Equal(StartOutcome.Started, (await withEndpoint.Supervisor.StartAsync(CancellationToken.None)).Outcome);
        Assert.Equal(ConnectionMode.Private, withEndpoint.Relays.Current.Options.Mode);
        Assert.Empty(withEndpoint.Mappings.Inspections);

        // Private by an override of the run, over Public in the settings: the check applies.
        var privateByOverride = new Hosting(
            new RelaySettings { ConnectionMode = ConnectionMode.Public },
            new RelayOverrides(Mode: ConnectionMode.Private));
        privateByOverride.Mappings.Exposure = exposure;
        Assert.Equal(
            StartOutcome.ExposedInPrivateMode,
            (await privateByOverride.Supervisor.StartAsync(CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData(TailscaleReadFailure.Failed)]
    [InlineData(TailscaleReadFailure.TimedOut)]
    [InlineData(TailscaleReadFailure.Unparseable)]
    [InlineData(TailscaleReadFailure.NotInstalled)]
    public async Task An_unreadable_status_does_not_stop_a_private_start(TailscaleReadFailure failure)
    {
        var hosting = new Hosting();
        hosting.Mappings.StatusFailure = failure;

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.Started, result.Outcome);
        Assert.Equal(SupervisorState.Running, hosting.Supervisor.State);

        // The one inspection that is published is step 8's, and it says "unknown", not "nothing".
        var inspection = Assert.IsType<ConnectionInspected>(Assert.Single(events, hostEvent => hostEvent is ConnectionInspected)).Inspection;
        Assert.Equal(failure, inspection.StatusFailure);
        Assert.Empty(inspection.Exposure);
        Assert.Equal(MappingState.Unknown, inspection.Private.State);
    }

    [Fact]
    public async Task Port_in_use_with_a_mapping_to_that_port_carries_the_reset_advice()
    {
        // A tailnet-only mapping to the relay port: it does not stop a Private start, and it is what
        // makes a taken port serious.
        var hosting = new Hosting(new RelaySettings { RelayPort = 50123, OwnedMappings = [PrivateEntry] });
        hosting.Mappings.Exposure = [new ExposedHandler(11443, "/", FunnelOn: false, Foreground: false, PrivateEntry)];
        hosting.Relays.NextStartFault = RelayFaultCode.PortInUse;

        var result = await hosting.Supervisor.StartAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.Equal(StartOutcome.RelayFault, result.Outcome);
        Assert.Equal(RelayFaultCode.PortInUse, result.Fault);
        Assert.Equal(
            "Port 50123 is in use by another program while a Tailscale mapping points at it. That program may have received your iPhone's requests, including the pairing token. Free the port, start the relay, then reset pairing.",
            result.FailureText);
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.True(hosting.Relays.Current.Disposed);

        // The relay's own fault event first, unchanged; then the inspection that the text rests on.
        Assert.Equal(
            [typeof(CheckingStarted), typeof(PrerequisitesChecked), typeof(RelayStarting), typeof(RelayEventForwarded), typeof(ConnectionInspected)],
            events.Select(hostEvent => hostEvent.GetType()));
        var faulted = Assert.IsType<RelayFaulted>(((RelayEventForwarded)events[3]).Event);
        Assert.Equal(RelayFaultCode.PortInUse, faulted.Code);
        Assert.Equal("The relay could not start.", faulted.Message);
        Assert.Single(((ConnectionInspected)events[4]).Inspection.Exposure);

        // The inspections were about the port the relay asked for.
        Assert.All(hosting.Mappings.Inspections, settings => Assert.Equal(50123, settings.RelayPort));

        // In Public mode the same holds: there the only inspection is this one.
        var publicMode = new Hosting(new RelaySettings { RelayPort = 50123, ConnectionMode = ConnectionMode.Public });
        publicMode.Mappings.Exposure = [new ExposedHandler(10000, "/livecue", FunnelOn: true, Foreground: false, null)];
        publicMode.Relays.NextStartFault = RelayFaultCode.PortInUse;
        var publicResult = await publicMode.Supervisor.StartAsync(CancellationToken.None);
        Assert.StartsWith("Port 50123 is in use by another program while a Tailscale mapping points at it.", publicResult.FailureText, StringComparison.Ordinal);
        Assert.Single(publicMode.Mappings.Inspections);
    }

    [Fact]
    public async Task Port_in_use_without_a_mapping_is_the_plain_text()
    {
        const string plain =
            "Port 50123 is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay.";

        // Nothing forwards to the port.
        var nothing = new Hosting(new RelaySettings { RelayPort = 50123 });
        nothing.Relays.NextStartFault = RelayFaultCode.PortInUse;
        var result = await nothing.Supervisor.StartAsync(CancellationToken.None);
        Assert.Equal(plain, result.FailureText);
        Assert.Empty(Assert.IsType<ConnectionInspected>(nothing.DrainEvents()[^1]).Inspection.Exposure);

        // The status could not be read: nothing is known to point at the port.
        var unread = new Hosting(new RelaySettings { RelayPort = 50123 });
        unread.Mappings.StatusFailure = TailscaleReadFailure.Failed;
        unread.Relays.NextStartFault = RelayFaultCode.PortInUse;
        Assert.Equal(plain, (await unread.Supervisor.StartAsync(CancellationToken.None)).FailureText);

        // An endpoint override: no mapping is involved, so none is inspected, whatever exists.
        var withEndpoint = new Hosting(new RelaySettings { RelayPort = 50123 }, new RelayOverrides(GivenEndpoint));
        withEndpoint.Mappings.Exposure = [new ExposedHandler(11443, "/", FunnelOn: false, Foreground: false, null)];
        withEndpoint.Relays.NextStartFault = RelayFaultCode.PortInUse;
        Assert.Equal(plain, (await withEndpoint.Supervisor.StartAsync(CancellationToken.None)).FailureText);
        Assert.Empty(withEndpoint.Mappings.Inspections);
        Assert.DoesNotContain(withEndpoint.DrainEvents(), hostEvent => hostEvent is ConnectionInspected);

        // Another fault with a mapping in place is not a port problem: no inspection, the usual text.
        var otherFault = new Hosting(new RelaySettings { RelayPort = 50123, ConnectionMode = ConnectionMode.Public });
        otherFault.Mappings.Exposure = [new ExposedHandler(10000, "/livecue", FunnelOn: true, Foreground: false, null)];
        otherFault.Relays.NextStartFault = RelayFaultCode.PairingStoreUnreadable;
        Assert.Equal(
            "The saved pairing could not be read. Reset pairing to create a new one.",
            (await otherFault.Supervisor.StartAsync(CancellationToken.None)).FailureText);
        Assert.Empty(otherFault.Mappings.Inspections);
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
        Assert.Equal(SupervisorState.Running, hosting.Supervisor.State);

        // A save that fails changes nothing and tells the caller.
        hosting.Store.SaveFails = true;
        Assert.Throws<IOException>(() => hosting.Supervisor.SaveSettings(new RelaySettings { RelayPort = 51000 }));
        Assert.Equal(50999, hosting.Supervisor.Settings.RelayPort);
    }

    [Fact]
    public async Task Composing_does_no_io_and_the_settings_are_loaded_on_first_use()
    {
        var hosting = new Hosting();

        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Null(hosting.Supervisor.Relay);
        Assert.Null(hosting.Supervisor.RunningOptions);
        Assert.Equal(0, hosting.Store.Loads);
        Assert.Empty(hosting.Prerequisites.Calls);
        Assert.Empty(hosting.Files.FilePaths);

        Assert.True(hosting.Supervisor.Settings.SameValues(hosting.Store.Stored));
        Assert.True(hosting.Supervisor.Settings.SameValues(hosting.Store.Stored));
        Assert.Equal(1, hosting.Store.Loads);

        await hosting.Supervisor.DisposeAsync();
        await hosting.Supervisor.DisposeAsync();
        Assert.False(hosting.Supervisor.Events.TryRead(out _));
        Assert.True(hosting.Supervisor.Events.Completion.IsCompleted);
    }

    [Fact]
    public async Task A_check_and_an_inspection_publish_their_events_and_do_not_change_the_state()
    {
        var hosting = new Hosting();
        hosting.Prerequisites.Fails = PrerequisiteId.CodexSignedIn;

        var report = await hosting.Supervisor.CheckAsync(CancellationToken.None);
        var inspection = await hosting.Supervisor.InspectConnectionAsync(CancellationToken.None);
        var events = hosting.DrainEvents();

        Assert.False(report.Passed);
        Assert.Equal(MappingState.Absent, inspection.Private.State);
        Assert.Collection(
            events,
            first => Assert.Same(report, Assert.IsType<PrerequisitesChecked>(first).Report),
            second => Assert.Same(inspection, Assert.IsType<ConnectionInspected>(second).Inspection));
        Assert.Equal(SupervisorState.Stopped, hosting.Supervisor.State);
        Assert.Empty(hosting.Relays.Created);

        // While running, too: a check on demand neither stops nor restarts anything.
        hosting.Prerequisites.Fails = null;
        await hosting.Supervisor.StartAsync(CancellationToken.None);
        hosting.Prerequisites.Fails = PrerequisiteId.TailscaleOnline;
        Assert.False((await hosting.Supervisor.CheckAsync(CancellationToken.None)).Passed);
        Assert.Equal(SupervisorState.Running, hosting.Supervisor.State);
        Assert.Single(hosting.Relays.Created);
    }

    [Fact]
    public async Task A_cancelled_start_leaves_nothing_running()
    {
        // Cancelled during the checks.
        var checking = new Hosting();
        checking.Prerequisites.Gate = new TaskCompletionSource();
        using (var cancel = new CancellationTokenSource())
        {
            var start = checking.Supervisor.StartAsync(cancel.Token);
            await WaitForAsync(() => checking.Supervisor.State == SupervisorState.Checking);
            await cancel.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        }

        Assert.Equal(SupervisorState.Stopped, checking.Supervisor.State);
        Assert.Empty(checking.Relays.Created);

        // A relay with a defect: whatever it throws from its start, the instance is disposed.
        var defect = new Hosting();
        defect.Relays.NextStartThrows = new InvalidOperationException("A defect.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => defect.Supervisor.StartAsync(CancellationToken.None));
        Assert.Equal(SupervisorState.Stopped, defect.Supervisor.State);
        Assert.Null(defect.Supervisor.Relay);
        Assert.True(defect.Relays.Current.Disposed);

        // Both supervisors start normally afterwards.
        checking.Prerequisites.Gate = null;
        Assert.Equal(StartOutcome.Started, (await checking.Supervisor.StartAsync(CancellationToken.None)).Outcome);
        Assert.Equal(StartOutcome.Started, (await defect.Supervisor.StartAsync(CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_relay_that_does_not_stop_is_abandoned_after_the_stop_budget()
    {
        var clock = new ManualTimeProvider();
        var stuck = new StuckRelay();
        var supervisor = new RelaySupervisor(
            new SupervisorOptions(DataDirectory, "1.0.0", new RelayOverrides(GivenEndpoint)),
            new InMemorySettingsStore(),
            new ScriptedPrerequisites(),
            new ScriptedMappings(),
            new EndpointResolver(),
            stuck,
            new FakeHostFileSystem(),
            clock);
        Assert.Equal(StartOutcome.Started, (await supervisor.StartAsync(CancellationToken.None)).Outcome);

        var stopping = supervisor.StopAsync(CancellationToken.None);
        await WaitForAsync(() => stuck.StopCalls == 1 && clock.ScheduledTimers > 0);
        Assert.False(stopping.IsCompleted);
        Assert.Equal(SupervisorState.Stopping, supervisor.State);

        // W-02 plus one second, on the supervisor's clock.
        clock.Advance(ProtocolConstants.StopBudget);
        await Task.Delay(50);
        Assert.False(stopping.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SupervisorState.Stopped, supervisor.State);
        Assert.Null(supervisor.Relay);
        Assert.Null(supervisor.RunningOptions);
    }

    [Fact]
    public async Task With_the_real_relay_the_supervisor_creates_the_data_folder_binds_loopback_and_stops_cleanly()
    {
        // The real hosting library and the real relay. Only two things are not real: the Codex that is
        // "found" is an empty file in the temporary folder, and no process is ever started.
        using var temp = new TempFolder();
        var dataDirectory = temp.Combine("data");
        var codex = temp.Combine("codex.exe");
        await File.WriteAllBytesAsync(codex, []);
        var processes = new FakeProcessRunner();
        var services = HostingFactory.Create(
            new SupervisorOptions(dataDirectory, "1.0.0", new RelayOverrides(PrivateEndpoint, Port: 0, CodexPath: codex)),
            new HostingOverrides { Processes = processes });
        await using var supervisor = services.Supervisor;
        Assert.False(Directory.Exists(dataDirectory));

        var result = await supervisor.StartAsync(CancellationToken.None);
        Assert.Equal(StartOutcome.Started, result.Outcome);
        var port = supervisor.Relay!.BoundPort;
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        using var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));
        await supervisor.StopAsync(CancellationToken.None);

        // Nobody here knows the token, so the honest answer is 401; it proves the listener is serving.
        Assert.InRange(port, 1024, 65535);
        Assert.NotEqual(RelaySettings.ForbiddenPort, port);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SupervisorState.Stopped, supervisor.State);

        // Check 1 created the data folder before the relay wrote its files into it.
        Assert.True(File.Exists(Path.Combine(dataDirectory, "pairing.json")));
        Assert.True(Directory.Exists(Path.Combine(dataDirectory, "schemas")));
        Assert.False(File.Exists(Path.Combine(dataDirectory, "settings.json")), "A start saves no settings.");
        Assert.Equal(codex, result.Report!.CodexExecutablePath);
        Assert.All(processes.Requests, request => Assert.Equal(codex, request.FileName));
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

    /// <summary>The supervisor over scripted checks, scripted mappings, a fake file system and fake relays.</summary>
    private sealed class Hosting
    {
        public Hosting(
            RelaySettings? settings = null,
            RelayOverrides? overrides = null,
            string dataDirectory = DataDirectory,
            FakeHostFileSystem? files = null)
        {
            if (settings is not null)
            {
                Prerequisites.Settings = settings;
            }

            Files = files ?? new FakeHostFileSystem();
            Store = new InMemorySettingsStore(Prerequisites.Settings);
            Supervisor = new RelaySupervisor(
                new SupervisorOptions(dataDirectory, "1.0.0", overrides),
                Store,
                Prerequisites,
                Mappings,
                new EndpointResolver(),
                Relays,
                Files,
                TimeProvider.System);
        }

        public RelaySupervisor Supervisor { get; }

        public InMemorySettingsStore Store { get; }

        public ScriptedPrerequisites Prerequisites { get; } = new();

        public ScriptedMappings Mappings { get; } = new();

        public FakeHostFileSystem Files { get; }

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

    /// <summary>A relay whose stop and dispose never finish, as a relay with a defect might behave.</summary>
    private sealed class StuckRelay : IRelay, IRelayFactory
    {
        private readonly Channel<RelayEvent> _events = Channel.CreateUnbounded<RelayEvent>();
        private readonly TaskCompletionSource _never = new();
        private int _stopCalls;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public RelayStatus Status { get; private set; } = RelayStatus.Stopped;

        public bool Accepting => true;

        public int BoundPort => 50000;

        public ChannelReader<RelayEvent> Events => _events.Reader;

        public IRelay Create(RelayOptions options) => this;

        public Task StartAsync(CancellationToken ct)
        {
            Status = RelayStatus.Running;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _stopCalls);
            return _never.Task;
        }

        public Task SetAcceptingAsync(bool accepting) => Task.CompletedTask;

        public Task<PairingQr> ShowPairingAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<PairingQr> ResetPairingAsync(CancellationToken ct) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => new(_never.Task);
    }
}
