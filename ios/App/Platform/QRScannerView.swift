import SwiftUI
import UIKit

/// Camera preview with QR metadata output. The first decoded string ends the scan.
struct QRScannerView: UIViewControllerRepresentable {
    private let permissions: any PermissionProviding
    private let onResult: @MainActor (Result<String, QRScanFailure>) -> Void

    init(permissions: any PermissionProviding, onResult: @escaping @MainActor (Result<String, QRScanFailure>) -> Void) {
        self.permissions = permissions
        self.onResult = onResult
    }

    func makeUIViewController(context: Context) -> UIViewController {
        // STUB(WP-06)
        UIViewController()
    }

    func updateUIViewController(_ uiViewController: UIViewController, context: Context) {
        // STUB(WP-06)
    }
}
