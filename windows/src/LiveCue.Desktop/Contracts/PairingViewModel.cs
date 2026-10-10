using System.ComponentModel;
using System.Windows.Input;

namespace LiveCue.Desktop;

/// <summary>The QR machine of architecture 3.12.</summary>
public enum QrState { Empty, Shown, Hidden }

public enum QrHiddenReason { None, Button, Timer, RelayEnded }

/// <summary>What the tile draws.</summary>
public enum QrTileFace
{
    /// <summary>Placeholder, "Start the relay to show the QR".</summary>
    RelayNotRunning,

    /// <summary>Placeholder, "Set up the connection first." (R-14).</summary>
    ConnectionNotSetUp,

    /// <summary>Placeholder, "Show the QR to pair an iPhone".</summary>
    Placeholder,

    /// <summary>Spinner while ShowPairing or ResetPairing runs.</summary>
    Generating,

    /// <summary>The code.</summary>
    Code,
}

/// <summary>The Pairing page (screens section 3).</summary>
public interface IPairingViewModel : INotifyPropertyChanged
{
    QrState QrState { get; }

    QrHiddenReason HiddenReason { get; }

    QrTileFace TileFace { get; }

    /// <summary>The matrix while Shown; null otherwise. Cleared, not just hidden, when the QR hides.</summary>
    bool[][]? QrModules { get; }

    /// <summary>The second placeholder line for the current TileFace.</summary>
    string PlaceholderLine { get; }

    /// <summary>"Hides in M:SS" while Shown; empty otherwise.</summary>
    string CountdownText { get; }

    /// <summary>True in the last 30 seconds.</summary>
    bool IsCountdownWarning { get; }

    /// <summary>The state part of the automation name: "shown, hides in 1 minute 42 seconds" or "hidden".</summary>
    string QrAutomationStatus { get; }

    /// <summary>PairingHint.</summary>
    string Hint { get; }

    bool CanShowQr { get; }

    /// <summary>"Start the relay first." or "Set up the connection first."; null when CanShowQr.</summary>
    string? ShowQrCaption { get; }

    bool CanHideQr { get; }

    /// <summary>PairingAddressField: the advertised endpoint, or empty.</summary>
    string PcAddress { get; }

    /// <summary>PairingVerifyNotice: the result of the last self-check; null until one completes and after
    /// the QR hides.</summary>
    BannerModel? VerifyNotice { get; }

    bool CanResetPairing { get; }

    ICommand ShowQrCommand { get; }

    ICommand HideQrCommand { get; }

    ICommand CopyAddressCommand { get; }

    /// <summary>Opens the reset confirmation.</summary>
    ICommand ResetPairingCommand { get; }

    ICommand OpenSettingsCommand { get; }
}
