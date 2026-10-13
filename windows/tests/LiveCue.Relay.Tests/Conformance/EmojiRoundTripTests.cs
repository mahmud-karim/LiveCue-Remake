using System.Text;
using System.Text.Json;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.Speech;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>
/// The open point of W-design 4.11, examined and ruled on. The relay's frozen JSON writer leaves the basic
/// plane as UTF-8 but writes a character outside it (an emoji, a rare ideograph) as an escaped surrogate
/// pair: twelve bytes, <c>😀</c>, where the phone sent four. Protocol section 1 says "non-ASCII
/// text as UTF-8, not as \u escapes".
///
/// Ruling, proved below: it is a deviation in form only.
/// <list type="bullet">
/// <item>Every JSON reader decodes the pair to the same text, so transcripts, answers and speech frames
/// with emoji arrive unchanged, end to end, in both directions.</item>
/// <item>No size limit is passed. C-10 bounds the request on the wire, before the relay rewrites it; the
/// rewritten body (the prompt block and the desktop's event) can be up to three times as long and has no
/// limit of its own. A speech frame is cut until it fits C-21, on a pair boundary, so a transcript of
/// emoji alone is shortened to about 10,900 emoji where C-22 would allow 16,000. An answer is bounded by
/// C-24 on what Codex wrote; the reply can be up to three times that on the wire.</item>
/// <item>What remains for the orchestrator is the wording of protocol section 1, or the writer.</item>
/// </list>
/// </summary>
public sealed class EmojiRoundTripTests
{
    /// <summary>Grinning face, a family joined with zero-width joiners, a flag, a skin tone, a rare ideograph, and plain text around them.</summary>
    private const string Mixed = "Déjà vu 😀 👨‍👩‍👧‍👦 🇧🇩 👍🏽 𠀋 naïve ☃ end";

    [Theory]
    [InlineData("/v1/assist")]
    [InlineData("/v1/session-summary")]
    public async Task A_transcript_with_emoji_reaches_codex_and_the_desktop_unchanged(string target)
    {
        await using var relay = await ConformanceRelay.StartAsync();
        // Written as the phone writes it: the characters themselves, as UTF-8.
        var body =
            $$"""{"requestId":"request-0001","assistant":{"model":"{{ConformanceRelay.Model}}","reasoningEffort":"{{ConformanceRelay.Effort}}"},"transcript":{{Raw(Mixed)}},"partialTranscript":"😀","instruction":"Answer with 🎉"}""";

        var (status, _) = await relay.ExchangeAsync(HttpMethod.Post, target, body);
        Assert.Equal(200, status);

        // The real process received the request as one line in which every character outside the basic
        // plane is an escaped pair, and which decodes to exactly what the phone sent.
        var block = (await relay.Codex.ReadExecReportAsync()).Prompt.Split('\n')[3];
        Assert.DoesNotContain(block, char.IsSurrogate);
        Assert.Contains("\\uD83D\\uDE00", block, StringComparison.Ordinal);
        Assert.Contains("Déjà vu", block, StringComparison.Ordinal);
        Assert.Contains("☃", block, StringComparison.Ordinal);
        using (var embedded = JsonDocument.Parse(block))
        {
            Assert.Equal(Mixed, embedded.RootElement.GetProperty("transcript").GetString());
            Assert.Equal("😀", embedded.RootElement.GetProperty("partialTranscript").GetString());
        }

        // The desktop's event carries the same text, as text and inside the payload.
        var started = await relay.Events.WaitForAsync<RequestStarted>();
        Assert.Equal(Mixed, started.Transcript);
        Assert.Equal("😀", started.PartialTranscript);
        Assert.Equal("Answer with 🎉", started.Instruction);
        using var payload = JsonDocument.Parse(started.PayloadJson);
        Assert.Equal(Mixed, payload.RootElement.GetProperty("transcript").GetString());
    }

