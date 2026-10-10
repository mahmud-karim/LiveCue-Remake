import Accessibility
import SwiftUI

/// A banner (design system 4.11): one accessibility element that reads the title, then the message.
///
/// A persistent message inside the content. Like a chip it brings its own base, so the title in the
/// tone's colour passes on any surface. A warning or danger banner is announced once, assertively, when
/// it appears.
struct Banner: View {
    private let tone: BannerTone
    private let title: String
    private let message: String

    @Environment(\.theme) private var theme

    init(tone: BannerTone, title: String, message: String) {
        self.tone = tone
        self.title = title
        self.message = message
    }

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: theme.space.s3) {
            LCGlyph(systemImage, size: .regular)
                .foregroundStyle(color)
            VStack(alignment: .leading, spacing: theme.space.half) {
                Text(verbatim: title)
                    .lcText(.label, color: color)
                Text(verbatim: message)
                    .lcText(.body)
            }
            .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.horizontal, theme.space.s4)
        .padding(.vertical, theme.space.s3)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background {
            LCTintedSurface(
                shape: RoundedRectangle(cornerRadius: theme.radius.field, style: .continuous),
                tint: theme.status.tint(for: statusTone)
            )
        }
        .accessibilityElement(children: .combine)
        .onAppear(perform: announce)
    }

    private var statusTone: StatusTone {
        switch tone {
        case .info: return .live
        case .success: return .success
        case .warning: return .warning
        case .danger: return .danger
        }
    }

    private var color: Color {
        theme.status.color(for: statusTone)
    }

    private var systemImage: String {
        switch tone {
        case .info: return "info.circle"
        case .success: return "checkmark"
        case .warning, .danger: return "exclamationmark.triangle.fill"
        }
    }

    private func announce() {
        guard tone == .warning || tone == .danger else {
            return
        }
        var announcement = AttributedString("\(title). \(message)")
        announcement.accessibilitySpeechAnnouncementPriority = .high
        AccessibilityNotification.Announcement(announcement).post()
    }
}
