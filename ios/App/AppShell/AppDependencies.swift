import Foundation

/// The protocol-typed dependencies of the app: everything that touches a microphone, a network, a clock
/// or a store. `live()` is in `AppEnvironment+Live.swift`, `fixture(_:)` in `AppEnvironment+Fixture.swift`.
@MainActor struct AppDependencies {
    var clock: any AppClock
    var permissions: any PermissionProviding
    var clipboard: any ClipboardWriting
    var preferences: any PreferencesStoring
    var secrets: any SecretStoring
    var pairingStore: any PairingStoring
    var sessions: any SessionStoring
    var relayGateway: any RelayGateway
    var answers: any AnswerServiceProviding
    var openRouterKeyVerifier: any OpenRouterKeyVerifying
    var metaKeyTester: any MetaKeyTesting
    var engineLoader: any OnDeviceEngineLoading
    /// The recogniser built into iOS. The default is the one of an iPhone that has none.
    var systemSpeech: any SystemSpeechRecognizing = UnavailableSystemSpeech()
    /// Built after the model manager exists, because on-device transcribers take their engine from it.
    var makeTranscriberFactory: @MainActor (OnDeviceModelManager) -> any SpeechTranscriberFactory
}
