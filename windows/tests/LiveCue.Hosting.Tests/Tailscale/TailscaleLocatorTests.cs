using LiveCue.Hosting.Tailscale;
using LiveCue.Hosting.Tests.Support;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// Where <c>tailscale.exe</c> is looked for (W-design 4.14). The file found is one the application
/// starts, so the folder comes from the operating system and not from a variable (security test ST-11).
/// </summary>
public sealed class TailscaleLocatorTests
{
    private const string Installed = @"C:\Program Files\Tailscale\tailscale.exe";

    [Fact]
    public void The_program_files_folder_is_not_read_from_the_environment()
    {
        // The variable points at a folder somebody planted; the known folder holds nothing.
        var files = new FakeHostFileSystem { ProgramFilesDirectory = @"C:\Program Files" };
        files.SetEnvironment("ProgramFiles", @"C:\Planted");
        files.SetEnvironment("ProgramW6432", @"C:\Planted");
        files.SetEnvironment("ProgramFiles(x86)", @"C:\Planted");
        files.AddFile(@"C:\Planted\Tailscale\tailscale.exe");

        Assert.Null(TailscaleLocator.Locate(files));

        // With the real installation present, that one is found, whatever the variable says.
        files.AddFile(Installed);
        Assert.Equal(Installed, TailscaleLocator.Locate(files));

        // And the folder of the known-folder API is the one that is asked.
        var elsewhere = new FakeHostFileSystem { ProgramFilesDirectory = @"C:\Programs" };
        elsewhere.AddFile(Installed);
        elsewhere.AddFile(@"C:\Programs\Tailscale\tailscale.exe");
        Assert.Equal(@"C:\Programs\Tailscale\tailscale.exe", TailscaleLocator.Locate(elsewhere));
    }

    [Fact]
    public void Without_an_installation_the_first_one_on_the_search_path_is_used()
    {
        var files = new FakeHostFileSystem();
        files.SetEnvironment("PATH", @"C:\Windows\System32; ""C:\Tools\One"" ;;C:\Tools\Two");
        Assert.Null(TailscaleLocator.Locate(files));

        files.AddFile(@"C:\Tools\Two\tailscale.exe");
        Assert.Equal(@"C:\Tools\Two\tailscale.exe", TailscaleLocator.Locate(files));

        files.AddFile(@"C:\Tools\One\tailscale.exe");
        Assert.Equal(@"C:\Tools\One\tailscale.exe", TailscaleLocator.Locate(files));

        // Program Files comes first.
        files.AddFile(Installed);
        Assert.Equal(Installed, TailscaleLocator.Locate(files));
    }

    [Fact]
    public void Only_a_folder_on_a_drive_of_this_pc_is_searched()
    {
        // Relative entries, a share and a device path are skipped, and so is a script of the same name.
        var files = new FakeHostFileSystem { ProgramFilesDirectory = string.Empty };
        files.SetEnvironment("PATH", @".;tools;\Tools;C:tools;\\server.example.test\share;\\?\C:\Tools;C:\Scripts");
        files.AddFile(@".\tailscale.exe");
        files.AddFile(@"tools\tailscale.exe");
        files.AddFile(@"\Tools\tailscale.exe");
        files.AddFile(@"C:tools\tailscale.exe");
        files.AddFile(@"\\server.example.test\share\tailscale.exe");
        files.AddFile(@"\\?\C:\Tools\tailscale.exe");
        files.AddFile(@"C:\Scripts\tailscale.cmd");
        files.AddFile(@"C:\Scripts\tailscale.ps1");

        Assert.Null(TailscaleLocator.Locate(files));

        // A Program Files folder that is not such a path is not used either.
        var share = new FakeHostFileSystem { ProgramFilesDirectory = @"\\server.example.test\Program Files" };
        share.AddFile(@"\\server.example.test\Program Files\Tailscale\tailscale.exe");
        Assert.Null(TailscaleLocator.Locate(share));
    }

    [Fact]
    public void Looking_only_asks_whether_files_exist()
    {
        var files = new FakeHostFileSystem();
        files.SetEnvironment("PATH", @"C:\Tools");
        files.AddFile(Installed, "contents that are never read");

        Assert.Equal(Installed, TailscaleLocator.Locate(files));
        Assert.Equal(0, files.Writes);
        Assert.Equal([Installed], files.FilePaths);
    }
}
