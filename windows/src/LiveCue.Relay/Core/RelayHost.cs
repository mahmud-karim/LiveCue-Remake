using System.Net.Sockets;
using System.Threading.Channels;
using LiveCue.Relay.Answers;
using LiveCue.Relay.Diagnostics;
using LiveCue.Relay.Edge;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;

namespace LiveCue.Relay.Core;

/// <summary>
/// One relay instance: <see cref="IRelay"/> and <see cref="IPairingExport"/> (W-design 4.2).
///
/// An instance is started once and never restarted. Its status changes before the matching event is
/// published, and <see cref="RelayStopped"/> is the last event of an instance that had started. A stop
/// runs once, whoever asks for it and however often; a fault while running publishes
/// <see cref="RelayFaulted"/>, runs the same stop sequence and leaves the status <see cref="RelayStatus.Faulted"/>.
/// </summary>
internal sealed class RelayHost : IRelay, IPairingExport
{
    /// <summary>The <see cref="Diagnostic.Source"/> of the diagnostics of start, stop and the timers.</summary>
    public const string Source = "relay";

    private const string WorkFolderName = "work";

    private readonly RelayOptions _options;
    private readonly RelayDependencies _dependencies;
    private readonly TimeProvider _clock;
    private readonly EventChannel _events;
    private readonly AccessState _access = new();

    // Start, and the decision to stop, one at a time. The stop sequence itself runs outside it.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Pause and resume: the state changes and their events keep one order.
    private readonly Lock _acceptingGate = new();

    // The periodic reports against the last report of a stop: nothing is published after RelayStopped.
    private readonly Lock _reportGate = new();

    private RelayServices? _services;
    private WebApplication? _app;
    private EdgePipeline? _pipeline;
    private ITimer? _statisticsTimer;
    private ITimer? _dropReportTimer;
    private bool _reportsEnded;
    private volatile Task? _stop;
    private volatile RelayStatus _status = RelayStatus.Stopped;
    private volatile int _boundPort;
    private bool _started;
    private int _disposed;

    private RelayHost(RelayOptions options, RelayDependencies dependencies)
    {
        _options = options;
        _dependencies = dependencies;
        _clock = dependencies.Clock;
        _events = new EventChannel(_clock);
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

    /// <summary>The edge of the running instance, for its counters and gauges; null unless started.</summary>
    internal EdgePipeline? Edge => Volatile.Read(ref _pipeline);

    /// <summary>The addresses the listener reports while the relay runs; empty otherwise.</summary>
    internal IReadOnlyList<string> ListenerAddresses =>
        _status == RelayStatus.Running && _app is { } app ? KestrelSetup.ReadAddresses(app) : [];

    public async Task StartAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_started)
            {
                throw new InvalidOperationException("A relay instance is started once. Create a new one to start again.");
            }

