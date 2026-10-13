using LiveCue.Desktop.Live;
using LiveCue.Hosting;

namespace LiveCue.Desktop.Tests.Live;

/// <summary>The rows of the prerequisite list (test plan 2.7; screens 2.6; W-design 5.5).</summary>
public sealed class PrerequisiteRowsTests
{
    private const string CodexPath = @"C:\Tools\Codex\codex.exe";

    [Fact]
    public void Rows_follow_the_report()
    {
        // No report yet: no rows.
        Assert.Empty(PrerequisiteRows.From(null));

        // Everything passed: one line each, in the fixed order. The Codex row says which one was found.
        var passed = PrerequisiteRows.From(PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>()));
        Assert.Equal(
            [
                new PrerequisiteRow(PrerequisiteId.DataFolder, PrerequisiteState.Passed, "LiveCue's data folder is ready", null),
                new PrerequisiteRow(PrerequisiteId.CodexFound, PrerequisiteState.Passed, "Codex CLI found", CodexPath),
                new PrerequisiteRow(PrerequisiteId.CodexSignedIn, PrerequisiteState.Passed, "Codex is signed in", null),
                new PrerequisiteRow(PrerequisiteId.TailscaleInstalled, PrerequisiteState.Passed, "Tailscale is installed", null),
                new PrerequisiteRow(PrerequisiteId.TailscaleOnline, PrerequisiteState.Passed, "Tailscale is online", null),
                new PrerequisiteRow(PrerequisiteId.TailscaleDnsName, PrerequisiteState.Passed, "This PC is pc.example.test", null),
            ],
            passed);

        // Every check failed that can fail by itself: what is wrong, and what to do.
        var failed = PrerequisiteRows.From(PageParts.Report(
            Enum.GetValues<PrerequisiteId>().ToDictionary(id => id, _ => PrerequisiteState.Failed)));
        Assert.Equal(
            [
                new PrerequisiteRow(
                    PrerequisiteId.DataFolder,
                    PrerequisiteState.Failed,
                    "LiveCue could not open its data folder",
                    "Check that your Windows account can write to its local application data folder, then choose Check again."),
                new PrerequisiteRow(
                    PrerequisiteId.CodexFound,
                    PrerequisiteState.Failed,
                    "Codex CLI was not found",
                    "Install or update the Codex desktop app, then open it once and sign in."),
                new PrerequisiteRow(
                    PrerequisiteId.CodexSignedIn,
                    PrerequisiteState.Failed,
                    "Codex is installed but not signed in",
                    "Open Codex and sign in with ChatGPT, then choose Check again."),
                new PrerequisiteRow(
                    PrerequisiteId.TailscaleInstalled,
                    PrerequisiteState.Failed,
                    "Tailscale is not installed",
                    "Install Tailscale, sign in, then choose Check again."),
                new PrerequisiteRow(
                    PrerequisiteId.TailscaleOnline,
                    PrerequisiteState.Failed,
                    "Tailscale is not online",
                    "Open Tailscale and connect first."),
                new PrerequisiteRow(
                    PrerequisiteId.TailscaleDnsName,
                    PrerequisiteState.Failed,
                    "Tailscale did not return this PC's DNS name",
                    "Check that MagicDNS is on for your tailnet, then choose Check again."),
            ],
            failed);

        // No Codex and no Tailscale: the checks that depend on them were not made. Each keeps its pass
        // text and says "Not checked"; all six rows are there, not only the first failure.
        var dependent = PrerequisiteRows.From(PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>
        {
            [PrerequisiteId.CodexFound] = PrerequisiteState.Failed,
            [PrerequisiteId.CodexSignedIn] = PrerequisiteState.NotChecked,
            [PrerequisiteId.TailscaleInstalled] = PrerequisiteState.Failed,
            [PrerequisiteId.TailscaleOnline] = PrerequisiteState.NotChecked,
            [PrerequisiteId.TailscaleDnsName] = PrerequisiteState.NotChecked,
        }));
        Assert.Equal(
            [
                PrerequisiteId.DataFolder, PrerequisiteId.CodexFound, PrerequisiteId.CodexSignedIn,
                PrerequisiteId.TailscaleInstalled, PrerequisiteId.TailscaleOnline, PrerequisiteId.TailscaleDnsName,
            ],
            dependent.Select(row => row.Id));
        Assert.Equal(new PrerequisiteRow(PrerequisiteId.CodexSignedIn, PrerequisiteState.NotChecked, "Codex is signed in", "Not checked"), dependent[2]);
        Assert.Equal(new PrerequisiteRow(PrerequisiteId.TailscaleOnline, PrerequisiteState.NotChecked, "Tailscale is online", "Not checked"), dependent[4]);

        // The name is not known while its check was not made: the row is named after the check.
        Assert.Equal(new PrerequisiteRow(PrerequisiteId.TailscaleDnsName, PrerequisiteState.NotChecked, "PC name", "Not checked"), dependent[5]);
    }

    [Fact]
    public void The_codex_row_carries_the_containment_warning_instead_of_the_path()
    {
        // The Codex in use is not the version of the last containment check (SEC-01): a warning, not a failure.
        var stale = PrerequisiteRows.From(PageParts.Report(
            new Dictionary<PrerequisiteId, PrerequisiteState>(), codexVersion: "1.3.0", checkedVersion: "1.2.3"));
        Assert.Equal(
            new PrerequisiteRow(
                PrerequisiteId.CodexFound,
                PrerequisiteState.Passed,
                "Codex CLI found",
                PrerequisiteTexts.ContainmentWarning("1.3.0", "1.2.3")),
            stale[1]);
        Assert.Contains("Mark as checked", stale[1].Subtitle, StringComparison.Ordinal);

        // No check recorded yet, and a version that could not be read: the same row, its own wording.
        var never = PrerequisiteRows.From(PageParts.Report(
            new Dictionary<PrerequisiteId, PrerequisiteState>(), codexVersion: null, checkedVersion: null));
        Assert.Equal(PrerequisiteTexts.ContainmentWarning(null, null), never[1].Subtitle);

        // A Codex that was not found has no version to warn about: its row is the failure.
        var missing = PrerequisiteRows.From(PageParts.Report(
            new Dictionary<PrerequisiteId, PrerequisiteState> { [PrerequisiteId.CodexFound] = PrerequisiteState.Failed },
            codexVersion: "1.3.0",
            checkedVersion: "1.2.3"));
        Assert.Equal("Install or update the Codex desktop app, then open it once and sign in.", missing[1].Subtitle);
    }
}
