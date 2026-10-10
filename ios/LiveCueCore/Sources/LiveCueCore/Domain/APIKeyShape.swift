import Foundation

/// The shape checks every API key passes before it is saved or used.
public enum APIKeyShape: Equatable, Sendable {
    case valid, empty, containsWhitespace

    /// `empty` for "", `containsWhitespace` when any character is whitespace or a newline.
    public static func check(_ key: String) -> APIKeyShape {
        // STUB(WP-02)
        .valid
    }
}
