using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Text;
using LiveCue.Relay;

namespace LiveCue.Desktop.Activity;

/// <summary>
/// The Activity page (screens section 4; W-design 5.7).
///
/// First version (foundation): the log shows the start of the relay, notices, faults, diagnostics and
/// the session's own messages. Request and reply lines with their payloads, the speech lines and the
/// character bounds are work package WP-W09.
/// </summary>
internal sealed class ActivityViewModel : ObservableObject, IActivityViewModel
{
    private readonly IDesktopSession _session;
    private readonly ActivityLog _log;
    private readonly RelayCommand _clear;

    public ActivityViewModel(IDesktopSession session, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);

        _session = session;
        _log = new ActivityLog(clock);
        Entries = new ReadOnlyObservableCollection<ActivityEntry>(_log.Entries);
        _clear = new RelayCommand(session.ClearActivity, () => !IsEmpty);

        ((INotifyCollectionChanged)_log.Entries).CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EntryCountText));
            OnPropertyChanged(nameof(Text));
            _clear.NotifyCanExecuteChanged();
        };

        session.RelayEventReceived += OnRelayEvent;
        session.MessageRaised += (_, message) => _log.Add(message.At, message.Text);
        session.Signal += (_, signal) =>
        {
            if (signal is SessionSignal.RelayStarting or SessionSignal.ActivityCleared)
            {
                _log.Clear();
            }
        };
    }

    public ReadOnlyObservableCollection<ActivityEntry> Entries { get; }

    public bool IsEmpty => _log.Entries.Count == 0;

    public string EntryCountText => _log.Entries.Count switch
    {
        0 => string.Empty,
        1 => ActivityText.OneEntry,
        var count => ActivityText.Entries(count),
    };

    public string Text => string.Join(
        Environment.NewLine,
        _log.Entries.Select(entry => entry.Payload is null
            ? $"{entry.TimeText} {entry.Message}"
            : $"{entry.TimeText} {entry.Message}{Environment.NewLine}{entry.Payload}"));

    // First version (foundation): the three counters stay at zero; WP-W09 follows EdgeStatistics.
    public long RejectedMalformed => 0;

    public long RejectedTokenNotAccepted => 0;

    public long RejectedFunnelInPrivateMode => 0;

    public ICommand ClearActivityCommand => _clear;

    private void OnRelayEvent(object? sender, RelayEvent relayEvent)
    {
        switch (relayEvent)
        {
            case RelayStarted started:
                _log.Add(started.At, ActivityText.RelayStarted);
                if (started.Mode == ConnectionMode.Public)
                {
                    _log.Add(started.At, ActivityText.PublicMode);
                }

                break;

            case Notice notice:
                _log.Add(notice.At, notice.Message);
                break;

            case RelayFaulted faulted:
                _log.Add(faulted.At, _session.StateText ?? faulted.Message);
                break;

            case Diagnostic diagnostic:
                _log.Add(diagnostic.At, ShellText.DiagnosticPrefix + diagnostic.Message);
                break;

            default:
                break;
        }
    }
}
