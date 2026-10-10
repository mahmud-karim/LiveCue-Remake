import SwiftUI

extension View {
    /// Puts the environment and every state owner and view model into the SwiftUI environment, so a view
    /// reads what it needs with `@Environment(Type.self)`.
    ///
    /// `SystemReadiness` is not here: it stores nothing and is not observable. View models read it.
    func appEnvironment(_ environment: AppEnvironment) -> some View {
        self
            .environment(environment)
            .appStateOwners(environment)
            .appViewModels(environment)
    }

    private func appStateOwners(_ environment: AppEnvironment) -> some View {
        self
            .environment(environment.app)
            .environment(environment.activity)
            .environment(environment.toasts)
            .environment(environment.speech)
            .environment(environment.assistant)
            .environment(environment.models)
            .environment(environment.relay)
            .environment(environment.pairing)
            .environment(environment.history)
            .environment(environment.session)
    }

    private func appViewModels(_ environment: AppEnvironment) -> some View {
        self
            .environment(environment.home)
            .environment(environment.live)
            .environment(environment.assistantLab)
            .environment(environment.settings)
            .environment(environment.transcription)
            .environment(environment.pcSpeechModel)
            .environment(environment.metaKey)
            .environment(environment.pairingScreen)
            .environment(environment.modelLibrary)
            .environment(environment.benchmark)
            .environment(environment.historyScreen)
    }
}
