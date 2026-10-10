using System.Threading.Channels;
using LiveCue.Relay;

namespace LiveCue.Hosting.Tests.Support;

/// <summary>
/// An <see cref="IRelay"/> that binds nothing and plays what the test scripts: a start that succeeds or
/// faults, events published on demand, and a fault while running.
/// </summary>
internal sealed class FakeRelay(RelayOptions options) : IRelay, IPairingExport
{
    /// <summary>A synthetic token, the one of the protocol's payload example. Never a real one.</summary>
    public static readonly string SyntheticToken = new('A', 43);

    public static readonly DateTimeOffset Time = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly Channel<RelayEvent> _events = Channel.CreateUnbounded<RelayEvent>();

    public RelayOptions Options { get; } = options;

    /// <summary>When set, the start publishes RelayFaulted and throws RelayStartException with this code.</summary>
    public RelayFaultCode? StartFault { get; init; }

    /// <summary>When set, the start throws this instead, as a relay with a defect would.</summary>
    public Exception? StartThrows { get; init; }

    /// <summary>What a successful start reports about the pairing.</summary>
    public bool PairingWasCreated { get; init; }

    public bool Disposed { get; private set; }

    public int StopCalls { get; private set; }

    public int ShowCalls { get; private set; }

    public int ResetCalls { get; private set; }

    public RelayStatus Status { get; private set; } = RelayStatus.Stopped;

    public bool Accepting { get; private set; } = true;

    public int BoundPort => Status == RelayStatus.Running ? (Options.Port == 0 ? 50000 : Options.Port) : 0;

    public ChannelReader<RelayEvent> Events => _events.Reader;

    /// <summary>Publishes an event, as the relay would from a request thread.</summary>
    public void Raise(RelayEvent relayEvent) => _events.Writer.TryWrite(relayEvent);

    /// <summary>The relay stops by itself: RelayFaulted(Internal), RelayStopped(Fault), end of events.</summary>
    public void FaultWhileRunning()
    {
        Status = RelayStatus.Faulted;
        Raise(new RelayFaulted(Time, RelayFaultCode.Internal, "The relay stopped after an internal error."));
        Raise(new RelayStopped(Time, RelayStopReason.Fault));
        _events.Writer.TryComplete();
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (StartThrows is { } defect)
        {
            throw defect;
        }

        if (StartFault is { } code)
        {
            Status = RelayStatus.Faulted;
            Raise(new RelayFaulted(Time, code, "The relay could not start."));
            _events.Writer.TryComplete();
            throw new RelayStartException(code, "The relay could not start.");
        }

        Status = RelayStatus.Running;
        Raise(new RelayStarted(
            Time, Options.AdvertisedEndpoint, BoundPort, Options.Mode, 2, Options.RelayVersion, PairingWasCreated));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        StopCalls++;
        if (Status == RelayStatus.Running)
        {
            Status = RelayStatus.Stopped;
            Raise(new RelayStopped(Time, RelayStopReason.Requested));
            _events.Writer.TryComplete();
        }

        return Task.CompletedTask;
    }

    public Task SetAcceptingAsync(bool accepting)
    {
        if (Status != RelayStatus.Running)
        {
            throw new InvalidOperationException("The relay is not running.");
        }

        if (Accepting != accepting)
        {
            Accepting = accepting;
            Raise(new AcceptingChanged(Time, accepting));
        }

        return Task.CompletedTask;
    }

    public Task<PairingQr> ShowPairingAsync(CancellationToken ct)
    {
        ShowCalls++;
        Raise(new PairingShown(Time, Options.AdvertisedEndpoint));
        Raise(new SelfCheckCompleted(Time, SelfCheckResult.Verified));
        return Task.FromResult(new PairingQr(Options.AdvertisedEndpoint, Matrix()));
    }

    public Task<PairingQr> ResetPairingAsync(CancellationToken ct)
    {
        ResetCalls++;
        Raise(new Notice(Time, NoticeKind.PairingReset, "Pairing reset. Every iPhone must pair again."));
        return ShowPairingAsync(ct);
    }

    public Task<string> GetPairingPayloadAsync(CancellationToken ct) => Task.FromResult(
        "{\"endpoint\":\"" + Options.AdvertisedEndpoint + "\",\"token\":\"" + SyntheticToken + "\"}");

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private static bool[][] Matrix()
    {
        var modules = new bool[21][];
        for (var row = 0; row < modules.Length; row++)
        {
            modules[row] = new bool[21];
            modules[row][row] = true;
        }

        return modules;
    }
}

/// <summary>Hands the supervisor one <see cref="FakeRelay"/> per start and remembers each.</summary>
internal sealed class FakeRelayFactory : IRelayFactory
{
    /// <summary>When set, the next relay's start faults with this code. It applies to one start.</summary>
    public RelayFaultCode? NextStartFault { get; set; }

    /// <summary>When set, the next relay's start throws this. It applies to one start.</summary>
    public Exception? NextStartThrows { get; set; }

    /// <summary>What the next relay's start reports about the pairing.</summary>
    public bool PairingWasCreated { get; set; }

    /// <summary>Every relay that was created, in order.</summary>
    public List<FakeRelay> Created { get; } = [];

    public FakeRelay Current => Created[^1];

    public IRelay Create(RelayOptions options)
    {
        var relay = new FakeRelay(options)
        {
            StartFault = NextStartFault,
            StartThrows = NextStartThrows,
            PairingWasCreated = PairingWasCreated,
        };
        NextStartFault = null;
        NextStartThrows = null;
        Created.Add(relay);
        return relay;
    }
}
