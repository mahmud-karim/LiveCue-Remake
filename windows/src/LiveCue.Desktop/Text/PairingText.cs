using System.Globalization;

namespace LiveCue.Desktop.Text;

/// <summary>
/// Strings of the Pairing page and its dialog (screens section 3 and 6.1), character for character.
/// Nothing here is, or is built from, the pairing text: the desktop never holds it (decision A-16).
/// </summary>
internal static class PairingText
{
    // ---- Hint (screens 3.3) ---------------------------------------------------------------------

    public const string InitialHint =
        "Pair your iPhone once. Restarts and showing this QR keep existing pairings valid.";

    public const string ShownHint =
        "On iPhone: Pair Windows PC > Scan PC QR code, then Verify and pair. QR hides after 2 minutes.";

    public const string ResetHint = "Pairing reset. Scan this QR on each iPhone.";

    public const string HiddenByButtonHint = "QR hidden. Existing pairing remains valid.";

    public const string HiddenByTimerHint = "QR hidden for privacy. Existing pairing remains valid.";

    // ---- QR tile (screens 3.2) ------------------------------------------------------------------

    public const string TileTitle = "Your pairing code";
    public const string TileRelayNotRunning = "Start the relay to show the QR";
    public const string TileConnectionNotSetUp = "Set up the connection first.";
    public const string TilePlaceholder = "Show the QR to pair an iPhone";

    /// <summary>The automation name of the QR; its state follows it (screens section 8).</summary>
    public const string QrName = "Pairing QR code";

    public const string QrHidden = "hidden";

    // ---- Buttons and their captions (screens 3.3) -----------------------------------------------

    public const string CaptionRelayNotRunning = "Start the relay first.";
    public const string CaptionConnectionNotSetUp = "Set up the connection first.";

    /// <summary>What the address field reads until the relay has run once.</summary>
    public const string AddressPlaceholder = "Shown when the relay is running";

    // ---- Verification notice (screens 3.3) ------------------------------------------------------

    public const string Verified = "Secure PC connection verified.";

    public const string VerifiedThroughFunnel =
        "Secure Funnel connection verified. The iPhone VPN is not required.";

    public const string NotVerified =
        "Pairing code created, but the advertised connection could not be verified. Check the PC tunnel before pairing.";

    public const string OpenSettings = "Open Settings";

    // ---- Reset confirmation (screens 6.1) -------------------------------------------------------

    public const string ResetTitle = "Reset pairing?";

    /// <summary>The body while no relay runs.</summary>
    public const string ResetBody =
        "This replaces the pairing code. Every iPhone paired with this PC must scan the new QR and pair again.";

    /// <summary>The body while the relay runs.</summary>
    public const string ResetBodyRunning = ResetBody + " The relay keeps running.";

    public const string ResetFailed =
        "The pairing could not be reset. Check that LiveCue can write its data folder, then retry.";

    /// <summary>The countdown chip: "Hides in M:SS".</summary>
    public static string HidesIn(TimeSpan remaining) => string.Create(
        CultureInfo.InvariantCulture,
        $"Hides in {(int)remaining.TotalMinutes}:{remaining.Seconds:00}");

    /// <summary>The state a screen reader hears: "shown, hides in 1 minute 42 seconds".</summary>
    public static string QrShown(TimeSpan remaining)
    {
        var minutes = (int)remaining.TotalMinutes;
        var seconds = remaining.Seconds;
        var parts = new List<string>(2);
        if (minutes > 0)
        {
            parts.Add(minutes == 1 ? "1 minute" : string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes"));
        }

        if (seconds > 0 || minutes == 0)
        {
            parts.Add(seconds == 1 ? "1 second" : string.Create(CultureInfo.InvariantCulture, $"{seconds} seconds"));
        }

        return "shown, hides in " + string.Join(' ', parts);
    }
}
