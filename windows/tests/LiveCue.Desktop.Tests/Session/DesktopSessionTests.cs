using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests.Session;

/// <summary>
/// The real <see cref="LiveCue.Desktop.Session.DesktopSession"/> over a scripted supervisor, on a real
/// single "UI" thread (test plan 2.6; W-design 5.4 and 5.11). Each test plays the events and results the
/// supervisor would produce and reads what the session shows.
/// </summary>
public sealed class DesktopSessionTests
{
    private const string PortInUseText =
        "Port 47841 is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay.";

    private const string PortInUseWithMappingText =
        "Port 47841 is in use by another program while a Tailscale mapping points at it. That program may have received your iPhone's requests, including the pairing token. Free the port, start the relay, then reset pairing.";

    private const string ExposedText =
        "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode.";

    private const string StoppedByItselfText = "Relay stopped. Click Start relay to reconnect.";

    private const string CommandFailedText = "The desktop command failed. Please retry.";

    /// <summary>The table of W-design 5.4, one case per row.</summary>
    [Theory]
    [InlineData("CheckingStarted")]
    [InlineData("PrerequisitesChecked: passed")]
    [InlineData("PrerequisitesChecked: failed")]
    [InlineData("PrerequisitesChecked: containment warning")]
    [InlineData("StartAsync returned ExposedInPrivateMode")]
    [InlineData("StartAsync returned RelayFault")]
    [InlineData("RelayStarting")]
    [InlineData("RelayStarted")]
    [InlineData("ConnectionInspected")]
    [InlineData("AcceptingChanged")]
    [InlineData("PhoneSeen")]
    [InlineData("RequestStarted")]
    [InlineData("RequestCompleted")]
    [InlineData("RequestFailed")]
    [InlineData("RelayFaulted")]
    [InlineData("RelayStopped: requested")]
    [InlineData("RelayStopped: fault")]
    [InlineData("every other relay event")]
    public async Task Event_to_state_cases(string row)
    {
        using var h = new SessionHarness();
        var session = h.Session;
        var now = h.Clock.GetUtcNow();

        switch (row)
        {
            case "CheckingStarted":
                await h.Publish(new CheckingStarted(now));

                Assert.Equal(RelayUiState.Checking, session.State);
                Assert.False(session.IsRelayRunning);
                Assert.Equal([nameof(IDesktopSession.State)], h.Changes);
                break;

            case "PrerequisitesChecked: passed":
            {
                var settings = RelaySettings.Default with { PrivateHttpsPort = 12443 };
                var report = FakeSupervisor.PassingReport(settings);
                await h.Publish(new CheckingStarted(now));
                await h.Publish(new PrerequisitesChecked(now, report));

                Assert.Same(report, session.Prerequisites);
                Assert.Same(report.SettingsLoad, session.SettingsLoad);
                Assert.Equal(12443, session.Settings.PrivateHttpsPort);
                Assert.Equal(RelayUiState.Checking, session.State);
                Assert.Null(session.StateText);
                Assert.Empty(h.Messages);
                break;
            }

            case "PrerequisitesChecked: failed":
                await h.Publish(new CheckingStarted(now));
                await h.Publish(new PrerequisitesChecked(now, FakeSupervisor.FailingReport(PrerequisiteId.CodexSignedIn)));

                Assert.Equal(RelayUiState.PrerequisiteFailed, session.State);
                Assert.Equal("Codex is installed but not signed in. Open Codex and sign in with ChatGPT.", session.StateText);
                Assert.Null(session.FaultCode);
                Assert.False(session.IsRelayRunning);

                // "Check again" that passes returns to the stopped layout and takes the text away.
                await h.Publish(new PrerequisitesChecked(now, FakeSupervisor.PassingReport()));
                Assert.Equal(RelayUiState.Stopped, session.State);
                Assert.Null(session.StateText);
                break;

            case "PrerequisitesChecked: containment warning":
            {
                // A Codex of version 0.50.0 while no containment check was recorded.
                var stale = FakeSupervisor.PassingReport(codexVersion: "0.50.0", codexPath: @"C:\Tools\codex.exe");
                await h.Publish(new PrerequisitesChecked(now, stale));
                await h.Publish(new PrerequisitesChecked(now, stale));

                var warning = Assert.Single(h.Messages);
                Assert.Equal(SessionMessageKind.Warning, warning.Kind);
                Assert.Equal(
                    "Codex is version 0.50.0; the containment check has not been run yet. Run the containment check, then choose Mark as checked in Settings.",
                    warning.Text);

                // Another pair of versions is told once more; a checked version that matches is not.
                var newer = FakeSupervisor.PassingReport(
                    RelaySettings.Default with { CodexContainmentCheckedVersion = "0.50.0" }, "0.51.0", @"C:\Tools\codex.exe");
                await h.Publish(new PrerequisitesChecked(now, newer));
                await h.Publish(new PrerequisitesChecked(now, newer));
                Assert.Equal(2, h.Messages.Count);
                Assert.Equal(
                    "Codex is version 0.51.0; the containment check was last run for 0.50.0. Run the containment check, then choose Mark as checked in Settings.",
                    h.Messages[1].Text);

                var current = FakeSupervisor.PassingReport(
                    RelaySettings.Default with { CodexContainmentCheckedVersion = "0.51.0" }, "0.51.0", @"C:\Tools\codex.exe");
                await h.Publish(new PrerequisitesChecked(now, current));
                Assert.Equal(2, h.Messages.Count);
                break;
            }

            case "StartAsync returned ExposedInPrivateMode":
                await StartRefusedForExposure(h);

                Assert.Equal(RelayUiState.Faulted, session.State);
                Assert.Null(session.FaultCode);
                Assert.Equal(ExposedText, session.StateText);
                break;

            case "StartAsync returned RelayFault":
                await StartFaultsWithPortInUse(h, PortInUseWithMappingText);

                Assert.Equal(RelayUiState.Faulted, session.State);
                Assert.Equal(RelayFaultCode.PortInUse, session.FaultCode);
                Assert.Equal(PortInUseWithMappingText, session.StateText);
                break;

            case "RelayStarting":
                await h.Publish(new CheckingStarted(now));
                await h.Publish(new PrerequisitesChecked(now, FakeSupervisor.PassingReport()));
                await h.Publish(new RelayStarting(now, FakeSupervisor.OptionsFor(RelaySettings.Default)));

                Assert.Equal(RelayUiState.Starting, session.State);
                Assert.False(session.IsRelayRunning);
                Assert.Equal([SessionSignal.RelayStarting], h.Signals);
                break;

            case "RelayStarted":
            {
                RelayUiState? stateSeenByASubscriber = null;
                session.RelayEventReceived += (_, relayEvent) =>
                {
                    if (relayEvent is RelayStarted)
                    {
                        stateSeenByASubscriber = session.State;
                    }
                };
                await h.StartAsync(MappingState.Absent);

                Assert.Equal(RelayUiState.Ready, session.State);
                Assert.True(session.IsRelayRunning);
                Assert.False(session.IsPaused);
                Assert.Equal(FakeSupervisor.Endpoint, session.AdvertisedEndpoint);
                Assert.Equal(PhonePresence.NotConnected, session.Presence);
                Assert.Null(session.LastPhoneSeen);
                Assert.IsType<RelayStarted>(Assert.Single(h.RelayEvents));

                // The session has its own state in order before anyone is told about the event.
                Assert.Equal(RelayUiState.Ready, stateSeenByASubscriber);
                break;
            }

            case "ConnectionInspected":
            {
                await h.StartAsync(MappingState.Absent);
                Assert.Equal(MappingState.Absent, session.Connection!.Private.State);
                Assert.False(session.IsConnectionReady);

                var present = FakeSupervisor.InspectionOf(MappingState.Present);
                await h.Publish(new ConnectionInspected(now, present));
                Assert.Same(present, session.Connection);
                Assert.True(session.IsConnectionReady);

                // A mapping somebody else made carries the relay as well.
                await h.Publish(new ConnectionInspected(now, FakeSupervisor.InspectionOf(MappingState.PresentNotOwned)));
                Assert.True(session.IsConnectionReady);

                await h.Publish(new ConnectionInspected(now, FakeSupervisor.InspectionOf(MappingState.Absent)));
                Assert.False(session.IsConnectionReady);
                Assert.Empty(h.Qrs);
                break;
            }

            case "AcceptingChanged":
                await h.StartAsync();
                await h.Forward(new AcceptingChanged(now, false));
                Assert.Equal(RelayUiState.Paused, session.State);
                Assert.True(session.IsPaused);
                Assert.True(session.IsRelayRunning);

                await h.Forward(new AcceptingChanged(now, true));
                Assert.Equal(RelayUiState.Ready, session.State);
                Assert.False(session.IsPaused);

                // While a request runs the pause is remembered, and shows when the request ends.
                await h.Forward(Started("request-0001"));
                await h.Forward(new AcceptingChanged(now, false));
                Assert.Equal(RelayUiState.Thinking, session.State);
                Assert.True(session.IsPaused);
                await h.Forward(Completed("request-0001"));
                Assert.Equal(RelayUiState.Paused, session.State);
                break;

            case "PhoneSeen":
            {
                await h.StartAsync();
                h.Clock.Advance(TimeSpan.FromSeconds(3));
                var pcClock = h.Clock.GetUtcNow();

                // The event carries the relay's time; the session stamps the PC clock of the moment it shows it.
                await h.Forward(new PhoneSeen(now));

                Assert.Equal(PhonePresence.Connected, session.Presence);
                Assert.Equal(pcClock, session.LastPhoneSeen);
                break;
            }

            case "RequestStarted":
                await h.StartAsync();
                await h.Forward(Started("request-0001"));

                Assert.Equal(RelayUiState.Thinking, session.State);
                Assert.True(session.IsRelayRunning);
                break;

            case "RequestCompleted":
                await h.StartAsync();
                await h.Forward(Started("request-0001"));
                await h.Forward(Completed("request-0001"));

                Assert.Equal(RelayUiState.Ready, session.State);
                break;

            case "RequestFailed":
                await h.StartAsync();
                await h.Forward(Started("request-0001"));
                await h.Forward(Failed("request-0001"));
                Assert.Equal(RelayUiState.RequestFailed, session.State);
                Assert.True(session.IsRelayRunning);
                Assert.Null(session.StateText);

                // The next request leaves it; so does a pause, and so does a resume.
                await h.Forward(Started("request-0002"));
                Assert.Equal(RelayUiState.Thinking, session.State);
                await h.Forward(Failed("request-0002"));
                await h.Forward(new AcceptingChanged(now, false));
                Assert.Equal(RelayUiState.Paused, session.State);
                await h.Forward(Started("request-0003"));
                await h.Forward(Failed("request-0003"));
                Assert.Equal(RelayUiState.RequestFailed, session.State);
                await h.Forward(new AcceptingChanged(now, true));
                Assert.Equal(RelayUiState.Ready, session.State);
                break;

            case "RelayFaulted":
                await h.StartAsync();
                await h.Forward(new PhoneSeen(now));
                await h.Forward(new RelayFaulted(now, RelayFaultCode.Internal, "The relay stopped unexpectedly."));

                Assert.Equal(RelayUiState.Faulted, session.State);
                Assert.Equal(RelayFaultCode.Internal, session.FaultCode);
                Assert.Equal(StoppedByItselfText, session.StateText);
                Assert.False(session.IsRelayRunning);
                Assert.False(session.IsConnectionReady);
                break;

            case "RelayStopped: requested":
                await h.StartAsync();
                await h.Forward(new PhoneSeen(now));
                await h.Forward(new AcceptingChanged(now, false));
                await h.Forward(new RelayStopped(now, RelayStopReason.Requested));

                Assert.Equal(RelayUiState.Stopped, session.State);
                Assert.Null(session.StateText);
                AssertEnded(h);
                break;

            case "RelayStopped: fault":
                await h.StartAsync();
                await h.Forward(new PhoneSeen(now));
                await h.Forward(new AcceptingChanged(now, false));
                await h.Forward(new RelayFaulted(now, RelayFaultCode.Internal, "The relay stopped unexpectedly."));
                await h.Forward(new RelayStopped(now, RelayStopReason.Fault));

                Assert.Equal(RelayUiState.Faulted, session.State);
                Assert.Equal(StoppedByItselfText, session.StateText);
                AssertEnded(h);
                break;

            case "every other relay event":
            {
                await h.StartAsync();
                h.Changes.Clear();
                h.RelayEvents.Clear();
                RelayEvent[] others =
                [
                    new PairingShown(now, FakeSupervisor.Endpoint),
                    new SelfCheckCompleted(now, SelfCheckResult.Verified),
                    new Notice(now, NoticeKind.PairingRestored, "Pairing restored."),
                    new AssistantNotice(now, "CATALOG", "The model list could not be read."),
                    new SpeechText(now, "What is the plan?"),
                    new EdgeStatistics(now, 1, 2, 3),
                    new Diagnostic(now, "edge", "A request failed unexpectedly. (InvalidOperationException)"),
                ];
                foreach (var other in others)
                {
                    await h.Forward(other);
                }

                Assert.Empty(h.Changes);
                Assert.Equal(others, h.RelayEvents);
                Assert.Equal(RelayUiState.Ready, session.State);
                break;
            }

            default:
                Assert.Fail("No such row: " + row);
                break;
        }

        // W-design 5.11: whatever the session raised, it raised on the UI thread.
        Assert.All(h.RaisedOn, thread => Assert.Equal(h.Ui.ThreadId, thread));
    }

