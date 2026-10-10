import Foundation
import LiveCueCore
import XCTest

/// The phone text of every code and condition, and the compositions of protocol 14.3.
///
/// P2-206 of protocol 15.9. The expected texts are those of protocol 14.1 and 14.2, column "The phone
/// shows".
final class RelayMessagesTests: XCTestCase {
    // MARK: - Protocol 14.1

    private static let wireTexts: [(code: RelayWireCode, text: String)] = [
        (.unauthorized, "The PC no longer recognizes this pairing. Scan its pairing QR once to reconnect."),
        (.notFound, "LiveCue Desktop does not offer this function. Update LiveCue Desktop, then retry."),
        (.protocolMismatch, "This iPhone app and LiveCue Desktop are different versions. Update both, then retry."),
        (.invalidRequest, "LiveCue Desktop could not read this request. Update both apps, then retry."),
        (.invalidJSON, "LiveCue Desktop could not read this request. Update both apps, then retry."),
        (.invalidRequestID, "LiveCue Desktop could not read this request. Update both apps, then retry."),
        (.invalidAssistant, "LiveCue Desktop could not read this request. Update both apps, then retry."),
        (.payloadTooLarge, "This request was too large for your PC. Retry after a shorter stretch of conversation."),
        (.requestTimeout, "The request reached your PC too slowly. Check the connection and retry."),
        (.tooManyRequests, "Your PC is handling too many requests. Wait a moment and retry."),
        (.deadlineExceeded, "Your PC took too long to answer. Retry in a moment."),
        (.internalError, "LiveCue Desktop hit an internal error. Retry, and restart LiveCue Desktop if it keeps happening."),
        (.relayPaused, "PC relay is paused. Resume it in LiveCue Desktop."),
        (.assistantBusy, "The PC is processing another request. Try again shortly."),
        (.assistantCatalogUnavailable, "The PC could not read Codex's model list. Keep Codex signed in, then retry. Your selected model has not changed."),
        (.assistantSelectionUnavailable, "The selected assistant model or reasoning level is not in this PC's current Codex catalog. Open Assistant models & timing and choose an available combination."),
        (.assistantFailed, "Codex on your PC could not finish this answer. Check that Codex is signed in on the PC, then retry."),
        (.assistantTimeout, "Codex on your PC took too long to answer. Retry, or choose a faster model or a lower reasoning level."),
        (.requestCancelled, "This request was cancelled."),
        // "Never shown" in protocol 14.1: it only answers a cancel. The relay's own sentence stands in.
        (.requestNotFound, "No request with that id is in progress."),
        (.invalidModel, "Choose Nemotron or Qwen3."),
        (.labNotConfigured, "PC speech models are not set up on this PC. Set the speech lab folder in LiveCue Desktop."),
        (.modelOperationInProgress, "A PC model operation is already in progress. Wait for it to finish."),
        (.modelBusy, "The PC model is in use. Stop the other conversation or lab test, then retry."),
        (.modelMismatch, "A different PC model is loaded. Select it before stopping."),
        (.invalidSpeechModel, "Your PC does not offer the selected speech model. Update LiveCue Desktop, then retry."),
        (.speechBusy, "Your PC is already transcribing another conversation. End that one first, then retry."),
        (.modelLoadFailed, "Could not load the PC model. Start Docker Desktop, stop any lab test, and retry."),
        (.modelLoadTimeout, "Model is still loading. Open Settings and wait for PC model status to become Ready, then retry."),
        (.modelNotReady, "The selected PC model is not ready or is busy. Retry after the other test stops."),
        (.labConnectionFailed, "Local model connection failed. Check Docker Desktop and retry."),
        (.labInferenceFailed, "PC speech inference failed. Check the local model service and retry."),
        (.labProtocolError, "The PC speech service sent an unexpected response. Resume to retry."),
        (.labDisconnected, "Local model disconnected. Resume to reconnect."),
        (.audioOutOfSequence, "The audio stream got out of step with your PC. Resume to reconnect."),
        (.audioLimit, "Audio arrived faster than your PC could accept it. Resume to reconnect."),
        (.frameTooLarge, "Audio arrived faster than your PC could accept it. Resume to reconnect."),
        (.unsupportedCommand, "PC transcription stopped unexpectedly. Resume to reconnect."),
        (.audioIdleTimeout, "Audio stream timed out. Resume to reconnect."),
        (.finalizeTimeout, "Local model did not finalize in time. Some final words may be missing."),
        (.sessionLimit, "30-minute session limit reached. Pause and resume to continue."),
        (.pairingChanged, "The pairing was reset on your PC. Scan its pairing QR to reconnect."),
        (.relayStopping, "LiveCue Desktop stopped its relay. Start it again, then resume.")
    ]

