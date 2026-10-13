using LiveCue.Desktop.Live;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests.Live;

/// <summary>The transcript card's bound (test plan 2.7; architecture 3.12): above 64,000 characters the newest 48,000 stay.</summary>
public sealed class TranscriptModelTests
{
    [Fact]
    public void The_text_is_cut_to_its_newest_part()
    {
        Assert.Equal(64_000, TranscriptModel.UpperBound);
        Assert.Equal(48_000, TranscriptModel.LowerBound);

        var transcript = new TranscriptModel();
        var newLine = Environment.NewLine.Length;

        // Lines of 1,000 characters: the text grows to the upper bound and no further.
        var lines = 0;
        var longest = 0;
        for (var index = 0; index < 200; index++)
        {
            transcript.Append(index.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) + new string('a', 996));
            lines++;
            longest = Math.Max(longest, transcript.Text.Length);
            Assert.True(transcript.Text.Length <= TranscriptModel.UpperBound);

            if (transcript.Text.Length < (lines * 1000) + ((lines - 1) * newLine))
            {
                // It was just cut: exactly the newest part of the lower bound is left.
                Assert.Equal(TranscriptModel.LowerBound, transcript.Text.Length);
                lines = 0;
                break;
            }
        }

        Assert.Equal(0, lines);
        Assert.True(longest > TranscriptModel.LowerBound);

        // The newest line is whole, and the text goes on growing from there.
        Assert.EndsWith(new string('a', 996), transcript.Text, StringComparison.Ordinal);
        var newest = transcript.Text[^1000..];
        transcript.Append("Local words");
        Assert.EndsWith(newest + Environment.NewLine + "Local words", transcript.Text, StringComparison.Ordinal);
        Assert.Equal(TranscriptModel.LowerBound + newLine + "Local words".Length, transcript.Text.Length);

        // A text at the bound is left alone; one character more is cut.
        Assert.Equal(TranscriptModel.UpperBound, TranscriptModel.Cut(new string('b', TranscriptModel.UpperBound)).Length);
        Assert.Equal(TranscriptModel.LowerBound, TranscriptModel.Cut(new string('b', TranscriptModel.UpperBound + 1)).Length);
    }

    [Fact]
    public void A_cut_never_starts_in_the_middle_of_a_character()
    {
        // Each of these characters takes two UTF-16 units, so an odd cut would land inside one.
        const string Wave = "\U0001F30A";
        var text = "x" + string.Concat(Enumerable.Repeat(Wave, 40_000));
        Assert.True(text.Length > TranscriptModel.UpperBound);

        var cut = TranscriptModel.Cut(text);
        Assert.True(char.IsHighSurrogate(cut[0]));
        Assert.Equal(TranscriptModel.LowerBound, cut.Length);

        var odd = TranscriptModel.Cut(text + "y");
        Assert.True(char.IsHighSurrogate(odd[0]));
        Assert.Equal(TranscriptModel.LowerBound - 1, odd.Length);
        Assert.EndsWith(Wave + "y", odd, StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_replaces_the_text_and_a_very_long_transcript_is_cut_as_well()
    {
        var transcript = new TranscriptModel();
        Assert.True(transcript.IsEmpty);

        transcript.Append("Local words");
        transcript.Show(Request(new string('q', TranscriptModel.UpperBound + 10) + "end", "interim", null));
        Assert.Equal(TranscriptModel.LowerBound, transcript.Text.Length);
        Assert.EndsWith("qend", transcript.Text, StringComparison.Ordinal);
        Assert.Equal("[In progress] interim", transcript.Secondary);

        transcript.Append(null);
        transcript.Append(string.Empty);
        Assert.Equal(TranscriptModel.LowerBound, transcript.Text.Length);

        transcript.Clear();
        Assert.True(transcript.IsEmpty);
        Assert.Equal(string.Empty, transcript.Secondary);
    }

    private static RequestStarted Request(string? transcript, string? partial, string? instruction) => new(
        DateTimeOffset.UnixEpoch, "id", AnswerKind.Assist, "model", "low", transcript, partial, instruction, "{}");
}
