import Foundation

/// Every failure of the direct Meta path, with the text the user sees (spec 02 section 5.6, as changed by
/// `04-ios-architecture.md` 1.3).
public enum MetaSpeechError: LocalizedError, Equatable, Sendable {
    case missingKey, malformedKey, authentication, provider, connection

    public var errorDescription: String? {
        switch self {
        case .missingKey:
            return "Add your Meta API key in Transcription. It stays in this iPhone's Keychain."
        case .malformedKey:
            return "This key contains spaces or line breaks. Paste it again exactly as issued."
        case .authentication:
            return "Meta did not accept the connection. Check the API key, account access, and billing in Transcription."
        case .provider:
            return "Meta transcription stopped. Check API access, billing, or rate limits before retrying."
        case .connection:
            return "Could not connect directly to Meta. Check this iPhone's internet connection and retry. No PC is required for Meta transcription."
        }
    }
}
