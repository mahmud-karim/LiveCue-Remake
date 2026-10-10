import Foundation
import LiveCueCore
import XCTest

/// Each frame type of protocol 10.3, read the way the phone must read it: wrong member types are absent,
/// a `loading` frame survives a missing member (A-14), and unknown types and members are ignored (P2-218).
final class RelaySpeechFrameTests: XCTestCase {
    // MARK: - Each frame type of protocol 10.3

    func testLoading() {
        let frame = RelaySpeechFrame.parse(
            #"{"type":"loading","provider":"nemotron","elapsedSeconds":12,"message":"Loading Nemotron on PC · 12 s. Microphone starts when ready."}"#
        )
        XCTAssertEqual(
            frame,
            .loading(provider: "nemotron", elapsedSeconds: 12, message: "Loading Nemotron on PC · 12 s. Microphone starts when ready.")
        )
    }

    func testReady() {
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"ready","provider":"qwen3","sampleRate":16000,"handshakeMs":412.5}"#),
            .ready(provider: "qwen3", sampleRate: 16_000, handshakeMs: 412.5)
        )
        // A whole number is a number too.
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"ready","provider":"nemotron","sampleRate":16000,"handshakeMs":3}"#),
            .ready(provider: "nemotron", sampleRate: 16_000, handshakeMs: 3)
        )
    }

    func testSpeechStart() {
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"speechStart","turnId":1,"audioProcessedMs":240}"#),
            .speechStart(turnId: 1, audioProcessedMs: 240)
        )
    }

    func testTranscript() {
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"transcript","turnId":1,"audioProcessedMs":880.5,"transcript":"Local words"}"#),
            .transcript(turnId: 1, audioProcessedMs: 880.5, transcript: "Local words")
        )
    }

    func testSpeechComplete() {
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"speechComplete","transcript":"Local words","turnId":1,"audioProcessedMs":80}"#),
            .speechComplete(turnId: 1, audioProcessedMs: 80, transcript: "Local words")
        )
    }

    func testStreamComplete() {
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"streamComplete","audioProcessedMs":80,"finalizationMs":35.25}"#),
            .streamComplete(audioProcessedMs: 80, finalizationMs: 35.25)
        )
    }

    func testError() {
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"error","code":"SPEECH_BUSY","message":"Another speech stream is active."}"#),
            .error(code: "SPEECH_BUSY", message: "Another speech stream is active.")
        )
        // The code is kept exactly as received, also one this build does not know.
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"error","code":"SOMETHING_NEW","message":"A newer relay said this."}"#),
            .error(code: "SOMETHING_NEW", message: "A newer relay said this.")
        )
    }

    func testTextThatIsNotASCIIIsKept() {
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"transcript","turnId":2,"transcript":"Grüße, 世界 👋"}"#),
            .transcript(turnId: 2, audioProcessedMs: nil, transcript: "Grüße, 世界 👋")
        )
    }

    // MARK: - Members that the lab did not send

    func testLabFramesCarryOnlyTheMembersThatWereSent() {
        // Protocol 10.3: each member other than `type` appears only when the lab sent it.
        XCTAssertEqual(RelaySpeechFrame.parse(#"{"type":"speechStart"}"#), .speechStart(turnId: nil, audioProcessedMs: nil))
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"transcript","transcript":"Local words"}"#),
            .transcript(turnId: nil, audioProcessedMs: nil, transcript: "Local words")
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"speechComplete"}"#),
            .speechComplete(turnId: nil, audioProcessedMs: nil, transcript: nil)
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"streamComplete","audioProcessedMs":80}"#),
            .streamComplete(audioProcessedMs: 80, finalizationMs: nil)
        )
        XCTAssertEqual(RelaySpeechFrame.parse(#"{"type":"streamComplete"}"#), .streamComplete(audioProcessedMs: nil, finalizationMs: nil))
    }

    // MARK: - Wrong member types read as absent

    func testMembersOfTheWrongTypeAreReadAsAbsent() {
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"transcript","turnId":"1","audioProcessedMs":"880","transcript":17}"#),
            .transcript(turnId: nil, audioProcessedMs: nil, transcript: nil)
        )
        // A boolean is never a number.
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"speechStart","turnId":true,"audioProcessedMs":false}"#),
            .speechStart(turnId: nil, audioProcessedMs: nil)
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"speechComplete","turnId":null,"audioProcessedMs":[80],"transcript":{"text":"x"}}"#),
            .speechComplete(turnId: nil, audioProcessedMs: nil, transcript: nil)
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"streamComplete","audioProcessedMs":"80","finalizationMs":true}"#),
            .streamComplete(audioProcessedMs: nil, finalizationMs: nil)
        )
        // A turn is a whole number.
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"speechStart","turnId":1.5,"audioProcessedMs":10.5}"#),
            .speechStart(turnId: nil, audioProcessedMs: 10.5)
        )
    }

    // MARK: - `loading` with a missing member is still `loading` (A-14)

    func testALoadingFrameWithAMissingMemberIsStillLoading() {
        XCTAssertEqual(RelaySpeechFrame.parse(#"{"type":"loading"}"#), .loading(provider: "", elapsedSeconds: 0, message: ""))
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"loading","elapsedSeconds":4}"#),
            .loading(provider: "", elapsedSeconds: 4, message: "")
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"loading","provider":"qwen3","message":"Loading"}"#),
            .loading(provider: "qwen3", elapsedSeconds: 0, message: "Loading")
        )
    }

    func testALoadingFrameWithAWronglyTypedMemberIsStillLoading() {
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"loading","provider":7,"elapsedSeconds":"12","message":null}"#),
            .loading(provider: "", elapsedSeconds: 0, message: "")
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"loading","provider":"nemotron","elapsedSeconds":2.5,"message":"Loading"}"#),
            .loading(provider: "nemotron", elapsedSeconds: 0, message: "Loading")
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"loading","provider":"nemotron","elapsedSeconds":true,"message":"Loading"}"#),
            .loading(provider: "nemotron", elapsedSeconds: 0, message: "Loading")
        )
    }

    // MARK: - `ready` and `error` need every member

    func testAReadyFrameWithAMissingOrWronglyTypedMemberIsUnknown() {
        let incomplete = [
            #"{"type":"ready"}"#,
            #"{"type":"ready","sampleRate":16000,"handshakeMs":400}"#,
            #"{"type":"ready","provider":"nemotron","handshakeMs":400}"#,
            #"{"type":"ready","provider":"nemotron","sampleRate":16000}"#,
            #"{"type":"ready","provider":"nemotron","sampleRate":"16000","handshakeMs":400}"#,
            #"{"type":"ready","provider":"nemotron","sampleRate":16000.5,"handshakeMs":400}"#,
            #"{"type":"ready","provider":"nemotron","sampleRate":16000,"handshakeMs":"400"}"#,
            #"{"type":"ready","provider":null,"sampleRate":16000,"handshakeMs":400}"#,
            #"{"type":"ready","provider":"nemotron","sampleRate":true,"handshakeMs":400}"#
        ]
        for text in incomplete {
            XCTAssertEqual(RelaySpeechFrame.parse(text), .unknown, text)
        }
    }

    func testAnErrorFrameWithAMissingOrWronglyTypedMemberIsUnknown() {
        let incomplete = [
            #"{"type":"error"}"#,
            #"{"type":"error","code":"SPEECH_BUSY"}"#,
            #"{"type":"error","message":"Another speech stream is active."}"#,
            #"{"type":"error","code":409,"message":"Another speech stream is active."}"#,
            #"{"type":"error","code":"SPEECH_BUSY","message":null}"#
        ]
        for text in incomplete {
            XCTAssertEqual(RelaySpeechFrame.parse(text), .unknown, text)
        }
    }

    // MARK: - Unknown types and unknown members (P2-218)

    func testAnObjectOfAnotherTypeIsUnknown() {
        let others = [
            #"{"type":"audioProgress","audioProcessedMs":80}"#,
            #"{"type":"speechEnd","turnId":1}"#,
            #"{"type":"somethingNew","payload":{"a":1}}"#,
            #"{"type":"endStream"}"#,
            #"{"type":""}"#,
            #"{"type":"Ready","provider":"nemotron","sampleRate":16000,"handshakeMs":400}"#,
            #"{"type":"READY","provider":"nemotron","sampleRate":16000,"handshakeMs":400}"#
        ]
        for text in others {
            XCTAssertEqual(RelaySpeechFrame.parse(text), .unknown, text)
        }
    }

    func testUnknownMembersOnKnownFramesAreIgnored() {
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"transcript","transcript":"Local words","private":"hidden","turnId":3}"#),
            .transcript(turnId: 3, audioProcessedMs: nil, transcript: "Local words")
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"ready","provider":"nemotron","sampleRate":16000,"handshakeMs":400,"labVersion":"9","extra":[1,2]}"#),
            .ready(provider: "nemotron", sampleRate: 16_000, handshakeMs: 400)
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"loading","provider":"qwen3","elapsedSeconds":2,"message":"Loading","percent":40}"#),
            .loading(provider: "qwen3", elapsedSeconds: 2, message: "Loading")
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"error","code":"MODEL_BUSY","message":"The PC model is in use.","detail":"hidden"}"#),
            .error(code: "MODEL_BUSY", message: "The PC model is in use.")
        )
        XCTAssertEqual(
            RelaySpeechFrame.parse(#"{"type":"streamComplete","audioProcessedMs":80,"finalizationMs":5,"model":"nemotron"}"#),
            .streamComplete(audioProcessedMs: 80, finalizationMs: 5)
        )
    }

    // MARK: - Text that is not a frame

    func testTextThatIsNotAJSONObjectWithAStringTypeIsNotAFrame() {
        let notFrames = [
            "",
            " ",
            "not JSON",
            "{",
            #"{"type":"ready""#,
            "[]",
            #"[{"type":"ready"}]"#,
            #""ready""#,
            "16000",
            "true",
            "null",
            "{}",
            #"{"provider":"nemotron","sampleRate":16000}"#,
            #"{"type":5}"#,
            #"{"type":null}"#,
            #"{"type":true}"#,
            #"{"type":["ready"]}"#,
            #"{"type":{"name":"ready"}}"#
        ]
        for text in notFrames {
            XCTAssertNil(RelaySpeechFrame.parse(text), text)
        }
    }
}
