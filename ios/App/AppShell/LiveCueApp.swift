import SwiftUI

/// The app entry. It builds the environment once, from the launch arguments, and hands it to the root
/// view. Daylight is the only theme: the root view forces the light appearance, and Info.plist does too.
@main
struct LiveCueApp: App {
    @State private var environment: AppEnvironment

    init() {
        _environment = State(initialValue: AppEnvironment.make(LaunchConfiguration.current))
    }

    var body: some Scene {
        WindowGroup {
            RootView()
                .appEnvironment(environment)
        }
    }
}
