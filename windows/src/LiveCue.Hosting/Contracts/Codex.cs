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

/// <summary>Runs "&lt;codex&gt; login status" (limit in architecture 3.7). Output is discarded.</summary>
public interface ICodexSignInCheck
{
    /// <summary>True for exit code 0. False for anything else, including a time-out.</summary>
    Task<bool> IsSignedInAsync(string codexPath, CancellationToken ct);
}
