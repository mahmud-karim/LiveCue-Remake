import Foundation

/// The PC speech model's status object (protocol 9.1).
public struct PCModelStatus: Codable, Equatable, Sendable {
    /// stopped, starting, ready, stopping, error
    public var state: String
    public var model: String?
    /// Required on decode.
    public var busy: Bool
    /// Required on decode; must be an integer.
    public var elapsedSeconds: Int
    /// Required on decode.
    public var message: String

    public init(state: String, model: String? = nil, busy: Bool = false, elapsedSeconds: Int = 0, message: String) {
        self.state = state
        self.model = model
        self.busy = busy
        self.elapsedSeconds = elapsedSeconds
        self.message = message
    }

    /// A start or a stop is running.
    public var isChanging: Bool {
        state == "starting" || state == "stopping"
    }

    /// State "ready" and model == provider id; `busy` is not considered.
    public func isReady(for provider: SpeechProvider) -> Bool {
        state == "ready" && model == provider.rawValue
    }

    /// The seven rows of spec 02 section 8.2.
    public func homeLabel(for provider: SpeechProvider) -> String {
        if otherLoadedModel(than: provider) != nil {
            return "Different model loaded"
        }
        switch state {
        case "starting":
            return "Loading · \(elapsedSeconds)s"
        case "stopping":
            return "Stopping · \(elapsedSeconds)s"
        case "ready":
            return busy ? "In use" : "Ready"
        case "error":
            return "Error · open Settings"
        default:
            return "Stopped · open Settings"
        }
    }

    /// The loaded model's id when it is not `provider` and the state is not "stopped".
    public func otherLoadedModel(than provider: SpeechProvider) -> String? {
        guard let model, model != provider.rawValue, state != "stopped" else { return nil }
        return model
    }
}