    // MARK: - Protocol 14.2

    /// Every condition but L-HTTP, whose text holds the status.
    private static let localTexts: [(condition: RelayLocalCondition, text: String)] = [
        (.tls, "The secure connection to your PC failed. Check the PC tunnel and retry. Scanning the QR again will not repair this connection."),
        (.unreachable, "Your PC is currently unreachable. Keep LiveCue Desktop running and check your internet connection. The app will reconnect automatically."),
        (.network, "Could not reach your PC because of a network error. Check the connection and retry."),
        (.timeout, "Your PC did not answer in time. Check that LiveCue Desktop is running, then retry."),
        (.noRelay, "Your PC answered, but the LiveCue relay is not running. Open LiveCue Desktop and start the relay."),
        (.unreadable, "The relay returned an unreadable response."),
        (.badEndpoint, "The relay address is invalid."),
        (.notPaired, "Pair this iPhone with the Windows relay first."),
        (.notConfirmed, "The PC did not confirm the selected model. Restart the updated LiveCue Desktop app, then retry."),
        (.speechNotConfirmed, "PC did not confirm the selected speech model. Update/restart LiveCue Desktop and its tunnel."),
        (.unexpectedFrame, "PC transcription is not ready."),
        (.streamDropped, "The connection to your PC dropped. Resume to reconnect.")
    ]

    private static let streamDroppedText = "The connection to your PC dropped. Resume to reconnect."

    // MARK: - P2-206: every code

    func testEveryWireCodeYieldsItsPhoneText() {
        // The table covers every case exactly once.
        XCTAssertEqual(Self.wireTexts.count, RelayWireCode.allCases.count)
        XCTAssertEqual(Set(Self.wireTexts.map { $0.code }), Set(RelayWireCode.allCases))

        for (code, text) in Self.wireTexts {
            XCTAssertEqual(RelayMessages.text(for: code), text, code.rawValue)
            // The same text whichever way the code arrived, and whatever the relay said with it.
            XCTAssertEqual(RelayMessages.text(for: RelayFailure.wire(code)), text, code.rawValue)
            XCTAssertEqual(RelayMessages.text(for: RelayFailure.wire(code, origin: .stream)), text, code.rawValue)
            let received = RelayFailure(
                kind: .wire(code.rawValue),
                origin: .http,
                httpStatus: 400,
                relayText: "The relay's own sentence."
            )
            XCTAssertEqual(RelayMessages.text(for: received), text, code.rawValue)
        }
    }

    func testEveryLocalConditionYieldsItsPhoneText() {
        XCTAssertEqual(Self.localTexts.count + 1, RelayLocalCondition.allCases.count)
        XCTAssertEqual(Set(Self.localTexts.map { $0.condition } + [RelayLocalCondition.http]), Set(RelayLocalCondition.allCases))

        for (condition, text) in Self.localTexts {
            XCTAssertEqual(RelayMessages.text(for: condition), text, condition.rawValue)
            // The status changes nothing outside L-HTTP.
            XCTAssertEqual(RelayMessages.text(for: condition, httpStatus: 503), text, condition.rawValue)
            XCTAssertEqual(RelayMessages.text(for: RelayFailure.local(condition)), text, condition.rawValue)
            XCTAssertEqual(RelayMessages.text(for: RelayFailure.local(condition, origin: .stream)), text, condition.rawValue)
        }
    }

