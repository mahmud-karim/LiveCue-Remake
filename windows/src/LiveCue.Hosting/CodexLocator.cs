namespace LiveCue.Hosting;

/// <summary>
/// Finds <c>codex.exe</c> (architecture 3.7, W-design 4.14). First match wins:
///
/// 1. the configured path. A file that is missing there is a failure, not a fall-through;
/// 2. the first file named exactly <c>codex.exe</c> in the folders of <c>PATH</c>, in order;
/// 3. the most recently written <c>codex.exe</c> under <c>%LOCALAPPDATA%\OpenAI\Codex\bin\</c>, at most
///    three levels deep.
///
/// Only a file named <c>codex.exe</c> at an absolute path is ever returned. Shims such as
/// <c>codex.cmd</c> and <c>codex.ps1</c> are never looked at: only a real executable is started, so no
/// command interpreter ever parses an argument.
/// </summary>
internal sealed class CodexLocator : ICodexLocator
{
    /// <summary>How deep the per-user install folder is searched.</summary>
    public const int UserInstallDepth = 3;

    private const string SearchPathVariable = "PATH";
    private const string LocalApplicationDataVariable = "LOCALAPPDATA";

    private readonly IHostFileSystem _files;

    public CodexLocator(IHostFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
    }

    public CodexLocation Locate(string? configuredPath)
    {
        if (configuredPath is not null)
        {
            return IsCodexFile(configuredPath) && _files.FileExists(configuredPath)
                ? new CodexLocation(CodexSource.Setting, configuredPath, CodexProblem.None)
                : new CodexLocation(CodexSource.None, null, CodexProblem.ConfiguredFileMissing);
        }

        if (OnSearchPath() is { } onPath)
        {
            return new CodexLocation(CodexSource.SearchPath, onPath, CodexProblem.None);
        }

        if (NewestUserInstall() is { } installed)
        {
            return new CodexLocation(CodexSource.UserInstall, installed, CodexProblem.None);
        }

        return new CodexLocation(CodexSource.None, null, CodexProblem.NotFound);
    }

    /// <summary>An absolute path whose file name is <c>codex.exe</c>, without regard to case.</summary>
    private static bool IsCodexFile(string path) =>
        Path.IsPathFullyQualified(path)
        && string.Equals(Path.GetFileName(path), SettingsValidator.CodexFileName, StringComparison.OrdinalIgnoreCase);

    private string? OnSearchPath()
    {
        var searchPath = _files.GetEnvironmentVariable(SearchPathVariable);
        if (string.IsNullOrEmpty(searchPath))
        {
            return null;
        }

        foreach (var entry in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // An entry may be quoted. A relative entry depends on the current folder and is skipped.
            var folder = entry.Trim('"').Trim();
            if (folder.Length == 0 || !Path.IsPathFullyQualified(folder))
            {
                continue;
            }

            var candidate = Path.Combine(folder, SettingsValidator.CodexFileName);
            if (_files.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? NewestUserInstall()
    {
        var localApplicationData = _files.GetEnvironmentVariable(LocalApplicationDataVariable);
        if (string.IsNullOrWhiteSpace(localApplicationData) || !Path.IsPathFullyQualified(localApplicationData))
        {
            return null;
        }

        var folder = Path.Combine(localApplicationData, "OpenAI", "Codex", "bin");
        return _files.FindFiles(folder, SettingsValidator.CodexFileName, UserInstallDepth)
            .Where(found => IsCodexFile(found.Path))
            .OrderByDescending(found => found.LastWriteTimeUtc)
            .ThenBy(found => found.Path, StringComparer.OrdinalIgnoreCase)
            .Select(found => found.Path)
            .FirstOrDefault();
    }
}
