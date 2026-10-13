using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using LiveCue.Desktop.Text;

namespace LiveCue.Desktop.Activity;

/// <summary>
/// What happened since the relay started, oldest first, in memory only (architecture 3.10): it is
/// cleared when the window closes and is never written to disk. Touch it from the UI thread only.
///
/// The log is bounded by its size as text (architecture 3.12, W-design 5.7). When it passes
/// <see cref="UpperBound"/> characters, whole entries are removed from the front until it holds at most
/// <see cref="LowerBound"/>; an entry that is larger than that by itself keeps the newest part of its
/// payload; and entry 0 becomes the line "[Older activity removed from memory]". So the log never
/// exceeds the upper bound and, once trimmed, starts with that marker (AC-WIN-030).
/// </summary>
internal sealed class ActivityLog(TimeProvider clock)
{
    /// <summary>Above this many characters the log is cut.</summary>
    public const int UpperBound = 200_000;

    /// <summary>What a cut leaves at most, the marker included.</summary>
    public const int LowerBound = 150_000;

    private static readonly int LineBreak = Environment.NewLine.Length;

    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    /// <summary>The length of the log as text: every entry's line, its payload, and a line break each.</summary>
    public int CharacterCount { get; private set; }

    /// <summary>True once older entries were removed: entry 0 is then the marker.</summary>
    public bool IsTrimmed => Entries.Count > 0 && Entries[0].IsTrimMarker;

    /// <summary>How many entries there are, not counting the marker.</summary>
    public int EntryCount => IsTrimmed ? Entries.Count - 1 : Entries.Count;

    /// <summary>The length of one entry as text, with the line break that ends it.</summary>
    public static int Measure(ActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var line = entry.IsTrimMarker ? entry.Message.Length : entry.TimeText.Length + 1 + entry.Message.Length;
        return line + (entry.Payload is null ? 0 : LineBreak + entry.Payload.Length) + LineBreak;
    }

    public void Add(string message) => Add(clock.GetUtcNow(), message);

    public void Add(DateTimeOffset at, string message, string? payload = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Local wall-clock time, always eight characters, so a column of them lines up.
        var local = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
        var entry = new ActivityEntry(at, local.ToString("HH:mm:ss", CultureInfo.InvariantCulture), message, payload);

        Entries.Add(entry);
        CharacterCount += Measure(entry);
        if (CharacterCount > UpperBound)
        {
            Trim();
        }
    }

    public void Clear()
    {
        Entries.Clear();
        CharacterCount = 0;
    }

    /// <summary>The whole log as text, one entry after the other, as it would be copied.</summary>
    public string ToText()
    {
        var text = new StringBuilder(CharacterCount);
        foreach (var entry in Entries)
        {
            if (text.Length > 0)
            {
                text.Append(Environment.NewLine);
            }

            if (!entry.IsTrimMarker)
            {
                text.Append(entry.TimeText).Append(' ');
            }

            text.Append(entry.Message);
            if (entry.Payload is not null)
            {
                text.Append(Environment.NewLine).Append(entry.Payload);
            }
        }

        return text.ToString();
    }

    private void Trim()
    {
        if (IsTrimmed)
        {
            RemoveFirst();
        }

        var marker = new ActivityEntry(default, string.Empty, ActivityText.TrimMarker, Payload: null, IsTrimMarker: true);
        var room = LowerBound - Measure(marker);

        while (CharacterCount > room && Entries.Count > 1)
        {
            RemoveFirst();
        }

        // One entry that is larger than the whole allowance: its newest part stays.
        if (CharacterCount > room && Entries.Count == 1)
        {
            var cut = Shorten(Entries[0], CharacterCount - room);
            Entries[0] = cut;
            CharacterCount = Measure(cut);
        }

        Entries.Insert(0, marker);
        CharacterCount += Measure(marker);
    }

    private void RemoveFirst()
    {
        CharacterCount -= Measure(Entries[0]);
        Entries.RemoveAt(0);
    }

    /// <summary>Takes <paramref name="excess"/> characters off the entry: off the front of its payload, else off the end of its message.</summary>
    private static ActivityEntry Shorten(ActivityEntry entry, int excess)
    {
        if (entry.Payload is { } payload)
        {
            var kept = Math.Max(0, payload.Length - excess);
            excess -= payload.Length - kept;
            entry = entry with { Payload = Newest(payload, kept) };
        }

        return excess > 0
            ? entry with { Message = entry.Message[..Math.Max(0, entry.Message.Length - excess)] }
            : entry;
    }

    /// <summary>The last <paramref name="length"/> characters at most, starting at a character boundary.</summary>
    internal static string Newest(string text, int length)
    {
        if (length >= text.Length)
        {
            return text;
        }

        if (length <= 0)
        {
            return string.Empty;
        }

        var start = text.Length - length;

        // Never begin in the middle of a surrogate pair: half a character is not text.
        if (char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1]))
        {
            start++;
        }

        return text[start..];
    }
}