    [Fact]
    public async Task Fault_texts()
    {
        // A start fault by code, as architecture 3.11 step 7 words it.
        (RelayFaultCode Code, string Text)[] startFaults =
        [
            (RelayFaultCode.PortInUse, PortInUseText),
            (RelayFaultCode.PairingStoreUnreadable, "The saved pairing could not be read. Reset pairing to create a new one."),
            (RelayFaultCode.InvalidEndpoint, "Could not start the relay."),
            (RelayFaultCode.StartFailed, "Could not start the relay."),
        ];
        foreach (var (code, text) in startFaults)
        {
            using var faulting = new SessionHarness();
            var at = faulting.Clock.GetUtcNow();
            await faulting.Publish(new CheckingStarted(at));
            await faulting.Publish(new RelayStarting(at, FakeSupervisor.OptionsFor(RelaySettings.Default)));
            await faulting.Forward(new RelayFaulted(at, code, "The relay's own words."));

            Assert.Equal(RelayUiState.Faulted, faulting.Session.State);
            Assert.Equal(code, faulting.Session.FaultCode);
            Assert.Equal(text, faulting.Session.StateText);
        }

        // A fault while running uses the "Relay stopped" text, and never the relay's message.
        using var h = new SessionHarness();
        var session = h.Session;
        var now = h.Clock.GetUtcNow();
        await h.StartAsync();
        await h.Forward(new RelayFaulted(now, RelayFaultCode.Internal, "The relay's own words."));
        await h.Forward(new RelayStopped(now, RelayStopReason.Fault));
        Assert.Equal(StoppedByItselfText, session.StateText);
        Assert.Equal(RelayFaultCode.Internal, session.FaultCode);

        // The text stays: through "Check again", and through whatever else arrives while stopped.
        await h.OnUi(s => s.CheckAgainAsync());
        await h.Publish(new ConnectionInspected(now, FakeSupervisor.InspectionOf(MappingState.Absent)));
        Assert.Equal(RelayUiState.Faulted, session.State);
        Assert.Equal(StoppedByItselfText, session.StateText);

        // The next start takes it away.
        h.Supervisor.State = SupervisorState.Stopped;
        await h.StartAsync();
        Assert.Equal(RelayUiState.Ready, session.State);
        Assert.Null(session.StateText);
        Assert.Null(session.FaultCode);

        // A relay that ends by itself without saying why still shows why the window is stopped.
        await h.Forward(new RelayStopped(now, RelayStopReason.Fault));
        Assert.Equal(RelayUiState.Faulted, session.State);
        Assert.Equal(StoppedByItselfText, session.StateText);
    }

