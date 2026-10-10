using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LiveCue.Relay.Core;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Host;

/// <summary>
/// The life cycle of one relay instance as a host sees it (architecture 3.4, statements I-01 to I-06;
/// W-design 4.2): start, stop, pause, faults, and the order of the events.
/// </summary>
public sealed class RelayLifecycleTests
{
    [Fact]
    public async Task An_empty_folder_starts_with_a_created_pairing_and_no_qr_event()
    {
        await using var host = await RelayTestHost.StartAsync();
        await host.Events.SettleAsync();

        // I-01, first half: RelayStarted with PairingWasCreated true, and nothing else.
        Assert.Equal(RelayStatus.Running, host.Relay.Status);
        Assert.True(host.Relay.Accepting);
        Assert.InRange(host.Port, 1024, 65535);
        Assert.Equal(host.Port, host.Relay.BoundPort);
        Assert.Equal(
            new RelayStarted(
                host.Clock.GetUtcNow(),
                RelayTestHost.Endpoint,
                host.Port,
                ConnectionMode.Private,
                ProtocolVersion: 2,
                RelayVersion: "1.0.0",
                PairingWasCreated: true),
            Assert.Single(host.Events.Snapshot()));
        Assert.Empty(host.Events.OfType<PairingShown>());

        // Second half: showing the pairing returns a square matrix and raises PairingShown.
        var qr = await host.Relay.ShowPairingAsync(CancellationToken.None);
        var shown = await host.Events.WaitForAsync<PairingShown>();

        Assert.Equal(RelayTestHost.Endpoint, qr.Endpoint);
        Assert.Equal(RelayTestHost.Endpoint, shown.Endpoint);
        Assert.True(qr.Modules.Length >= 21);
        Assert.All(qr.Modules, row => Assert.Equal(qr.Modules.Length, row.Length));
    }

    [Fact]
    public async Task Pause_raises_the_event_refuses_assist_and_keeps_health()
    {
        await using var host = await RelayTestHost.StartAsync();

        await host.Relay.SetAcceptingAsync(false);
        Assert.False(host.Relay.Accepting);

        // Calling it with the current value does nothing and publishes nothing.
        await host.Relay.SetAcceptingAsync(false);
        using var health = await host.GetAsync("/v1/health");
        using var assist = await host.PostAsync("/v1/assist", Bodies.Assist());
        using var modelStart = await host.PostAsync("/v1/local-model/start", Bodies.LocalModel("nemotron"));
        await host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        // An answer request is not served while paused: no run starts and nothing reaches the desktop as
        // a request. The refusal itself is the answer area's (RELAY_PAUSED, protocol P2-051); a route
        // whose area checks the pause flag today gives exactly that.
        Assert.False(assist.IsSuccessStatusCode);
        Assert.Empty(host.Backend.Runs);
        Assert.Empty(host.Events.OfType<RequestStarted>());
        await Wire.AssertErrorAsync(modelStart, 503, "RELAY_PAUSED");
        Assert.Equal(0, host.Lab.StartCalls);

        await host.Relay.SetAcceptingAsync(true);
        await host.Relay.SetAcceptingAsync(true);
        await host.Events.SettleAsync();

        Assert.True(host.Relay.Accepting);
        Assert.Equal(
            [new AcceptingChanged(host.Clock.GetUtcNow(), false), new AcceptingChanged(host.Clock.GetUtcNow(), true)],
            host.Events.OfType<AcceptingChanged>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_port_in_use_is_a_start_fault_and_leaves_nothing_listening(bool heldByAnotherRelay)
    {
        // The port is taken by an ordinary listener, or by another relay (a second LiveCue window).
        using var occupier = new TcpListener(IPAddress.Loopback, 0);
        await using var other = heldByAnotherRelay ? await RelayTestHost.StartAsync() : null;
        if (other is null)
        {
            occupier.Start();
        }

        var taken = other?.Port ?? ((IPEndPoint)occupier.LocalEndpoint).Port;
        await using var host = RelayTestHost.Create(options => options.Port = taken);

        var exception = await Assert.ThrowsAsync<RelayStartException>(() => host.Relay.StartAsync(CancellationToken.None));
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RelayFaultCode.PortInUse, exception.Code);
        Assert.Equal("The relay port is already in use.", exception.Message);
        Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
        Assert.Equal(0, host.Relay.BoundPort);
        Assert.Equal(
            new RelayFaulted(host.Clock.GetUtcNow(), RelayFaultCode.PortInUse, "The relay port is already in use."),
            Assert.Single(host.Events.Snapshot()));

        // Nothing of the failed instance listens: the port has the one listener it had before.
        Assert.Single(
            IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners(), endpoint => endpoint.Port == taken);

        // The first relay was not disturbed, and the failed one can be stopped and disposed without harm.
        if (other is not null)
        {
            using var served = await other.GetAsync("/v1/health");
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        }

        await host.Relay.StopAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.StartAsync(CancellationToken.None));
        Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
    }

