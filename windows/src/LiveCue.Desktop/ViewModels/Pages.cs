using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop.ViewModels;

// The page view models of the spike. Each has the shape the screen design gives its view
// (docs/design/03-windows-screens.md) and only as much behaviour as the skeleton relay supports;
// the texts are the design's own.

/// <summary>Live: start and stop the relay, relay and phone status, the transcript and reply mirror.</summary>
public sealed class LivePageViewModel : PageViewModel
{
    public LivePageViewModel(RelayViewModel relay)
    {
        ArgumentNullException.ThrowIfNull(relay);
        Relay = relay;
    }

    public override string Title => "Live";

    public RelayViewModel Relay { get; }

    // The mirror is not wired yet: these are the initial texts of the design (2.7 and 2.8).
    public string TranscriptHint { get; } = "Live from your iPhone";

    public string TranscriptText { get; } = "Your next question will appear here.";

    public string ReplyHint { get; } = "Powered by Codex on your PC";

    public string ReplyText { get; } = "Ready when you are.\nTap Assist on your iPhone.";
}

/// <summary>Pairing: the QR tile, the PC address and the reset. Pairing itself is not built yet.</summary>
public sealed partial class PairingPageViewModel : PageViewModel
{
    private const string InitialHint =
        "Pair your iPhone once. Restarts and showing this QR keep existing pairings valid.";

    public PairingPageViewModel(RelayViewModel relay)
    {
        ArgumentNullException.ThrowIfNull(relay);
        Relay = relay;
        Hint = InitialHint;
        Relay.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(RelayViewModel.IsReady))
            {
                Hint = InitialHint;
                OnPropertyChanged(nameof(TileCaption));
                OnPropertyChanged(nameof(ShowQrCaption));
                ShowQrCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public override string Title => "Pairing";

    public RelayViewModel Relay { get; }

    [ObservableProperty]
    public partial string Hint { get; private set; }

    /// <summary>The second line of the QR tile's placeholder.</summary>
    public string TileCaption => Relay.IsReady ? "Show the QR to pair an iPhone" : "Start the relay to show the QR";

    /// <summary>Why "Show QR" is disabled, when it is.</summary>
    public string ShowQrCaption => Relay.IsReady ? string.Empty : "Start the relay first.";

    private bool CanShowQr() => Relay.IsReady;

    /// <summary>"Show QR": disabled until the relay is ready.</summary>
    [RelayCommand(CanExecute = nameof(CanShowQr))]
    private void ShowQr() => Hint = "Pairing is not part of this build yet. The QR will appear here.";

    /// <summary>"Hide QR": disabled while nothing is shown, which in the spike is always.</summary>
    public IRelayCommand HideQrCommand { get; } = new RelayCommand(static () => { }, static () => false);

    /// <summary>"Reset pairing…": needs a pairing to reset, so it stays disabled in the spike.</summary>
    public IRelayCommand ResetPairingCommand { get; } = new RelayCommand(static () => { }, static () => false);
}

/// <summary>Activity: the in-memory log of this run.</summary>
public sealed partial class ActivityPageViewModel : PageViewModel
{
    private readonly ActivityLog _log;

    public ActivityPageViewModel(ActivityLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        ((INotifyCollectionChanged)_log.Entries).CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CountText));
            ClearCommand.NotifyCanExecuteChanged();
        };
    }

    public override string Title => "Activity";

    public ObservableCollection<ActivityEntry> Entries => _log.Entries;

    public bool IsEmpty => _log.Entries.Count == 0;

    /// <summary>The entry count at the right of the footer line; empty while there is nothing to count.</summary>
    public string CountText => _log.Entries.Count switch
    {
        0 => string.Empty,
        1 => "1 entry",
        var count => string.Create(CultureInfo.InvariantCulture, $"{count} entries"),
    };

    private bool CanClear() => !IsEmpty;

    /// <summary>"Clear activity": disabled while the log is empty. No confirmation; nothing here is kept anyway.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear() => _log.Clear();
}

/// <summary>Settings: read-only facts from settings.json, for now.</summary>
public sealed class SettingsPageViewModel : PageViewModel
{
    public SettingsPageViewModel(SettingsLoadResult loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        var settings = loaded.Settings;

        IsPublic = settings.ConnectionMode == ConnectionMode.Public;
        ModeText = IsPublic ? "Public Funnel" : "Private tailnet";
        ModeExplanation = IsPublic
            ? "Reachable from the internet over HTTPS and protected by the pairing token, not by tailnet membership. The iPhone needs only an internet connection."
            : "Reachable only from devices signed in to your tailnet. The iPhone needs Tailscale connected.";
        HttpsPortText = (IsPublic ? settings.PublicHttpsPort : settings.PrivateHttpsPort).ToString(CultureInfo.InvariantCulture);
        PathText = IsPublic ? settings.PublicPathPrefix + "/" : "/";
        RelayPortText = settings.RelayPort.ToString(CultureInfo.InvariantCulture);
        SettingsFileProblem = loaded.Source == SettingsSource.DefaultsBecauseFileIsInvalid
            ? "settings.json is not valid and was left untouched. LiveCue is running on the default settings."
            : null;
    }

    public override string Title => "Settings";

    public bool IsPublic { get; }

    public string ModeText { get; }

    public string ModeExplanation { get; }

    public string HttpsPortText { get; }

    public string PathText { get; }

    public string RelayPortText { get; }

    /// <summary>Set when settings.json exists and could not be used; null otherwise.</summary>
    public string? SettingsFileProblem { get; }

    public string VersionText { get; } = ProtocolConstants.RelayVersion;

    public string ProtocolText { get; } = ProtocolConstants.ProtocolVersion.ToString(CultureInfo.InvariantCulture);

    public string LicenceText { get; } = "MIT";
}
