namespace LiveCue.Hosting;

public enum CodexSource { None, Setting, SearchPath, UserInstall }

public enum CodexProblem
{
    None,

    /// <summary>codexExecutablePath is set and the file is missing. Not a fall-through.</summary>
    ConfiguredFileMissing,
    NotFound,
}

public sealed record CodexLocation(CodexSource Source, string? Path, CodexProblem Problem);

/// <summary>Discovery order of architecture 3.7. Only a file named exactly codex.exe is ever returned.</summary>
public interface ICodexLocator
{
    CodexLocation Locate(string? configuredPath);
}

/// <summary>The two questions asked of the Codex executable itself (architecture 3.7). Both run it with
/// the CodexAllowlist environment.</summary>
public interface ICodexSignInCheck
{
    /// <summary>Runs "&lt;codex&gt; login status" (limit in architecture 3.7); its output is discarded.
    /// True for exit code 0. False for anything else, including a time-out.</summary>
    Task<bool> IsSignedInAsync(string codexPath, CancellationToken ct);

    /// <summary>
    /// Runs "&lt;codex&gt; --version" (limit 15 s, at most 256 bytes of output kept). Returns the first
    /// line, trimmed, when the exit code is 0 and the line is 1 to 64 characters of letters, digits,
    /// space, '.', '_', '+' and '-'. Null for anything else. This is the only Codex output that is ever
    /// kept or shown. Never throws except for cancellation.
    /// </summary>
    Task<string?> ReadVersionAsync(string codexPath, CancellationToken ct);
}
