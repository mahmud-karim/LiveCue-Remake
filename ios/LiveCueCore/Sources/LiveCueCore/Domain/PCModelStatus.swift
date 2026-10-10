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

    public var isChanging: Bool {
        // STUB(WP-02)
        false
    }

    /// State "ready" and model == provider id; `busy` is not considered.
    public func isReady(for provider: SpeechProvider) -> Bool {
        // STUB(WP-02)
        false
    }

    /// The seven rows of spec 02 section 8.2.
    public func homeLabel(for provider: SpeechProvider) -> String {
        // STUB(WP-02)
        ""
    }

    /// The loaded model's id when it is not `provider` and the state is not "stopped".
    public func otherLoadedModel(than provider: SpeechProvider) -> String? {
        // STUB(WP-02)
        nil
    }
}
