namespace LiveCue.Hosting;

/// <summary>The production file system and environment of the hosting library.</summary>
internal sealed class HostFileSystem : IHostFileSystem
{
    public string BaseDirectory => AppContext.BaseDirectory;

    public string ProgramFilesDirectory => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public bool IsOnFixedLocalDrive(string path)
    {
        if (path is null || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
        {
            return false;
        }

        try
        {
            return new DriveInfo(path[..1]).DriveType == DriveType.Fixed;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllBytesAtomic(string path, byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var pending = path + ".pending";
        using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(contents);
            stream.Flush(flushToDisk: true);
        }

        File.Move(pending, path, overwrite: true);
    }

    public IReadOnlyList<FoundFile> FindFiles(string directory, string fileName, int maxDepth)
    {
        var found = new List<FoundFile>();
        Search(directory, fileName, maxDepth, found);
        return found;
    }

    private static void Search(string directory, string fileName, int depthLeft, List<FoundFile> found)
    {
        try
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                found.Add(new FoundFile(candidate, File.GetLastWriteTimeUtc(candidate)));
            }

            if (depthLeft <= 0)
            {
                return;
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                Search(child, fileName, depthLeft - 1, found);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Folders that cannot be read are skipped.
        }
    }
}
