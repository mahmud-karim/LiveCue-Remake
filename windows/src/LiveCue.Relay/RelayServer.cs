using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using LiveCue.Relay.Diagnostics;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Routes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiveCue.Relay;

/// <summary>
/// The relay: Kestrel inside the current process, configured exactly as protocol 5.1 says. It is
/// started with <see cref="StartAsync"/> and never with a blocking run call, because in the desktop
/// app the dispatcher owns the main thread.
/// </summary>
internal sealed class RelayServer : IRelay
{
    private const string PortInUseText = "The relay port is already in use.";
    private const string InvalidEndpointText = "The advertised endpoint is not valid.";
    private const string StartFailedText = "Could not start the relay.";

    private readonly RelayOptions _options;
    private readonly TimeProvider _clock;
    private readonly ITokenVerifier _tokens;
    private readonly EventChannel _events = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WebApplication? _app;
    private volatile RelayStatus _status = RelayStatus.Stopped;
    private volatile int _boundPort;
    private bool _started;
    private bool _disposed;

    public RelayServer(RelayOptions options, RelayDependencies dependencies)
    {
        _options = options;
        _clock = dependencies.Clock;
        _tokens = dependencies.TokenVerifier ?? EphemeralPairing.Create();
    }

    public RelayStatus Status => _status;

    public int BoundPort => _status == RelayStatus.Running ? _boundPort : 0;

    public ChannelReader<RelayEvent> Events => _events.Reader;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("A relay instance is started once. Create a new one to start again.");
            }

            _started = true;
            _status = RelayStatus.Starting;

            if (!_options.HasUsablePort)
            {
                throw Fault(RelayFaultCode.StartFailed, StartFailedText, null);
            }

            if (!_options.HasCanonicalEndpoint)
            {
                throw Fault(RelayFaultCode.InvalidEndpoint, InvalidEndpointText, null);
            }

            var app = Build();
            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
                _boundPort = ReadBoundPort(app);
            }
            catch (Exception exception)
            {
                await app.DisposeAsync().ConfigureAwait(false);
                if (exception is OperationCanceledException)
                {
                    _status = RelayStatus.Stopped;
                    throw;
                }

                // The exception's own text may name paths or addresses; hosts get a fixed sentence.
                throw IsPortInUse(exception)
                    ? Fault(RelayFaultCode.PortInUse, PortInUseText, exception)
                    : Fault(RelayFaultCode.StartFailed, StartFailedText, exception);
            }

            _app = app;
            _status = RelayStatus.Running;
            _events.Raise(new RelayStarted(
                _clock.GetUtcNow(),
                _options.AdvertisedEndpoint,
                _boundPort,
                _options.Mode,
                ProtocolConstants.ProtocolVersion,
                _options.RelayVersion,
                PairingWasCreated: false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => StopCoreAsync(dispose: false, cancellationToken);

    public async ValueTask DisposeAsync() => await StopCoreAsync(dispose: true, CancellationToken.None).ConfigureAwait(false);

    private async Task StopCoreAsync(bool dispose, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var app = _app;
            _app = null;
            if (app is not null)
            {
                _status = RelayStatus.Stopping;
                try
                {
                    await app.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                    _status = RelayStatus.Stopped;
                    _events.Raise(new RelayStopped(_clock.GetUtcNow(), RelayStopReason.Requested));
                }
            }

            if (dispose && !_disposed)
            {
                _disposed = true;
                _events.Complete();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private WebApplication Build()
    {
        // The empty builder reads no environment variables, command line or settings files, and adds
        // no logging, so the relay behaves the same in every host process and writes no log.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());

        builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;

            var limits = kestrel.Limits;
            limits.MaxRequestLineSize = ProtocolConstants.MaxRequestLineBytes;
            limits.MaxRequestHeadersTotalSize = ProtocolConstants.MaxRequestHeadersBytes;
            limits.MaxRequestHeaderCount = ProtocolConstants.MaxRequestHeaderCount;
            limits.MaxRequestBodySize = ProtocolConstants.MaxRequestBodyBytes;
            limits.MaxConcurrentConnections = ProtocolConstants.MaxConnections;
            limits.RequestHeadersTimeout = ProtocolConstants.RequestHeadersTimeout;
            limits.KeepAliveTimeout = ProtocolConstants.KeepAliveTimeout;
            limits.MinRequestBodyDataRate = new MinDataRate(
                ProtocolConstants.MinRequestBodyBytesPerSecond, ProtocolConstants.MinRequestBodyRateGrace);

            // 127.0.0.1 only: never IPv6, never a wildcard, never a routable address. HTTP/1.1 only.
            kestrel.Listen(IPAddress.Loopback, _options.Port, listen => listen.Protocols = HttpProtocols.Http1);
        });

        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = ProtocolConstants.StopTimeout);

        // The host process owns Ctrl+C and shutdown; the relay must not install console handlers.
        builder.Services.AddSingleton<IHostLifetime, HostOwnedLifetime>();

        var app = builder.Build();
        var edge = new EdgePipeline(
            _options.Mode,
            _tokens,
            new RouteTable(),
            onAuthenticated: () => _events.Raise(new PhoneSeen(_clock.GetUtcNow())));
        app.Run(edge.HandleAsync);
        return app;
    }

    private RelayStartException Fault(RelayFaultCode code, string message, Exception? cause)
    {
        _status = RelayStatus.Faulted;
        _events.Raise(new RelayFaulted(_clock.GetUtcNow(), code, message));
        return new RelayStartException(code, message, cause);
    }

    private static int ReadBoundPort(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not report the address it bound.");
        return new Uri(address).Port;
    }

    private static bool IsPortInUse(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AddressInUseException or SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A host lifetime that does nothing: start and stop are driven only through <see cref="IRelay"/>.</summary>
    private sealed class HostOwnedLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
