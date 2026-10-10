# 00 - Platform research

Research date: 2026-10-10. Scope: facts the remake's builders need before writing code that can only be
compiled on a hosted macOS runner. Every fact below was checked against a primary source on the research date
unless it is listed under "Not verified" in its section. Signatures were read from Apple's documentation data,
package source at the pinned tags, the `actions/runner-images` repository, and Microsoft Learn.

Nothing in this file was compiled. Swift snippets were checked symbol by symbol against the documentation, but
the first CI run is still the real proof. Treat "Not verified" items as open risks.

## Recommendations

1. **Pin Xcode 26.3 on every macOS job** with `DEVELOPER_DIR=/Applications/Xcode_26.3.app/Contents/Developer`.
   It is the Xcode the old project shipped with, it is installed on both `macos-26` and `macos-15`, and it
   avoids the source-breaking changes in Xcode 27. Fail fast if the directory is missing.
2. **Run tests twice**: on `macos-26` against the iOS 26.2 simulator (native Liquid Glass path) and on
   `macos-15` against the iOS 18.6 simulator (fallback path). `macos-15` defaults to Xcode 16.4, which has no
   iOS 26 SDK, so the pin is mandatory there.
3. **Do not use Xcode 27 yet.** It is stable (27.0, 2026-09-14) but only on the preview `xcode-27` runner image,
   it replaces `@State` with a macro that rejects some previously valid code, and it changes builder semantics.
4. **Wrap every iOS 26 symbol in one small file** of availability-gated modifiers. Never let `Glass`,
   `GlassEffectContainer`, `ToolbarSpacer` or the glass button styles appear in a signature that must exist on
   iOS 18. Use the patterns in section 1 verbatim.
5. **Use Liquid Glass only for the control layer** (pinned controls, toolbars, floating status). Keep transcript
   and answer content on standard materials or plain surfaces, never glass on glass.
6. **Move the platform-independent core into a real Swift package in a subfolder** and reference it from
   `project.yml` as a local package. It links statically (no embedded framework to sign) and can be tested with
   `swift test` on macOS and Linux.
7. **Add a Linux CI job** (`ubuntu-latest`, container `swift:6.2-noble`) that builds and tests the core package
   before any macOS job starts. It catches most logic and concurrency errors in about a minute.
   The core must not call `URLSession.bytes(for:)` directly: that API does not exist in Foundation on Linux.
8. **Keep `FluidAudio` at exact 0.15.6** for the first release. The streaming manager the app uses is
   byte-identical in 0.17.7, so there is nothing to gain yet and a later bump is a one-line change.
9. **Decide deliberately whether to keep `desert-ant-core` (Voz) 3.1.0.** It is source-available, not open
   source, requires attribution, and ships a usage-reporting client that the licence forbids disabling. If it
   stays, keep the exact pin and disclose it in the README and in the app.
10. **Ship a Release-configuration, unsigned IPA** with a unique bundle identifier and a unique Keychain
    service string. Do not depend on entitlements, custom URL schemes, app groups, extensions or remote push.
    Keep a file-based fallback next to the Keychain for the pairing credential.
11. **Build the Windows app as self-contained WPF on .NET 10** (`net10.0-windows`, SDK 10.0.401). Trimming is not
    available for WPF. Get the glass look from a DWM system backdrop plus `WindowChrome`, never from
    `AllowsTransparency`.
12. **Host the relay inside the .NET app as a separate class library on Kestrel**, with a thin console host for
    headless runs and tests. Keep only the tunnel-gateway adapter as a Node module. Port the old relay tests as
    black-box contract tests first. This is a recommendation with trade-offs laid out in section 9; the
    architect decides.

---

## 1. Liquid Glass in SwiftUI (iOS 26)

### 1.1 Verified API surface

All symbols below are documented as iOS 26.0 unless stated, and none carries a deprecation flag as of the
research date. `glassEffect` and friends list iOS, iPadOS, Mac Catalyst, macOS, tvOS and watchOS (not visionOS).

| Symbol | Declaration (from Apple documentation) |
| --- | --- |
| Glass effect | `nonisolated func glassEffect(_ glass: Glass = .regular, in shape: some Shape = DefaultGlassEffectShape()) -> some View` |
| `Glass` | `struct Glass` : `Equatable`, `Sendable` |
| Variants | `static var regular: Glass`, `static var clear: Glass`, `static var identity: Glass` |
| Modifiers on `Glass` | `func tint(_ color: Color?) -> Glass`, `func interactive(_ isEnabled: Bool = true) -> Glass` |
| Default shape | `struct DefaultGlassEffectShape` (a capsule; `init()`) |
| Container | `@MainActor struct GlassEffectContainer<Content> where Content : View`, `init(spacing: CGFloat? = nil, content: () -> Content)` |
| Identity | `nonisolated func glassEffectID(_ id: (some Hashable & Sendable)?, in namespace: Namespace.ID) -> some View` |
| Union | `@MainActor func glassEffectUnion(id: (some Hashable & Sendable)?, namespace: Namespace.ID) -> some View` |
| Transition | `@MainActor func glassEffectTransition(_ transition: GlassEffectTransition) -> some View` with `.matchedGeometry`, `.materialize`, `.identity` |
| Button styles | `.buttonStyle(.glass)`, `.buttonStyle(.glassProminent)`, `.buttonStyle(.glass(_ glass: Glass))`; types `GlassButtonStyle`, `GlassProminentButtonStyle` (both `PrimitiveButtonStyle`) |
| Tab bar minimise | `nonisolated func tabBarMinimizeBehavior(_ behavior: TabBarMinimizeBehavior) -> some View`; values `.automatic`, `.never`, `.onScrollDown`, `.onScrollUp` |
| Tab accessory | `nonisolated func tabViewBottomAccessory<Content>(content: () -> Content) -> some View` (iOS, iPadOS, Catalyst only) |
| Tab accessory, toggled | `tabViewBottomAccessory(isEnabled: Bool, content:)` is **iOS 26.1**, not 26.0 |
| Accessory placement | `@Environment(\.tabViewBottomAccessoryPlacement) var placement: TabViewBottomAccessoryPlacement?` with cases `.inline`, `.expanded` |
| Toolbar spacer | `ToolbarSpacer(_ sizing: SpacerSizing = .flexible, placement: ToolbarItemPlacement = .automatic)`; `SpacerSizing.fixed`, `.flexible` |
| Shared background | `func sharedBackgroundVisibility(_ visibility: Visibility) -> some ToolbarContent` (a modifier on `ToolbarContent`, not on `View`) |
| System toolbar item | `DefaultToolbarItem(kind:placement:)`, for example `DefaultToolbarItem(kind: .search, placement: .bottomBar)` |
| Scroll edge style | `nonisolated func scrollEdgeEffectStyle(_ style: ScrollEdgeEffectStyle?, for edges: Edge.Set) -> some View`; `.automatic`, `.hard`, `.soft`. **`for:` has no default value.** |
| Scroll edge hidden | `nonisolated func scrollEdgeEffectHidden(_ hidden: Bool = true, for edges: Edge.Set = .all) -> some View` |
| Custom bars | `nonisolated func safeAreaBar(edge: VerticalEdge, alignment: HorizontalAlignment = .center, spacing: CGFloat? = nil, content: () -> some View) -> some View` (also a `HorizontalEdge` overload). Like `safeAreaInset`, but it also extends the scroll edge effect. |
| Background extension | `@MainActor func backgroundExtensionEffect() -> some View`, `backgroundExtensionEffect(isEnabled: Bool)` |
| Search behaviour | `func searchToolbarBehavior(_ behavior: SearchToolbarBehavior)`; the value is `.minimize` (one documentation sample misspells it `.minimized`) |
| Concentric shapes | `ConcentricRectangle`, `func containerShape(_ shape: some RoundedRectangularShape)` |
| Button sizing | `func buttonSizing(_ sizing: ButtonSizing)` with `.flexible`, `.fitted` |
| UIKit | `UIGlassEffect(style:)` with `isInteractive`, `tintColor`; `UIGlassContainerEffect` with `spacing` (both `UIVisualEffect` subclasses) |
| Opt-out key | Info.plist `UIDesignRequiresCompatibility` (Boolean). Apple marks it temporary, and it is **ignored when building with the iOS 27 SDK**. |

`Tab(role: .search)` and `TabRole.search` are iOS 18 API and need no gating.

### 1.2 Behaviour that comes for free

- Standard bars, sheets, popovers, menus, alerts and controls adopt the material when the app is built with the
  iOS 26 SDK and runs on iOS 26. No code is needed.
- Sheets: partial-height sheets are inset with a glass background; at full height the background becomes opaque
  and anchors to the screen edge. Apple's guidance is to **remove custom `presentationBackground`** so the
  system material shows. A sheet can morph out of the button that presents it by pairing
  `matchedTransitionSource(id:in:)` on the button with `navigationTransition(.zoom(sourceID:in:))` on the
  sheet content (both iOS 18 API).
- Toolbar items are grouped on shared glass automatically. Split groups with `ToolbarSpacer(.fixed)`; take an
  item out of the shared background with `.sharedBackgroundVisibility(.hidden)`.
- Scroll views get an automatic edge effect under bars. Remove any custom darkening or blur behind bars,
  because it interferes with that effect.
- Section headers in lists and forms render in title case rather than all capitals.

### 1.3 Changes since the first iOS 26 release

