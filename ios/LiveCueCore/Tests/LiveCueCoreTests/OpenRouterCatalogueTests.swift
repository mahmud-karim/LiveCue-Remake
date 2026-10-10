import Foundation
@testable import LiveCueCore
import XCTest

/// Catalogue decoding, filter and sort.
///
/// AC-CORE-040, 041 and S09 of `docs/spec/02-ios-core.md` section 10, with the two changes of the
/// decisions: a malformed entry is skipped (CORE-A5), and the order is by lower-cased name and then by id,
/// without locale rules (CORE-B4).
final class OpenRouterCatalogueTests: XCTestCase {
    // MARK: - Helpers

    private static func options(_ json: String) throws -> [AssistantModelOption] {
        try OpenRouterCatalogue.options(from: Data(json.utf8))
    }

    /// One catalogue entry that takes text in and gives text out.
    private static func entry(id: String, name: String, extra: String = "") -> String {
        "{\"id\":\"\(id)\",\"name\":\"\(name)\",\"architecture\":{\"input_modalities\":[\"text\"],\"output_modalities\":[\"text\"]}\(extra)}"
    }

    private static func catalogue(_ entries: [String]) -> String {
        "{\"data\":[" + entries.joined(separator: ",") + "]}"
    }

    // MARK: - testPublicCatalogFiltersAndDoesNotSendKey (the decoding half)

    func testTheCatalogueOfTheSpecKeepsTheTextModel() throws {
        let options = try Self.options(
            #"{"data":[{"id":"text/model","name":"Text","architecture":{"input_modalities":["text"],"output_modalities":["text"]},"supported_parameters":["structured_outputs"],"pricing":{"prompt":"0.000001","completion":"0.000002"}},{"id":"audio/model","name":"Audio","architecture":{"input_modalities":["audio"],"output_modalities":["audio"]}}]}"#
        )

        // AC-CORE-040: the audio-only model is filtered out.
        XCTAssertEqual(options.count, 1)
        // AC-CORE-041
        XCTAssertEqual(options.first?.structuredOutputs, true)
        XCTAssertEqual(options.first?.promptPrice, "0.000001")
        XCTAssertEqual(
            options,
            [
                AssistantModelOption(
                    id: "text/model",
                    name: "Text",
                    reasoningEfforts: ["default"],
                    structuredOutputs: true,
                    promptPrice: "0.000001",
                    completionPrice: "0.000002"
                )
            ]
        )
    }

    // MARK: - AC-CORE-S09: filter

