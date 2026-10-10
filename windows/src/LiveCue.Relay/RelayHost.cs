using System.Net;
using System.Net.Sockets;
using LiveCue.Relay.Http;
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
/// Runs the relay on Kestrel inside the current process, bound to the IPv4 loopback address only.
/// One instance can be started and stopped any number of times.
/// </summary>
public sealed class RelayHost : IRelayHost
{
    private const long MaxRequestBodyBytes = 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private WebApplication? _app;
    private volatile RelayHostState _state = RelayHostState.Stopped;
    private volatile int _port;
    private bool _disposed;

    public RelayHost()
        : this(TimeProvider.System)
    {
    }

    public RelayHost(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public event EventHandler<RelayEvent>? EventRaised;

    public RelayHostState State => _state;

    public int? Port => _state == RelayHostState.Running ? _port : null;

    public async Task StartAsync(RelayOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        int boundPort;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_app is not null)
            {
                throw new InvalidOperationException("The relay is already running.");
            }

            _state = RelayHostState.Starting;
            var app = Build(options);
            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
                boundPort = ReadBoundPort(app);
            }
            catch (Exception exception)
            {
                await app.DisposeAsync().ConfigureAwait(false);
                _state = RelayHostState.Stopped;
                if (exception is OperationCanceledException)
                {
                    throw;
                }

                throw new RelayStartException(Classify(exception), options.Port, exception);
            }

            _app = app;
            _port = boundPort;
            _state = RelayHostState.Running;
        }
        finally
        {
            _gate.Release();
        }

        Raise(new RelayEvent.Started(boundPort));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!await StopCoreAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        Raise(new RelayEvent.Stopped());
    }

    public async ValueTask DisposeAsync()
    {
        var stopped = await StopCoreAsync(CancellationToken.None, markDisposed: true).ConfigureAwait(false);
        if (stopped)
        {
            Raise(new RelayEvent.Stopped());
        }
    }

    private async Task<bool> StopCoreAsync(CancellationToken cancellationToken, bool markDisposed = false)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _disposed |= markDisposed;
            var app = _app;
            if (app is null)
            {
                return false;
            }

            _state = RelayHostState.Stopping;
            _app = null;
            try
            {
                await app.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await app.DisposeAsync().ConfigureAwait(false);
                _state = RelayHostState.Stopped;
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private WebApplication Build(RelayOptions options)
    {
        // The empty builder reads no environment variables, command line or settings files, and adds
        // no logging, so the relay behaves the same in every host process.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());

        builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
            // IPv4 loopback only: never a wildcard address, never a LAN interface.
            kestrel.Listen(IPAddress.Loopback, options.Port, listen => listen.Protocols = HttpProtocols.Http1);
        });

        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = options.ShutdownTimeout);
        // The host process owns Ctrl+C and shutdown; the relay must not install console handlers.
        builder.Services.AddSingleton<IHostLifetime, HostOwnedLifetime>();

        var app = builder.Build();
        RelayPipeline.Configure(
            app,
            options.Authenticator,
            onAuthenticatedRequest: () => Raise(new RelayEvent.PhoneSeen(_timeProvider.GetUtcNow())));
        return app;
    }

    private static int ReadBoundPort(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not report the address it bound.");
        return new Uri(address).Port;
    }

    private static RelayStartFailure Classify(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AddressInUseException or SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
            {
                return RelayStartFailure.PortInUse;
            }
        }

        return RelayStartFailure.Other;
    }

    private void Raise(RelayEvent relayEvent) => EventRaised?.Invoke(this, relayEvent);

    /// <summary>A host lifetime that does nothing: start and stop are driven only through <see cref="IRelayHost"/>.</summary>
    private sealed class HostOwnedLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
