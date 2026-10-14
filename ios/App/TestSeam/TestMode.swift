import Foundation

/// The fourteen modifiers of the test seam (architecture 4.11, and `-fluid-ready` since D-29). Each is
/// set by its launch argument.
struct TestMode: Equatable, Sendable {
    /// `-on-device-ready`
    var onDeviceReady = false
    /// `-fluid-ready`
    var fluidReady = false
    /// `-meta-no-key`
    var metaNoKey = false
    /// `-meta-key-persistence`
    var metaKeyPersistence = false
    /// `-openrouter-ui-reset`
    var openRouterUIReset = false
    /// `-openrouter-persistence-test`
    var openRouterPersistenceTest = false
    /// `-pc-offline`
    var pcOffline = false
    /// `-pc-catalog-slow`
    var pcCatalogSlow = false
    /// `-pc-model-slow-start`
    var pcModelSlowStart = false
    /// `-pc-model-start-error`
    var pcModelStartError = false
    /// `-assist-fails-once`
    var assistFailsOnce = false
    /// `-pairing-persistence-test`
    var pairingPersistenceTest = false
    /// `-save-pairing-fixture`
    var savePairingFixture = false
    /// `-drop-test-keychain`
    var dropTestKeychain = false

    /// Base test mode: no modifier.
    init() {}

    init(arguments: [String]) {
        let given = Set(arguments)
        onDeviceReady = given.contains("-on-device-ready")
        fluidReady = given.contains("-fluid-ready")
        metaNoKey = given.contains("-meta-no-key")
        metaKeyPersistence = given.contains("-meta-key-persistence")
        openRouterUIReset = given.contains("-openrouter-ui-reset")
        openRouterPersistenceTest = given.contains("-openrouter-persistence-test")
        pcOffline = given.contains("-pc-offline")
        pcCatalogSlow = given.contains("-pc-catalog-slow")
        pcModelSlowStart = given.contains("-pc-model-slow-start")
        pcModelStartError = given.contains("-pc-model-start-error")
        assistFailsOnce = given.contains("-assist-fails-once")
        pairingPersistenceTest = given.contains("-pairing-persistence-test")
        savePairingFixture = given.contains("-save-pairing-fixture")
        dropTestKeychain = given.contains("-drop-test-keychain")
    }
}
