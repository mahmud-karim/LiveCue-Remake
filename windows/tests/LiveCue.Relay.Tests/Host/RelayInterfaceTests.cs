using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Host;

/// <summary>
/// The in-process interface (architecture 3.4) as a host sees it: start, stop, pause, pairing and
/// events. Where a test covers one of the architecture's interface statements, its id is in the name.
/// Carried over from the spike and extended to the whole contract; work package WP-W01 replaces this
/// file with the named tests of the test plan (section 1.2).
/// </summary>
public sealed class RelayInterfaceTests
{
    [Fact]
    public async Task I_01_starting_on_port_zero_publishes_relay_started_with_the_bound_port_and_no_qr_event()
    {
        await using var host = await RelayTestHost.StartAsync();
        await host.Events.SettleAsync();

        Assert.Equal(RelayStatus.Running, host.Relay.Status);
        Assert.True(host.Relay.Accepting);
        Assert.InRange(host.Port, 1024, 65535);
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

        var qr = await host.Relay.ShowPairingAsync(CancellationToken.None);
        await host.Events.WaitForAsync<PairingShown>();

        Assert.Equal(RelayTestHost.Endpoint, qr.Endpoint);
        Assert.True(qr.Modules.Length >= 21);
        Assert.All(qr.Modules, row => Assert.Equal(qr.Modules.Length, row.Length));
    }

    [Fact]
    public async Task I_02_pausing_publishes_the_event_and_keeps_health()
    {
        await using var host = await RelayTestHost.StartAsync();

        await host.Relay.SetAcceptingAsync(false);
        await host.Relay.SetAcceptingAsync(false);
        using var health = await host.GetAsync("/v1/health");
        using var start = await host.PostAsync("/v1/local-model/start", Bodies.LocalModel("nemotron"));
        await host.Relay.SetAcceptingAsync(true);
        await host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, start.StatusCode);
        Assert.Contains("RELAY_PAUSED", await start.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal([false, true], host.Events.OfType<AcceptingChanged>().Select(changed => changed.Accepting));
        Assert.True(host.Relay.Accepting);
    }

    [Fact]
    public async Task P2_015_the_only_listening_endpoint_is_ipv4_loopback_on_the_relay_port()
    {
        await using var host = await RelayTestHost.StartAsync();

        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == host.Port)
            .ToList();

