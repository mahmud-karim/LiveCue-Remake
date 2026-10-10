using System.Text;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Contracts;

/// <summary>
/// The error table against protocol 14.1, written out here a second time on purpose: every code with
/// its HTTP status (0 for a code that exists only as a speech frame) and its exact relay text.
/// </summary>
public sealed class RelayErrorTableTests
{
    public static TheoryData<string, int, string> Protocol => new()
    {
        { "UNAUTHORIZED", 401, "Invalid pairing token." },
        { "NOT_FOUND", 404, "Endpoint not found." },
        { "PROTOCOL_MISMATCH", 400, "Protocol version mismatch." },
        { "INVALID_REQUEST", 400, "Request is not valid for this endpoint." },
        { "INVALID_JSON", 400, "Body must be valid JSON." },
        { "INVALID_REQUEST_ID", 400, "A valid requestId is required." },
        { "INVALID_ASSISTANT", 400, "Invalid assistant settings." },
        { "PAYLOAD_TOO_LARGE", 413, "Request exceeds 128 KB." },
        { "REQUEST_TIMEOUT", 408, "Request body did not arrive in time." },
        { "TOO_MANY_REQUESTS", 429, "Too many requests in progress." },
        { "DEADLINE_EXCEEDED", 504, "The PC took too long to answer." },
        { "INTERNAL_ERROR", 500, "The PC relay hit an internal error." },
        { "RELAY_PAUSED", 503, "PC relay is paused. Resume it in LiveCue Desktop." },
        { "ASSISTANT_BUSY", 429, "The PC is processing another request. Try again shortly." },
        { "ASSISTANT_CATALOG_UNAVAILABLE", 503, "The PC could not read Codex's model list. Keep Codex signed in, then retry. Your selected model has not changed." },
        { "ASSISTANT_SELECTION_UNAVAILABLE", 400, "The selected assistant model or reasoning level is not in this PC's current Codex catalog. Open Assistant models & timing and choose an available combination." },
        { "ASSISTANT_FAILED", 500, "The local assistant request failed." },
        { "ASSISTANT_TIMEOUT", 504, "The local assistant request timed out." },
        { "REQUEST_CANCELLED", 409, "The request was cancelled." },
        { "REQUEST_NOT_FOUND", 404, "No request with that id is in progress." },
        { "INVALID_MODEL", 400, "Choose Nemotron or Qwen3." },
        { "LAB_NOT_CONFIGURED", 503, "PC speech models are not set up on this PC." },
        { "MODEL_OPERATION_IN_PROGRESS", 409, "A PC model operation is already in progress. Wait for it to finish." },
        { "MODEL_BUSY", 409, "The PC model is in use." },
        { "MODEL_MISMATCH", 409, "A different PC model is loaded. Select it before stopping." },
        { "INVALID_SPEECH_MODEL", 0, "Unknown speech model." },
        { "SPEECH_BUSY", 0, "Another speech stream is active." },
        { "MODEL_LOAD_FAILED", 0, "Could not load the PC model." },
        { "MODEL_LOAD_TIMEOUT", 0, "Model is still loading." },
        { "MODEL_NOT_READY", 0, "The selected PC model is not ready or is busy." },
        { "LAB_CONNECTION_FAILED", 0, "Local model connection failed." },
        { "LAB_INFERENCE_FAILED", 0, "PC speech inference failed." },
        { "LAB_PROTOCOL_ERROR", 0, "Unexpected local speech response." },
        { "LAB_DISCONNECTED", 0, "Local model disconnected." },
        { "AUDIO_OUT_OF_SEQUENCE", 0, "Audio arrived outside an active transcription stream." },
        { "AUDIO_LIMIT", 0, "Audio exceeded the live-stream buffer limit." },
        { "FRAME_TOO_LARGE", 0, "Speech frame exceeds 16 KB." },
        { "UNSUPPORTED_COMMAND", 0, "Unsupported speech command." },
        { "AUDIO_IDLE_TIMEOUT", 0, "Audio stream timed out." },
        { "FINALIZE_TIMEOUT", 0, "Local model did not finalize in time." },
        { "SESSION_LIMIT", 0, "30-minute session limit reached." },
        { "PAIRING_CHANGED", 0, "Pairing changed on the PC." },
        { "RELAY_STOPPING", 0, "The PC relay is stopping." },
    };

    [Theory]
    [MemberData(nameof(Protocol))]
    public void Table_matches_the_protocol(string code, int status, string text)
    {
        var error = RelayErrors.ForCode(code);

        Assert.NotNull(error);
        Assert.Equal(new RelayError(status, code, text), error.Value);
        Assert.Equal(
            "{\"error\":\"" + text + "\",\"code\":\"" + code + "\"}",
            Encoding.UTF8.GetString(RelayErrors.Body(error.Value).Span));
        Assert.Equal(
            "{\"type\":\"error\",\"code\":\"" + code + "\",\"message\":\"" + text + "\"}",
            Encoding.UTF8.GetString(RelayErrors.Frame(error.Value).Span));
    }

    [Fact]
    public void The_table_has_exactly_the_codes_of_the_protocol_and_each_has_a_constant()
    {
        var expected = Protocol.Select(row => (string)row[0]).ToList();
        var constants = typeof(RelayCodes).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(43, expected.Count);
        Assert.Equal(expected, RelayErrors.All.Select(error => error.Code));
        Assert.Equal(expected.Append(RelayCodes.ClientDisconnected).Order(), constants.Order());
        Assert.Null(RelayErrors.ForCode(RelayCodes.ClientDisconnected));
        Assert.Null(RelayErrors.ForCode("unauthorized"));
        Assert.Null(RelayErrors.ForCode(string.Empty));
    }

    [Fact]
    public void The_three_401_answers_come_from_one_array()
    {
        var first = RelayErrors.Body(RelayErrors.Unauthorized);
        var second = RelayErrors.Body(RelayErrors.Unauthorized);

        Assert.True(first.Span == second.Span, "Body returns the same pre-built bytes every time.");
        Assert.Equal(
            "{\"error\":\"Invalid pairing token.\",\"code\":\"UNAUTHORIZED\"}",
            Encoding.UTF8.GetString(first.Span));
    }

    [Fact]
    public void Text_that_is_not_ascii_or_holds_an_ampersand_is_written_as_it_is()
    {
        var body = Encoding.UTF8.GetString(RelayErrors.Body(RelayErrors.AssistantSelectionUnavailable).Span);

        Assert.Contains("Assistant models & timing", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", body, StringComparison.Ordinal);
    }
}