    [Fact]
    public async Task A_new_instance_is_accepting()
    {
        await using var first = await RelayTestHost.StartAsync();
        var port = first.Port;
        await first.Relay.SetAcceptingAsync(false);
        await first.Relay.StopAsync(CancellationToken.None);

        // I-05: whatever the previous instance's pause state was. Here on the very same port.
        await using var second = await RelayTestHost.StartAsync(options => options.Port = port);
        using var served = await second.PostAsync("/v1/local-model/stop", Bodies.LocalModel("nemotron"));

        Assert.False(first.Relay.Accepting);
        Assert.Equal(RelayStatus.Running, second.Relay.Status);
        Assert.Equal(port, second.Relay.BoundPort);
        Assert.True(second.Relay.Accepting);
        Assert.DoesNotContain("RELAY_PAUSED", await served.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_existing_pairing_raises_the_restored_notice_and_no_qr_event()
    {
        // A first run creates the pairing; its file is what the second run finds.
        await using var first = await RelayTestHost.StartAsync();
        var file = first.PairingFiles.Get(Path.Combine(first.Data.Path, "pairing.json"));
        Assert.NotNull(file);
        await first.Relay.StopAsync(CancellationToken.None);

        await using var second = RelayTestHost.Create();
        second.PairingFiles.Put(Path.Combine(second.Data.Path, "pairing.json"), file);
        await second.StartRelayAsync();
        await second.Events.SettleAsync();

        // I-06: started with PairingWasCreated false, then the notice, and no QR by itself.
        var events = second.Events.Snapshot();
        Assert.Equal(2, events.Count);
        Assert.False(Assert.IsType<RelayStarted>(events[0]).PairingWasCreated);
        Assert.Equal(
            new Notice(
                second.Clock.GetUtcNow(),
                NoticeKind.PairingRestored,
                "Saved pairing restored. Existing iPhones reconnect automatically; Show QR keeps their pairing valid."),
            events[1]);
        Assert.Empty(second.Events.OfType<PairingShown>());

        // The phone that was paired with the first run still is.
        Assert.Equal(first.Token, second.Token);
        using var served = await second.GetAsync("/v1/health");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    [Theory]
    [InlineData(47831)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(-1)]
    public async Task Start_refuses_port_47831_without_binding_it(int port)
    {
        // The old relay's port, and ports outside 1024 to 65535 (0 picks a free one).
        await using var host = RelayTestHost.Create(options => options.Port = port);

        var exception = await Assert.ThrowsAsync<RelayStartException>(() => host.Relay.StartAsync(CancellationToken.None));
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RelayFaultCode.StartFailed, exception.Code);
        Assert.Equal("Could not start the relay.", exception.Message);
        Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
        Assert.Equal(0, host.Relay.BoundPort);
        Assert.Equal(
            new RelayFaulted(host.Clock.GetUtcNow(), RelayFaultCode.StartFailed, "Could not start the relay."),
            Assert.Single(host.Events.Snapshot()));

        // Refused at step 3, long before the listener: no pairing was loaded or created, nothing was written.
        Assert.Empty(host.PairingFiles.Paths);
        Assert.Equal(0, host.PairingFiles.Reads);
        Assert.Empty(host.Data.FileNames());
        Assert.Null(((RelayHost)host.Relay).Edge);
    }

    [Fact]
    public async Task A_data_folder_that_does_not_exist_is_a_start_fault()
    {
        await using var host = RelayTestHost.Create();
        host.Data.Dispose();

        var exception = await Assert.ThrowsAsync<RelayStartException>(() => host.Relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayFaultCode.StartFailed, exception.Code);
        Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
        Assert.False(Directory.Exists(host.Data.Path));
    }

    [Fact]
    public async Task An_unreadable_pairing_file_is_its_own_fault_and_nothing_listens()
    {
        await using var host = RelayTestHost.Create();
        host.PairingFiles.Put(Path.Combine(host.Data.Path, "pairing.json"), "not a pairing file"u8.ToArray());

        var exception = await Assert.ThrowsAsync<RelayStartException>(() => host.Relay.StartAsync(CancellationToken.None));
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RelayFaultCode.PairingStoreUnreadable, exception.Code);
        Assert.Equal("The saved pairing could not be read.", exception.Message);
        Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
        Assert.Equal(RelayFaultCode.PairingStoreUnreadable, Assert.IsType<RelayFaulted>(Assert.Single(host.Events.Snapshot())).Code);
        Assert.Null(((RelayHost)host.Relay).Edge);
    }

    [Fact]
    public async Task One_instance_is_started_once()
    {
        await using var host = await RelayTestHost.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.StartAsync(CancellationToken.None));
        Assert.Equal(RelayStatus.Running, host.Relay.Status);

        await host.Relay.StopAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.StartAsync(CancellationToken.None));
        Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
    }

    [Fact]
    public async Task A_start_that_is_cancelled_before_it_began_leaves_the_instance_as_it_was()
    {
        await using var host = RelayTestHost.Create();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Relay.StartAsync(cancelled.Token));
        Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
        Assert.Equal(0, host.Relay.BoundPort);

        await host.StartRelayAsync();
        Assert.Equal(RelayStatus.Running, host.Relay.Status);
    }

    [Fact]
    public async Task Before_a_start_nothing_can_be_asked_of_the_relay_and_a_stop_does_nothing()
    {
        await using var host = RelayTestHost.Create();

        Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
        Assert.True(host.Relay.Accepting);
        Assert.Equal(0, host.Relay.BoundPort);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.SetAcceptingAsync(false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.ShowPairingAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.ResetPairingAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((IPairingExport)host.Relay).GetPairingPayloadAsync(CancellationToken.None));

        // A stop of a relay that never started is not its one stop: it still starts afterwards.
        await host.Relay.StopAsync(CancellationToken.None);
        Assert.False(host.Relay.Events.Completion.IsCompleted);
        await host.StartRelayAsync();
        Assert.Equal(RelayStatus.Running, host.Relay.Status);
    }

    [Fact]
    public async Task Stop_is_idempotent()
    {
        await using var host = await RelayTestHost.StartAsync();
        var port = host.Port;

        // Three callers at once, and one more afterwards: one stop.
        await Task.WhenAll(
            host.Relay.StopAsync(CancellationToken.None),
            host.Relay.StopAsync(CancellationToken.None),
            host.Relay.StopAsync(CancellationToken.None));
        await host.Relay.StopAsync(CancellationToken.None);
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
        Assert.Equal(0, host.Relay.BoundPort);
        Assert.Equal(RelayStopReason.Requested, Assert.Single(host.Events.OfType<RelayStopped>()).Reason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.SetAcceptingAsync(false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.ShowPairingAsync(CancellationToken.None));

        // The port is free again: something else can bind it.
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
    }

    [Fact]
    public async Task The_event_channel_completes_after_stopped()
    {
        await using var host = await RelayTestHost.StartAsync();
        using (await host.GetAsync("/v1/health"))
        {
        }

        Assert.False(host.Relay.Events.Completion.IsCompleted);
        await host.Relay.StopAsync(CancellationToken.None);
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await host.Relay.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // RelayStopped is the last event of an instance that had started; the status changed before it.
        var events = host.Events.Snapshot();
        Assert.IsType<RelayStarted>(events[0]);
        Assert.Equal(new RelayStopped(host.Clock.GetUtcNow(), RelayStopReason.Requested), events[^1]);
        Assert.Single(events.OfType<RelayStopped>());
        Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
    }

    [Fact]
    public async Task A_stop_does_not_answer_a_request_in_flight_and_ends_at_once()
    {
        await using var host = await RelayTestHost.StartAsync();
        var edge = ((RelayHost)host.Relay).Edge!;
        host.Catalog.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var raw = await host.OpenRawAsync();
        await raw.SendAsync(host.RawHead("GET /v1/models HTTP/1.1"));
        await Wire.UntilAsync(() => edge.Gauge.InFlight == 1, "the request is held");
        var watch = Stopwatch.StartNew();
        await host.Relay.StopAsync(CancellationToken.None);
        var stopTook = watch.Elapsed;
        var answer = await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(5));
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // The request ended when it was told to; the caller sees a transport failure, not a response.
        Assert.Null(answer);
        Assert.Equal(0, edge.Active);
        Assert.Equal(0, edge.Gauge.InFlight);
        Assert.True(stopTook < TimeSpan.FromSeconds(2), $"The stop took {stopTook}.");
        Assert.IsType<RelayStopped>(host.Events.Snapshot()[^1]);
        Assert.Empty(host.Events.OfType<Diagnostic>());
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task A_stop_is_bounded_in_real_time_even_when_the_relays_clock_does_not_move()
    {
        // A request whose work ignores every signal, on a clock that never advances: neither C-58 nor the
        // stop budget of the relay's clock can fire. The listener's own time-out, in real time, still
        // ends the stop.
        await using var host = await RelayTestHost.StartAsync();
        var port = host.Port;
        using var blocker = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Catalog.Loader = () =>
        {
            entered.TrySetResult();
            blocker.Wait(TimeSpan.FromSeconds(30));
            return FakeCatalogSource.Fixture;
        };

        try
        {
            using var raw = await host.OpenRawAsync();
            await raw.SendAsync(host.RawHead("GET /v1/models HTTP/1.1"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var watch = Stopwatch.StartNew();
            await host.Relay.StopAsync(CancellationToken.None);
            var stopTook = watch.Elapsed;
            await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.InRange(stopTook, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));
            Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
            Assert.IsType<RelayStopped>(host.Events.Snapshot()[^1]);
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        finally
        {
            blocker.Set();
        }
    }

    [Fact]
    public async Task The_stop_budget_of_the_relays_clock_ends_a_stop_that_waits()
    {
        // The same stubborn request; this time the relay's clock passes C-58 and then W-02.
        await using var host = await RelayTestHost.StartAsync();
        using var blocker = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Catalog.Loader = () =>
        {
            entered.TrySetResult();
            blocker.Wait(TimeSpan.FromSeconds(30));
            return FakeCatalogSource.Fixture;
        };

        try
        {
            using var raw = await host.OpenRawAsync();
            await raw.SendAsync(host.RawHead("GET /v1/models HTTP/1.1"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var timersBefore = host.Clock.ScheduledTimers;
            var watch = Stopwatch.StartNew();
            var stop = host.Relay.StopAsync(CancellationToken.None);
            await Wire.UntilAsync(() => host.Clock.ScheduledTimers >= timersBefore + 2, "the stop waits on its two timers");
            Assert.Equal(RelayStatus.Stopping, host.Relay.Status);
            host.Clock.Advance(LiveCue.Relay.Protocol.ProtocolConstants.StopBudget);
            await stop;

            // Not the real-time bound of the listener (W-02 in real time), which would take longer.
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2.3), $"The stop took {watch.Elapsed}.");
            Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
        }
        finally
        {
            blocker.Set();
        }
    }

    [Fact]
    public async Task A_fault_while_running_publishes_faulted_then_stops_and_publishes_stopped_with_reason_fault()
    {
        await using var host = await RelayTestHost.StartAsync();
        var relay = (RelayHost)host.Relay;
        var port = host.Port;
        using (await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", authorization: null)))
        {
        }

        await relay.FaultAsync("A periodic report failed.", new InvalidOperationException("PRIVATE DIAGNOSTIC"));
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // The status is Faulted from before the first event of the fault, and stays so.
        var now = host.Clock.GetUtcNow();
        Assert.Equal(RelayStatus.Faulted, relay.Status);
        Assert.Equal(0, relay.BoundPort);
        Assert.Equal(
            [
                new Diagnostic(now, "relay", "A periodic report failed. (InvalidOperationException)"),
                new RelayFaulted(now, RelayFaultCode.Internal, "The relay stopped after an internal error."),
                new EdgeStatistics(now, 1, 0, 0),
                new RelayStopped(now, RelayStopReason.Fault),
            ],
            host.Events.Snapshot().Skip(1));

        // The listener is closed, the relay does not restart itself, and later calls do no harm.
        using (var listener = new TcpListener(IPAddress.Loopback, port))
        {
            listener.Start();
        }

        await relay.FaultAsync("Again.", null);
        await relay.StopAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => relay.SetAcceptingAsync(true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => relay.StartAsync(CancellationToken.None));
        Assert.Equal(RelayStatus.Faulted, relay.Status);
        Assert.Single(host.Events.OfType<RelayStopped>());
    }

    [Fact]
    public async Task Disposing_stops_a_running_relay_and_completes_the_events()
    {
        var host = await RelayTestHost.StartAsync();
        var relay = host.Relay;

        await host.DisposeAsync();
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await relay.DisposeAsync();

        Assert.Equal(RelayStatus.Stopped, relay.Status);
        Assert.Single(host.Events.OfType<RelayStopped>());
        Assert.False(relay.Accepting);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => relay.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Disposing_a_relay_that_never_started_completes_the_events_without_any()
    {
        var host = RelayTestHost.Create();

        await host.DisposeAsync();
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(host.Events.Snapshot());
        Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
    }

    [Fact]
    public async Task A_start_writes_the_schemas_and_the_launcher_copies_and_nothing_else()
    {
        await using var host = await RelayTestHost.StartAsync();

        // Step 5. The pairing file of this host is kept in memory; the per-request folders are elsewhere
        // (the host passes its own TempRoot), so no work folder is made.
        Assert.Equal(
            [
                Path.Combine("lab", "LabCommon.ps1"),
                Path.Combine("lab", "Start-ASR-Lab.ps1"),
                Path.Combine("lab", "Stop-ASR-Lab.ps1"),
                Path.Combine("schemas", "assist.schema.json"),
                Path.Combine("schemas", "summary.schema.json"),
            ],
            host.Data.FileNames());
        Assert.False(Directory.Exists(host.Data.Combine("work")));
    }

    [Fact]
    public async Task The_production_relay_without_any_seam_starts_and_refuses_a_caller_without_the_token()
    {
        // Nothing injected: the relay as the supervisor creates it, with the real file system and DPAPI.
        using var data = new LiveCue.TestSupport.TempFolder();
        var options = new RelayOptions(0, RelayTestHost.Endpoint, ConnectionMode.Private, data.Path, null, null, 47842, "1.0.0");
        await using var relay = RelayFactory.Create(options);
        await relay.StartAsync(CancellationToken.None);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{relay.BoundPort}") };

        using var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));
        var files = data.FileNames();
        await relay.StopAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Wire.Unauthorized, await response.Content.ReadAsStringAsync());

        // Exactly what architecture 3.5 lists, and an empty work folder.
        Assert.Equal(
            [
                Path.Combine("lab", "LabCommon.ps1"),
                Path.Combine("lab", "Start-ASR-Lab.ps1"),
                Path.Combine("lab", "Stop-ASR-Lab.ps1"),
                "pairing.json",
                Path.Combine("schemas", "assist.schema.json"),
                Path.Combine("schemas", "summary.schema.json"),
            ],
            files);
        Assert.True(Directory.Exists(Path.Combine(data.Path, "work")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(data.Path, "work")));
    }

    [Fact]
    public async Task The_catalogue_is_warmed_once_at_start_when_codex_was_found_and_its_outcome_is_ignored()
    {
        await using var warmed = RelayTestHost.Create(options => options.WarmCatalogOnStart = true);
        warmed.Catalog.Fails = true;
        await warmed.StartRelayAsync();
        await Wire.UntilAsync(() => warmed.Catalog.Calls >= 1, "the warm-up read ran");

        await using var noCodex = await RelayTestHost.StartAsync(options =>
        {
            options.WarmCatalogOnStart = true;
            options.CodexExecutablePath = null;
        });
        await using var notAsked = await RelayTestHost.StartAsync();
        await Task.Delay(50);

        Assert.Equal(RelayStatus.Running, warmed.Relay.Status);
        Assert.Equal(0, noCodex.Catalog.Calls);
        Assert.Equal(0, notAsked.Catalog.Calls);
    }
}
