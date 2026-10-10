import SwiftUI

/// The root container. With a session it shows the live session screen on its own stack; without one,
/// the three tabs. On top of either: the one global "LiveCue" alert and the toast.
///
/// It also injects the resolved theme, forces the light appearance, starts the environment once and
/// tells it when the scene becomes active.
struct RootView: View {
    @Environment(AppEnvironment.self) private var environment
    @Environment(AppModel.self) private var app
    @Environment(SessionController.self) private var session
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency
    @Environment(\.colorSchemeContrast) private var contrast

    /// Counts the times the live screen went away: the trigger of the "listening stopped" haptic.
    @State private var sessionEndCount = 0

    var body: some View {
        @Bindable var app = app

        ZStack {
            if session.showsLiveScreen {
                SessionStack()
            } else {
                TabShell()
            }
            ToastOverlay()
        }
        .environment(\.theme, resolvedTheme)
        .alert("LiveCue", isPresented: $app.isAlertPresented) {
            Button("OK") {}
        } message: {
            Text(app.alertMessage ?? "")
        }
        .haptic(.listeningStopped, trigger: sessionEndCount)
        .onChange(of: session.showsLiveScreen) { wasShowing, isShowing in
            if wasShowing && !isShowing {
                sessionEndCount += 1
            }
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active {
                environment.sceneBecameActive()
            }
        }
        .task {
            await environment.bootstrap()
        }
        .preferredColorScheme(.light)
    }

    private var resolvedTheme: Theme {
        Theme.daylight.resolved(
            reduceTransparency: reduceTransparency,
            increaseContrast: contrast == .increased
        )
    }
}

/// The three tabs, each a navigation stack over its own path.
private struct TabShell: View {
    @Environment(AppModel.self) private var app
    @Environment(\.theme) private var theme

    var body: some View {
        @Bindable var app = app

        TabView(selection: $app.selectedTab) {
            Tab("Live", systemImage: "waveform", value: AppTab.live) {
                NavigationStack(path: $app.livePath) {
                    HomeScreen()
                        .navigationDestination(for: Route.self) { route in
                            RouteDestination(route: route)
                        }
                }
                .tint(theme.accent.fill)
            }

            Tab("History", systemImage: "clock", value: AppTab.history) {
                NavigationStack(path: $app.historyPath) {
                    HistoryScreen()
                        .navigationDestination(for: Route.self) { route in
                            RouteDestination(route: route)
                        }
                }
                .tint(theme.accent.fill)
            }

            Tab("Settings", systemImage: "gearshape", value: AppTab.settings) {
                NavigationStack(path: $app.settingsPath) {
                    SettingsScreen()
                        .navigationDestination(for: Route.self) { route in
                            RouteDestination(route: route)
                        }
                }
                .tint(theme.accent.fill)
            }
        }
        .tint(theme.accent.strong)
        .lcTabBarChrome()
    }
}

/// The live session screen on its own stack. It replaces the tabs while a session exists.
private struct SessionStack: View {
    @Environment(AppModel.self) private var app
    @Environment(\.theme) private var theme

    var body: some View {
        @Bindable var app = app

        NavigationStack(path: $app.sessionPath) {
            LiveSessionScreen()
                .navigationDestination(for: Route.self) { route in
                    RouteDestination(route: route)
                }
        }
        .tint(theme.accent.fill)
    }
}
