using System.ComponentModel;
using System.Windows.Input;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop;

/// <summary>One stale mapping of ours and the sentence that describes it.</summary>
public sealed record StaleMappingRow(OwnedMapping Entry, string Text);

/// <summary>One row of the exposure report that is not in the ledger: the sentence that describes it and
/// the removal command as text for the owner to run. LiveCue never runs it. CommandText is empty when no
/// command can be shown safely (4.15).</summary>
public sealed record UnownedExposureRow(ExposedHandler Handler, string Text, string CommandText);

/// <summary>The Settings page (screens section 5). Field properties are two-way and hold the text as
/// typed; nothing is saved until SaveCommand.</summary>
public interface ISettingsViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
{
    ConnectionMode Mode { get; set; }

    string ModeExplanation { get; }

    string RelayPortText { get; set; }

    string PrivateHttpsPortText { get; set; }

    /// <summary>443, 8443 or 10000.</summary>
    int PublicHttpsPort { get; set; }

    string PublicPathPrefix { get; set; }

    /// <summary>SettingsAddressField: the endpoint the QR would advertise for the form as it stands, or a
    /// placeholder when Tailscale's name is unknown.</summary>
    string PcAddress { get; }

    /// <summary>MappingStatus for the selected mode.</summary>
    StatusIndicator MappingStatus { get; }

    /// <summary>The blocked reason, or the fix when Tailscale is missing or offline; null otherwise.</summary>
    string? MappingCaption { get; }

    /// <summary>The exact command that Set up would run for the saved settings; empty when none.</summary>
    string MappingCommandText { get; }

    string MappingEffectText { get; }

    bool CanSetUpMapping { get; }

    /// <summary>The selected mode's own mapping when it is Present and in the ledger; null otherwise.
    /// "Remove…" is shown when it is not null.</summary>
    OwnedMapping? CurrentOwnedMapping { get; }

    /// <summary>One standing warning per stale mapping of ours (the other mode's, or one at an old port),
    /// each with a "Remove…" button.</summary>
    IReadOnlyList<StaleMappingRow> StaleMappings { get; }

    /// <summary>One standing warning per handler that forwards to the relay port and is not in the ledger,
    /// each with its removal command as selectable text and no button but "Check again".</summary>
    IReadOnlyList<UnownedExposureRow> UnownedExposures { get; }

    /// <summary>"Tailscale's configuration could not be read, so LiveCue cannot tell what is exposed.", or
    /// null when the last inspection read the serve status.</summary>
    string? ExposureUnknownText { get; }

    /// <summary>"The relay is still using the previous connection settings…", or null.</summary>
    BannerModel? RestartNotice { get; }

    /// <summary>A problem with settings.json at load (running on defaults), or null.</summary>
    BannerModel? SettingsFileNotice { get; }

    /// <summary>Blank means automatic.</summary>
    string CodexPathText { get; set; }

    /// <summary>CodexPathStatus: found path or the fix.</summary>
    StatusIndicator CodexPathStatus { get; }

    string? CodexPathDetail { get; }

    /// <summary>CodexLoginStatus.</summary>
    StatusIndicator CodexLoginStatus { get; }

    /// <summary>CodexVersionStatus: the version text of the Codex in use, or "Unknown" (architecture 3.7).
    /// Success tone when the containment check was run for this version, warning tone otherwise.</summary>
    StatusIndicator CodexVersionStatus { get; }

    /// <summary>The containment warning of architecture 3.7, or "Containment check run for this
    /// version."; null when no Codex was found.</summary>
    string? CodexVersionDetail { get; }

    /// <summary>True when a version was read and it is not the recorded one.</summary>
    bool CanMarkContainmentChecked { get; }

    bool IsCheckingAssistant { get; }

    /// <summary>Blank means automatic.</summary>
    string LabFolderText { get; set; }

    /// <summary>SpeechLabStatus.</summary>
    StatusIndicator LabFolderStatus { get; }

    string? LabFolderDetail { get; }

    /// <summary>LoadedModelStatus.</summary>
    StatusIndicator LoadedModelStatus { get; }

    string SpeechCaption { get; }

    /// <summary>HideFromCaptureToggle: two-way, part of the form like every field; takes effect on the
    /// window when the settings are saved.</summary>
    bool HideFromScreenCapture { get; set; }

    string Version { get; }

    /// <summary>True when the form differs from the saved settings.</summary>
    bool HasChanges { get; }

    /// <summary>Enabled when HasChanges and the form has no errors.</summary>
    ICommand SaveCommand { get; }

    ICommand DiscardCommand { get; }

    /// <summary>Opens the mapping dialog to set up the selected mode.</summary>
    ICommand SetUpMappingCommand { get; }

    /// <summary>Opens the mapping dialog to remove. Parameter: an <see cref="OwnedMapping"/>.</summary>
    ICommand RemoveMappingCommand { get; }

    ICommand CheckMappingCommand { get; }

    ICommand CheckAssistantCommand { get; }

    /// <summary>"Mark as checked": saves the settings with CodexContainmentCheckedVersion set to the
    /// version in use. It changes nothing else, and unsaved form changes are not saved with it.</summary>
    ICommand MarkContainmentCheckedCommand { get; }

    ICommand CopyAddressCommand { get; }

    ICommand OpenSourceCommand { get; }

    ICommand OpenNoticesCommand { get; }
}
