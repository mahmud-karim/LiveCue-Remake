using System.Net;
using System.Net.Sockets;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiveCue.Relay.Edge;

/// <summary>
/// The listener of protocol 5.1 (W-design 4.3): Kestrel inside the current process, on 127.0.0.1 only,
/// HTTP/1.1 only, with the parser limits and timeouts of the protocol. No configuration file or
/// environment variable is read, no logging provider exists, there is no host filtering, no endpoint
/// routing and no developer exception page: the pipeline is WebSockets followed by one terminal delegate.
/// </summary>
internal static class KestrelSetup
{
    public static WebApplication Build(RelayContext context, EdgePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pipeline);

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());

        // The socket transport is the one UseKestrelCore registers; only the way it opens the listen socket changes.
        builder.Services.Configure<SocketTransportOptions>(
            sockets => sockets.CreateBoundListenSocket = CreateExclusiveLoopbackSocket);

        builder.WebHost
            .UseKestrelCore()
            .ConfigureKestrel(kestrel =>
            {
                kestrel.AddServerHeader = false;

                var limits = kestrel.Limits;
                limits.MaxRequestLineSize = ProtocolConstants.MaxRequestLineBytes;
                limits.MaxRequestHeadersTotalSize = ProtocolConstants.MaxHeaderBytes;
                limits.MaxRequestHeaderCount = ProtocolConstants.MaxHeaderCount;
                limits.RequestHeadersTimeout = ProtocolConstants.HeadersTimeout;
                limits.KeepAliveTimeout = ProtocolConstants.KeepAliveTimeout;
                limits.MinRequestBodyDataRate = new MinDataRate(
                    ProtocolConstants.MinBodyBytesPerSecond, ProtocolConstants.MinBodyRateGrace);

                // Second lines behind BodyReader, ConnectionGate and the speech route's own count.
                limits.MaxRequestBodySize = ProtocolConstants.MaxRequestBodyBytes;
                limits.MaxConcurrentConnections = ProtocolConstants.MaxConnections;
                limits.MaxConcurrentUpgradedConnections = ProtocolConstants.MaxSpeechSockets;

                // 127.0.0.1 only: never IPv6, never a wildcard, never a routable address. HTTP/1.1 only.
                kestrel.Listen(IPAddress.Loopback, context.Options.Port, listen =>
                {
                    listen.Protocols = HttpProtocols.Http1;

                    // P-0: every connection is counted here, before a byte of it is parsed.
                    listen.Use(pipeline.Connections.Wrap);
                });
            });

        // A second line behind the relay's own stop budget, in real time: it bounds the whole stop of the
        // application, the wait of DrainBeforeStop included, also when the relay's clock does not move.
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = ProtocolConstants.StopBudget);

        // The host process owns Ctrl+C and shutdown; the relay installs no console handler.
        builder.Services.AddSingleton<IHostLifetime, HostOwnedLifetime>();

        // The first thing the application does when it is told to stop, while the listener is still open.
        builder.Services.AddSingleton<IHostedService>(new DrainBeforeStop(pipeline, context.Clock));

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            // C-57. The keep-alive frame is an unsolicited Pong (K-2): KeepAliveTimeout stays infinite,
            // so nothing is required of the phone and no Ping is ever sent.
            KeepAliveInterval = ProtocolConstants.KeepAliveFrameInterval,
            KeepAliveTimeout = Timeout.InfiniteTimeSpan,
        });
        app.Run(pipeline.InvokeAsync);
        return app;
    }

    /// <summary>The loopback port Kestrel bound (the one that was asked for, or the one port 0 picked).</summary>
    public static int ReadBoundPort(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not report the address it bound.");
        return new Uri(address).Port;
    }

    /// <summary>The addresses the server reports, for the test that proves there is exactly one.</summary>
    public static IReadOnlyList<string> ReadAddresses(WebApplication app) =>
        [.. app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? []];

    /// <summary>
    /// The listen socket of protocol 5.1: IPv4 loopback, opened for exclusive use of the address, so that
    /// no second socket can share the port while the relay runs (on Windows a socket that asks for address
    /// reuse could otherwise bind beside it and take its requests). Anything but 127.0.0.1 is refused
    /// here once more, whatever was configured above.
    /// </summary>
    internal static Socket CreateExclusiveLoopbackSocket(EndPoint endpoint)
    {
        if (endpoint is not IPEndPoint { AddressFamily: AddressFamily.InterNetwork } loopback
            || !loopback.Address.Equals(IPAddress.Loopback))
        {
            throw new InvalidOperationException("The relay listens on 127.0.0.1 only.");
        }

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.ExclusiveAddressUse = true;
            socket.Bind(loopback);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>A host lifetime that does nothing: start and stop are driven only through IRelay.</summary>
    private sealed class HostOwnedLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// The wait of W-design 4.2 between "every request was told to stop" and "the listener closes": until
    /// no request and no speech socket remains, or until C-58 has passed on the relay's clock. A speech
    /// stream needs that time to send RELAY_STOPPING and finish its close handshake.
    ///
    /// The host calls this before it stops the server, with a token that fires at the relay's stop budget
    /// and at the host's own shutdown time-out, whichever comes first.
    /// </summary>
    private sealed class DrainBeforeStop(EdgePipeline pipeline, TimeProvider clock) : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StoppingAsync(CancellationToken cancellationToken)
        {
            using var handshake = new CancellationTokenSource(ProtocolConstants.CloseHandshakeWait, clock);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(handshake.Token, cancellationToken);
            try
            {
                await pipeline.WhenIdleAsync().WaitAsync(wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // What is still open is aborted with the listener.
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