    [Fact]
    public async Task A_request_of_emoji_at_the_body_limit_is_served_and_arrives_whole()
    {
        // The largest body the relay accepts, nearly all of it four-byte characters: C-10 is checked on
        // the wire, the rewritten body is three times as long, and nothing limits it or cuts it.
        await using var relay = await ConformanceRelay.StartAsync();
        var head =
            $$"""{"requestId":"request-0001","assistant":{"model":"{{ConformanceRelay.Model}}","reasoningEffort":"{{ConformanceRelay.Effort}}"},"transcript":""" + "\"";
        const string Tail = "\"}";
        var count = (ProtocolConstants.MaxRequestBodyBytes - Encoding.UTF8.GetByteCount(head) - Tail.Length) / 4;
        var padding = ProtocolConstants.MaxRequestBodyBytes - Encoding.UTF8.GetByteCount(head) - Tail.Length - (4 * count);
        var transcript = new string('x', padding) + string.Concat(Enumerable.Repeat("😀", count));
        var body = head + transcript + Tail;
        Assert.Equal(ProtocolConstants.MaxRequestBodyBytes, Encoding.UTF8.GetByteCount(body));

        var (status, _) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", body);
        Assert.Equal(200, status);

        var block = (await relay.Codex.ReadExecReportAsync()).Prompt.Split('\n')[3];
        Assert.InRange(Encoding.UTF8.GetByteCount(block), 2 * ProtocolConstants.MaxRequestBodyBytes, 3 * ProtocolConstants.MaxRequestBodyBytes);
        using var embedded = JsonDocument.Parse(block);
        Assert.Equal(transcript, embedded.RootElement.GetProperty("transcript").GetString());
        Assert.Equal(transcript, (await relay.Events.WaitForAsync<RequestStarted>()).Transcript);

        // One byte more is refused as any oversized body is.
        (status, var text) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", head + "x" + transcript + Tail);
        Assert.Equal(413, status);
        Assert.Equal(Wire.PayloadTooLarge, text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_answer_with_emoji_reaches_the_phone_unchanged(bool escapedByCodex)
    {
        // Codex may write the characters as UTF-8 or as escaped pairs; the reply decodes to the same text.
        await using var host = await RelayTestHost.StartAsync();
        var written = escapedByCodex ? JsonSerializer.Serialize(Mixed) : Raw(Mixed);
        Assert.Equal(escapedByCodex, written.Contains("\\uD83D", StringComparison.OrdinalIgnoreCase));
        host.Backend.AssistResult = Bodies.Utf8(
            "{\"detectedQuestion\":" + written + ",\"answer\":" + written + ",\"details\":" + written
            + ",\"memory\":{\"summary\":" + written + ",\"facts\":[" + written + "],\"openQuestions\":[" + written + "],\"throughSegmentId\":null}}");
        host.Backend.SummaryResult = Bodies.Utf8(
            "{\"title\":" + written + ",\"summary\":" + written + ",\"keyPoints\":[" + written + "],\"actionItems\":[" + written + "]}");

        using var assist = await host.PostAsync("/v1/assist", Bodies.Assist());
        var assistBytes = await assist.Content.ReadAsByteArrayAsync();
        using var summary = await host.PostAsync("/v1/session-summary", Bodies.Summary("request-0002"));
        var summaryBytes = await summary.Content.ReadAsByteArrayAsync();

        Assert.Equal(200, (int)assist.StatusCode);
        Assert.Equal(200, (int)summary.StatusCode);
        foreach (var bytes in (byte[][])[assistBytes, summaryBytes])
        {
            // Valid UTF-8, no byte of a four-byte sequence, and the pairs written as escapes.
            Assert.True(System.Text.Unicode.Utf8.IsValid(bytes));
            Assert.DoesNotContain(bytes, value => value >= 0xF0);
            Assert.Contains("\\uD83D\\uDE00", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        }

        using var assistReply = JsonDocument.Parse(assistBytes);
        var root = assistReply.RootElement;
        Assert.Equal(Mixed, root.GetProperty("detectedQuestion").GetString());
        Assert.Equal(Mixed, root.GetProperty("answer").GetString());
        Assert.Equal(Mixed, root.GetProperty("details").GetString());
        Assert.Equal(Mixed, root.GetProperty("memory").GetProperty("summary").GetString());
        Assert.Equal(Mixed, root.GetProperty("memory").GetProperty("facts")[0].GetString());
        Assert.Equal(Mixed, root.GetProperty("memory").GetProperty("openQuestions")[0].GetString());
        using var summaryReply = JsonDocument.Parse(summaryBytes);
        Assert.Equal(Mixed, summaryReply.RootElement.GetProperty("title").GetString());
        Assert.Equal(Mixed, summaryReply.RootElement.GetProperty("keyPoints")[0].GetString());
        Assert.Equal(Mixed, summaryReply.RootElement.GetProperty("actionItems")[0].GetString());
    }

    [Fact]
    public async Task The_largest_answer_of_emoji_is_three_times_what_codex_wrote_and_no_more()
    {
        // C-24 bounds what Codex wrote. An answer that is all four-byte characters is the worst case for
        // the reply on the wire: three times the size, plus the members around it.
        await using var host = await RelayTestHost.StartAsync();
        var count = (ProtocolConstants.MaxCodexOutputBytes - 400) / 4;
        var answer = string.Concat(Enumerable.Repeat("😀", count));
        host.Backend.AssistResult = Bodies.ValidAssistOutput(answer);
        Assert.InRange(host.Backend.AssistResult.Length, ProtocolConstants.MaxCodexOutputBytes - 400, ProtocolConstants.MaxCodexOutputBytes);

        using var response = await host.PostAsync("/v1/assist", Bodies.Assist());
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(200, (int)response.StatusCode);
        Assert.InRange(bytes.Length, 3 * 4 * count, (3 * ProtocolConstants.MaxCodexOutputBytes) + 1024);
        using var reply = JsonDocument.Parse(bytes);
        Assert.Equal(answer, reply.RootElement.GetProperty("answer").GetString());
    }

    [Fact]
    public async Task Speech_frames_with_emoji_reach_the_phone_unchanged_and_stay_within_C21()
    {
        await using var relay = await ConformanceRelay.StartAsync();
        relay.LabSockets.AutoRespond = false;
        using var phone = await relay.OpenReadyStreamAsync();
        var lab = relay.LabSockets.Current;

        // An ordinary transcript with emoji, written by the lab as UTF-8.
        lab.Enqueue("{\"type\":\"transcript\",\"turnId\":1,\"transcript\":" + Raw(Mixed) + "}");
        var frame = await phone.ReceiveTextAsync();
        Assert.DoesNotContain(frame!, char.IsSurrogate);
        Assert.Contains("\\uD83D\\uDE00", frame, StringComparison.Ordinal);
        Assert.Equal(Mixed, SpeechPhone.Parse(frame!).GetProperty("transcript").GetString());

        // The same text written by the lab as escaped pairs (Python's default) arrives the same.
        lab.Enqueue(JsonSerializer.Serialize(new { type = "transcript", turnId = 1, transcript = Mixed }));
        Assert.Equal(frame, await phone.ReceiveTextAsync());

        // The longest transcript C-22 allows, all of it emoji: 16,000 pairs. As escapes that is 192,000
        // bytes, beyond C-21, so the frame is cut further: it fits, it ends on a whole pair, and it is the
        // longest prefix that fits.
        var longest = string.Concat(Enumerable.Repeat("😀", ProtocolConstants.MaxTranscriptUnits / 2));
        lab.Enqueue("{\"type\":\"speechComplete\",\"turnId\":2,\"transcript\":" + Raw(longest + "😀😀") + "}");
        var cut = await phone.ReceiveTextAsync();
        var size = Encoding.UTF8.GetByteCount(cut!);
        var text = SpeechPhone.Parse(cut!).GetProperty("transcript").GetString()!;
        Assert.InRange(size, ProtocolConstants.MaxRelayFrameBytes - 11, ProtocolConstants.MaxRelayFrameBytes);
        Assert.Equal(0, text.Length % 2);
        Assert.StartsWith(text, longest, StringComparison.Ordinal);
        Assert.True(text.Length < longest.Length);
        Assert.All(text.Chunk(2), pair => Assert.True(char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1])));

        // A transcript of the basic plane at C-22 is not cut at all, and one with a pair across the limit
        // loses the whole pair.
        var plain = new string('é', ProtocolConstants.MaxTranscriptUnits - 1);
        lab.Enqueue("{\"type\":\"transcript\",\"transcript\":" + Raw(plain + "😀") + "}");
        var boundary = SpeechPhone.Parse((await phone.ReceiveTextAsync())!).GetProperty("transcript").GetString();
        Assert.Equal(plain, boundary);

        // The desktop's text event is what the frame carried.
        var spoken = await relay.Events.WaitForAsync<SpeechText>();
        Assert.Equal(text, spoken.Text);
        Assert.Empty(relay.Events.OfType<Diagnostic>());
    }

    /// <summary>
    /// A JSON string with its characters written as they are, as UTF-8. (No framework encoder writes a
    /// character outside the basic plane unescaped, which is the point of this file.) Only for text
    /// without quotes, backslashes or control characters.
    /// </summary>
    private static string Raw(string text)
    {
        Assert.DoesNotContain(text, character => character is '"' or '\\' || char.IsControl(character));
        return "\"" + text + "\"";
    }
}
