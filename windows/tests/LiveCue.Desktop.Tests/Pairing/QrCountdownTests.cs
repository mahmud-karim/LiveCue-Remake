using LiveCue.Desktop.Pairing;

namespace LiveCue.Desktop.Tests.Pairing;

/// <summary>The countdown of a shown QR (test plan 2.8; W-design 5.6; screens 3.2).</summary>
public sealed class QrCountdownTests
{
    private static readonly DateTimeOffset ShownAt = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Countdown()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), QrCountdown.Duration);
        Assert.Equal(TimeSpan.FromSeconds(30), QrCountdown.WarningWindow);
        var hideAt = QrCountdown.HideTime(ShownAt);
        Assert.Equal(ShownAt.AddMinutes(2), hideAt);

        // (seconds since shown, text, automation state, warning, over)
        (double Elapsed, string Text, string Spoken, bool Warning, bool Over)[] cases =
        [
            (0, "Hides in 2:00", "shown, hides in 2 minutes", false, false),
            (0.4, "Hides in 2:00", "shown, hides in 2 minutes", false, false),
            (1, "Hides in 1:59", "shown, hides in 1 minute 59 seconds", false, false),
            (18, "Hides in 1:42", "shown, hides in 1 minute 42 seconds", false, false),
            (59, "Hides in 1:01", "shown, hides in 1 minute 1 second", false, false),
            (60, "Hides in 1:00", "shown, hides in 1 minute", false, false),
            (61, "Hides in 0:59", "shown, hides in 59 seconds", false, false),
            (89, "Hides in 0:31", "shown, hides in 31 seconds", false, false),
            (90, "Hides in 0:30", "shown, hides in 30 seconds", true, false),
            (111, "Hides in 0:09", "shown, hides in 9 seconds", true, false),
            (119, "Hides in 0:01", "shown, hides in 1 second", true, false),
            (119.5, "Hides in 0:01", "shown, hides in 1 second", true, false),
            (120, "Hides in 0:00", "shown, hides in 0 seconds", false, true),
            (300, "Hides in 0:00", "shown, hides in 0 seconds", false, true),
        ];

        foreach (var (elapsed, text, spoken, warning, over) in cases)
        {
            var countdown = QrCountdown.At(hideAt, ShownAt.AddSeconds(elapsed));
            Assert.Equal(text, countdown.Text);
            Assert.Equal(spoken, countdown.AutomationStatus);
            Assert.Equal(warning, countdown.IsWarning);
            Assert.Equal(over, countdown.IsOver);
        }

        // A clock that was set back never shows more than the full two minutes.
        var early = QrCountdown.At(hideAt, ShownAt.AddMinutes(-10));
        Assert.Equal(QrCountdown.Duration, early.Remaining);
        Assert.Equal("Hides in 2:00", early.Text);

        // Two readings within the same second are the same value: nothing is told twice.
        Assert.Equal(QrCountdown.At(hideAt, ShownAt.AddSeconds(18.2)), QrCountdown.At(hideAt, ShownAt.AddSeconds(18.9)));
    }
}
