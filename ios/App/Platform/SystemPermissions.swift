import AVFoundation
import Foundation

/// Real permission requests: `AVAudioApplication.requestRecordPermission()` and
/// `AVCaptureDevice.requestAccess(for: .video)`.
///
/// Each call shows the system prompt the first time and answers from the stored decision afterwards.
/// Inside LiveContainer the decision belongs to LiveContainer as a whole, which is why the denied texts
/// name it.
struct SystemPermissions: PermissionProviding {
    init() {}

    func requestMicrophone() async -> Bool {
        await AVAudioApplication.requestRecordPermission()
    }

    func requestCamera() async -> Bool {
        await AVCaptureDevice.requestAccess(for: .video)
    }
}