- No Liquid Glass symbol has been renamed or deprecated since iOS 26.0.
- iOS 26.1 added `tabViewBottomAccessory(isEnabled:content:)`.
- The documentation now reflects the Xcode 27 SDK and shows `@ContentBuilder` where Xcode 26 shows
  `@ViewBuilder`. `ContentBuilder` is declared as `typealias ContentBuilder = ViewBuilder`, but Apple states
  that the new type-agnostic behaviour applies "when you build in Xcode 27 and later". **Write `@ViewBuilder` in
  project code**; `@ContentBuilder` should be assumed unavailable in Xcode 26.3.
- New in the iOS 27 SDK only, so unavailable to this project while it builds with Xcode 26.3:
  `toolbarMinimizationBehavior(_:for:)`, `TabRole.prominent`, the `crossFade` sheet transition, the `@State`
  macro, `GeometryProxy.concentricCornerRadii`.
- Names that appear in older blog posts and do not exist: `glassEffect(_:in:isEnabled:)` (use
  `.identity` to switch the effect off), and `glassBackgroundEffect` (that one is visionOS).

### 1.4 Where glass belongs (Apple guidance)

- Glass is "a distinct functional layer for controls and navigation elements" that floats above content.
  "Don't use Liquid Glass in the content layer." Use standard materials (`.ultraThinMaterial`, `.thinMaterial`,
  `.regularMaterial`, `.thickMaterial`) or opaque surfaces for content.
- "Use Liquid Glass effects sparingly" on custom controls. Limit them to the most important functional elements.
- Avoid glass on glass. Put fills, vibrancy or plain labels on top of a glass surface, not another glass layer.
- Regular and clear variants should not be mixed. Clear is only for controls over media-rich backgrounds, and
  needs a dimming layer (Apple suggests a dark layer of about 35% opacity over bright content).
- Tint only to convey meaning (a primary action), not for decoration. Colour belongs in the content layer.
- Always combine nearby custom glass shapes in one `GlassEffectContainer`. Glass cannot sample other glass, so
  shapes in different containers render inconsistently, and many loose effects cost performance.
- Apply `glassEffect` after other appearance modifiers, because the modifier captures the content it renders.
- `glassEffectID` and `glassEffectTransition` only matter during view-hierarchy transitions and animations.

### 1.5 Accessibility

From Apple's WWDC25 session on the material: Reduce Transparency "makes Liquid Glass frostier and obscures more
of the content behind it"; Increase Contrast "makes elements predominantly black or white and highlights them
with a contrasting border"; Reduce Motion "decreases the intensity of some effects and disables any elastic
properties". These apply automatically to system glass. People can also choose a preferred look for the
material in device settings.

Consequences for this app:

- Native `glassEffect` needs no extra code for these settings.
- The iOS 18 fallback built on SwiftUI materials also adapts automatically, because materials become opaque
  under Reduce Transparency.
- Any hand-drawn translucent surface (a white fill at partial opacity, a custom gradient sheen) must read
  `@Environment(\.accessibilityReduceTransparency)` and switch to an opaque fill. Apple's documentation for
  that value: backgrounds "should not be semi-transparent; they should be opaque".

### 1.6 Supporting iOS 18 with a fallback

Rules that keep this compiling with deployment target iOS 18.0:

1. The project must be built with an iOS 26 SDK (Xcode 26.x). With Xcode 16 the symbols do not exist and
   `#available` does not help.
2. Types introduced in iOS 26 may only be named inside an `if #available(iOS 26.0, *)` block or inside a
   declaration marked `@available(iOS 26.0, *)`.
3. Two branches of a conditional must not be merged with `?:` when their types differ. Button styles are the
   usual trap: `.glass` and `.bordered` are different types.
4. `View.background(_:in:fillStyle:)` requires `InsettableShape`, while `glassEffect(_:in:)` accepts any
   `Shape`. A wrapper generic over `Shape` must build its fallback with `shape.fill(...)`.

```swift
import SwiftUI

/// Describes a glass surface without naming any iOS 26 type.
struct LCGlass: Equatable, Sendable {
    enum Variant: Sendable { case regular, clear }

    var variant: Variant = .regular
    var tint: Color? = nil
    var isInteractive = false

    static let regular = LCGlass()
    static let clear = LCGlass(variant: .clear)

    func tinted(_ color: Color) -> LCGlass { var copy = self; copy.tint = color; return copy }
    func interactive(_ enabled: Bool = true) -> LCGlass { var copy = self; copy.isInteractive = enabled; return copy }

    @available(iOS 26.0, *)
    var system: Glass {
        var glass: Glass = (variant == .clear) ? .clear : .regular
        if let tint { glass = glass.tint(tint) }
        if isInteractive { glass = glass.interactive() }
        return glass
    }
}

struct LCGlassModifier<S: Shape>: ViewModifier {
    let glass: LCGlass
    let shape: S

    func body(content: Content) -> some View {
        if #available(iOS 26.0, *) {
            content.glassEffect(glass.system, in: shape)
        } else {
            // Materials turn opaque by themselves when Reduce Transparency is on.
            content.background { shape.fill(.ultraThinMaterial) }
        }
    }
}

extension View {
    func lcGlass(_ glass: LCGlass = .regular, in shape: some Shape = Capsule()) -> some View {
        modifier(LCGlassModifier(glass: glass, shape: shape))
    }

    @ViewBuilder
    func lcGlassID(_ id: some Hashable & Sendable, in namespace: Namespace.ID) -> some View {
        if #available(iOS 26.0, *) { glassEffectID(id, in: namespace) } else { self }
    }

    @ViewBuilder
    func lcScrollEdge(hard: Bool, for edges: Edge.Set) -> some View {
        if #available(iOS 26.0, *) {
            scrollEdgeEffectStyle(hard ? ScrollEdgeEffectStyle.hard : ScrollEdgeEffectStyle.soft, for: edges)
        } else {
            self
        }
    }

    @ViewBuilder
    func lcTabBarMinimizesOnScroll() -> some View {
        if #available(iOS 26.0, *) { tabBarMinimizeBehavior(.onScrollDown) } else { self }
    }
}

/// Groups glass shapes on iOS 26 and is a transparent wrapper on iOS 18.
struct LCGlassContainer<Content: View>: View {
    private let spacing: CGFloat?
    private let content: Content

    init(spacing: CGFloat? = nil, @ViewBuilder content: () -> Content) {
        self.spacing = spacing
        self.content = content()
    }

    var body: some View {
        if #available(iOS 26.0, *) {
            GlassEffectContainer(spacing: spacing) { content }
        } else {
            content
        }
    }
}

/// Usage: `Button("Assist") { ... }.modifier(LCGlassButton(prominent: true))`
struct LCGlassButton: ViewModifier {
    var prominent = false

    func body(content: Content) -> some View {
        if #available(iOS 26.0, *) {
            if prominent { content.buttonStyle(.glassProminent) } else { content.buttonStyle(.glass) }
        } else {
            if prominent { content.buttonStyle(.borderedProminent) } else { content.buttonStyle(.bordered) }
        }
    }
}

/// A pinned bottom control bar that joins the scroll edge effect on iOS 26.
struct LCBottomBar<Bar: View>: ViewModifier {
    let bar: Bar

    func body(content: Content) -> some View {
        if #available(iOS 26.0, *) {
            content.safeAreaBar(edge: .bottom) { bar }
        } else {
            content.safeAreaInset(edge: .bottom) { bar }
        }
    }
}
```

Toolbar content that needs `ToolbarSpacer` is safest written as two complete `.toolbar` blocks chosen by one
`if #available` in a `ViewModifier`, one with the spacers and one without. An inline
`if #available(iOS 26.0, *) { ToolbarSpacer(.fixed) }` inside a single `.toolbar { }` builder should also work
(`ToolbarContentBuilder` has `buildIf` and `buildLimitedAvailability`), but that form was not compiled here.

### 1.7 Running inside LiveContainer

Whether a guest app gets the iOS 26 design depends on which SDK version the system libraries see for the
process. See section 5.4: LiveContainer has a per-app "Spoof SDK Version" setting for exactly this.

### Sources

- https://developer.apple.com/documentation/swiftui/view/glasseffect(_:in:)
- https://developer.apple.com/documentation/swiftui/glass
- https://developer.apple.com/documentation/swiftui/glasseffectcontainer
- https://developer.apple.com/documentation/swiftui/applying-liquid-glass-to-custom-views
- https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass
- https://developer.apple.com/design/human-interface-guidelines/materials
- https://developer.apple.com/documentation/swiftui/view/tabviewbottomaccessory(isenabled:content:)
- https://developer.apple.com/documentation/swiftui/view/scrolledgeeffectstyle(_:for:)
- https://developer.apple.com/documentation/swiftui/view/safeareabar(edge:alignment:spacing:content:)
- https://developer.apple.com/documentation/swiftui/toolbarspacer
- https://developer.apple.com/documentation/swiftui/contentbuilder and .../swiftui/viewbuilder
- https://developer.apple.com/documentation/updates/swiftui
- https://developer.apple.com/documentation/ios-ipados-release-notes/ios-ipados-27-release-notes
- https://developer.apple.com/documentation/bundleresources/information-property-list/uidesignrequirescompatibility
- https://developer.apple.com/videos/play/wwdc2025/219/ and https://developer.apple.com/videos/play/wwdc2025/323/

**Confidence:** high for signatures and availability (read from documentation data); medium for the snippets
(not compiled).

