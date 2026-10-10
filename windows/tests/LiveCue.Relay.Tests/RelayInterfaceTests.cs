using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests;

/// <summary>
/// The in-process interface (architecture 3.4) as a host sees it: start, stop and events. Where a
/// test covers one of the architecture's interface statements, its id is in the name.
/// </summary>
public sealed class RelayInterfaceTests
{
    [Fact]
    public async Task I_01_starting_on_port_zero_raises_relay_started_with_the_port_that_was_bound()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 10, 9, 30, 0, TimeSpan.Zero));
        await using var host = await RelayTestHost.StartAsync(clock: clock);

        Assert.Equal(RelayStatus.Running, host.Relay.Status);
        Assert.InRange(host.Port, 1024, 65535);
        Assert.Equal(
            new RelayStarted(
                clock.GetUtcNow(),
                RelayTestHost.Endpoint,
                host.Port,
                ConnectionMode.Private,
                ProtocolVersion: 2,
                RelayVersion: "1.0.0",
                PairingWasCreated: false),
            Assert.Single(host.Events));
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
    public async Task An_authenticated_request_raises_phone_seen_with_the_clock_time()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 10, 9, 30, 0, TimeSpan.Zero));
        await using var host = await RelayTestHost.StartAsync(clock: clock);

        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        using var response = await host.Client.SendAsync(request);

        Assert.Contains(new PhoneSeen(clock.GetUtcNow()), host.Events);
    }

    [Fact]
    public async Task Stopping_releases_the_port_and_raises_relay_stopped_once()
    {
        await using var host = await RelayTestHost.StartAsync();
        var port = host.Port;

        await host.Relay.StopAsync(CancellationToken.None);
        await host.Relay.StopAsync(CancellationToken.None);

        Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
        Assert.Equal(0, host.Relay.BoundPort);
        var stopped = Assert.Single(host.Events.OfType<RelayStopped>());
        Assert.Equal(RelayStopReason.Requested, stopped.Reason);

        // The port is free again if something else can bind it.
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
    }

    [Fact]
    public async Task I_04_a_port_in_use_throws_port_in_use_raises_relay_faulted_and_leaves_nothing_listening()
    {
        using var occupier = new TcpListener(IPAddress.Loopback, 0);
        occupier.Start();
        var takenPort = ((IPEndPoint)occupier.LocalEndpoint).Port;
        await using var relay = RelayTestHost.Create(RelayTestHost.Options(takenPort));

        var exception = await Assert.ThrowsAsync<RelayStartException>(
            () => relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayFaultCode.PortInUse, exception.Code);
        Assert.Equal(RelayStatus.Faulted, relay.Status);
        Assert.True(relay.Events.TryRead(out var raised));
        Assert.Equal(RelayFaultCode.PortInUse, Assert.IsType<RelayFaulted>(raised).Code);
        Assert.DoesNotContain(takenPort.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message);
    }

    [Fact]
    public async Task I_05_a_new_instance_created_after_a_stop_starts_on_the_same_port()
    {
        await using var first = await RelayTestHost.StartAsync();
        var port = first.Port;
        await first.Relay.StopAsync(CancellationToken.None);

        await using var second = RelayTestHost.Create(RelayTestHost.Options(port));
        await second.StartAsync(CancellationToken.None);

        Assert.Equal(RelayStatus.Running, second.Status);
        Assert.Equal(port, second.BoundPort);
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
    [InlineData("https://pc.example.test:11443")]
    [InlineData("https://pc.example.test:11443/?x=1")]
    [InlineData("https://PC.example.test:11443/")]
    [InlineData("pc.example.test")]
    [InlineData("")]
    public async Task An_endpoint_that_is_not_canonical_is_the_invalid_endpoint_fault(string endpoint)
    {
        await using var relay = RelayTestHost.Create(RelayTestHost.Options(endpoint: endpoint));

        var exception = await Assert.ThrowsAsync<RelayStartException>(
            () => relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayFaultCode.InvalidEndpoint, exception.Code);
        Assert.Equal(RelayStatus.Faulted, relay.Status);
    }

    [Theory]
    [InlineData(ProtocolConstants.PreviousRelayPort)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(-1)]
    public async Task The_previous_relays_port_and_ports_out_of_range_are_refused_before_anything_is_bound(int port)
    {
        await using var relay = RelayTestHost.Create(RelayTestHost.Options(port));

        await Assert.ThrowsAsync<RelayStartException>(() => relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayStatus.Faulted, relay.Status);
    }

    [Fact]
    public void The_default_port_is_47841_and_the_previous_relays_port_is_never_allowed()
    {
        Assert.Equal(47841, ProtocolConstants.DefaultRelayPort);
        Assert.True(ProtocolConstants.IsAllowedRelayPort(ProtocolConstants.DefaultRelayPort));
        Assert.False(ProtocolConstants.IsAllowedRelayPort(47831));
    }

    [Fact]
    public async Task Disposing_stops_a_running_relay_and_completes_the_event_channel()
    {
        var host = await RelayTestHost.StartAsync();

        await host.DisposeAsync();

        Assert.Equal(RelayStatus.Stopped, host.Relay.Status);
        Assert.Single(host.Events.OfType<RelayStopped>());
        Assert.True(host.Relay.Events.Completion.IsCompleted);
    }

    [Fact]
    public async Task Without_a_pairing_store_no_token_is_accepted()
    {
        // The production relay of the spike: nothing injected, so nobody knows the token.
        await using var relay = RelayFactory.Create(RelayTestHost.Options());
        await relay.StartAsync(CancellationToken.None);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{relay.BoundPort}") };

        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void The_relay_library_references_no_other_project_and_no_desktop_framework()
    {
        var references = typeof(IRelay).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        Assert.DoesNotContain(references, name => name.StartsWith("LiveCue.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name is "PresentationFramework" or "PresentationCore" or "WindowsBase");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
