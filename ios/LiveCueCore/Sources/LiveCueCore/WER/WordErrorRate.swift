import Foundation

public enum WordErrorRate {
    /// Spec 02 section 3.4. Can exceed 1.
    public static func compute(reference: String, hypothesis: String) -> Double {
        let expected = words(in: reference)
        let heard = words(in: hypothesis)
        guard !expected.isEmpty else {
            return heard.isEmpty ? 0 : 1
        }
        return Double(editDistance(from: expected, to: heard)) / Double(expected.count)
    }

    /// Lower-cased pieces between characters that are neither letters nor digits; empty pieces dropped.
    private static func words(in text: String) -> [Substring] {
        text.lowercased().split(whereSeparator: { !($0.isLetter || $0.isNumber) })
    }

    /// Word-level edit distance: an insertion, a deletion and a substitution each cost 1.
    private static func editDistance(from expected: [Substring], to heard: [Substring]) -> Int {
        guard !heard.isEmpty else { return expected.count }
        // One row of the table at a time: `previous[j]` is the distance to the first `j` heard words.
        var previous = Array(0...heard.count)
        var current = previous
        for (row, word) in expected.enumerated() {
            current[0] = row + 1
            for column in 1...heard.count {
                let substitution = previous[column - 1] + (word == heard[column - 1] ? 0 : 1)
                let deletion = previous[column] + 1
                let insertion = current[column - 1] + 1
                current[column] = min(substitution, deletion, insertion)
            }
            swap(&previous, &current)
        }
        return previous[heard.count]
    }
}
