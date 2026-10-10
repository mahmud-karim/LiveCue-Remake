import Foundation

/// The two pure functions behind `LiveCueJSON.string(from:)` and `LiveCueJSON.date(from:)`.
///
/// They use integer arithmetic on the days-since-epoch count (the civil-date algorithm), not a formatter,
/// so they behave the same on Linux and need no shared mutable object.
enum ISO8601Milliseconds {
    /// "2026-10-01T14:03:22.123Z": always three fractional digits, always "Z".
    static func string(from date: Date) -> String {
        // STUB(WP-02)
        ""
    }

    /// Accepts zero to nine fractional digits and "Z". Anything else is nil.
    static func date(from string: String) -> Date? {
        // STUB(WP-02)
        nil
    }
}
