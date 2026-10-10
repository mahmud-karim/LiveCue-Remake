using System.Text.Json;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Speech;

/// <summary>What a lab message is, by its <c>type</c>.</summary>
internal enum LabEventKind
{
    /// <summary>No string <c>type</c>, an unknown one, or <c>audioProgress</c>: never forwarded.</summary>
    Other,

    Ready,
    SpeechStart,
    Transcript,
    SpeechComplete,
    StreamComplete,
    Error,
}

/// <summary>
/// The allowlist of protocol 10.3. Nothing the lab writes reaches the phone as it was written: the four
/// lab-derived frame types are rebuilt here, member by member, in the order <c>type</c>, <c>turnId</c>,
/// <c>audioProcessedMs</c>, <c>finalizationMs</c>, <c>transcript</c>. A member the table does not list for
/// a type, a member with the wrong JSON type, and every other member are left out.
/// </summary>
internal static class LabEventFilter
{
    /// <summary>The kind of a parsed lab message. Only a string <c>type</c> counts.</summary>
    public static LabEventKind Classify(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String)
        {
            return LabEventKind.Other;
        }

        return type.ValueEquals("transcript") ? LabEventKind.Transcript
            : type.ValueEquals("speechStart") ? LabEventKind.SpeechStart
            : type.ValueEquals("speechComplete") ? LabEventKind.SpeechComplete
            : type.ValueEquals("streamComplete") ? LabEventKind.StreamComplete
            : type.ValueEquals("ready") ? LabEventKind.Ready
            : type.ValueEquals("error") ? LabEventKind.Error
            : LabEventKind.Other;
    }

    /// <summary>
    /// True for the one first message the relay accepts from the lab (protocol 10.4 step 5): an object
    /// whose <c>type</c> is <c>ready</c>, whose <c>model</c> is the requested model and whose
    /// <c>sampleRate</c> is the number 16000.
    /// </summary>
    public static bool IsReadyFor(JsonElement message, string model) =>
        Classify(message) == LabEventKind.Ready
        && message.TryGetProperty("model", out var loaded)
        && loaded.ValueKind == JsonValueKind.String
        && loaded.ValueEquals(model)
        && message.TryGetProperty("sampleRate", out var rate)
        && rate.ValueKind == JsonValueKind.Number
        && rate.TryGetDouble(out var hertz)
        && hertz == ProtocolConstants.AudioSampleRate;

    /// <summary>
    /// The frame for the phone, rebuilt from the allowlist. <paramref name="kind"/> is one of the four
    /// lab-derived kinds. <paramref name="transcript"/> is the text as the frame carries it (cut), or
    /// null when the frame has none.
    ///
    /// A number is forwarded as its original token when it parses to a finite value. The text is cut to
    /// C-22 code units, one fewer when that would end on the first half of a surrogate pair. If the
    /// encoded frame is still longer than C-21, which needs a text made mostly of characters that JSON
    /// escapes, the text is cut further by the same rule until the frame fits.
    ///
    /// Null means no frame within C-21 exists. That cannot happen for a lab message within C-23, because
    /// without its text a rebuilt frame is never longer than the message it came from.
    /// </summary>
    public static byte[]? Rebuild(LabEventKind kind, JsonElement message, out string? transcript)
    {
        var type = kind switch
        {
            LabEventKind.SpeechStart => "speechStart",
            LabEventKind.Transcript => "transcript",
            LabEventKind.SpeechComplete => "speechComplete",
            LabEventKind.StreamComplete => "streamComplete",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "Not a lab-derived frame type."),
        };

        var closesTheStream = kind == LabEventKind.StreamComplete;
        var turnId = closesTheStream ? null : NumberToken(message, "turnId");
        var audioProcessedMs = NumberToken(message, "audioProcessedMs");
        var finalizationMs = closesTheStream ? NumberToken(message, "finalizationMs") : null;
        var text = kind is LabEventKind.Transcript or LabEventKind.SpeechComplete
            ? Text(message, "transcript")
            : null;

        byte[] Write(string? cut) => WireJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            WriteNumber(writer, "turnId", turnId);
            WriteNumber(writer, "audioProcessedMs", audioProcessedMs);
            WriteNumber(writer, "finalizationMs", finalizationMs);
            if (cut is not null)
            {
                writer.WriteString("transcript", cut);
            }

            writer.WriteEndObject();
        });

        transcript = text is null ? null : Cut(text, ProtocolConstants.MaxTranscriptUnits);
        var frame = Write(transcript);
        if (frame.Length <= ProtocolConstants.MaxRelayFrameBytes)
        {
            return frame;
        }

        if (transcript is null)
        {
            return null;
        }

        // The longest shorter text whose frame fits: a search over the number of code units kept.
        var full = transcript;
        byte[]? fitting = null;
        transcript = null;
        var low = 0;
        var high = full.Length - 1;
        while (low <= high)
        {
            var units = low + ((high - low) / 2);
            var candidate = Cut(full, units);
            var encoded = Write(candidate);
            if (encoded.Length <= ProtocolConstants.MaxRelayFrameBytes)
            {
                fitting = encoded;
                transcript = candidate;
                low = units + 1;
            }
            else
            {
                high = units - 1;
            }
        }

        return fitting;
    }

    /// <summary>
    /// The first <paramref name="units"/> UTF-16 code units of the text; one fewer when the last unit kept
    /// would be a high surrogate, so that a pair is never split.
    /// </summary>
    internal static string Cut(string text, int units)
    {
        if (text.Length <= units)
        {
            return text;
        }

        if (units > 0 && char.IsHighSurrogate(text[units - 1]))
        {
            units--;
        }

        return text[..units];
    }

    /// <summary>The member's original token when it is a JSON number with a finite value; otherwise null.</summary>
    private static string? NumberToken(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
        && double.IsFinite(number)
            ? value.GetRawText()
            : null;

    /// <summary>The member's text when it is a JSON string of well-formed text; otherwise null.</summary>
    private static string? Text(JsonElement message, string name)
    {
        if (!message.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            // An escape that names half a surrogate pair: not text, so the member is left out.
            return null;
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, string? token)
    {
        if (token is not null)
        {
            writer.WritePropertyName(name);
            writer.WriteRawValue(token);
        }
    }
}
