import Foundation

/// The shape checks every API key passes before it is saved or used.
public enum APIKeyShape: Equatable, Sendable {
    case valid, empty, containsWhitespace

    /// `empty` for "", `containsWhitespace` when any character is whitespace or a newline.
    public static func check(_ key: String) -> APIKeyShape {
        if key.isEmpty {
            return .empty
        }
        // Scalars, not characters: a space that follows a prepending mark is part of that mark's character.
        if key.unicodeScalars.contains(where: { $0.properties.isWhitespace }) {
            return .containsWhitespace
        }
        return .valid
    }
}
