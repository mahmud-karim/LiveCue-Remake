using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LiveCue.Hosting;

namespace LiveCue.Relay.Host;

/// <summary>
/// Turns host events into output lines (architecture 3.14, W-design 4.16): one text line per event, or
/// with <c>--events json</c> one JSON object per event.
///
/// No line ever carries the pairing token: no event does. Conversation text is not printed either. The
/// text form never prints it. The JSON form leaves out the members <c>Transcript</c>,
/// <c>PartialTranscript</c>, <c>Instruction</c> and <c>PayloadJson</c> of the request events and
/// <c>Text</c> of <c>SpeechText</c>, unless <c>--include-content</c> was given.
/// </summary>
internal static class ConsoleEventPrinter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,

        // One line per object either way: a line break inside a text is always escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The name of an event's time in a JSON line.</summary>
    private const string TimeMember = "at";

    /// <summary>The members that hold conversation text, by their names in a JSON line.</summary>
    private static readonly string[] RequestContent = ["transcript", "partialTranscript", "instruction", "payloadJson"];
    private static readonly string[] SpeechContent = ["text"];

    /// <summary>The text line for one event, or null when the event prints nothing.</summary>
    public static string? Text(HostEvent hostEvent) => hostEvent switch
    {
        CheckingStarted => "Checking Codex and Tailscale...",
        PrerequisitesChecked => null,
        RelayStarting starting => Invariant($"Starting the relay on 127.0.0.1:{starting.Options.Port}."),
        ConnectionInspected inspected => Text(inspected.Inspection),
        RelayEventForwarded forwarded => Text(forwarded.Event),
        _ => hostEvent?.GetType().Name,
    };

    /// <summary>
    /// The JSON line for one event: <c>type</c> (the record's name) and then the record's members, in
    /// camel case, enums as their names. An event of the relay is written as itself, not wrapped.
    /// </summary>
    public static string Json(HostEvent hostEvent, bool includeContent)
    {
        ArgumentNullException.ThrowIfNull(hostEvent);

        object subject = hostEvent is RelayEventForwarded forwarded ? forwarded.Event : hostEvent;
        var withheld = includeContent ? [] : ContentMembers(subject);
        var members = JsonSerializer.SerializeToNode(subject, subject.GetType(), JsonOptions)!.AsObject();

        // "type" first, then the time, then the members in the order the record declares them.
        var line = new JsonObject { ["type"] = subject.GetType().Name };
        var names = members.Select(member => member.Key).ToList();
        foreach (var name in names.Where(name => name == TimeMember).Concat(names.Where(name => name != TimeMember)))
        {
            var value = members[name];
            members.Remove(name);
            if (!withheld.Contains(name, StringComparer.Ordinal))
            {
                line[name] = value;
            }
        }

        return line.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// A line of the host itself in the JSON form, so that every line of a run is one JSON object: a
    /// warning, or the reason the host is about to end with a non-zero exit code.
    /// </summary>
    public static string JsonMessage(string type, string message, int? exitCode = null)
    {
        var line = new JsonObject { ["type"] = type, ["message"] = message };
        if (exitCode is { } code)
        {
            line["exitCode"] = code;
        }

        return line.ToJsonString(JsonOptions);
    }

    private static string[] ContentMembers(object subject) => subject switch
    {
        RequestStarted or RequestCompleted or RequestFailed => RequestContent,
        SpeechText => SpeechContent,
        _ => [],
    };

    private static string Text(ConnectionInspection inspection) =>
        inspection.StatusFailure != TailscaleReadFailure.None
            ? "Tailscale mapping: unknown, its configuration could not be read."
            : Invariant(
                $"Tailscale mapping: private {inspection.Private.State}, public {inspection.Public.State}; {inspection.Exposure.Count} handler(s) forward to the relay port, {inspection.Exposure.Count(handler => handler.FunnelOn)} through Funnel.");

    private static string Text(RelayEvent relayEvent) => relayEvent switch
    {
        RelayStarted started => Invariant(
            $"Relay {started.RelayVersion} started on 127.0.0.1:{started.Port} (protocol {started.ProtocolVersion}). PC address: {started.Endpoint}"),
        RelayStopped => "Relay stopped.",

        // The host prints the text for a fault itself, once, when it ends (architecture 3.11).
        RelayFaulted faulted => "Relay fault: " + RelayFaultCodes.Name(faulted.Code) + ".",
        AcceptingChanged changed => changed.Accepting ? "Relay resumed." : "Relay paused.",
        PhoneSeen => "iPhone seen.",
        PairingShown => "Pairing shown.",
        SelfCheckCompleted completed => "Self-check: " + completed.Result + ".",
        Notice notice => notice.Message,
        RequestStarted started => Invariant(
            $"Request {started.RequestId} started: {started.Kind}, {started.Model}, {started.ReasoningEffort}."),
        RequestCompleted completed => Invariant(
            $"Request {completed.RequestId} completed in {completed.DurationMs:0} ms (Codex {completed.CodexMs:0} ms)."),
        RequestFailed failed => Invariant($"Request {failed.RequestId} failed: {failed.Code}. {failed.Message}"),
        AssistantNotice notice => notice.Message,
        LocalModelChanged changed => changed.Message,
        SpeechStreamChanged changed => changed.Message,
        SpeechText => "Speech text received.",
        EdgeStatistics statistics => Invariant(
            $"Rejected requests so far: {statistics.Malformed} malformed, {statistics.TokenNotAccepted} with a token that is not accepted, {statistics.FunnelInPrivateMode} through Funnel in Private mode."),
        Diagnostic diagnostic => "Relay diagnostic: " + diagnostic.Source + ": " + diagnostic.Message,
        _ => relayEvent.GetType().Name,
    };

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
