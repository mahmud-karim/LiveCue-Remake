using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Text;
using LiveCue.Relay;

namespace LiveCue.Desktop.Activity;

/// <summary>
/// The Activity page (screens section 4; W-design 5.7): a readable record of what the relay did since
/// it started. Every line comes from an event of the session; a request and a reply carry their
/// payload as indented JSON. The log lives in memory only and is bounded (<see cref="ActivityLog"/>).
///
/// The three rejected-request counters (SEC-14) are not log entries: they are the totals of the
/// relay's latest <see cref="EdgeStatistics"/> event, shown as plain numbers in one caption line. They
/// start again from zero with each relay and are not cleared by "Clear activity".
/// </summary>
internal sealed class ActivityViewModel : ObservableObject, IActivityViewModel
{
    private readonly IDesktopSession _session;
    private readonly ActivityLog _log;
    private readonly RelayCommand _clear;
    private long _rejectedMalformed;
    private long _rejectedTokenNotAccepted;
    private long _rejectedFunnelInPrivateMode;

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
        session.Signal += OnSignal;
    }

    public ReadOnlyObservableCollection<ActivityEntry> Entries { get; }

    public bool IsEmpty => _log.Entries.Count == 0;

    public string EntryCountText => ActivityText.EntryCount(_log.EntryCount);

    public string Text => _log.ToText();

    public long RejectedMalformed => _rejectedMalformed;

    public long RejectedTokenNotAccepted => _rejectedTokenNotAccepted;

    public long RejectedFunnelInPrivateMode => _rejectedFunnelInPrivateMode;

    /// <summary>The caption line above the log: the three counters in one sentence.</summary>
    public string RejectedCountsText =>
        ActivityText.RejectedCounts(_rejectedMalformed, _rejectedTokenNotAccepted, _rejectedFunnelInPrivateMode);

    public ICommand ClearActivityCommand => _clear;

    private void OnSignal(object? sender, SessionSignal signal)
    {
        switch (signal)
        {
            case SessionSignal.RelayStarting:
                // A new relay instance: its log starts empty and it counts from zero.
                _log.Clear();
                SetRejected(0, 0, 0);
                break;

            case SessionSignal.ActivityCleared:
                _log.Clear();
                break;

            default:
                break;
        }
    }

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

            case SelfCheckCompleted selfCheck:
                _log.Add(selfCheck.At, ActivityText.SelfCheck(selfCheck.Result));
                break;

            case RequestStarted request:
                _log.Add(
                    request.At,
                    ActivityText.Incoming(request.Kind, request.RequestId),
                    PayloadFormatter.Indent(request.PayloadJson));
                break;

            case RequestCompleted reply:
                _log.Add(
                    reply.At,
                    ActivityText.Reply(reply.RequestId, reply.DurationMs),
                    PayloadFormatter.Indent(reply.PayloadJson));
                break;

            case RequestFailed failed:
                _log.Add(failed.At, LiveText.Failure(failed.Code));
                break;

            case AssistantNotice assistant:
                _log.Add(assistant.At, assistant.Message);
                break;

            case LocalModelChanged model:
                _log.Add(model.At, model.Message);
                break;

            case SpeechStreamChanged speech:
                _log.Add(speech.At, speech.Message);
                break;

            case RelayFaulted faulted:
                _log.Add(faulted.At, _session.StateText ?? faulted.Message);
                break;

            case Diagnostic diagnostic:
                _log.Add(diagnostic.At, ShellText.DiagnosticPrefix + diagnostic.Message);
                break;

            case EdgeStatistics statistics:
                // Numbers only: no entry, no banner, no toast, whatever the totals are.
                SetRejected(statistics.Malformed, statistics.TokenNotAccepted, statistics.FunnelInPrivateMode);
                break;

            default:
                break;
        }
    }

    private void SetRejected(long malformed, long tokenNotAccepted, long funnelInPrivateMode)
    {
        var changed = SetProperty(ref _rejectedMalformed, malformed, nameof(RejectedMalformed));
        changed |= SetProperty(ref _rejectedTokenNotAccepted, tokenNotAccepted, nameof(RejectedTokenNotAccepted));
        changed |= SetProperty(ref _rejectedFunnelInPrivateMode, funnelInPrivateMode, nameof(RejectedFunnelInPrivateMode));
        if (changed)
        {
            OnPropertyChanged(nameof(RejectedCountsText));
        }
    }
}
