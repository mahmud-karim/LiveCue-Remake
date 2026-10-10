import Foundation

/// The relay's routes (protocol 6.1), with the phone's timeout for each.
public enum RelayRoute: Equatable, Sendable {
    case health, pairVerify, models, localModel, localModelStart, localModelStop
    case assist, sessionSummary, cancel(requestId: String), speech

    /// "GET", "POST" or "DELETE".
    public var method: String {
        switch self {
        case .health, .models, .localModel, .speech: return "GET"
        case .pairVerify, .localModelStart, .localModelStop, .assist, .sessionSummary: return "POST"
        case .cancel: return "DELETE"
        }
    }

    /// With a leading slash, exactly as protocol 6.1.
    public var path: String {
        switch self {
        case .health: return "/v1/health"
        case .pairVerify: return "/v1/pair/verify"
        case .models: return "/v1/models"
        case .localModel: return "/v1/local-model"
        case .localModelStart: return "/v1/local-model/start"
        case .localModelStop: return "/v1/local-model/stop"
        case .assist: return "/v1/assist"
        case .sessionSummary: return "/v1/session-summary"
        case .cancel(let requestId): return "/v1/requests/" + requestId
        case .speech: return "/v1/speech"
        }
    }

    public var hasBody: Bool {
        switch self {
        case .pairVerify, .localModelStart, .localModelStop, .assist, .sessionSummary: return true
        case .health, .models, .localModel, .cancel, .speech: return false
        }
    }

    /// Protocol 6.1: the total time from sending the request to having the complete response.
    /// For the speech route it is C-59, the limit for connecting plus model loading.
    public var phoneTimeout: TimeInterval {
        switch self {
        case .health, .localModel, .cancel: return 8
        case .pairVerify, .localModelStart, .localModelStop: return 15
        case .models: return 45
        case .assist, .sessionSummary: return 70
        case .speech: return RelayConstants.speechConnectSeconds
        }
    }
}
