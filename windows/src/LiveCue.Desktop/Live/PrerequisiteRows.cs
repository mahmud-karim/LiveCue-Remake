using LiveCue.Desktop.Text;
using LiveCue.Hosting;

namespace LiveCue.Desktop.Live;

/// <summary>
/// The rows of the prerequisite list from a report: order, titles, fixes, "Not checked" (screens 2.6;
/// W-design 5.5). Every check gets a row, in the fixed order of the checks, not only the first failure.
///
///   passed       its pass text; the Codex row adds the path that was found, or, when that Codex is
///                not the version of the last containment check, the containment warning (SEC-01)
///   failed       what is wrong, and under it what to do
///   not checked  its pass text with "Not checked": a check it depends on failed
/// </summary>
internal static class PrerequisiteRows
{
    public static IReadOnlyList<PrerequisiteRow> From(PrerequisiteReport? report)
    {
        if (report is null)
        {
            return [];
        }

        return [.. report.Results.OrderBy(result => result.Id).Select(result => Row(result, report))];
    }

    private static PrerequisiteRow Row(PrerequisiteResult result, PrerequisiteReport report) => result.State switch
    {
        PrerequisiteState.Failed => new PrerequisiteRow(
            result.Id, result.State, LiveText.CheckFailed(result.Id), LiveText.CheckFix(result.Id)),

        PrerequisiteState.Passed => new PrerequisiteRow(
            result.Id, result.State, LiveText.CheckPassed(result.Id, result.Detail ?? report.DnsName), PassedDetail(result, report)),

        _ => new PrerequisiteRow(
            result.Id, result.State, LiveText.CheckPassed(result.Id, report.DnsName), LiveText.NotChecked),
    };

    private static string? PassedDetail(PrerequisiteResult result, PrerequisiteReport report)
    {
        if (result.Id != PrerequisiteId.CodexFound)
        {
            return null;
        }

        return report.ContainmentCheckIsStale
            ? PrerequisiteTexts.ContainmentWarning(report.CodexVersion, report.SettingsLoad.Settings.CodexContainmentCheckedVersion)
            : result.Detail ?? report.CodexExecutablePath;
    }
}
