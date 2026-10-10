import Foundation
import LiveCueCore

/// The fixed values of the test seam. Nothing here is a real host, key, token, model or answer.
enum FixtureValues {
    static let segmentText = "What is the main advantage of local transcription?"
    static let partialAnswer = "Your audio stays on the iPhone,"
    static let answer = "Your audio stays on the iPhone, so nothing you say leaves the device to be transcribed."
    static let details = "Local transcription also keeps working without a network connection."
    static let metaKey = "fixture-meta-key"

    /// The two 5.6 models include "none"; the others offer low, medium and high.
    static let codexCatalogue: [AssistantModelOption] = [
        AssistantModelOption(id: "gpt-6-astra", name: "GPT-6 Astra", reasoningEfforts: ["low", "medium", "high"]),
        AssistantModelOption(id: "gpt-6-luna", name: "GPT-6 Luna", reasoningEfforts: ["low", "medium", "high"]),
        AssistantModelOption(id: "gpt-5.6-sol", name: "GPT-5.6 Sol", reasoningEfforts: ["none", "low", "medium", "high"]),
        AssistantModelOption(id: "gpt-5.6-luna", name: "GPT-5.6 Luna", reasoningEfforts: ["none", "low", "medium", "high"]),
        AssistantModelOption(id: "gpt-5.3-codex-spark", name: "GPT-5.3 Codex Spark", reasoningEfforts: ["low", "medium", "high"])
    ]

    static let openRouterCatalogue: [AssistantModelOption] = [
        AssistantModelOption(id: "test/direct", name: "Test Direct", reasoningEfforts: ["default"])
    ]

    /// A reserved test domain, an explicit port and a trailing slash.
    static let pairingEndpoint = "https://pc.example.test:11443/"
    /// The synthetic token of protocol 4.2: 43 characters of the shape C-02 requires. It opens nothing.
    static let pairingToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"

    /// https://pc.example.test:11443/ and the synthetic token.
    ///
    /// Both texts are constants that pass the pairing rules; the core's `PairingPayloadTests` and the
    /// app's `AppEnvironmentSmokeTests` prove it. The precondition can therefore not fire.
    static var pairing: PairingPayload {
        guard let pairing = try? PairingPayload.manual(address: pairingEndpoint, token: pairingToken) else {
            preconditionFailure("The fixture pairing is not a valid pairing payload.")
        }
        return pairing
    }

    /// The fixture timings. The fixture answer service replaces model and effort with the request's.
    static let execution = RelayExecution(
        model: "gpt-5.6-sol",
        reasoningEffort: "low",
        codexMs: 600,
        relayTotalMs: 620,
        requestReadMs: 2,
        relayOverheadMs: 20
    )

    static let openRouterUsage = AssistantUsage(
        generationId: "fixture-generation",
        promptTokens: 120,
        completionTokens: 40,
        costCredits: 0.0001
    )

    static let assistPartialSeconds: TimeInterval = 0.4
    static let pcModelChangeSeconds: TimeInterval = 1
    static let pcModelSlowStartSeconds: TimeInterval = 6
    static let catalogueSlowSeconds: TimeInterval = 8
    static let preparationHoldSeconds: TimeInterval = 2
}
