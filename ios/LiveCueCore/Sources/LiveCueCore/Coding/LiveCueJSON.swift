import Foundation

/// The one encoder and decoder configuration of the app's own JSON.
public enum LiveCueJSON {
    /// Dates as "2026-10-01T14:03:22.123Z" (always three fractional digits, always "Z").
    /// Output formatting: sorted keys, slashes not escaped, compact.
    public static func makeEncoder() -> JSONEncoder {
        // STUB(WP-02)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return encoder
    }

    /// Dates with zero to nine fractional digits and "Z". Anything else fails decoding.
    public static func makeDecoder() -> JSONDecoder {
        // STUB(WP-02)
        JSONDecoder()
    }

    public static func encode<T: Encodable>(_ value: T) throws -> Data {
        try makeEncoder().encode(value)
    }

    public static func decode<T: Decodable>(_ type: T.Type, from data: Data) throws -> T {
        try makeDecoder().decode(type, from: data)
    }

    public static func string(from date: Date) -> String {
        ISO8601Milliseconds.string(from: date)
    }

    public static func date(from string: String) -> Date? {
        ISO8601Milliseconds.date(from: string)
    }

    public static func roundedToMilliseconds(_ date: Date) -> Date {
        // STUB(WP-02)
        date
    }
}
