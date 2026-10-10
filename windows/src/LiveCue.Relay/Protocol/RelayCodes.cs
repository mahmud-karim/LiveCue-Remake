namespace LiveCue.Relay.Protocol;

/// <summary>
/// Every wire code of protocol 14.1 as a constant, plus <see cref="ClientDisconnected"/>, which is used
/// in desktop events only and is never sent on the wire.
/// </summary>
public static class RelayCodes
{
    public const string Unauthorized = "UNAUTHORIZED";

    public const string NotFound = "NOT_FOUND";

    public const string ProtocolMismatch = "PROTOCOL_MISMATCH";

    public const string InvalidRequest = "INVALID_REQUEST";

    public const string InvalidJson = "INVALID_JSON";

    public const string InvalidRequestId = "INVALID_REQUEST_ID";

    public const string InvalidAssistant = "INVALID_ASSISTANT";

    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";

    public const string RequestTimeout = "REQUEST_TIMEOUT";

    public const string TooManyRequests = "TOO_MANY_REQUESTS";

    public const string DeadlineExceeded = "DEADLINE_EXCEEDED";

    public const string InternalError = "INTERNAL_ERROR";

    public const string RelayPaused = "RELAY_PAUSED";

    public const string AssistantBusy = "ASSISTANT_BUSY";

    public const string AssistantCatalogUnavailable = "ASSISTANT_CATALOG_UNAVAILABLE";

    public const string AssistantSelectionUnavailable = "ASSISTANT_SELECTION_UNAVAILABLE";

    public const string AssistantFailed = "ASSISTANT_FAILED";

    public const string AssistantTimeout = "ASSISTANT_TIMEOUT";

    public const string RequestCancelled = "REQUEST_CANCELLED";

    public const string RequestNotFound = "REQUEST_NOT_FOUND";

    public const string InvalidModel = "INVALID_MODEL";

    public const string LabNotConfigured = "LAB_NOT_CONFIGURED";

    public const string ModelOperationInProgress = "MODEL_OPERATION_IN_PROGRESS";

    public const string ModelBusy = "MODEL_BUSY";

    public const string ModelMismatch = "MODEL_MISMATCH";

    public const string InvalidSpeechModel = "INVALID_SPEECH_MODEL";

    public const string SpeechBusy = "SPEECH_BUSY";

    public const string ModelLoadFailed = "MODEL_LOAD_FAILED";

    public const string ModelLoadTimeout = "MODEL_LOAD_TIMEOUT";

    public const string ModelNotReady = "MODEL_NOT_READY";

    public const string LabConnectionFailed = "LAB_CONNECTION_FAILED";

    public const string LabInferenceFailed = "LAB_INFERENCE_FAILED";

    public const string LabProtocolError = "LAB_PROTOCOL_ERROR";

    public const string LabDisconnected = "LAB_DISCONNECTED";

    public const string AudioOutOfSequence = "AUDIO_OUT_OF_SEQUENCE";

    public const string AudioLimit = "AUDIO_LIMIT";

    public const string FrameTooLarge = "FRAME_TOO_LARGE";

    public const string UnsupportedCommand = "UNSUPPORTED_COMMAND";

    public const string AudioIdleTimeout = "AUDIO_IDLE_TIMEOUT";

    public const string FinalizeTimeout = "FINALIZE_TIMEOUT";

    public const string SessionLimit = "SESSION_LIMIT";

    public const string PairingChanged = "PAIRING_CHANGED";

    public const string RelayStopping = "RELAY_STOPPING";

    /// <summary>The caller's connection went away before the response. Never sent on the wire.</summary>
    public const string ClientDisconnected = "CLIENT_DISCONNECTED";
}
