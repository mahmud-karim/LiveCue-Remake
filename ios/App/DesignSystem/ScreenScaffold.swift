import SwiftUI

/// The ground behind everything, then one column 16 from the edges with 12 between children; a
/// `ScrollView` unless `scrolls` is false. System backgrounds are hidden.
///
/// It also gives its content the defaults every screen wants: text in `ink.primary`, and on iOS 18 the
/// material tab bar and the fade above a bottom bar (design system 3.3).
struct ScreenScaffold<Content: View>: View {
    private let scrolls: Bool
    private let content: Content

    init(scrolls: Bool = true, @ViewBuilder content: () -> Content) {
        self.scrolls = scrolls
        self.content = content()
    }

    var body: some View {
        #if DEBUG
        if DesignGallery.isActive {
            // A debug build that was asked for the component gallery shows it in place of every screen.
            DesignGalleryHost()
        } else {
            LCScaffoldLayout(scrolls: scrolls, content: content)
        }
        #else
        LCScaffoldLayout(scrolls: scrolls, content: content)
        #endif
    }
}

/// What `ScreenScaffold` draws.
struct LCScaffoldLayout<Content: View>: View {
    let scrolls: Bool
    let content: Content

    @Environment(\.theme) private var theme

    var body: some View {
        ZStack {
            GroundView()
            if scrolls {
                ScrollView {
                    column
                        .padding(.top, theme.space.s1)
                        .padding(.bottom, theme.space.s6)
                }
                .scrollDismissesKeyboard(.interactively)
                .modifier(LCFadeAboveBottomBar())
            } else {
                column
                    .padding(.top, theme.space.s1)
                    .modifier(LCFadeAboveBottomBar())
            }
        }
        // No system background shows anywhere: lists and text editors inside a screen are clear too.
        .scrollContentBackground(.hidden)
        .foregroundStyle(theme.ink.primary)
        .lcTabBarChrome()
    }

    private var column: some View {
        VStack(alignment: .leading, spacing: theme.space.panelGap) {
            content
        }
        .frame(maxWidth: .infinity, maxHeight: scrolls ? nil : .infinity, alignment: .topLeading)
        .padding(.horizontal, theme.space.screenInset)
    }
}

/// The fallback for the scroll edge effect under custom chrome (design system 3.3): content fades to
/// nothing over the last stretch above a bottom bar, and is not drawn under the bar. On iOS 26 the system
/// does this, and nothing is masked.
private struct LCFadeAboveBottomBar: ViewModifier {
    @Environment(\.theme) private var theme
    @Environment(\.lcFadesAboveBottomBar) private var fadesAboveBottomBar

    func body(content: Content) -> some View {
        if fadesAboveBottomBar {
            content.mask {
                VStack(spacing: 0) {
                    // Content may still pass under the bars at the top.
                    Rectangle()
                        .fill(theme.ink.primary)
                        .ignoresSafeArea(edges: .top)
                    LinearGradient(
                        colors: [theme.ink.primary, theme.ink.primary.opacity(0)],
                        startPoint: .top,
                        endPoint: .bottom
                    )
                    .frame(height: theme.component.scrollFade)
                }
            }
        } else {
            content
        }
    }
}