    [Fact]
    public async Task A_throwing_handler_is_isolated()
    {
        using var h = new SessionHarness();
        var session = h.Session;
        var now = h.Clock.GetUtcNow();
        var laterSubscriberSaw = new List<RelayEvent>();
        session.RelayEventReceived += (_, relayEvent) =>
        {
            if (relayEvent is PhoneSeen)
            {
                throw new InvalidOperationException("secret detail that must never be shown");
            }
        };
        session.RelayEventReceived += (_, relayEvent) => laterSubscriberSaw.Add(relayEvent);
        session.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(IDesktopSession.Presence))
            {
                throw new FormatException("another secret");
            }
        };
        await h.StartAsync();
        h.Messages.Clear();

        await h.Forward(new PhoneSeen(now));
        await h.Forward(new Diagnostic(now, "edge", "Later event."));

        // One line for the event, with the type name of the first failure and nothing of its message.
        var message = Assert.Single(h.Messages);
        Assert.Equal(SessionMessageKind.Error, message.Kind);
        Assert.Equal("Relay diagnostic: An event could not be shown (FormatException).", message.Text);

        // The session's own state was updated, the subscriber after the broken one was called, and the
        // next event arrived as usual.
        Assert.Equal(PhonePresence.Connected, session.Presence);
        Assert.Equal(2, laterSubscriberSaw.Count(relayEvent => relayEvent is PhoneSeen or Diagnostic));
        Assert.IsType<Diagnostic>(h.RelayEvents[^1]);

        // A subscriber of the messages that throws is not told about it with another message.
        session.MessageRaised += (_, _) => throw new InvalidOperationException("a broken log");
        await h.Forward(new PhoneSeen(now));
        await h.Forward(new Diagnostic(now, "edge", "Still arriving."));
        Assert.IsType<Diagnostic>(h.RelayEvents[^1]);
        Assert.Equal(2, h.Messages.Count);
    }

    [Fact]
    public async Task First_start_shows_the_qr_once_the_connection_is_ready()
    {
        using var h = new SessionHarness();
        var now = h.Clock.GetUtcNow();
        h.Supervisor.PairingWasCreated = true;

        // The relay created its pairing, but no mapping carries it yet: nothing to show.
        await h.StartAsync(MappingState.Absent);
        Assert.Empty(h.Qrs);
        Assert.Equal(0, h.Relay.ShowPairingCalls);

        // The mapping appears: the QR is asked for, once.
        await h.Publish(new ConnectionInspected(now, FakeSupervisor.InspectionOf(MappingState.Present)));
        await h.Until(() => h.Qrs.Count == 1, "the first QR");
        Assert.Equal(FakeSupervisor.Endpoint, h.Qrs[0].Endpoint);

        await h.Publish(new ConnectionInspected(now, FakeSupervisor.InspectionOf(MappingState.Absent)));
        await h.Publish(new ConnectionInspected(now, FakeSupervisor.InspectionOf(MappingState.Present)));
        await h.Settled();
        Assert.Equal(1, h.Relay.ShowPairingCalls);

        // A later start of a relay that already had its pairing shows nothing by itself.
        var firstRelay = h.Relay;
        await h.OnUi(session => session.StopAsync());
        h.Supervisor.PairingWasCreated = false;
        await h.StartAsync();
        await h.Settled();
        Assert.Single(h.Qrs);
        Assert.Equal(1, firstRelay.ShowPairingCalls);
        Assert.Equal(0, h.Relay.ShowPairingCalls);
    }

    [Fact]
    public async Task First_start_with_the_connection_already_there_shows_the_qr_at_once()
    {
        using var h = new SessionHarness();
        h.Supervisor.PairingWasCreated = true;

        await h.StartAsync(MappingState.Present);
        await h.Until(() => h.Qrs.Count == 1, "the first QR");

        Assert.Equal(1, h.Relay.ShowPairingCalls);
    }

    [Fact]
    public async Task A_stored_reset_recovers_from_an_unreadable_pairing()
    {
        using var h = new SessionHarness();
        var session = h.Session;
        const string unreadable = "The saved pairing could not be read. Reset pairing to create a new one.";
        h.Supervisor.OnStart = supervisor =>
        {
            supervisor.Publish(new CheckingStarted(supervisor.Now));
            supervisor.Publish(new PrerequisitesChecked(supervisor.Now, supervisor.Report));
            supervisor.Publish(new RelayStarting(supervisor.Now, FakeSupervisor.OptionsFor(RelaySettings.Default)));
            supervisor.Forward(new RelayFaulted(supervisor.Now, RelayFaultCode.PairingStoreUnreadable, "The pairing store could not be read."));
            return Task.FromResult(new StartResult(
                StartOutcome.RelayFault, supervisor.Report, RelayFaultCode.PairingStoreUnreadable, unreadable));
        };
        await h.OnUi(s => s.StartAsync());
        Assert.Equal(RelayUiState.Faulted, session.State);
        Assert.Equal(RelayFaultCode.PairingStoreUnreadable, session.FaultCode);
        Assert.Equal(unreadable, session.StateText);

        // A reset that cannot write leaves the fault where it is.
        h.Supervisor.ResetFailure = new IOException("disk detail");
        Assert.False(await h.OnUi(s => s.ResetPairingAsync()));
        Assert.Equal(RelayUiState.Faulted, session.State);
        Assert.Equal(CommandFailedText, Assert.Single(h.Messages).Text);

        // The reset goes to the stored pairing, clears the fault and says what it means for the phones.
        h.Supervisor.ResetFailure = null;
        h.Messages.Clear();
        Assert.True(await h.OnUi(s => s.ResetPairingAsync()));

        Assert.Equal(2, h.Supervisor.ResetCalls);
        Assert.Equal(RelayUiState.Stopped, session.State);
        Assert.Null(session.FaultCode);
        Assert.Null(session.StateText);
        var message = Assert.Single(h.Messages);
        Assert.Equal(SessionMessageKind.Info, message.Kind);
        Assert.Equal("Pairing reset. Every iPhone must pair again.", message.Text);
        Assert.Empty(h.Qrs);

        // The new pairing's QR is shown by the next start, as soon as the connection is ready.
        h.Supervisor.OnStart = h.Supervisor.StartsNormally;
        await h.StartAsync(MappingState.Present);
        await h.Until(() => h.Qrs.Count == 1, "the QR of the new pairing");
    }

    [Fact]
    public async Task Commands_do_not_block_and_do_not_overlap()
    {
        using var h = new SessionHarness();
        var session = h.Session;

        // A start whose checks take as long as the test likes.
        var checks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Supervisor.OnStart = async supervisor =>
        {
            await checks.Task.ConfigureAwait(false);
            return await supervisor.StartsNormally(supervisor).ConfigureAwait(false);
        };

        // Two calls from the UI thread: the second returns at once, the first is still running, and
        // the UI thread is free meanwhile (this very lambda returns, and the next one runs).
        var starts = await h.Read(s => new[] { s.StartAsync(), s.StartAsync() });
        Assert.True(starts[1].IsCompletedSuccessfully);
        Assert.False(starts[0].IsCompleted);
        Assert.Equal(RelayUiState.Stopped, await h.Read(s => s.State));
        await h.Until(() => h.Supervisor.StartCalls == 1, "the start call");

        // "Check again" does not run beside a start.
        await h.OnUi(s => s.CheckAgainAsync());
        Assert.Equal(0, h.Supervisor.CheckCalls);

        checks.SetResult();
        await starts[0];
        Assert.Equal(1, h.Supervisor.StartCalls);
        Assert.Equal(RelayUiState.Ready, session.State);

        // Pairing: one call at a time. The second "show" returns at once; a second reset answers false.
        h.Relay.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shows = await h.Read(s => new[] { s.ShowPairingAsync(), s.ShowPairingAsync() });
        var resets = await h.Read(s => new[] { s.ResetPairingAsync(), s.ResetPairingAsync() });
        Assert.True(shows[1].IsCompletedSuccessfully);
        Assert.False(shows[0].IsCompleted);
        Assert.False(await resets[1]);
        Assert.False(resets[0].IsCompleted);
        h.Relay.Gate.SetResult();
        await shows[0];
        Assert.True(await resets[0]);
        Assert.Equal(1, h.Relay.ShowPairingCalls);
        Assert.Equal(1, h.Relay.ResetPairingCalls);
        Assert.Equal(2, h.Qrs.Count);

        // A mapping command: the second call runs nothing.
        var mapping = new TaskCompletionSource<MappingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Mappings.OnExecute = _ => mapping.Task.GetAwaiter().GetResult();
        var plan = await h.Read(s => s.PlanSetUp(out _)!);
        var executions = await h.Read(s => new[] { s.ExecuteMappingAsync(plan), s.ExecuteMappingAsync(plan) });
        Assert.Equal(MappingOutcome.Refused, (await executions[1]).Outcome);
        mapping.SetResult(new MappingResult(MappingOutcome.Done, MappingRefusal.None, false, session.Settings, null));
        Assert.Equal(MappingOutcome.Done, (await executions[0]).Outcome);
        Assert.Single(h.Mappings.Executed);

        // Pause, resume and stop, each once.
        await h.OnUi(s => s.TogglePauseAsync());
        await h.Until(() => session.IsPaused, "the pause");
        await h.OnUi(s => s.TogglePauseAsync());
        await h.Until(() => !session.IsPaused, "the resume");
        var stops = await h.Read(s => new[] { s.StopAsync(), s.StopAsync() });
        await Task.WhenAll(stops);
        Assert.Equal(1, h.Supervisor.StopCalls);
        Assert.Equal(RelayUiState.Stopped, session.State);

        // Not one supervisor, relay or mapping call was made on the UI thread, and everything the
        // session raised, it raised there.
        Assert.NotEmpty(h.Supervisor.CallThreads);
        Assert.DoesNotContain(h.Ui.ThreadId, h.Supervisor.CallThreads);
        Assert.DoesNotContain(h.Ui.ThreadId, h.Mappings.CallThreads);
        Assert.NotEmpty(h.RaisedOn);
        Assert.All(h.RaisedOn, thread => Assert.Equal(h.Ui.ThreadId, thread));
    }

    /// <summary>ST-08: check 6a refused the start. No relay, no fault code, the supervisor's text, and the handler stays known.</summary>
    [Fact]
    public async Task An_exposure_refusal_is_the_faulted_layout_with_its_text()
    {
        using var h = new SessionHarness();
        var session = h.Session;

        var exposure = await StartRefusedForExposure(h);

        Assert.Equal(RelayUiState.Faulted, session.State);
        Assert.Null(session.FaultCode);
        Assert.Equal(ExposedText, session.StateText);
        Assert.False(session.IsRelayRunning);
        Assert.False(session.IsConnectionReady);
        Assert.Null(session.AdvertisedEndpoint);

        // The inspection that showed the exposure stays, so the pages can name the handler.
        Assert.Same(exposure, session.Connection);
        Assert.True(session.Connection!.HasFunnelExposure);
        Assert.Equal(10000, Assert.Single(session.Connection.UnownedExposure).HttpsPort);

        // No relay event arrived for it, and nothing was signalled as started or ended.
        Assert.Empty(h.RelayEvents);
        Assert.Empty(h.Signals);
        Assert.Empty(h.Messages);

        // The text stays until the next start, which shows the checks again.
        h.Supervisor.OnStart = h.Supervisor.StartsNormally;
        await h.StartAsync();
        Assert.Equal(RelayUiState.Ready, session.State);
        Assert.Null(session.StateText);
    }

    /// <summary>ST-10: the supervisor knows more than the relay's fault event; its text is the one shown.</summary>
    [Fact]
    public async Task The_start_result_text_replaces_the_fault_text()
    {
        using var h = new SessionHarness();
        var session = h.Session;
        var texts = new List<string?>();
        session.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(IDesktopSession.StateText))
            {
                texts.Add(session.StateText);
            }
        };

        await StartFaultsWithPortInUse(h, PortInUseWithMappingText);

        Assert.Equal(RelayUiState.Faulted, session.State);
        Assert.Equal(RelayFaultCode.PortInUse, session.FaultCode);
        Assert.Equal(PortInUseWithMappingText, session.StateText);

        // First the text of the event, then the text of the result: never the other way round.
        Assert.Equal([PortInUseText, PortInUseWithMappingText], texts);
        Assert.Single(session.Connection!.Exposure);

        // A plain port-in-use result leaves the text of the event as it is.
        using var plain = new SessionHarness();
        await StartFaultsWithPortInUse(plain, PortInUseText);
        Assert.Equal(PortInUseText, plain.Session.StateText);
    }

    [Fact]
    public async Task A_start_that_passes_keeps_its_containment_warning_in_the_new_log()
    {
        using var h = new SessionHarness();
        h.Supervisor.Report = FakeSupervisor.PassingReport(codexVersion: null, codexPath: @"C:\Tools\codex.exe");
        var order = new List<string>();
        h.Session.Signal += (_, signal) => order.Add(signal.ToString());
        h.Session.MessageRaised += (_, message) => order.Add(message.Kind.ToString());

        await h.StartAsync();

        // The start signal empties the activity log, so the warning of this start comes after it.
        Assert.Equal(["RelayStarting", "Warning"], order);
        Assert.Equal(
            "Codex's version could not be read; the containment check has not been run yet. Run the containment check, then choose Mark as checked in Settings.",
            Assert.Single(h.Messages).Text);

        // A start that ends at a failed check tells it too.
        using var failing = new SessionHarness();
        var report = FakeSupervisor.FailingReport(PrerequisiteId.TailscaleOnline) with
        {
            CodexExecutablePath = @"C:\Tools\codex.exe",
            CodexVersion = "0.50.0",
        };
        failing.Supervisor.OnStart = supervisor =>
        {
            supervisor.Publish(new CheckingStarted(supervisor.Now));
            supervisor.Publish(new PrerequisitesChecked(supervisor.Now, report));
            return Task.FromResult(new StartResult(
                StartOutcome.PrerequisiteFailed, report, null, "Tailscale is not online. Open Tailscale and connect first."));
        };
        await failing.OnUi(s => s.StartAsync());
        Assert.Equal(RelayUiState.PrerequisiteFailed, failing.Session.State);
        Assert.Equal("Tailscale is not online. Open Tailscale and connect first.", failing.Session.StateText);
        Assert.Equal(SessionMessageKind.Warning, Assert.Single(failing.Messages).Kind);
    }

    [Fact]
    public async Task A_command_that_throws_becomes_one_message_and_never_an_exception()
    {
        using var h = new SessionHarness();
        var session = h.Session;

        // A supervisor that throws from Start: the window does not stay "checking".
        h.Supervisor.OnStart = supervisor =>
        {
            supervisor.Publish(new CheckingStarted(supervisor.Now));
            return Task.FromException<StartResult>(new InvalidOperationException("supervisor detail"));
        };
        await h.OnUi(s => s.StartAsync());
        Assert.Equal(RelayUiState.Faulted, session.State);
        Assert.Equal("Could not start the relay.", session.StateText);
        Assert.Equal(CommandFailedText, Assert.Single(h.Messages).Text);
        Assert.Equal(SessionMessageKind.Error, h.Messages[0].Kind);

        // A relay call that throws: the state stays what the relay last confirmed.
        h.Supervisor.OnStart = h.Supervisor.StartsNormally;
        await h.StartAsync();
        h.Messages.Clear();
        h.Relay.Failure = new InvalidOperationException("relay detail");
        await h.OnUi(s => s.TogglePauseAsync());
        await h.OnUi(s => s.ShowPairingAsync());
        Assert.False(await h.OnUi(s => s.ResetPairingAsync()));

        Assert.Equal(RelayUiState.Ready, session.State);
        Assert.False(session.IsPaused);
        Assert.Empty(h.Qrs);
        Assert.Equal([CommandFailedText, CommandFailedText, CommandFailedText], h.Messages.Select(message => message.Text));

        // A mapping manager that throws is a failed command, with nothing changed.
        h.Messages.Clear();
        h.Mappings.OnExecute = null;
        var plan = await h.Read(s => s.PlanSetUp(out _)!);
        var result = await h.OnUi(s => s.ExecuteMappingAsync(plan));
        Assert.Equal(MappingOutcome.CommandFailed, result.Outcome);
        Assert.Equal(CommandFailedText, Assert.Single(h.Messages).Text);

        // Saving: the validator's problems are returned; any other failure is a message.
        h.Messages.Clear();
        SettingsProblem[] problems = [new(SettingsField.RelayPort, SettingsProblemCode.Forbidden)];
        h.Supervisor.SaveFailure = new SettingsValidationException(problems);
        Assert.Equal(problems, await h.Read(s => s.SaveSettings(RelaySettings.Default)));
        Assert.Empty(h.Messages);
        h.Supervisor.SaveFailure = new IOException("disk detail");
        Assert.Empty(await h.Read(s => s.SaveSettings(RelaySettings.Default)));
        Assert.Equal(CommandFailedText, Assert.Single(h.Messages).Text);
    }

    [Fact]
    public async Task Pause_and_show_do_nothing_while_they_cannot_apply()
    {
        using var h = new SessionHarness();

        // Stopped: there is no relay to pause and no connection to show a QR for.
        await h.OnUi(s => s.TogglePauseAsync());
        await h.OnUi(s => s.ShowPairingAsync());
        Assert.Empty(h.Supervisor.CallThreads);

        // Running without a mapping: "Show QR" still has nothing to show.
        await h.StartAsync(MappingState.Absent);
        await h.OnUi(s => s.ShowPairingAsync());
        Assert.Equal(0, h.Relay.ShowPairingCalls);
        Assert.Empty(h.Qrs);
        Assert.Empty(h.Messages);
    }

    [Fact]
    public async Task A_mapping_result_becomes_messages_and_a_done_one_stores_the_new_inspection()
    {
        using var h = new SessionHarness();
        var session = h.Session;
        h.Supervisor.PairingWasCreated = true;
        await h.StartAsync(MappingState.Absent);
        var plan = await h.Read(s => s.PlanSetUp(out _)!);
        Assert.Equal([FakeSupervisor.DnsName], h.Mappings.PlannedWithDnsName);

        // Done: the ledger and the inspection are adopted, the line says so, and the connection that
        // has just become ready brings the first QR by itself.
        var owned = new OwnedMapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:47841", h.Clock.GetUtcNow());
        var saved = session.Settings with { OwnedMappings = [owned] };
        var after = FakeSupervisor.InspectionOf(MappingState.Present);
        h.Mappings.OnExecute = _ => new MappingResult(MappingOutcome.Done, MappingRefusal.None, OthersChanged: true, saved, after);

        var done = await h.OnUi(s => s.ExecuteMappingAsync(plan));

        Assert.Equal(MappingOutcome.Done, done.Outcome);
        Assert.Same(after, session.Connection);
        Assert.Equal([owned], session.Settings.OwnedMappings);
        Assert.True(session.IsConnectionReady);
        Assert.Equal(
            [
                (SessionMessageKind.Info, "Tailscale mapping set up."),
                (SessionMessageKind.Warning, "Warning: other entries of Tailscale's serve configuration changed during this command. LiveCue repaired nothing. Review them with: tailscale serve status"),
            ],
            h.Messages.Select(message => (message.Kind, message.Text)));
        await h.Until(() => h.Qrs.Count == 1, "the first QR");

        // Every other outcome is one line and changes nothing that is shown.
        (MappingResult Result, SessionMessageKind Kind, string Text)[] others =
        [
            (Result(MappingOutcome.Refused, MappingRefusal.PrivatePortOccupied), SessionMessageKind.Warning,
                "Port 11443 already has other Tailscale handlers. Choose another private HTTPS port."),
            (Result(MappingOutcome.CommandFailed), SessionMessageKind.Error,
                "Tailscale HTTPS setup failed. Check that Tailscale is connected."),
            (Result(MappingOutcome.CommandTimedOut), SessionMessageKind.Error,
                "Tailscale did not finish in time. Run the command shown above in a terminal to see Tailscale's own instructions. The first use of HTTPS or Funnel on a tailnet must be approved in the Tailscale admin console."),
            (Result(MappingOutcome.NotVerified), SessionMessageKind.Warning,
                "Tailscale reported success, but the mapping is not in its configuration. Choose Check again."),
            (Result(MappingOutcome.LedgerNotSaved), SessionMessageKind.Warning,
                "The mapping changed, but LiveCue could not save its settings. LiveCue will treat the mapping as not its own."),
        ];
        foreach (var (result, kind, text) in others)
        {
            h.Messages.Clear();
            h.Mappings.OnExecute = _ => result;
            Assert.Same(result, await h.OnUi(s => s.ExecuteMappingAsync(plan)));
            var message = Assert.Single(h.Messages);
            Assert.Equal((kind, text), (message.Kind, message.Text));
            Assert.Same(after, session.Connection);
        }

        // A removal has its own two lines.
        var removal = await h.Read(s => s.PlanRemoval(owned));
        h.Messages.Clear();
        h.Mappings.OnExecute = _ => Result(MappingOutcome.Done);
        await h.OnUi(s => s.ExecuteMappingAsync(removal));
        h.Mappings.OnExecute = _ => Result(MappingOutcome.CommandFailed);
        await h.OnUi(s => s.ExecuteMappingAsync(removal));
        Assert.Equal(
            ["Tailscale mapping removed.", "Tailscale could not remove the mapping. Check that Tailscale is connected."],
            h.Messages.Select(message => message.Text));

        MappingResult Result(MappingOutcome outcome, MappingRefusal refusal = MappingRefusal.None) =>
            new(outcome, refusal, OthersChanged: false, session.Settings, After: null);
    }

    [Fact]
    public async Task Saved_settings_are_shown_and_compared_with_what_the_relay_runs_with()
    {
        using var h = new SessionHarness();
        var session = h.Session;

        // Before anything was loaded the session shows what the supervisor would load.
        Assert.True(session.Settings.SameValues(RelaySettings.Default));
        Assert.Equal(SettingsFileState.Missing, session.SettingsLoad.State);
        Assert.False(session.SettingsDifferFromRunning);

        await h.StartAsync();
        Assert.False(session.SettingsDifferFromRunning);
        h.Changes.Clear();

        // A change that the running relay does not have yet.
        var changed = session.Settings with { RelayPort = 47850 };
        Assert.Empty(await h.Read(s => s.SaveSettings(changed)));
        Assert.Equal([changed], h.Supervisor.Saved);
        Assert.Equal(47850, session.Settings.RelayPort);
        Assert.Equal(SettingsFileState.Loaded, session.SettingsLoad.State);
        Assert.True(session.SettingsDifferFromRunning);
        Assert.Contains(nameof(IDesktopSession.Settings), h.Changes);
        Assert.Contains(nameof(IDesktopSession.SettingsDifferFromRunning), h.Changes);

        // Back to what runs: no difference. Another address for the same port and mode: a difference.
        await h.Read(s => s.SaveSettings(RelaySettings.Default));
        Assert.False(session.SettingsDifferFromRunning);
        await h.Read(s => s.SaveSettings(RelaySettings.Default with { PrivateHttpsPort = 12443 }));
        Assert.True(session.SettingsDifferFromRunning);
        await h.Read(s => s.SaveSettings(RelaySettings.Default with { HideFromScreenCapture = false }));
        Assert.False(session.SettingsDifferFromRunning);

        // Stopped: nothing runs, so nothing differs.
        await h.Read(s => s.SaveSettings(changed));
        await h.OnUi(s => s.StopAsync());
        Assert.False(session.SettingsDifferFromRunning);
    }

    [Fact]
    public async Task Check_again_checks_and_inspects_and_clear_activity_signals_it()
    {
        using var h = new SessionHarness();
        var session = h.Session;
        h.Supervisor.Inspection = FakeSupervisor.InspectionOf(MappingState.Blocked, MappingRefusal.PrivatePortOccupied);

        await h.OnUi(s => s.CheckAgainAsync());

        Assert.Equal((1, 1), (h.Supervisor.CheckCalls, h.Supervisor.InspectCalls));
        Assert.Same(h.Supervisor.Report, session.Prerequisites);
        Assert.Same(h.Supervisor.Inspection, session.Connection);
        Assert.Equal(RelayUiState.Stopped, session.State);
        Assert.Equal(0, h.Supervisor.StartCalls);

        await h.Ui.RunAsync(session.ClearActivity);
        Assert.Equal([SessionSignal.ActivityCleared], h.Signals);
    }

    [Fact]
    public async Task A_disposed_session_lets_its_pending_command_finish()
    {
        var h = new SessionHarness();
        var checks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Supervisor.OnStart = async supervisor =>
        {
            await checks.Task.ConfigureAwait(false);
            return new StartResult(StartOutcome.AlreadyRunning, null, null, null);
        };
        var start = (await h.Read(s => new[] { s.StartAsync() }))[0];
        await h.Until(() => h.Supervisor.StartCalls == 1, "the start call");

        await h.Ui.RunAsync(h.Session.Dispose);
        checks.SetResult();

        await start.WaitAsync(TimeSpan.FromSeconds(10));
        h.Dispose();
    }

    private static RequestStarted Started(string id) =>
        new(ManualStart, id, AnswerKind.Assist, "gpt-test", "low", "Question?", null, null, "{}");

    private static RequestCompleted Completed(string id) =>
        new(ManualStart, id, AnswerKind.Assist, 912, 850, "gpt-test", "low", "{}");

    private static RequestFailed Failed(string id) =>
        new(ManualStart, id, AnswerKind.Assist, "ASSISTANT_FAILED", "The local assistant request failed.");

    private static DateTimeOffset ManualStart => LiveCue.TestSupport.ManualTimeProvider.DefaultStart;

    /// <summary>What every end of a relay leaves behind (the "Both" half of the RelayStopped row).</summary>
    private static void AssertEnded(SessionHarness h)
    {
        var session = h.Session;
        Assert.False(session.IsRelayRunning);
        Assert.Equal(PhonePresence.NotConnected, session.Presence);
        Assert.Null(session.LastPhoneSeen);
        Assert.False(session.IsPaused);
        Assert.False(session.IsConnectionReady);
        Assert.Equal(FakeSupervisor.Endpoint, session.AdvertisedEndpoint);
        Assert.Equal([SessionSignal.RelayStarting, SessionSignal.RelayEnded], h.Signals);
        Assert.IsType<RelayStopped>(h.RelayEvents[^1]);
    }

    /// <summary>
    /// A start that check 6a refuses, as the supervisor plays it: the checks pass, the inspection that
    /// shows the Funnel handler is published, no relay is created and no fault event exists.
    /// </summary>
    private static async Task<ConnectionInspection> StartRefusedForExposure(SessionHarness h)
    {
        var exposure = FakeSupervisor.InspectionOf(
            MappingState.Absent,
            exposure: [new ExposedHandler(10000, "/livecue", FunnelOn: true, Foreground: false, LedgerEntry: null)]);
        h.Supervisor.OnStart = supervisor =>
        {
            supervisor.Publish(new CheckingStarted(supervisor.Now));
            supervisor.Publish(new PrerequisitesChecked(supervisor.Now, supervisor.Report));
            supervisor.Publish(new ConnectionInspected(supervisor.Now, exposure));
            return Task.FromResult(new StartResult(StartOutcome.ExposedInPrivateMode, supervisor.Report, null, ExposedText));
        };
        await h.OnUi(session => session.StartAsync());
        return exposure;
    }

    /// <summary>
    /// A start whose relay finds its port taken: the relay's fault event, the inspection the supervisor
    /// makes after it, and the result with the text the supervisor chose.
    /// </summary>
    private static Task StartFaultsWithPortInUse(SessionHarness h, string resultText)
    {
        h.Supervisor.OnStart = supervisor =>
        {
            supervisor.Publish(new CheckingStarted(supervisor.Now));
            supervisor.Publish(new PrerequisitesChecked(supervisor.Now, supervisor.Report));
            supervisor.Publish(new RelayStarting(supervisor.Now, FakeSupervisor.OptionsFor(RelaySettings.Default)));
            supervisor.Forward(new RelayFaulted(supervisor.Now, RelayFaultCode.PortInUse, "The relay port is already in use."));
            supervisor.Publish(new ConnectionInspected(
                supervisor.Now,
                FakeSupervisor.InspectionOf(
                    MappingState.Present,
                    exposure: [new ExposedHandler(11443, "/", FunnelOn: false, Foreground: false, LedgerEntry: null)])));
            return Task.FromResult(new StartResult(StartOutcome.RelayFault, supervisor.Report, RelayFaultCode.PortInUse, resultText));
        };
        return h.OnUi(session => session.StartAsync());
    }
}
