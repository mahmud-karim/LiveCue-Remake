import Foundation

/// What a UI test asks of the app through launch arguments.
enum TestSeam {
    /// `true` when the app was launched with `-ui-testing`.
    static let isUITesting = ProcessInfo.processInfo.arguments.contains("-ui-testing")
}
