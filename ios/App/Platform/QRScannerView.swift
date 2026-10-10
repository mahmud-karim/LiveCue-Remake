// The capture session is not marked for concurrency. It is configured on the main actor and only started
// and stopped on the scanner's serial queue, which is how the framework asks to be used.
@preconcurrency import AVFoundation
import SwiftUI
import UIKit

/// Camera preview with QR metadata output. The first decoded string ends the scan.
///
/// An `AVCaptureSession` with one `AVCaptureMetadataOutput` limited to `.qr`, delegate queue `.main`, an
/// aspect-fill preview layer on a black view; `startRunning` and `stopRunning` on a private serial queue.
/// The camera permission is asked for when the view loads. `onResult` is called at most once.
struct QRScannerView: UIViewControllerRepresentable {
    private let permissions: any PermissionProviding
    private let onResult: @MainActor (Result<String, QRScanFailure>) -> Void

    init(permissions: any PermissionProviding, onResult: @escaping @MainActor (Result<String, QRScanFailure>) -> Void) {
        self.permissions = permissions
        self.onResult = onResult
    }

    func makeUIViewController(context: Context) -> QRScannerController {
        QRScannerController(permissions: permissions, onResult: onResult)
    }

    func updateUIViewController(_ uiViewController: QRScannerController, context: Context) {
        // Nothing changes after the scanner was made.
    }
}

/// The view controller behind `QRScannerView`.
final class QRScannerController: UIViewController, AVCaptureMetadataOutputObjectsDelegate {
    private let permissions: any PermissionProviding
    private let onResult: @MainActor (Result<String, QRScanFailure>) -> Void
    private let captureSession = AVCaptureSession()
    /// `startRunning` and `stopRunning` block, so they stay off the main thread, one after the other.
    private let sessionQueue = DispatchQueue(label: "com.mahmudkarim.LiveCueRemake.qr-scanner")
    private var previewLayer: AVCaptureVideoPreviewLayer?
    /// True once a result was delivered, or once the view went away: nothing is delivered after that.
    private var isOver = false

    init(permissions: any PermissionProviding, onResult: @escaping @MainActor (Result<String, QRScanFailure>) -> Void) {
        self.permissions = permissions
        self.onResult = onResult
        super.init(nibName: nil, bundle: nil)
    }

    /// The scanner is never loaded from an archive.
    required init?(coder: NSCoder) {
        return nil
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
        Task { [weak self] in
            await self?.prepare()
        }
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        previewLayer?.frame = view.bounds
    }

    override func viewDidDisappear(_ animated: Bool) {
        super.viewDidDisappear(animated)
        isOver = true
        stopRunning()
    }

    // MARK: - Set-up

    private func prepare() async {
        let granted = await permissions.requestCamera()
        guard !isOver else { return }
        guard granted else {
            finish(.failure(.cameraDenied))
            return
        }
        if let failure = configure() {
            finish(.failure(failure))
            return
        }
        startRunning()
    }

    /// Builds the capture session and the preview. Returns why it could not, or nil.
    private func configure() -> QRScanFailure? {
        guard let camera = AVCaptureDevice.default(for: .video),
              let input = try? AVCaptureDeviceInput(device: camera),
              captureSession.canAddInput(input)
        else {
            return .cameraUnavailable
        }
        let output = AVCaptureMetadataOutput()
        guard captureSession.canAddOutput(output) else {
            return .scannerUnavailable
        }

        captureSession.beginConfiguration()
        captureSession.addInput(input)
        captureSession.addOutput(output)
        // The types an output offers are known only after it joined a session that has an input.
        let offersQR = output.availableMetadataObjectTypes.contains(.qr)
        if offersQR {
            output.metadataObjectTypes = [.qr]
            output.setMetadataObjectsDelegate(self, queue: .main)
        }
        captureSession.commitConfiguration()
        guard offersQR else {
            return .scannerUnavailable
        }

        let layer = AVCaptureVideoPreviewLayer(session: captureSession)
        layer.videoGravity = .resizeAspectFill
        layer.frame = view.bounds
        view.layer.addSublayer(layer)
        previewLayer = layer
        return nil
    }

    private func startRunning() {
        let session = captureSession
        sessionQueue.async {
            if !session.isRunning {
                session.startRunning()
            }
        }
    }

    private func stopRunning() {
        let session = captureSession
        sessionQueue.async {
            if session.isRunning {
                session.stopRunning()
            }
        }
    }

    private func finish(_ result: Result<String, QRScanFailure>) {
        guard !isOver else { return }
        isOver = true
        stopRunning()
        onResult(result)
    }

    // MARK: - AVCaptureMetadataOutputObjectsDelegate

    /// Called on the main queue, but not known to the compiler as the main actor: the text is taken out
    /// here and handed over.
    nonisolated func metadataOutput(
        _ output: AVCaptureMetadataOutput,
        didOutput metadataObjects: [AVMetadataObject],
        from connection: AVCaptureConnection
    ) {
        let texts = metadataObjects.compactMap { ($0 as? AVMetadataMachineReadableCodeObject)?.stringValue }
        guard let text = texts.first else { return }
        Task { @MainActor [weak self] in
            self?.finish(.success(text))
        }
    }
}
