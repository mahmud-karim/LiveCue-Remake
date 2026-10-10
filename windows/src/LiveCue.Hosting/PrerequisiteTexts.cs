using System.Globalization;
using LiveCue.Relay;

namespace LiveCue.Hosting;

/// <summary>The status-line texts of architecture 3.11, character for character.</summary>
public static class PrerequisiteTexts
{
    /// <summary>What <c>--check-only</c> prints when every check passed (architecture 3.14).</summary>
    public const string Passed = "LiveCue Relay prerequisites passed.";

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
}
