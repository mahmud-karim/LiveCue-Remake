import Foundation

/// The two pure functions behind `LiveCueJSON.string(from:)` and `LiveCueJSON.date(from:)`.
///
/// They use integer arithmetic on the days-since-epoch count (the civil-date algorithm), not a formatter,
/// so they behave the same on Linux and need no shared mutable object.
///
/// Every date is handled as a whole number of milliseconds since 1970-01-01T00:00:00Z. The text form has
/// a four-digit year, so the range is 0000-01-01T00:00:00.000Z to 9999-12-31T23:59:59.999Z; a date
/// outside it is written as the nearest end of the range.
enum ISO8601Milliseconds {
    /// "2026-10-01T14:03:22.123Z": always three fractional digits, always "Z".
    static func string(from date: Date) -> String {
        let total = milliseconds(from: date)
        let days = floorDivide(total, millisecondsPerDay)
        let inDay = total - days * millisecondsPerDay
        let civil = civilDate(fromDays: days)

        var bytes: [UInt8] = []
        bytes.reserveCapacity(24)
        appendDigits(civil.year, width: 4, to: &bytes)
        bytes.append(dash)
        appendDigits(civil.month, width: 2, to: &bytes)
        bytes.append(dash)
        appendDigits(civil.day, width: 2, to: &bytes)
        bytes.append(letterT)
        appendDigits(inDay / 3_600_000, width: 2, to: &bytes)
        bytes.append(colon)
        appendDigits(inDay % 3_600_000 / 60_000, width: 2, to: &bytes)
        bytes.append(colon)
        appendDigits(inDay % 60_000 / 1_000, width: 2, to: &bytes)
        bytes.append(dot)
        appendDigits(inDay % 1_000, width: 3, to: &bytes)
        bytes.append(letterZ)
        return String(decoding: bytes, as: UTF8.self)
    }

    /// Accepts zero to nine fractional digits and "Z". Anything else is nil.
    ///
    /// The grammar is exactly `YYYY-MM-DDTHH:MM:SS`, then nothing or a "." with one to nine digits, then
    /// "Z". The date must exist (no 30 February, no hour 24, no second 60). Digits beyond the third
    /// fractional one are rounded to the nearest millisecond, so every date this returns is one that
    /// `string(from:)` writes back unchanged.
    static func date(from string: String) -> Date? {
        let bytes = Array(string.utf8)
        // 19 bytes of date and time, an optional fraction of 2 to 10 bytes, and the "Z".
        guard bytes.count == 20 || (22...30).contains(bytes.count) else { return nil }
        guard bytes[bytes.count - 1] == letterZ else { return nil }
        guard bytes[4] == dash, bytes[7] == dash, bytes[10] == letterT, bytes[13] == colon, bytes[16] == colon else {
            return nil
        }
        guard
            let year = number(in: bytes, at: 0, length: 4),
            let month = number(in: bytes, at: 5, length: 2),
            let day = number(in: bytes, at: 8, length: 2),
            let hour = number(in: bytes, at: 11, length: 2),
            let minute = number(in: bytes, at: 14, length: 2),
            let second = number(in: bytes, at: 17, length: 2)
        else {
            return nil
        }
        guard month >= 1, month <= 12, day >= 1, day <= daysIn(month: month, year: year) else { return nil }
        guard hour <= 23, minute <= 59, second <= 59 else { return nil }

        var nanoseconds: Int64 = 0
        if bytes.count > 20 {
            guard bytes[19] == dot else { return nil }
            let digits = bytes.count - 21
            guard let fraction = number(in: bytes, at: 20, length: digits) else { return nil }
            nanoseconds = fraction
            for _ in digits..<9 {
                nanoseconds *= 10
            }
        }

        let seconds = days(fromYear: year, month: month, day: day) * 86_400 + hour * 3_600 + minute * 60 + second
        let total = seconds * 1_000 + (nanoseconds + 500_000) / 1_000_000
        // Rounding the last half millisecond of year 9999 up would leave the range.
        return date(fromMilliseconds: min(total, maximumMilliseconds))
    }

    /// The date moved to the nearest whole millisecond (a tie moves away from zero), inside the range.
    static func rounded(_ date: Date) -> Date {
        self.date(fromMilliseconds: milliseconds(from: date))
    }

    // MARK: - Milliseconds

