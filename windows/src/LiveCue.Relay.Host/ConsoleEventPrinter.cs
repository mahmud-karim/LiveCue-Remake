using System.Globalization;
using LiveCue.Hosting;

namespace LiveCue.Relay.Host;

/// <summary>
/// Turns host events into output lines (architecture 3.14). No line ever carries the pairing token: no
/// event does.
///
/// First version (foundation): text lines only. <c>--events json</c> is parsed, and one JSON object per
/// event is work package WP-W06.
/// </summary>
internal static class ConsoleEventPrinter
{
    /// <summary>The line for one event, or null when the event prints nothing.</summary>
    public static string? Text(HostEvent hostEvent) => hostEvent switch
    {
        CheckingStarted => "Checking Codex and Tailscale...",
        PrerequisitesChecked => null,
        RelayStarting starting => string.Create(
            CultureInfo.InvariantCulture, $"Starting the relay on 127.0.0.1:{starting.Options.Port}."),
        ConnectionInspected inspected =>
            $"Tailscale mapping: private {inspected.Inspection.Private.State}, public {inspected.Inspection.Public.State}.",
        RelayEventForwarded forwarded => Text(forwarded.Event),
        _ => hostEvent.GetType().Name,
    };

    private static string Text(RelayEvent relayEvent) => relayEvent switch
    {
        RelayStarted started => string.Create(
            CultureInfo.InvariantCulture,
            $"Relay {started.RelayVersion} started on 127.0.0.1:{started.Port} (protocol {started.ProtocolVersion}). PC address: {started.Endpoint}"),
        RelayStopped => "Relay stopped.",
        RelayFaulted faulted => faulted.Message,
        AcceptingChanged changed => changed.Accepting ? "Relay resumed." : "Relay paused.",
        PhoneSeen => "iPhone seen.",
        Notice notice => notice.Message,
        AssistantNotice notice => notice.Message,
        LocalModelChanged changed => changed.Message,
        SpeechStreamChanged changed => changed.Message,
        Diagnostic diagnostic => "Relay diagnostic: " + diagnostic.Message,
        _ => relayEvent.GetType().Name,
    };
}
