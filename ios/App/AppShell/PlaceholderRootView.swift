import LiveCueCore
import SwiftUI

/// The only screen of the toolchain spike: the Daylight ground, the name, and one glass button.
///
/// It exists to prove that the app builds, links its packages, launches and renders on iOS 26 and iOS 18.
/// The accessibility identifiers are read by `PlaceholderUITests`. Under `-ui-testing` it also shows
/// whether the Keychain works in this build.
struct PlaceholderRootView: View {
    @Environment(\.theme) private var theme
    @State private var tapCount = 0
    /// The outcome of `KeychainProbe`, or `nil` outside UI tests.
    @State private var keychainState: String?

    var body: some View {
        ZStack {
            GroundView()

            VStack(spacing: 8) {
                Spacer()

                title
                    .font(.largeTitle.bold())
                    .accessibilityLabel("LiveCue")
                    .accessibilityAddTraits(.isHeader)
                    .accessibilityIdentifier("placeholder.title")

                Text(versionLine)
                    .font(.footnote)
                    .foregroundStyle(theme.ink.secondary)
                    .accessibilityIdentifier("placeholder.version")

                Spacer()

                LCGlassContainer {
                    LCGlassButton {
                        tapCount += 1
                    } label: {
                        Text("Tap the glass")
                            .font(.headline)
                            .frame(maxWidth: .infinity)
                    }
                    .accessibilityIdentifier("placeholder.glassButton")
                }
                .padding(.horizontal, 32)

                Text(LiquidGlass.isNative ? "Native Liquid Glass" : "Material fallback")
                    .font(.footnote)
                    .foregroundStyle(theme.ink.secondary)
                    .accessibilityIdentifier("placeholder.glassPath")
                    .padding(.top, 8)

                Text("Taps: \(tapCount)")
                    .font(.footnote.monospacedDigit())
                    .foregroundStyle(theme.ink.secondary)
                    .accessibilityIdentifier("placeholder.tapCount")

                if let keychainState {
                    Text("Keychain: \(keychainState)")
                        .font(.footnote)
                        .foregroundStyle(theme.ink.secondary)
                        .accessibilityIdentifier("placeholder.keychain")
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 24)
        }
        .task {
            if TestSeam.isUITesting {
                keychainState = KeychainProbe.run()
            }
        }
    }

    /// The name, two-tone at display size: "Live" in ink and "Cue" in the accent.
    private var title: Text {
        let live: Text = Text("Live").foregroundStyle(theme.ink.primary)
        let cue: Text = Text("Cue").foregroundStyle(theme.accent.fill)
        return Text("\(live)\(cue)")
    }

    /// The bundle's version and build, for example "Version 1.0.0 (1)".
    private var versionLine: String {
        let info = Bundle.main.infoDictionary
        let version = (info?["CFBundleShortVersionString"] as? String).flatMap { SemanticVersion($0) }
        let build = info?["CFBundleVersion"] as? String

        switch (version, build) {
        case let (version?, build?): return "Version \(version) (\(build))"
        case let (version?, nil): return "Version \(version)"
        default: return "Version unknown"
        }
    }
}
