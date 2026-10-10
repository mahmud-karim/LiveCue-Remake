import Foundation

/// The phone's own text for every relay code and local condition (protocol 14).
///
/// The texts are copied from the tables of protocol 14.1 and 14.2. None is built from a system error, a
/// token or a response body; the one exception is the relay's own text for a code this build does not
/// know.
public enum RelayMessages {
    /// Protocol 14.1 and 14.2. A known code or condition gives its phone text. An unknown code gives
    /// `relayText` when it is 1 to 300 characters, otherwise the L-HTTP text (origin http) or the
    /// L-STREAM-DROPPED text (origin stream). No result ever contains a token.
    public static func text(for failure: RelayFailure) -> String {
        switch failure.kind {
        case .local(let condition):
            return text(for: condition, httpStatus: failure.httpStatus)
        case .wire(let received):
            if let code = RelayWireCode(rawValue: received) {
                return text(for: code)
            }
            if let relayText = failure.relayText,
               (1...RelayConstants.unknownCodeTextMaximumCharacters).contains(relayText.count) {
                return relayText
            }
            switch failure.origin {
            case .http:
                return text(for: RelayLocalCondition.http, httpStatus: failure.httpStatus)
            case .stream:
                return text(for: RelayLocalCondition.streamDropped)
            }
        }
    }

    public static func text(for code: RelayWireCode) -> String {
        switch code {
        case .unauthorized:
            return "The PC no longer recognizes this pairing. Scan its pairing QR once to reconnect."
        case .notFound:
            return "LiveCue Desktop does not offer this function. Update LiveCue Desktop, then retry."
        case .protocolMismatch:
            return "This iPhone app and LiveCue Desktop are different versions. Update both, then retry."
        case .invalidRequest, .invalidJSON, .invalidRequestID, .invalidAssistant:
            return "LiveCue Desktop could not read this request. Update both apps, then retry."
        case .payloadTooLarge:
            return "This request was too large for your PC. Retry after a shorter stretch of conversation."
        case .requestTimeout:
            return "The request reached your PC too slowly. Check the connection and retry."
        case .tooManyRequests:
            return "Your PC is handling too many requests. Wait a moment and retry."
        case .deadlineExceeded:
            return "Your PC took too long to answer. Retry in a moment."
        case .internalError:
            return "LiveCue Desktop hit an internal error. Retry, and restart LiveCue Desktop if it keeps happening."
        case .relayPaused:
            return "PC relay is paused. Resume it in LiveCue Desktop."
        case .assistantBusy:
            return "The PC is processing another request. Try again shortly."
        case .assistantCatalogUnavailable:
            return "The PC could not read Codex's model list. Keep Codex signed in, then retry. Your selected model has not changed."
        case .assistantSelectionUnavailable:
            return "The selected assistant model or reasoning level is not in this PC's current Codex catalog. Open Assistant models & timing and choose an available combination."
        case .assistantFailed:
            return "Codex on your PC could not finish this answer. Check that Codex is signed in on the PC, then retry."
        case .assistantTimeout:
            return "Codex on your PC took too long to answer. Retry, or choose a faster model or a lower reasoning level."
        case .requestCancelled:
            return "This request was cancelled."
        case .requestNotFound:
            // Protocol 14.1 marks this code "never shown": it only answers a cancel, whose outcome is
            // ignored. The relay's own sentence stands in, so that no code is without a text.
            return "No request with that id is in progress."
        case .invalidModel:
            return "Choose Nemotron or Qwen3."
        case .labNotConfigured:
            return "PC speech models are not set up on this PC. Set the speech lab folder in LiveCue Desktop."
        case .modelOperationInProgress:
            return "A PC model operation is already in progress. Wait for it to finish."
        case .modelBusy:
            return "The PC model is in use. Stop the other conversation or lab test, then retry."
        case .modelMismatch:
            return "A different PC model is loaded. Select it before stopping."
        case .invalidSpeechModel:
            return "Your PC does not offer the selected speech model. Update LiveCue Desktop, then retry."
        case .speechBusy:
            return "Your PC is already transcribing another conversation. End that one first, then retry."
        case .modelLoadFailed:
            return "Could not load the PC model. Start Docker Desktop, stop any lab test, and retry."
        case .modelLoadTimeout:
            return "Model is still loading. Open Settings and wait for PC model status to become Ready, then retry."
        case .modelNotReady:
            return "The selected PC model is not ready or is busy. Retry after the other test stops."
        case .labConnectionFailed:
            return "Local model connection failed. Check Docker Desktop and retry."
        case .labInferenceFailed:
            return "PC speech inference failed. Check the local model service and retry."
        case .labProtocolError:
            return "The PC speech service sent an unexpected response. Resume to retry."
        case .labDisconnected:
            return "Local model disconnected. Resume to reconnect."
        case .audioOutOfSequence:
            return "The audio stream got out of step with your PC. Resume to reconnect."
        case .audioLimit, .frameTooLarge:
            return "Audio arrived faster than your PC could accept it. Resume to reconnect."
        case .unsupportedCommand:
            return "PC transcription stopped unexpectedly. Resume to reconnect."
        case .audioIdleTimeout:
            return "Audio stream timed out. Resume to reconnect."
        case .finalizeTimeout:
            return "Local model did not finalize in time. Some final words may be missing."
        case .sessionLimit:
            return "30-minute session limit reached. Pause and resume to continue."
        case .pairingChanged:
            return "The pairing was reset on your PC. Scan its pairing QR to reconnect."
        case .relayStopping:
            return "LiveCue Desktop stopped its relay. Start it again, then resume."
        }
    }

