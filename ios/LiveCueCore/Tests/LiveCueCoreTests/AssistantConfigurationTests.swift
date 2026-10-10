import Foundation
import LiveCueCore
import XCTest

/// Defaults, legacy decoding, `effort(keeping:)` and `supports(_:)`.
final class AssistantConfigurationTests: XCTestCase {
    // MARK: - Defaults

    func testFirstRunDefaultIsCodexSolLow() {
        let configuration = AssistantConfiguration()
        XCTAssertEqual(configuration.model, "gpt-5.6-sol")
        XCTAssertEqual(configuration.reasoningEffort, "low")
        XCTAssertEqual(configuration.provider, .codex)
    }

    func testDefaultForEachProvider() {
        XCTAssertEqual(AssistantConfiguration.default(for: .codex), AssistantConfiguration())
        XCTAssertEqual(
            AssistantConfiguration.default(for: .codex),
            AssistantConfiguration(model: "gpt-5.6-sol", reasoningEffort: "low", provider: .codex)
        )
        XCTAssertEqual(
            AssistantConfiguration.default(for: .openrouter),
            AssistantConfiguration(model: "", reasoningEffort: "default", provider: .openrouter)
        )
        for provider in AssistantProvider.allCases {
            XCTAssertEqual(AssistantConfiguration.default(for: provider).provider, provider)
        }
    }

    func testProviderDisplayNamesAndRawValues() {
        XCTAssertEqual(AssistantProvider.allCases, [.codex, .openrouter])
        XCTAssertEqual(AssistantProvider.codex.rawValue, "codex")
        XCTAssertEqual(AssistantProvider.openrouter.rawValue, "openrouter")
        XCTAssertEqual(AssistantProvider.codex.displayName, "PC \u{00B7} Codex")
        XCTAssertEqual(AssistantProvider.openrouter.displayName, "OpenRouter \u{00B7} direct")
    }

    // MARK: - Coding

    /// AC-CORE-036.
    func testALegacyConfigurationWithoutAProviderMeansCodex() throws {
        let legacy = Data("{\"model\":\"gpt-6-luna\",\"reasoningEffort\":\"low\"}".utf8)

        let decoded = try JSONDecoder().decode(AssistantConfiguration.self, from: legacy)
        XCTAssertEqual(decoded.provider, .codex)
        XCTAssertEqual(decoded.model, "gpt-6-luna")
        XCTAssertEqual(decoded.reasoningEffort, "low")

        let stored = try LiveCueJSON.decode(AssistantConfiguration.self, from: legacy)
        XCTAssertEqual(stored, AssistantConfiguration(model: "gpt-6-luna", reasoningEffort: "low", provider: .codex))
    }

    func testANullProviderMeansCodex() throws {
        let json = Data("{\"model\":\"gpt-6-luna\",\"reasoningEffort\":\"low\",\"provider\":null}".utf8)
        XCTAssertEqual(try LiveCueJSON.decode(AssistantConfiguration.self, from: json).provider, .codex)
    }

    /// AC-CORE-037.
    func testAnOpenRouterConfigurationRoundTrips() throws {
        let configuration = AssistantConfiguration(model: "test/model", reasoningEffort: "default", provider: .openrouter)

        let decoded = try JSONDecoder().decode(AssistantConfiguration.self, from: JSONEncoder().encode(configuration))
        XCTAssertEqual(decoded, configuration)
        let stored = try LiveCueJSON.decode(AssistantConfiguration.self, from: LiveCueJSON.encode(configuration))
        XCTAssertEqual(stored, configuration)
    }

    func testTheProviderIsAlwaysWritten() throws {
        let codex = try XCTUnwrap(JSONValue.parse(LiveCueJSON.encode(AssistantConfiguration())))
        XCTAssertEqual(codex["provider"], .string("codex"))
        XCTAssertEqual(codex["model"], .string("gpt-5.6-sol"))
        XCTAssertEqual(codex["reasoningEffort"], .string("low"))
        XCTAssertEqual(codex.objectValue?.count, 3)

        let openRouter = try XCTUnwrap(JSONValue.parse(LiveCueJSON.encode(AssistantConfiguration.default(for: .openrouter))))
        XCTAssertEqual(openRouter["provider"], .string("openrouter"))
        XCTAssertEqual(openRouter["model"], .string(""))
        XCTAssertEqual(openRouter["reasoningEffort"], .string("default"))

        // A legacy value gains the member the next time it is written.
        let legacy = try LiveCueJSON.decode(
            AssistantConfiguration.self,
            from: Data("{\"model\":\"gpt-6-luna\",\"reasoningEffort\":\"low\"}".utf8)
        )
        let rewritten = try XCTUnwrap(JSONValue.parse(LiveCueJSON.encode(legacy)))
        XCTAssertEqual(rewritten["provider"], .string("codex"))
    }

