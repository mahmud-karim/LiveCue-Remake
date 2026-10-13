using System.Globalization;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop.Text;

/// <summary>
/// Strings of the Live page (screens section 2; W-design 5.5). The relay and phone status texts are the
/// session's (<c>Session/StatusTexts.cs</c>); the reason a mapping is blocked and the "exposure unknown"
/// sentence are the mapping manager's (<see cref="MappingTexts"/>).
/// </summary>
internal static class LiveText
{
    // ---- Relay controls (screens 2.2) -----------------------------------------------------------

    public const string StartRelay = "Start relay";
    public const string Checking = "Checking…";
    public const string PauseRelay = "Pause relay";
    public const string ResumeRelay = "Resume relay";
    public const string StopRelay = "Stop relay";

    // ---- PC address (screens 2.5) ---------------------------------------------------------------

    public const string PcAddressPlaceholder = "Shown when the relay is running";

    /// <summary>The name and the tooltip of the copy button beside the address.</summary>
    public const string CopyPcAddress = "Copy PC address";

    // ---- Prerequisite list (screens 2.6) --------------------------------------------------------

    public const string PrerequisitesTitle = "Before the relay can start";
    public const string CheckAgain = "Check again";
    public const string OpenSettings = "Open Settings";

    /// <summary>The subtitle of a check that could not run because one it depends on failed.</summary>
    public const string NotChecked = "Not checked";

    /// <summary>The title of the PC name row while no name is known: the check's own name.</summary>
    public const string PcNameUnknown = "PC name";

    // ---- Incoming transcript (screens 2.7) ------------------------------------------------------

    public const string TranscriptHint = "Live from your iPhone";
    public const string TranscriptPlaceholder = "Your next question will appear here.";
    public const string TranscriptReceived = "Received from your iPhone";
    public const string TranscriptSummaryRequested = "Session summary requested";

    /// <summary>Starts the block of the interim text, drawn in ink.secondary.</summary>
    public const string InProgressPrefix = "[In progress] ";

    /// <summary>Starts the block of the instruction, drawn in ink.secondary.</summary>
    public const string InstructionPrefix = "Instruction: ";

    // ---- Assistant reply (screens 2.8) ----------------------------------------------------------

    public const string ReplyHint = "Powered by Codex on your PC";
    public const string ReplyPlaceholder = "Ready when you are.\nTap Assist on your iPhone.";
    public const string ReplyProcessing = "Processing with Codex on your PC";
    public const string Thinking = "Thinking…";
    public const string ReplyNotSent = "Not sent. Try Assist again.";
    public const string ReplyCleared = "Activity cleared";

    public const string KeyPoints = "Key points";
    public const string ActionItems = "Action items";

    public const string FailureTitle = "Nothing was sent to the iPhone";
    public const string FailureBody = "Assistant request failed or timed out. Check Codex sign-in and retry.";
    public const string FailureCancelled = "Cancelled by the iPhone.";

    // ---- Connection not set up (screens 2.10) ---------------------------------------------------

    public const string ConnectionNotSetUp =
        "Connection not set up. Choose Set up connection to let your iPhone reach this PC.";

    public const string SetUpConnection = "Set up connection…";

    // ---- Exposure warning (W-design 5.5) --------------------------------------------------------

    public const string ExposureTitle = "This relay is reachable from the internet";

    /// <summary>The hint of a reply that was sent: who answered and how long Codex took.</summary>
    public static string ReplySent(string model, string reasoningEffort, double codexMs) => string.Create(
        CultureInfo.InvariantCulture,
        $"Sent to iPhone · {model} · {reasoningEffort} · {codexMs / 1000:0.00} s Codex");

    /// <summary>The caption under the reply card: the relay's own total for the request.</summary>
    public static string ReplyTiming(double durationMs) => string.Create(
        CultureInfo.InvariantCulture,
        $"PC relay total: {durationMs / 1000:0.000} s. Codex includes startup and cloud processing.");

    /// <summary>
    /// The body of the failure banner, by the outcome code of the request (ruling R-12): a request the
    /// phone cancelled, or whose connection the phone dropped, is not a failure of the assistant.
    /// </summary>
    public static string Failure(string? code) => code is RelayCodes.RequestCancelled or RelayCodes.ClientDisconnected
        ? FailureCancelled
        : FailureBody;

    /// <summary>What a check row reads when the check passed.</summary>
    /// <param name="pcName">This PC's Tailscale name, for the last check; null while it is not known.</param>
    public static string CheckPassed(PrerequisiteId id, string? pcName = null) => id switch
    {
        PrerequisiteId.DataFolder => "LiveCue's data folder is ready",
        PrerequisiteId.CodexFound => "Codex CLI found",
        PrerequisiteId.CodexSignedIn => "Codex is signed in",
        PrerequisiteId.TailscaleInstalled => "Tailscale is installed",
        PrerequisiteId.TailscaleOnline => "Tailscale is online",
        PrerequisiteId.TailscaleDnsName => string.IsNullOrEmpty(pcName) ? PcNameUnknown : "This PC is " + pcName,
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    /// <summary>What is wrong: the title of a failed check.</summary>
    public static string CheckFailed(PrerequisiteId id) => id switch
    {
        PrerequisiteId.DataFolder => "LiveCue could not open its data folder",
        PrerequisiteId.CodexFound => "Codex CLI was not found",
        PrerequisiteId.CodexSignedIn => "Codex is installed but not signed in",
        PrerequisiteId.TailscaleInstalled => "Tailscale is not installed",
        PrerequisiteId.TailscaleOnline => "Tailscale is not online",
        PrerequisiteId.TailscaleDnsName => "Tailscale did not return this PC's DNS name",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    /// <summary>What to do about it: the second line of a failed check.</summary>
    public static string CheckFix(PrerequisiteId id) => id switch
    {
        PrerequisiteId.DataFolder =>
            "Check that your Windows account can write to its local application data folder, then choose Check again.",
        PrerequisiteId.CodexFound => "Install or update the Codex desktop app, then open it once and sign in.",
        PrerequisiteId.CodexSignedIn => "Open Codex and sign in with ChatGPT, then choose Check again.",
        PrerequisiteId.TailscaleInstalled => "Install Tailscale, sign in, then choose Check again.",
        PrerequisiteId.TailscaleOnline => "Open Tailscale and connect first.",
        PrerequisiteId.TailscaleDnsName => "Check that MagicDNS is on for your tailnet, then choose Check again.",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    /// <summary>
    /// The exposure warning for mappings that forward to the relay and that LiveCue did not create
    /// (W-design 5.5). The sentence is the design's "mapping(s) … point", written out for one and for many.
    /// </summary>
    public static string UnmanagedMappings(int count) => count == 1
        ? "1 Tailscale mapping that LiveCue does not manage points at this relay. Open Settings to see it and how to remove it."
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{count} Tailscale mappings that LiveCue does not manage point at this relay. Open Settings to see them and how to remove them.");

    /// <summary>Which hint a new request shows above the transcript.</summary>
    public static string RequestHint(AnswerKind kind) =>
        kind == AnswerKind.Summary ? TranscriptSummaryRequested : TranscriptReceived;
}