    func testLHTTPShowsTheStatus() {
        XCTAssertEqual(RelayMessages.text(for: .http, httpStatus: 418), "Relay error (418).")
        XCTAssertEqual(RelayMessages.text(for: RelayFailure.local(.http, httpStatus: 500)), "Relay error (500).")
        XCTAssertEqual(RelayMessages.text(for: RelayFailure.local(.http, httpStatus: 302)), "Relay error (302).")
        XCTAssertEqual(
            RelayMessages.text(for: RelayFailure.local(.http, origin: .stream, httpStatus: 426)),
            "Relay error (426)."
        )
        // A caller that has no status still gets a text, and it invents no number.
        XCTAssertEqual(RelayMessages.text(for: RelayLocalCondition.http), "Relay error.")
    }

    // MARK: - P2-206: a code this build does not know

    func testAnUnknownCodeYieldsTheRelaysText() {
        let texts = ["x", "Later versions explain this.", String(repeating: "a", count: 300), String(repeating: "\u{1F600}", count: 300)]
        for text in texts {
            let http = RelayFailure(kind: .wire("FUTURE_CODE"), origin: .http, httpStatus: 400, relayText: text)
            XCTAssertEqual(RelayMessages.text(for: http), text)

            let stream = RelayFailure(kind: .wire("FUTURE_CODE"), origin: .stream, relayText: text)
            XCTAssertEqual(RelayMessages.text(for: stream), text)
        }
    }

    func testAnUnknownCodeWithoutAUsableRelayTextYieldsLHTTPOrStreamDropped() {
        let unusable: [String?] = [nil, "", String(repeating: "a", count: 301)]
        for text in unusable {
            let name = text.map { "\($0.count) characters" } ?? "no text"

            let http = RelayFailure(kind: .wire("FUTURE_CODE"), origin: .http, httpStatus: 418, relayText: text)
            XCTAssertEqual(RelayMessages.text(for: http), "Relay error (418).", name)

            let stream = RelayFailure(kind: .wire("FUTURE_CODE"), origin: .stream, relayText: text)
            XCTAssertEqual(RelayMessages.text(for: stream), Self.streamDroppedText, name)
        }
    }

    func testACodeIsMatchedExactly() {
        // Codes are case-sensitive identifiers: another spelling is another, unknown, code.
        for code in ["unauthorized", "Unauthorized", "UNAUTHORIZED ", " UNAUTHORIZED", "", "L-TLS"] {
            let failure = RelayFailure(kind: .wire(code), origin: .http, httpStatus: 401, relayText: "From the relay.")
            XCTAssertEqual(RelayMessages.text(for: failure), "From the relay.", code)
        }
    }

    // MARK: - P2-206: no text contains the token

    func testNoTextContainsTheToken() {
        let token = CoreFixtures.token
        // A relay that echoed the token in its sentence: the phone's own text is shown for a known code.
        let leaking = "Invalid pairing token " + token + "."

        var texts: [String] = []
        for code in RelayWireCode.allCases {
            texts.append(RelayMessages.text(for: code))
            for origin in [RelayFailure.Origin.http, RelayFailure.Origin.stream] {
                let failure = RelayFailure(kind: .wire(code.rawValue), origin: origin, httpStatus: 401, relayText: leaking)
                texts.append(RelayMessages.text(for: failure))
                texts.append(RelayMessages.savedPairingHealthFailure(failure))
                texts.append(RelayMessages.catalogueFailure(failure))
                texts.append(RelayMessages.pcModelCheckFailure(failure))
                texts.append(RelayMessages.startFailure(RelayMessages.text(for: failure)))
            }
        }
        for condition in RelayLocalCondition.allCases {
            texts.append(RelayMessages.text(for: condition))
            texts.append(RelayMessages.text(for: condition, httpStatus: 502))
            var failure = RelayFailure.local(condition, httpStatus: 502)
            failure.relayText = leaking
            texts.append(RelayMessages.text(for: failure))
            texts.append(RelayMessages.savedPairingHealthFailure(failure))
            texts.append(RelayMessages.catalogueFailure(failure))
            texts.append(RelayMessages.pcModelCheckFailure(failure))
        }

        XCTAssertEqual(texts.count, 43 * 11 + 13 * 6)
        for text in texts {
            XCTAssertFalse(text.isEmpty)
            XCTAssertFalse(text.contains(token), text)
            XCTAssertFalse(text.contains("Bearer"), text)
        }
    }

