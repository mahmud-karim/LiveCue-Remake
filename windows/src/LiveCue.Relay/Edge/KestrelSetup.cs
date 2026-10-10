using System.Net;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiveCue.Relay.Edge;

/// <summary>
/// The listener of protocol 5.1 (W-design 4.3): Kestrel inside the current process, on 127.0.0.1 only,
/// HTTP/1.1 only, with the parser limits and timeouts of the protocol. No configuration file or
/// environment variable is read, no logging provider exists, and there is no endpoint routing: the
/// pipeline is WebSockets followed by one terminal delegate.
///
/// First version (foundation): the connection gate (C-30 over plain and upgraded connections together)
/// is work package WP-W01; Kestrel's own two limits are set.
/// </summary>
internal static class KestrelSetup
{
    public static WebApplication Build(RelayContext context, EdgePipeline pipeline)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());

        builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;

            var limits = kestrel.Limits;
            limits.MaxRequestLineSize = ProtocolConstants.MaxRequestLineBytes;
            limits.MaxRequestHeadersTotalSize = ProtocolConstants.MaxHeaderBytes;
            limits.MaxRequestHeaderCount = ProtocolConstants.MaxHeaderCount;
            limits.MaxRequestBodySize = ProtocolConstants.MaxRequestBodyBytes;
            limits.MaxConcurrentConnections = ProtocolConstants.MaxConnections;
            limits.MaxConcurrentUpgradedConnections = ProtocolConstants.MaxSpeechSockets;
            limits.RequestHeadersTimeout = ProtocolConstants.HeadersTimeout;
            limits.KeepAliveTimeout = ProtocolConstants.KeepAliveTimeout;
            limits.MinRequestBodyDataRate = new MinDataRate(
                ProtocolConstants.MinBodyBytesPerSecond, ProtocolConstants.MinBodyRateGrace);

            // 127.0.0.1 only: never IPv6, never a wildcard, never a routable address. HTTP/1.1 only.
            kestrel.Listen(IPAddress.Loopback, context.Options.Port, listen => listen.Protocols = HttpProtocols.Http1);
        });

        // A second line behind the relay's own stop budget, in real time.
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = ProtocolConstants.StopBudget);

        // The host process owns Ctrl+C and shutdown; the relay installs no console handler.
        builder.Services.AddSingleton<IHostLifetime, HostOwnedLifetime>();

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = ProtocolConstants.KeepAliveFrameInterval });
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

    /// <summary>A host lifetime that does nothing: start and stop are driven only through IRelay.</summary>
    private sealed class HostOwnedLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
