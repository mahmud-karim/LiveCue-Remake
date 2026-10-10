using System.Collections.ObjectModel;
using System.Globalization;

namespace LiveCue.Desktop.Activity;

/// <summary>
/// What happened since the relay started, oldest first, in memory only (architecture 3.10): it is
/// cleared when the window closes and is never written to disk. Touch it from the UI thread only.
///
/// First version (foundation): bounded by entry count, as in the spike. The rule of architecture 3.12
/// (the character bounds and the trim marker) is work package WP-W09.
/// </summary>
internal sealed class ActivityLog(TimeProvider clock)
{
    public const int Capacity = 500;

    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    public void Add(string message) => Add(clock.GetUtcNow(), message);

    public void Add(DateTimeOffset at, string message)
    {
        // Local wall-clock time, always eight characters, so a column of them lines up.
        var local = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
        Entries.Add(new ActivityEntry(at, local.ToString("HH:mm:ss", CultureInfo.InvariantCulture), message, Payload: null));
        while (Entries.Count > Capacity)
        {
            Entries.RemoveAt(0);
        }
    }

    public void Clear() => Entries.Clear();
}
