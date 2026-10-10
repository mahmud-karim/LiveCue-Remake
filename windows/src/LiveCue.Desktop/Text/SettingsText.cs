namespace LiveCue.Desktop.Text;

/// <summary>
/// Strings of the Settings page and the mapping dialog (screens section 5 and 6.2). First version
/// (foundation): the texts the stub page shows. Work package WP-W10 adds the rest.
/// </summary>
internal static class SettingsText
{
    public const string PrivateMode = "Private tailnet";
    public const string PublicMode = "Public Funnel";

    public const string PrivateExplanation =
        "Reachable only from devices signed in to your tailnet. The iPhone needs Tailscale connected.";

    public const string PublicExplanation =
        "Reachable from the internet over HTTPS and protected by the pairing token, not by tailnet membership. The iPhone needs only an internet connection.";

    public const string AddressUnknown = "Shown when Tailscale is connected";

    public const string NotReportedYet = "Not reported yet";
    public const string NotChecked = "Not checked";
    public const string CodexVersionUnknown = "Unknown";
    public const string Automatic = "Automatic";

    public const string SetUpTitle = "Set up the Tailscale mapping?";
    public const string RemoveTitle = "Remove the Tailscale mapping?";
    public const string DialogIntro = "LiveCue will run:";
    public const string SetUp = "Set up";
    public const string Remove = "Remove";

    public const string CommandDidNotRun = "Tailscale did not complete the command. Nothing else was changed.";
}
