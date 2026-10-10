using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Speech;

/// <summary>
/// The frames the relay builds itself (protocol 10.3): <c>loading</c>, <c>ready</c> and <c>error</c>.
///
/// First version (foundation): the <c>error</c> frame only. Work package WP-W05 adds the other two.
/// </summary>
internal static class SpeechFrames
{
    /// <summary>{"type":"error","code":…,"message":…} with the relay text of the code.</summary>
    public static ReadOnlyMemory<byte> Error(RelayError error) => RelayErrors.Frame(error);
}
