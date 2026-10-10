namespace LiveCue.Desktop.Text;

/// <summary>
/// Strings of the Pairing page and its dialog (screens section 3 and 6.1). First version (foundation):
/// the texts of the page at rest and of the reset dialog. Work package WP-W10 adds the rest.
/// </summary>
internal static class PairingText
{
    public const string InitialHint =
        "Pair your iPhone once. Restarts and showing this QR keep existing pairings valid.";

    public const string TileRelayNotRunning = "Start the relay to show the QR";
    public const string TileConnectionNotSetUp = "Set up the connection first.";
    public const string TilePlaceholder = "Show the QR to pair an iPhone";

    public const string CaptionRelayNotRunning = "Start the relay first.";
    public const string CaptionConnectionNotSetUp = "Set up the connection first.";

    public const string QrHidden = "hidden";

    public const string ResetTitle = "Reset pairing?";

    public const string ResetBody =
        "Every paired iPhone stops working until it scans the new QR.";

    public const string ResetFailed =
        "The pairing could not be reset. Check that LiveCue can write its data folder, then retry.";
}
