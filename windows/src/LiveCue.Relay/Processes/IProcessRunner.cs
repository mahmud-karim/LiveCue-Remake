namespace LiveCue.Relay.Processes;

/// <summary>Which environment variables a child process receives.</summary>
public enum EnvironmentPolicy
{
    /// <summary>The parent's environment unchanged. Used for the Tailscale CLI.</summary>
    Inherit,

    /// <summary>Only the names of protocol 12.6, compared without regard to case: the thirteen names of
    /// protocol 11.5, plus HOMEDRIVE, HOMEPATH and CODEX_HOME, plus HTTPS_PROXY, HTTP_PROXY, ALL_PROXY and
    /// NO_PROXY when the parent has them. Used for every Codex process: catalogue, sign-in check, version
    /// read and answer run. (Before the review this member was WithoutLiveCueVariables.)</summary>
    CodexAllowlist,

    /// <summary>Only the thirteen names of protocol 11.5, compared without regard to case. Used for the
    /// lab launcher scripts.</summary>
    LabScriptAllowlist,
}

/// <summary>One process to run. No member is ever passed through a shell.</summary>
public sealed record ProcessRequest
{
    /// <summary>Absolute path of an .exe file. A relative path or another extension is an
    /// <see cref="ArgumentException"/>.</summary>
    public required string FileName { get; init; }

    /// <summary>Passed one by one through ProcessStartInfo.ArgumentList, never joined into a string.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public EnvironmentPolicy Environment { get; init; } = EnvironmentPolicy.Inherit;

    /// <summary>Written to standard input as UTF-8 without a byte-order mark, then the pipe is closed.
    /// Null closes standard input at once. Ignored by StartSessionAsync.</summary>
    public string? StandardInput { get; init; }

    /// <summary>Bytes of standard output to keep. 0 discards standard output. Output beyond the limit ends
    /// the process with <see cref="ProcessOutcome.OutputLimitExceeded"/>.</summary>
    public int MaxStandardOutputBytes { get; init; }

    /// <summary>Wall-clock limit, measured with the runner's TimeProvider.</summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>True places the process in the kill-on-close job object, so it cannot outlive this
    /// application (architecture 3.7), and a time-out or cancellation kills its whole process tree.
    /// False for the lab launcher scripts only: the process is not placed in the job, and a time-out or
    /// cancellation kills that one process and leaves its descendants (Docker Desktop) alone.</summary>
    public bool KillWithApplication { get; init; } = true;
}

public enum ProcessOutcome
{
    /// <summary>The process ended by itself. ExitCode is meaningful.</summary>
    Exited,
    TimedOut,
    Cancelled,
    OutputLimitExceeded,
    StartFailed,
}

/// <param name="StandardOutput">At most MaxStandardOutputBytes bytes; empty unless Outcome is Exited.</param>
public sealed record ProcessResult(ProcessOutcome Outcome, int ExitCode, ReadOnlyMemory<byte> StandardOutput);

/// <summary>Thrown by <see cref="IProcessSession"/> and by StartSessionAsync. Fixed message per outcome.</summary>
public sealed class ProcessFailedException : Exception
{
    public ProcessFailedException(ProcessOutcome outcome)
        : base("The child process failed: " + outcome + ".")
    {
        Outcome = outcome;
    }

    public ProcessOutcome Outcome { get; }
}

/// <summary>
/// A line-oriented conversation with a running process (the Codex catalogue exchange). One writer and one
/// reader at a time. Disposing closes standard input and kills the whole process tree; it never throws.
/// </summary>
public interface IProcessSession : IAsyncDisposable
{
    /// <summary>Writes the line and a line feed as UTF-8 and flushes.</summary>
    ValueTask WriteLineAsync(string line, CancellationToken ct);

    /// <summary>The next line without its terminator, or null at end of output. Throws
    /// <see cref="ProcessFailedException"/> for OutputLimitExceeded and TimedOut.</summary>
    ValueTask<string?> ReadLineAsync(CancellationToken ct);
}

/// <summary>
/// Starts child processes. Always: UseShellExecute false, CreateNoWindow true, standard error discarded,
/// the process killed on time-out, cancellation, output overflow and dispose (with its whole tree when
/// KillWithApplication is true).
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs to completion. Never throws for a process failure: the outcome says what happened, and for
    /// Cancelled the tree is already dead when the task completes. Throws only ArgumentException.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct);

    /// <summary>Starts the process for a line exchange. Throws <see cref="ProcessFailedException"/> with
    /// StartFailed when it cannot be started.</summary>
    Task<IProcessSession> StartSessionAsync(ProcessRequest request, CancellationToken ct);
}
