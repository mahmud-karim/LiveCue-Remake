namespace LiveCue.Relay.Processes;

/// <summary>
/// The production runner (W-design 4.13): no shell, hidden, argument list, time limit, output cap,
/// tree kill.
///
/// First version (foundation): starts nothing. Every request is checked as the contract says and then
/// reported as StartFailed. Work package WP-W03 builds the runner.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly TimeProvider _clock;

    public ProcessRunner(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        Validate(request);
        _ = _clock;
        return Task.FromResult(new ProcessResult(ProcessOutcome.StartFailed, ExitCode: -1, StandardOutput: default));
    }

    public Task<IProcessSession> StartSessionAsync(ProcessRequest request, CancellationToken ct)
    {
        Validate(request);
        throw new ProcessFailedException(ProcessOutcome.StartFailed);
    }

    /// <summary>An absolute path of an .exe file, and nothing else, may ever be started.</summary>
    private static void Validate(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.FileName)
            || !Path.IsPathFullyQualified(request.FileName)
            || !string.Equals(Path.GetExtension(request.FileName), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The file to run must be the absolute path of an .exe file.", nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.Arguments);
    }
}
