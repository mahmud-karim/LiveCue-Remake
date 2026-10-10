using System.Collections.ObjectModel;
using System.Globalization;

namespace LiveCue.Desktop.ViewModels;

/// <summary>One line of the activity log: when (as the wall clock showed it) and what.</summary>
public sealed record ActivityEntry(DateTimeOffset At, string TimeText, string Message);

/// <summary>
/// What the relay did since it started, oldest first, in memory only (architecture 3.10): it is
/// cleared when the window closes and is never written to disk. It is bounded, so a relay left
/// running for weeks cannot grow it without limit. Touch it from the UI thread only.
///
/// The spike bounds it by entry count. The final rule (200,000 characters cut to the newest 150,000
/// under the line "[Older activity removed from memory]") arrives with request and reply payloads.
/// </summary>
public sealed class ActivityLog(TimeProvider clock)
{
    public const int Capacity = 500;

    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    public void Add(string message) => Add(clock.GetUtcNow(), message);

    public void Add(DateTimeOffset at, string message)
    {
        // Local wall-clock time, always eight characters, so a column of them lines up.
        var local = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
        Entries.Add(new ActivityEntry(at, local.ToString("HH:mm:ss", CultureInfo.InvariantCulture), message));
        while (Entries.Count > Capacity)
        {
            Entries.RemoveAt(0);
        }
    }

    public void Clear() => Entries.Clear();
}
