using System.Text;
using System.Text.Json;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Speech;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-103, the allowlist of protocol 10.3: what the lab writes never reaches the phone as it
/// was written. The four lab-derived frames are rebuilt member by member.
/// </summary>
public sealed class LabEventFilterTests
{
    [Theory]
    [InlineData(
        """{"type":"transcript","transcript":"Local words","private":"hidden"}""",
        """{"type":"transcript","transcript":"Local words"}""")]
    [InlineData(
        """{"private":{"deep":["hidden"]},"transcript":"Words","audioProcessedMs":320,"turnId":2,"type":"transcript","message":"hidden"}""",
        """{"type":"transcript","turnId":2,"audioProcessedMs":320,"transcript":"Words"}""")]
    [InlineData(
        """{"type":"speechStart","turnId":1,"audioProcessedMs":0,"transcript":"hidden","finalizationMs":5}""",
        """{"type":"speechStart","turnId":1,"audioProcessedMs":0}""")]
    [InlineData(
        """{"type":"speechComplete","turnId":3,"transcript":"Final words","audioProcessedMs":30000,"finalizationMs":7,"model":"hidden"}""",
        """{"type":"speechComplete","turnId":3,"audioProcessedMs":30000,"transcript":"Final words"}""")]
    [InlineData(
        """{"type":"streamComplete","audioProcessedMs":80,"finalizationMs":12.5,"turnId":9,"transcript":"hidden"}""",
        """{"type":"streamComplete","audioProcessedMs":80,"finalizationMs":12.5}""")]
    [InlineData("""{"type":"streamComplete"}""", """{"type":"streamComplete"}""")]
    [InlineData(
        """{"type":"transcript","transcript":"first","transcript":"last"}""",
        """{"type":"transcript","transcript":"last"}""")]
    [InlineData(
        """{"type":"transcript","transcript":"Zoë said “hi” · 你好 😀 <b>&amp;</b>"}""",
        """{"type":"transcript","transcript":"Zoë said “hi” · 你好 😀 <b>&amp;</b>"}""")]
    public void Unknown_members_and_types_are_dropped(string fromLab, string toPhone)
    {
        Assert.Equal(toPhone, Rebuild(fromLab));
        Assert.DoesNotContain("hidden", Rebuild(fromLab), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"type":"audioProgress","audioProcessedMs":80}""")]
    [InlineData("""{"type":"somethingNew","transcript":"hidden"}""")]
    [InlineData("""{"type":"Transcript","transcript":"hidden"}""")]
    [InlineData("""{"type":"transcript ","transcript":"hidden"}""")]
    [InlineData("""{"transcript":"hidden"}""")]
    [InlineData("""{"type":7,"transcript":"hidden"}""")]
    [InlineData("""{"type":null}""")]
    [InlineData("""{"type":["transcript"]}""")]
    [InlineData("{}")]
    public void A_message_of_another_type_is_not_a_frame(string fromLab)
    {
        Assert.True(WireJson.TryParseObject(Encoding.UTF8.GetBytes(fromLab), out var message));

        Assert.Equal(LabEventKind.Other, LabEventFilter.Classify(message));
    }

    [Theory]
    [InlineData("""{"type":"ready","model":"nemotron","sampleRate":16000}""", "nemotron", true)]
    [InlineData("""{"type":"ready","model":"qwen3","sampleRate":16000,"extra":1}""", "qwen3", true)]
    [InlineData("""{"type":"ready","model":"qwen3","sampleRate":16000}""", "nemotron", false)]
    [InlineData("""{"type":"ready","model":"nemotron","sampleRate":8000}""", "nemotron", false)]
    [InlineData("""{"type":"ready","model":"nemotron","sampleRate":"16000"}""", "nemotron", false)]
    [InlineData("""{"type":"ready","model":"nemotron"}""", "nemotron", false)]
    [InlineData("""{"type":"ready","sampleRate":16000}""", "nemotron", false)]
    [InlineData("""{"type":"ready","model":["nemotron"],"sampleRate":16000}""", "nemotron", false)]
    [InlineData("""{"type":"error","message":"Model is busy."}""", "nemotron", false)]
    [InlineData("""{"type":"speechStart","model":"nemotron","sampleRate":16000}""", "nemotron", false)]
    public void Only_ready_for_the_requested_model_at_16000_is_the_first_message(string fromLab, string model, bool accepted)
    {
        Assert.True(WireJson.TryParseObject(Encoding.UTF8.GetBytes(fromLab), out var message));

        Assert.Equal(accepted, LabEventFilter.IsReadyFor(message, model));
    }

    [Theory]
    [InlineData("""{"type":"transcript","turnId":1e999,"transcript":"x"}""", """{"type":"transcript","transcript":"x"}""")]
    [InlineData("""{"type":"transcript","turnId":-1e999,"transcript":"x"}""", """{"type":"transcript","transcript":"x"}""")]
    [InlineData("""{"type":"transcript","turnId":"1","transcript":"x"}""", """{"type":"transcript","transcript":"x"}""")]
    [InlineData("""{"type":"transcript","turnId":null,"audioProcessedMs":true}""", """{"type":"transcript"}""")]
    [InlineData("""{"type":"speechStart","turnId":{"n":1},"audioProcessedMs":[80]}""", """{"type":"speechStart"}""")]
    [InlineData("""{"type":"streamComplete","audioProcessedMs":"80","finalizationMs":1e400}""", """{"type":"streamComplete"}""")]
    [InlineData("""{"type":"transcript","transcript":7}""", """{"type":"transcript"}""")]
    [InlineData("""{"type":"transcript","transcript":null}""", """{"type":"transcript"}""")]
    [InlineData("""{"type":"transcript","transcript":["hidden"]}""", """{"type":"transcript"}""")]
    public void A_non_finite_number_is_omitted(string fromLab, string toPhone)
    {
        Assert.Equal(toPhone, Rebuild(fromLab));
    }

    [Theory]
    [InlineData("""{"type":"speechStart","turnId":1,"audioProcessedMs":0}""")]
    [InlineData("""{"type":"speechStart","turnId":12.50,"audioProcessedMs":1E2}""")]
    [InlineData("""{"type":"speechStart","turnId":-0.000,"audioProcessedMs":1234567890123456789}""")]
    [InlineData("""{"type":"streamComplete","audioProcessedMs":1.5e3,"finalizationMs":0.25}""")]
    public void A_finite_number_keeps_its_original_token(string fromLab)
    {
        Assert.Equal(fromLab, Rebuild(fromLab));
    }

    [Fact]
    public void A_transcript_that_names_half_a_surrogate_pair_is_left_out()
    {
        // The escape is valid JSON, but the text it names is not: there is no string to forward.
        var rebuilt = Rebuild("""{"type":"transcript","turnId":1,"transcript":"broken \ud83d text"}""");

        Assert.Equal("""{"type":"transcript","turnId":1}""", rebuilt);
    }

    [Fact]
    public void A_long_transcript_is_cut_without_splitting_a_pair()
    {
        var limit = ProtocolConstants.MaxTranscriptUnits;
        const string Pair = "😀";

        // One unit too long: cut to exactly C-22 units.
        AssertCut(new string('a', limit + 1), new string('a', limit));

        // Exactly C-22 units is not cut, whether or not it ends in a pair.
        AssertCut(new string('a', limit), new string('a', limit));
        AssertCut(new string('a', limit - 2) + Pair, new string('a', limit - 2) + Pair);

        // The pair straddles the limit: the high half would be the last unit kept, so it goes too.
        AssertCut(new string('a', limit - 1) + Pair + "tail", new string('a', limit - 1));

        // The pair ends exactly at the limit: it stays whole.
        AssertCut(new string('a', limit - 2) + Pair + "tail", new string('a', limit - 2) + Pair);

        // A text of pairs only: 16,000 whole pairs.
        var pairs = string.Concat(Enumerable.Repeat(Pair, limit));
        AssertCut(pairs, pairs[..limit]);
    }

    [Theory]
    [InlineData("\u0001")]
    [InlineData("\u0001\u0001\u0001😀")]
    [InlineData("\u007f😀")]
    public void A_heavily_escaped_transcript_is_cut_until_the_frame_fits_C21(string unit)
    {
        // Text made of characters JSON must escape: every control character becomes six bytes, so the
        // C-22 cut alone would leave a frame far beyond C-21.
        var limit = ProtocolConstants.MaxTranscriptUnits;
        var text = string.Concat(Enumerable.Repeat(unit, (limit / unit.Length) + 1));
        var message = Lab("speechComplete", text, turnId: 4);
        Assert.True(Frame(LabEventFilter.Cut(text, limit), turnId: 4).Length > ProtocolConstants.MaxRelayFrameBytes);

        var frame = LabEventFilter.Rebuild(LabEventKind.SpeechComplete, message, out var transcript);

        Assert.NotNull(frame);
        Assert.NotNull(transcript);
        Assert.True(frame.Length <= ProtocolConstants.MaxRelayFrameBytes);
        Assert.Equal(Frame(transcript, turnId: 4), frame);

        // What is kept is a prefix that does not end on half a pair, and it is the longest one that fits.
        Assert.StartsWith(transcript, text, StringComparison.Ordinal);
        Assert.False(char.IsHighSurrogate(transcript[^1]));
        var longer = transcript.Length + 1;
        if (char.IsHighSurrogate(text[longer - 1]))
        {
            longer++;
        }

        Assert.True(Frame(text[..longer], turnId: 4).Length > ProtocolConstants.MaxRelayFrameBytes);
        using var parsed = JsonDocument.Parse(frame);
        Assert.Equal(transcript, parsed.RootElement.GetProperty("transcript").GetString());
    }

    [Fact]
    public void Every_rebuilt_frame_fits_C21_for_any_lab_message_within_C23()
    {
        // The largest lab message there can be (C-23), filled with text as a lab could write it: plain,
        // a control character left raw, three-byte and four-byte characters, and escaped quotes.
        const string Head = """{"type":"transcript","turnId":1,"audioProcessedMs":1234567,"transcript":"hidden-not","private":"hidden","transcript":" """;
        const string Tail = "\"}";
        foreach (var filler in new[] { "a", "\u007f", "\u0085", "你", "😀", "\\\"", "\\u0001" })
        {
            var room = ProtocolConstants.MaxLabMessageBytes - Encoding.UTF8.GetByteCount(Head) - Tail.Length;
            var fromLab = Encoding.UTF8.GetBytes(
                Head + string.Concat(Enumerable.Repeat(filler, room / Encoding.UTF8.GetByteCount(filler))) + Tail);
            Assert.InRange(fromLab.Length, ProtocolConstants.MaxLabMessageBytes - 8, ProtocolConstants.MaxLabMessageBytes);
            Assert.True(WireJson.TryParseObject(fromLab, out var message));

            var frame = LabEventFilter.Rebuild(LabEventKind.Transcript, message, out var transcript);

            Assert.NotNull(frame);
            Assert.True(frame.Length <= ProtocolConstants.MaxRelayFrameBytes);
            Assert.True(transcript!.Length <= ProtocolConstants.MaxTranscriptUnits);
            Assert.DoesNotContain("hidden", Encoding.UTF8.GetString(frame), StringComparison.Ordinal);
        }
    }

    private static void AssertCut(string fromLab, string toPhone)
    {
        var frame = LabEventFilter.Rebuild(LabEventKind.Transcript, Lab("transcript", fromLab, turnId: 1), out var transcript);

        Assert.Equal(toPhone, transcript);
        using var parsed = JsonDocument.Parse(frame);
        Assert.Equal(toPhone, parsed.RootElement.GetProperty("transcript").GetString());
    }

    /// <summary>A lab message as the lab would write it (its own JSON writer, not the relay's).</summary>
    private static JsonElement Lab(string type, string transcript, int turnId)
    {
        var json = JsonSerializer.Serialize(new { type, turnId, transcript, @private = "hidden" });
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>The frame the relay must produce for a speechComplete with that text.</summary>
    private static byte[] Frame(string transcript, int turnId) => WireJson.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", "speechComplete");
        writer.WriteNumber("turnId", turnId);
        writer.WriteString("transcript", transcript);
        writer.WriteEndObject();
    });

    private static string Rebuild(string fromLab)
    {
        Assert.True(WireJson.TryParseObject(Encoding.UTF8.GetBytes(fromLab), out var message));
        var kind = LabEventFilter.Classify(message);
        Assert.NotEqual(LabEventKind.Other, kind);
        var frame = LabEventFilter.Rebuild(kind, message, out var transcript);
        Assert.NotNull(frame);
        var text = Encoding.UTF8.GetString(frame);
        if (transcript is not null)
        {
            using var parsed = JsonDocument.Parse(text);
            Assert.Equal(transcript, parsed.RootElement.GetProperty("transcript").GetString());
        }

        return text;
    }
}