**Not verified:** the snippets were not compiled; the inline `#available` form inside a toolbar builder; that
Xcode 26.3 rejects `@ContentBuilder` (inferred from Apple's wording); the exact iOS version that added the
user-facing appearance setting for the material.

---

## 2. GitHub Actions for a public repository

### 2.1 Runner labels (from `actions/runner-images`)

| Image | Architecture | Labels | Status |
| --- | --- | --- | --- |
| macOS 26 | arm64 | `macos-latest`, `macos-26`, `macos-26-xlarge` | GA |
| macOS 26 | x64 | `macos-26-intel`, `macos-26-large`, `macos-latest-large` | GA |
| macOS 15 | arm64 | `macos-15`, `macos-15-xlarge` | GA |
| macOS 15 | x64 | `macos-15-intel`, `macos-15-large` | GA |
| macOS 14 | both | `macos-14`, `macos-14-large`, `macos-14-xlarge` | Deprecated; unsupported from 2026-11-02 |
| Xcode 27 (macOS 27) | arm64 | `xcode-27`, `xcode-27-xlarge` | Public preview |
| Windows Server 2025 (VS 2026) | x64 | `windows-latest`, `windows-2025`, `windows-2025-vs2026` | GA |
| Windows Server 2022 | x64 | `windows-2022` | GA |
| Windows 11 | arm64 | `windows-11-arm`, `windows-11-vs2026-arm` | GA |
| Ubuntu 24.04 | x64 | `ubuntu-latest`, `ubuntu-24.04` | GA (`ubuntu-latest` moves to 26.04 in November 2026) |
| Ubuntu 26.04 | x64 | `ubuntu-26.04` | GA |

`-large` and `-xlarge` are larger runners and are always billed. Use only the standard labels.

### 2.2 Xcode on each image

All paths are `/Applications/Xcode_<version>.app`. Select one with
`DEVELOPER_DIR=/Applications/Xcode_26.3.app/Contents/Developer`.

| Image (version read) | Installed Xcode | Default | iOS simulator runtimes |
| --- | --- | --- | --- |
| `macos-26` arm64 (macOS 26.6.2, image 20260907) | 26.0.1, 26.1.1, 26.2, **26.3**, 26.4.1, 26.5, 26.6 | 26.6 (17F113) | 26.2, 26.4 (26.4.1), 26.5 |
| `macos-15` arm64 (macOS 15.7.9, image 20260907) | 16.0, 16.1, 16.2, 16.3, 16.4, 26.0.1, 26.1.1, 26.2, **26.3** | **16.4** (16F6) | 18.5, 18.6, 26.0 (26.0.1), 26.1, 26.2 |
| `xcode-27` preview (macOS 27.0.1, image 20261006) | 27.0, 27.1 (beta build), 27.2 beta | 27.0 (27A266a) | 27.0 confirmed |

SDK and compiler per Xcode (Apple's support table):

| Xcode | iOS SDK | Swift | Deployment targets | Needs macOS |
| --- | --- | --- | --- | --- |
| 26.2, 26.3 | 26.2 | 6.2.3 | iOS 15 to 26.2 | 15.6 or later |
| 26.4.1 | 26.4 | 6.3 | iOS 15 to 26.4 | 26.2 or later |
| 26.5, 26.6 | 26.5 | 6.3 | iOS 15 to 26.5 | 26.2 or later |
| 27 | 27 | 6.4 | iOS 15 to 27 | 26.6 or later |

Notes for workflows:

- `macos-26` has **no iOS 18 runtime** and no 26.0 or 26.1 runtime. The iOS 18 fallback can only be exercised
  on `macos-15`.
- The old project's simulator picker chose the newest iPhone runtime that is not newer than the SDK of the
  selected Xcode (26.2 for Xcode 26.3). Keep that rule; a newer runtime on the same image belongs to a
  different Xcode.
- Intel labels cannot link the simulator build of FluidAudio 0.15.6 (see section 3.4).
- Homebrew is preinstalled on the macOS images (6.0.22 on `macos-26`); XcodeGen is not, so install it with
  `brew install xcodegen`.

### 2.3 Current stable Xcode

- Stable: **Xcode 27.0** (27A266a), released 2026-09-14, requires macOS 26.6 or later, Apple silicon only.
- Xcode 27.1 is at release-candidate stage (2026-10-05); 27.2 is in beta.
- Newest release of the 26 line: Xcode 26.6 (17F113), 2026-06-25.
- Stable OS releases: iOS 27.0.1 and macOS 27.0.1 (2026-09-28).

### 2.4 Cost and limits

- "Use of the standard GitHub-hosted runners is free and unlimited on public repositories." Larger runners are
  always charged.
- Concurrency on the Free plan: 20 jobs in total, of which at most **5 macOS** jobs. Pro and Team also allow 5
  macOS jobs.
- Job time limit 6 hours; matrix limit 256 jobs per run.
- Dependency cache: 10 GB included per repository.
- Standard public-repository runner sizes: Linux and Windows 4 vCPU and 16 GB; macOS arm64 3 vCPU (M1) and 7 GB.

### 2.5 Windows and Linux images

- `windows-latest` (Windows Server 2025 with Visual Studio 2026, image 20260925): .NET SDKs 8.0.131, 8.0.206,
  8.0.319, 8.0.425, 9.0.121, 9.0.205, 9.0.318, 10.0.112, 10.0.204, 10.0.303, **10.0.401**. Shared frameworks
  include `Microsoft.WindowsDesktop.App` and `Microsoft.AspNetCore.App` 10.0.12. So the **.NET 10 SDK is
  preinstalled**, in the same version as the portable SDK used locally.
- Node on `windows-latest`: default **22.23.3** with npm 10.9.9; cached 22.23.3 and **24.21.0**. Use
  `actions/setup-node` with `node-version: 24` for the relay or the tunnel adapter tests.
- `ubuntu-latest` (24.04, image 20261004): **Swift 6.4** preinstalled, Node 22.23.3, .NET SDK up to 10.0.401.
- `macos-26`: Node 24.20.0 default (22.23.2 also cached); .NET SDK up to 10.0.400. `macos-15`: Node 22.23.2
  default.
- Latest major versions of common actions on the research date: `actions/checkout` v7, `actions/upload-artifact`
  v7, `actions/download-artifact` v8, `actions/setup-node` v7, `actions/setup-dotnet` v6, `actions/cache` v6.

```yaml
env:
  DEVELOPER_DIR: /Applications/Xcode_26.3.app/Contents/Developer

jobs:
  core-linux:
    runs-on: ubuntu-latest
    container: swift:6.2-noble          # same compiler line as Xcode 26.3
    steps:
      - uses: actions/checkout@v7
      - run: swift test --package-path Packages/LiveCueCore

  ios-tests:
    needs: core-linux
    strategy:
      fail-fast: false
      matrix:
        include:
          - { runner: macos-26, runtime: "26.2" }   # native glass
          - { runner: macos-15, runtime: "18.6" }   # fallback
    runs-on: ${{ matrix.runner }}
    timeout-minutes: 40
    steps:
      - uses: actions/checkout@v7
      - name: Require the pinned Xcode
        run: test -d "$DEVELOPER_DIR" || { ls /Applications | grep -i xcode; exit 1; }
      - run: brew install xcodegen && xcodegen generate
      # choose an iPhone simulator on iOS ${{ matrix.runtime }}, boot it, then xcodebuild test
```

### Sources

- https://github.com/actions/runner-images (README, `images/macos/macos-26-arm64-Readme.md`,
  `images/macos/macos-15-arm64-Readme.md`, `images/macos/xcode-27-arm64-Readme.md`,
  `images/windows/Windows2025-VS2026-Readme.md`, `images/ubuntu/Ubuntu2404-Readme.md`)
- https://github.com/actions/runner-images/issues/14404 (Xcode 27 preview image)
- https://docs.github.com/en/actions/reference/runners/github-hosted-runners
- https://docs.github.com/en/actions/reference/limits
- https://docs.github.com/en/billing/concepts/product-billing/github-actions
- https://developer.apple.com/news/releases/ and https://developer.apple.com/support/xcode/

**Confidence:** high (image readmes were read directly).

**Not verified:** which plan the repository owner is on (limits above assume Free); whether GitHub will keep
Xcode 26.3 on these images once Xcode 27 reaches them; whether the `v4` action versions used by the old
workflows still run without warnings; additional simulator runtimes on the `xcode-27` image.

---

## 3. FluidAudio

### 3.1 What it is

Apache-2.0 Swift SDK for on-device audio models on Apple platforms, running Core ML models with Neural Engine
support. It covers batch speech recognition (Parakeet TDT v2 and v3 and newer variants), streaming speech
recognition (Parakeet EOU with end-of-utterance detection, Nemotron streaming), voice activity detection
(Silero), speaker diarisation (several pipelines), and text to speech. Models download from Hugging Face on
first use.

Package facts at 0.15.6: `swift-tools-version: 6.0`, platforms macOS 14 and iOS 17, library product
`FluidAudio`, no Swift package dependencies, one binary target (`NemoTextProcessing.xcframework`, a prebuilt
static library of about 49 MB compressed, downloaded during package resolution).

### 3.2 Versions

| Version | Date | Relevant change |
| --- | --- | --- |
| 0.15.6 | 2026-08-19 | The old app's pin |
| 0.15.7 | 2026-09-10 | Fixes elsewhere (Nemotron, TTS, text normalisation linking) |
| 0.15.8 | 2026-09-20 | Batch and sliding-window fixes; a download fix for root-level files |
| 0.16.1 | 2026-09-21 | Binary target bumped to a build with Intel-simulator and Catalyst slices |
| 0.17.0 to 0.17.5 | 2026-09-23 to 10-01 | Nemotron 3 diarisation, new batch models, logger controls |
| **0.17.7** | 2026-10-08 | Latest; adds a small TTS model. Adds `Package@swift-6.2.swift` with a default-on `NemoTextProcessing` package trait |

Comparison of 0.15.6 and 0.17.7 by file hash: `StreamingEouAsrManager.swift`, `RnntDecoder.swift`,
`Tokenizer.swift`, `EncoderCacheManager.swift`, `StreamingAsrUtils.swift` and `DownloadTypes.swift` are
**identical**. The download stack (`ModelHub`, `FileDownloader`, `ModelCache`, `HFTreeLister`),
`AudioMelSpectrogram.swift` and `ModelNames.swift` changed. 74 of 281 library source files changed overall.

### 3.3 API the remake needs (read from the 0.15.6 tag)

```swift
import AVFoundation
import CoreML
import FluidAudio

public enum StreamingChunkSize: Sendable { case ms160, ms320, ms1280 }
public typealias PartialCallback = @Sendable (String) -> Void
public typealias EouCallback = @Sendable (String) -> Void
public typealias ProgressHandler = @Sendable (DownloadProgress) -> Void

public struct DownloadProgress: Sendable {
    public let fractionCompleted: Double
    public let phase: DownloadPhase
}
public enum DownloadPhase: Sendable {
    case listing
    case downloading(completedFiles: Int, totalFiles: Int)
    case compiling(modelName: String)
}

public actor StreamingEouAsrManager {
    public init(configuration: MLModelConfiguration = MLModelConfiguration(),
                chunkSize: StreamingChunkSize = .ms160,
                eouDebounceMs: Int = 1280,
                debugFeatures: Bool = false)

    /// Downloads from Hugging Face if needed, then loads.
    public func loadModels(to directory: URL? = nil,
                           configuration: MLModelConfiguration? = nil,
                           progressHandler: ProgressHandler? = nil) async throws
    public func loadModels(from directory: URL) async throws

    public func setPartialCallback(_ callback: @escaping PartialCallback)
    public func setEouCallback(_ callback: @escaping EouCallback)
    public func process(audioBuffer: AVAudioPCMBuffer) async throws -> String   // returns ""; text arrives via callbacks
    public func finish() async throws -> String                                  // flushes and returns the transcript
    public func reset() async
    public func cleanup() async
}
```

How the old app drives it, which the remake should reproduce:

1. Create `MLModelConfiguration`, set `computeUnits = .cpuAndNeuralEngine`.
2. `StreamingEouAsrManager(configuration: config, chunkSize: .ms320, eouDebounceMs: 640)`.
3. `try await manager.loadModels(to: nil, configuration: nil, progressHandler: { progress in ... })`. Map
   `progress.fractionCompleted` to a clamped fraction and switch over `progress.phase` (`.listing`,
   `.downloading(let completed, let total)`, `.compiling`). The handler is `@Sendable` and runs off the main
   actor, so hop with `Task { @MainActor in ... }`.
4. Before each session: `await manager.reset()` then `await manager.setPartialCallback { text in ... }`. All
   methods are actor-isolated, so every call needs `await`.
5. Capture with `AVAudioEngine`, convert to 16 kHz mono Float32 with `AVAudioConverter`, hand each chunk to a
   bounded `AsyncStream`, and in the consumer wrap it in an `AVAudioPCMBuffer` and call
   `try await manager.process(audioBuffer:)`.
6. On pause: stop the engine, finish the stream, then `let text = try await manager.finish()` and emit it as the
   final segment.
7. To free memory or switch model: `await manager.cleanup()`.

Model location: the default cache is `Application Support/FluidAudio/Models/parakeet-eou-streaming/` in the
app's container; the 320 ms model comes from the Hugging Face repository
`FluidInference/parakeet-realtime-eou-120m-coreml`, subfolder `320ms`. The EOU model is English only. In
`StreamingChunkSize`, `.ms320.durationMs` returns 630 rather than 320, so do not use that property for UI text.

The batch API also exists at 0.15.6 (`AsrModels.downloadAndLoad(version: .v3)`, `AsrManager(config:)`,
`loadModels(_:)`, `transcribe(_ samples: [Float], source:)` returning `ASRResult` with `text`, `duration`,
`tokenTimings`). The library's own documentation shows both `configure(models:)` and `loadModels(_:)` for the
same step; only `loadModels(_:)` exists in the 0.15.6 source. Read signatures from the tag, not from the README.

### 3.4 The "simulator artefact needs Apple Silicon" note

Confirmed. FluidAudio 0.15.6 links `NemoTextProcessing.xcframework` from `text-processing-rs` 0.3.0. The 0.3.1
release notes say the earlier archive had no Intel-simulator or Catalyst slice and that such consumers "failed
to link because no matching slice existed"; FluidAudio 0.16.1 states that 0.15.6 to 0.15.8 are affected.
Standard `macos-26` and `macos-15` runners are arm64, so they are fine. `macos-15-intel` and `macos-26-intel`
would fail at link time for the simulator.

### 3.5 Recommendation: keep exact 0.15.6

- The calls the app makes are unchanged in 0.17.7, so upgrading buys nothing for streaming captions today.
- 0.15.6 is known to resolve, build and link with Xcode 26.3 in the old project's CI.
- 0.17.x changes the download stack and mel spectrogram code, which would be new, device-only risk.
- 0.17.x adds a tools-6.2 manifest with package traits. XcodeGen 2.46.0 can express traits, but that is one more
  untested moving part.
- Revisit after the first working release in a dedicated CI run. Reasons to move then: download-recovery
  fixes, logger controls that keep recognised words out of the console (0.17.4), and an Intel-capable binary.

### Sources

- https://github.com/FluidInference/FluidAudio (tags `v0.15.6` and `v0.17.7`: `Package.swift`,
  `Package@swift-6.2.swift`, `Sources/FluidAudio/ASR/Parakeet/Streaming/EOU/StreamingEouAsrManager.swift`,
  `Sources/FluidAudio/Shared/Download/DownloadTypes.swift`, `Sources/FluidAudio/ModelNames.swift`,
  `Documentation/ASR/GettingStarted.md`)
