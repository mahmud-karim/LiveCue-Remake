import Foundation

/// Real permission requests: `AVAudioApplication.requestRecordPermission()` and
/// `AVCaptureDevice.requestAccess(for: .video)`.
struct SystemPermissions: PermissionProviding {
    init() {}

    func requestMicrophone() async -> Bool {
        // STUB(WP-06)
        false
    }

    func requestCamera() async -> Bool {
        // STUB(WP-06)
        false
    }
}