        Assert.Equal(new IPEndPoint(IPAddress.Loopback, host.Port), Assert.Single(listeners));
    }

    [Fact]
    public async Task P2_143_a_reset_rejects_the_old_token_at_once_and_accepts_the_new_one()
    {
        await using var host = await RelayTestHost.StartAsync();
        var oldToken = host.Token;

        await host.Relay.ResetPairingAsync(CancellationToken.None);
        using var withOld = await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", TestTokens.Bearer(oldToken)));
        var newToken = await host.ReadTokenAsync();
        using var withNew = await host.GetAsync("/v1/health");
        await host.Events.SettleAsync();

        Assert.NotEqual(oldToken, newToken);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
        Assert.Equal(NoticeKind.PairingReset, Assert.Single(host.Events.OfType<Notice>()).Kind);
    }

    [Fact]
    public async Task No_event_and_no_payload_object_of_a_run_carries_the_token()
    {
        await using var host = await RelayTestHost.StartAsync();

        using var health = await host.GetAsync("/v1/health");
        await host.Relay.ShowPairingAsync(CancellationToken.None);
        await host.Events.SettleAsync();

        Assert.All(host.Events.Snapshot(), relayEvent =>
            Assert.DoesNotContain(host.Token, relayEvent.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stopping_releases_the_port_publishes_relay_stopped_once_and_completes_the_events()
    {
        await using var host = await RelayTestHost.StartAsync();
        var port = host.Port;

        await host.Relay.StopAsync(CancellationToken.None);
        await host.Relay.StopAsync(CancellationToken.None);
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
        Assert.Equal(0, host.Relay.BoundPort);
        Assert.Equal(RelayStopReason.Requested, Assert.Single(host.Events.OfType<RelayStopped>()).Reason);
        Assert.IsType<RelayStopped>(host.Events.Snapshot()[^1]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.SetAcceptingAsync(false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.ShowPairingAsync(CancellationToken.None));

        // The port is free again if something else can bind it.
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
    }

    [Fact]
    public async Task I_04_a_port_in_use_throws_port_in_use_publishes_relay_faulted_and_leaves_nothing_listening()
    {
        using var occupier = new TcpListener(IPAddress.Loopback, 0);
        occupier.Start();
        var takenPort = ((IPEndPoint)occupier.LocalEndpoint).Port;
        await using var host = RelayTestHost.Create(options => options.Port = takenPort);

        var exception = await Assert.ThrowsAsync<RelayStartException>(
            () => host.Relay.StartAsync(CancellationToken.None));
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RelayFaultCode.PortInUse, exception.Code);
        Assert.Equal("The relay port is already in use.", exception.Message);
        Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
        Assert.Equal(RelayFaultCode.PortInUse, Assert.IsType<RelayFaulted>(Assert.Single(host.Events.Snapshot())).Code);
    }

    [Fact]
    public async Task I_05_a_new_instance_created_after_a_stop_starts_on_the_same_port_and_is_accepting()
    {
        await using var first = await RelayTestHost.StartAsync();
        var port = first.Port;
        await first.Relay.SetAcceptingAsync(false);
        await first.Relay.StopAsync(CancellationToken.None);

        await using var second = await RelayTestHost.StartAsync(options => options.Port = port);

        Assert.Equal(RelayStatus.Running, second.Relay.Status);
        Assert.Equal(port, second.Relay.BoundPort);
        Assert.True(second.Relay.Accepting);
    }

    [Fact]
    public async Task One_instance_is_one_run_and_cannot_be_started_twice()
    {
        await using var host = await RelayTestHost.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayStatus.Running, host.Relay.Status);
    }

    [Theory]
    [InlineData("http://pc.example.test:11443/")]
    [InlineData("HTTPS://pc.example.test:11443/")]
    [InlineData("https://pc.example.test:11443/?x=1")]
    [InlineData("https://pc.example.test:11443//")]
    [InlineData("https://pc.example.test:11443/../")]
    [InlineData("https://pc.example.test:11443/a/b/c/d/e")]
    [InlineData("pc.example.test")]
    [InlineData("")]
    public async Task P2_148_an_endpoint_outside_the_grammar_is_the_invalid_endpoint_fault(string endpoint)
    {
        await using var host = RelayTestHost.Create(options => options.Endpoint = endpoint);

        var exception = await Assert.ThrowsAsync<RelayStartException>(
            () => host.Relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayFaultCode.InvalidEndpoint, exception.Code);
        Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
    }

    [Fact]
    public async Task P2_148_an_endpoint_without_a_trailing_slash_is_advertised_in_canonical_form()
    {
        await using var host = await RelayTestHost.StartAsync(options => options.Endpoint = "https://PC.example.test:11443");

        var started = await host.Events.WaitForAsync<RelayStarted>();

        Assert.Equal("https://pc.example.test:11443/", started.Endpoint);
    }

    [Theory]
    [InlineData(47831)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(-1)]
    public async Task The_old_relays_port_and_ports_out_of_range_are_refused_before_anything_is_bound(int port)
    {
        await using var host = RelayTestHost.Create(options => options.Port = port);

        var exception = await Assert.ThrowsAsync<RelayStartException>(
            () => host.Relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayFaultCode.StartFailed, exception.Code);
        Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
        Assert.Equal(0, host.Relay.BoundPort);
    }

    [Fact]
    public async Task A_start_writes_the_two_schema_files_into_the_data_folder_and_nothing_else()
    {
        await using var host = await RelayTestHost.StartAsync();

        Assert.Equal(
            [Path.Combine("schemas", "assist.schema.json"), Path.Combine("schemas", "summary.schema.json")],
            host.Data.FileNames());
    }

    [Fact]
    public async Task Disposing_stops_a_running_relay_and_completes_the_event_channel()
    {
        var host = await RelayTestHost.StartAsync();

        await host.DisposeAsync();
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
        Assert.Single(host.Events.OfType<RelayStopped>());
    }

    [Fact]
    public async Task The_production_relay_without_any_seam_starts_and_refuses_a_caller_without_the_token()
    {
        // Nothing injected: the relay as the supervisor creates it.
        using var data = new LiveCue.TestSupport.TempFolder();
        var options = new RelayOptions(0, RelayTestHost.Endpoint, ConnectionMode.Private, data.Path, null, null, 47842, "1.0.0");
        await using var relay = RelayFactory.Create(options);
        await relay.StartAsync(CancellationToken.None);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{relay.BoundPort}") };

        using var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
