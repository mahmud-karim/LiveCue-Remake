namespace LiveCue.Desktop.Text;

/// <summary>Strings of the shell, the sidebar and the session's own messages (screens section 1; W-design 5.4, 5.12).</summary>
internal static class ShellText
{
    public const string WindowTitle = "LiveCue";

    public const string Live = "Live";
    public const string Pairing = "Pairing";
    public const string Activity = "Activity";
    public const string Settings = "Settings";

    /// <summary>The caption of the sidebar status card.</summary>
    public const string ClosingStopsRelay = "Closing this window stops the relay.";

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

    /// <summary>The diagnostic line for an event whose handling threw (WIN-10). Only the type name is shown.</summary>
    public static string EventNotShown(Exception exception) =>
        $"{DiagnosticPrefix}An event could not be shown ({exception.GetType().Name}).";
}
