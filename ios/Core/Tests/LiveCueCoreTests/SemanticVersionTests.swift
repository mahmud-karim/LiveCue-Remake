import Foundation
import LiveCueCore
import Testing

@Suite("SemanticVersion")
struct SemanticVersionTests {
    @Test("Parses three numeric parts")
    func parsesThreeNumericParts() {
        #expect(SemanticVersion("1.0.0") == SemanticVersion(major: 1, minor: 0, patch: 0))
        #expect(SemanticVersion("12.34.56") == SemanticVersion(major: 12, minor: 34, patch: 56))
    }

    @Test(
        "Rejects malformed text",
        arguments: ["", "1", "1.0", "1.0.0.0", "1..0", "a.b.c", "1.0.-1", "+1.0.0", " 1.0.0", "1.0.0-beta"]
    )
    func rejectsMalformedText(_ text: String) {
        #expect(SemanticVersion(text) == nil)
    }

    @Test("Rejects a part that does not fit in an integer")
    func rejectsOverflow() {
        #expect(SemanticVersion("1.0.99999999999999999999999999") == nil)
    }

    @Test("Orders by major, then minor, then patch")
    func ordersByMajorThenMinorThenPatch() {
        let ascending = [
            SemanticVersion(major: 0, minor: 9, patch: 9),
            SemanticVersion(major: 1, minor: 0, patch: 0),
            SemanticVersion(major: 1, minor: 0, patch: 1),
            SemanticVersion(major: 1, minor: 2, patch: 0),
            SemanticVersion(major: 10, minor: 0, patch: 0)
        ]
        #expect(ascending.shuffled().sorted() == ascending)
    }

    @Test("Prints as major.minor.patch")
    func printsAsDottedText() {
        #expect(SemanticVersion(major: 1, minor: 2, patch: 3).description == "1.2.3")
    }

    @Test("Round-trips through JSON as a string")
    func roundTripsThroughJSON() throws {
        let versions = [SemanticVersion(major: 1, minor: 2, patch: 3)]

        let data = try JSONEncoder().encode(versions)
        #expect(String(decoding: data, as: UTF8.self) == #"["1.2.3"]"#)

        let decoded = try JSONDecoder().decode([SemanticVersion].self, from: data)
        #expect(decoded == versions)
    }

    @Test("Refuses to decode malformed JSON text")
    func refusesMalformedJSON() {
        let data = Data(#"["1.2"]"#.utf8)
        #expect(throws: DecodingError.self) {
            try JSONDecoder().decode([SemanticVersion].self, from: data)
        }
    }
}
