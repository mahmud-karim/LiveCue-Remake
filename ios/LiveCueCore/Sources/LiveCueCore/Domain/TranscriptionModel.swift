import Foundation

/// An entry of the on-device model catalogue.
public struct TranscriptionModel: Codable, Equatable, Sendable, Identifiable {
    public var variant: String
    public var displayName: String
    public var approximateMegabytes: Int
    public var quality: String
    public var recommended: Bool

    public var id: String {
        variant
    }

    public init(variant: String, displayName: String, approximateMegabytes: Int, quality: String, recommended: Bool) {
        self.variant = variant
        self.displayName = displayName
        self.approximateMegabytes = approximateMegabytes
        self.quality = quality
        self.recommended = recommended
    }

    public static let voz = TranscriptionModel(
        variant: "voz",
        displayName: "Voz on Assist",
        approximateMegabytes: 467,
        quality: "Transcribes when you tap Assist",
        recommended: true
    )

    public static let parakeet = TranscriptionModel(
        variant: "parakeet",
        displayName: "Live Parakeet",
        approximateMegabytes: 230,
        quality: "English live captions · 320 ms mode",
        recommended: true
    )

    /// The catalogue entry of an on-device provider; nil for a provider that streams audio.
    public static func onDevice(for provider: SpeechProvider) -> TranscriptionModel? {
        switch provider {
        case .voz: return TranscriptionModel.voz
        case .parakeet: return TranscriptionModel.parakeet
        case .meta, .nemotron, .qwen3: return nil
        }
    }
}
