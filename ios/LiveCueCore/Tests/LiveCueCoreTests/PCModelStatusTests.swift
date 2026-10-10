import Foundation
import LiveCueCore
import XCTest

/// Decoding, readiness and the home labels of the PC model status.
final class PCModelStatusTests: XCTestCase {
    private let readyJSON = "{\"state\":\"ready\",\"model\":\"nemotron\",\"busy\":false,\"elapsedSeconds\":0,\"message\":\"Ready\"}"

    private func decode(_ json: String) throws -> PCModelStatus {
        try LiveCueJSON.decode(PCModelStatus.self, from: Data(json.utf8))
    }

    // MARK: - Home labels (spec 02 section 8.2)

    /// AC-CORE-070 to AC-CORE-075.
    func testHomeStatusDistinguishesLoadingReadyBusyStoppedAndFailure() {
        XCTAssertEqual(
            PCModelStatus(state: "starting", model: "nemotron", elapsedSeconds: 42, message: "Loading").homeLabel(for: .nemotron),
            "Loading \u{00B7} 42s"
        )
        XCTAssertEqual(PCModelStatus(state: "ready", model: "nemotron", message: "Loaded").homeLabel(for: .nemotron), "Ready")
        XCTAssertEqual(
            PCModelStatus(state: "ready", model: "nemotron", busy: true, message: "Listening").homeLabel(for: .nemotron),
            "In use"
        )
        XCTAssertEqual(
            PCModelStatus(state: "ready", model: "qwen3", message: "Loaded").homeLabel(for: .nemotron),
            "Different model loaded"
        )
        XCTAssertEqual(
            PCModelStatus(state: "error", model: "nemotron", message: "Failed").homeLabel(for: .nemotron),
            "Error \u{00B7} open Settings"
        )
        XCTAssertEqual(PCModelStatus(state: "stopped", message: "Stopped").homeLabel(for: .nemotron), "Stopped \u{00B7} open Settings")
    }

    /// AC-CORE-S18.
    func testStoppingUnknownStatesAndADifferentModelThatIsStopped() {
        XCTAssertEqual(
            PCModelStatus(state: "stopping", model: "nemotron", elapsedSeconds: 7, message: "Stopping").homeLabel(for: .nemotron),
            "Stopping \u{00B7} 7s"
        )
        XCTAssertEqual(
            PCModelStatus(state: "warming", model: "nemotron", message: "?").homeLabel(for: .nemotron),
            "Stopped \u{00B7} open Settings"
        )
        XCTAssertEqual(PCModelStatus(state: "", message: "").homeLabel(for: .qwen3), "Stopped \u{00B7} open Settings")
        XCTAssertEqual(
            PCModelStatus(state: "READY", model: "nemotron", message: "Loaded").homeLabel(for: .nemotron),
            "Stopped \u{00B7} open Settings",
            "States are compared exactly."
        )
        XCTAssertEqual(
            PCModelStatus(state: "stopped", model: "qwen3", message: "Stopped").homeLabel(for: .nemotron),
            "Stopped \u{00B7} open Settings"
        )
    }

    func testADifferentModelComesBeforeEveryOtherRow() {
        for state in ["starting", "stopping", "ready", "error", "warming"] {
            let status = PCModelStatus(state: state, model: "qwen3", busy: true, elapsedSeconds: 9, message: "m")
            XCTAssertEqual(status.homeLabel(for: .nemotron), "Different model loaded", state)
            XCTAssertNotEqual(status.homeLabel(for: .qwen3), "Different model loaded", state)
        }
        // The same status asked for its own model shows its own row.
        XCTAssertEqual(
            PCModelStatus(state: "starting", model: "qwen3", elapsedSeconds: 9, message: "m").homeLabel(for: .qwen3),
            "Loading \u{00B7} 9s"
        )
        XCTAssertEqual(
            PCModelStatus(state: "error", model: "qwen3", message: "m").homeLabel(for: .qwen3),
            "Error \u{00B7} open Settings"
        )
    }

    func testLabelsWithoutAModel() {
        // CORE-A15: a ready status that names no model reads "Ready" but is ready for no provider.
        let ready = PCModelStatus(state: "ready", message: "Loaded")
        XCTAssertEqual(ready.homeLabel(for: .nemotron), "Ready")
        XCTAssertFalse(ready.isReady(for: .nemotron))
        XCTAssertFalse(ready.isReady(for: .qwen3))

        XCTAssertEqual(PCModelStatus(state: "starting", elapsedSeconds: 3, message: "m").homeLabel(for: .qwen3), "Loading \u{00B7} 3s")
        XCTAssertEqual(PCModelStatus(state: "starting", message: "m").homeLabel(for: .qwen3), "Loading \u{00B7} 0s")
        XCTAssertEqual(PCModelStatus(state: "stopping", elapsedSeconds: 120, message: "m").homeLabel(for: .qwen3), "Stopping \u{00B7} 120s")
    }

