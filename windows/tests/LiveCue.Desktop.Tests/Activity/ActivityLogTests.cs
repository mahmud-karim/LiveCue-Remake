using LiveCue.Desktop.Activity;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.Activity;

/// <summary>
/// The bounded in-memory log (test plan 2.7; architecture 3.12; W-design 5.7; AC-WIN-030): above
/// 200,000 characters it is cut to the newest 150,000 at most, and then starts with the marker.
/// </summary>
public sealed class ActivityLogTests
{
    private const string Marker = "[Older activity removed from memory]";

    [Fact]
    public void Bounds()
    {
        Assert.Equal(200_000, ActivityLog.UpperBound);
        Assert.Equal(150_000, ActivityLog.LowerBound);

        var log = new ActivityLog(new ManualTimeProvider());
        var trims = 0;
        var largest = 0;

        // 400 entries of 1,000 characters each (the self-test's step 18): twice what the log may hold.
        for (var index = 0; index < 400; index++)
        {
            var before = log.Entries.Count;
            log.Add(index.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) + new string('m', 996));

            // The count the log keeps is the length of its text, and it never passes the upper bound.
            Assert.Equal(log.ToText().Length + Environment.NewLine.Length, log.CharacterCount);
            Assert.True(log.CharacterCount <= ActivityLog.UpperBound);
            largest = Math.Max(largest, log.CharacterCount);

            if (log.Entries.Count <= before)
            {
                // It was just cut: at most the lower bound is left, and entry 0 is the marker.
                trims++;
                Assert.True(log.CharacterCount <= ActivityLog.LowerBound);
                Assert.True(log.CharacterCount > ActivityLog.LowerBound - 1100);
                Assert.True(log.IsTrimmed);
            }

            if (trims > 0)
            {
                Assert.Equal(Marker, log.Entries[0].Message);
                Assert.True(log.Entries[0].IsTrimMarker);
                Assert.Equal(string.Empty, log.Entries[0].TimeText);
                Assert.Single(log.Entries, entry => entry.IsTrimMarker);
            }
            else
            {
                Assert.False(log.IsTrimmed);
                Assert.Equal(index + 1, log.Entries.Count);
            }
        }

        Assert.True(trims >= 2);
        Assert.True(largest > ActivityLog.LowerBound);

        // The newest entries are the ones that stayed, whole and in order.
        Assert.StartsWith("0399", log.Entries[^1].Message, StringComparison.Ordinal);
        var kept = log.Entries.Skip(1).Select(entry => int.Parse(entry.Message[..4], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(Enumerable.Range(400 - kept.Count, kept.Count), kept);
        Assert.Equal(kept.Count, log.EntryCount);
        Assert.StartsWith(Marker + Environment.NewLine + "09:00:00 ", log.ToText(), StringComparison.Ordinal);

        log.Clear();
        Assert.Empty(log.Entries);
        Assert.Equal(0, log.CharacterCount);
        Assert.False(log.IsTrimmed);
        Assert.Equal(string.Empty, log.ToText());
    }

    [Fact]
    public void One_entry_larger_than_the_log_keeps_the_newest_part_of_its_payload()
    {
        var log = new ActivityLog(new ManualTimeProvider());
        log.Add("Relay started. Waiting for your iPhone.");

        var payload = new string('p', ActivityLog.UpperBound) + "\"end\"}";
        log.Add(ManualTimeProvider.DefaultStart, "REPLY [7f3a9c12] 0.91 s", payload);

        Assert.Equal(2, log.Entries.Count);
        Assert.Equal(Marker, log.Entries[0].Message);
        Assert.Equal("REPLY [7f3a9c12] 0.91 s", log.Entries[1].Message);
        Assert.EndsWith("p\"end\"}", log.Entries[1].Payload, StringComparison.Ordinal);
        Assert.Equal(ActivityLog.LowerBound, log.CharacterCount);
        Assert.Equal(log.ToText().Length + Environment.NewLine.Length, log.CharacterCount);
        Assert.Equal(1, log.EntryCount);

        // The log goes on from there.
        log.Add("Relay diagnostic: next");
        Assert.Equal(3, log.Entries.Count);
        Assert.True(log.CharacterCount <= ActivityLog.UpperBound);
    }

    [Fact]
    public void An_entry_has_its_time_in_the_local_zone_and_its_length_as_text()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(2), "test", "test");
        var log = new ActivityLog(new ManualTimeProvider(new DateTimeOffset(2026, 10, 10, 21, 59, 58, TimeSpan.Zero), zone));

        log.Add("first");
        log.Add(new DateTimeOffset(2026, 10, 10, 7, 5, 9, TimeSpan.Zero), "INCOMING assist [id]", "{}");

        Assert.Equal(["23:59:58", "09:05:09"], log.Entries.Select(entry => entry.TimeText));
        Assert.Equal("{}", log.Entries[1].Payload);
        Assert.Null(log.Entries[0].Payload);
        Assert.Equal(
            "23:59:58 first" + Environment.NewLine + "09:05:09 INCOMING assist [id]" + Environment.NewLine + "{}",
            log.ToText());

        // Time, a space, the message, a line break and the payload, and one line break.
        var lineBreak = Environment.NewLine.Length;
        Assert.Equal(8 + 1 + 5 + lineBreak, ActivityLog.Measure(log.Entries[0]));
        Assert.Equal(8 + 1 + 20 + lineBreak + 2 + lineBreak, ActivityLog.Measure(log.Entries[1]));
        Assert.Equal(ActivityLog.Measure(log.Entries[0]) + ActivityLog.Measure(log.Entries[1]), log.CharacterCount);
    }
}