    func testAnEntryIsKeptOnlyWhenItTakesTextInAndGivesTextOut() throws {
        let options = try Self.options(
            #"""
            {"data":[
              {"id":"a/both","name":"Both","architecture":{"input_modalities":["image","text"],"output_modalities":["text","image"]}},
              {"id":"b/in-only","name":"In only","architecture":{"input_modalities":["text"],"output_modalities":["image"]}},
              {"id":"c/out-only","name":"Out only","architecture":{"input_modalities":["audio"],"output_modalities":["text"]}},
              {"id":"d/neither","name":"Neither","architecture":{"input_modalities":[],"output_modalities":[]}},
              {"id":"e/upper","name":"Upper","architecture":{"input_modalities":["TEXT"],"output_modalities":["Text"]}}
            ]}
            """#
        )

        XCTAssertEqual(options.map(\.id), ["a/both"])
    }

    func testABatchVariantIsExcluded() throws {
        let options = try Self.options(
            Self.catalogue([
                Self.entry(id: "vendor/model:batch", name: "Batch"),
                Self.entry(id: "vendor/model:batch-preview", name: "Batch preview"),
                Self.entry(id: "vendor/model:free", name: "Free"),
                Self.entry(id: "vendor/batchelor", name: "Not a batch"),
                Self.entry(id: "vendor/model", name: "Plain")
            ])
        )

        XCTAssertEqual(options.map(\.id), ["vendor/model:free", "vendor/batchelor", "vendor/model"])
    }

    func testEveryOptionHasTheOneEffortAndAStructuredOutputsFlag() throws {
        let options = try Self.options(
            Self.catalogue([
                Self.entry(id: "m/a", name: "A"),
                Self.entry(id: "m/b", name: "B", extra: ",\"supported_parameters\":[\"tools\",\"structured_outputs\"]"),
                Self.entry(id: "m/c", name: "C", extra: ",\"supported_parameters\":[\"tools\"]"),
                Self.entry(id: "m/d", name: "D", extra: ",\"supported_parameters\":\"structured_outputs\""),
                Self.entry(id: "m/e", name: "E", extra: ",\"supported_parameters\":null")
            ])
        )

        XCTAssertEqual(options.map(\.id), ["m/a", "m/b", "m/c", "m/d", "m/e"])
        for option in options {
            XCTAssertEqual(option.reasoningEfforts, ["default"], option.id)
            // Never absent for an OpenRouter option.
            XCTAssertNotNil(option.structuredOutputs, option.id)
        }
        // False when `supported_parameters` is missing, lacks the value or is not an array.
        XCTAssertEqual(options.map(\.structuredOutputs), [false, true, false, false, false])
    }

    // MARK: - AC-CORE-S09 as changed: order (CORE-B4)

    func testOptionsAreSortedByLowerCasedNameThenById() throws {
        let options = try Self.options(
            Self.catalogue([
                Self.entry(id: "m/3", name: "beta"),
                Self.entry(id: "m/2", name: "Alpha"),
                Self.entry(id: "m/1", name: "alpha"),
                Self.entry(id: "m/0", name: "Zeta"),
                Self.entry(id: "m/5", name: "ALPHA 2"),
                Self.entry(id: "m/4", name: "10 models")
            ])
        )

        // "10 models", "alpha" (m/1), "alpha" (m/2), "alpha 2", "beta", "zeta".
        XCTAssertEqual(options.map(\.id), ["m/4", "m/1", "m/2", "m/5", "m/3", "m/0"])
        XCTAssertEqual(options.map(\.name), ["10 models", "alpha", "Alpha", "ALPHA 2", "beta", "Zeta"])
    }

    func testTheOrderDoesNotDependOnTheOrderReceived() throws {
        let entries = [
            Self.entry(id: "z/same", name: "Same"),
            Self.entry(id: "a/same", name: "same"),
            Self.entry(id: "m/same", name: "SAME"),
            Self.entry(id: "b/other", name: "Other")
        ]

        let forward = try Self.options(Self.catalogue(entries))
        let backward = try Self.options(Self.catalogue(entries.reversed()))

        XCTAssertEqual(forward.map(\.id), ["b/other", "a/same", "m/same", "z/same"])
        XCTAssertEqual(backward, forward)
    }

    func testTheOrderFollowsUnicodeNotALanguage() throws {
        // Without locale rules: "é" sorts after every ASCII letter, wherever the phone is set up.
        let options = try Self.options(
            Self.catalogue([
                Self.entry(id: "m/1", name: "Évora"),
                Self.entry(id: "m/2", name: "zulu"),
                Self.entry(id: "m/3", name: "echo")
            ])
        )

        XCTAssertEqual(options.map(\.name), ["echo", "zulu", "Évora"])
    }

    // MARK: - AC-CORE-S09 as changed: malformed entries (CORE-A5)

    func testAMalformedEntryIsSkippedAndTheRestIsKept() throws {
        let options = try Self.options(
            #"""
            {"data":[
              {"id":"ok/first","name":"First","architecture":{"input_modalities":["text"],"output_modalities":["text"]}},
              {"id":"bad/no-name","architecture":{"input_modalities":["text"],"output_modalities":["text"]}},
              {"name":"No id","architecture":{"input_modalities":["text"],"output_modalities":["text"]}},
              {"id":7,"name":"Numeric id","architecture":{"input_modalities":["text"],"output_modalities":["text"]}},
              {"id":"bad/name","name":null,"architecture":{"input_modalities":["text"],"output_modalities":["text"]}},
              {"id":"bad/no-architecture","name":"No architecture"},
              {"id":"bad/architecture","name":"Architecture","architecture":"text"},
              {"id":"bad/no-input","name":"No input","architecture":{"output_modalities":["text"]}},
              {"id":"bad/no-output","name":"No output","architecture":{"input_modalities":["text"]}},
              {"id":"bad/input","name":"Input","architecture":{"input_modalities":"text","output_modalities":["text"]}},
              "text",
              7,
              null,
              [],
              {},
              {"id":"ok/last","name":"Last","architecture":{"input_modalities":["text",7,null],"output_modalities":["text"]},"unknown":{"a":[1,2]}}
            ]}
            """#
        )

        XCTAssertEqual(options.map(\.id), ["ok/first", "ok/last"])
    }

    func testAPriceThatIsNotAStringIsAbsent() throws {
        let options = try Self.options(
            Self.catalogue([
                Self.entry(id: "m/a", name: "A", extra: ",\"pricing\":{\"prompt\":\"0.000001\",\"completion\":\"0.000002\"}"),
                Self.entry(id: "m/b", name: "B", extra: ",\"pricing\":{\"prompt\":0.000001,\"completion\":null}"),
                Self.entry(id: "m/c", name: "C", extra: ",\"pricing\":{\"prompt\":\"0\"}"),
                Self.entry(id: "m/d", name: "D", extra: ",\"pricing\":\"free\""),
                Self.entry(id: "m/e", name: "E", extra: ",\"pricing\":{\"prompt\":{\"usd\":\"1\"},\"completion\":[\"1\"]}"),
                Self.entry(id: "m/f", name: "F")
            ])
        )

        XCTAssertEqual(options.map(\.id), ["m/a", "m/b", "m/c", "m/d", "m/e", "m/f"])
        XCTAssertEqual(options.map(\.promptPrice), ["0.000001", nil, "0", nil, nil, nil])
        XCTAssertEqual(options.map(\.completionPrice), ["0.000002", nil, nil, nil, nil, nil])
    }

    // MARK: - AC-CORE-S09 as changed: only `data` can fail the catalogue

    func testAMissingOrNonArrayDataFails() {
        let bodies = [
            "{}",
            #"{"models":[]}"#,
            #"{"data":null}"#,
            #"{"data":{}}"#,
            #"{"data":"none"}"#,
            #"{"data":7}"#,
            "[]",
            "not json",
            ""
        ]
        for body in bodies {
            XCTAssertThrowsError(try Self.options(body), body) { error in
                XCTAssertEqual(error as? OpenRouterError, .invalidResponse, body)
            }
        }
    }

    func testAnEmptyCatalogueAndACatalogueOfMalformedEntriesAreEmpty() throws {
        XCTAssertEqual(try Self.options(#"{"data":[]}"#), [])
        XCTAssertEqual(try Self.options(#"{"data":[{"id":"x"},null,3]}"#), [])
        XCTAssertEqual(try Self.options(#"{"data":[],"object":"list"}"#), [])
    }
}
