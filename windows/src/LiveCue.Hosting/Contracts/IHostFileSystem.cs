namespace LiveCue.Hosting;

public sealed record FoundFile(string Path, DateTime LastWriteTimeUtc);

/// <summary>File-system and environment seam of the hosting library.</summary>
public interface IHostFileSystem
{
    /// <summary>The folder of the running executable (AppContext.BaseDirectory).</summary>
    string BaseDirectory { get; }

    /// <summary>The Program Files folder from the known-folder API
    /// (Environment.GetFolderPath(SpecialFolder.ProgramFiles)), never from an environment variable.</summary>
    string ProgramFilesDirectory { get; }

    /// <summary>True when <paramref name="path"/> starts with a drive letter, a colon and a backslash and
    /// that drive is a fixed local volume (DriveType.Fixed). False for every other form and on any error.</summary>
    bool IsOnFixedLocalDrive(string path);

    string? GetEnvironmentVariable(string name);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    byte[] ReadAllBytes(string path);

    /// <summary>Writes path + ".pending", flushes, then replaces the file in one rename.</summary>
    void WriteAllBytesAtomic(string path, byte[] contents);

    /// <summary>Files named exactly <paramref name="fileName"/> (ordinal, ignoring case) under
    /// <paramref name="directory"/>, at most <paramref name="maxDepth"/> levels below it. Never throws;
    /// folders that cannot be read are skipped.</summary>
    IReadOnlyList<FoundFile> FindFiles(string directory, string fileName, int maxDepth);
}
