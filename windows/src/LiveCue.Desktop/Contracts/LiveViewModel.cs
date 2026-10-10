using System.ComponentModel;
using System.Windows.Input;
using LiveCue.Hosting;

namespace LiveCue.Desktop;

/// <summary>One row of the prerequisite list (screens 2.6).</summary>
/// <param name="Subtitle">The fix line for a failure; the detail line for a pass; null otherwise.</param>
public sealed record PrerequisiteRow(PrerequisiteId Id, PrerequisiteState State, string Title, string? Subtitle);

/// <summary>The Live page (screens section 2).</summary>
public interface ILiveViewModel : INotifyPropertyChanged
{
    /// <summary>RelayStatusText: mark, colour and the full status text of screens 2.3.</summary>
    StatusIndicator RelayStatus { get; }

    /// <summary>The caption under "Paused"; null otherwise.</summary>
    string? RelayStatusCaption { get; }

    /// <summary>PhoneStatusText.</summary>
    StatusIndicator PhoneStatus { get; }

    /// <summary>PcAddressText: the endpoint, or the "Shown when the relay is running" placeholder.</summary>
    string PcAddress { get; }

    bool HasPcAddress { get; }

    /// <summary>The "connection not set up" banner (R-14), or null.</summary>
    BannerModel? ConnectionBanner { get; }

    /// <summary>True in Stopped, Checking, PrerequisiteFailed, Starting and Faulted.</summary>
    bool IsStartVisible { get; }

    /// <summary>True in Checking and Starting: the control shows a spinner and "Checking…".</summary>
    bool IsStartBusy { get; }

    string StartLabel { get; }

    /// <summary>True while the relay runs: Pause or Resume, and Stop.</summary>
    bool AreRunningControlsVisible { get; }

    /// <summary>"Pause relay" or "Resume relay".</summary>
    string PauseResumeLabel { get; }

    bool IsPaused { get; }

    /// <summary>True in PrerequisiteFailed: the list replaces the two panels.</summary>
    bool ArePrerequisitesVisible { get; }

    IReadOnlyList<PrerequisiteRow> Prerequisites { get; }

    string TranscriptHint { get; }

    /// <summary>The transcript, then appended speech lines. Bounded (section 5.7).</summary>
    string TranscriptText { get; }

    /// <summary>The "[In progress] …" and "Instruction: …" blocks, drawn in ink.secondary; empty when none.</summary>
    string TranscriptSecondaryText { get; }

    bool IsTranscriptPlaceholder { get; }

    string ReplyHint { get; }

    /// <summary>The answer or summary and, after a blank line, the details.</summary>
    string ReplyText { get; }

    IReadOnlyList<string> ReplyKeyPoints { get; }

    IReadOnlyList<string> ReplyActionItems { get; }

    bool IsReplyPlaceholder { get; }

    bool IsReplyThinking { get; }

    /// <summary>The danger banner of a failed request, or null.</summary>
    BannerModel? ReplyFailure { get; }

    /// <summary>ReplyTiming: the caption under the reply card, or null.</summary>
    string? ReplyTiming { get; }

    ICommand StartRelayCommand { get; }

    ICommand PauseResumeCommand { get; }

    ICommand StopRelayCommand { get; }

    ICommand CheckAgainCommand { get; }

    ICommand OpenSettingsCommand { get; }

    /// <summary>Opens the mapping dialog for the selected mode.</summary>
    ICommand SetUpConnectionCommand { get; }

    ICommand CopyPcAddressCommand { get; }
}
