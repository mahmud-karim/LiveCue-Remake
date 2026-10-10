using LiveCue.Desktop.Composition;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests.Session;

/// <summary>
/// The real session over the real supervisor, with the self-test's scripted relay and hosting parts:
/// the wiring the app itself uses, without a window. Rewritten from the spike's relay view-model tests
/// for the new contracts; work package WP-W08 owns this folder and replaces this file with the tests of
/// the test plan (section 2.6).
/// </summary>
public sealed class DesktopSessionTests
{
    private const string Endpoint = "https://pc.example.test:11443/";

    [Fact]
    public async Task At_launch_the_relay_is_stopped_and_nothing_is_known_about_a_phone()
    {
        await using var app = new ScriptedApp();
        var session = app.Session;

        Assert.Equal(RelayUiState.Stopped, session.State);
        Assert.Null(session.StateText);
        Assert.Null(session.FaultCode);
        Assert.False(session.IsRelayRunning);
        Assert.False(session.IsPaused);
        Assert.Equal(PhonePresence.NotConnected, session.Presence);
        Assert.Null(session.LastPhoneSeen);
        Assert.Null(session.AdvertisedEndpoint);
        Assert.Null(session.Prerequisites);
        Assert.Null(session.Connection);
        Assert.False(session.IsConnectionReady);
        Assert.True(session.Settings.SameValues(RelaySettings.Default));
        Assert.Equal(0, app.Script.Created);
    }

    [Fact]
    public async Task Start_ends_ready_with_the_endpoint_the_report_and_the_inspected_connection()
    {
        await using var app = new ScriptedApp();
        var session = app.Session;
        var signals = new List<SessionSignal>();
        var relayEvents = new List<RelayEvent>();
        session.Signal += (_, signal) => signals.Add(signal);
        session.RelayEventReceived += (_, relayEvent) => relayEvents.Add(relayEvent);

        await session.StartAsync();
        await app.UntilAsync(() => session.State == RelayUiState.Ready && session.Connection is not null);

        Assert.True(session.IsRelayRunning);
        Assert.Equal(Endpoint, session.AdvertisedEndpoint);
        Assert.True(session.Prerequisites!.Passed);
        Assert.Equal(MappingState.Present, session.Connection!.Private.State);
        Assert.True(session.IsConnectionReady);
        Assert.False(session.SettingsDifferFromRunning);
        Assert.Equal([SessionSignal.RelayStarting], signals);
        Assert.IsType<RelayStarted>(Assert.Single(relayEvents));
        Assert.Equal(47841, app.Script.Current!.Options.Port);
        Assert.Equal("Ready", app.Services.Shell.Live.RelayStatus.Text);
        Assert.Equal("Relay started. Waiting for your iPhone.", Assert.Single(app.Services.Shell.Activity.Entries).Message);
    }

    [Fact]
    public async Task A_phone_a_pause_a_request_and_a_stop_move_the_state_as_the_event_table_says()
    {
        await using var app = new ScriptedApp();
        var session = app.Session;
        await session.StartAsync();
        await app.UntilAsync(() => session.State == RelayUiState.Ready);
        var relay = app.Script.Current!;
        var at = app.Clock.GetUtcNow();

        app.Script.PhoneSeen();
        await app.UntilAsync(() => session.Presence == PhonePresence.Connected);
        Assert.Equal(at, session.LastPhoneSeen);

        await session.TogglePauseAsync();
        await app.UntilAsync(() => session.State == RelayUiState.Paused);
        Assert.True(session.IsPaused);
        Assert.False(relay.Accepting);

        relay.Raise(new RequestStarted(at, "request-0001", AnswerKind.Assist, "gpt-test", "low", "Question?", null, null, "{}"));
        await app.UntilAsync(() => session.State == RelayUiState.Thinking);
        relay.Raise(new RequestCompleted(at, "request-0001", AnswerKind.Assist, 912, 850, "gpt-test", "low", "{}"));
        await app.UntilAsync(() => session.State == RelayUiState.Paused);

        await session.TogglePauseAsync();
        await app.UntilAsync(() => session.State == RelayUiState.Ready);
        relay.Raise(new RequestStarted(at, "request-0002", AnswerKind.Summary, "gpt-test", "low", null, null, null, "{}"));
        relay.Raise(new RequestFailed(at, "request-0002", AnswerKind.Summary, "ASSISTANT_FAILED", "The local assistant request failed."));
        await app.UntilAsync(() => session.State == RelayUiState.RequestFailed);
        Assert.True(session.IsRelayRunning);

        var ended = 0;
        session.Signal += (_, signal) => ended += signal == SessionSignal.RelayEnded ? 1 : 0;
        await session.StopAsync();
        await app.UntilAsync(() => session.State == RelayUiState.Stopped);

        Assert.Equal(1, ended);
        Assert.Equal(PhonePresence.NotConnected, session.Presence);
        Assert.Null(session.LastPhoneSeen);
        Assert.False(session.IsPaused);
        Assert.False(session.IsConnectionReady);
        Assert.Equal(Endpoint, session.AdvertisedEndpoint);
        Assert.Equal(SupervisorState.Stopped, app.Services.Hosting.Supervisor.State);
    }

