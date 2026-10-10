import Foundation
import LiveCueCore
import XCTest

/// A boolean is never a number and a number never a boolean (CORE-A17); non-finite and fractional numbers
/// have no `intValue`; text that is not JSON parses to nil.
final class JSONValueTests: XCTestCase {
    // MARK: - Booleans and numbers

    func testABooleanIsNeverANumber() throws {
        let value = try XCTUnwrap(JSONValue.parse("{\"yes\":true,\"no\":false}"))
        XCTAssertEqual(value["yes"], .bool(true))
        XCTAssertEqual(value["no"], .bool(false))
        XCTAssertEqual(value["yes"]?.boolValue, true)
        XCTAssertEqual(value["no"]?.boolValue, false)
        XCTAssertNil(value["yes"]?.numberValue)
        XCTAssertNil(value["yes"]?.intValue)
        XCTAssertNil(value["no"]?.numberValue)
        XCTAssertNil(value["no"]?.intValue)
    }

    func testANumberIsNeverABoolean() throws {
        let value = try XCTUnwrap(JSONValue.parse("{\"one\":1,\"zero\":0,\"real\":1.0}"))
        XCTAssertEqual(value["one"], .number(1))
        XCTAssertEqual(value["zero"], .number(0))
        XCTAssertEqual(value["real"], .number(1))
        XCTAssertNil(value["one"]?.boolValue)
        XCTAssertNil(value["zero"]?.boolValue)
        XCTAssertNil(value["real"]?.boolValue)
        XCTAssertEqual(value["one"]?.intValue, 1)
        XCTAssertEqual(value["zero"]?.intValue, 0)
        XCTAssertNotEqual(value["one"], .bool(true))
        XCTAssertNotEqual(value["zero"], .bool(false))
    }

    func testIntValueNeedsAFiniteWholeNumberThatFits() throws {
        XCTAssertEqual(JSONValue.number(42).intValue, 42)
        XCTAssertEqual(JSONValue.number(-7).intValue, -7)
        XCTAssertEqual(JSONValue.number(12_000).intValue, 12_000)
        XCTAssertEqual(JSONValue.number(0).intValue, 0)

        XCTAssertNil(JSONValue.number(1.5).intValue)
        XCTAssertNil(JSONValue.number(-0.25).intValue)
        XCTAssertNil(JSONValue.number(.infinity).intValue)
        XCTAssertNil(JSONValue.number(-.infinity).intValue)
        XCTAssertNil(JSONValue.number(.nan).intValue)
        XCTAssertNil(JSONValue.number(1e300).intValue)

        XCTAssertEqual(JSONValue.number(1.5).numberValue, 1.5)
        XCTAssertNil(JSONValue.number(.infinity).numberValue)
        XCTAssertNil(JSONValue.number(.nan).numberValue)

        let parsed = try XCTUnwrap(JSONValue.parse("{\"whole\":12,\"fraction\":12.5,\"exponent\":1e3,\"negative\":-3}"))
        XCTAssertEqual(parsed["whole"]?.intValue, 12)
        XCTAssertNil(parsed["fraction"]?.intValue)
        XCTAssertEqual(parsed["fraction"]?.numberValue, 12.5)
        XCTAssertEqual(parsed["exponent"]?.intValue, 1_000)
        XCTAssertEqual(parsed["negative"]?.intValue, -3)
    }

    // MARK: - Every kind of value

    func testParsesEveryKindOfValue() throws {
        let text = """
        {"nothing":null,"flag":true,"count":3,"ratio":0.5,"name":"LiveCue","list":[1,"two",false,null,[],{}],"nested":{"inner":{"deep":"yes"}}}
        """
        let value = try XCTUnwrap(JSONValue.parse(text))

        XCTAssertEqual(value["nothing"], .null)
        XCTAssertEqual(value["flag"]?.boolValue, true)
        XCTAssertEqual(value["count"]?.intValue, 3)
        XCTAssertEqual(value["ratio"]?.numberValue, 0.5)
        XCTAssertEqual(value["name"]?.stringValue, "LiveCue")
        XCTAssertEqual(
            value["list"]?.arrayValue,
            [.number(1), .string("two"), .bool(false), .null, .array([]), .object([:])]
        )
        XCTAssertEqual(value["nested"]?["inner"]?["deep"]?.stringValue, "yes")
        XCTAssertEqual(value.objectValue?.count, 7)
    }

