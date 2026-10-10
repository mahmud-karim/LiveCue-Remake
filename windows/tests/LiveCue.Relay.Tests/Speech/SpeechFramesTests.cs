using System.Text;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Speech;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// The three frames the relay builds itself (protocol 10.3), byte for byte: every member, in a fixed
/// order, <c>provider</c> as the model id and the two integers as integers.
/// </summary>
public sealed class SpeechFramesTests
{
    [Theory]
    [InlineData("nemotron", 0, """{"type":"loading","provider":"nemotron","elapsedSeconds":0,"message":"Loading Nemotron on PC · 0 s. Microphone starts when ready."}""")]
    [InlineData("qwen3", 2, """{"type":"loading","provider":"qwen3","elapsedSeconds":2,"message":"Loading Qwen3 on PC · 2 s. Microphone starts when ready."}""")]
    [InlineData("qwen3", 168, """{"type":"loading","provider":"qwen3","elapsedSeconds":168,"message":"Loading Qwen3 on PC · 168 s. Microphone starts when ready."}""")]
    public void Loading_carries_the_model_id_and_whole_seconds(string model, int seconds, string expected)
    {
        Assert.Equal(expected, Encoding.UTF8.GetString(SpeechFrames.Loading(model, seconds)));
    }

    [Theory]
    [InlineData("nemotron", 0, """{"type":"ready","provider":"nemotron","sampleRate":16000,"handshakeMs":0}""")]
    [InlineData("qwen3", 41.7, """{"type":"ready","provider":"qwen3","sampleRate":16000,"handshakeMs":41.7}""")]
    [InlineData("qwen3", 1234.56, """{"type":"ready","provider":"qwen3","sampleRate":16000,"handshakeMs":1234.6}""")]
    [InlineData("nemotron", 0.04, """{"type":"ready","provider":"nemotron","sampleRate":16000,"handshakeMs":0}""")]
    [InlineData("nemotron", 170000, """{"type":"ready","provider":"nemotron","sampleRate":16000,"handshakeMs":170000}""")]
    public void Ready_carries_the_model_id_the_integer_16000_and_one_decimal(string model, double milliseconds, string expected)
    {
        var frame = SpeechFrames.Ready(model, TimeSpan.FromMilliseconds(milliseconds));

        Assert.Equal(expected, Encoding.UTF8.GetString(frame));
    }

    [Fact]
    public void Error_is_the_prebuilt_frame_of_the_code()
    {
        Assert.Equal(
            """{"type":"error","code":"AUDIO_LIMIT","message":"Audio exceeded the live-stream buffer limit."}""",
            Encoding.UTF8.GetString(SpeechFrames.Error(RelayErrors.AudioLimit).Span));
    }
}
