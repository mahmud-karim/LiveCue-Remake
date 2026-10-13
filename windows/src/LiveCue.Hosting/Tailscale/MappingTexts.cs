using System.Globalization;
using System.Text;

namespace LiveCue.Hosting;

/// <summary>
/// Every text of the mapping manager (W-design 4.15): the effect sentence of a confirmation dialog, the
/// refusals, the outcomes of a command that ran, and the exposure warnings. The desktop shows them as
/// they are; nothing here builds a command that the application runs.
/// </summary>
public static class MappingTexts
{
    /// <summary>Rule TS-7: shown prominently whenever <see cref="MappingResult.OthersChanged"/> is true.</summary>
    public const string OthersChanged =
        "Warning: other entries of Tailscale's serve configuration changed during this command. LiveCue repaired nothing. Review them with: tailscale serve status";

    /// <summary>Shown when the serve status could not be read: exposure is unknown, not absent.</summary>
    public const string ExposureUnknown =
        "Tailscale's configuration could not be read, so LiveCue cannot tell what is exposed. Choose Check again.";

    /// <summary>What an exposure row reads when <see cref="ManualRemoval"/> has no command to offer.</summary>
    public const string ManualRemovalUnavailable = "Remove it with Tailscale's own tools: tailscale serve status";

    private const string UnknownAddress = "this PC's Tailscale address";
    private const int MaxManualMountLength = 129;
    private const int MaxShownMountLength = 128;

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

    /// <summary>
    /// The text of an outcome of a command that ran. Empty for <see cref="MappingOutcome.Done"/> and for
    /// <see cref="MappingOutcome.Refused"/>, whose text is <see cref="Refusal"/>.
    /// </summary>
    public static string Outcome(MappingOutcome outcome, MappingAction action)
    {
        var removal = action is MappingAction.RemovePrivate or MappingAction.RemovePublic;
        return outcome switch
        {
            MappingOutcome.Done or MappingOutcome.Refused => string.Empty,
            MappingOutcome.CommandFailed => removal
                ? "Tailscale could not remove the mapping. Check that Tailscale is connected."
                : "Tailscale HTTPS setup failed. Check that Tailscale is connected.",
            MappingOutcome.CommandTimedOut =>
                "Tailscale did not finish in time. Run the command shown above in a terminal to see Tailscale's own instructions. The first use of HTTPS or Funnel on a tailnet must be approved in the Tailscale admin console.",
            MappingOutcome.NotVerified => removal
                ? "Tailscale reported success, but LiveCue could not confirm that the mapping is gone. Choose Check again."
                : "Tailscale reported success, but the mapping is not in its configuration. Choose Check again.",
            MappingOutcome.LedgerNotSaved =>
                "The mapping changed, but LiveCue could not save its settings. LiveCue will treat the mapping as not its own.",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
    }

    /// <summary>
    /// What a mapping dialog shows after <see cref="IMappingManager.ExecuteAsync"/>: empty for Done, the
    /// refusal text for Refused, otherwise the outcome text. <see cref="OthersChanged"/> is separate.
    /// </summary>
    public static string Failure(MappingResult result, MappingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(plan);
        var command = plan.Command;
        return result.Outcome == MappingOutcome.Refused
            ? Refusal(result.Refusal, command?.HttpsPort ?? 0, command?.Path ?? string.Empty)
            : Outcome(result.Outcome, plan.Action);
    }

    /// <summary>
    /// The sentence of an exposure warning: a handler that forwards to the relay and that LiveCue did not
    /// create. The mount comes from Tailscale's configuration, so it is shown without control characters
    /// and cut at 128 characters.
    /// </summary>
    public static string Exposure(ExposedHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var port = handler.HttpsPort.ToString(CultureInfo.InvariantCulture);
        var mount = Shown(handler.Mount);
        var sentence = handler.FunnelOn
            ? $"A Tailscale Funnel mapping on port {port}, path {mount}, publishes this relay on the internet. LiveCue did not create it and will not remove it."
            : $"A Tailscale mapping on port {port}, path {mount}, forwards to this relay inside your tailnet. LiveCue did not create it and will not remove it.";
        return handler.Foreground
            ? sentence + " It belongs to a running Tailscale session or service; end that to remove it."
            : sentence;
    }

    /// <summary>
    /// The command the owner can run in a terminal to remove a handler LiveCue does not manage. Text only:
    /// the application never runs it and it is never made into a <see cref="MappingCommand"/>. Empty for a
    /// foreground or service handler, for a port outside 1 to 65535, and for a mount that is not "/" or
    /// "/" followed by 1 to 128 letters, digits, ".", "_", "~", "-" and "/": a mount with any other
    /// character is never put into a command somebody is asked to paste.
    /// </summary>
    public static string ManualRemoval(ExposedHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (handler.Foreground || handler.HttpsPort is < 1 or > 65535 || !IsSafeMount(handler.Mount))
        {
            return string.Empty;
        }

        var port = handler.HttpsPort.ToString(CultureInfo.InvariantCulture);
        return handler.FunnelOn
            ? $"tailscale funnel --https={port} --set-path={handler.Mount} off"
            : $"tailscale serve --https={port} --set-path={handler.Mount} off";
    }

    private static bool IsSafeMount(string? mount)
    {
        if (mount is null || mount.Length is 0 or > MaxManualMountLength || mount[0] != '/')
        {
            return false;
        }

        foreach (var character in mount)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '~' or '-' or '/'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A mount as it is shown: no control, format, separator or surrogate characters, at most 128 characters.</summary>
    private static string Shown(string? mount)
    {
        if (string.IsNullOrEmpty(mount))
        {
            return "(none)";
        }

        var shown = new StringBuilder(Math.Min(mount.Length, MaxShownMountLength) + 1);
        foreach (var character in mount)
        {
            if (shown.Length == MaxShownMountLength)
            {
                shown.Append('…');
                break;
            }

            shown.Append(char.GetUnicodeCategory(character) is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
                ? '�'
                : character);
        }

        return shown.ToString();
    }
}
