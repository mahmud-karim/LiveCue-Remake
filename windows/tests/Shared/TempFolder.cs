namespace LiveCue.TestSupport;

/// <summary>
/// A folder of its own under the system temp folder, created now and deleted with everything in it on
/// dispose. Tests use it as the data folder; nothing ever touches the real one.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    private const string ParentName = "LiveCueRemake-tests";

    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), ParentName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>The absolute path. It exists until dispose.</summary>
    public string Path { get; }

    /// <summary>The absolute path of a file or folder inside this folder. Nothing is created.</summary>
    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>The names of every file below this folder, relative to it, sorted.</summary>
    public IReadOnlyList<string> FileNames() =>
    [
        .. Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
            .Select(file => System.IO.Path.GetRelativePath(Path, file))
            .Order(StringComparer.Ordinal),
    ];

    public void Dispose()
    {
        // A file can stay locked for a moment after a child process ends; a leftover is not a test failure.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }
}
