import Foundation

/// Every method throws `OpenRouterError`, or `CancellationError` when the calling task was cancelled
/// (CORE-A11). One request per call, no retry, no fallback.
public struct OpenRouterClient: Sendable {
    public static let baseURL = "https://openrouter.ai/api/v1/"
    public static let keyCheckSeconds: TimeInterval = 20
    public static let catalogueSeconds: TimeInterval = 20
    public static let assistIdleSeconds: TimeInterval = 65
    public static let assistTotalSeconds: TimeInterval = 120

    private let transport: any HTTPTransport
    private let timing: CoreTiming

    public init(transport: any HTTPTransport, timing: CoreTiming = .system) {
        self.transport = transport
        self.timing = timing
    }

    public func verifyKey(_ key: String) async throws {
        // STUB(WP-03)
        throw OpenRouterError.unavailable
    }

    /// No `Authorization` header. A malformed entry is skipped (CORE-A5); only a missing or non-array
    /// `data` fails. Sorted by lower-cased name, then by id, without locale rules (CORE-B4).
    public func models() async throws -> [AssistantModelOption] {
        // STUB(WP-03)
        throw OpenRouterError.unavailable
    }

    /// Check order: empty `option.id` gives `.noModel`; the body is built; the key is checked; the request
    /// is sent. The embedded context JSON has no `instruction` member (CORE-A4). `onUpdate` receives the
    /// whole answer so far and is awaited.
    public func assist(
        _ request: AssistRequest,
        option: AssistantModelOption,
        key: String,
        throughSegmentId: UUID?,
        onUpdate: (@Sendable (String) async -> Void)? = nil
    ) async throws -> AssistResponse {
        // STUB(WP-03)
        throw OpenRouterError.unavailable
    }
}
