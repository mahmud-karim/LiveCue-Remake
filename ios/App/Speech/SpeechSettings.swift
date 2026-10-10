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

    init(
        preferences: any PreferencesStoring,
        secrets: any SecretStoring,
        keyTester: any MetaKeyTesting,
        activity: SessionActivity
    ) {
        // STUB(WP-09)
        provider = preferences.speechProvider
        hasMetaKey = secrets.read(.metaAPIKey) != nil
    }

    /// Refused during a session or while blocked.
    func select(_ provider: SpeechProvider) {
        // STUB(WP-09)
    }

    /// The three key actions are refused during a session.
    @discardableResult
    func saveMetaKey(_ text: String) -> Bool {
        // STUB(WP-09)
        false
    }

    func testMetaKey() async {
        // STUB(WP-09)
    }

    func removeMetaKey() {
        // STUB(WP-09)
    }
}