    [Fact]
    public async Task A_port_in_use_fault_returns_the_stopped_layout_and_keeps_its_text_until_the_next_start()
    {
        await using var app = new ScriptedApp();
        var session = app.Session;
        app.Script.NextStartFault = RelayFaultCode.PortInUse;

        await session.StartAsync();
        await app.UntilAsync(() => session.State == RelayUiState.Faulted);

        Assert.Equal(RelayFaultCode.PortInUse, session.FaultCode);
        Assert.Equal(
            "Port 47841 is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay.",
            session.StateText);
        Assert.False(session.IsRelayRunning);
        Assert.True(app.Services.Shell.Live.IsStartVisible);
        Assert.Equal(session.StateText, Assert.Single(app.Services.Shell.Activity.Entries).Message);

        await session.StartAsync();
        await app.UntilAsync(() => session.State == RelayUiState.Ready);

        Assert.Null(session.StateText);
        Assert.Null(session.FaultCode);
        Assert.Equal(2, app.Script.Created);
    }

    [Theory]
    [InlineData(RelayFaultCode.PairingStoreUnreadable, "The saved pairing could not be read. Reset pairing to create a new one.")]
    [InlineData(RelayFaultCode.StartFailed, "Could not start the relay.")]
    public async Task Other_start_faults_show_their_architecture_text(RelayFaultCode code, string text)
    {
        await using var app = new ScriptedApp();
        app.Script.NextStartFault = code;

        await app.Session.StartAsync();
        await app.UntilAsync(() => app.Session.State == RelayUiState.Faulted);

        Assert.Equal(text, app.Session.StateText);
    }

    [Fact]
    public async Task A_failed_prerequisite_shows_its_text_and_starts_no_relay()
    {
        // The foundation's own checker over a file system that refuses everything: check 1 fails.
        var script = new RelayScript(TimeProvider.System);
        var hosting = HostingFactory.Create(
            new SupervisorOptions(@"C:\LiveCue-tests\data", "1.0.0"),
            new HostingOverrides { Files = new RecordingFiles(), Processes = new RecordingProcesses(), RelayFactory = script });
        var ui = new ImmediateDispatcher { Queue = true };
        await using var services = AppServices.Create(
            hosting, ui, TimeProvider.System, new FakeClipboard(), new FakeLauncher());
        var session = services.Session;

        await session.StartAsync();
        await ScriptedApp.UntilAsync(ui, () => session.State == RelayUiState.PrerequisiteFailed);

        Assert.Equal("LiveCue could not open its data folder.", session.StateText);
        Assert.Equal(PrerequisiteId.DataFolder, session.Prerequisites!.FirstFailure!.Id);
        Assert.Equal(0, script.Created);
        Assert.Equal("Cannot start yet", services.Shell.SidebarRelayStatus.Text);
    }

