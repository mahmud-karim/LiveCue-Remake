using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace LiveCue.Desktop;

/// <summary>One log entry (screens 4.1).</summary>
/// <param name="TimeText">HH:mm:ss in local time; empty for the trim marker.</param>
/// <param name="Payload">Indented JSON of a request or reply; null otherwise.</param>
public sealed record ActivityEntry(DateTimeOffset At, string TimeText, string Message, string? Payload, bool IsTrimMarker = false);

/// <summary>The Activity page (screens section 4).</summary>
public interface IActivityViewModel : INotifyPropertyChanged
{
    /// <summary>Oldest first. When the log was trimmed, entry 0 is the trim marker.</summary>
    ReadOnlyObservableCollection<ActivityEntry> Entries { get; }

    bool IsEmpty { get; }

    /// <summary>"N entries" (the marker is not counted); empty when the log is empty.</summary>
    string EntryCountText { get; }

    /// <summary>The whole log as text, as it would be copied: for tests and the self-test.</summary>
    string Text { get; }

    /// <summary>The three totals of the latest EdgeStatistics event (protocol 5.5), shown as plain numbers
    /// with fixed captions. 0 until the first event and again from each relay start. They are not log
    /// entries, are not cleared by "Clear activity" and raise no alert.</summary>
    long RejectedMalformed { get; }

    long RejectedTokenNotAccepted { get; }

    long RejectedFunnelInPrivateMode { get; }

    ICommand ClearActivityCommand { get; }
}
