using LiveCue.Relay.Processes;

namespace LiveCue.Hosting;

/// <summary>
/// Runs <c>&lt;codex&gt; login status</c> (architecture 3.7): exit code 0 means signed in. The output is
/// discarded, and the child gets no LIVECUE_ variable, like every Codex process.
/// </summary>
internal sealed class CodexSignInCheck : ICodexSignInCheck
{
    /// <summary>The limit of architecture 3.7.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    private readonly IProcessRunner _processes;

    public CodexSignInCheck(IProcessRunner processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        _processes = processes;
    }

    public async Task<bool> IsSignedInAsync(string codexPath, CancellationToken ct)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest
            {
                FileName = codexPath,
                Arguments = ["login", "status"],
                Environment = EnvironmentPolicy.WithoutLiveCueVariables,
                MaxStandardOutputBytes = 0,
                Timeout = Limit,
            },
            ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        return result is { Outcome: ProcessOutcome.Exited, ExitCode: 0 };
    }
}