    func testModelAndEffortAreRequiredAndAnUnknownProviderFails() {
        let noModel = Data("{\"reasoningEffort\":\"low\",\"provider\":\"codex\"}".utf8)
        XCTAssertThrowsError(try LiveCueJSON.decode(AssistantConfiguration.self, from: noModel))

        let noEffort = Data("{\"model\":\"gpt-5.6-sol\",\"provider\":\"codex\"}".utf8)
        XCTAssertThrowsError(try LiveCueJSON.decode(AssistantConfiguration.self, from: noEffort))

        let unknownProvider = Data("{\"model\":\"m\",\"reasoningEffort\":\"low\",\"provider\":\"somewhere\"}".utf8)
        XCTAssertThrowsError(try LiveCueJSON.decode(AssistantConfiguration.self, from: unknownProvider))
    }

    func testUnknownKeysAreIgnoredAndAnEmptyModelIsAllowed() throws {
        let json = Data("{\"model\":\"\",\"reasoningEffort\":\"default\",\"provider\":\"openrouter\",\"preset\":7}".utf8)
        XCTAssertEqual(
            try LiveCueJSON.decode(AssistantConfiguration.self, from: json),
            AssistantConfiguration.default(for: .openrouter)
        )
    }

    // MARK: - effort(keeping:)

    func testEffortKeepsTheCurrentOneWhenTheModelListsIt() {
        let option = AssistantModelOption(id: "gpt-test", name: "GPT Test", reasoningEfforts: ["minimal", "low", "high"])
        XCTAssertEqual(option.effort(keeping: "high"), "high")
        XCTAssertEqual(option.effort(keeping: "minimal"), "minimal")
    }

    func testEffortFallsToTheFirstListedOne() {
        let option = AssistantModelOption(id: "gpt-test", name: "GPT Test", reasoningEfforts: ["medium", "high"])
        XCTAssertEqual(option.effort(keeping: "low"), "medium")
        XCTAssertEqual(option.effort(keeping: ""), "medium")
        XCTAssertEqual(option.effort(keeping: "HIGH"), "medium", "Efforts are compared exactly.")
    }

    func testEffortFallsToLowWhenTheModelListsNone() {
        let option = AssistantModelOption(id: "gpt-test", name: "GPT Test", reasoningEfforts: [])
        XCTAssertEqual(option.effort(keeping: "xhigh"), "low")
        XCTAssertEqual(option.effort(keeping: "low"), "low")
    }

    // MARK: - supports(_:)

    func testSupportsNeedsTheModelAndItsEffort() {
        let catalogue = [
            AssistantModelOption(id: "gpt-5.6-sol", name: "Sol", reasoningEfforts: ["none", "low", "medium"]),
            AssistantModelOption(id: "gpt-5.6-luna", name: "Luna", reasoningEfforts: ["low", "high"])
        ]
        XCTAssertTrue(catalogue.supports(AssistantConfiguration()))
        XCTAssertTrue(catalogue.supports(AssistantConfiguration(model: "gpt-5.6-luna", reasoningEffort: "high")))
        XCTAssertTrue(catalogue.supports(AssistantConfiguration(model: "gpt-5.6-sol", reasoningEffort: "none")))

        XCTAssertFalse(
            catalogue.supports(AssistantConfiguration(model: "gpt-5.6-sol", reasoningEffort: "high")),
            "The effort belongs to another model."
        )
        XCTAssertFalse(catalogue.supports(AssistantConfiguration(model: "gpt-5.6-terra", reasoningEffort: "low")))
        XCTAssertFalse(catalogue.supports(AssistantConfiguration(model: "GPT-5.6-SOL", reasoningEffort: "low")))
        XCTAssertFalse(catalogue.supports(AssistantConfiguration(model: "", reasoningEffort: "low")))
    }

    func testSupportsIgnoresTheProviderAndAnEmptyCatalogueSupportsNothing() {
        let catalogue = [AssistantModelOption(id: "test/model", name: "Test", reasoningEfforts: ["default"])]
        XCTAssertTrue(
            catalogue.supports(AssistantConfiguration(model: "test/model", reasoningEffort: "default", provider: .openrouter))
        )
        XCTAssertTrue(
            catalogue.supports(AssistantConfiguration(model: "test/model", reasoningEffort: "default", provider: .codex))
        )
        XCTAssertFalse(catalogue.supports(AssistantConfiguration.default(for: .openrouter)), "Nothing chosen yet.")

        let empty: [AssistantModelOption] = []
        XCTAssertFalse(empty.supports(AssistantConfiguration()))
    }

    func testTheFixtureCatalogueDecodesAndSupportsItsModel() throws {
        struct Catalogue: Decodable {
            let models: [AssistantModelOption]
        }
        let catalogue = try LiveCueJSON.decode(Catalogue.self, from: Data(CoreFixtures.catalogueJSON.utf8))
        XCTAssertEqual(catalogue.models, [AssistantModelOption(id: "gpt-test", name: "GPT Test", reasoningEfforts: ["low"])])
        XCTAssertNil(catalogue.models.first?.structuredOutputs)
        XCTAssertTrue(catalogue.models.supports(AssistantConfiguration(model: "gpt-test", reasoningEffort: "low")))
        XCTAssertFalse(catalogue.models.supports(AssistantConfiguration(model: "gpt-test", reasoningEffort: "high")))
    }
}