    func testAccessorsAnswerOnlyForTheirOwnKind() {
        let string = JSONValue.string("12")
        XCTAssertNil(string.numberValue)
        XCTAssertNil(string.intValue)
        XCTAssertNil(string.boolValue)
        XCTAssertNil(string.arrayValue)
        XCTAssertNil(string.objectValue)
        XCTAssertNil(string["key"])

        let null = JSONValue.null
        XCTAssertNil(null.stringValue)
        XCTAssertNil(null.numberValue)
        XCTAssertNil(null.boolValue)
        XCTAssertNil(null["key"])

        let array = JSONValue.array([.string("a")])
        XCTAssertNil(array["0"])
        XCTAssertNil(array.objectValue)
        XCTAssertNil(array.stringValue)

        let object = JSONValue.object(["key": .string("value")])
        XCTAssertNil(object.arrayValue)
        XCTAssertNil(object["other"])
        XCTAssertEqual(object["key"], .string("value"))
    }

    func testAStringThatLooksLikeAnotherValueStaysAString() throws {
        let value = try XCTUnwrap(JSONValue.parse("{\"a\":\"true\",\"b\":\"1\",\"c\":\"null\"}"))
        XCTAssertEqual(value["a"], .string("true"))
        XCTAssertEqual(value["b"], .string("1"))
        XCTAssertEqual(value["c"], .string("null"))
    }

    func testKeepsTextAsWritten() throws {
        let value = try XCTUnwrap(JSONValue.parse("{\"text\":\"caf\\u00e9 · \\\"quoted\\\" \\n line / slash\"}"))
        XCTAssertEqual(value["text"]?.stringValue, "caf\u{E9} · \"quoted\" \n line / slash")
    }

    // MARK: - Not JSON

    func testTextThatIsNotJSONParsesToNil() {
        // A comma after the last member is not in this list: Foundation's parser accepts it.
        let notJSON = [
            "",
            "   ",
            "not a pairing QR",
            "{",
            "{\"a\":}",
            "{a:1}",
            "{'a':1}",
            "[1,2",
            "[1,,2]",
            "{\"a\":1}{\"b\":2}",
            "{\"a\":tru}",
            "{\"a\":NaN}",
            "{\"a\":Infinity}",
            "{\"a\":01}"
        ]
        for text in notJSON {
            XCTAssertNil(JSONValue.parse(text), "\"\(text)\" is not JSON")
        }
        XCTAssertNil(JSONValue.parse(Data([0xFF, 0xFE, 0x00])))
        XCTAssertNil(JSONValue.parse(Data()))
    }

    func testParsingDataAndTextAgree() throws {
        let text = "{\"endpoint\":\"https://pc.example.test/\",\"token\":\"x\"}"
        XCTAssertEqual(JSONValue.parse(Data(text.utf8)), JSONValue.parse(text))
        XCTAssertNotNil(JSONValue.parse(text))
    }

    // MARK: - Coding

    func testRoundTripsThroughTheEncoder() throws {
        let original = JSONValue.object([
            "nothing": .null,
            "flag": .bool(true),
            "off": .bool(false),
            "count": .number(3),
            "one": .number(1),
            "zero": .number(0),
            "ratio": .number(0.25),
            "name": .string("LiveCue"),
            "list": .array([.number(1), .string("two"), .bool(false), .null]),
            "nested": .object(["inner": .array([.object(["deep": .string("yes")])])])
        ])

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        let data = try encoder.encode(original)
        XCTAssertEqual(JSONValue.parse(data), original)
        XCTAssertEqual(try JSONDecoder().decode(JSONValue.self, from: data), original)

        // Booleans are written as booleans and numbers as numbers.
        let text = String(decoding: data, as: UTF8.self)
        XCTAssertTrue(text.contains("\"flag\":true"), text)
        XCTAssertTrue(text.contains("\"off\":false"), text)
        XCTAssertTrue(text.contains("\"one\":1"), text)
        XCTAssertTrue(text.contains("\"zero\":0"), text)
        XCTAssertTrue(text.contains("\"nothing\":null"), text)
    }

    func testDecodesAsAMemberOfAnotherType() throws {
        struct Envelope: Decodable {
            let code: String
            let detail: JSONValue
        }
        let data = Data("{\"code\":\"X\",\"detail\":{\"busy\":false,\"elapsedSeconds\":4}}".utf8)
        let envelope = try JSONDecoder().decode(Envelope.self, from: data)
        XCTAssertEqual(envelope.code, "X")
        XCTAssertEqual(envelope.detail["busy"]?.boolValue, false)
        XCTAssertNil(envelope.detail["busy"]?.intValue)
        XCTAssertEqual(envelope.detail["elapsedSeconds"]?.intValue, 4)
        XCTAssertNil(envelope.detail["elapsedSeconds"]?.boolValue)
    }
}
