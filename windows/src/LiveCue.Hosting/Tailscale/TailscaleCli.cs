using LiveCue.Relay.Processes;

namespace LiveCue.Hosting.Tailscale;

/// <summary>
/// The Tailscale CLI: two read commands and the four write commands, each with a fixed argument list and
/// the limit of rule TS-10. Arguments go to the process runner as a list, never through a shell.
///
/// This is the last place before a process starts, so it checks once more what the mapping manager
/// already made sure of: a write command is built again by its own factory and runs only when its
/// argument list is, text for text, what the factory makes (rules TS-2 and TS-3). The output of a write
/// command is discarded; the two status documents are the only output that is kept.
/// </summary>
internal sealed class TailscaleCli : ITailscaleCli
{
    /// <summary>Rule TS-10: the limit of each CLI call.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    /// <summary>The most that is kept of <c>tailscale status --json</c>, which lists every peer of the tailnet.</summary>
    public const int MaxStatusBytes = 16 * 1024 * 1024;

    /// <summary>The most that is kept of <c>tailscale serve status --json</c>.</summary>
    public const int MaxServeStatusBytes = 4 * 1024 * 1024;

    private static readonly string[] StatusArguments = ["status", "--json"];
    private static readonly string[] ServeStatusArguments = ["serve", "status", "--json"];

    private readonly IProcessRunner _processes;
    private readonly IHostFileSystem _files;
    private volatile string? _executablePath;

    public TailscaleCli(IProcessRunner processes, IHostFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(files);
        _processes = processes;
        _files = files;
    }

    private delegate bool Parser<T>(ReadOnlyMemory<byte> utf8, out T? value)
        where T : class;

    /// <summary>
    /// Looked up on first use, never when the hosting library is composed. A path that was found is kept;
    /// while none is found every use looks again, so that "Check again" sees a Tailscale that was
    /// installed in the meantime.
    /// </summary>
    public string? ExecutablePath => _executablePath ??= TailscaleLocator.Locate(_files);

    public Task<TailscaleRead<TailscaleNodeStatus>> ReadStatusAsync(CancellationToken ct) =>
        ReadAsync<TailscaleNodeStatus>(StatusArguments, MaxStatusBytes, TailscaleStatusParser.TryParse, ct);

    public Task<TailscaleRead<ServeSnapshot>> ReadServeStatusAsync(CancellationToken ct) =>
        ReadAsync<ServeSnapshot>(ServeStatusArguments, MaxServeStatusBytes, ServeStatusParser.TryParse, ct);

    public async Task<TailscaleWriteOutcome> RunAsync(MappingCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ct.ThrowIfCancellationRequested();

        // Not one of the four forms as its factory writes it: nothing is started.
        var canonical = MappingRules.Canonical(command);
        if (canonical is null)
        {
            return TailscaleWriteOutcome.Failed;
        }

        var path = ExecutablePath;
        if (path is null)
        {
            return TailscaleWriteOutcome.NotInstalled;
        }

        var result = await StartAsync(path, canonical.Arguments, maxOutputBytes: 0, ct).ConfigureAwait(false);
        switch (result.Outcome)
        {
            case ProcessOutcome.Exited:
                return result.ExitCode == 0 ? TailscaleWriteOutcome.Succeeded : TailscaleWriteOutcome.Failed;
            case ProcessOutcome.TimedOut:
                return TailscaleWriteOutcome.TimedOut;
            case ProcessOutcome.StartFailed:
                return StillInstalled(path) ? TailscaleWriteOutcome.Failed : TailscaleWriteOutcome.NotInstalled;
            case ProcessOutcome.Cancelled:
                ct.ThrowIfCancellationRequested();
                return TailscaleWriteOutcome.Failed;
            default:
                return TailscaleWriteOutcome.Failed;
        }
    }

    private async Task<TailscaleRead<T>> ReadAsync<T>(string[] arguments, int maxOutputBytes, Parser<T> parse, CancellationToken ct)
        where T : class
    {
        ct.ThrowIfCancellationRequested();

        var path = ExecutablePath;
        if (path is null)
        {
            return Failure<T>(TailscaleReadFailure.NotInstalled);
        }

        var result = await StartAsync(path, arguments, maxOutputBytes, ct).ConfigureAwait(false);
        switch (result.Outcome)
        {
            case ProcessOutcome.Exited when result.ExitCode == 0:
                return parse(result.StandardOutput, out var value) && value is not null
                    ? new TailscaleRead<T>(value, TailscaleReadFailure.None)
                    : Failure<T>(TailscaleReadFailure.Unparseable);
            case ProcessOutcome.TimedOut:
                return Failure<T>(TailscaleReadFailure.TimedOut);
            case ProcessOutcome.OutputLimitExceeded:
                // More than is kept: the document was not read, so nothing is concluded from it.
                return Failure<T>(TailscaleReadFailure.Unparseable);
            case ProcessOutcome.StartFailed:
                return Failure<T>(StillInstalled(path) ? TailscaleReadFailure.Failed : TailscaleReadFailure.NotInstalled);
            case ProcessOutcome.Cancelled:
                ct.ThrowIfCancellationRequested();
                return Failure<T>(TailscaleReadFailure.Failed);
            default:
                return Failure<T>(TailscaleReadFailure.Failed);
        }
    }

    /// <summary>
    /// One call: the parent's environment, no input, the process tree killed at the limit and with the
    /// application. A request the runner refuses is a start that failed; this class never throws for it.
    /// </summary>
    private async Task<ProcessResult> StartAsync(string path, IReadOnlyList<string> arguments, int maxOutputBytes, CancellationToken ct)
    {
        var request = new ProcessRequest
        {
            FileName = path,
            Arguments = [.. arguments],
            Environment = EnvironmentPolicy.Inherit,
            StandardInput = null,
            MaxStandardOutputBytes = maxOutputBytes,
            Timeout = Limit,
            KillWithApplication = true,
        };

        try
        {
            return await _processes.RunAsync(request, ct).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return new ProcessResult(ProcessOutcome.StartFailed, -1, ReadOnlyMemory<byte>.Empty);
        }
    }

    /// <summary>After a start that failed: false, and the remembered path forgotten, when the file is gone.</summary>
    private bool StillInstalled(string path)
    {
        if (_files.FileExists(path))
        {
            return true;
        }

        _executablePath = null;
        return false;
    }

    private static TailscaleRead<T> Failure<T>(TailscaleReadFailure failure)
        where T : class => new(null, failure);
}
