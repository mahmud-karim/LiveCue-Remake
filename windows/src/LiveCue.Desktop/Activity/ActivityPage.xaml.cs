using System.Windows.Controls;

namespace LiveCue.Desktop.Activity;

/// <summary>
/// The Activity page. Its data context is an <see cref="IActivityViewModel"/>.
///
/// The view keeps one piece of state of its own: whether the log is being followed. It is followed
/// while it is scrolled to its end, so a new entry comes into view; once the owner scrolls up to read
/// something, new entries no longer move the text under their eyes (screens 4.2).
/// </summary>
public partial class ActivityPage : UserControl
{
    /// <summary>How close to the end still counts as "at the end": less than a line.</summary>
    private const double EndTolerance = 2;

    private bool _follows = true;

    public ActivityPage()
    {
        InitializeComponent();
    }

    private void OnLogScrolled(object sender, ScrollChangedEventArgs change)
    {
        if (change.OriginalSource is not ScrollViewer log)
        {
            return;
        }

        var atEnd = log.VerticalOffset >= log.ScrollableHeight - EndTolerance;
        if (change.VerticalChange < 0)
        {
            // The text moved towards its start: the owner scrolled up to read, and following stops.
            // (Unless it only moved because the log got shorter and it is still at its end.)
            _follows = atEnd;
        }
        else if (change.VerticalChange > 0 && atEnd)
        {
            // Back at the end: followed again.
            _follows = true;
        }

        if (_follows && !atEnd)
        {
            // The log grew, or the window changed size, under a log that is being followed.
            log.ScrollToEnd();
        }
    }
}
