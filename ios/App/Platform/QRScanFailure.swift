import Foundation

/// Why the QR scanner could not deliver a code.
enum QRScanFailure: Error, Equatable, Sendable {
    case cameraDenied, cameraUnavailable, scannerUnavailable

    /// Spec 01 section 8.1, with the denied text of `04-ios-architecture.md` 1.3.
    var message: String {
        switch self {
        case .cameraDenied:
            return "Camera access is required. Enable it in Settings, or paste the pairing payload instead. If you use LiveContainer, allow it for LiveContainer in Settings."
        case .cameraUnavailable:
            return "Camera is unavailable. Paste the pairing payload instead."
        case .scannerUnavailable:
            return "QR scanning is unavailable."
        }
    }
}