    // MARK: - Protocol 14.3

    func testAStartFailureIsPrefixed() {
        XCTAssertEqual(
            RelayMessages.startFailure("PC transcription is not ready."),
            "Transcription could not start. PC transcription is not ready."
        )
        XCTAssertEqual(
            RelayMessages.startFailure(RelayMessages.text(for: RelayFailure.wire(.speechBusy, origin: .stream))),
            "Transcription could not start. Your PC is already transcribing another conversation. End that one first, then retry."
        )
    }

    func testAHealthFailureOfTheSavedPairingIsPrefixedForTheFiveConnectionConditions() {
        let prefixed: [RelayLocalCondition] = [.tls, .unreachable, .network, .timeout, .noRelay]
        for condition in RelayLocalCondition.allCases {
            let failure = RelayFailure.local(condition, httpStatus: 502)
            let text = RelayMessages.text(for: failure)
            if prefixed.contains(condition) {
                XCTAssertEqual(
                    RelayMessages.savedPairingHealthFailure(failure),
                    "Your pairing is saved. " + text,
                    condition.rawValue
                )
            } else {
                XCTAssertEqual(RelayMessages.savedPairingHealthFailure(failure), text, condition.rawValue)
            }
        }
        XCTAssertEqual(
            RelayMessages.savedPairingHealthFailure(RelayFailure.local(.unreachable)),
            "Your pairing is saved. Your PC is currently unreachable. Keep LiveCue Desktop running and check your internet connection. The app will reconnect automatically."
        )
    }

    func testAHealthFailureWithAWireCodeIsNeverPrefixed() {
        // IOS-B10: never for UNAUTHORIZED, and for no other code either.
        for code in RelayWireCode.allCases {
            let failure = RelayFailure.wire(code)
            XCTAssertEqual(RelayMessages.savedPairingHealthFailure(failure), RelayMessages.text(for: code), code.rawValue)
        }
        XCTAssertEqual(
            RelayMessages.savedPairingHealthFailure(RelayFailure.wire(.unauthorized)),
            "The PC no longer recognizes this pairing. Scan its pairing QR once to reconnect."
        )
        let unknown = RelayFailure(kind: .wire("FUTURE_CODE"), origin: .http, httpStatus: 503, relayText: "Later.")
        XCTAssertEqual(RelayMessages.savedPairingHealthFailure(unknown), "Later.")
    }

    func testACatalogueFailureIsPrefixed() {
        XCTAssertEqual(
            RelayMessages.catalogueFailure(RelayFailure.local(.timeout)),
            "Could not load models. Restart the updated PC app and check the connection. Your PC did not answer in time. Check that LiveCue Desktop is running, then retry."
        )
        XCTAssertEqual(
            RelayMessages.catalogueFailure(RelayFailure.wire(.assistantCatalogUnavailable)),
            "Could not load models. Restart the updated PC app and check the connection. The PC could not read Codex's model list. Keep Codex signed in, then retry. Your selected model has not changed."
        )
    }

    func testAPCModelCheckFailureIsPrefixed() {
        XCTAssertEqual(
            RelayMessages.pcModelCheckFailure(RelayFailure.local(.noRelay, httpStatus: 502)),
            "Could not check the PC model. Keep the updated LiveCue Desktop running. Your PC answered, but the LiveCue relay is not running. Open LiveCue Desktop and start the relay."
        )
        XCTAssertEqual(
            RelayMessages.pcModelCheckFailure(RelayFailure.local(.http, httpStatus: 500)),
            "Could not check the PC model. Keep the updated LiveCue Desktop running. Relay error (500)."
        )
    }
}