- https://github.com/FluidInference/FluidAudio/releases
- https://github.com/FluidInference/text-processing-rs/releases/tag/v0.3.1

**Confidence:** high for API and versions; medium for the upgrade assessment.

**Not verified:** runtime behaviour on a device; model download size (the old app's catalogue says about
230 MB); the slice list inside the 0.3.0 archive itself (taken from the maintainers' release notes).

---

## 4. desert-ant-core and Voz 3.1.0

### 4.1 What it is

`desert-ant-core` is a multi-product Swift package (also Kotlin and JavaScript) of small on-device models. The
`Voz` product is on-device speech recognition for Apple platforms: Core ML only, 25 European languages, word
timestamps at 80 ms resolution. Its catalogue comment says the current export is a Parakeet TDT 0.6B v3
transducer. The model (about 467 MB) downloads from Hugging Face on first use and is cached.

- Repository: public, not archived, last pushed 2026-10-09. Tags run to **v3.6.0** (2026-10-06); 3.1.0 is still
  available.
- Requirements: iOS 18, macOS 15, Swift 6.2 (Xcode 26). `swift-tools-version: 6.2`.
- Package traits `MLX` and `Xet` are opt-in and off by default; with them off, no macro or networking-stack
  dependencies are pulled in. The package still depends on `swift-numerics`.
- Between 3.1.0 and 3.6.0 the public `Voz` API gained one initialiser and the pinned model revision moved from
  `v0.1.0` to `v0.3.0`, which means a different model download.

### 4.2 What the old app uses it for

The "Voz on Assist" mode: audio is buffered in memory while recording, and when the user taps Assist the whole
pending buffer is transcribed in one batch. Calls, as used:

```swift
import Voz

// Load, downloading first if needed. The progress closure is @Sendable.
let voz = try await Voz(progress: { progress in
    // progress.fraction (0...1), progress.completedBytes, progress.totalBytes  (all from DownloadProgress)
})

// 16 kHz mono Float samples. `Voz` is an actor, so the call needs `await`.
let result = try await voz.transcribe(samples: samples)
// result.text, result.words (each with .text, .start, .end), result.duration, result.processingTime
```

Signatures at 3.1.0:

```swift
public actor Voz {
    public init(directory: String? = nil, cacheRoot: String? = nil,
                progress: @Sendable @escaping (DownloadProgress) -> Void = { _ in }) async throws
    public init(modelDirectory: URL, computeUnits: MLComputeUnits = .cpuAndNeuralEngine) throws
    public static func isDownloaded(directory: String? = nil, cacheRoot: String? = nil) -> Bool
    public static func download(directory: String? = nil, cacheRoot: String? = nil,
                                progress: @Sendable @escaping (DownloadProgress) -> Void = { _ in }) async throws -> String
    public func transcribe(samples: [Float], progress: @Sendable (Progress) -> Void = { _ in }) throws -> Result
    public nonisolated let sampleRate: Double
    public static let supportedLanguages: Set<String>
}
public struct DownloadProgress: Sendable { let completedBytes: Int64; let totalBytes: Int64; var fraction: Double }
public struct Word: Sendable, Equatable, Codable { let text: String; let start: TimeInterval; let end: TimeInterval }
```

The old app keeps the last two seconds of audio as overlap between batches and filters `result.words` by
`word.end` to avoid repeating text. Voz does not detect language and produces confident nonsense for
unsupported languages. The documentation says the first load after a download spends roughly 20 seconds on
Neural Engine specialisation.

### 4.3 Licence and telemetry

- Licence: **Desert Ant Labs Source-Available License 1.0** (SPDX `LicenseRef-DAL-Source-Available-1.0`). The
  licence text calls itself source-available, not open source.
- Free below 100,000 monthly active devices per platform per model; a commercial licence is required above.
- Attribution is required: credit the vendor where users can reasonably find it.
- The models, outputs and logs may not be used to train a competing model.
- The package contains a usage-reporting client (`Sources/Usage`): a random device identifier stored in
  `UserDefaults`, the app's bundle identifier, the SDK identity and a call count, posted to the vendor's ingest
  endpoint at most once per day per install. The licence says: "Do not tamper with the telemetry ... or
  interfere with its reporting."
- A public MIT-licensed repository may depend on the package through SwiftPM, but it must not vendor or
  redistribute the SDK as its own, and the app's privacy statement cannot say that nothing but the chosen
  providers is contacted while Voz is included.

### 4.4 Options for the architect

- Keep `exactVersion: 3.1.0` (lowest technical risk, proven in the old CI), add attribution, and document the
  usage ping.
- Or drop the dependency and implement the batch mode with FluidAudio's batch Parakeet TDT v3 API, which is the
  same model family under Apache-2.0 with no usage reporting. This removes a licence and privacy caveat but is
  new, untested code on a path that only a device can validate.

### Sources

- https://github.com/Desert-Ant-Labs/desert-ant-core (tag `v3.1.0`: `Package.swift`, `LICENSE.md`,
  `Sources/Voz/Voz.swift`, `Sources/Voz/Words.swift`, `Sources/Voz/Catalog.swift`,
  `Sources/ModelStore/ModelStore.swift`, `Sources/Usage/*`, `docs/models/voz.md`, `README.md`)
