using LiveCue.Relay.Processes;

namespace LiveCue.Hosting.Tailscale;

/// <summary>
/// The Tailscale CLI: two read commands and the four write commands, each with a fixed argument list and
/// the limit of rule TS-10.
///
/// First version (foundation): Tailscale is reported as "not installed". Nothing is looked up and no
/// process is ever started. Work package WP-W07 builds the CLI.
/// </summary>
internal sealed class TailscaleCli : ITailscaleCli
{
    /// <summary>Rule TS-10: the limit of each CLI call.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    public TailscaleCli(IProcessRunner processes, IHostFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(files);
    }

    public string? ExecutablePath => null;

    public Task<TailscaleRead<TailscaleNodeStatus>> ReadStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new TailscaleRead<TailscaleNodeStatus>(null, TailscaleReadFailure.NotInstalled));
    }

    public Task<TailscaleRead<ServeSnapshot>> ReadServeStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new TailscaleRead<ServeSnapshot>(null, TailscaleReadFailure.NotInstalled));
    }

    public Task<TailscaleWriteOutcome> RunAsync(MappingCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(TailscaleWriteOutcome.NotInstalled);
    }
}
