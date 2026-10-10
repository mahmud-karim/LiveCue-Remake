using System.Net.Sockets;
using System.Threading.Channels;
using LiveCue.Relay.Answers;
using LiveCue.Relay.Diagnostics;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;

namespace LiveCue.Relay.Core;

/// <summary>
/// One relay instance: <see cref="IRelay"/> and <see cref="IPairingExport"/> (W-design 4.2).
///
/// First version (foundation): start, stop, the pause flag and the event channel work as the contract
/// says. Waiting for requests and speech sockets to end before the listener closes, the runtime fault
/// path and the dropped-event report are work package WP-W01.
/// </summary>
internal sealed class RelayHost : IRelay, IPairingExport
{
    private readonly RelayOptions _options;
    private readonly RelayDependencies _dependencies;
    private readonly EventChannel _events;
    private readonly AccessState _access = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private RelayServices? _services;
    private WebApplication? _app;
    private volatile RelayStatus _status = RelayStatus.Stopped;
    private volatile int _boundPort;
    private bool _started;
    private bool _disposed;

    private RelayHost(RelayOptions options, RelayDependencies dependencies)
    {
        _options = options;
        _dependencies = dependencies;
        _events = new EventChannel(dependencies.Clock);
    }

    public static IRelay Create(RelayOptions options, RelayDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependencies);
        return new RelayHost(options, dependencies);
    }

    public RelayStatus Status => _status;

    public bool Accepting => _access.Accepting;

    public int BoundPort => _status == RelayStatus.Running ? _boundPort : 0;

    public ChannelReader<RelayEvent> Events => _events.Reader;

    public async Task StartAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("A relay instance is started once. Create a new one to start again.");
            }

            // Step 1.
            _started = true;
            _status = RelayStatus.Starting;

            // Step 2. The canonical text is what is advertised.
            if (!AdvertisedEndpoint.TryCanonicalize(_options.AdvertisedEndpoint, out var endpoint))
            {
                throw Fault(RelayFaultCode.InvalidEndpoint);
            }

            // Step 3.
            var port = _options.Port;
            if ((port != 0 && port is < 1024 or > 65535)
                || port == ProtocolConstants.ForbiddenPort
                || string.IsNullOrWhiteSpace(_options.DataDirectory)
                || !Directory.Exists(_options.DataDirectory))
            {
                throw Fault(RelayFaultCode.StartFailed);
            }

            var context = new RelayContext(
                _options,
                _dependencies,
                _events,
                _access,
                _dependencies.Processes ?? new ProcessRunner(_dependencies.Clock),
                endpoint);
            var services = RelayComposition.Compose(context);

            // Step 4.
            bool pairingWasCreated;
            try
            {
                pairingWasCreated = await services.Pairing.LoadOrCreateAsync(ct).ConfigureAwait(false);
            }
            catch (RelayStartException exception)
            {
                throw Fault(exception.Code);
            }

            // Step 5.
            try
            {
                SchemaFiles.Rewrite(_options.DataDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Fault(RelayFaultCode.StartFailed);
            }

            // Step 6.
            var app = KestrelSetup.Build(context, new EdgePipeline(context, services));
            try
            {
                await app.StartAsync(ct).ConfigureAwait(false);
                _boundPort = KestrelSetup.ReadBoundPort(app);
            }
            catch (Exception exception)
            {
                await app.DisposeAsync().ConfigureAwait(false);
                if (exception is OperationCanceledException)
                {
                    _status = RelayStatus.Stopped;
                    _events.Complete();
                    throw;
                }

                // Recognised by type, never by text; hosts get a fixed sentence.
                throw Fault(IsAddressInUse(exception) ? RelayFaultCode.PortInUse : RelayFaultCode.StartFailed);
            }

            // Step 7.
            _app = app;
            _services = services;
            _status = RelayStatus.Running;
            _events.Publish(new RelayStarted(
                _events.Now,
                endpoint,
                _boundPort,
                _options.Mode,
                ProtocolConstants.ProtocolVersion,
                _options.RelayVersion,
                pairingWasCreated));
            if (!pairingWasCreated)
            {
                _events.Publish(new Notice(_events.Now, NoticeKind.PairingRestored, FixedTexts.PairingRestored));
            }

            // Step 8. The outcome is ignored.
            if (_options.WarmCatalogOnStart && _options.CodexExecutablePath is not null)
            {
                _ = WarmCatalogAsync(services);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StopAsync(CancellationToken ct) => StopCoreAsync(dispose: false, ct);

    public async ValueTask DisposeAsync() => await StopCoreAsync(dispose: true, CancellationToken.None).ConfigureAwait(false);

    public Task SetAcceptingAsync(bool accepting)
    {
        RequireRunning();
        if (_access.SetAccepting(accepting))
        {
            _events.Publish(new AcceptingChanged(_events.Now, accepting));
        }

        return Task.CompletedTask;
    }

    public Task<PairingQr> ShowPairingAsync(CancellationToken ct) => RequireRunning().Pairing.ShowAsync(ct);

    public Task<PairingQr> ResetPairingAsync(CancellationToken ct) => RequireRunning().Pairing.ResetAsync(ct);

    public Task<string> GetPairingPayloadAsync(CancellationToken ct) => RequireRunning().Pairing.GetPayloadAsync(ct);

    private RelayServices RequireRunning() =>
        _status == RelayStatus.Running && _services is { } services
            ? services
            : throw new InvalidOperationException("The relay is not running.");

    private async Task StopCoreAsync(bool dispose, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var app = _app;
            _app = null;
            if (app is not null)
            {
                _status = RelayStatus.Stopping;
                _access.BeginStopping();
                try
                {
                    using var budget = new CancellationTokenSource(ProtocolConstants.StopBudget, _dependencies.Clock);
                    await app.StopAsync(budget.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The budget passed: what was still open has been aborted with the listener.
                }
                finally
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                    _services = null;
                    _status = RelayStatus.Stopped;
                    _events.Publish(new RelayStopped(_events.Now, RelayStopReason.Requested));
                    _events.Complete();
                }
            }

            if (dispose && !_disposed)
            {
                _disposed = true;
                _events.Complete();
                _access.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private RelayStartException Fault(RelayFaultCode code)
    {
        var message = FixedTexts.Fault(code);
        _status = RelayStatus.Faulted;
        _events.Publish(new RelayFaulted(_events.Now, code, message));
        _events.Complete();
        return new RelayStartException(code, message);
    }

    private async Task WarmCatalogAsync(RelayServices services)
    {
        try
        {
            await services.Catalog.ReadAsync(_access.Stopping).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The warm-up's outcome is ignored by design (W-design 4.2 step 8).
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private static bool IsAddressInUse(Exception exception)
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
}