- https://license.desertant.com/1.0

**Confidence:** high for API, licence summary and availability; medium for the telemetry description.

**Not verified:** that every Voz load actually emits the usage report (the client and its wiring were read, the
call path was not traced end to end); legal interpretation of the licence for this project.

---

## 5. LiveContainer

**Source caveat.** On the research date every direct URL under the upstream LiveContainer organisation
(repository page, raw files, API, documentation site) returned HTTP 404, while the organisation's repository
listing still showed the project as public and updated on 2026-10-09. The facts below were read from source
code in a public third-party fork that tracks upstream 3.8.2 and states that it leaves upstream behaviour
unchanged, plus an older README in a maintainer's fork. Re-check against upstream when it is reachable.

### 5.1 How a guest app runs

- LiveContainer is one installed, signed app. A guest app is unpacked into LiveContainer's own storage, its
  main binary is patched (`MH_EXECUTE` becomes `MH_DYLIB`, `__PAGEZERO` is shrunk, a loader command is added),
  and it is loaded with `dlopen` into LiveContainer's process. `NSBundle.mainBundle`, the home directory
  (`HOME`, `CFFIXED_USER_HOME`) and `UserDefaults` are redirected to the guest.
- With JIT, code signing is bypassed. Without JIT ("JIT-less"), LiveContainer re-signs the guest's executables
  with the certificate it imported from the sideloading store. Either way the IPA does not need a signature.
- The guest runs with **LiveContainer's entitlements, permissions and Info.plist-level registrations**, not its
  own.

### 5.2 What the IPA must contain

- A zip archive with a top-level `Payload/` folder containing one `.app` bundle (the installer takes the first
  `.app` it finds).
- A readable `Info.plist` with `CFBundleIdentifier` and `CFBundleExecutable`.
- A main executable with an **arm64 slice**, **not encrypted** (encrypted binaries are rejected with an error).
- A minimum OS version no higher than the device's.
- Build it in the **Release** configuration. Debug builds default to `ENABLE_DEBUG_DYLIB`, which moves the app's
  code into a separate `<Name>.debug.dylib` behind a stub executable; that layout is not what the patcher
  expects. If a debug IPA is ever needed, set `ENABLE_DEBUG_DYLIB: NO`.

### 5.3 What it need not or must not rely on

| Area | Behaviour inside LiveContainer | Consequence for this app |
| --- | --- | --- |
| Signature, provisioning profile | Not needed; ignored or replaced | Build with signing disabled |
| Entitlements | "Entitlements from the guest app are not applied to the host app" | No keychain groups, app groups, iCloud, push, associated domains |
| Keychain | `SecItemAdd`, `SecItemCopyMatching`, `SecItemUpdate`, `SecItemDelete` are hooked and `kSecAttrAccessGroup` is **overwritten** with one of LiveContainer's 128 shared groups, chosen per data container | Do not pass an access group. Items are separated from other guests only by group plus service and account, so use a service string unique to this app. If the host was installed without those groups the hooks are skipped and calls can fail with -34018; keep a file fallback. |
| Keychain accessibility | Not altered by the hook | `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly` as in the old app remains appropriate for background use |
| App groups | Guest's group entitlement is absent | Do not use `containerURL(forSecurityApplicationGroupIdentifier:)` or suite-based `UserDefaults` |
| Microphone, camera | Granted to LiveContainer as a whole: "App Permissions are globally applied". The prompt shows the host's generic text ("The guest app is requesting for this permission."), not the app's own usage string | Handle the denied state with instructions that name LiveContainer in Settings. Keep the usage strings in Info.plist anyway for a normal install. |
| Background audio | The host's `UIBackgroundModes` include `audio` (also `voip`, `fetch`, `location`, `remote-notification`, and others) | Recording with an active audio session can continue in the background as it would in a normal install |
| Local network | Host declares `NSLocalNetworkUsageDescription` and a fixed `NSBonjourServices` list | See below; connections over a VPN or to the public internet need no local-network permission at all |
| Custom URL schemes | Only the host's `livecontainer` scheme is registered with iOS; guest `CFBundleURLTypes` are not. LiveContainer has its own "open in app" forwarding | Do not make pairing or any feature depend on the system opening a custom scheme or universal link. QR scanning inside the app is the right mechanism. |
| Notifications | Local notifications need the per-app "Fix Local Notifications" setting; "Remote push notification might not work" | Avoid notifications, or document the setting |
| App extensions | "App extensions aren't supported" | No widgets, Live Activities, share or intents extensions |
| Sandbox | "Guest app containers are not sandboxed. This means one guest app can access other guest apps' data." | Treat on-disk secrets as readable by other guests; keep secrets in the Keychain where possible |
| Storage location | Guest data lives under LiveContainer's own Documents folder | Large model downloads become part of the host's backup unless excluded; set `isExcludedFromBackup` on model and cache folders (inference from the layout) |

Local network detail (Apple TN3179): "A local network is an IP network associated with a broadcast-capable
network interface. Such interfaces include Wi-Fi and Ethernet, but not cellular (WWAN) or VPN." A connection
to a tailnet address or to a public HTTPS endpoint therefore does not trigger the local-network prompt. A
connection to a LAN address typed in by hand would, and would then use LiveContainer's prompt text. The
simulator does not implement local network privacy at all.

### 5.4 Design version (Liquid Glass) inside LiveContainer

System frameworks decide between the old and the new design by asking which SDK the *program* was linked
against, and the program here is LiveContainer. LiveContainer has a per-app setting, "Spoof SDK Version", whose
description reads: "Spoofing SDK version forces system libraries to operate in the SDK version the app is built
with." When it is on, LiveContainer hooks `dyld_program_sdk_at_least` and `dyld_get_program_sdk_version` to
report the guest's SDK version.

Consequence: if the installed LiveContainer build was itself linked against an older SDK, the remake could show
the pre-26 design on iOS 26 until that setting is enabled for it. The README should tell the user to enable
"Spoof SDK Version" if bars and sheets do not look like iOS 26. The remake's own `glassEffect` calls are gated
on the OS version and are not affected by this.

### 5.5 Installing a second app alongside the first

Verified in the installer source: a guest is stored in a folder named `<bundle identifier>.app`, and existing
apps are matched **by bundle identifier**. Installing an IPA whose identifier already exists opens a prompt:
"There is an existing application with the same bundle identifier. Replace one or install as new." Replacing
keeps the data container and per-app settings. An IPA with a **different bundle identifier installs without a
prompt as a separate app** with its own data container and its own keychain group assignment. The `.app`
folder name inside the IPA does not matter.

For the remake this means a new bundle identifier, a new Keychain service string, and no shared files with the
old app; the old install is left untouched.

### Sources

- LiveContainer source as mirrored at https://github.com/SSH-PuR66/LiveContainer-Plus (files
  `LiveContainer/Tweaks/SecItem.m`, `LiveContainer/Tweaks/Dyld.m`, `LiveContainer/LCBootstrap.m`,
  `LiveContainerSwiftUI/Views/AppList/LCAppListView.swift`, `LiveContainerSwiftUI/Models/LCAppInfo.m`,
  `TweakLoader/Notification.m`, `TweakLoader/UIKit+GuestHooks.m`, `Resources/Info.plist`, `entitlements.xml`,
  `Resources/Localizable.xcstrings`)
