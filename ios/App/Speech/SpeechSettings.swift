import Foundation
import LiveCueCore
import Observation

/// The selected speech provider and the Meta key actions (spec 01 sections 2.6 and 4.1).
@MainActor @Observable final class SpeechSettings {
    private(set) var provider: SpeechProvider
    private(set) var hasMetaKey: Bool
    private(set) var metaKeyMessage: InlineMessage? = nil
    private(set) var isTestingMetaKey: Bool = false
    @ObservationIgnored var onProviderChanged: (@MainActor (SpeechProvider) -> Void)?
    /// Installed by `AppEnvironment` from `SystemReadiness`.
    @ObservationIgnored var changeIsBlocked: (@MainActor () -> Bool)?

    private let preferences: any PreferencesStoring
    private let secrets: any SecretStoring
    private let keyTester: any MetaKeyTesting
    private let activity: SessionActivity

    init(
        preferences: any PreferencesStoring,
        secrets: any SecretStoring,
        keyTester: any MetaKeyTesting,
        activity: SessionActivity
    ) {
        self.preferences = preferences
        self.secrets = secrets
        self.keyTester = keyTester
        self.activity = activity
        // An unknown stored value reads as Meta: the preferences store answers so.
        provider = preferences.speechProvider
        hasMetaKey = secrets.read(.metaAPIKey) != nil
    }

    /// Refused during a session or while blocked.
    func select(_ provider: SpeechProvider) {
        guard provider != self.provider, !activity.isActive else { return }
        if let changeIsBlocked, changeIsBlocked() {
            return
        }
        self.provider = provider
        preferences.speechProvider = provider
        onProviderChanged?(provider)
    }

    /// The three key actions are refused during a session.
    ///
    /// The text is trimmed first (spec 01 section 2.6). What is left must not be empty and must hold no
    /// space or line break.
    @discardableResult
    func saveMetaKey(_ text: String) -> Bool {
        guard !activity.isActive else { return false }

        let key = text.trimmingCharacters(in: .whitespacesAndNewlines)
        switch APIKeyShape.check(key) {
        case .empty:
            metaKeyMessage = InlineMessage(SpeechSettings.text(of: MetaSpeechError.missingKey), isError: true)
            return false
        case .containsWhitespace:
            metaKeyMessage = InlineMessage(SpeechSettings.text(of: MetaSpeechError.malformedKey), isError: true)
            return false
        case .valid:
            break
        }

        do {
            try secrets.save(key, for: .metaAPIKey)
        } catch {
            metaKeyMessage = InlineMessage(SpeechSettings.couldNotSaveMessage(for: error), isError: true)
            return false
        }
        hasMetaKey = true
        metaKeyMessage = InlineMessage(SpeechSettings.keySavedMessage)
        return true
    }

    func testMetaKey() async {
        guard !activity.isActive, !isTestingMetaKey else { return }
        guard let key = secrets.read(.metaAPIKey) else {
            hasMetaKey = false
            metaKeyMessage = InlineMessage(SpeechSettings.text(of: MetaSpeechError.missingKey), isError: true)
            return
        }

        isTestingMetaKey = true
        do {
            try await keyTester.test(key: key)
            metaKeyMessage = InlineMessage(SpeechSettings.keyVerifiedMessage)
        } catch let failure as MetaSpeechError {
            metaKeyMessage = InlineMessage(SpeechSettings.text(of: failure), isError: true)
        } catch is CancellationError {
            // Never shown.
        } catch {
            metaKeyMessage = InlineMessage(SpeechSettings.text(of: MetaSpeechError.connection), isError: true)
        }
        isTestingMetaKey = false
    }

    func removeMetaKey() {
        guard !activity.isActive else { return }
        do {
            try secrets.remove(.metaAPIKey)
        } catch {
            metaKeyMessage = InlineMessage(SpeechSettings.couldNotRemoveMessage(for: error), isError: true)
            return
        }
        hasMetaKey = false
        metaKeyMessage = InlineMessage(SpeechSettings.keyRemovedMessage)
    }

    // MARK: - Texts (spec 01 section 8.2)

    private static let keySavedMessage = "Key saved securely on this iPhone. Meta connects directly; no PC needed."
    private static let keyVerifiedMessage = "Meta connection verified directly. No microphone audio was sent."
    private static let keyRemovedMessage = "Key removed."

    private static func text(of failure: MetaSpeechError) -> String {
        failure.errorDescription ?? ""
    }

    private static func couldNotSaveMessage(for error: any Error) -> String {
        "Could not save the key in Keychain (status \(status(of: error))). No insecure fallback was used."
    }

    private static func couldNotRemoveMessage(for error: any Error) -> String {
        "Keychain could not remove this key (status \(status(of: error))). Try again."
    }

    /// The Keychain status a store reported; -1 for an error that carries none.
    private static func status(of error: any Error) -> Int32 {
        (error as? SecretStoreFailure)?.status ?? -1
    }
}
