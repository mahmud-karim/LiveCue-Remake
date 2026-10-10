import Foundation

/// The one encoder and decoder configuration of the app's own JSON.
public enum LiveCueJSON {
    /// Dates as "2026-10-01T14:03:22.123Z" (always three fractional digits, always "Z").
    /// Output formatting: sorted keys, slashes not escaped, compact.
    public static func makeEncoder() -> JSONEncoder {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        encoder.dateEncodingStrategy = .custom { date, target in
            var container = target.singleValueContainer()
            try container.encode(ISO8601Milliseconds.string(from: date))
        }
        return encoder
    }

    /// Dates with zero to nine fractional digits and "Z". Anything else fails decoding.
    public static func makeDecoder() -> JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { source in
            let container = try source.singleValueContainer()
            let text = try container.decode(String.self)
            guard let date = ISO8601Milliseconds.date(from: text) else {
                throw DecodingError.dataCorruptedError(
                    in: container,
                    debugDescription: "A date must be ISO 8601 in UTC with zero to nine fractional digits and a final Z."
                )
            }
            return date
        }
        return decoder
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

    /// The date moved to the nearest whole millisecond, which is what `string(from:)` writes. A date so
    /// rounded is equal to the date read back from its text.
    public static func roundedToMilliseconds(_ date: Date) -> Date {
        ISO8601Milliseconds.rounded(date)
    }
}