- Older upstream README as kept at https://github.com/hugeBlack/LiveContainer
- https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy
- https://developer.apple.com/documentation/xcode/build-settings-reference (`ENABLE_DEBUG_DYLIB`)

**Confidence:** medium. Mechanisms were read from source, but from a fork rather than upstream, and nothing was
run on a device.

**Not verified:** current upstream state (unreachable); which SDK the user's installed LiveContainer build was
linked against; background audio in LiveContainer's multitasking mode, where guests run in an extension
process; which App Transport Security dictionary applies to a guest (the host allows arbitrary loads);
behaviour of the Keychain when the host lacks its 128 groups.

---

## 6. XcodeGen

### 6.1 Version

XcodeGen **2.46.0** (2026-07-16) is current, and it is the version Homebrew installs (bottles exist for the
macOS 15 and macOS 26 arm64 runners). 2.46.0 added Swift package `traits` on package references, preserves
target declaration order, and now embeds static frameworks by default.

### 6.2 Layout: a real package for the core

A `Package.swift` for the core is compatible with the app target. Two workable shapes:

- **Recommended:** put the package in a subfolder (`Packages/LiveCueCore/Package.swift` with `Sources/` and
  `Tests/` beneath it) and reference it as a local package. Xcode links SwiftPM library products statically, so
  the IPA carries no embedded framework. Tests run with `swift test` on macOS and Linux, and the same test
  target can be added to the Xcode scheme to run on the simulator.
- The old project's shape: a framework target in `project.yml` and a root `Package.swift` that both point at the
  same source folders. It works, but it produces an embedded dynamic framework and keeps two definitions in
  step by hand. A local package whose path is the repository root itself should be avoided, because the
  package directory would contain the generated project.

```swift
// Packages/LiveCueCore/Package.swift
// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "LiveCueCore",
    platforms: [.iOS(.v18), .macOS(.v15)],
    products: [.library(name: "LiveCueCore", targets: ["LiveCueCore"])],
    targets: [
        .target(name: "LiveCueCore"),
        .testTarget(name: "LiveCueCoreTests", dependencies: ["LiveCueCore"])
    ]
)
```

With tools version 6.0 the package defaults to the Swift 6 language mode. To hold a target at Swift 5 use
`swiftSettings: [.swiftLanguageMode(.v5)]` (PackageDescription 6.0). `.defaultIsolation(...)` exists from 6.2.

### 6.3 project.yml

```yaml
name: LiveCueRemake
options:
  bundleIdPrefix: com.example
  deploymentTarget:
    iOS: "18.0"
  xcodeVersion: "26.3"
settings:
  base:
    SWIFT_VERSION: "5.0"
    SWIFT_STRICT_CONCURRENCY: complete
    DEVELOPMENT_TEAM: ""
    CODE_SIGN_STYLE: Manual
    CODE_SIGNING_ALLOWED: "NO"
    CODE_SIGNING_REQUIRED: "NO"
    CODE_SIGN_IDENTITY: ""
packages:
  LiveCueCore:
    path: Packages/LiveCueCore
  FluidAudio:
    url: https://github.com/FluidInference/FluidAudio
    exactVersion: 0.15.6
  DesertAnt:
    url: https://github.com/Desert-Ant-Labs/desert-ant-core
    exactVersion: 3.1.0
targets:
  LiveCueRemake:
    type: application
    platform: iOS
    sources: [App]
    dependencies:
      - package: LiveCueCore
        product: LiveCueCore
      - package: FluidAudio
        product: FluidAudio
      - package: DesertAnt
        product: Voz
    settings:
      base:
        PRODUCT_BUNDLE_IDENTIFIER: com.example.LiveCueRemake
        TARGETED_DEVICE_FAMILY: "1"
        ENABLE_USER_SCRIPT_SANDBOXING: "YES"
        CODE_SIGN_ENTITLEMENTS[sdk=iphonesimulator*]: Support/Simulator.entitlements
  LiveCueRemakeUITests:
    type: bundle.ui-testing
    platform: iOS
    sources: [UITests]
    dependencies:
      - target: LiveCueRemake
    settings:
      base:
        GENERATE_INFOPLIST_FILE: "YES"
schemes:
  LiveCueRemake:
    build:
      targets:
        LiveCueRemake: all
        LiveCueRemakeUITests: [test]
    test:
      gatherCoverageData: true
      targets:
        - package: LiveCueCore/LiveCueCoreTests
        - name: LiveCueRemakeUITests
```

Quote `"NO"` and `"YES"`; unquoted they are YAML booleans. Info.plist keys (usage descriptions,
`UIBackgroundModes: [audio]`, launch screen, orientations) go under the target's `info.properties`, as in the
old project.

### 6.4 Unsigned device build

Proven by the old release workflow:

```sh
xcodebuild build -project LiveCueRemake.xcodeproj -scheme LiveCueRemake -configuration Release \
  -sdk iphoneos -destination "generic/platform=iOS" -derivedDataPath "$RUNNER_TEMP/DerivedData" \
  CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY=""

mkdir -p "$RUNNER_TEMP/Package/Payload"
ditto "$RUNNER_TEMP/DerivedData/Build/Products/Release-iphoneos/LiveCueRemake.app" \
      "$RUNNER_TEMP/Package/Payload/LiveCueRemake.app"
ditto -c -k --sequesterRsrc --keepParent "$RUNNER_TEMP/Package/Payload" LiveCueRemake.ipa
```

Validate before publishing: the bundle identifier and version in the built `Info.plist`, `file` reporting an
arm64 executable, and `unzip -t` on the archive.

### 6.5 Simulator tests with ad hoc signing, and why the entitlements file exists

```sh
xcodebuild test -project LiveCueRemake.xcodeproj -scheme LiveCueRemake \
  -destination "platform=iOS Simulator,id=$DEVICE_ID" -parallel-testing-enabled NO \
  -resultBundlePath artifacts/Tests.xcresult \
  CODE_SIGNING_ALLOWED=YES CODE_SIGN_STYLE=Automatic CODE_SIGN_IDENTITY=-
```

```xml
<!-- Support/Simulator.entitlements -->
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>application-identifier</key><string>com.example.LiveCueRemake</string>
  <key>keychain-access-groups</key><array><string>com.example.LiveCueRemake</string></array>
  <key>get-task-allow</key><true/>
</dict></plist>
```

Why: Apple's developer support explains that the iOS keychain "enforces access control via entitlements. If you
don't have appropriate entitlements, you get this -34018 (`errSecMissingEntitlement`) error", and that on the
simulator real entitlements cannot be used, so Xcode embeds *simulated* entitlements in the binary's
`__TEXT,__entitlements` section. The project default here is signing switched off (needed for the unsigned
device build), and a binary built that way carries no entitlements, so Keychain calls in simulator tests fail.
Turning signing on with the ad hoc identity (`-`) and supplying an entitlements file scoped to
`sdk=iphonesimulator*` makes Xcode embed an application identifier and a keychain access group with no team
or profile. The scope keeps the file out of the device build. Inspect the result with
`otool -s __TEXT __entitlements -V <path to simulator binary>`.

### 6.6 Swift 6 language mode or Swift 5 with strict concurrency

- `SWIFT_VERSION` selects the language mode (`"5.0"` or `"6.0"`); Xcode 26 and 27 support both.
- `SWIFT_STRICT_CONCURRENCY` (`minimal`, `targeted`, `complete`): "This is always 'complete' when in the Swift 6
  language mode and produces errors instead of warnings."
- Xcode 26 also has `SWIFT_APPROACHABLE_CONCURRENCY` and `SWIFT_DEFAULT_ACTOR_ISOLATION` (`MainActor` makes
  unannotated code main-actor isolated).

Recommendation for a project that cannot compile locally:

- **App target: Swift 5 mode with `SWIFT_STRICT_CONCURRENCY: complete`.** The same diagnostics appear as
  warnings, so a data-race finding does not cost a CI cycle. The old project used Swift 5 with `targeted`.
- **Core package: Swift 6 mode.** It is small, has no UI, and is compiled by the fast Linux job, so errors there
  are cheap.
- Leave `SWIFT_DEFAULT_ACTOR_ISOLATION` unset and annotate view models with `@MainActor` explicitly. Both
  speech packages hand callbacks across isolation boundaries as `@Sendable` closures, and explicit isolation
  keeps that code predictable.

### Sources

- https://github.com/yonaskolb/XcodeGen/releases/tag/2.46.0 and `Docs/ProjectSpec.md` in that repository
- https://developer.apple.com/documentation/xcode/build-settings-reference
- https://developer.apple.com/forums/thread/733449 (simulated entitlements and error -34018)
- https://github.com/swiftlang/swift-package-manager (`Sources/PackageDescription/BuildSettings.swift`, release 6.2)
- The old project's `project.yml` and workflows, which ran green with these signing arguments

**Confidence:** high for XcodeGen options and build settings; medium for the entitlement explanation.

**Not verified:** no primary source states outright that `CODE_SIGNING_ALLOWED=NO` strips simulated
entitlements; this is inferred from the mechanism and from the old project's history. The sample `project.yml`
was not generated or built.

---

## 7. Checking Swift without a Mac

### 7.1 Linux CI (recommended)

