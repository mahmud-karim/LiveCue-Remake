import SwiftUI

/// Maps a `Route` to its screen. Each tab's stack and the session stack attach one
/// `.navigationDestination(for: Route.self) { RouteDestination(route: $0) }`.
struct RouteDestination: View {
    let route: Route

    var body: some View {
        switch route {
        case .transcription:
            TranscriptionScreen()
        case .pairing:
            PairingScreen()
        case .assistantLab:
            AssistantLabScreen()
        case .modelLibrary:
            ModelLibraryScreen()
        case .benchmark:
            BenchmarkScreen()
        case .metaKey:
            MetaKeyScreen()
        case .pcSpeechModel:
            PCSpeechModelScreen()
        case .openRouterModels:
            OpenRouterModelsScreen()
        case .reasoning:
            ReasoningScreen()
        case .runDetails(let turnID):
            RunDetailsScreen(turnID: turnID)
        case .sessionDetail(let sessionID):
            SessionDetailScreen(sessionID: sessionID)
        }
    }
}
