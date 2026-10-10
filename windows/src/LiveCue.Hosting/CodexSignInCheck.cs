using System.Text;
using LiveCue.Relay.Processes;

namespace LiveCue.Hosting;

/// <summary>
/// The two questions asked of the Codex executable itself (architecture 3.7, W-design 4.14):
/// <c>&lt;codex&gt; login status</c>, whose exit code 0 means signed in and whose output is discarded,
/// and <c>&lt;codex&gt; --version</c>, whose first line is the only Codex output that is ever kept or
/// shown. Both children get the CodexAllowlist environment, like every Codex process.
/// </summary>
internal sealed class CodexSignInCheck : ICodexSignInCheck
{
    /// <summary>The limit of architecture 3.7, for each of the two commands.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    /// <summary>How much of the output of <c>--version</c> is kept, in bytes.</summary>
    public const int MaxVersionOutputBytes = 256;

    /// <summary>The longest version text that is accepted.</summary>
    public const int MaxVersionTextLength = 64;

    private readonly IProcessRunner _processes;

    public CodexSignInCheck(IProcessRunner processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        _processes = processes;
    }

    /// <summary>
    /// The shape of a Codex version text, as a whole text: 1 to 64 characters of ASCII letters, digits,
    /// space, '.', '_', '+' and '-'. A text that ends in a line break, a quote or a path does not match.
    /// </summary>
    public static bool IsVersionText(string? text)
    {
        if (text is null || text.Length is < 1 or > MaxVersionTextLength)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not (' ' or '.' or '_' or '+' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<bool> IsSignedInAsync(string codexPath, CancellationToken ct)
    {
        var result = await RunAsync(codexPath, ["login", "status"], maxOutputBytes: 0, ct).ConfigureAwait(false);
        return result is { Outcome: ProcessOutcome.Exited, ExitCode: 0 };
    }

    public async Task<string?> ReadVersionAsync(string codexPath, CancellationToken ct)
    {
        var result = await RunAsync(codexPath, ["--version"], MaxVersionOutputBytes, ct).ConfigureAwait(false);
        if (result is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 })
        {
            return null;
        }

        var line = FirstLine(Encoding.UTF8.GetString(result.StandardOutput.Span)).Trim();
        return IsVersionText(line) ? line : null;
    }

    private static string FirstLine(string text)
    {
        var end = text.AsSpan().IndexOfAny('\r', '\n');
        return end < 0 ? text : text[..end];
    }

    /// <summary>Null when the process could not even be asked for (a path the runner refuses).</summary>
    private async Task<ProcessResult?> RunAsync(
        string codexPath, string[] arguments, int maxOutputBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ProcessResult result;
        try
        {
            result = await _processes.RunAsync(
                new ProcessRequest
                {
                    FileName = codexPath,
                    Arguments = arguments,
                    Environment = EnvironmentPolicy.CodexAllowlist,
                    MaxStandardOutputBytes = maxOutputBytes,
                    Timeout = Limit,
                },
                ct).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The runner starts only the absolute path of an .exe file. Anything else is "no answer".
            return null;
        }

        ct.ThrowIfCancellationRequested();
        return result;
    }
}
