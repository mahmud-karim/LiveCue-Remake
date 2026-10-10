using System.Collections.Frozen;

namespace LiveCue.Relay.Protocol;

/// <summary>One row of protocol 14.1: status, code and the fixed relay text.</summary>
internal readonly record struct RelayError(int Status, string Code, string Text);

/// <summary>
/// The table of protocol 14.1. One static readonly member per code, named like RelayCodes.
/// Body(error) returns the pre-built UTF-8 bytes of {"error":Text,"code":Code}; Frame(error) the bytes of
/// {"type":"error","code":Code,"message":Text}. ForCode returns null for an unknown code.
/// A code that exists only as a speech frame has Status 0.
/// </summary>
internal static partial class RelayErrors
{
    public static readonly RelayError Unauthorized = new(401, RelayCodes.Unauthorized, "Invalid pairing token.");

    public static readonly RelayError NotFound = new(404, RelayCodes.NotFound, "Endpoint not found.");

    public static readonly RelayError ProtocolMismatch = new(400, RelayCodes.ProtocolMismatch, "Protocol version mismatch.");

    public static readonly RelayError InvalidRequest = new(400, RelayCodes.InvalidRequest, "Request is not valid for this endpoint.");

    public static readonly RelayError InvalidJson = new(400, RelayCodes.InvalidJson, "Body must be valid JSON.");

    public static readonly RelayError InvalidRequestId = new(400, RelayCodes.InvalidRequestId, "A valid requestId is required.");

    public static readonly RelayError InvalidAssistant = new(400, RelayCodes.InvalidAssistant, "Invalid assistant settings.");

    public static readonly RelayError PayloadTooLarge = new(413, RelayCodes.PayloadTooLarge, "Request exceeds 128 KB.");

    public static readonly RelayError RequestTimeout = new(408, RelayCodes.RequestTimeout, "Request body did not arrive in time.");

    public static readonly RelayError TooManyRequests = new(429, RelayCodes.TooManyRequests, "Too many requests in progress.");

    public static readonly RelayError DeadlineExceeded = new(504, RelayCodes.DeadlineExceeded, "The PC took too long to answer.");

    public static readonly RelayError InternalError = new(500, RelayCodes.InternalError, "The PC relay hit an internal error.");

    public static readonly RelayError RelayPaused = new(503, RelayCodes.RelayPaused, "PC relay is paused. Resume it in LiveCue Desktop.");

    public static readonly RelayError AssistantBusy = new(429, RelayCodes.AssistantBusy, "The PC is processing another request. Try again shortly.");

    public static readonly RelayError AssistantCatalogUnavailable = new(503, RelayCodes.AssistantCatalogUnavailable, "The PC could not read Codex's model list. Keep Codex signed in, then retry. Your selected model has not changed.");

    public static readonly RelayError AssistantSelectionUnavailable = new(400, RelayCodes.AssistantSelectionUnavailable, "The selected assistant model or reasoning level is not in this PC's current Codex catalog. Open Assistant models & timing and choose an available combination.");

    public static readonly RelayError AssistantFailed = new(500, RelayCodes.AssistantFailed, "The local assistant request failed.");

    public static readonly RelayError AssistantTimeout = new(504, RelayCodes.AssistantTimeout, "The local assistant request timed out.");

    public static readonly RelayError RequestCancelled = new(409, RelayCodes.RequestCancelled, "The request was cancelled.");

    public static readonly RelayError RequestNotFound = new(404, RelayCodes.RequestNotFound, "No request with that id is in progress.");

    public static readonly RelayError InvalidModel = new(400, RelayCodes.InvalidModel, "Choose Nemotron or Qwen3.");

    public static readonly RelayError LabNotConfigured = new(503, RelayCodes.LabNotConfigured, "PC speech models are not set up on this PC.");

    public static readonly RelayError ModelOperationInProgress = new(409, RelayCodes.ModelOperationInProgress, "A PC model operation is already in progress. Wait for it to finish.");

    public static readonly RelayError ModelBusy = new(409, RelayCodes.ModelBusy, "The PC model is in use.");

    public static readonly RelayError ModelMismatch = new(409, RelayCodes.ModelMismatch, "A different PC model is loaded. Select it before stopping.");

    public static readonly RelayError InvalidSpeechModel = new(0, RelayCodes.InvalidSpeechModel, "Unknown speech model.");

    public static readonly RelayError SpeechBusy = new(0, RelayCodes.SpeechBusy, "Another speech stream is active.");

    public static readonly RelayError ModelLoadFailed = new(0, RelayCodes.ModelLoadFailed, "Could not load the PC model.");

