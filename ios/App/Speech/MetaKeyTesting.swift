import Foundation
import LiveCueCore

/// The Meta key test. Throws `MetaSpeechError`.
protocol MetaKeyTesting: Sendable {
    func test(key: String) async throws
}

/// The key test over a real socket: `MetaConnectionTest`, which sends no audio.
struct LiveMetaKeyTester: MetaKeyTesting {
    private let transport: any WebSocketTransport
    private let clock: any AppClock

    init(transport: any WebSocketTransport, clock: any AppClock) {
        self.transport = transport
        self.clock = clock
    }

    func test(key: String) async throws {
        try await MetaConnectionTest.run(key: key, transport: transport, timing: clock.coreTiming)
    }
}
