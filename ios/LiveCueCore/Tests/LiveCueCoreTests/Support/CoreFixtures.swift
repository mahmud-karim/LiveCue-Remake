import Foundation
import LiveCueCore

/// Synthetic values for the core tests. Nothing here is a real host, token, session or reply.
enum CoreFixtures {
    /// The synthetic token of protocol 4.2: 43 characters of the shape C-02 requires.
    static let token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"

    /// A pairing with the synthetic token. `endpoint` must be a valid endpoint: a test that passes
    /// anything else has a mistake in it, and stops here.
    static func pairing(_ endpoint: String = "https://pc.example.test:11443/") -> PairingPayload {
        do {
            return try PairingPayload.manual(address: endpoint, token: token)
        } catch {
            preconditionFailure("CoreFixtures.pairing needs a valid endpoint, got \(endpoint)")
        }
    }

    /// A session whose segment `i` holds `segments[i]` and spans `i` to `i + 1` seconds.
    static func session(segments: [String]) -> Session {
        var made: [TranscriptSegment] = []
        for (index, text) in segments.enumerated() {
            made.append(TranscriptSegment(text: text, startSeconds: Double(index), endSeconds: Double(index + 1)))
        }
        return Session(startedAt: Date(timeIntervalSince1970: 1_790_000_000), segments: made)
    }

    /// The assist reply of protocol 8.3 with model "gpt-test" and effort "low".
    static let assistReplyJSON = """
    {"detectedQuestion":"What is the main advantage of local transcription?","answer":"Your audio stays on the phone.","details":"","memory":{"summary":"Earlier context","facts":["A fact"],"openQuestions":[],"throughSegmentId":null},"execution":{"model":"gpt-test","reasoningEffort":"low","codexMs":600.1,"relayTotalMs":620.4,"requestReadMs":2.0,"relayOverheadMs":20.3}}
    """

    /// The summary reply of protocol 8.3 with model "gpt-test" and effort "low".
    static let summaryReplyJSON = """
    {"title":"Local transcription","summary":"A short summary.","keyPoints":["A key point"],"actionItems":["An action item"],"execution":{"model":"gpt-test","reasoningEffort":"low","codexMs":900.0,"relayTotalMs":915.2,"requestReadMs":1.4,"relayOverheadMs":15.2}}
    """

    /// A catalogue with the one model the two replies above name.
    static let catalogueJSON = """
    {"models":[{"id":"gpt-test","name":"GPT Test","reasoningEfforts":["low"]}]}
    """
}
