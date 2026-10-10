import Foundation
import LiveCueCore
import XCTest

/// Protocol 13.7 as a pure state machine: P2-213 (the `ready` check), P2-214 (errors and closes),
/// P2-215 (the completed rule), P2-217 (refused upgrades) and step 7 (a close whose reason is a code).
final class RelaySpeechNegotiationTests: XCTestCase {
    private let unexpectedFrame = RelayFailure.local(.unexpectedFrame, origin: .stream)
    private let notConfirmed = RelayFailure.local(.speechNotConfirmed, origin: .stream)
    private let streamDropped = RelayFailure.local(.streamDropped, origin: .stream)

    private func ready(_ provider: String, sampleRate: Int = 16_000, handshakeMs: Double = 412.5) -> String {
        #"{"type":"ready","provider":"\#(provider)","sampleRate":\#(sampleRate),"handshakeMs":\#(handshakeMs)}"#
    }

    /// A negotiation that has seen a valid `ready`.
    private func readyNegotiation(_ model: SpeechProvider = .nemotron) throws -> RelaySpeechNegotiation {
        var negotiation = RelaySpeechNegotiation(model: model)
        XCTAssertEqual(try negotiation.receive(text: ready(model.rawValue)), .ready(handshakeMs: 412.5))
        return negotiation
    }

    private func assertThrows(
        _ expected: RelayFailure,
        _ negotiation: inout RelaySpeechNegotiation,
        _ text: String,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        do {
            let step = try negotiation.receive(text: text)
            XCTFail("Expected a failure for \(text), got \(step).", file: file, line: line)
        } catch {
            XCTAssertEqual(error as? RelayFailure, expected, text, file: file, line: line)
        }
    }

    // MARK: - Step 2: waiting for `ready` (P2-213)

    func testANewNegotiationIsNotReadyAndHasNotEnded() {
        let negotiation = RelaySpeechNegotiation(model: .nemotron)
        XCTAssertFalse(negotiation.isReady)
        XCTAssertFalse(negotiation.endStreamSent)
    }

