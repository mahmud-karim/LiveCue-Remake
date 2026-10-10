import Foundation

// Internal; reached by tests with `@testable import LiveCueCore`.

struct OpenRouterStreamResult: Equatable, Sendable {
    var id: String?
    var model: String?
    var text: String
    var usage: AssistantUsage?
}

/// The SSE parser of a streamed answer, and the whole-body form.
struct OpenRouterStreamParser: Sendable {
    /// Readable after a failure, for the preview.
    private(set) var text: String
    private(set) var done: Bool

    init() {
        text = ""
        done = false
    }

    /// One line without its terminator. True when the answer text grew. Throws `OpenRouterError`.
    mutating func consumeLine(_ line: String) throws -> Bool {
        // STUB(WP-03)
        false
    }

    /// Succeeds only when done is set, the finish reason is "stop" and the trimmed text is not empty.
    func result() throws -> OpenRouterStreamResult {
        // STUB(WP-03)
        throw OpenRouterError.invalidResponse
    }

    /// The whole-body form.
    static func completeJSON(_ data: Data) throws -> OpenRouterStreamResult {
        // STUB(WP-03)
        throw OpenRouterError.invalidResponse
    }
}
