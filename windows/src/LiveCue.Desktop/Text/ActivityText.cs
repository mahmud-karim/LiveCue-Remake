namespace LiveCue.Desktop.Text;

/// <summary>
/// Strings of the Activity page (screens section 4). First version (foundation): the lines the
/// foundation's log can show. Work package WP-W09 adds the rest.
/// </summary>
internal static class ActivityText
{
    public const string RelayStarted = "Relay started. Waiting for your iPhone.";

    public const string PublicMode =
        "Public mode: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address.";

    /// <summary>Entry 0 of a log that was trimmed (architecture 3.12).</summary>
    public const string TrimMarker = "[Older activity removed from memory]";

    public const string OneEntry = "1 entry";

    public static string Entries(int count) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{count} entries");
}
