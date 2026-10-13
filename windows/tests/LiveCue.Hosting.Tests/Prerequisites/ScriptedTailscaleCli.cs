namespace LiveCue.Hosting.Tests.Prerequisites;

/// <summary>
/// The Tailscale CLI as a test scripts it: an executable path, what the two status reads return, and a
/// record of every call. No process is involved. A write command is recorded and never "run".
/// </summary>
internal sealed class ScriptedTailscaleCli : ITailscaleCli
{
    public const string DnsName = "pc.example.test";

    private readonly Lock _gate = new();
    private readonly List<MappingCommand> _writes = [];
    private int _statusReads;
    private int _serveStatusReads;

    /// <summary>Null plays a PC without Tailscale.</summary>
    public string? ExecutablePath { get; set; } = @"C:\Program Files\Tailscale\tailscale.exe";

    /// <summary>What <c>tailscale status --json</c> gives: by default a connected PC with a name.</summary>
    public TailscaleRead<TailscaleNodeStatus> Status { get; set; } = Read(new TailscaleNodeStatus(Online: true, DnsName));

    /// <summary>What <c>tailscale serve status --json</c> gives: by default an empty configuration.</summary>
    public TailscaleRead<ServeSnapshot> ServeStatus { get; set; } = Read(new ServeSnapshot([], [], [], [], []));

    public int StatusReads => Volatile.Read(ref _statusReads);

    public int ServeStatusReads => Volatile.Read(ref _serveStatusReads);

    /// <summary>Every write command that was asked for, in order. Rule TS-9 wants this empty.</summary>
    public IReadOnlyList<MappingCommand> Writes
    {
        get
        {
            lock (_gate)
            {
                return [.. _writes];
            }
        }
    }

    public static TailscaleRead<T> Read<T>(T value)
        where T : class => new(value, TailscaleReadFailure.None);

    public static TailscaleRead<T> Failed<T>(TailscaleReadFailure failure)
        where T : class => new(null, failure);

    public Task<TailscaleRead<TailscaleNodeStatus>> ReadStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _statusReads);
        return Task.FromResult(Status);
    }

    public Task<TailscaleRead<ServeSnapshot>> ReadServeStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _serveStatusReads);
        return Task.FromResult(ServeStatus);
    }

    public Task<TailscaleWriteOutcome> RunAsync(MappingCommand command, CancellationToken ct)
    {
        lock (_gate)
        {
            _writes.Add(command);
        }

        return Task.FromResult(TailscaleWriteOutcome.Failed);
    }
}
