import Foundation
import LiveCueCore
import XCTest

/// Handshake, acknowledgement and the event sanitiser: spec 02 section 5 and its acceptance criteria,
/// with CORE-A9 (the acknowledgement contract is kept), CORE-A17 (a boolean is not a number) and
/// CORE-A18 (a key with whitespace is malformed, not missing).
final class MetaRealtimeAPITests: XCTestCase {
    private let allErrors: [MetaSpeechError] = [.missingKey, .malformedKey, .authentication, .provider, .connection]

    // MARK: - testDirectHandshakeUsesFixedTLSServiceAndNoPC (AC-CORE-021 to 023)

    func testTheEndpointIsFixedWithNoQueryAndNoUser() throws {
        // AC-CORE-021
        XCTAssertEqual(MetaRealtimeAPI.endpoint.absoluteString, "wss://api.meta.ai/v1/asr/realtime")

        let components = try XCTUnwrap(URLComponents(url: MetaRealtimeAPI.endpoint, resolvingAgainstBaseURL: false))
        XCTAssertEqual(components.scheme, "wss")
        XCTAssertEqual(components.host, "api.meta.ai")
        XCTAssertEqual(components.path, "/v1/asr/realtime")
        XCTAssertNil(components.query)
        XCTAssertNil(components.fragment)
        XCTAssertNil(components.user)
        XCTAssertNil(components.password)
        XCTAssertNil(components.port)
    }

