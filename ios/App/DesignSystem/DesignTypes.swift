import SwiftUI

/// The type roles of the design system, section 2.10. Dynamic Type text styles only (IOS-B14).
enum TextRole: CaseIterable, Sendable {
    case display, title, headline, cue, transcript, body, label, caption, micro, mono

    /// The Dynamic Type text style and weight of the role. Sizes are never fixed, so every role follows
    /// the text size and Bold Text settings.
    var font: Font {
        switch self {
        case .display: return .largeTitle.bold()
        case .title: return .title2.bold()
        case .headline: return .headline
        case .cue: return .title3
        case .transcript: return .body
        case .body: return .body
        case .label: return .subheadline.weight(.semibold)
        case .caption: return .footnote
        case .micro: return .caption.weight(.semibold)
        case .mono: return .footnote.monospaced()
        }
    }
}

enum StatusTone: Equatable, Sendable {
    case live, success, warning, danger, neutral
}

enum ChipMark: Equatable, Sendable {
    case dot, pulsingDot, check, pause, alert, ring, spinner, none
}

enum BannerTone: Equatable, Sendable {
    case info, success, warning, danger
}

enum LCButtonKind: Equatable, Sendable {
    case primary, secondary, destructive, quiet
}

enum LCButtonSize: Equatable, Sendable {
    case large, regular
}

enum LevelMeterState: Equatable, Sendable {
    case listening, idle, paused
}

extension View {
    /// Font of the role plus a colour (default `ink.primary`); tabular digits when `tabular` is true.
    func lcText(_ role: TextRole, color: Color? = nil, tabular: Bool = false) -> some View {
        modifier(LCTextStyle(role: role, color: color, isTabular: tabular))
    }
}

private struct LCTextStyle: ViewModifier {
    let role: TextRole
    let color: Color?
    let isTabular: Bool

    @Environment(\.theme) private var theme

    func body(content: Content) -> some View {
        if role == .transcript {
            // Transcript text is body text with more room between its lines (design system 2.10).
            styled(content).lineSpacing(theme.component.transcriptLineSpacing)
        } else {
            styled(content)
        }
    }

    private func styled(_ content: Content) -> some View {
        content
            .font(isTabular ? role.font.monospacedDigit() : role.font)
            .foregroundStyle(color ?? theme.ink.primary)
    }
}
