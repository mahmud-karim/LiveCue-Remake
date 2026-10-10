import Foundation

/// Catalogue decoding, filter and sort. Internal; reached by tests with `@testable import LiveCueCore`.
enum OpenRouterCatalogue {
    /// A malformed entry is skipped (CORE-A5); only a missing or non-array `data` fails. Sorted by
    /// lower-cased name, then by id, without locale rules (CORE-B4).
    static func options(from body: Data) throws -> [AssistantModelOption] {
        // STUB(WP-03)
        []
    }
}
