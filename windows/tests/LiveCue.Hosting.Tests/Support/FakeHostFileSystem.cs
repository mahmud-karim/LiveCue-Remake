namespace LiveCue.Hosting.Tests.Support;

/// <summary>
/// The hosting library's file system and environment in memory. Paths are compared without regard to
/// case, as on Windows. Nothing here ever touches a real disk or the real environment.
/// </summary>
internal sealed class FakeHostFileSystem : IHostFileSystem
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (byte[] Contents, DateTime WrittenUtc)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _environment = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder of the "running executable".</summary>
    public string BaseDirectory { get; set; } = @"C:\LiveCue-tests\app";

    /// <summary>True: creating a folder throws, as for a data folder that cannot be opened.</summary>
    public bool CreateDirectoryFails { get; set; }

    /// <summary>True: every write throws and the old file stays.</summary>
    public bool WritesFail { get; set; }

    public int Writes { get; private set; }

    /// <summary>The paths of every file, sorted.</summary>
    public IReadOnlyList<string> FilePaths
    {
        get
        {
            lock (_gate)
            {
                return [.. _files.Keys.Order(StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    public FakeHostFileSystem SetEnvironment(string name, string? value)
    {
        lock (_gate)
        {
            if (value is null)
            {
                _environment.Remove(name);
            }
            else
            {
                _environment[name] = value;
            }
        }

        return this;
    }

    /// <summary>Puts a file there, with its parent folders.</summary>
    public FakeHostFileSystem AddFile(string path, string contents = "", DateTime? writtenUtc = null) =>
        AddFile(path, System.Text.Encoding.UTF8.GetBytes(contents), writtenUtc);

    public FakeHostFileSystem AddFile(string path, byte[] contents, DateTime? writtenUtc = null)
    {
        lock (_gate)
        {
            _files[path] = ([.. contents], writtenUtc ?? new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc));
            AddParents(path);
        }

        return this;
    }

    public FakeHostFileSystem AddDirectory(string path)
    {
        lock (_gate)
        {
            _directories.Add(Trim(path));
            AddParents(path);
        }

        return this;
    }

    public string? GetEnvironmentVariable(string name)
    {
        lock (_gate)
        {
            return _environment.GetValueOrDefault(name);
        }
    }

    public bool FileExists(string path)
    {
        lock (_gate)
        {
            return _files.ContainsKey(path);
        }
    }

    public bool DirectoryExists(string path)
    {
        lock (_gate)
        {
            return _directories.Contains(Trim(path));
        }
    }

    public void CreateDirectory(string path)
    {
        if (CreateDirectoryFails)
        {
            throw new UnauthorizedAccessException("The folder could not be created.");
        }

        AddDirectory(path);
    }

    public byte[] ReadAllBytes(string path)
    {
        lock (_gate)
        {
            return _files.TryGetValue(path, out var file)
                ? [.. file.Contents]
                : throw new FileNotFoundException("The file does not exist.");
        }
    }

    public void WriteAllBytesAtomic(string path, byte[] contents)
    {
        if (WritesFail)
        {
            throw new IOException("The file could not be written.");
        }

        lock (_gate)
        {
            Writes++;
        }

        AddFile(path, contents);
    }

    public IReadOnlyList<FoundFile> FindFiles(string directory, string fileName, int maxDepth)
    {
        var root = Trim(directory) + Path.DirectorySeparatorChar;
        lock (_gate)
        {
            return
            [
                .. _files
                    .Where(file => file.Key.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Path.GetFileName(file.Key), fileName, StringComparison.OrdinalIgnoreCase)
                        && file.Key[root.Length..].Count(character => character == Path.DirectorySeparatorChar) <= maxDepth)
                    .Select(file => new FoundFile(file.Key, file.Value.WrittenUtc))
                    .OrderBy(found => found.Path, StringComparer.OrdinalIgnoreCase),
            ];
        }
    }

    private static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private void AddParents(string path)
    {
        for (var parent = Path.GetDirectoryName(Trim(path)); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
        {
            _directories.Add(Trim(parent));
        }
    }
}
