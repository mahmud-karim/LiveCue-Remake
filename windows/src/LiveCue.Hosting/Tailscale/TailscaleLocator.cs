namespace LiveCue.Hosting.Tailscale;

/// <summary>
/// Finds <c>tailscale.exe</c> (W-design 4.14): <c>Tailscale\tailscale.exe</c> under the Program Files
/// folder of the known-folder API, else the first <c>tailscale.exe</c> on the search path. The
/// <c>ProgramFiles</c> environment variable is never read: a variable can be set by whatever started
/// this process, and the file found here is one this application runs.
/// </summary>
internal static class TailscaleLocator
{
    public const string FolderName = "Tailscale";
    public const string FileName = "tailscale.exe";

    private const string SearchPathVariable = "PATH";

    /// <summary>The absolute path of the executable, or null when Tailscale is not installed. Only asks
    /// whether files exist; nothing is opened or started.</summary>
    public static string? Locate(IHostFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var programFiles = files.ProgramFilesDirectory;
        if (IsLocalFolder(programFiles))
        {
            var installed = Path.Combine(programFiles, FolderName, FileName);
            if (files.FileExists(installed))
            {
                return installed;
            }
        }

        var searchPath = files.GetEnvironmentVariable(SearchPathVariable);
        if (string.IsNullOrEmpty(searchPath))
        {
            return null;
        }

        foreach (var entry in searchPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // An entry that is relative, on a share or in a device namespace is skipped: only a folder
            // on a drive of this PC can name a file that is started.
            var folder = entry.Trim('"');
            if (!IsLocalFolder(folder))
            {
                continue;
            }

            var candidate = Path.Combine(folder, FileName);
            if (files.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>A drive letter, a colon and a separator, with no character a path cannot hold.</summary>
    private static bool IsLocalFolder(string? folder) =>
        folder is { Length: >= 3 }
        && char.IsAsciiLetter(folder[0])
        && folder[1] == ':'
        && folder[2] is '\\' or '/'
        && folder.AsSpan().IndexOfAny(Path.GetInvalidPathChars()) < 0;
}
