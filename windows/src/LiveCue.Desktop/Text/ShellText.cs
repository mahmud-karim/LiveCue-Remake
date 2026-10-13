using LiveCue.Hosting;

namespace LiveCue.Desktop.Text;

/// <summary>
/// Strings of the shell, the sidebar and the session's own messages (screens section 1; W-design 5.4,
/// 5.9 and 5.12, and the outcome texts of 4.15).
/// </summary>
internal static class ShellText
{
    public const string WindowTitle = "LiveCue";

    public const string Live = "Live";
    public const string Pairing = "Pairing";
    public const string Activity = "Activity";
    public const string Settings = "Settings";

    /// <summary>The automation name of the sidebar's list of views.</summary>
    public const string Views = "Views";

    /// <summary>The two kickers of the sidebar status card.</summary>
    public const string RelayKicker = "Relay";
    public const string PhoneKicker = "iPhone";

    /// <summary>The caption of the sidebar status card.</summary>
    public const string ClosingStopsRelay = "Closing this window stops the relay.";

    /// <summary>The names of the three caption buttons (design system 3.5).</summary>
    public const string Minimize = "Minimize";
    public const string Maximize = "Maximize";
    public const string Restore = "Restore";
    public const string Close = "Close";

    /// <summary>The only toast text (W-design 5.9).</summary>
    public const string Copied = "Copied";

    /// <summary>The message box of a start-up that failed before the window was shown (W-design 5.12).</summary>
    public const string StartupFailed = "LiveCue could not finish startup.";

    /// <summary>The activity line for an action of the window that threw (R-36).</summary>
    public const string CommandFailed = "The desktop command failed. Please retry.";

    /// <summary>The prefix of every diagnostic line (architecture 3.10).</summary>
    public const string DiagnosticPrefix = "Relay diagnostic: ";

    /// <summary>The session message of a stored-pairing reset (W-design 5.4).</summary>
    public const string PairingReset = "Pairing reset. Every iPhone must pair again.";

    /// <summary>W-design 5.12: the display affinity call failed on this build of Windows.</summary>
    public const string CaptureExclusionUnavailable = "This version of Windows cannot hide the window from screen capture.";

    // What a mapping command that worked says in the activity (screens 4.1). Every other outcome has
    // the mapping manager's own text (W-design 4.15, MappingTexts).
    public const string MappingSetUp = "Tailscale mapping set up.";
    public const string MappingRemoved = "Tailscale mapping removed.";

    /// <summary>The diagnostic line for an event whose handling threw (WIN-10). Only the type name is shown.</summary>
    public static string EventNotShown(Exception exception) =>
        $"{DiagnosticPrefix}An event could not be shown ({exception.GetType().Name}).";

    /// <summary>The diagnostic line for an exception that reached the dispatcher (W-design 5.12). Only the type name is shown.</summary>
    public static string UnexpectedError(Exception exception) =>
        $"{DiagnosticPrefix}The window handled an unexpected error ({exception.GetType().Name}).";

    /// <summary>
    /// The activity line of one mapping result: the session's own line for a command that worked, the
    /// mapping manager's text for a refusal or a failure, and null when there is nothing to say.
    /// </summary>
    public static string? MappingOutcome(MappingPlan plan, MappingResult result)
    {
        if (result.Outcome == Hosting.MappingOutcome.Done)
        {
            return plan.Action is MappingAction.RemovePrivate or MappingAction.RemovePublic ? MappingRemoved : MappingSetUp;
        }

        var text = MappingTexts.Failure(result, plan);
        return text.Length == 0 ? null : text;
    }
}
