import Foundation

/// The wire codes of protocol 14.1.
public enum RelayWireCode: String, CaseIterable, Sendable {
    case unauthorized = "UNAUTHORIZED", notFound = "NOT_FOUND", protocolMismatch = "PROTOCOL_MISMATCH"
    case invalidRequest = "INVALID_REQUEST", invalidJSON = "INVALID_JSON", invalidRequestID = "INVALID_REQUEST_ID"
    case invalidAssistant = "INVALID_ASSISTANT", payloadTooLarge = "PAYLOAD_TOO_LARGE", requestTimeout = "REQUEST_TIMEOUT"
    case tooManyRequests = "TOO_MANY_REQUESTS", deadlineExceeded = "DEADLINE_EXCEEDED", internalError = "INTERNAL_ERROR"
    case relayPaused = "RELAY_PAUSED", assistantBusy = "ASSISTANT_BUSY"
    case assistantCatalogUnavailable = "ASSISTANT_CATALOG_UNAVAILABLE"
    case assistantSelectionUnavailable = "ASSISTANT_SELECTION_UNAVAILABLE"
    case assistantFailed = "ASSISTANT_FAILED", assistantTimeout = "ASSISTANT_TIMEOUT"
    case requestCancelled = "REQUEST_CANCELLED", requestNotFound = "REQUEST_NOT_FOUND"
    case invalidModel = "INVALID_MODEL", labNotConfigured = "LAB_NOT_CONFIGURED"
    case modelOperationInProgress = "MODEL_OPERATION_IN_PROGRESS", modelBusy = "MODEL_BUSY", modelMismatch = "MODEL_MISMATCH"
    case invalidSpeechModel = "INVALID_SPEECH_MODEL", speechBusy = "SPEECH_BUSY"
    case modelLoadFailed = "MODEL_LOAD_FAILED", modelLoadTimeout = "MODEL_LOAD_TIMEOUT", modelNotReady = "MODEL_NOT_READY"
    case labConnectionFailed = "LAB_CONNECTION_FAILED", labInferenceFailed = "LAB_INFERENCE_FAILED"
    case labProtocolError = "LAB_PROTOCOL_ERROR", labDisconnected = "LAB_DISCONNECTED"
    case audioOutOfSequence = "AUDIO_OUT_OF_SEQUENCE", audioLimit = "AUDIO_LIMIT", frameTooLarge = "FRAME_TOO_LARGE"
    case unsupportedCommand = "UNSUPPORTED_COMMAND", audioIdleTimeout = "AUDIO_IDLE_TIMEOUT"
    case finalizeTimeout = "FINALIZE_TIMEOUT", sessionLimit = "SESSION_LIMIT"
    case pairingChanged = "PAIRING_CHANGED", relayStopping = "RELAY_STOPPING"
}

/// The phone-local conditions of protocol 14.2.
public enum RelayLocalCondition: String, CaseIterable, Sendable {
    case tls = "L-TLS", unreachable = "L-UNREACHABLE", network = "L-NETWORK", timeout = "L-TIMEOUT"
    case noRelay = "L-NO-RELAY", http = "L-HTTP", unreadable = "L-UNREADABLE", badEndpoint = "L-BAD-ENDPOINT"
    case notPaired = "L-NOT-PAIRED", notConfirmed = "L-NOT-CONFIRMED"
    case speechNotConfirmed = "L-SPEECH-NOT-CONFIRMED", unexpectedFrame = "L-UNEXPECTED-FRAME"
    case streamDropped = "L-STREAM-DROPPED"
}

/// The one error type of every relay call and of the relay speech stream.
public struct RelayFailure: Error, Equatable, Sendable {
    public enum Kind: Equatable, Sendable {
        /// A code exactly as received; it may be one this build does not know.
        case wire(String)
        case local(RelayLocalCondition)
    }

    public enum Origin: Equatable, Sendable {
        case http, stream
    }

    public var kind: Kind
    public var origin: Origin
    /// Set for HTTP outcomes; shown by L-HTTP.
    public var httpStatus: Int?
    /// The relay's `error` or `message`; used only for an unknown code.
    public var relayText: String?

    public init(kind: Kind, origin: Origin, httpStatus: Int? = nil, relayText: String? = nil) {
        self.kind = kind
        self.origin = origin
        self.httpStatus = httpStatus
        self.relayText = relayText
    }

    public static func local(_ condition: RelayLocalCondition, origin: Origin = .http, httpStatus: Int? = nil) -> RelayFailure {
        RelayFailure(kind: Kind.local(condition), origin: origin, httpStatus: httpStatus)
    }

    public static func wire(_ code: RelayWireCode, origin: Origin = .http) -> RelayFailure {
        RelayFailure(kind: Kind.wire(code.rawValue), origin: origin)
    }

    /// nil for local conditions and unknown codes.
    public var wireCode: RelayWireCode? {
        guard case .wire(let code) = kind else { return nil }
        return RelayWireCode(rawValue: code)
    }

    public func isCode(_ code: RelayWireCode) -> Bool {
        kind == Kind.wire(code.rawValue)
    }

    public func isLocal(_ condition: RelayLocalCondition) -> Bool {
        kind == Kind.local(condition)
    }
}
