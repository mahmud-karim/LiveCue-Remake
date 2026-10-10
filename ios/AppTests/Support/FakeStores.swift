import Foundation
import LiveCueCore
@testable import LiveCueRemake

/// Preferences in memory.
@MainActor final class FakePreferences: PreferencesStoring {
    var assistantInstruction: String = ""
    var speechProvider: SpeechProvider = .meta
    var assistantConfiguration = AssistantConfiguration()
    /// What `remember` stored, by provider.
    private(set) var remembered: [AssistantProvider: AssistantConfiguration] = [:]
    private(set) var resetCount = 0

    init() {}

    func rememberedConfiguration(for provider: AssistantProvider) -> AssistantConfiguration? {
        remembered[provider]
    }

    func remember(_ configuration: AssistantConfiguration) {
        remembered[configuration.provider] = configuration
    }

    func resetAssistantConfiguration() {
        remembered = [:]
        assistantConfiguration = AssistantConfiguration()
        resetCount += 1
    }
}

/// API keys in memory. Set `failStatus` to make `save` and `remove` throw that status.
final class FakeSecrets: SecretStoring, @unchecked Sendable {
    private let lock = NSLock()
    private var values: [SecretKind: String]
    private var failure: Int32?

    init(_ values: [SecretKind: String] = [:]) {
        self.values = values
    }

    var failStatus: Int32? {
        get { lock.withLock { failure } }
        set { lock.withLock { failure = newValue } }
    }

    func read(_ kind: SecretKind) -> String? {
        lock.withLock { values[kind] }
    }

    func save(_ value: String, for kind: SecretKind) throws {
        try lock.withLock {
            if let failure {
                throw SecretStoreFailure(status: failure)
            }
            values[kind] = value
        }
    }

    func remove(_ kind: SecretKind) throws {
        try lock.withLock {
            if let failure {
                throw SecretStoreFailure(status: failure)
            }
            values[kind] = nil
        }
    }
}

/// A pairing in memory. Set `saveFails` to make `save` throw and keep the old pairing.
final class FakePairingStore: PairingStoring, @unchecked Sendable {
    private let lock = NSLock()
    private var pairing: PairingPayload?
    private var failsToSave = false
    private var saves: [PairingPayload] = []
    private var keychainRemovals = 0

    init(_ pairing: PairingPayload? = nil) {
        self.pairing = pairing
    }

    var saveFails: Bool {
        get { lock.withLock { failsToSave } }
        set { lock.withLock { failsToSave = newValue } }
    }

    /// Every pairing that was saved, in order.
    var saved: [PairingPayload] {
        lock.withLock { saves }
    }

    var removeKeychainCopyCalls: Int {
        lock.withLock { keychainRemovals }
    }

    func load() -> PairingPayload? {
        lock.withLock { pairing }
    }

    func save(_ pairing: PairingPayload) throws {
        try lock.withLock {
            if failsToSave {
                throw CocoaError(.fileWriteUnknown)
            }
            self.pairing = pairing
            saves.append(pairing)
        }
    }

    func removeKeychainCopy() {
        lock.withLock { keychainRemovals += 1 }
    }
}

struct FakePermissions: PermissionProviding {
    private let microphone: Bool
    private let camera: Bool

    init(microphone: Bool = true, camera: Bool = true) {
        self.microphone = microphone
        self.camera = camera
    }

    func requestMicrophone() async -> Bool {
        microphone
    }

    func requestCamera() async -> Bool {
        camera
    }
}

/// A clipboard that keeps what was copied.
@MainActor final class FakeClipboard: ClipboardWriting {
    private(set) var copies: [String] = []

    init() {}

    func copy(_ text: String) {
        copies.append(text)
    }
}
