namespace LiveCue.Desktop;

/// <summary>
/// Every automation id of docs/design/03-windows-screens.md section 9 and the six ids the security review
/// added, each constant named exactly like its value. Views set <c>AutomationProperties.AutomationId</c> from here with <c>x:Static</c>; the
/// self-test finds elements by it.
/// </summary>
public static class AutomationIds
{
    // Live
    public const string StartRelayButton = nameof(StartRelayButton);
    public const string PauseRelayButton = nameof(PauseRelayButton);
    public const string StopRelayButton = nameof(StopRelayButton);
    public const string RelayStatusText = nameof(RelayStatusText);
    public const string PhoneStatusText = nameof(PhoneStatusText);
    public const string PcAddressText = nameof(PcAddressText);
    public const string TranscriptHint = nameof(TranscriptHint);
    public const string TranscriptText = nameof(TranscriptText);
    public const string ReplyHint = nameof(ReplyHint);
    public const string ReplyText = nameof(ReplyText);
    public const string ReplyTiming = nameof(ReplyTiming);
    public const string PrerequisiteList = nameof(PrerequisiteList);
    public const string ConnectionBanner = nameof(ConnectionBanner);
    public const string LiveSetUpConnectionButton = nameof(LiveSetUpConnectionButton);

    // Sidebar
    public const string NavLive = nameof(NavLive);
    public const string NavPairing = nameof(NavPairing);
    public const string NavActivity = nameof(NavActivity);
    public const string NavSettings = nameof(NavSettings);
    public const string SidebarRelayStatus = nameof(SidebarRelayStatus);
    public const string SidebarPhoneStatus = nameof(SidebarPhoneStatus);

    // Pairing
    public const string PairingQr = nameof(PairingQr);
    public const string ShowQrButton = nameof(ShowQrButton);
    public const string HideQrButton = nameof(HideQrButton);
    public const string PairingHint = nameof(PairingHint);
    public const string PairingAddressField = nameof(PairingAddressField);
    public const string PairingVerifyNotice = nameof(PairingVerifyNotice);
    public const string ResetPairingButton = nameof(ResetPairingButton);
    public const string ResetPairingDialog = nameof(ResetPairingDialog);
    public const string ConfirmResetPairingButton = nameof(ConfirmResetPairingButton);

    // Activity
    public const string ActivityLog = nameof(ActivityLog);
    public const string ClearActivityButton = nameof(ClearActivityButton);

    // Settings, Connection, left column
    public const string ConnectionModeSelector = nameof(ConnectionModeSelector);
    public const string RelayPortField = nameof(RelayPortField);
    public const string PrivateHttpsPortField = nameof(PrivateHttpsPortField);
    public const string PublicHttpsPortSelector = nameof(PublicHttpsPortSelector);
    public const string PublicPathPrefixField = nameof(PublicPathPrefixField);
    public const string SettingsAddressField = nameof(SettingsAddressField);
    public const string SaveSettingsButton = nameof(SaveSettingsButton);
    public const string DiscardSettingsButton = nameof(DiscardSettingsButton);
    public const string RestartNotice = nameof(RestartNotice);
    public const string SettingsFileNotice = nameof(SettingsFileNotice);

    // Settings, Connection, Tailscale mapping
    public const string MappingStatus = nameof(MappingStatus);
    public const string MappingCommandText = nameof(MappingCommandText);
    public const string SetUpMappingButton = nameof(SetUpMappingButton);
    public const string CheckMappingButton = nameof(CheckMappingButton);
    public const string RemoveMappingButton = nameof(RemoveMappingButton);
    public const string StaleMappingList = nameof(StaleMappingList);
    public const string SetUpMappingDialog = nameof(SetUpMappingDialog);
    public const string RemoveMappingDialog = nameof(RemoveMappingDialog);

    // Settings, Assistant
    public const string CodexPathField = nameof(CodexPathField);
    public const string CodexPathStatus = nameof(CodexPathStatus);
    public const string CodexLoginStatus = nameof(CodexLoginStatus);
    public const string CheckAssistantButton = nameof(CheckAssistantButton);

    // Settings, PC speech models
    public const string SpeechLabFolderField = nameof(SpeechLabFolderField);
    public const string SpeechLabStatus = nameof(SpeechLabStatus);
    public const string LoadedModelStatus = nameof(LoadedModelStatus);

    // Settings, About
    public const string ThirdPartyNoticesButton = nameof(ThirdPartyNoticesButton);
    public const string SourceCodeButton = nameof(SourceCodeButton);

    // Added by the security review. Screens section 9 does not list these six yet; until it does, their
    // definition is W-design 3.9, 5.5, 5.7 and 5.8.
    public const string ExposureBanner = nameof(ExposureBanner);
    public const string ActivityRejectedCounts = nameof(ActivityRejectedCounts);
    public const string UnownedExposureList = nameof(UnownedExposureList);
    public const string CodexVersionStatus = nameof(CodexVersionStatus);
    public const string MarkContainmentCheckedButton = nameof(MarkContainmentCheckedButton);
    public const string HideFromCaptureToggle = nameof(HideFromCaptureToggle);
}