- `ubuntu-latest` has Swift 6.4 preinstalled. Official `swift` container images exist for 6.0 to 6.4 on Ubuntu
  24.04 (`noble`) and other distributions; `swift:6.2-noble` is about 1.1 GB compressed.
- Use the container that matches the Xcode compiler line (Xcode 26.3 ships Swift 6.2.3), so that code accepted
  on Linux is not rejected on macOS for using a newer language feature.
- `swift build` and `swift test` work for a package that depends only on Foundation. XCTest and Swift Testing
  ship with the toolchain.

What is and is not available on Linux:

| Available | Not available |
| --- | --- |
| Foundation value types, `JSONEncoder`/`JSONDecoder`, `Data`, `URL`, `Date`, regular expressions | SwiftUI, UIKit, Combine, SwiftData, AVFoundation, Core ML, Security (Keychain), CryptoKit, `os.Logger` |
| Swift concurrency, `AsyncStream`, the `Observation` module | `URLSession.bytes(for:)` and `URLSession.AsyncBytes` (absent from `swift-corelibs-foundation`) |
| `URLSession` data, upload and download tasks, and `URLSessionWebSocketTask`, all in `FoundationNetworking` | |

Rules for the core so that it compiles on both:

```swift
import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking   // URLSession, URLRequest, HTTPURLResponse on Linux
#endif
```

- The old core calls `session.bytes(for: request)` for server-sent events. Put streaming HTTP behind a small
  protocol in the core (for example a function returning `AsyncThrowingStream<Data, Error>`), keep the parser
  and all logic in the core, and put the `URLSession.bytes` implementation in the app target or in a file
  wrapped in `#if canImport(Darwin)`.
- Keep Keychain, audio capture, speech models and persistence out of the core.
- Do not rely on `Bundle.module` resources or locale-dependent formatting in tests without checking them on
  Linux as well.

A green Linux job proves the logic compiles and the tests pass; it does not prove the iOS build, because Apple
frameworks and availability checks are not exercised.

### 7.2 On the Windows PC

- A native Swift toolchain for Windows exists (6.4.0, `winget install --id Swift.Toolchain`), but it requires
  Visual Studio 2022 with the MSVC tools and a Windows SDK. The PC has neither Swift nor those build tools, and
  installing a toolchain is outside the project's "no new toolchain" constraint.
- Docker Desktop and WSL are already present on the PC. If the owner approves a one-time image download of
  about 1.1 GB, the same check as CI runs locally with no host installation:
  `docker run --rm -v "<repo>:/work" -w /work swift:6.2-noble swift test --package-path Packages/LiveCueCore`.
- Without that approval, the Linux CI job is the cheapest feedback loop: it is free, unlimited, and does not
  count against the macOS concurrency limit.

### Sources

- https://www.swift.org/install/windows/ and https://www.swift.org/install/linux/
- https://hub.docker.com/_/swift
- https://github.com/swiftlang/swift-corelibs-foundation (`Sources/FoundationNetworking/URLSession/URLSession.swift`,
  `URLSessionTask.swift`)
- `images/ubuntu/Ubuntu2404-Readme.md` in https://github.com/actions/runner-images

**Confidence:** high.

**Not verified:** nothing was built on Linux or in Docker; WebSocket support in `FoundationNetworking` depends
on the libcurl build in the image and was not exercised.

---

## 8. WPF on .NET 10 for a glass interface

### 8.1 Toolchain facts

- .NET 10 is the current LTS (released November 2025, supported to 2028-11-14). Latest patch: runtime 10.0.12,
  SDK **10.0.401** (2026-09-08).
- The portable SDK used on the build PC reports `--version` **10.0.401** and `--list-sdks` **10.0.401** only.
  Its folder also holds the 10.0.12 `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App` and
  `Microsoft.AspNetCore.App` shared frameworks and their reference packs. That matches `windows-latest`.
- The PC has no system-wide SDK and no .NET 10 runtime (system runtimes are 5.0, 6.0 and 9.0), so the desktop
  app must be published **self-contained**.
- The PC runs Windows 11 23H2 (build 22631), which meets the 22621 minimum for system backdrops.

### 8.2 Built-in Fluent theme

- Introduced in .NET 9. Enable it with `ThemeMode="System"` (or `Light`, `Dark`, `None`) on `Application` or
  `Window` in XAML, or by merging
  `pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml`.
- Setting `ThemeMode` **from code is still experimental in .NET 10**: the property carries
  `[Experimental("WPF0001")]` in the 10.0 source, so code access fails to build unless `WPF0001` is suppressed
  (`<NoWarn>$(NoWarn);WPF0001</NoWarn>` or `#pragma warning disable WPF0001`). Setting it in XAML needs no
  suppression.
- .NET 10 added Fluent styles for more controls (`DatePicker`, `GridSplitter`, `GridView`, `GroupBox`,
  `Hyperlink`, `Label`, `NavigationWindow`, `RichTextBox`, `TextBox`) and fixed bugs. Microsoft's own wording:
  "Fluent UI style support is still in progress."
- With the Fluent theme active, WPF applies a Mica backdrop to windows itself. Opt out with the AppContext
  switch `Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop`.
- If the app-level `ThemeMode` is not `None`, a window cannot go back to `None`.
- Accent colours are available with or without the theme through `SystemColors.AccentColorBrushKey` and its
  light and dark variants.

For a bespoke light glass design, the built-in theme is best treated as a base for standard controls with its
brushes overridden, or left off in favour of the app's own styles. `ThemeMode="Light"` keeps the app light
when Windows is in dark mode.

### 8.3 System backdrops through DWM

`DwmSetWindowAttribute` attribute values, from the Windows SDK documentation:

| Attribute | Value | Minimum build |
| --- | --- | --- |
| `DWMWA_USE_IMMERSIVE_DARK_MODE` | 20 | 22000 |
| `DWMWA_WINDOW_CORNER_PREFERENCE` | 33 | 22000 |
| `DWMWA_BORDER_COLOR` | 34 | 22000 |
| `DWMWA_CAPTION_COLOR` | 35 | 22000 |
| `DWMWA_TEXT_COLOR` | 36 | 22000 |
| `DWMWA_SYSTEMBACKDROP_TYPE` | 38 | **22621** |

`DWM_SYSTEMBACKDROP_TYPE`: `DWMSBT_AUTO` 0, `DWMSBT_NONE` 1, `DWMSBT_MAINWINDOW` 2 (Mica),
`DWMSBT_TRANSIENTWINDOW` 3 (Desktop Acrylic), `DWMSBT_TABBEDWINDOW` 4 (Mica Alt).

The sequence below is the one WPF's own `WindowBackdropManager` uses:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

internal static class Backdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int Mica = 2, Acrylic = 3, MicaAlt = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    /// Call from Window.OnSourceInitialized. The window must NOT set AllowsTransparency.
    public static bool Apply(Window window, int type, bool light = true)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return false;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || HwndSource.FromHwnd(hwnd)?.CompositionTarget is not { } target) return false;

        window.Background = Brushes.Transparent;
        target.BackgroundColor = Colors.Transparent;

        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int dark = light ? 0 : 1;   // 0 keeps the backdrop light whatever the system theme is
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) == 0;
    }
}
```

Facts to design around:

- Mica samples the desktop wallpaper once and is opaque; Acrylic blurs whatever is behind the window.
- The backdrop falls back to a **solid colour** when the window is inactive, when the user turns off
  transparency effects, in battery saver, on low-end hardware and in High Contrast. The design must look right
  in that state, so always paint a deliberate ground colour or gradient that reads well without the backdrop.
- A backdrop appears only where every layer between it and the window is transparent.
- WPF has no "blur what is behind this element" brush. A glass card inside the window is a translucent fill
  with a border highlight and a soft shadow over the ground; that reads as glass over a smooth gradient without
  real blur. Blurring live in-window content would mean rendering it to a brush and applying `BlurEffect`,
  which is costly and rarely worth it.

### 8.4 Custom title bar with WindowChrome

```xml
<Window ... xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework">
  <shell:WindowChrome.WindowChrome>
    <shell:WindowChrome CaptionHeight="44" ResizeBorderThickness="6"
                        GlassFrameThickness="-1" CornerRadius="0" UseAeroCaptionButtons="False" />
  </shell:WindowChrome.WindowChrome>
  <!-- Buttons in the caption area need: shell:WindowChrome.IsHitTestVisibleInChrome="True" -->
</Window>
```

- `WindowChrome` extends the client area over the title bar and keeps system move, resize, snap and
  double-click-to-maximise through the invisible caption and resize areas.
- `GlassFrameThickness="-1"` extends the frame over the whole window, which is what a backdrop needs.
  A uniform `0` removes the glass frame and hides the standard caption buttons.
- `CornerRadius` has no effect while the glass frame is enabled; Windows 11 rounds the window itself.
- Elements in the caption area are not interactive unless `WindowChrome.IsHitTestVisibleInChrome="True"`.
- The app draws its own icon, title and caption buttons.
- `WindowStyle="None"` alone removes resizing and makes a maximised window cover the taskbar; prefer
  `WindowChrome`. A maximised chrome window overhangs the screen by the resize border, so add a matching margin
  to the root when `WindowState == Maximized`.

### 8.5 AllowsTransparency pitfalls

- `AllowsTransparency="True"` requires `WindowStyle="None"`; otherwise an `InvalidOperationException` is thrown.
- It exists for non-rectangular windows and makes the window a layered window: no system frame, no native
  shadow, rounded corners or snap behaviour, and extra rendering cost.
- WPF's own backdrop code refuses to apply a system backdrop when `AllowsTransparency` is set.
- Hosted native child windows (`WebBrowser`, `WindowsFormsHost`) do not render correctly inside layered windows.

Use the DWM backdrop path for see-through glass and keep `AllowsTransparency` off.

### 8.6 Per-monitor DPI

"WPF apps are System DPI aware by default, and need to declare themselves to be Per Monitor DPI aware via an
app.manifest file." Add an application manifest and reference it with `<ApplicationManifest>`:

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2, PerMonitor</dpiAwareness>
  </windowsSettings>
</application>
```

