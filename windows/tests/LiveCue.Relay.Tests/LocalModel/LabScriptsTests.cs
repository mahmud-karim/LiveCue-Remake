using System.Text;
using LiveCue.Relay.LocalModel;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.LocalModel;

/// <summary>
/// The relay's own launcher scripts (protocol 11.5, security test ST-11): what is embedded is what
/// <c>asr/</c> holds, and what is written into the data folder is what is embedded.
/// </summary>
public sealed class LabScriptsTests
{
    [Fact]
    public void The_embedded_copies_equal_the_files_of_asr()
    {
        var asr = Path.Combine(RepositoryRoot(), "asr");

        Assert.Equal(["Start-ASR-Lab.ps1", "Stop-ASR-Lab.ps1", "LabCommon.ps1"], LabScripts.FileNames);
        foreach (var fileName in LabScripts.FileNames)
        {
            var embedded = LabScripts.Embedded(fileName).ToArray();

            Assert.NotEmpty(embedded);
            Assert.Equal(File.ReadAllBytes(Path.Combine(asr, fileName)), embedded);
        }
    }

    [Fact]
    public void The_embedded_launchers_load_only_the_shared_script_and_only_from_their_own_folder()
    {
        // The relay copies three files. That is the whole set only as long as each launcher loads exactly
        // LabCommon.ps1 from the folder it runs from, and LabCommon.ps1 loads no further script.
        foreach (var launcher in new[] { LabScripts.StartFileName, LabScripts.StopFileName })
        {
            var loaded = Assert.Single(LoadedScripts(launcher));
            Assert.Equal(". (Join-Path $PSScriptRoot 'LabCommon.ps1')", loaded);
        }

        Assert.Empty(LoadedScripts(LabScripts.CommonFileName));
    }

    [Fact]
    public void Write_copies_writes_the_three_files_and_replaces_what_is_there()
    {
        using var data = new TempFolder();
        var folder = Path.Combine(data.Path, "lab");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, LabScripts.StartFileName), "& calc.exe");
        File.WriteAllText(Path.Combine(folder, LabScripts.CommonFileName), new string('x', 100_000));

        LabScripts.WriteCopies(data.Path);
        LabScripts.WriteCopies(data.Path);

        Assert.Equal(
            [@"lab\LabCommon.ps1", @"lab\Start-ASR-Lab.ps1", @"lab\Stop-ASR-Lab.ps1"],
            data.FileNames());
        foreach (var fileName in LabScripts.FileNames)
        {
            Assert.Equal(LabScripts.FolderFor(data.Path), folder);
            Assert.Equal(Path.Combine(folder, fileName), LabScripts.PathFor(data.Path, fileName));
            Assert.Equal(LabScripts.Embedded(fileName).ToArray(), File.ReadAllBytes(Path.Combine(folder, fileName)));
        }
    }

    [Fact]
    public void Write_copies_fails_loudly_when_a_copy_cannot_be_written()
    {
        using var data = new TempFolder();
        Directory.CreateDirectory(Path.Combine(data.Path, "lab", LabScripts.StopFileName));

        // The relay start turns this into its "could not start" fault.
        var exception = Record.Exception(() => LabScripts.WriteCopies(data.Path));

        Assert.True(exception is IOException or UnauthorizedAccessException);
    }

    [Fact]
    public void A_run_checks_the_launcher_it_starts_and_the_shared_script()
    {
        using var data = new TempFolder();
        var stop = LabScripts.PathFor(data.Path, LabScripts.StopFileName);

        Assert.True(LabScripts.TryMakeCurrent(data.Path, LabScripts.StartFileName));

        // The other launcher is not this run's business.
        Assert.Equal([@"lab\LabCommon.ps1", @"lab\Start-ASR-Lab.ps1"], data.FileNames());
        File.WriteAllText(stop, "& calc.exe");
        Assert.True(LabScripts.TryMakeCurrent(data.Path, LabScripts.StartFileName));
        Assert.Equal("& calc.exe", File.ReadAllText(stop));

        Assert.True(LabScripts.TryMakeCurrent(data.Path, LabScripts.StopFileName));
        Assert.Equal(LabScripts.Embedded(LabScripts.StopFileName).ToArray(), File.ReadAllBytes(stop));
    }

    [Fact]
    public void An_unknown_file_name_is_not_one_of_the_scripts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LabScripts.Embedded("Build-ASR-Images.ps1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => LabScripts.Embedded(@"..\pairing.json"));
    }

    /// <summary>The lines of an embedded script that load another script: dot-sourcing, modules, the call operator on a file.</summary>
    private static List<string> LoadedScripts(string fileName)
    {
        var text = Encoding.UTF8.GetString(LabScripts.Embedded(fileName).Span);
        return
        [
            .. text.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(". ", StringComparison.Ordinal)
                    || line.StartsWith("Import-Module", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("using module", StringComparison.OrdinalIgnoreCase)
                    || (line.StartsWith('&') && line.Contains(".ps1", StringComparison.OrdinalIgnoreCase))),
        ];
    }

    /// <summary>The repository this build came from: the first folder above the tests that holds both sides.</summary>
    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "asr", LabScripts.StartFileName))
                && File.Exists(Path.Combine(folder.FullName, "windows", "LiveCueRemake.sln")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (asr/ beside windows/) was not found above the test folder.");
    }
}