    func testBusyOnlyMattersWhenReady() {
        XCTAssertEqual(
            PCModelStatus(state: "starting", model: "nemotron", busy: true, elapsedSeconds: 1, message: "m").homeLabel(for: .nemotron),
            "Loading \u{00B7} 1s"
        )
        XCTAssertEqual(
            PCModelStatus(state: "error", model: "nemotron", busy: true, message: "m").homeLabel(for: .nemotron),
            "Error \u{00B7} open Settings"
        )
        XCTAssertEqual(
            PCModelStatus(state: "stopped", busy: true, message: "m").homeLabel(for: .nemotron),
            "Stopped \u{00B7} open Settings"
        )
    }

    // MARK: - Readiness

    /// AC-CORE-076 to AC-CORE-080, and the first sentence of P2-219.
    func testReadinessRequiresTheSelectedModel() throws {
        let status = try decode(readyJSON)
        XCTAssertEqual(status, PCModelStatus(state: "ready", model: "nemotron", busy: false, elapsedSeconds: 0, message: "Ready"))
        XCTAssertTrue(status.isReady(for: .nemotron))
        XCTAssertFalse(status.isReady(for: .qwen3))
        XCTAssertFalse(status.isChanging)

        XCTAssertTrue(PCModelStatus(state: "starting", model: "nemotron", message: "Loading").isChanging)
        XCTAssertFalse(PCModelStatus(state: "error", model: "nemotron", message: "Failed").isReady(for: .nemotron))

        // The default decoder reads the same object the same way.
        XCTAssertEqual(try JSONDecoder().decode(PCModelStatus.self, from: Data(readyJSON.utf8)), status)
    }

    func testReadinessDoesNotConsiderBusy() {
        let busy = PCModelStatus(state: "ready", model: "qwen3", busy: true, message: "Listening")
        XCTAssertTrue(busy.isReady(for: .qwen3))
        XCTAssertFalse(busy.isReady(for: .nemotron))
    }

    func testOnlyAPCModelCanBeReady() {
        let status = PCModelStatus(state: "ready", model: "nemotron", message: "Loaded")
        for provider in SpeechProvider.allCases {
            XCTAssertEqual(status.isReady(for: provider), provider == .nemotron, provider.rawValue)
        }
        for state in ["stopped", "starting", "stopping", "error", "", "Ready"] {
            XCTAssertFalse(PCModelStatus(state: state, model: "nemotron", message: "m").isReady(for: .nemotron), state)
        }
    }

    func testIsChangingWhileStartingOrStopping() {
        let expected = ["stopped": false, "starting": true, "ready": false, "stopping": true, "error": false, "warming": false]
        for (state, changing) in expected {
            XCTAssertEqual(PCModelStatus(state: state, model: "nemotron", message: "m").isChanging, changing, state)
            XCTAssertEqual(PCModelStatus(state: state, message: "m").isChanging, changing, state)
        }
    }

    // MARK: - The other loaded model

    func testOtherLoadedModel() {
        let qwen = PCModelStatus(state: "ready", model: "qwen3", message: "Loaded")
        XCTAssertEqual(qwen.otherLoadedModel(than: .nemotron), "qwen3")
        XCTAssertNil(qwen.otherLoadedModel(than: .qwen3))
        XCTAssertEqual(qwen.otherLoadedModel(than: .meta), "qwen3")

        for state in ["starting", "stopping", "error", "warming"] {
            let status = PCModelStatus(state: state, model: "nemotron", message: "m")
            XCTAssertEqual(status.otherLoadedModel(than: .qwen3), "nemotron", state)
            XCTAssertNil(status.otherLoadedModel(than: .nemotron), state)
        }

        XCTAssertNil(PCModelStatus(state: "stopped", model: "qwen3", message: "m").otherLoadedModel(than: .nemotron))
        XCTAssertNil(PCModelStatus(state: "stopped", message: "m").otherLoadedModel(than: .nemotron))
        XCTAssertNil(PCModelStatus(state: "ready", message: "m").otherLoadedModel(than: .nemotron))
        XCTAssertEqual(
            PCModelStatus(state: "ready", model: "something-else", message: "m").otherLoadedModel(than: .nemotron),
            "something-else",
            "The id is passed on as the relay wrote it."
        )
    }

    // MARK: - Decoding (P2-219)

    func testAStatusWithANullModelDecodes() throws {
        let status = try decode("{\"state\":\"stopped\",\"model\":null,\"busy\":false,\"elapsedSeconds\":0,\"message\":\"Stopped\"}")
        XCTAssertEqual(status, PCModelStatus(state: "stopped", message: "Stopped"))
        XCTAssertNil(status.model)

        // The same object as P2-219 gives it: only the model differs from the ready status.
        let ready = try decode("{\"state\":\"ready\",\"model\":null,\"busy\":false,\"elapsedSeconds\":0,\"message\":\"Ready\"}")
        XCTAssertNil(ready.model)
        XCTAssertFalse(ready.isReady(for: .nemotron))

        let absent = try decode("{\"state\":\"stopped\",\"busy\":false,\"elapsedSeconds\":0,\"message\":\"Stopped\"}")
        XCTAssertEqual(absent, status)
    }

