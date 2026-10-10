using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LiveCue.Relay.Tests;

/// <summary>Start, stop and events, as the desktop app sees them through <see cref="IRelayHost"/>.</summary>
public sealed class RelayHostLifecycleTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Starting_on_port_zero_reports_the_port_that_was_bound()
    {
        await using var host = await RelayTestHost.StartAsync(Cancellation);

        Assert.Equal(RelayHostState.Running, host.Relay.State);
        Assert.InRange(host.Port, 1024, 65535);
        Assert.Equal(new RelayEvent.Started(host.Port), Assert.Single(host.Events));
    }

    [Fact]
    public async Task The_listener_is_bound_to_ipv4_loopback_only()
    {
        await using var host = await RelayTestHost.StartAsync(Cancellation);

        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == host.Port)
            .ToList();

        Assert.Equal(new IPEndPoint(IPAddress.Loopback, host.Port), Assert.Single(listeners));
    }

    [Fact]
    public async Task An_authenticated_request_raises_phone_seen_with_the_clock_time()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 10, 9, 30, 0, TimeSpan.Zero));
        await using var host = await RelayTestHost.StartAsync(Cancellation, clock);

        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        using var response = await host.Client.SendAsync(request, Cancellation);

        Assert.Contains(new RelayEvent.PhoneSeen(clock.GetUtcNow()), host.Events);
    }

    [Fact]
    public async Task A_refused_request_does_not_raise_phone_seen()
    {
        await using var host = await RelayTestHost.StartAsync(Cancellation);

        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health", "Bearer wrong");
        using var response = await host.Client.SendAsync(request, Cancellation);

        Assert.DoesNotContain(host.Events, relayEvent => relayEvent is RelayEvent.PhoneSeen);
    }

    [Fact]
    public async Task Stopping_releases_the_port_and_raises_stopped_once()
    {
        await using var host = await RelayTestHost.StartAsync(Cancellation);
        var port = host.Port;

        await host.Relay.StopAsync(Cancellation);
        await host.Relay.StopAsync(Cancellation);

        Assert.Equal(RelayHostState.Stopped, host.Relay.State);
        Assert.Null(host.Relay.Port);
        Assert.Single(host.Events, relayEvent => relayEvent is RelayEvent.Stopped);

        // The port is free again if something else can bind it.
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
    }

    [Fact]
    public async Task A_stopped_relay_can_be_started_again()
    {
        await using var host = await RelayTestHost.StartAsync(Cancellation);
        await host.Relay.StopAsync(Cancellation);

        await host.Relay.StartAsync(RelayTestHost.Options(), Cancellation);

        Assert.Equal(RelayHostState.Running, host.Relay.State);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{host.Relay.Port}") };
        using var request = RelayTestHost.Request(HttpMethod.Get, "/v1/health");
        using var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Starting_twice_is_an_error_and_leaves_the_first_listener_running()
    {
        await using var host = await RelayTestHost.StartAsync(Cancellation);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Relay.StartAsync(RelayTestHost.Options(), Cancellation));

        Assert.Equal(RelayHostState.Running, host.Relay.State);
        Assert.Equal(host.Port, host.Relay.Port);
    }

    [Fact]
    public async Task A_port_that_is_taken_fails_with_port_in_use_and_returns_to_stopped()
    {
        using var occupier = new TcpListener(IPAddress.Loopback, 0);
        occupier.Start();
        var takenPort = ((IPEndPoint)occupier.LocalEndpoint).Port;
        await using var relay = new RelayHost();
        var events = new List<RelayEvent>();
        relay.EventRaised += (_, relayEvent) => events.Add(relayEvent);

        var exception = await Assert.ThrowsAsync<RelayStartException>(
            () => relay.StartAsync(RelayTestHost.Options(takenPort), Cancellation));

        Assert.Equal(RelayStartFailure.PortInUse, exception.Failure);
        Assert.Equal(takenPort, exception.Port);
        Assert.Equal(RelayHostState.Stopped, relay.State);
        Assert.Empty(events);
    }

    [Fact]
    public async Task The_previous_relays_port_is_refused_before_anything_is_bound()
    {
        await using var relay = new RelayHost();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => relay.StartAsync(RelayTestHost.Options(RelayDefaults.LegacyRelayPort), Cancellation));

        Assert.Equal("Port", exception.ParamName);
        Assert.Equal(RelayHostState.Stopped, relay.State);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    public async Task Ports_outside_the_allowed_range_are_refused(int port)
    {
        await using var relay = new RelayHost();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => relay.StartAsync(RelayTestHost.Options(port), Cancellation));
    }

    [Fact]
    public void The_default_port_is_not_the_previous_relays_port()
    {
        Assert.Equal(47841, RelayDefaults.Port);
        Assert.NotEqual(RelayDefaults.LegacyRelayPort, RelayDefaults.Port);
        Assert.True(RelayOptions.IsAllowedPort(RelayDefaults.Port));
        Assert.False(RelayOptions.IsAllowedPort(RelayDefaults.LegacyRelayPort));
    }

    [Fact]
    public async Task Disposing_stops_a_running_relay_and_forbids_restarting()
    {
        var host = await RelayTestHost.StartAsync(Cancellation);

        await host.DisposeAsync();

        Assert.Equal(RelayHostState.Stopped, host.Relay.State);
        Assert.Single(host.Events, relayEvent => relayEvent is RelayEvent.Stopped);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => host.Relay.StartAsync(RelayTestHost.Options(), Cancellation));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