    /// `httpStatus` is used by L-HTTP only: "Relay error (502)."
    public static func text(for condition: RelayLocalCondition, httpStatus: Int? = nil) -> String {
        switch condition {
        case .tls:
            return "The secure connection to your PC failed. Check the PC tunnel and retry. Scanning the QR again will not repair this connection."
        case .unreachable:
            return "Your PC is currently unreachable. Keep LiveCue Desktop running and check your internet connection. The app will reconnect automatically."
        case .network:
            return "Could not reach your PC because of a network error. Check the connection and retry."
        case .timeout:
            return "Your PC did not answer in time. Check that LiveCue Desktop is running, then retry."
        case .noRelay:
            return "Your PC answered, but the LiveCue relay is not running. Open LiveCue Desktop and start the relay."
        case .http:
            guard let httpStatus else {
                // Every HTTP outcome carries its status; this is the text when a caller has none.
                return "Relay error."
            }
            return "Relay error (\(httpStatus))."
        case .unreadable:
            return "The relay returned an unreadable response."
        case .badEndpoint:
            return "The relay address is invalid."
        case .notPaired:
            return "Pair this iPhone with the Windows relay first."
        case .notConfirmed:
            return "The PC did not confirm the selected model. Restart the updated LiveCue Desktop app, then retry."
        case .speechNotConfirmed:
            return "PC did not confirm the selected speech model. Update/restart LiveCue Desktop and its tunnel."
        case .unexpectedFrame:
            return "PC transcription is not ready."
        case .streamDropped:
            return "The connection to your PC dropped. Resume to reconnect."
        }
    }

    // MARK: - Protocol 14.3 compositions

    /// "Transcription could not start. " + message
    public static func startFailure(_ message: String) -> String {
        "Transcription could not start. " + message
    }

    /// "Your pairing is saved. " + text for L-TLS, L-UNREACHABLE, L-NETWORK, L-TIMEOUT, L-NO-RELAY; the
    /// bare text otherwise.
    public static func savedPairingHealthFailure(_ failure: RelayFailure) -> String {
        let message = text(for: failure)
        guard case .local(let condition) = failure.kind else {
            return message
        }
        switch condition {
        case .tls, .unreachable, .network, .timeout, .noRelay:
            return "Your pairing is saved. " + message
        case .http, .unreadable, .badEndpoint, .notPaired, .notConfirmed, .speechNotConfirmed,
             .unexpectedFrame, .streamDropped:
            return message
        }
    }

    /// "Could not load models. Restart the updated PC app and check the connection. " + text
    public static func catalogueFailure(_ failure: RelayFailure) -> String {
        "Could not load models. Restart the updated PC app and check the connection. " + text(for: failure)
    }

    /// "Could not check the PC model. Keep the updated LiveCue Desktop running. " + text
    public static func pcModelCheckFailure(_ failure: RelayFailure) -> String {
        "Could not check the PC model. Keep the updated LiveCue Desktop running. " + text(for: failure)
    }
}