    [Fact]
    public async Task A_subscriber_that_throws_becomes_one_diagnostic_line_and_later_events_still_arrive()
    {
        await using var app = new ScriptedApp();
        var session = app.Session;
        var messages = new List<SessionMessage>();
        session.MessageRaised += (_, message) => messages.Add(message);
        session.RelayEventReceived += (_, relayEvent) =>
        {
            if (relayEvent is PhoneSeen)
            {
                throw new InvalidOperationException("secret detail that must never be shown");
            }
        };
        await session.StartAsync();
        await app.UntilAsync(() => session.State == RelayUiState.Ready);

        app.Script.PhoneSeen();
        app.Script.Diagnostic("Later event.");
        await app.UntilAsync(() => app.Services.Shell.Activity.Entries.Any(entry => entry.Message.EndsWith("Later event.", StringComparison.Ordinal)));

        var message = Assert.Single(messages);
        Assert.Equal("Relay diagnostic: An event could not be shown (InvalidOperationException).", message.Text);
        Assert.DoesNotContain("secret", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_and_reset_hand_the_matrix_to_the_pages_and_never_more_than_that()
    {
        await using var app = new ScriptedApp();
        var session = app.Session;
        var matrices = new List<PairingQr>();
        session.PairingQrProduced += (_, qr) => matrices.Add(qr);

        await session.ShowPairingAsync();
        Assert.Empty(matrices);

        await session.StartAsync();
        await app.UntilAsync(() => session.IsConnectionReady);
        await session.ShowPairingAsync();
        Assert.True(await session.ResetPairingAsync());

        Assert.Equal(2, matrices.Count);
        Assert.All(matrices, qr =>
        {
            Assert.Equal(Endpoint, qr.Endpoint);
            Assert.True(qr.Modules.Length >= 21);
        });
    }

    [Fact]
    public async Task A_reset_while_stopped_goes_to_the_stored_pairing_and_says_so()
    {
        await using var app = new ScriptedApp();
        var messages = new List<SessionMessage>();
        app.Session.MessageRaised += (_, message) => messages.Add(message);

        Assert.True(await app.Session.ResetPairingAsync());

        Assert.Equal("Pairing reset. Every iPhone must pair again.", Assert.Single(messages).Text);
        Assert.Equal(0, app.Script.Created);
    }

    [Fact]
    public async Task Saving_settings_goes_through_the_supervisor_and_a_plan_is_made_from_the_saved_settings()
    {
        await using var app = new ScriptedApp();
        var session = app.Session;

        var problems = session.SaveSettings(RelaySettings.Default with { ConnectionMode = ConnectionMode.Public });
        var plan = session.PlanSetUp(out var refusal);

        Assert.Empty(problems);
        Assert.Equal(ConnectionMode.Public, session.Settings.ConnectionMode);
        Assert.Equal(MappingRefusal.None, refusal);
        Assert.Equal(MappingCommand.ApplyPublic(10000, "/livecue", 47841).DisplayText, plan!.Command.DisplayText);
        Assert.Equal(0, app.Hosting.Mappings.Executions);
    }

    /// <summary>
    /// The app's composition root with the self-test's scripted relay and hosting parts and a hand-moved
    /// clock: the same wiring the self-test uses, without a window.
    /// </summary>
    private sealed class ScriptedApp : IAsyncDisposable
    {
        public ScriptedApp()
        {
            Clock = new ManualClock(new DateTimeOffset(2026, 10, 10, 9, 14, 2, TimeSpan.Zero));
            Script = new RelayScript(Clock);
            Hosting = new ScriptedHosting(Clock, Script);
            Services = AppServices.Create(Hosting.Services, Ui, Clock, new FakeClipboard(), new FakeLauncher());
        }

        /// <summary>
        /// The "UI thread": what the session posts is held and run by the test itself, in order, so the
        /// session and the view models are only ever touched from the test, as in the app.
        /// </summary>
        public ImmediateDispatcher Ui { get; } = new() { Queue = true };

        public ManualClock Clock { get; }

        public RelayScript Script { get; }

        public ScriptedHosting Hosting { get; }

        public AppServices Services { get; }

        public IDesktopSession Session => Services.Session;

        /// <summary>
        /// Runs what the session posted until the condition holds. Events travel from the supervisor's
        /// pump to the dispatcher, so there is nothing to await but the result.
        /// </summary>
        public static async Task UntilAsync(ImmediateDispatcher ui, Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                ui.RunQueued();
                if (condition())
                {
                    return;
                }

                Assert.True(DateTime.UtcNow < until, "The session did not reach the expected state in time.");
                await Task.Delay(5);
            }
        }

        public Task UntilAsync(Func<bool> condition) => UntilAsync(Ui, condition);

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }
}
