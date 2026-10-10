import Foundation

/// Every failure of the OpenRouter client, with the text the user sees (spec 02 section 4.7, as changed
/// by `04-ios-architecture.md` 1.3).
public enum OpenRouterError: LocalizedError, Equatable, Sendable {
    case missingKey, malformedKey, invalidKey, credits, rateLimited, unavailable, invalidResponse, truncated, noModel

    public var errorDescription: String? {
        switch self {
        case .missingKey:
            return "Add your OpenRouter API key in Assistant models & timing first."
        case .malformedKey:
            return "This key contains spaces or line breaks. Paste it again exactly as issued."
        case .invalidKey:
            return "OpenRouter rejected this API key. Replace it in Assistant models & timing."
        case .credits:
            return "OpenRouter credits are insufficient. Check your OpenRouter account balance or key spending limit."
        case .rateLimited:
            return "OpenRouter rate-limited this request. Wait briefly and try again."
        case .unavailable:
            return "OpenRouter could not complete this request. Check your connection or try again later. No fallback service was used."
        case .invalidResponse:
            return "OpenRouter returned an incomplete or unreadable answer. No fallback model was used."
        case .truncated:
            return "The selected model reached the response limit before finishing. Try a shorter question or another model."
        case .noModel:
            return "Choose an OpenRouter model in Assistant models & timing first."
        }
    }
}
