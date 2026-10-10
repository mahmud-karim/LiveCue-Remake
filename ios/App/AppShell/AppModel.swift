import Foundation
import Observation

/// Navigation state and the one global alert.
@MainActor @Observable final class AppModel {
    var selectedTab: AppTab = .live
    var livePath: [Route] = []
    var historyPath: [Route] = []
    var settingsPath: [Route] = []
    /// The stack of the live session screen.
    var sessionPath: [Route] = []
    private(set) var alertMessage: String? = nil

    /// Setting false clears the message.
    var isAlertPresented: Bool {
        get {
            alertMessage != nil
        }
        set {
            if !newValue {
                alertMessage = nil
            }
        }
    }

    init() {}

    /// A newer message replaces an older one.
    func presentAlert(_ message: String) {
        alertMessage = message
    }

    /// Empties all four paths.
    func sessionDidStart() {
        clearPaths()
    }

    /// Empties all four paths and selects the Live tab.
    func sessionDidEnd() {
        clearPaths()
        selectedTab = .live
    }

    private func clearPaths() {
        livePath = []
        historyPath = []
        settingsPath = []
        sessionPath = []
    }
}
