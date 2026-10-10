import Foundation
import LiveCueCore
import XCTest

final class SemanticVersionTests: XCTestCase {
    func testParsesThreeNumericParts() {
        XCTAssertEqual(SemanticVersion("1.0.0"), SemanticVersion(major: 1, minor: 0, patch: 0))
        XCTAssertEqual(SemanticVersion("12.34.56"), SemanticVersion(major: 12, minor: 34, patch: 56))
    }

    func testRejectsMalformedText() {
        let malformed = ["", "1", "1.0", "1.0.0.0", "1..0", "a.b.c", "1.0.-1", "+1.0.0", " 1.0.0", "1.0.0-beta"]
        for text in malformed {
            XCTAssertNil(SemanticVersion(text), "\"\(text)\" should not parse")
        }
    }

    func testRejectsAPartThatDoesNotFitInAnInteger() {
        XCTAssertNil(SemanticVersion("1.0.99999999999999999999999999"))
    }

    func testOrdersByMajorThenMinorThenPatch() {
        let ascending = [
            SemanticVersion(major: 0, minor: 9, patch: 9),
            SemanticVersion(major: 1, minor: 0, patch: 0),
            SemanticVersion(major: 1, minor: 0, patch: 1),
            SemanticVersion(major: 1, minor: 2, patch: 0),
            SemanticVersion(major: 10, minor: 0, patch: 0)
        ]
        XCTAssertEqual(ascending.shuffled().sorted(), ascending)
    }

    func testPrintsAsDottedText() {
        XCTAssertEqual(SemanticVersion(major: 1, minor: 2, patch: 3).description, "1.2.3")
    }

    func testRoundTripsThroughJSONAsAString() throws {
        let versions = [SemanticVersion(major: 1, minor: 2, patch: 3)]

        let data = try JSONEncoder().encode(versions)
        XCTAssertEqual(String(decoding: data, as: UTF8.self), #"["1.2.3"]"#)
        XCTAssertEqual(try JSONDecoder().decode([SemanticVersion].self, from: data), versions)
    }

    func testRefusesToDecodeMalformedJSONText() {
        let data = Data(#"["1.2"]"#.utf8)
        XCTAssertThrowsError(try JSONDecoder().decode([SemanticVersion].self, from: data)) { error in
            XCTAssertTrue(error is DecodingError, "Unexpected error: \(error)")
        }
    }
}
