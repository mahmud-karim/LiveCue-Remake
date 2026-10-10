using LiveCue.TestSupport;

namespace LiveCue.Hosting.Tests.Settings;

/// <summary>The production file system of the hosting library, in a temporary folder of its own.</summary>
public sealed class HostFileSystemTests
{
    private readonly HostFileSystem _files = new();

    [Fact]
    public void An_atomic_write_replaces_the_file_and_leaves_no_pending_file()
    {
        using var folder = new TempFolder();
        var path = folder.Combine("settings.json");

        _files.WriteAllBytesAtomic(path, [1, 2, 3]);
        _files.WriteAllBytesAtomic(path, [4, 5]);

        Assert.Equal([4, 5], _files.ReadAllBytes(path));
        Assert.Equal(["settings.json"], folder.FileNames());
        Assert.True(_files.FileExists(path));
        Assert.False(_files.FileExists(path + ".pending"));
    }

    [Fact]
    public void A_write_that_fails_keeps_the_old_file_and_leaves_no_pending_file()
    {
        using var folder = new TempFolder();
        var path = folder.Combine("settings.json");
        _files.WriteAllBytesAtomic(path, [1, 2, 3]);

        // The file is open without sharing, as an editor or a second process would hold it.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<Exception>(() => _files.WriteAllBytesAtomic(path, [4, 5]));
        }

        Assert.Equal([1, 2, 3], _files.ReadAllBytes(path));
        Assert.Equal(["settings.json"], folder.FileNames());
    }

    [Fact]
    public void Files_are_found_down_to_the_given_depth_and_unreadable_folders_are_skipped()
    {
        using var folder = new TempFolder();
        var bin = folder.Combine("bin");
        foreach (var relative in (string[])["codex.exe", @"a\CODEX.EXE", @"a\b\c\codex.exe", @"a\b\c\d\codex.exe", @"a\codex.cmd", @"a\b\other.exe"])
        {
            var path = Path.Combine(bin, relative);
            _files.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }

        var found = _files.FindFiles(bin, "codex.exe", 3).Select(file => Path.GetRelativePath(bin, file.Path)).ToList();

        // Three levels below the folder, the name compared without regard to case; no shim and no deeper file.
        Assert.Equal(3, found.Count);
        Assert.Contains("codex.exe", found);
        Assert.Contains(@"a\codex.exe", found, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"a\b\c\codex.exe", found);
        Assert.Equal(["codex.exe"], _files.FindFiles(bin, "codex.exe", 0).Select(file => Path.GetFileName(file.Path)));
        Assert.All(_files.FindFiles(bin, "codex.exe", 3), file => Assert.Equal(DateTimeKind.Utc, file.LastWriteTimeUtc.Kind));

        // A folder that does not exist is not an error.
        Assert.Empty(_files.FindFiles(folder.Combine("missing"), "codex.exe", 3));
    }

    [Fact]
    public void Only_a_drive_letter_path_on_a_fixed_volume_is_on_a_fixed_local_drive()
    {
        // The Windows system drive is a fixed local volume on every PC that runs these tests.
        var system = Environment.SystemDirectory;
        Assert.True(_files.IsOnFixedLocalDrive(system));
        Assert.True(_files.IsOnFixedLocalDrive(system[..3]));

        Assert.False(_files.IsOnFixedLocalDrive(@"\\server\share\asr"));
        Assert.False(_files.IsOnFixedLocalDrive(@"\\?\" + system));
        Assert.False(_files.IsOnFixedLocalDrive(@"\\.\" + system));
        Assert.False(_files.IsOnFixedLocalDrive(system[..2] + "asr"));
        Assert.False(_files.IsOnFixedLocalDrive(system[..2]));
        Assert.False(_files.IsOnFixedLocalDrive(system.Replace('\\', '/')));
        Assert.False(_files.IsOnFixedLocalDrive("asr"));
        Assert.False(_files.IsOnFixedLocalDrive(string.Empty));
        Assert.False(_files.IsOnFixedLocalDrive(null!));
    }

    [Fact]
    public void The_known_folders_come_from_the_operating_system()
    {
        Assert.Equal(AppContext.BaseDirectory, _files.BaseDirectory);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), _files.ProgramFilesDirectory);
        Assert.True(Path.IsPathFullyQualified(_files.ProgramFilesDirectory));
    }
}