    public static readonly RelayError ModelLoadTimeout = new(0, RelayCodes.ModelLoadTimeout, "Model is still loading.");

    public static readonly RelayError ModelNotReady = new(0, RelayCodes.ModelNotReady, "The selected PC model is not ready or is busy.");

    public static readonly RelayError LabConnectionFailed = new(0, RelayCodes.LabConnectionFailed, "Local model connection failed.");

    public static readonly RelayError LabInferenceFailed = new(0, RelayCodes.LabInferenceFailed, "PC speech inference failed.");

    public static readonly RelayError LabProtocolError = new(0, RelayCodes.LabProtocolError, "Unexpected local speech response.");

    public static readonly RelayError LabDisconnected = new(0, RelayCodes.LabDisconnected, "Local model disconnected.");

    public static readonly RelayError AudioOutOfSequence = new(0, RelayCodes.AudioOutOfSequence, "Audio arrived outside an active transcription stream.");

    public static readonly RelayError AudioLimit = new(0, RelayCodes.AudioLimit, "Audio exceeded the live-stream buffer limit.");

    public static readonly RelayError FrameTooLarge = new(0, RelayCodes.FrameTooLarge, "Speech frame exceeds 16 KB.");

    public static readonly RelayError UnsupportedCommand = new(0, RelayCodes.UnsupportedCommand, "Unsupported speech command.");

    public static readonly RelayError AudioIdleTimeout = new(0, RelayCodes.AudioIdleTimeout, "Audio stream timed out.");

    public static readonly RelayError FinalizeTimeout = new(0, RelayCodes.FinalizeTimeout, "Local model did not finalize in time.");

    public static readonly RelayError SessionLimit = new(0, RelayCodes.SessionLimit, "30-minute session limit reached.");

    public static readonly RelayError PairingChanged = new(0, RelayCodes.PairingChanged, "Pairing changed on the PC.");

    public static readonly RelayError RelayStopping = new(0, RelayCodes.RelayStopping, "The PC relay is stopping.");

    /// <summary>Every row, in the order of the protocol's table.</summary>
    public static IReadOnlyList<RelayError> All { get; } =
    [
        Unauthorized,
        NotFound,
        ProtocolMismatch,
        InvalidRequest,
        InvalidJson,
        InvalidRequestId,
        InvalidAssistant,
        PayloadTooLarge,
        RequestTimeout,
        TooManyRequests,
        DeadlineExceeded,
        InternalError,
        RelayPaused,
        AssistantBusy,
        AssistantCatalogUnavailable,
        AssistantSelectionUnavailable,
        AssistantFailed,
        AssistantTimeout,
        RequestCancelled,
        RequestNotFound,
        InvalidModel,
        LabNotConfigured,
        ModelOperationInProgress,
        ModelBusy,
        ModelMismatch,
        InvalidSpeechModel,
        SpeechBusy,
        ModelLoadFailed,
        ModelLoadTimeout,
        ModelNotReady,
        LabConnectionFailed,
        LabInferenceFailed,
        LabProtocolError,
        LabDisconnected,
        AudioOutOfSequence,
        AudioLimit,
        FrameTooLarge,
        UnsupportedCommand,
        AudioIdleTimeout,
        FinalizeTimeout,
        SessionLimit,
        PairingChanged,
        RelayStopping,
    ];

    private static readonly FrozenDictionary<string, Prebuilt> ByCode =
        All.ToFrozenDictionary(error => error.Code, error => new Prebuilt(error), StringComparer.Ordinal);

    public static ReadOnlyMemory<byte> Body(RelayError error) =>
        ByCode.TryGetValue(error.Code, out var prebuilt) && prebuilt.Error == error
            ? prebuilt.Body
            : BuildBody(error);

    public static ReadOnlyMemory<byte> Frame(RelayError error) =>
        ByCode.TryGetValue(error.Code, out var prebuilt) && prebuilt.Error == error
            ? prebuilt.Frame
            : BuildFrame(error);

    public static RelayError? ForCode(string code) =>
        code is not null && ByCode.TryGetValue(code, out var prebuilt) ? prebuilt.Error : null;

    private static byte[] BuildBody(RelayError error) => WireJson.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("error", error.Text);
        writer.WriteString("code", error.Code);
        writer.WriteEndObject();
    });

    private static byte[] BuildFrame(RelayError error) => WireJson.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", "error");
        writer.WriteString("code", error.Code);
        writer.WriteString("message", error.Text);
        writer.WriteEndObject();
    });

    private sealed class Prebuilt(RelayError error)
    {
        public RelayError Error { get; } = error;

        public byte[] Body { get; } = BuildBody(error);

        public byte[] Frame { get; } = BuildFrame(error);
    }
}
