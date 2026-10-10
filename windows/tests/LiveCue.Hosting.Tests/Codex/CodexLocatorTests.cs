using LiveCue.Hosting.Tests.Support;

namespace LiveCue.Hosting.Tests.Codex;

/// <summary>
/// Codex discovery (architecture 3.7, test plan 2.2): the configured path, then <c>PATH</c>, then the
/// per-user install folder. Everything is in a fake file system; no real folder is looked at.
/// </summary>
public sealed class CodexLocatorTests
{
    private const string Configured = @"C:\Tools\codex.exe";
    private const string LocalAppData = @"C:\Users\tester\AppData\Local";
    private const string UserBin = LocalAppData + @"\OpenAI\Codex\bin";

    private static readonly DateTime Older = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_setting_wins_and_a_missing_file_fails()
    {
        // Codex is also on the search path and in the per-user folder: neither is looked at.
        var files = EverywhereElse().AddFile(Configured);
        var locator = new CodexLocator(files);

        Assert.Equal(new CodexLocation(CodexSource.Setting, Configured, CodexProblem.None), locator.Locate(Configured));

        // A configured file that is missing is a failure, not a fall-through to the other places.
        var missing = new CodexLocation(CodexSource.None, null, CodexProblem.ConfiguredFileMissing);
        Assert.Equal(missing, locator.Locate(@"C:\Gone\codex.exe"));

        // Only a file named codex.exe at an absolute path is ever returned.
        files.AddFile(@"C:\Tools\codex.cmd").AddFile(@"C:\Tools\other.exe").AddFile(@"codex.exe");
        Assert.Equal(missing, locator.Locate(@"C:\Tools\codex.cmd"));
        Assert.Equal(missing, locator.Locate(@"C:\Tools\other.exe"));
        Assert.Equal(missing, locator.Locate("codex.exe"));
        Assert.Equal(missing, locator.Locate(string.Empty));

        // The name is compared without regard to case, as the file system does.
        Assert.Equal(CodexSource.Setting, locator.Locate(@"C:\Tools\CODEX.EXE").Source);
    }

    [Fact]
    public void Path_finds_only_a_real_executable()
    {
        var files = new FakeHostFileSystem()
            .SetEnvironment("PATH", @"C:\Shims;relative\bin; ;""C:\Quoted Folder"";C:\First\;C:\Second")
            .AddFile(@"C:\Shims\codex.cmd")
            .AddFile(@"C:\Shims\codex.ps1")
            .AddFile(@"C:\Shims\codex")
            .AddFile(@"relative\bin\codex.exe")
            .AddFile(@"C:\First\codex.exe")
            .AddFile(@"C:\Second\codex.exe");
        var locator = new CodexLocator(files);

        // The shims in the first folder are passed over; the folders are walked in order.
        Assert.Equal(new CodexLocation(CodexSource.SearchPath, @"C:\First\codex.exe", CodexProblem.None), locator.Locate(null));

        // A quoted entry is read without its quotes, and comes before the later folders.
        files.AddFile(@"C:\Quoted Folder\codex.exe");
        Assert.Equal(@"C:\Quoted Folder\codex.exe", locator.Locate(null).Path);

        // The search path wins over the per-user install folder.
        files.SetEnvironment("LOCALAPPDATA", LocalAppData).AddFile(UserBin + @"\codex.exe");
        Assert.Equal(CodexSource.SearchPath, locator.Locate(null).Source);
    }

    [Fact]
    public void The_newest_user_install_is_chosen_within_the_depth()
    {
        var files = new FakeHostFileSystem()
            .SetEnvironment("PATH", @"C:\Windows\System32")
            .SetEnvironment("LOCALAPPDATA", LocalAppData)
            .AddFile(UserBin + @"\codex.exe", string.Empty, Older)
            .AddFile(UserBin + @"\1.2.3\x64\codex.exe", string.Empty, Newer)
            .AddFile(UserBin + @"\1.2.2\codex.exe", string.Empty, Older.AddDays(1))

            // Newer than all of them, but four levels deep and so never seen.
            .AddFile(UserBin + @"\a\b\c\d\codex.exe", string.Empty, Newer.AddDays(30))

            // Newer shims are not Codex.
            .AddFile(UserBin + @"\codex.cmd", string.Empty, Newer.AddDays(30))

            // A folder beside "bin" is not the install folder.
            .AddFile(LocalAppData + @"\OpenAI\Codex\codex.exe", string.Empty, Newer.AddDays(30));
        var locator = new CodexLocator(files);

        Assert.Equal(
            new CodexLocation(CodexSource.UserInstall, UserBin + @"\1.2.3\x64\codex.exe", CodexProblem.None),
            locator.Locate(null));

        // Three levels deep is still inside.
        files.AddFile(UserBin + @"\a\b\c\codex.exe", string.Empty, Newer.AddDays(1));
        Assert.Equal(UserBin + @"\a\b\c\codex.exe", locator.Locate(null).Path);
    }

    [Fact]
    public void Nothing_found_is_not_found()
    {
        var notFound = new CodexLocation(CodexSource.None, null, CodexProblem.NotFound);

        // No search path and no per-user folder at all.
        Assert.Equal(notFound, new CodexLocator(new FakeHostFileSystem()).Locate(null));

        // Both exist and hold no codex.exe; shims do not count.
        var files = new FakeHostFileSystem()
            .SetEnvironment("PATH", @"C:\Shims;C:\Windows\System32")
            .SetEnvironment("LOCALAPPDATA", LocalAppData)
            .AddFile(@"C:\Shims\codex.cmd")
            .AddFile(@"C:\Shims\codex.ps1")
            .AddFile(UserBin + @"\codex.cmd");
        Assert.Equal(notFound, new CodexLocator(files).Locate(null));

        // A per-user folder that is not an absolute path is not searched.
        var relative = new FakeHostFileSystem()
            .SetEnvironment("LOCALAPPDATA", "Local")
            .AddFile(@"Local\OpenAI\Codex\bin\codex.exe");
        Assert.Equal(notFound, new CodexLocator(relative).Locate(null));
    }

    private static FakeHostFileSystem EverywhereElse() => new FakeHostFileSystem()
        .SetEnvironment("PATH", @"C:\OnPath")
        .SetEnvironment("LOCALAPPDATA", LocalAppData)
        .AddFile(@"C:\OnPath\codex.exe")
        .AddFile(UserBin + @"\codex.exe");
}