    func testTheLimitsAndSizesOfTheMetaPath() {
        XCTAssertEqual(MetaRealtimeAPI.sampleRate, 24_000)
        XCTAssertEqual(MetaRealtimeAPI.frameBytes, 3_840)
        XCTAssertEqual(MetaRealtimeAPI.connectSeconds, 30)
        XCTAssertEqual(MetaRealtimeAPI.testSeconds, 15)
        XCTAssertEqual(MetaRealtimeAPI.finalizeSeconds, 15)
        XCTAssertEqual(MetaRealtimeAPI.maximumMessageBytes, 128 * 1_024)
        XCTAssertEqual(MetaRealtimeAPI.endStreamMessage, #"{"type":"endStream"}"#)
    }

    func testTheHandshakeHasExactlyTheSixMembers() throws {
        let text = try MetaRealtimeAPI.handshake(key: "synthetic-meta-unit-key")
        let members = try XCTUnwrap(JSONValue.parse(text)?.objectValue)

        // AC-CORE-022
        XCTAssertEqual(members["authorization"], .object(["accessToken": .string("Bearer synthetic-meta-unit-key")]))
        XCTAssertEqual(members["audioEncoding"], .string("PCM_24KHZ"))
        XCTAssertEqual(members["mode"], .string("ENDPOINTING"))
        XCTAssertEqual(members["partialMode"], .string("CUMULATIVE"))
        XCTAssertEqual(members["model"], .string("muse-voice-transcribe-1.0"))
        XCTAssertEqual(members["emitAudioProgress"], .bool(true))

        // Exactly these six, so nothing of a pairing can be in it. AC-CORE-023
        XCTAssertEqual(
            members.keys.sorted(),
            ["audioEncoding", "authorization", "emitAudioProgress", "mode", "model", "partialMode"]
        )
        XCTAssertNil(members["endpoint"])
        XCTAssertNil(members["token"])
    }

    func testTheHandshakeCarriesAnyKeyUnchanged() throws {
        // Characters that JSON must escape, and one that an encoder may escape.
        for key in [#"quote"and\backslash"#, "slash/and-unicode-é", "tab-free-but-{braces}"] {
            let text = try MetaRealtimeAPI.handshake(key: key)
            let value = try XCTUnwrap(JSONValue.parse(text), key)
            XCTAssertEqual(value["authorization"]?["accessToken"]?.stringValue, "Bearer " + key)
        }
    }

    // MARK: - testInvalidKeysAndFailedAuthenticationAreSafe (AC-CORE-025 to 028)

    func testAnEmptyKeyIsMissingAndAKeyWithWhitespaceIsMalformed() {
        // AC-CORE-025: all three keys of the old test still fail.
        let rows: [(key: String, error: MetaSpeechError)] = [
            ("", .missingKey),
            ("key with spaces", .malformedKey),
            ("key\nsecret", .malformedKey),
            (" ", .malformedKey),
            ("key\tsecret", .malformedKey),
            ("key\r\nsecret", .malformedKey),
            ("trailing-newline\n", .malformedKey),
            ("non\u{00A0}breaking", .malformedKey),
            ("line\u{2028}separator", .malformedKey)
        ]
        for row in rows {
            XCTAssertThrowsError(try MetaRealtimeAPI.handshake(key: row.key), row.key) { error in
                XCTAssertEqual(error as? MetaSpeechError, row.error, row.key)
            }
        }
    }

    func testAcknowledgingASessionIdSucceeds() {
        // AC-CORE-026
        XCTAssertNoThrow(try MetaRealtimeAPI.acknowledge(#"{"sessionId":"synthetic-session"}"#))
        // Members other than `type` do not matter.
        XCTAssertNoThrow(try MetaRealtimeAPI.acknowledge(#"{"sessionId":"synthetic-session","region":"test","expiresIn":60}"#))
    }

    func testAnythingElseIsAnAuthenticationFailureThatSurfacesNothing() {
        // AC-CORE-027, and CORE-A9: any `type` member rejects the message, even a harmless one.
        let rejected = [
            #"{"sessionId":""}"#,
            #"{"type":"error","sessionId":"id","message":"secret"}"#,
            "{}",
            #"{"type":"ready","sessionId":"id"}"#,
            #"{"type":null,"sessionId":"id"}"#,
            #"{"type":7,"sessionId":"id"}"#,
            #"{"sessionId":12345}"#,
            #"{"sessionId":null}"#,
            #"{"sessionId":["secret"]}"#,
            #"["sessionId","secret"]"#,
            #""secret""#,
            "secret is not JSON",
            ""
        ]
        for message in rejected {
            XCTAssertThrowsError(try MetaRealtimeAPI.acknowledge(message), message) { error in
                XCTAssertEqual(error as? MetaSpeechError, .authentication, message)
                let description = (error as? MetaSpeechError)?.errorDescription ?? ""
                XCTAssertFalse(description.isEmpty)
                XCTAssertFalse(description.contains("secret"), message)
            }
        }
    }

    func testAnErrorEventFailsAsProviderAndSurfacesNothing() {
        // AC-CORE-028
        let key = "synthetic-sensitive-key"
        XCTAssertThrowsError(try MetaRealtimeAPI.event(#"{"type":"error","message":"synthetic-sensitive-key"}"#, key: key)) { error in
            XCTAssertEqual(error as? MetaSpeechError, .provider)
            let description = (error as? MetaSpeechError)?.errorDescription ?? ""
            XCTAssertFalse(description.isEmpty)
            XCTAssertFalse(description.contains(key))
        }
        // The other members of an error event are not read at all.
        XCTAssertThrowsError(try MetaRealtimeAPI.event(#"{"type":"error","turnId":2,"transcript":"words"}"#, key: key)) { error in
            XCTAssertEqual(error as? MetaSpeechError, .provider)
        }
    }

    func testNoErrorDescriptionContainsAKey() {
        let key = "synthetic-sensitive-key"
        for error in allErrors {
            let description = error.errorDescription ?? ""
            XCTAssertFalse(description.isEmpty, "\(error)")
            XCTAssertFalse(description.contains(key), "\(error)")
        }
    }

    // MARK: - testAllowlistedEventsPreserveCaptionsAndUsageWithoutSecrets (AC-CORE-029, 030)

    func testAnAllowlistedEventKeepsCaptionAndUsageAndDropsEverythingElse() throws {
        // AC-CORE-029
        let event = try MetaRealtimeAPI.event(
            """
            {"type":"transcript","turnId":2,"audioProcessedMs":1234,"final":false,"transcript":"Hello synthetic-meta-key","authorization":"synthetic-meta-key","metadata":"synthetic-meta-key"}
            """,
            key: "synthetic-meta-key"
        )
        XCTAssertEqual(event.transcript, "Hello [redacted]")
        XCTAssertEqual(event.audioProcessedMs, 1_234)
        XCTAssertEqual(event.isFinal, false)
        XCTAssertEqual(event.turnId, 2)
        XCTAssertEqual(event.type, "transcript")
        XCTAssertFalse(event.isIgnored)

        // The clean event has no place for `authorization` or `metadata`: it is exactly these members.
        XCTAssertEqual(
            event,
            MetaEvent(type: "transcript", turnId: 2, audioProcessedMs: 1_234, isFinal: false, transcript: "Hello [redacted]")
        )
    }

    func testAnEventOutsideTheAllowlistIsIgnored() throws {
        // AC-CORE-030
        let key = "synthetic-meta-key"
        let ignored = [
            #"{"type":"unknown","transcript":"synthetic-meta-key"}"#,
            #"{"type":"streamComplete","audioProcessedMs":80}"#,
            #"{"type":"ready","sessionId":"id"}"#,
            #"{"type":"Transcript","transcript":"wrong case"}"#,
            #"{"type":"","transcript":"empty type"}"#,
            #"{"transcript":"no type"}"#,
            #"{"type":5,"transcript":"type is a number"}"#,
            #"{"type":null}"#,
            #"{"type":["transcript"]}"#,
            "{}",
            #"["transcript"]"#,
            "not JSON",
            ""
        ]
        for message in ignored {
            let event = try MetaRealtimeAPI.event(message, key: key)
            XCTAssertTrue(event.isIgnored, message)
            XCTAssertEqual(event, MetaEvent(type: ""), message)
            XCTAssertEqual(event.transcriptEvent, .other, message)
        }
    }

    func testTheFiveAllowedTypesPass() throws {
        for type in ["speechStart", "speechEnd", "speechComplete", "transcript", "audioProgress"] {
            let event = try MetaRealtimeAPI.event(#"{"type":"\#(type)","turnId":3,"audioProcessedMs":250.5}"#, key: "k")
            XCTAssertEqual(event, MetaEvent(type: type, turnId: 3, audioProcessedMs: 250.5), type)
            XCTAssertFalse(event.isIgnored, type)
        }
    }

    func testTheKeyIsRedactedEverywhereInTheTranscriptAndOnlyThere() throws {
        let key = "synthetic-meta-key"
        let event = try MetaRealtimeAPI.event(
            #"{"type":"speechComplete","turnId":1,"transcript":"synthetic-meta-key and synthetic-meta-key again"}"#,
            key: key
        )
        XCTAssertEqual(event.transcript, "[redacted] and [redacted] again")

        // Redaction is skipped for an empty key: the text is copied as it is.
        let unredacted = try MetaRealtimeAPI.event(#"{"type":"transcript","transcript":"plain words"}"#, key: "")
        XCTAssertEqual(unredacted.transcript, "plain words")
    }

    // MARK: - AC-CORE-S19 and CORE-A17

    func testATranscriptIsCutToItsFirst32000Characters() throws {
        let long = String(repeating: "a", count: 31_999) + "é" + String(repeating: "b", count: 500)
        let message = #"{"type":"transcript","transcript":""# + long + #""}"#
        let event = try MetaRealtimeAPI.event(message, key: "k")
        let transcript = try XCTUnwrap(event.transcript)
        XCTAssertEqual(transcript.count, 32_000)
        XCTAssertEqual(transcript, String(long.prefix(32_000)))
        XCTAssertTrue(transcript.hasSuffix("aé"))

        // Exactly at the limit nothing is cut.
        let exact = String(repeating: "c", count: 32_000)
        let whole = try MetaRealtimeAPI.event(#"{"type":"transcript","transcript":""# + exact + #""}"#, key: "k")
        XCTAssertEqual(whole.transcript, exact)
    }

    func testRedactionHappensBeforeTheCut() throws {
        // The key straddles the 32,000th character. Cutting first would leave half a key in the text.
        let key = "synthetic-meta-key"
        let text = String(repeating: "a", count: 31_990) + key + " tail"
        let event = try MetaRealtimeAPI.event(#"{"type":"transcript","transcript":""# + text + #""}"#, key: key)
        let transcript = try XCTUnwrap(event.transcript)
        XCTAssertEqual(transcript.count, 32_000)
        XCTAssertEqual(transcript, String(repeating: "a", count: 31_990) + "[redacted]")
        XCTAssertFalse(transcript.contains("synthetic"))
    }

    func testMembersOfTheWrongTypeAreDropped() throws {
        // AC-CORE-S19: a `turnId` or `audioProcessedMs` that is not a number is dropped.
        let strings = try MetaRealtimeAPI.event(
            #"{"type":"transcript","turnId":"2","audioProcessedMs":"1234","final":"false","transcript":17}"#,
            key: "k"
        )
        XCTAssertEqual(strings, MetaEvent(type: "transcript"))

        // CORE-A17: a JSON boolean is not a number.
        let booleans = try MetaRealtimeAPI.event(
            #"{"type":"speechStart","turnId":true,"audioProcessedMs":false}"#,
            key: "k"
        )
        XCTAssertNil(booleans.turnId)
        XCTAssertNil(booleans.audioProcessedMs)
        XCTAssertEqual(booleans, MetaEvent(type: "speechStart"))

        // And a number is not a boolean.
        let numbers = try MetaRealtimeAPI.event(#"{"type":"transcript","final":1,"turnId":4}"#, key: "k")
        XCTAssertNil(numbers.isFinal)
        XCTAssertEqual(numbers.turnId, 4)

        // `null`, arrays and objects are not values either.
        let others = try MetaRealtimeAPI.event(
            #"{"type":"transcript","turnId":null,"audioProcessedMs":[1],"final":{},"transcript":null}"#,
            key: "k"
        )
        XCTAssertEqual(others, MetaEvent(type: "transcript"))
    }

    func testATurnThatIsNotAWholeNumberIsDropped() throws {
        let event = try MetaRealtimeAPI.event(#"{"type":"speechStart","turnId":2.5,"audioProcessedMs":10.25}"#, key: "k")
        XCTAssertNil(event.turnId)
        XCTAssertEqual(event.audioProcessedMs, 10.25)

        let whole = try MetaRealtimeAPI.event(#"{"type":"speechStart","turnId":3.0}"#, key: "k")
        XCTAssertEqual(whole.turnId, 3)
    }

    func testFinalIsCopiedWhenItIsABoolean() throws {
        let final = try MetaRealtimeAPI.event(#"{"type":"transcript","final":true,"transcript":"Done"}"#, key: "k")
        XCTAssertEqual(final.isFinal, true)
        let absent = try MetaRealtimeAPI.event(#"{"type":"transcript","transcript":"Going"}"#, key: "k")
        XCTAssertNil(absent.isFinal)
    }
}
