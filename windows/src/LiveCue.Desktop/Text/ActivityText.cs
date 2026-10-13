using System.Globalization;
using LiveCue.Relay;

namespace LiveCue.Desktop.Text;

/// <summary>
/// Strings of the Activity page (screens section 4; W-design 5.7). A line that repeats a text of the
/// relay, the session or the mapping manager shows that text as it arrives and has no constant here.
/// </summary>
internal static class ActivityText
{
    public const string ClearActivity = "Clear activity";

    public const string RelayStarted = "Relay started. Waiting for your iPhone.";

    public const string PublicMode =
        "Public mode: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address.";

    /// <summary>Entry 0 of a log that was trimmed (architecture 3.12).</summary>
    public const string TrimMarker = "[Older activity removed from memory]";

    public const string OneEntry = "1 entry";

    public const string EmptyTitle = "No activity yet";
    public const string EmptyBody = "Start the relay, then tap Assist on your iPhone.";

    public const string Footer =
        "Activity stays in memory on this PC. It is cleared when LiveCue closes and is never written to disk.";

    // The result of checking the advertised address after a QR was shown (screens 3.3 and 4.1).
    public const string SelfCheckVerified = "Secure PC connection verified.";

    public const string SelfCheckVerifiedThroughFunnel =
        "Secure Funnel connection verified. The iPhone VPN is not required.";

    public const string SelfCheckNotVerified =
        "Pairing code created, but the advertised connection could not be verified. Check the PC tunnel before pairing.";

    public static string Entries(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} entries");

    /// <summary>"N entries" for the footer; empty for an empty log.</summary>
    public static string EntryCount(int count) => count switch
    {
        <= 0 => string.Empty,
        1 => OneEntry,
        _ => Entries(count),
    };

    public static string SelfCheck(SelfCheckResult result) => result switch
    {
        SelfCheckResult.Verified => SelfCheckVerified,
        SelfCheckResult.VerifiedThroughFunnel => SelfCheckVerifiedThroughFunnel,
        _ => SelfCheckNotVerified,
    };

    /// <summary>"INCOMING assist [id]" or "INCOMING summary [id]".</summary>
    public static string Incoming(AnswerKind kind, string requestId) =>
        $"INCOMING {(kind == AnswerKind.Summary ? "summary" : "assist")} [{requestId}]";

    /// <summary>"REPLY [id] 0.91 s": the relay's total for the request, in seconds.</summary>
    public static string Reply(string requestId, double durationMs) => string.Create(
        CultureInfo.InvariantCulture,
        $"REPLY [{requestId}] {durationMs / 1000:0.00} s");

    /// <summary>
    /// The one caption line above the log (W-design 5.7; SEC-14): the three totals of the relay's
    /// rejected-request counters as plain numbers. It never changes colour and never becomes an alert.
    /// </summary>
    public static string RejectedCounts(long malformed, long tokenNotAccepted, long funnelInPrivateMode) => string.Create(
        CultureInfo.InvariantCulture,
        $"Rejected since the relay started: {malformed} malformed · {tokenNotAccepted} with an unknown token · {funnelInPrivateMode} through Funnel in Private mode");
}
