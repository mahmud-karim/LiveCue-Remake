import Foundation

enum LaunchMode: Equatable, Sendable {
    case live
    case fixture(TestMode)
}

/// What the launch arguments ask of the app.
struct LaunchConfiguration: Equatable, Sendable {
    /// The argument every UI test passes.
    static let uiTestingArgument = "-ui-testing"
    /// Set by Xcode in the environment of an app that hosts unit tests.
    static let unitTestHostKey = "XCTestConfigurationFilePath"

    let mode: LaunchMode

    /// Fixture mode when the arguments contain "-ui-testing", or when the environment has
    /// "XCTestConfigurationFilePath" (the app is hosting unit tests; base test mode). Live otherwise.
    init(arguments: [String], environment: [String: String]) {
        if arguments.contains(LaunchConfiguration.uiTestingArgument) {
            mode = .fixture(TestMode(arguments: arguments))
        } else if environment[LaunchConfiguration.unitTestHostKey] != nil {
            mode = .fixture(TestMode())
        } else {
            mode = .live
        }
    }

    /// From this process.
    static var current: LaunchConfiguration {
        LaunchConfiguration(
            arguments: ProcessInfo.processInfo.arguments,
            environment: ProcessInfo.processInfo.environment
        )
    }
}
