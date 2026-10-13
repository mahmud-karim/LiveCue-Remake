import SwiftUI

/// The sheet "Scan PC QR": the camera preview under the viewfinder overlay, with "Cancel".
///
/// The first code that is read turns the brackets green for a moment and is then handed to `onResult`. A
/// failure (camera denied, no camera, no scanner) is handed over at once. The owner of the sheet closes
/// it in both cases.
struct QRScanSheet: View {
    private let onResult: @MainActor (Result<String, QRScanFailure>) -> Void
    private let onCancel: @MainActor () -> Void

    @Environment(AppEnvironment.self) private var environment
    /// True from the moment a code was read until the sheet is gone.
    @State private var didRead = false

    /// How long the brackets stay green before the sheet closes (design system 4.15).
    private static let confirmationSeconds: TimeInterval = 0.4

    init(
        onResult: @escaping @MainActor (Result<String, QRScanFailure>) -> Void,
        onCancel: @escaping @MainActor () -> Void
    ) {
        self.onResult = onResult
        self.onCancel = onCancel
    }

    var body: some View {
        ZStack {
            QRScannerView(permissions: environment.dependencies.permissions, onResult: scanned)
                .ignoresSafeArea()
            QRScanOverlay(didRead: didRead, onCancel: onCancel)
        }
        .lcSheetChrome()
    }

    private func scanned(_ result: Result<String, QRScanFailure>) {
        guard case .success = result else {
            onResult(result)
            return
        }
        didRead = true
        let clock = environment.dependencies.clock
        let deliver = onResult
        Task {
            try? await clock.sleep(seconds: QRScanSheet.confirmationSeconds)
            deliver(result)
        }
    }
}

/// What lies over the camera preview: the dimmed surround, the viewfinder brackets, the title with
/// "Cancel", and the hint. Nothing here is chrome; the title bar and the hint use the inverse surface.
struct QRScanOverlay: View {
    private let didRead: Bool
    private let onCancel: @MainActor () -> Void

    @Environment(\.theme) private var theme

    init(didRead: Bool, onCancel: @escaping @MainActor () -> Void) {
        self.didRead = didRead
        self.onCancel = onCancel
    }

    var body: some View {
        ZStack {
            viewfinder
                .ignoresSafeArea()
            VStack(spacing: 0) {
                header
                Spacer(minLength: theme.space.s4)
                hint
            }
            .padding(.horizontal, theme.space.screenInset)
            .padding(.top, theme.space.s5)
            .padding(.bottom, theme.space.s6)
        }
    }

    /// The preview is dimmed everywhere but inside the square, and four brackets mark its corners.
    private var viewfinder: some View {
        ZStack {
            QRViewfinderSurround(side: QRViewfinderMetrics.side, radius: theme.radius.panel)
                .fill(theme.surface.scrim, style: FillStyle(eoFill: true))
            QRViewfinderBrackets(radius: theme.radius.panel, length: QRViewfinderMetrics.bracketLength)
                .stroke(
                    didRead ? theme.status.success : theme.ink.onInverse,
                    style: StrokeStyle(lineWidth: QRViewfinderMetrics.bracketWidth, lineCap: .round, lineJoin: .round)
                )
                .frame(width: QRViewfinderMetrics.side, height: QRViewfinderMetrics.side)
                .animation(theme.motion.fast, value: didRead)
        }
        .accessibilityHidden(true)
    }

    private var header: some View {
        ZStack {
            Text(verbatim: "Scan PC QR")
                .lcText(.headline, color: theme.ink.onInverse)
                .accessibilityAddTraits(.isHeader)
            HStack(spacing: 0) {
                Spacer(minLength: 0)
                Button(action: onCancel) {
                    Text(verbatim: "Cancel")
                        .lcText(.label, color: theme.ink.onInverse)
                        .padding(.horizontal, theme.space.s4)
                        .frame(minHeight: theme.size.minimumTarget)
                        .background {
                            Capsule().fill(theme.surface.inverse)
                        }
                }
                .buttonStyle(.plain)
            }
        }
    }

    private var hint: some View {
        Text(verbatim: "Point at the QR in LiveCue Desktop")
            .lcText(.label, color: theme.ink.onInverse)
            .multilineTextAlignment(.center)
            .padding(.horizontal, theme.space.s4)
            .padding(.vertical, theme.space.s3)
            .background {
                Capsule().fill(theme.surface.inverse)
            }
    }
}

/// The lengths of the viewfinder (design system 4.15), in points.
private enum QRViewfinderMetrics {
    static let side: CGFloat = 260
    static let bracketLength: CGFloat = 36
    static let bracketWidth: CGFloat = 4
}

/// Everything but a centred rounded square. Filled with the even-odd rule, it dims the preview around
/// the viewfinder and leaves the viewfinder clear.
private struct QRViewfinderSurround: Shape {
    let side: CGFloat
    let radius: CGFloat

    func path(in rect: CGRect) -> Path {
        var path = Path(rect)
        let square = CGRect(x: rect.midX - side / 2, y: rect.midY - side / 2, width: side, height: side)
        path.addRoundedRect(in: square, cornerSize: CGSize(width: radius, height: radius), style: .continuous)
        return path
    }
}

/// Four corner brackets of a rounded square: each runs `length` along both sides of its corner.
private struct QRViewfinderBrackets: Shape {
    let radius: CGFloat
    let length: CGFloat

    func path(in rect: CGRect) -> Path {
        let reach = max(length, radius)
        var path = Path()

        path.move(to: CGPoint(x: rect.minX, y: rect.minY + reach))
        path.addArc(
            tangent1End: CGPoint(x: rect.minX, y: rect.minY),
            tangent2End: CGPoint(x: rect.minX + reach, y: rect.minY),
            radius: radius
        )
        path.addLine(to: CGPoint(x: rect.minX + reach, y: rect.minY))

        path.move(to: CGPoint(x: rect.maxX - reach, y: rect.minY))
        path.addArc(
            tangent1End: CGPoint(x: rect.maxX, y: rect.minY),
            tangent2End: CGPoint(x: rect.maxX, y: rect.minY + reach),
            radius: radius
        )
        path.addLine(to: CGPoint(x: rect.maxX, y: rect.minY + reach))

        path.move(to: CGPoint(x: rect.maxX, y: rect.maxY - reach))
        path.addArc(
            tangent1End: CGPoint(x: rect.maxX, y: rect.maxY),
            tangent2End: CGPoint(x: rect.maxX - reach, y: rect.maxY),
            radius: radius
        )
        path.addLine(to: CGPoint(x: rect.maxX - reach, y: rect.maxY))

        path.move(to: CGPoint(x: rect.minX + reach, y: rect.maxY))
        path.addArc(
            tangent1End: CGPoint(x: rect.minX, y: rect.maxY),
            tangent2End: CGPoint(x: rect.minX, y: rect.maxY - reach),
            radius: radius
        )
        path.addLine(to: CGPoint(x: rect.minX, y: rect.maxY - reach))

        return path
    }
}
