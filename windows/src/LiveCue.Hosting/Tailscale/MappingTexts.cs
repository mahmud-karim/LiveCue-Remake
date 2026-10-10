using System.Globalization;

namespace LiveCue.Hosting;

/// <summary>
/// The effect and refusal texts of the mapping manager (W-design 4.15).
///
/// First version (foundation): the effect sentences and the refusal texts. The texts of the outcomes
/// other than Refused (a failed or timed-out command, "not verified", "ledger not saved", "others
/// changed") are work package WP-W07.
/// </summary>
public static class MappingTexts
{
    private const string UnknownAddress = "this PC's Tailscale address";

    /// <summary>One sentence in plain language for a confirmation dialog.</summary>
    /// <param name="endpoint">The advertised endpoint of that mapping, or null when the name is unknown.</param>
    /// <param name="relayPort">The relay port of the target. Not used by the remove forms.</param>
    public static string Effect(MappingAction action, string? endpoint, int relayPort)
    {
        var address = endpoint ?? UnknownAddress;
        var port = relayPort.ToString(CultureInfo.InvariantCulture);
        return action switch
        {
            MappingAction.ApplyPrivate =>
                $"Tailscale serves {address} inside your tailnet and forwards it to the LiveCue relay on this PC (port {port}, this PC only). Nothing becomes reachable from the internet.",
            MappingAction.ApplyPublic =>
                $"Tailscale Funnel publishes {address} on the internet and forwards it to the LiveCue relay on this PC (port {port}, this PC only). Anyone can reach the address; only an iPhone with the pairing token gets past it.",
            MappingAction.RemovePrivate =>
                $"Tailscale stops serving {address} inside your tailnet. iPhones paired with that address can no longer reach this PC.",
            MappingAction.RemovePublic =>
                $"Tailscale stops publishing {address} on the internet. iPhones paired with that address can no longer reach this PC.",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }

    /// <summary>Why the manager will not run a command. Empty for <see cref="MappingRefusal.None"/>.</summary>
    public static string Refusal(MappingRefusal refusal, int httpsPort, string path)
    {
        var port = httpsPort.ToString(CultureInfo.InvariantCulture);
        return refusal switch
        {
            MappingRefusal.None => string.Empty,
            MappingRefusal.TailscaleNotInstalled => "Tailscale is not installed in Program Files.",
            MappingRefusal.StatusUnavailable or MappingRefusal.StatusUnreadable =>
                "LiveCue could not read Tailscale's configuration, so it changed nothing. Check that Tailscale is running, then choose Check again.",
            MappingRefusal.InvalidSettings => "The connection settings are not valid. Check the ports and the path, then save.",
            MappingRefusal.ForbiddenRelayPort =>
                "Port 47831 belongs to the old LiveCue and is never used. Choose another relay port.",
            MappingRefusal.PrivatePortOccupied =>
                $"Port {port} already has other Tailscale handlers. Choose another private HTTPS port.",
            MappingRefusal.PrivatePortHasFunnel =>
                $"Port {port} has Funnel turned on. A private listener there would turn Funnel off for the whole port. Choose another private HTTPS port.",
            MappingRefusal.PublicMountOccupied =>
                $"The path {path} on port {port} is already used by something else. Choose another path.",
            MappingRefusal.PublicPortHasTcpForward =>
                $"Port {port} forwards raw TCP and cannot take a Funnel path. Choose another public port.",
            MappingRefusal.PublicPortWouldPublishOthers =>
                $"Port {port} has tailnet-only handlers. Turning Funnel on there would publish them to the internet. Choose a port that already has Funnel on, or an unused one.",
            MappingRefusal.AlreadyPresent => "The connection is already set up.",
            MappingRefusal.NotPresent => "That mapping no longer exists in Tailscale. LiveCue removed it from its own list.",
            MappingRefusal.NotInLedger => "This mapping was not created by LiveCue, so LiveCue will not remove it.",
            MappingRefusal.LedgerMismatch =>
                "The mapping no longer matches what LiveCue created, so LiveCue will not remove it.",
            MappingRefusal.PlanOutOfDate =>
                "The settings changed while this dialog was open. Close it and look at the command again.",
            _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
        };
    }
}