    func testAStatusMissingBusyElapsedSecondsOrMessageDoesNotDecode() {
        XCTAssertThrowsError(try decode("{\"state\":\"ready\",\"model\":\"nemotron\",\"elapsedSeconds\":0,\"message\":\"Ready\"}"), "busy")
        XCTAssertThrowsError(try decode("{\"state\":\"ready\",\"model\":\"nemotron\",\"busy\":false,\"message\":\"Ready\"}"), "elapsedSeconds")
        XCTAssertThrowsError(try decode("{\"state\":\"ready\",\"model\":\"nemotron\",\"busy\":false,\"elapsedSeconds\":0}"), "message")
        XCTAssertThrowsError(try decode("{\"model\":\"nemotron\",\"busy\":false,\"elapsedSeconds\":0,\"message\":\"Ready\"}"), "state")
        XCTAssertThrowsError(try decode("{}"))

        // The default decoder is as strict.
        let noBusy = Data("{\"state\":\"ready\",\"model\":\"nemotron\",\"elapsedSeconds\":0,\"message\":\"Ready\"}".utf8)
        XCTAssertThrowsError(try JSONDecoder().decode(PCModelStatus.self, from: noBusy))
    }

    func testAMemberOfTheWrongTypeDoesNotDecode() {
        XCTAssertThrowsError(
            try decode("{\"state\":\"ready\",\"model\":\"nemotron\",\"busy\":null,\"elapsedSeconds\":0,\"message\":\"Ready\"}"),
            "busy null"
        )
        XCTAssertThrowsError(
            try decode("{\"state\":\"ready\",\"model\":\"nemotron\",\"busy\":\"false\",\"elapsedSeconds\":0,\"message\":\"Ready\"}"),
            "busy as text"
        )
        XCTAssertThrowsError(
            try decode("{\"state\":\"ready\",\"model\":\"nemotron\",\"busy\":false,\"elapsedSeconds\":\"3\",\"message\":\"Ready\"}"),
            "a number in quotes"
        )
        XCTAssertThrowsError(
            try decode("{\"state\":\"ready\",\"model\":\"nemotron\",\"busy\":false,\"elapsedSeconds\":1.5,\"message\":\"Ready\"}"),
            "elapsedSeconds must be an integer"
        )
        XCTAssertThrowsError(
            try decode("{\"state\":\"ready\",\"model\":\"nemotron\",\"busy\":false,\"elapsedSeconds\":null,\"message\":\"Ready\"}"),
            "elapsedSeconds null"
        )
        XCTAssertThrowsError(
            try decode("{\"state\":\"ready\",\"model\":\"nemotron\",\"busy\":false,\"elapsedSeconds\":0,\"message\":null}"),
            "message null"
        )
        XCTAssertThrowsError(
            try decode("{\"state\":\"ready\",\"model\":7,\"busy\":false,\"elapsedSeconds\":0,\"message\":\"Ready\"}"),
            "model as a number"
        )
        XCTAssertThrowsError(try decode("[]"))
        XCTAssertThrowsError(try decode("not json"))
    }

    func testEveryStateOfTheProtocolDecodesAndUnknownKeysAreIgnored() throws {
        let loading = try decode(
            "{\"state\":\"starting\",\"model\":\"qwen3\",\"busy\":false,\"elapsedSeconds\":42,\"message\":\"Loading the model on the PC GPU.\",\"somethingNew\":[1,2]}"
        )
        XCTAssertEqual(
            loading,
            PCModelStatus(state: "starting", model: "qwen3", busy: false, elapsedSeconds: 42, message: "Loading the model on the PC GPU.")
        )
        XCTAssertEqual(loading.homeLabel(for: .qwen3), "Loading \u{00B7} 42s")
        XCTAssertTrue(loading.isChanging)

        for state in ["stopped", "starting", "ready", "stopping", "error", "a state from the future"] {
            let status = try decode("{\"state\":\"\(state)\",\"model\":\"nemotron\",\"busy\":true,\"elapsedSeconds\":5,\"message\":\"m\"}")
            XCTAssertEqual(status.state, state)
            XCTAssertTrue(status.busy)
            XCTAssertEqual(status.elapsedSeconds, 5)
        }
    }

    func testAStatusRoundTrips() throws {
        let statuses = [
            PCModelStatus(state: "stopped", message: "Stopped"),
            PCModelStatus(state: "starting", model: "nemotron", busy: false, elapsedSeconds: 42, message: "Loading"),
            PCModelStatus(state: "ready", model: "qwen3", busy: true, elapsedSeconds: 0, message: "Listening"),
            PCModelStatus(state: "error", model: "nemotron", message: "Failed \u{00B7} \"quoted\" / slashed")
        ]
        for status in statuses {
            XCTAssertEqual(try decode(String(decoding: LiveCueJSON.encode(status), as: UTF8.self)), status, status.state)
            XCTAssertEqual(try JSONDecoder().decode(PCModelStatus.self, from: JSONEncoder().encode(status)), status, status.state)
        }
    }
}