            // Step 1.
            _started = true;
            _status = RelayStatus.Starting;
            try
            {
                await StartCoreAsync(ct).ConfigureAwait(false);
            }
            catch (RelayStartException)
            {
                // The failing step published RelayFaulted and set Faulted.
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller gave up. Nothing is listening and there is nothing to report.
                _status = RelayStatus.Stopped;
                _events.Complete();
                throw;
            }
#pragma warning disable CA1031 // Whatever a step throws, the host gets the fixed fault and its fixed sentence.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Nothing is left listening, whichever step it was.
                await AbandonListenerAsync().ConfigureAwait(false);
                throw Fault(RelayFaultCode.StartFailed);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        Task stop;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            stop = BeginStop(RelayStopReason.Requested);
        }
        finally
        {
            _gate.Release();
        }

        // The token ends the caller's wait, never the stop itself.
        await stop.WaitAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        EndReports();
        _events.Complete();
        _access.Dispose();
    }

    public Task SetAcceptingAsync(bool accepting)
    {
        lock (_acceptingGate)
        {
            RequireRunning();

            // The flag flips and WhileAccepting is cancelled before the event is published.
            if (_access.SetAccepting(accepting))
            {
                _events.Publish(new AcceptingChanged(_events.Now, accepting));
            }
        }

        return Task.CompletedTask;
    }

    public Task<PairingQr> ShowPairingAsync(CancellationToken ct) => RequireRunning().Pairing.ShowAsync(ct);

    public Task<PairingQr> ResetPairingAsync(CancellationToken ct) => RequireRunning().Pairing.ResetAsync(ct);

    public Task<string> GetPairingPayloadAsync(CancellationToken ct) => RequireRunning().Pairing.GetPayloadAsync(ct);

    /// <summary>
    /// A fault while running (W-design 4.2): publishes RelayFaulted(Internal), then runs the stop
    /// sequence and publishes RelayStopped(Fault). The relay never restarts itself. Does nothing when the
    /// relay is not running or is already stopping.
    /// </summary>
    internal async Task FaultAsync(string fixedText, Exception? exception)
    {
        Task stop;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_app is null || _stop is not null)
            {
                return;
            }

            _events.Diagnostic(Source, fixedText, exception);
            _status = RelayStatus.Faulted;
            _events.Publish(new RelayFaulted(_events.Now, RelayFaultCode.Internal, FixedTexts.Fault(RelayFaultCode.Internal)));
            stop = BeginStop(RelayStopReason.Fault);
        }
        finally
        {
            _gate.Release();
        }

        await stop.ConfigureAwait(false);
    }

    private async Task StartCoreAsync(CancellationToken ct)
    {
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
            _dependencies.Processes ?? new ProcessRunner(_clock),
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
        PrepareDataFolder();

        // Step 6.
        var pipeline = new EdgePipeline(context, services);
        var app = KestrelSetup.Build(context, pipeline);
        try
        {
            await app.StartAsync(ct).ConfigureAwait(false);
            _boundPort = KestrelSetup.ReadBoundPort(app);
        }
        catch (Exception exception)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            if (exception is OperationCanceledException && ct.IsCancellationRequested)
            {
                throw;
            }

            // Recognised by type, never by text; hosts get a fixed sentence.
            throw Fault(IsAddressInUse(exception) ? RelayFaultCode.PortInUse : RelayFaultCode.StartFailed);
        }

        // Step 7.
        _app = app;
        _services = services;
        Volatile.Write(ref _pipeline, pipeline);
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

        StartReports();

        // The listener is the relay's alone: nothing else stops its application. If it stops all the
        // same, the relay is no longer reachable and says so.
        app.Lifetime.ApplicationStopping.Register(
            () =>
            {
                if (_stop is null)
                {
                    _ = FaultAsync("The listener stopped by itself.", null);
                }
            });

        // Step 8. The outcome is ignored.
        if (_options.WarmCatalogOnStart && _options.CodexExecutablePath is not null)
        {
            _ = WarmCatalogAsync(services);
        }
    }

    /// <summary>
    /// Step 5: the two schema files and the three launcher scripts are rewritten from the embedded
    /// copies; and, when the per-request folders live under the data folder, <c>work\</c> is created and
    /// emptied, so that an output file left by a crash or a kill does not outlive this start
    /// (protocol 12.2). A folder a test or a host supplied as TempRoot is never emptied.
    /// </summary>
    private void PrepareDataFolder()
    {
        var data = _options.DataDirectory;
        SchemaFiles.Rewrite(data);
        LabScripts.WriteCopies(data);
        if (_dependencies.TempRoot is not null)
        {
            return;
        }

        var work = Directory.CreateDirectory(Path.Combine(data, WorkFolderName));
        if (work.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            // A link would make "everything in work\" mean somebody else's folder.
            throw new IOException("The working folder is a link.");
        }

        foreach (var entry in work.EnumerateFileSystemInfos())
        {
            try
            {
                if (entry is DirectoryInfo folder && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    folder.Delete(recursive: true);
                }
                else
                {
                    entry.Delete();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Left as it is, and reported; not a reason to refuse the start.
                _events.Diagnostic(Source, "A leftover working folder could not be deleted.", exception);
            }
        }
    }

    /// <summary>A start that failed after the listener was bound closes it again.</summary>
    private async Task AbandonListenerAsync()
    {
        var app = _app;
        _app = null;
        _services = null;
        EndReports();
        if (app is null)
        {
            return;
        }

        try
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The start has failed already; the fault that follows says so.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private RelayServices RequireRunning() =>
        _status == RelayStatus.Running && _services is { } services
            ? services
            : throw new InvalidOperationException("The relay is not running.");

    /// <summary>
    /// Called with the gate held. Returns the one stop of this instance, starting it when the relay runs
    /// and none has begun; a relay that never started, or whose start failed, has nothing to stop.
    /// </summary>
    private Task BeginStop(RelayStopReason reason)
    {
        if (_stop is { } running)
        {
            return running;
        }

        if (_app is not { } app)
        {
            return Task.CompletedTask;
        }

        if (reason == RelayStopReason.Requested)
        {
            _status = RelayStatus.Stopping;
        }

        // On the pool: cancelling the stopping token runs the callbacks of every open request and stream.
        return _stop = Task.Run(() => RunStopAsync(app, reason));
    }

    private async Task RunStopAsync(WebApplication app, RelayStopReason reason)
    {
        // The budget (W-02) counts from here. That one token makes every answer run kill its process and
        // end, and every speech stream send RELAY_STOPPING and close with 1001. In-flight HTTP requests
        // are not answered: their connections are aborted.
        using var budget = new CancellationTokenSource(ProtocolConstants.StopBudget, _clock);
        _access.BeginStopping();

        try
        {
            // The application first waits until no request and no speech socket remains, or until C-58 has
            // passed (KestrelSetup.DrainBeforeStop). Then the listener closes, and what is still open
            // when the budget passes is aborted.
            await app.StopAsync(budget.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A stop never throws; what could not be closed gracefully is disposed below.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (exception is not OperationCanceledException)
            {
                _events.Diagnostic(Source, "The listener did not stop cleanly.", exception);
            }
        }

        try
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // As above.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _events.Diagnostic(Source, "The listener was not disposed cleanly.", exception);
        }

        _app = null;
        _services = null;
        if (reason == RelayStopReason.Requested)
        {
            _status = RelayStatus.Stopped;
        }

        // The last totals, when one changed since the previous event; then the last event of the instance.
        EndReports();
        _events.Publish(new RelayStopped(_events.Now, reason));
        _events.Complete();
    }

    /// <summary>The two periodic reports: the rejected-request totals (W-13) and dropped events (W-01).</summary>
    private void StartReports()
    {
        lock (_reportGate)
        {
            _statisticsTimer = _clock.CreateTimer(
                static state => ((RelayHost)state!).OnReportTimer(statistics: true),
                this,
                ProtocolConstants.EdgeStatisticsInterval,
                ProtocolConstants.EdgeStatisticsInterval);
            _dropReportTimer = _clock.CreateTimer(
                static state => ((RelayHost)state!).OnReportTimer(statistics: false),
                this,
                ProtocolConstants.DroppedEventReportInterval,
                ProtocolConstants.DroppedEventReportInterval);
        }
    }

    private void OnReportTimer(bool statistics)
    {
        try
        {
            lock (_reportGate)
            {
                if (_reportsEnded)
                {
                    return;
                }

                if (!statistics)
                {
                    _events.ReportDrops();
                }
                else if (Edge?.TakeStatistics(_events.Now) is { } totals)
                {
                    _events.Publish(totals);
                }
            }
        }
#pragma warning disable CA1031 // A timer callback has no caller to throw to: a failing report is a fault of the relay.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _ = FaultAsync("A periodic report failed.", exception);
        }
    }

    /// <summary>Ends both timers and publishes the totals once more when one changed since the last event.</summary>
    private void EndReports()
    {
        lock (_reportGate)
        {
            if (_reportsEnded)
            {
                return;
            }

            _reportsEnded = true;
            _statisticsTimer?.Dispose();
            _dropReportTimer?.Dispose();
            if (Edge?.TakeStatistics(_events.Now) is { } totals)
            {
                _events.Publish(totals);
            }
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
