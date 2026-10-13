using LiveCue.Desktop.Composition;

namespace LiveCue.Desktop.Settings;

/// <summary>
/// The About block of the Settings page (screens 5.4): the version of this executable, the licence,
/// where the source is, and the third-party notices that ship beside the app.
/// </summary>
internal static class AboutInfo
{
    public const string Licence = "MIT";

    /// <summary>The version of this executable ("1.0.0").</summary>
    public static string Version => AppEnvironment.Version;

    /// <summary>The public repository: "Source code on GitHub".</summary>
    public static Uri SourceUrl { get; } = new("https://github.com/mahmud-karim/LiveCue-Remake");

    /// <summary>THIRD-PARTY-NOTICES.md beside the executable: every component the app ships and its licence.</summary>
    public static string NoticesPath => AppEnvironment.ThirdPartyNoticesPath;
}
