namespace LiveCue.Hosting;

/// <summary>
/// Finds <c>codex.exe</c> (architecture 3.7).
///
/// First version (foundation): step 1 only. A configured path is used when its file exists and is a
/// failure when it does not; with no configured path nothing is found. Walking the search path and the
/// per-user install folder are work package WP-W06.
/// </summary>
internal sealed class CodexLocator : ICodexLocator
{
    private readonly IHostFileSystem _files;

    public CodexLocator(IHostFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
    }

    public CodexLocation Locate(string? configuredPath)
    {
        if (configuredPath is null)
        {
            return new CodexLocation(CodexSource.None, null, CodexProblem.NotFound);
        }

        return string.Equals(Path.GetFileName(configuredPath), SettingsValidator.CodexFileName, StringComparison.OrdinalIgnoreCase)
            && _files.FileExists(configuredPath)
            ? new CodexLocation(CodexSource.Setting, configuredPath, CodexProblem.None)
            : new CodexLocation(CodexSource.None, null, CodexProblem.ConfiguredFileMissing);
    }
}
