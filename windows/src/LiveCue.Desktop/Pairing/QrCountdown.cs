using LiveCue.Desktop.Text;

namespace LiveCue.Desktop.Pairing;

/// <summary>
/// The countdown of a shown QR (W-design 5.6; architecture 3.12): the time left before it hides
/// itself, as the chip under the tile shows it. A QR is shown for <see cref="Duration"/>; the chip
/// warns in the last <see cref="WarningWindow"/>; at zero the QR hides.
///
/// A value, not a timer: the view model asks for one at each tick of its clock.
/// </summary>
internal readonly record struct QrCountdown
{
    /// <summary>How long a QR stays on screen (screens 3.2: 120 seconds).</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(120);

    /// <summary>The chip becomes a warning chip in the last 30 seconds.</summary>
    public static readonly TimeSpan WarningWindow = TimeSpan.FromSeconds(30);

    private QrCountdown(TimeSpan remaining) => Remaining = remaining;

    /// <summary>Whole seconds left, from <see cref="Duration"/> down to zero.</summary>
    public TimeSpan Remaining { get; }

    /// <summary>True at zero: the QR hides.</summary>
    public bool IsOver => Remaining <= TimeSpan.Zero;

    /// <summary>True in the last 30 seconds, while the QR is still shown.</summary>
    public bool IsWarning => !IsOver && Remaining <= WarningWindow;

    /// <summary>"Hides in M:SS".</summary>
    public string Text => PairingText.HidesIn(Remaining);

    /// <summary>"shown, hides in 1 minute 42 seconds".</summary>
    public string AutomationStatus => PairingText.QrShown(Remaining);

    /// <summary>When a QR shown at <paramref name="shownAt"/> hides.</summary>
    public static DateTimeOffset HideTime(DateTimeOffset shownAt) => shownAt + Duration;

    /// <summary>
    /// The countdown at <paramref name="now"/> of a QR that hides at <paramref name="hideAt"/>. A part
    /// of a second counts as a whole one, so the chip reads 2:00 when the QR appears and reaches 0:00
    /// exactly when it hides. A clock that was set back never shows more than the full duration.
    /// </summary>
    public static QrCountdown At(DateTimeOffset hideAt, DateTimeOffset now)
    {
        var left = hideAt - now;
        if (left <= TimeSpan.Zero)
        {
            return new QrCountdown(TimeSpan.Zero);
        }

        var seconds = Math.Min(Math.Ceiling(left.TotalSeconds), Duration.TotalSeconds);
        return new QrCountdown(TimeSpan.FromSeconds(seconds));
    }
}
