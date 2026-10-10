namespace LiveCue.Relay.Core;

/// <summary>Where every area publishes. Publishing never blocks and never throws.</summary>
internal interface IRelayEventSink
{
    /// <summary>The clock's current time, for the At member of a new event.</summary>
    DateTimeOffset Now { get; }

    void Publish(RelayEvent relayEvent);

    /// <summary>Publishes a Diagnostic whose message is "<paramref name="fixedText"/> (TypeName)".
    /// The exception's own message and stack are never used.</summary>
    void Diagnostic(string source, string fixedText, Exception? exception = null);
}
