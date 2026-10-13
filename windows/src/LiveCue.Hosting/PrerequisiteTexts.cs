using System.Globalization;
using LiveCue.Relay;

namespace LiveCue.Hosting;

/// <summary>
/// The status-line texts of architecture 3.11 and the containment warning of architecture 3.7, character
/// for character. Both hosts show these texts; neither composes its own.
/// </summary>
public static class PrerequisiteTexts
{
    /// <summary>What <c>--check-only</c> prints when every check passed (architecture 3.14).</summary>
    public const string Passed = "LiveCue Relay prerequisites passed.";

    /// <summary>Check 6a: Private mode while a handler with Funnel on forwards to the relay port. The
    /// relay is not started.</summary>
    public const string ExposedInPrivateMode =
        "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode.";

    private const string ContainmentAdvice = "Run the containment check, then choose Mark as checked in Settings.";

    /// <summary>The failure text of one of the checks 1 to 6.</summary>
    public static string Failure(PrerequisiteId id) => id switch
    {
        PrerequisiteId.DataFolder => "LiveCue could not open its data folder.",
        PrerequisiteId.CodexFound =>
            "Codex CLI was not found. Install or update the Codex desktop app, then open it once and sign in.",
        PrerequisiteId.CodexSignedIn => "Codex is installed but not signed in. Open Codex and sign in with ChatGPT.",
        PrerequisiteId.TailscaleInstalled => "Tailscale is not installed in Program Files.",
        PrerequisiteId.TailscaleOnline => "Tailscale is not online. Open Tailscale and connect first.",
        PrerequisiteId.TailscaleDnsName => "Tailscale did not return this PC DNS name.",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    /// <summary>The text of a relay start fault (step 7). <paramref name="port"/> is the relay port that was asked for.</summary>
    public static string StartFault(RelayFaultCode code, int port) => code switch
    {
        RelayFaultCode.PortInUse => string.Create(
            CultureInfo.InvariantCulture,
            $"Port {port} is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay."),
        RelayFaultCode.PairingStoreUnreadable => "The saved pairing could not be read. Reset pairing to create a new one.",
        _ => "Could not start the relay.",
    };

    /// <summary>
    /// The text of a relay start fault when it is known whether a Tailscale mapping points at the relay
    /// port. For a port that is in use while a mapping forwards to it, the text says what may have
    /// happened: another program had the port, and the phone's requests went to it. In every other case
    /// this is the text of the two-parameter form.
    /// </summary>
    /// <param name="mappingPointsAtPort">True when the exposure report lists a mapping to that port.</param>
    public static string StartFault(RelayFaultCode code, int port, bool mappingPointsAtPort) =>
        code == RelayFaultCode.PortInUse && mappingPointsAtPort
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Port {port} is in use by another program while a Tailscale mapping points at it. That program may have received your iPhone's requests, including the pairing token. Free the port, start the relay, then reset pairing.")
            : StartFault(code, port);

    /// <summary>
    /// The warning of architecture 3.7 for a Codex whose version is unknown or is not the version of the
    /// last containment check. A warning only: nothing is refused because of it.
    /// </summary>
    /// <param name="version">The version text of the Codex in use, or null when it could not be read.</param>
    /// <param name="checkedVersion">The version of the last containment check, or null when none was recorded.</param>
    public static string ContainmentWarning(string? version, string? checkedVersion)
    {
        var inUse = version is null ? "Codex's version could not be read" : "Codex is version " + version;
        var lastCheck = checkedVersion is null
            ? "the containment check has not been run yet"
            : "the containment check was last run for " + checkedVersion;
        return inUse + "; " + lastCheck + ". " + ContainmentAdvice;
    }
}
