import Foundation

/// Microphone and camera permission.
protocol PermissionProviding: Sendable {
    /// Prompts on first use; false when denied.
    func requestMicrophone() async -> Bool
    /// Prompts on first use; false when denied.
    func requestCamera() async -> Bool
}