    func testLoadingKeepsWaiting() throws {
        var negotiation = RelaySpeechNegotiation(model: .nemotron)
        for elapsed in [0, 2, 4, 178] {
            let step = try negotiation.receive(
                text: #"{"type":"loading","provider":"nemotron","elapsedSeconds":\#(elapsed),"message":"Loading Nemotron on PC"}"#
            )
            XCTAssertEqual(step, .loading(elapsedSeconds: elapsed))
            XCTAssertFalse(negotiation.isReady, "The microphone stays closed while the model loads.")
        }

        // A `loading` frame with a missing member keeps the wait going too (A-14).
        XCTAssertEqual(try negotiation.receive(text: #"{"type":"loading"}"#), .loading(elapsedSeconds: 0))
        XCTAssertFalse(negotiation.isReady)
    }

    func testAValidReadyOpensTheMicrophoneForEachModel() throws {
        for model in [SpeechProvider.nemotron, .qwen3] {
            var negotiation = RelaySpeechNegotiation(model: model)
            _ = try negotiation.receive(text: #"{"type":"loading","provider":"\#(model.rawValue)","elapsedSeconds":0,"message":"Loading"}"#)
            XCTAssertEqual(try negotiation.receive(text: ready(model.rawValue, handshakeMs: 250)), .ready(handshakeMs: 250))
            XCTAssertTrue(negotiation.isReady, model.rawValue)
            XCTAssertFalse(negotiation.endStreamSent)
        }
    }

    func testAReadyNamingAnotherProviderIsNotConfirmed() {
        var negotiation = RelaySpeechNegotiation(model: .nemotron)
        assertThrows(notConfirmed, &negotiation, ready("qwen3"))
        XCTAssertFalse(negotiation.isReady, "No audio may be sent.")

        var other = RelaySpeechNegotiation(model: .qwen3)
        assertThrows(notConfirmed, &other, ready("nemotron"))
        assertThrows(notConfirmed, &other, ready("Qwen3"))
        assertThrows(notConfirmed, &other, ready(""))
        XCTAssertFalse(other.isReady)
    }

    func testAReadyNamingAnotherSampleRateIsNotConfirmed() {
        for sampleRate in [24_000, 8_000, 0, 16_001] {
            var negotiation = RelaySpeechNegotiation(model: .nemotron)
            assertThrows(notConfirmed, &negotiation, ready("nemotron", sampleRate: sampleRate))
            XCTAssertFalse(negotiation.isReady, "\(sampleRate)")
        }
    }

    func testAReadyWithAMissingMemberIsNotAReady() throws {
        // Such a frame parses as an unknown one: it is ignored and the wait goes on.
        var negotiation = RelaySpeechNegotiation(model: .nemotron)
        XCTAssertEqual(try negotiation.receive(text: #"{"type":"ready","provider":"nemotron","sampleRate":16000}"#), .ignore)
        XCTAssertFalse(negotiation.isReady)
    }

    // MARK: - Before `ready`: unknown types are ignored, lab types are unexpected (A-03)

    func testBeforeReadyAnUnknownTypeIsIgnored() throws {
        var negotiation = RelaySpeechNegotiation(model: .nemotron)
        for text in [#"{"type":"somethingNew","value":1}"#, #"{"type":"audioProgress","audioProcessedMs":0}"#, #"{"type":"speechEnd"}"#] {
            XCTAssertEqual(try negotiation.receive(text: text), .ignore, text)
            XCTAssertFalse(negotiation.isReady)
        }
        // The wait is intact: a valid `ready` still opens the microphone.
        XCTAssertEqual(try negotiation.receive(text: ready("nemotron")), .ready(handshakeMs: 412.5))
        XCTAssertTrue(negotiation.isReady)
    }

    func testBeforeReadyEachOfTheFourLabTypesIsUnexpected() {
        let labFrames = [
            #"{"type":"speechStart","turnId":1,"audioProcessedMs":0}"#,
            #"{"type":"transcript","turnId":1,"audioProcessedMs":80,"transcript":"Local words"}"#,
            #"{"type":"speechComplete","turnId":1,"audioProcessedMs":80,"transcript":"Local words"}"#,
            #"{"type":"streamComplete","audioProcessedMs":80}"#
        ]
        for text in labFrames {
            var negotiation = RelaySpeechNegotiation(model: .nemotron)
            assertThrows(unexpectedFrame, &negotiation, text)
            XCTAssertFalse(negotiation.isReady)

            // The same after `loading`: only `ready` opens the stream.
            var loading = RelaySpeechNegotiation(model: .nemotron)
            _ = try? loading.receive(text: #"{"type":"loading","provider":"nemotron","elapsedSeconds":2,"message":"Loading"}"#)
            assertThrows(unexpectedFrame, &loading, text)
        }
    }

    func testTextThatIsNotAFrameIsUnexpected() throws {
        let notFrames = ["", "not JSON", "[]", "{}", #"{"type":5}"#, #"{"provider":"nemotron","sampleRate":16000}"#]
        for text in notFrames {
            var waiting = RelaySpeechNegotiation(model: .nemotron)
            assertThrows(unexpectedFrame, &waiting, text)

            var streaming = try readyNegotiation()
            assertThrows(unexpectedFrame, &streaming, text)
        }
    }

    // MARK: - `error` frames (P2-214)

    func testAnErrorFrameBeforeReadyFailsWithThatCode() {
        for code in [RelayWireCode.speechBusy, .relayPaused, .modelLoadFailed, .modelLoadTimeout, .invalidSpeechModel, .protocolMismatch] {
            var negotiation = RelaySpeechNegotiation(model: .nemotron)
            assertThrows(
                RelayFailure.wire(code, origin: .stream),
                &negotiation,
                #"{"type":"error","code":"\#(code.rawValue)","message":"The relay's own text."}"#
            )
            XCTAssertFalse(negotiation.isReady)
        }
    }

    func testAnErrorFrameAfterReadyEndsWithThatCode() throws {
        for code in [RelayWireCode.labDisconnected, .audioIdleTimeout, .finalizeTimeout, .sessionLimit, .pairingChanged, .relayStopping] {
            var negotiation = try readyNegotiation()
            assertThrows(
                RelayFailure.wire(code, origin: .stream),
                &negotiation,
                #"{"type":"error","code":"\#(code.rawValue)","message":"The relay's own text."}"#
            )
        }
    }

    func testAnErrorFrameFailureIsAStreamFailureWithTheCodeAsReceived() {
        var negotiation = RelaySpeechNegotiation(model: .nemotron)
        do {
            _ = try negotiation.receive(text: #"{"type":"error","code":"SPEECH_BUSY","message":"Another speech stream is active."}"#)
            XCTFail("An error frame fails.")
        } catch {
            let failure = error as? RelayFailure
            XCTAssertEqual(failure?.kind, .wire("SPEECH_BUSY"))
            XCTAssertEqual(failure?.origin, .stream)
            XCTAssertEqual(failure?.wireCode, .speechBusy)
            XCTAssertNil(failure?.httpStatus)
        }
    }

    func testAnUnknownCodeKeepsTheRelaysText() {
        // Protocol 14: for a code the phone does not know it shows the relay's text.
        var negotiation = RelaySpeechNegotiation(model: .nemotron)
        assertThrows(
            RelayFailure(kind: .wire("SOMETHING_NEW"), origin: .stream, relayText: "A newer relay said this."),
            &negotiation,
            #"{"type":"error","code":"SOMETHING_NEW","message":"A newer relay said this."}"#
        )
    }

    // MARK: - Step 4: while streaming

    func testLabFramesBecomeTranscriptEvents() throws {
        var negotiation = try readyNegotiation()

        XCTAssertEqual(
            try negotiation.receive(text: #"{"type":"speechStart","turnId":1,"audioProcessedMs":120}"#),
            .event(.speechStart(turn: 1, processedMs: 120), audioProcessedMs: 120, hasText: false)
        )
        XCTAssertEqual(
            try negotiation.receive(text: #"{"type":"transcript","turnId":1,"audioProcessedMs":880,"transcript":"Local words"}"#),
            .event(.transcript(turn: 1, text: "Local words", processedMs: 880), audioProcessedMs: 880, hasText: true)
        )
        XCTAssertEqual(
            try negotiation.receive(text: #"{"type":"speechComplete","turnId":1,"audioProcessedMs":1600,"transcript":"Local words."}"#),
            .event(.speechComplete(turn: 1, text: "Local words.", processedMs: 1_600), audioProcessedMs: 1_600, hasText: true)
        )
        XCTAssertEqual(
            try negotiation.receive(text: #"{"type":"streamComplete","audioProcessedMs":1600,"finalizationMs":35}"#),
            .streamComplete(finalizationMs: 35)
        )
        XCTAssertEqual(try negotiation.receive(text: #"{"type":"streamComplete","audioProcessedMs":1600}"#), .streamComplete(finalizationMs: nil))
    }

    func testAnAbsentPositionIsZeroForTheStateMachineAndAbsentForUsage() throws {
        var negotiation = try readyNegotiation()

        // The fake lab of protocol 15.6 answers audio with exactly this.
        XCTAssertEqual(
            try negotiation.receive(text: #"{"type":"transcript","transcript":"Local words","private":"hidden"}"#),
            .event(.transcript(turn: nil, text: "Local words", processedMs: 0), audioProcessedMs: nil, hasText: true)
        )
        XCTAssertEqual(
            try negotiation.receive(text: #"{"type":"speechStart"}"#),
            .event(.speechStart(turn: nil, processedMs: 0), audioProcessedMs: nil, hasText: false)
        )
    }

    func testOnlyNonEmptyTextCountsAsText() throws {
        var negotiation = try readyNegotiation()
        XCTAssertEqual(
            try negotiation.receive(text: #"{"type":"transcript","turnId":1,"audioProcessedMs":10,"transcript":""}"#),
            .event(.transcript(turn: 1, text: "", processedMs: 10), audioProcessedMs: 10, hasText: false)
        )
        XCTAssertEqual(
            try negotiation.receive(text: #"{"type":"speechComplete","turnId":1,"audioProcessedMs":20}"#),
            .event(.speechComplete(turn: 1, text: nil, processedMs: 20), audioProcessedMs: 20, hasText: false)
        )
        XCTAssertEqual(
            try negotiation.receive(text: #"{"type":"transcript","turnId":2,"transcript":17}"#),
            .event(.transcript(turn: 2, text: nil, processedMs: 0), audioProcessedMs: nil, hasText: false)
        )
    }

    func testWhileStreamingUnknownTypesAndUnknownMembersAreIgnored() throws {
        // P2-218
        var negotiation = try readyNegotiation()
        for text in [#"{"type":"somethingNew","value":1}"#, #"{"type":"audioProgress","audioProcessedMs":80}"#, #"{"type":"error","code":"NO_MESSAGE"}"#] {
            XCTAssertEqual(try negotiation.receive(text: text), .ignore, text)
        }
        XCTAssertEqual(
            try negotiation.receive(text: #"{"type":"speechComplete","turnId":4,"audioProcessedMs":80,"transcript":"Local words","private":"hidden"}"#),
            .event(.speechComplete(turn: 4, text: "Local words", processedMs: 80), audioProcessedMs: 80, hasText: true)
        )
        XCTAssertTrue(negotiation.isReady)
    }

    func testALateLoadingOrASecondReadyChangesNothing() throws {
        var negotiation = try readyNegotiation()
        XCTAssertEqual(try negotiation.receive(text: #"{"type":"loading","provider":"nemotron","elapsedSeconds":9,"message":"Loading"}"#), .ignore)
        XCTAssertEqual(try negotiation.receive(text: ready("nemotron")), .ignore)
        XCTAssertTrue(negotiation.isReady)
    }

    // MARK: - Step 6: the completed rule (P2-215)

    func testMarkingEndStreamSent() throws {
        var negotiation = try readyNegotiation()
        XCTAssertFalse(negotiation.endStreamSent)
        negotiation.markEndStreamSent()
        XCTAssertTrue(negotiation.endStreamSent)
        XCTAssertTrue(negotiation.isReady)
    }

    func testCompletedOnlyWhenEndStreamWasSentAndTheCloseIs1000InsideTheWindow() throws {
        var negotiation = try readyNegotiation()

        // The relay closed normally, but the phone had not asked the stream to end.
        XCTAssertEqual(negotiation.ending(closeCode: 1000, closeReason: "", insideWindow: true), .interrupted(streamDropped))

        negotiation.markEndStreamSent()
        XCTAssertEqual(negotiation.ending(closeCode: 1000, closeReason: "", insideWindow: true), .completed)

        // Any other close code, and a 1000 that came too late, are interrupted.
        XCTAssertEqual(negotiation.ending(closeCode: 1011, closeReason: "", insideWindow: true), .interrupted(streamDropped))
        XCTAssertEqual(negotiation.ending(closeCode: 1001, closeReason: "", insideWindow: true), .interrupted(streamDropped))
        XCTAssertEqual(negotiation.ending(closeCode: 1006, closeReason: "", insideWindow: true), .interrupted(streamDropped))
        XCTAssertEqual(negotiation.ending(closeCode: 1000, closeReason: "", insideWindow: false), .interrupted(streamDropped))
    }

    func testADropIsInterruptedWithStreamDropped() throws {
        var negotiation = RelaySpeechNegotiation(model: .qwen3)
        XCTAssertEqual(negotiation.endingAfterDrop(), .interrupted(streamDropped))

        negotiation = try readyNegotiation(.qwen3)
        XCTAssertEqual(negotiation.endingAfterDrop(), .interrupted(streamDropped))

        // Also after `endStream`: a drop is never a clean end.
        negotiation.markEndStreamSent()
        XCTAssertEqual(negotiation.endingAfterDrop(), .interrupted(streamDropped))
    }

    func testWhenC60PassesTheConditionIsStreamDropped() throws {
        var negotiation = try readyNegotiation()
        negotiation.markEndStreamSent()
        XCTAssertEqual(negotiation.ending(closeCode: 1000, closeReason: "", insideWindow: false), .interrupted(streamDropped))
        XCTAssertEqual(
            negotiation.ending(closeCode: 1011, closeReason: "FINALIZE_TIMEOUT", insideWindow: false),
            .interrupted(streamDropped),
            "After C-60 the phone has stopped listening: what arrives then is not read."
        )
    }

    // MARK: - Step 7: a close without an `error` frame

    func testACloseWhoseReasonIsAKnownCodeUsesThatCode() throws {
        var negotiation = try readyNegotiation()
        let rows: [(code: Int, reason: RelayWireCode)] = [
            (1011, .finalizeTimeout), (1011, .labDisconnected), (1011, .audioIdleTimeout),
            (1008, .speechBusy), (1008, .relayPaused), (1009, .frameTooLarge), (1001, .relayStopping)
        ]
        for row in rows {
            XCTAssertEqual(
                negotiation.ending(closeCode: row.code, closeReason: row.reason.rawValue, insideWindow: true),
                .interrupted(RelayFailure.wire(row.reason, origin: .stream)),
                row.reason.rawValue
            )
        }

        // The same after `endStream`, and before `ready`.
        negotiation.markEndStreamSent()
        XCTAssertEqual(
            negotiation.ending(closeCode: 1011, closeReason: "FINALIZE_TIMEOUT", insideWindow: true),
            .interrupted(RelayFailure.wire(.finalizeTimeout, origin: .stream))
        )
        let waiting = RelaySpeechNegotiation(model: .nemotron)
        XCTAssertEqual(
            waiting.ending(closeCode: 1008, closeReason: "SPEECH_BUSY", insideWindow: true),
            .interrupted(RelayFailure.wire(.speechBusy, origin: .stream))
        )
    }

    func testACloseWithAnyOtherReasonIsStreamDropped() throws {
        let negotiation = try readyNegotiation()
        for reason in ["", "going away", "speech_busy", "SOMETHING_NEW", " SPEECH_BUSY", "SPEECH_BUSY\n", "L-TIMEOUT"] {
            XCTAssertEqual(
                negotiation.ending(closeCode: 1011, closeReason: reason, insideWindow: true),
                .interrupted(streamDropped),
                reason.debugDescription
            )
        }
    }

    func testEveryKnownCodeIsRecognisedAsACloseReason() throws {
        let negotiation = try readyNegotiation()
        for code in RelayWireCode.allCases {
            XCTAssertEqual(
                negotiation.ending(closeCode: 1011, closeReason: code.rawValue, insideWindow: true),
                .interrupted(RelayFailure.wire(code, origin: .stream)),
                code.rawValue
            )
        }
    }

    // MARK: - Step 1: opening (P2-217)

    func testARefusedUpgradeIsClassifiedByItsStatus() {
        XCTAssertEqual(RelaySpeechNegotiation.failure(forUpgradeStatus: 401), RelayFailure.wire(.unauthorized, origin: .stream))
        XCTAssertEqual(RelaySpeechNegotiation.failure(forUpgradeStatus: 404), RelayFailure.wire(.notFound, origin: .stream))
        XCTAssertEqual(RelaySpeechNegotiation.failure(forUpgradeStatus: 429), RelayFailure.wire(.tooManyRequests, origin: .stream))
        for status in [502, 503, 504] {
            XCTAssertEqual(
                RelaySpeechNegotiation.failure(forUpgradeStatus: status),
                RelayFailure.local(.noRelay, origin: .stream),
                "\(status)"
            )
        }
    }

    func testAnyOtherUpgradeStatusIsAnHTTPConditionWithTheStatus() {
        for status in [200, 301, 302, 400, 403, 426, 500, 501, 505] {
            let failure = RelaySpeechNegotiation.failure(forUpgradeStatus: status)
            XCTAssertEqual(failure, RelayFailure.local(.http, origin: .stream, httpStatus: status), "\(status)")
            XCTAssertTrue(failure.isLocal(.http))
            XCTAssertEqual(failure.httpStatus, status)
        }
    }

    func testATransportFailureIsClassifiedAsRows4To6() {
        XCTAssertEqual(RelaySpeechNegotiation.failure(for: .tls), RelayFailure.local(.tls, origin: .stream))
        XCTAssertEqual(RelaySpeechNegotiation.failure(for: .unreachable), RelayFailure.local(.unreachable, origin: .stream))
        XCTAssertEqual(RelaySpeechNegotiation.failure(for: .timedOut), RelayFailure.local(.unreachable, origin: .stream))
        XCTAssertEqual(RelaySpeechNegotiation.failure(for: .other), RelayFailure.local(.network, origin: .stream))
    }

    func testEveryFailureOfTheStreamHasTheStreamOrigin() {
        for status in [401, 404, 429, 500, 503] {
            XCTAssertEqual(RelaySpeechNegotiation.failure(forUpgradeStatus: status).origin, .stream, "\(status)")
        }
        let transportFailures: [TransportFailure] = [.tls, .unreachable, .timedOut, .other, .cancelled]
        for failure in transportFailures {
            XCTAssertEqual(RelaySpeechNegotiation.failure(for: failure).origin, .stream, "\(failure)")
        }
    }
}
