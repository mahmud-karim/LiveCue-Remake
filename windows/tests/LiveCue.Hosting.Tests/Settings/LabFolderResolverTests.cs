using LiveCue.Hosting.Tests.Support;

namespace LiveCue.Hosting.Tests.Settings;

/// <summary>Which lab folder a relay is given (W-design 4.14; test plan 2.1 and ST-11).</summary>
public sealed class LabFolderResolverTests
{
    private const string Lab = @"C:\Tools\asr";
    private const string Bundled = @"C:\App\asr";

    [Fact]
    public void Resolution_order()
    {
        var withoutBundled = Files();
        var withBundled = Files().AddFile(Bundled + @"\Start-ASR-Lab.ps1").AddFile(Bundled + @"\Stop-ASR-Lab.ps1");
        var configured = new RelaySettings { AsrLabFolder = Lab };

        // The setting, when it still holds both scripts, whatever stands beside the executable.
        Assert.Equal(Lab, LabFolderResolver.Resolve(configured, withoutBundled));
        Assert.Equal(Lab, LabFolderResolver.Resolve(configured, withBundled));

        // A setting whose folder lost a script, or is gone, is "not configured": it does not fall back.
        Assert.Null(LabFolderResolver.Resolve(new RelaySettings { AsrLabFolder = @"C:\Tools\half" }, withBundled));
        Assert.Null(LabFolderResolver.Resolve(new RelaySettings { AsrLabFolder = @"C:\Tools\gone" }, withBundled));

        // No setting: asr\ beside the executable when it holds both scripts, otherwise none.
        Assert.Equal(Bundled, LabFolderResolver.Resolve(RelaySettings.Default, withBundled));
        Assert.Null(LabFolderResolver.Resolve(RelaySettings.Default, withoutBundled));
        Assert.Null(LabFolderResolver.Resolve(
            RelaySettings.Default,
            Files().AddFile(Bundled + @"\Start-ASR-Lab.ps1")));

        // The same rule for a folder that did not come from the settings.
        Assert.Equal(Lab, LabFolderResolver.Resolve(Lab, withBundled));
        Assert.Equal(Bundled, LabFolderResolver.Resolve((string?)null, withBundled));
    }

    [Theory]
    [InlineData(@"\\server\share\asr")]
    [InlineData(@"\\?\C:\Tools\asr")]
    [InlineData(@"\\.\C:\Tools\asr")]
    [InlineData(@"C:asr")]
    [InlineData("asr")]
    [InlineData(@"N:\asr")]
    public void A_folder_that_is_not_on_a_fixed_local_drive_resolves_to_none(string folder)
    {
        // Every one of these folders holds both scripts, and the file as loaded may still name it:
        // a file edited by hand is not validated against the file system. The rule is applied again here.
        var files = Files()
            .AddFile(folder + @"\Start-ASR-Lab.ps1")
            .AddFile(folder + @"\Stop-ASR-Lab.ps1");
        Assert.True(LabFolderResolver.HoldsBothScripts(folder, files));

        Assert.Null(LabFolderResolver.Resolve(new RelaySettings { AsrLabFolder = folder }, files));
        Assert.Null(LabFolderResolver.Resolve(folder, files));
    }

    [Fact]
    public void The_bundled_folder_of_an_application_on_a_network_drive_resolves_to_none()
    {
        var files = Files();
        files.BaseDirectory = @"N:\LiveCue";
        files.AddFile(@"N:\LiveCue\asr\Start-ASR-Lab.ps1").AddFile(@"N:\LiveCue\asr\Stop-ASR-Lab.ps1");

        Assert.Null(LabFolderResolver.Resolve(RelaySettings.Default, files));
    }

    /// <summary>Drive C is a fixed local drive; every other drive is not.</summary>
    private static FakeHostFileSystem Files() => new FakeHostFileSystem
    {
        BaseDirectory = @"C:\App",
        FixedLocalDrive = path => path.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase),
    }
        .AddFile(Lab + @"\Start-ASR-Lab.ps1")
        .AddFile(Lab + @"\Stop-ASR-Lab.ps1")
        .AddFile(@"C:\Tools\half\Stop-ASR-Lab.ps1");
}
