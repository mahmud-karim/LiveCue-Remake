using System.Globalization;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Text;

/// <summary>
/// Strings of the Settings page and the mapping dialog (screens section 5 and 6.2; W-design 5.8). The
/// texts of the mapping manager itself (the command's effect, the refusals, the outcomes, the exposure
/// warnings) are in <see cref="MappingTexts"/> and are shown as they are.
/// </summary>
internal static class SettingsText
{
    // ---- Connection, left column (screens 5.1) --------------------------------------------------

    public const string PrivateMode = "Private tailnet";
    public const string PublicMode = "Public Funnel";

    public const string PrivateExplanation =
        "Reachable only from devices signed in to your tailnet. The iPhone needs Tailscale connected.";

    public const string PublicExplanation =
        "Reachable from the internet over HTTPS and protected by the pairing token, not by tailnet membership. The iPhone needs only an internet connection.";

    /// <summary>What the address field reads while this PC's Tailscale name is not known.</summary>
    public const string AddressUnknown = "Shown when Tailscale is connected";

    public const string RestartNotice =
        "The relay is still using the previous connection settings. Stop and start it to apply the change.";

    public const string SettingsFileNotice =
        "LiveCue could not read its settings file and is using the defaults. Saving here replaces the file.";

    // ---- Field errors (design system 4.6; W-design 4.14 and 5.8) --------------------------------

    public const string RelayPortOutOfRange = "Enter a port from 1024 to 65535.";

    public const string RelayPortIsLabPort = "This port is used by the PC speech lab. Choose another relay port.";

    public const string PrivatePortOutOfRange = "Enter a port from 1 to 65535.";

    public const string PrivatePortIsFunnelPort =
        "Ports 443, 8443 and 10000 can be published by Tailscale Funnel. Choose another port for the private listener.";

    public const string PathPrefixBadFormat =
        "Use a path such as /livecue: a slash, then 1 to 32 lowercase letters, digits or hyphens, starting with a letter or digit.";

    public const string CodexPathNotFound =
        "No file was found at this path. Enter the full path of codex.exe, or leave the field empty.";

    public const string CodexPathWrongFileName = "The file must be named codex.exe.";

    public const string LabFolderNotLocal =
        "Choose a folder on a local drive of this PC. Network shares are not used.";

    public const string LabFolderNotFound = "This folder does not exist.";

    public const string LabFolderMissingScripts =
        "This folder does not hold Start-ASR-Lab.ps1 and Stop-ASR-Lab.ps1.";

    public const string ValueNotAllowed = "This value is not allowed.";

    // ---- Connection, Tailscale mapping (screens 5.1) --------------------------------------------

    public const string MappingActive = "Active";
    public const string MappingActiveNotOwned = "Active, not created by LiveCue";
    public const string MappingNotSetUp = "Not set up";
    public const string MappingBlocked = "Blocked";
    public const string MappingNotChecked = "Not checked";
    public const string TailscaleNotOnline = "Tailscale is not online";
    public const string TailscaleNotInstalled = "Tailscale is not installed";
    public const string TailscaleUnreadable = "LiveCue could not read Tailscale's configuration";

    public const string SaveFirst = "Save the settings first.";
    public const string TailscaleNotOnlineFix = "Open Tailscale and connect, then choose Check again.";
    public const string TailscaleNotInstalledFix = "Install Tailscale and sign in, then choose Check again.";
    public const string TailscaleUnreadableFix = "Check that Tailscale is running, then choose Check again.";

    // ---- Assistant (screens 5.2; W-design 5.8) --------------------------------------------------

    public const string Automatic = "Automatic";
    public const string Found = "Found";
    public const string NotFound = "Not found";
    public const string SignedIn = "Signed in";
    public const string NotSignedIn = "Not signed in";
    public const string NotChecked = "Not checked";

    public const string CodexNotFoundFix =
        "Install or update the Codex desktop app, then open it once and sign in.";

    public const string CodexNotSignedInFix =
        "Open Codex and sign in with ChatGPT, then choose Check again.";

    public const string CodexVersionUnknown = "Unknown";
    public const string ContainmentChecked = "Containment check run for this version.";

    // ---- PC speech models (screens 5.3) ---------------------------------------------------------

    public const string LabNotSetUp = "Not set up";
    public const string NotReportedYet = "Not reported yet";
    public const string NoModel = "None";

    public const string SpeechCaption =
        "Models are started and stopped from the iPhone: Settings, Transcription, PC speech model. Start opens Docker Desktop if needed; one model runs at a time.";

    public const string SpeechSetupHint =
        "PC speech models are optional. Without the lab folder, choose Meta Muse, Voz or Parakeet on the iPhone.";

    // ---- Mapping dialog (screens 6.2) -----------------------------------------------------------

    public const string SetUpTitle = "Set up the Tailscale mapping?";
    public const string RemoveTitle = "Remove the Tailscale mapping?";
    public const string DialogIntro = "LiveCue will run:";
    public const string SetUp = "Set up";
    public const string Remove = "Remove";

    /// <summary>The name a PC speech model is shown by, as the relay's own texts name it (protocol C-04).</summary>
    public static string ModelLabel(string model) => model switch
    {
        "nemotron" => "Nemotron",
        "qwen3" => "Qwen3",
        _ => model,
    };

    /// <summary>The loaded-model row: "Nemotron · ready".</summary>
    public static string ModelLoading(string label) => label + " · loading";

    public static string ModelReady(string label) => label + " · ready";

    public static string ModelInUse(string label) => label + " · in use";

    /// <summary>One stale mapping of ours (screens 5.1, StaleMappingList).</summary>
    public static string StaleMapping(ConnectionMode mode, string address) =>
        $"LiveCue's {(mode == ConnectionMode.Public ? PublicMode : PrivateMode)} mapping at {address} still exists.";

    /// <summary>Where a mapping is, while this PC's Tailscale name is not known.</summary>
    public static string AddressWithoutName(int httpsPort, string path) => string.Create(
        CultureInfo.InvariantCulture,
        $"port {httpsPort}, path {path}");

    /// <summary>The error line of one field for one problem of the settings rules (W-design 4.14).</summary>
    public static string FieldError(SettingsProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return (problem.Field, problem.Code) switch
        {
            (SettingsField.RelayPort, SettingsProblemCode.OutOfRange) => RelayPortOutOfRange,
            (SettingsField.RelayPort, SettingsProblemCode.Forbidden) =>
                MappingTexts.Refusal(MappingRefusal.ForbiddenRelayPort, 0, string.Empty),
            (SettingsField.RelayPort, SettingsProblemCode.Duplicate) => RelayPortIsLabPort,
            (SettingsField.PrivateHttpsPort, SettingsProblemCode.OutOfRange) => PrivatePortOutOfRange,
            (SettingsField.PrivateHttpsPort, SettingsProblemCode.NotAllowed) => PrivatePortIsFunnelPort,
            (SettingsField.PublicPathPrefix, _) => PathPrefixBadFormat,
            (SettingsField.CodexExecutablePath, SettingsProblemCode.WrongFileName) => CodexPathWrongFileName,
            (SettingsField.CodexExecutablePath, _) => CodexPathNotFound,
            (SettingsField.AsrLabFolder, SettingsProblemCode.NotAllowed) => LabFolderNotLocal,
            (SettingsField.AsrLabFolder, SettingsProblemCode.NotFound) => LabFolderNotFound,
            (SettingsField.AsrLabFolder, SettingsProblemCode.MissingScripts) => LabFolderMissingScripts,
            _ => ValueNotAllowed,
        };
    }
}
