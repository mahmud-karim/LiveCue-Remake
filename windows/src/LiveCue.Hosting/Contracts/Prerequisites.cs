namespace LiveCue.Hosting;

/// <summary>Checks 1 to 6 of architecture 3.11, numbered as there.</summary>
public enum PrerequisiteId
{
    DataFolder = 1,
    CodexFound = 2,
    CodexSignedIn = 3,
    TailscaleInstalled = 4,
    TailscaleOnline = 5,
    TailscaleDnsName = 6,
}

public enum PrerequisiteState
{
    Passed,
    Failed,

    /// <summary>Not evaluated because a check it depends on failed, or because it was skipped on request.</summary>
    NotChecked,
}

/// <param name="Detail">For a passed CodexFound: the path. For a passed TailscaleDnsName: the name.
/// Otherwise null. Never tool output.</param>
public sealed record PrerequisiteResult(PrerequisiteId Id, PrerequisiteState State, string? Detail);

/// <param name="CodexPathOverride">Console host --codex. Null means the setting.</param>
/// <param name="SkipTailscale">Console host --endpoint: checks 4 to 6 are NotChecked.</param>
public sealed record PrerequisiteOptions(
    string DataDirectory,
    string? CodexPathOverride = null,
    bool SkipTailscale = false);

/// <summary>All six results, always in id order.</summary>
/// <param name="CodexVersion">The version text of the Codex that was found (ICodexSignInCheck.ReadVersionAsync),
/// or null when no Codex was found or its version could not be read.</param>
public sealed record PrerequisiteReport(
    IReadOnlyList<PrerequisiteResult> Results,
    SettingsLoadResult SettingsLoad,
    string? CodexExecutablePath,
    string? DnsName,
    string? CodexVersion = null)
{
    /// <summary>The first failed check in id order, or null.</summary>
    public PrerequisiteResult? FirstFailure => Results.FirstOrDefault(r => r.State == PrerequisiteState.Failed);

    public bool Passed => FirstFailure is null;

    /// <summary>
    /// True when a Codex was found and its version is unknown or is not the version of the last
    /// containment check (settings, CodexContainmentCheckedVersion; ordinal comparison). A warning only:
    /// it never makes a check fail and nothing is refused because of it (architecture 3.7).
    /// </summary>
    public bool ContainmentCheckIsStale =>
        CodexExecutablePath is not null
        && (CodexVersion is null
            || !string.Equals(CodexVersion, SettingsLoad.Settings.CodexContainmentCheckedVersion, StringComparison.Ordinal));
}

/// <summary>
/// Evaluates the checks in order, each at most once. Dependencies: every check needs 1; 3 needs 2; 5 needs
/// 4; 6 needs 5. A check whose dependency failed is NotChecked. Blocking calls run on the thread pool.
/// Never throws except for cancellation.
/// </summary>
public interface IPrerequisiteChecker
{
    Task<PrerequisiteReport> CheckAsync(PrerequisiteOptions options, CancellationToken ct);
}