    private static let millisecondsPerDay: Int64 = 86_400_000
    /// 0000-01-01T00:00:00.000Z
    private static let minimumMilliseconds: Int64 = -62_167_219_200_000
    /// 9999-12-31T23:59:59.999Z
    private static let maximumMilliseconds: Int64 = 253_402_300_799_999

    private static func milliseconds(from date: Date) -> Int64 {
        let scaled = (date.timeIntervalSince1970 * 1_000).rounded()
        if scaled.isNaN {
            return 0
        }
        if scaled <= Double(minimumMilliseconds) {
            return minimumMilliseconds
        }
        if scaled >= Double(maximumMilliseconds) {
            return maximumMilliseconds
        }
        return Int64(scaled)
    }

    /// The one way a count of milliseconds becomes a `Date`. Writing and reading both go through it, which
    /// is what makes a rounded date equal to the date read back from its text.
    private static func date(fromMilliseconds milliseconds: Int64) -> Date {
        Date(timeIntervalSince1970: Double(milliseconds) / 1_000)
    }

    // MARK: - Civil dates (proleptic Gregorian calendar)

    private static func floorDivide(_ value: Int64, _ divisor: Int64) -> Int64 {
        let quotient = value / divisor
        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient
    }

    /// Year, month and day of a count of days since 1970-01-01.
    private static func civilDate(fromDays days: Int64) -> (year: Int64, month: Int64, day: Int64) {
        let shifted = days + 719_468
        let era = (shifted >= 0 ? shifted : shifted - 146_096) / 146_097
        let dayOfEra = shifted - era * 146_097
        let yearOfEra = (dayOfEra - dayOfEra / 1_460 + dayOfEra / 36_524 - dayOfEra / 146_096) / 365
        let dayOfYear = dayOfEra - (365 * yearOfEra + yearOfEra / 4 - yearOfEra / 100)
        let monthIndex = (5 * dayOfYear + 2) / 153
        let day = dayOfYear - (153 * monthIndex + 2) / 5 + 1
        let month = monthIndex < 10 ? monthIndex + 3 : monthIndex - 9
        let year = yearOfEra + era * 400 + (month <= 2 ? 1 : 0)
        return (year, month, day)
    }

    /// The count of days since 1970-01-01 of a year, month and day.
    private static func days(fromYear year: Int64, month: Int64, day: Int64) -> Int64 {
        let shiftedYear = month <= 2 ? year - 1 : year
        let era = (shiftedYear >= 0 ? shiftedYear : shiftedYear - 399) / 400
        let yearOfEra = shiftedYear - era * 400
        let dayOfYear = (153 * (month > 2 ? month - 3 : month + 9) + 2) / 5 + day - 1
        let dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear
        return era * 146_097 + dayOfEra - 719_468
    }

    private static func daysIn(month: Int64, year: Int64) -> Int64 {
        switch month {
        case 4, 6, 9, 11:
            return 30
        case 2:
            let isLeap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)
            return isLeap ? 29 : 28
        default:
            return 31
        }
    }

    // MARK: - Text

    private static let dash = UInt8(ascii: "-")
    private static let colon = UInt8(ascii: ":")
    private static let dot = UInt8(ascii: ".")
    private static let letterT = UInt8(ascii: "T")
    private static let letterZ = UInt8(ascii: "Z")
    private static let zero = UInt8(ascii: "0")
    private static let nine = UInt8(ascii: "9")

    /// Writes `value` with exactly `width` digits. The caller guarantees 0 <= value < 10^width.
    private static func appendDigits(_ value: Int64, width: Int, to bytes: inout [UInt8]) {
        var divisor: Int64 = 1
        for _ in 1..<width {
            divisor *= 10
        }
        var rest = value
        while divisor > 0 {
            bytes.append(zero + UInt8(truncatingIfNeeded: rest / divisor))
            rest %= divisor
            divisor /= 10
        }
    }

    /// The number written by `length` ASCII digits starting at `start`; nil when one of them is not a digit.
    private static func number(in bytes: [UInt8], at start: Int, length: Int) -> Int64? {
        var value: Int64 = 0
        for index in start..<(start + length) {
            let byte = bytes[index]
            guard byte >= zero, byte <= nine else { return nil }
            value = value * 10 + Int64(byte - zero)
        }
        return value
    }
}
