import Foundation

/// Microphone and camera always granted, with no prompt.
struct FixturePermissions: PermissionProviding {
    init() {}

    func requestMicrophone() async -> Bool {
        true
    }

    func requestCamera() async -> Bool {
        true
    }
}

/// The Meta key test of the test seam: it succeeds at once and opens no socket.
struct FixtureMetaKeyTester: MetaKeyTesting {
    init() {}

    func test(key: String) async throws {}
}
