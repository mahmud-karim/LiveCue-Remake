using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Speech;

/// <summary>
/// The frames the relay builds itself (protocol 10.3): <c>loading</c>, <c>ready</c> and <c>error</c>.
/// Each always carries every member, in a fixed order. The four lab-derived frames are rebuilt by
/// <see cref="LabEventFilter"/>.
/// </summary>
internal static class SpeechFrames
{
    /// <summary>{"type":"error","code":…,"message":…} with the relay text of the code.</summary>
    public static ReadOnlyMemory<byte> Error(RelayError error) => RelayErrors.Frame(error);

    /// <summary>
    /// {"type":"loading","provider":…,"elapsedSeconds":…,"message":…}. <c>provider</c> is the model id the
    /// phone asked for, exactly as it sent it (the phone compares it with its own selection), never a
    /// label; the label appears only inside <c>message</c>. <c>elapsedSeconds</c> is a JSON integer.
    /// </summary>
    public static byte[] Loading(string model, int elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(model);
        var seconds = Math.Max(0, elapsedSeconds);
        return WireJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "loading");
            writer.WriteString("provider", model);
            writer.WriteNumber("elapsedSeconds", seconds);
            writer.WriteString("message", FixedTexts.LoadingFrame(model, seconds));
            writer.WriteEndObject();
        });
    }

    /// <summary>
    /// {"type":"ready","provider":…,"sampleRate":16000,"handshakeMs":…}. <c>provider</c> is the requested
    /// model id, <c>sampleRate</c> is the JSON integer 16000, and <c>handshakeMs</c> is the time since the
    /// upgrade in milliseconds with one decimal.
    /// </summary>
    public static byte[] Ready(string model, TimeSpan sinceUpgrade)
    {
        ArgumentNullException.ThrowIfNull(model);
        var handshakeMs = Math.Round(
            Math.Max(0, sinceUpgrade.TotalMilliseconds), 1, MidpointRounding.AwayFromZero);
        return WireJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "ready");
            writer.WriteString("provider", model);
            writer.WriteNumber("sampleRate", ProtocolConstants.AudioSampleRate);
            writer.WriteNumber("handshakeMs", handshakeMs);
            writer.WriteEndObject();
        });
    }
}
