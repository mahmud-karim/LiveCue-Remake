import OSLog
import SwiftUI

@main
struct LiveCueApp: App {
    init() {
        let speechTypes = SpeechPackages.linkedTypeNames.joined(separator: ", ")
        Logger(subsystem: "com.mahmudkarim.LiveCueRemake", category: "launch")
            .info("Speech packages linked: \(speechTypes, privacy: .public)")
    }

    var body: some Scene {
        WindowGroup {
            PlaceholderRootView()
                .environment(\.theme, .daylight)
                .tint(Theme.daylight.accent.fill)
                // Daylight is the only theme. Info.plist forces the light style as well.
                .preferredColorScheme(.light)
        }
    }
}