Windows uses the newer element when both are present. WPF then raises `Window.DpiChanged` and
`Visual.OnDpiChanged`; `VisualTreeHelper.GetDpi(visual)` returns the current scale. Layout in device-independent
units needs no code; bitmap assets should be vector or multi-resolution.

### 8.7 Publishing

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <DebugType>embedded</DebugType>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />  <!-- only if the relay is hosted in-process -->
  </ItemGroup>
</Project>
```

- `dotnet publish -c Release -r win-x64` produces the single file. Single-file apps are specific to one OS and
  architecture.
- **Trimming is not available**: "almost no WPF apps are runnable after trimming, so trimming support for WPF is
  currently disabled in the .NET SDK". Do not set `PublishTrimmed`.
- Only managed assemblies are bundled by default; WPF's native libraries stay beside the executable unless
  `IncludeNativeLibrariesForSelfExtract` is set, in which case they are extracted to a folder under the user's
  temp directory at first start.
- `EnableCompressionInSingleFile` reduces size and costs start-up time. Measure both.
- In a single-file app `Assembly.Location` is empty. Use `AppContext.BaseDirectory` and
  `Environment.ProcessPath`.
- Self-contained publish restores runtime packs from NuGet, which is a package restore rather than a new
  install.
- Pin the SDK with a `global.json` (`"version": "10.0.401"`, `"rollForward": "latestFeature"`) so the PC and CI
  agree.

### 8.8 QR code generation with permissive licences

| Package | Version | Licence | Notes |
| --- | --- | --- | --- |
| `Net.Codecrete.QrCodeGenerator` | 3.2.1 (2026-09-07) | MIT | No dependencies; returns the module matrix and can emit SVG or bitmap bytes. Easy to draw as WPF geometry. |
| `QRCoder` | 1.8.0 (2026-04-04) | MIT | Widely used; PNG-bytes and SVG renderers need no imaging library, though the package references `System.Drawing.Common` on modern targets. |
| `ZXing.Net` | 0.16.11 (2025-10-25) | Apache-2.0 | Generates and reads many barcode types; larger than needed for one QR code. |

Either MIT option keeps QR generation fully local, as the old desktop app did.

### Sources

- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100 and .../net90
- https://github.com/dotnet/wpf (branch `release/10.0`: `System/Windows/Application.cs`, `Window.cs`,
  `System/Windows/Appearance/WindowBackdropManager.cs`, `MS/Internal/FrameworkAppContextSwitches.cs`)
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type
- https://learn.microsoft.com/en-us/windows/apps/design/style/mica
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency
- https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md
- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities
- https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
- NuGet registration data for the three packages

**Confidence:** high for API values, theme status and publishing rules; medium for design guidance.

**Not verified:** published size (nothing was built; expect on the order of 60 to 80 MB compressed and well
over 100 MB uncompressed, to be measured on the first publish); the `PerMonitorV2` token specifically on
.NET 10 (the cited guide predates it); the C# snippet was not compiled.

---

## 9. Relay runtime: inputs for the decision

### 9.1 Facts

- The old relay is Node and TypeScript: about 750 lines across ten source files, with seven test files, two
  dependencies (`ws` 8.22.0 and `qrcode-terminal` 0.12.0), `"engines": { "node": ">=24" }`, run directly with
  `node --experimental-strip-types`.
- What it does: HTTP JSON endpoints under `/v1/` (health, pair verification, model catalogue, local speech
  model status, start and stop, assist, session summary), one WebSocket endpoint (`/v1/speech`, 16 KiB maximum
  message, compression off) that relays audio to a local speech service, bearer-token checks, child processes
  for the assistant command-line tool and for PowerShell (DPAPI and model control scripts), and a
  line-oriented control channel over stdin and stdout to the desktop window. The old desktop app starts the
  relay as a child process.
- A separate Node module adapts the relay to an existing authenticated tunnel gateway on the PC.
- Build PC: `node --version` **v24.14.1**, `npm --version` **11.11.0**.
- Node release lines: **24 "Krypton" is the active LTS** (latest 24.21.0; maintenance from 2026-10-20; end of
  life 2028-04-30). Node 26 is the Current line (26.11.1) and **becomes LTS on 2026-10-28**. Node 22 "Jod" is in
  maintenance until 2027-04-30.
- Type stripping is on by default since Node 23.6 and **stable since 24.12.0**, so the old
  `--experimental-strip-types` flag is unnecessary on the PC's Node. Limits: no enums, parameter properties or
  runtime namespaces; `tsconfig.json` is ignored; imports need explicit `.ts` extensions and `import type`;
  files under `node_modules` are not stripped.
- ASP.NET Core on .NET 10: Kestrel can be hosted inside any process, including a WPF app, by referencing the
  `Microsoft.AspNetCore.App` shared framework. WebSockets are built in (`app.UseWebSockets()`,
  `HttpContext.WebSockets.AcceptWebSocketAsync()`, options `KeepAliveInterval`, `KeepAliveTimeout`,
  `AllowedOrigins`). Server-sent events have a first-class result in .NET 10:
  `TypedResults.ServerSentEvents(IAsyncEnumerable<...>, eventType: ...)` with `SseItem<T>`.
- The shared frameworks needed for that are already in the portable SDK and on `windows-latest`.

### 9.2 Trade-offs

| Topic | Keep Node and TypeScript | Host in the .NET app (ASP.NET Core) |
| --- | --- | --- |
| Starting point | Port or reuse about 750 lines and their tests | Rewrite; the old tests become the specification |
| Install | Needs Node 24 and `npm install` beside the desktop app | One self-contained executable, nothing else to install |
| Process model | Second process; lifecycle, restart and a control protocol over pipes | In-process; the window reads relay state directly |
| WebSocket | `ws` with `maxPayload` and compression switches | Built in; compression is off unless enabled; **no built-in maximum message size**, so the receive loop must enforce it |
| Streaming responses | Manual `text/event-stream` writes | `TypedResults.ServerSentEvents`, or manual writes |
| Child processes | `child_process.spawn` | `System.Diagnostics.Process` with redirected streams; `Kill(entireProcessTree: true)` |
| Windows secrets | Spawns PowerShell for DPAPI | `ProtectedData` in-process |
| Testing | `node --test`, fast, no build | xUnit against `TestServer` or a real loopback Kestrel on port 0; runs with the portable SDK and on `windows-latest` |
| Languages on the Windows side | Two | One, with shared types between relay and window |
| Tunnel gateway adapter | Same language as the relay | Stays a small Node module either way, because it plugs into a Node gateway |
| Publish size | Small scripts plus Node | Adds the ASP.NET Core shared framework to the self-contained publish |

Requirements that hold for both: bind to loopback only (`options.Listen(IPAddress.Loopback, port)` in Kestrel;
`ListenLocalhost` also binds IPv6 loopback), use a port different from the old relay so both can run, and keep
the "keep the request pipeline alive until the socket closes" rule for WebSockets in ASP.NET Core:
"you **must** keep the middleware pipeline running for the duration of the connection."

```csharp
// Hosting Kestrel inside the desktop process without taking over the UI thread.
var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, port));
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.MapGet("/v1/health", () => Results.Json(new { ok = true }));
await app.StartAsync();      // not Run(): the WPF dispatcher owns the main thread
// ... on shutdown:
await app.StopAsync();
await app.DisposeAsync();
```

### 9.3 Recommendation

Host the relay in .NET, as its own class library with no WPF dependency, consumed by the desktop app and by a
small console host. Reasons: a single self-contained install with no Node prerequisite, relay state available
to the window without a pipe protocol, native DPAPI, one language and one test runner for the Windows side, and
everything buildable and testable on the PC with the SDK that is already there.

The cost is a rewrite of working code. Contain it by writing the black-box contract tests first (HTTP and
WebSocket behaviour taken from the old tests, including the 16 KiB message limit, token checks and error
shapes), running them against the old relay to confirm they describe real behaviour, and then against the new
one. Keep the tunnel-gateway adapter as a Node module with its existing style of tests.

If the architect prefers the smallest possible change, keeping Node is viable: Node 24 is LTS and present, the
code is small, and the experimental flag can be dropped.

### Sources

- The old project's `Relay/package.json`, `Relay/src/*.ts`, `Relay/funnel/livecue-proxy.mjs` and README
- https://nodejs.org/dist/index.json and https://github.com/nodejs/Release (`schedule.json`)
- https://nodejs.org/docs/latest-v24.x/api/typescript.html
- https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets

**Confidence:** high for the facts; the recommendation is a judgement.

**Not verified:** the size added by the ASP.NET Core framework in a self-contained publish; whether
`ProtectedData` needs an extra package reference on `net10.0-windows`; the C# snippet was not compiled.
