using System.ComponentModel;
using System.Windows.Input;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop;

/// <summary>One stale mapping of ours and the sentence that describes it.</summary>
public sealed record StaleMappingRow(OwnedMapping Entry, string Text);

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

    bool IsCheckingAssistant { get; }

    /// <summary>Blank means automatic.</summary>
    string LabFolderText { get; set; }

    /// <summary>SpeechLabStatus.</summary>
    StatusIndicator LabFolderStatus { get; }

    string? LabFolderDetail { get; }

    /// <summary>LoadedModelStatus.</summary>
    StatusIndicator LoadedModelStatus { get; }

    string SpeechCaption { get; }

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

    ICommand CopyAddressCommand { get; }

    ICommand OpenSourceCommand { get; }

    ICommand OpenNoticesCommand { get; }
}
