using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// The listener of protocol 5.1 (W-design 4.3): protocol P2-015, test plan ST-10, and the settings table.
/// </summary>
public sealed class ListenerTests
{
    [Fact]
    public async Task The_only_endpoint_is_loopback()
    {
        await using var host = await RelayTestHost.StartAsync();
        var port = host.Port;

        // What the server says it listens on, and what the system says is listening on that port.
        Assert.Equal([$"http://127.0.0.1:{port}"], ((RelayHost)host.Relay).ListenerAddresses);
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == port)
            .ToList();
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, port), Assert.Single(listeners));

        // 127.0.0.1 connects. No other address of this machine does: not IPv6 loopback, not a routable one.
        Assert.True(await ConnectsAsync(IPAddress.Loopback, port));
        Assert.False(await ConnectsAsync(IPAddress.IPv6Loopback, port));
        var others = NetworkInterface.GetAllNetworkInterfaces()
            .Where(card => card.OperationalStatus == OperationalStatus.Up)
            .SelectMany(card => card.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => !IPAddress.IsLoopback(address))
            .Distinct()
            .ToList();
        foreach (var address in others)
        {
            Assert.False(await ConnectsAsync(address, port), "The relay answered on an address that is not 127.0.0.1.");
        }
    }

    [Fact]
    public async Task The_listen_socket_is_exclusive()
    {
        await using var host = await RelayTestHost.StartAsync();
        var port = host.Port;

        // A second socket cannot be bound to the relay's address and port while the relay runs: not
        // plainly, and not by asking for address reuse, which on Windows would otherwise let it bind
        // beside an ordinary listener and take its connections.
        Assert.False(Binds(IPAddress.Loopback, port, reuseAddress: false));
        Assert.False(Binds(IPAddress.Loopback, port, reuseAddress: true));

        // Windows keeps the wildcard address apart from 127.0.0.1, so a socket may still listen on the
        // same port number there (with or without address reuse). It is not on the relay's address:
        // whatever is sent to 127.0.0.1 reaches the relay and never that socket.
        var others = new List<Socket>();
        foreach (var reuseAddress in (bool[])[false, true])
        {
            var other = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                if (reuseAddress)
                {
                    other.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                }

                other.Bind(new IPEndPoint(IPAddress.Any, port));
                other.Listen();
                others.Add(other);
            }
            catch (SocketException)
            {
                other.Dispose();
            }
        }

        try
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                using var raw = await host.OpenRawAsync();
                await raw.SendAsync(host.RawHead("GET /v1/health HTTP/1.1"));
                var served = await raw.ReadResponseAsync();
                Assert.Equal(200, served!.Status);
                Assert.Equal("""{"ok":true,"protocol":2,"relayVersion":"1.0.0"}""", served.Body);
            }

            Assert.All(others, other => Assert.False(
                other.Poll(0, SelectMode.SelectRead), "A connection to 127.0.0.1 reached another socket."));
        }
        finally
        {
            others.ForEach(other => other.Dispose());
        }

        // After a stop the port is free again.
        await host.Relay.StopAsync(CancellationToken.None);
        Assert.True(Binds(IPAddress.Loopback, port, reuseAddress: false));
    }

    [Fact]
    public void The_listen_socket_is_made_for_exclusive_use_and_only_on_ipv4_loopback()
    {
        // The server core's own default leaves the option off; the relay's socket has it on, whatever
        // the system would or would not allow a second socket to do without it.
        using (var socket = KestrelSetup.CreateExclusiveLoopbackSocket(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            Assert.True(socket.ExclusiveAddressUse);
            Assert.True(socket.IsBound);
            Assert.Equal(IPAddress.Loopback, ((IPEndPoint)socket.LocalEndPoint!).Address);
            Assert.Equal(AddressFamily.InterNetwork, socket.AddressFamily);
        }

        // Anything but 127.0.0.1 is refused here once more, whatever was configured.
        EndPoint[] others =
        [
            new IPEndPoint(IPAddress.Any, 0),
            new IPEndPoint(IPAddress.IPv6Loopback, 0),
            new IPEndPoint(IPAddress.IPv6Any, 0),
            new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0),
            new DnsEndPoint("localhost", 0),
        ];
        Assert.All(others, endpoint => Assert.Throws<InvalidOperationException>(
            () => KestrelSetup.CreateExclusiveLoopbackSocket(endpoint)));
    }

    [Fact]
    public async Task The_server_core_has_the_limits_of_the_protocol()
    {
        await using var edge = await EdgeHarness.StartAsync();
        var kestrel = edge.ApplicationServices.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var limits = kestrel.Limits;

        Assert.False(kestrel.AddServerHeader);
        Assert.Equal(ProtocolConstants.MaxRequestLineBytes, limits.MaxRequestLineSize);
        Assert.Equal(ProtocolConstants.MaxHeaderBytes, limits.MaxRequestHeadersTotalSize);
        Assert.Equal(ProtocolConstants.MaxHeaderCount, limits.MaxRequestHeaderCount);
        Assert.Equal(ProtocolConstants.HeadersTimeout, limits.RequestHeadersTimeout);
        Assert.Equal(ProtocolConstants.KeepAliveTimeout, limits.KeepAliveTimeout);
        Assert.Equal(ProtocolConstants.MinBodyBytesPerSecond, limits.MinRequestBodyDataRate!.BytesPerSecond);
        Assert.Equal(ProtocolConstants.MinBodyRateGrace, limits.MinRequestBodyDataRate.GracePeriod);
        Assert.Equal(ProtocolConstants.MaxRequestBodyBytes, limits.MaxRequestBodySize);
        Assert.Equal(ProtocolConstants.MaxConnections, limits.MaxConcurrentConnections);
        Assert.Equal(ProtocolConstants.MaxSpeechSockets, limits.MaxConcurrentUpgradedConnections);

        // A response the caller does not read is cut off by the server core's own rate limit, which stays on.
        Assert.NotNull(limits.MinResponseDataRate);
    }

    [Fact]
    public async Task Plain_http_1_1_only()
    {
        await using var host = await RelayTestHost.StartAsync();

        // No TLS: a TLS hello is not HTTP, and the connection ends without an answer of the relay.
        using var tls = await host.OpenRawAsync();
        await tls.SendAsync(new byte[] { 0x16, 0x03, 0x01, 0x00, 0x05, 0x01, 0x00, 0x00, 0x01, 0x00 });
        var toTls = await Wire.ReadRawAsync(tls, timeout: TimeSpan.FromSeconds(15));

        // No HTTP/2: the preface is refused by the server core (with the HTTP/2 frame that says "use 1.1").
        using var h2 = await host.OpenRawAsync();
        await h2.SendAsync("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n");
        var toH2 = await Wire.ReadRawAsync(h2, timeout: TimeSpan.FromSeconds(15));
        await host.Events.SettleAsync();

        // Either a bare 400 or a closed connection (test plan S-12); never an answer of a route.
        Assert.True(toTls is null || toTls.StartsWith("HTTP/1.1 400 ", StringComparison.Ordinal));
        Assert.True(toH2 is null || !toH2.StartsWith("HTTP/1.1", StringComparison.Ordinal));
        Assert.True(await tls.IsClosedByServerAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await h2.IsClosedByServerAsync(TimeSpan.FromSeconds(5)));

        // Nothing reached the edge, and the listener keeps serving.
        Assert.IsType<RelayStarted>(Assert.Single(host.Events.Snapshot()));
        using var served = await host.GetAsync("/v1/health");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    private static async Task<bool> ConnectsAsync(IPAddress address, int port)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(address, port, limit.Token);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static bool Binds(IPAddress address, int port, bool reuseAddress)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            if (reuseAddress)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }

            socket.Bind(new IPEndPoint(address, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
