import Foundation

enum AppVersion {
    /// The bundle's marketing version, or "Unknown" when it cannot be read.
    static var marketing: String {
        let value = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
        guard let value, !value.isEmpty else { return "Unknown" }
        return value
    }
}
