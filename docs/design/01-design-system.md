# LiveCue design system: foundations

Status: foundations for the remake. Screen layouts follow in a later document once the functional spec is final.

Audience: one SwiftUI developer (iPhone) and one WPF developer (Windows) working independently. Everything here is a decision, not a menu. Where a platform cannot do what the other does, the document says what it does instead.

The look is called **Daylight**: a pale blue ground with soft blooms, frosted white glass, navy ink and one blue accent. It is the same look the owner already uses in their other personal apps, so LiveCue has to read as a member of that family on first sight.

Contents

1. [Design principles](#1-design-principles)
2. [Tokens](#2-tokens)
3. [Glass material recipes](#3-glass-material-recipes)
4. [Component library](#4-component-library)
5. [Theme policy](#5-theme-policy)
6. [Accessibility](#6-accessibility)
7. [Motion and haptics](#7-motion-and-haptics)
8. [Brand](#8-brand)
9. [Do and don't](#9-do-and-dont)
10. [Layout direction](#10-layout-direction)
11. [Verification checklist](#11-verification-checklist)

---

## 1. Design principles

LiveCue is looked at for about a second at a time, by someone who is in the middle of talking to another person. Every rule below follows from that.

1. **Legibility first.** Text contrast is computed, not judged by eye. Every text and surface pairing in this document has a stated ratio at the worst point of the background. If a visual effect and a contrast ratio disagree, the ratio wins.
2. **Calm.** Nothing moves unless something happened. No ambient animation, no shimmer, no decorative gradients on text. The only looping motion is the live indicator while the microphone is open, because that is information.
3. **One accent.** Blue means "this is the thing to press" or "this is live". Green, amber and red appear only as status, never as decoration. A screen never shows two accent-filled controls at once.
4. **Glass is structure, not decoration.** Glass marks the layer that floats (controls and chrome). Content (the transcript, cues, summaries, lists) sits on quiet frosted panels that we fill ourselves, so their contrast is known. Glass never sits on glass.
5. **The cue is the hero.** On the live screen the newest cue has the largest body type and the brightest surface. Everything else is quieter than it.
6. **Stable geometry.** Text never reflows when its state changes (interim to final, idle to busy). Controls keep their size across states. A glance should land where it landed last time.
7. **Same family, native hands.** Both apps share tokens, proportions and component anatomy. Each uses its platform's own system font, controls and conventions where those exist.

---

## 2. Tokens

### 2.1 Naming and structure

Tokens are named by role, never by colour. Three layers:

| Layer | Example | Who may reference it |
|---|---|---|
| Palette (raw values) | `#0B57D0` | Only the theme definition |
| Semantic tokens | `accent.fill`, `surface.panel` | Components |
| Component tokens | `button.primary.height` | One component |

Platform spelling of the same token:

| Token | Swift | XAML resource key |
|---|---|---|
| `surface.panel` | `theme.surface.panel` | `SurfacePanelBrush` (brush), `SurfacePanelColor` (colour) |
| `ink.secondary` | `theme.ink.secondary` | `InkSecondaryBrush` |
| `radius.panel` | `theme.radius.panel` | `RadiusPanel` |

All colours are sRGB. Alpha is written as a decimal. Colours are composited source-over in gamma-encoded sRGB, which is what SwiftUI, WPF and browsers do by default; do not enable linear-space blending.

### 2.2 Ground

The ground is the only thing behind the app. It is opaque and does not scroll.

| Token | Value | Notes |
|---|---|---|
| `ground.base` | `#E6ECF5` α 1.0 | Fills the whole window or screen |
| `ground.bloom.1` | `#9DC1F5` | Centre (0.06, -0.06), radius 0.46 |
| `ground.bloom.2` | `#B9C4F7` | Centre (0.96, 0.24), radius 0.44 |
| `ground.bloom.3` | `#A8D4F2` | Centre (0.02, 0.64), radius 0.48 |
| `ground.bloom.4` | `#C7D3F5` | Centre (0.92, 1.00), radius 0.50 |

Bloom geometry, identical on both platforms:

- Centre is a fraction of (width, height) of the window or screen, including under safe areas and the title bar.
- Radius is a fraction of `max(width, height)`. Blooms are circles, not ellipses.
- Each bloom is a radial gradient from its colour at α 1.0 (offset 0) to the same colour at α 0 (offset 0.72). Nothing is drawn beyond 0.72.
- Paint order, bottom to top: base, bloom 4, bloom 3, bloom 2, bloom 1.
- Recompute on size change. No animation, no parallax.

Luminance extremes, used for every contrast figure below:

| Point | Colour | Relative luminance |
|---|---|---|
| Darkest the ground can be | `#9DC1F5` (bloom 1 at full strength) | 0.519 |
| Brightest the ground can be | `#E6ECF5` (base) | 0.834 |

Every ground pixel is a mix of the base and the blooms, so no pixel is darker than bloom 1.

### 2.3 Surfaces and elevation

Five elevations above the ground. "Solid twin" is the opaque colour used when transparency is reduced (section 6).

| Elevation | Token | Fill | Solid twin | Used for |
|---|---|---|---|---|
| 0 | `ground.*` | see 2.2 | `#E6ECF5` | Page background, page titles |
| 1 | `surface.panel` | `#FFFFFF` α 0.62 | `#F6F8FB` | Content panels, the Windows sidebar, list groups |
| 1 (nested) | `surface.inset` | `#FFFFFF` α 0.46 | `#FAFBFD` | Cards and tiles inside a panel only |
| 2 | `surface.raised` | `#FFFFFF` α 0.85 | `#FFFFFF` | Secondary buttons, the newest cue card, field fills, selected items, chip bases |
| 2 | `surface.raised.hover` | `#FFFFFF` α 1.0 | `#FFFFFF` | Windows hover on raised controls |
| 2 | `surface.raised.pressed` | `#EEF3FB` α 1.0 | `#EEF3FB` | Pressed raised controls |
| 3 | `surface.floating` | `#FFFFFF` α 0.62 over a backdrop blur | `#F6F8FB` | iPhone chrome that floats over scrolling content, on iOS 18 to 25 only. On iOS 26 this role is native Liquid Glass. Not used on Windows (rule W-1 in 3.5) |
| 4 | `surface.overlay` | `#F9FBFF` α 0.97 | `#F8FBFF` | Sheets, dialogs, menus |
| 5 | `surface.inverse` | `#0E1B33` α 0.90 | `#0E1B33` | Toasts, tooltips, the camera hint |
| n/a | `surface.scrim` | `#0E1B33` α 0.30 | same | Behind dialogs |
| n/a | `surface.qr` | `#FFFFFF` α 1.0 | same | QR code tile. Always opaque |

State overlays, drawn on top of whatever is underneath:

| Token | Value | Used for |
|---|---|---|
| `state.hover` | `#FFFFFF` α 0.45 | Windows hover on rows and quiet buttons, on `surface.panel` and `surface.inset` |
| `state.pressed` | `#FFFFFF` α 0.25 | Pressed rows and quiet buttons, on `surface.panel` and `surface.inset` |
| `state.hoverOnOverlay` | `#0E1B33` α 0.06 | Hover on rows, quiet buttons and menu items on `surface.overlay` |
| `state.pressedOnOverlay` | `#0E1B33` α 0.10 | Pressed, on `surface.overlay` |
| `state.captionHover` | `#0E1B33` α 0.08 | Windows title bar buttons only (glyph, no text) |

On panels and cards, hover and pressed only ever lighten. A darkening press overlay of ink at α 0.06 would take accent text on a panel from 4.80:1 to 4.28:1, so it is not used there. `surface.overlay` is already almost white, so a lighter overlay would be invisible on it; there the state overlays darken slightly, and the surface is bright enough that every text colour still passes (lowest 5.13:1 hovered and 4.73:1 pressed, over a black backdrop).

### 2.4 Strokes and sheen

| Token | Value | Width | Used for |
|---|---|---|---|
| `stroke.edge` | Vertical gradient `#FFFFFF` α 0.95 (top) to `#FFFFFF` α 0.60 (bottom) | 1 | Panel, floating and overlay edges. This is the top highlight |
| `stroke.raised` | `#FFFFFF` α 0.90 | 1 | Raised controls and prominent cards |
| `stroke.inset` | `#FFFFFF` α 0.70 | 1 | Inset cards |
| `stroke.onAccent` | `#FFFFFF` α 0.30 | 1 | Accent-filled controls |
| `stroke.hairline` | `#0E1B33` α 0.12 | 1 | Dividers. Decorative, carries no meaning |
| `stroke.control` | `#0E1B33` α 0.60 | 1 | Outline of text fields and other inputs whose boundary identifies them |
| `stroke.danger` | `#B3243F` α 0.35 | 1 | Outline of destructive secondary buttons |
| `stroke.focus` | `#0B57D0` α 1.0 | 2, offset 2 outside | Keyboard focus ring |
| `glass.sheen` | Vertical gradient `#FFFFFF` α 0.28 (top) to `#FFFFFF` α 0 at 40% of the height | fill | Inner glow on panels, floating chrome and overlays. Not on buttons |

Widths are points on iPhone and device-independent pixels on Windows.

### 2.5 Ink

| Token | Value | Used for |
|---|---|---|
| `ink.primary` | `#0E1B33` | Titles, body, final transcript text, cue text, labels on glass |
| `ink.secondary` | `#3D4A63` | Supporting text, interim transcript text, placeholders, timestamps, speaker labels |
| `ink.disabled` | `#0E1B33` α 0.38 | Disabled labels only |
| `ink.onAccent` | `#FFFFFF` | Text and glyphs on accent or danger fills |
| `ink.onInverse` | `#FFFFFF` | Text on `surface.inverse` |

There are only two readable ink levels. There is no third "tertiary" grey: anything a person needs to read uses primary or secondary.

### 2.6 Accent

| Token | Value | Used for |
|---|---|---|
| `accent.fill` | `#0B57D0` | Primary button fill, toggles on, progress, focus ring, live indicator, accent text and glyphs on our own surfaces (`surface.panel` or denser) |
| `accent.hover` | `#1A66DE` | Windows hover on accent fills |
| `accent.pressed` | `#0949B0` | Pressed accent fills |
| `accent.strong` | `#0842A0` | Accent-coloured text on anything we do not fill ourselves (native Liquid Glass, `surface.floating`, bare ground), and the accent under Increase Contrast |
| `accent.tint` | `#0B57D0` α 0.12 | Icon tiles, info banners, live chip |

### 2.7 Status colours

| Token | Value | Strong (Increase Contrast) | Tint | Meaning |
|---|---|---|---|---|
| `status.live` | `#0B57D0` (alias of `accent.fill`) | `#0842A0` | α 0.12 | Microphone open, listening |
| `status.success` | `#0B6443` | `#095438` | `#0B6443` α 0.12 | Connected, paired, done |
| `status.warning` | `#7D5108` | `#5F3D04` | `#7D5108` α 0.12 | Paused, reconnecting, expiring |
| `status.danger` | `#B3243F` | `#8F172E` | `#B3243F` α 0.12 | Error, destructive, stop glyph |
| `status.danger.hover` | `#BD2C47` | n/a | n/a | Windows hover on a danger fill |
| `status.danger.pressed` | `#9E1B34` | n/a | n/a | Pressed danger fill |
| `status.offline` | `#3D4A63` (alias of `ink.secondary`) | `#0E1B33` | `#0E1B33` α 0.08 | Offline, not paired, idle |

Listening is the product's normal state, so it is drawn in the accent, not in red. Red is reserved for the stop glyph, destructive actions and errors.

`status.live` is a separate token from `accent.fill` so a later theme can split them.

### 2.8 Control tracks

| Token | Value | Used for |
|---|---|---|
| `track.progress` | `#0E1B33` α 0.14 | Progress bar and ring track |
| `track.segmented` | `#0E1B33` α 0.10 | Windows segmented control trough |
| `track.toggleOff` | `#0E1B33` α 0.55 | Windows toggle, off |
| `control.knob` | `#FFFFFF` | Toggle knob |
| `control.scrollThumb` | `#0E1B33` α 0.25 | Windows scrollbar thumb |
| `shade` | `#283C6E` | Colour of every shadow except the knob's |

### 2.9 Contrast verification

Method: WCAG 2.2 relative luminance and contrast ratio. Translucent layers are composited in order over the darkest and the brightest ground colour from 2.2, including the surface's own shadow where it shows through the fill (see 2.13). The sheen and any backdrop blur are left out; both only lighten or average the ground, so the real figure is never lower than the stated one.

Thresholds: 4.5:1 for all text below 24 px regular or 18.66 px bold; 3:1 for larger text, glyphs and the parts of a control needed to identify it or its state.

**Table A. Text on our own surfaces.** Each cell is "over darkest ground / over brightest ground".

| Surface | Composite (darkest / brightest) | `ink.primary` | `ink.secondary` | `accent.fill` | `status.success` | `status.warning` | `status.danger` |
|---|---|---|---|---|---|---|---|
| Ground, no surface | `#9DC1F5` / `#E6ECF5` | 9.30 / 14.45 | 4.83 / 7.50 | 3.46 / 5.38 | 3.90 / 6.06 | 3.73 / 5.79 | 3.51 / 5.46 |
| `surface.panel` | `#D4E0F4` / `#EBEEF4` | 12.91 / 14.82 | 6.70 / 7.69 | 4.80 / 5.51 | 5.41 / 6.21 | 5.17 / 5.94 | 4.87 / 5.59 |
| `surface.panel` under a neighbour's shadow as well | `#CEDAEE` / `#E3E6EE` | 12.20 / 13.77 | 6.33 / 7.14 | 4.54 / 5.12 | 5.11 / 5.77 | 4.89 / 5.52 | 4.60 / 5.20 |
| `surface.inset` on panel | `#E8EEF9` / `#F4F6F9` | 14.77 / 15.87 | 7.66 / 8.23 | 5.49 / 5.91 | 6.19 / 6.65 | 5.92 / 6.36 | 5.57 / 5.99 |
| `surface.raised` on ground | `#EDF3FA` / `#F7F8FA` | 15.34 / 16.14 | 7.96 / 8.37 | 5.71 / 6.00 | 6.43 / 6.77 | 6.15 / 6.47 | 5.79 / 6.09 |
| `surface.raised` on panel | `#F8FAFD` / `#FCFDFD` | 16.48 / 16.80 | 8.55 / 8.72 | 6.13 / 6.25 | 6.91 / 7.04 | 6.60 / 6.73 | 6.22 / 6.34 |
| `surface.overlay` over scrim | `#F4F7FC` / `#F6F8FC` | 16.03 / 16.13 | 8.31 / 8.37 | 5.96 / 6.00 | 6.72 / 6.76 | 6.42 / 6.47 | 6.05 / 6.09 |

Reading the table:

- On `surface.panel` and everything denser, all six text colours pass AA (lowest 4.54:1).
- `surface.overlay` is opaque enough that the backdrop barely matters. Over pure black, with no scrim, it composites to `#F2F3F7` and the six colours are 15.52, 8.05, 5.77, 6.51, 6.22 and 5.86.
- Directly on the ground only `ink.primary` (9.30:1) and `ink.secondary` (4.83:1) pass. **Rule C-1: coloured text never sits directly on the ground**, except text of 24 px regular or 18.66 px bold and larger (lowest 3.46:1 against the 3:1 large-text threshold) and `accent.strong` (4.95:1).

**Table B. Text on fills.**

| Pairing | Ratio |
|---|---|
| `ink.onAccent` on `accent.fill` | 6.39 |
| `ink.onAccent` on `accent.hover` | 5.25 |
| `ink.onAccent` on `accent.pressed` | 8.10 |
| `ink.onAccent` on `status.danger` | 6.48 |
| `ink.onAccent` on `status.danger.hover` | 5.81 |
| `ink.onAccent` on `status.danger.pressed` | 7.89 |
| `ink.onInverse` on `surface.inverse` over pure white (worst case) | 12.90 |
| `ink.onInverse` on `surface.inverse` over darkest ground | 14.06 |
| QR modules `#0E1B33` on `surface.qr` | 17.17 |
| White glyph on the Windows close-button hover `#C42B1C` | 5.66 |

**Table C. Chips and banners.** A chip or banner always brings its own base: `surface.raised`, then the status tint at α 0.12, then text in the status colour. Each cell is darkest / brightest.

| Chip on | accent | success | warning | danger | neutral (`ink.secondary` on ink α 0.08) |
|---|---|---|---|---|---|
| Ground | 4.78 / 5.01 | 5.38 / 5.64 | 5.17 / 5.43 | 4.78 / 5.02 | 6.79 / 7.14 |
| `surface.panel` | 5.11 / 5.20 | 5.75 / 5.86 | 5.53 / 5.64 | 5.12 / 5.22 | 7.29 / 7.43 |
| `surface.inset` | 5.20 / 5.25 | 5.85 / 5.91 | 5.63 / 5.69 | 5.21 / 5.27 | 7.42 / 7.50 |

`ink.primary` body text on any of these tinted bases is at least 12.67:1 and `ink.secondary` at least 6.57:1.

A tint laid straight on a panel with no raised base (how the web reference builds badges) gives 4.19:1 for accent and danger text at the darkest point, which fails. That construction is not used for text. It is used for icon tiles, where the glyph only needs 3:1: accent glyph on `accent.tint` on panel is 4.07 / 4.63, and `ink.primary` on it is 10.95 / 12.44.

**Table D. Non-text contrast (3:1).** Each cell is darkest / brightest.

| Element | Against | Ratio |
|---|---|---|
| Focus ring `accent.fill` | Ground | 3.46 / 5.38 |
| Focus ring `accent.fill` | `surface.panel` | 4.80 / 5.51 |
| `stroke.control` on a field in a panel | Field fill | 4.50 / 4.53 |
| `stroke.control` on a field in a panel | The panel around it | 3.52 / 3.99 |
| `stroke.control` on a field in a sheet or dialog | The overlay around it | 4.29 / 4.31 |
| `track.toggleOff` | Ground | 3.17 / 3.70 |
| `track.toggleOff` | `surface.panel` | 3.56 / 3.73 |
| `track.toggleOff` | `surface.inset` | 3.72 / 3.80 |
| `control.knob` | `track.toggleOff` (on panel) | 4.74 / 4.31 |
| `control.knob` | `accent.fill` (toggle on) | 6.39 |
| Progress indicator `accent.fill` | `track.progress` on panel | 3.65 / 4.16 |
| Status dots (success, warning, danger) | Ground | 3.90, 3.73, 3.51 at darkest |
| Status dots (success, warning, danger) | `surface.panel` | 5.41, 5.17, 4.87 at darkest |
| Live dot and visualiser bars `accent.fill` | Ground / panel | 3.46 / 4.80 at darkest |

Consequences:

- **Rule C-2: text fields and progress bars never sit directly on the ground.** A field outline on the darkest ground is 2.66:1 and a progress indicator against its track there is 2.68:1. Both live in a panel or denser surface.
- The segmented control's selected thumb is only 1.51:1 against its trough, so selection is also carried by the label: selected is `accent.fill` at semibold (5.97:1 on the thumb), unselected is `ink.primary` at regular weight (10.64:1 on the trough).
- Accent text against neighbouring ink text is 2.69:1, below the 3:1 needed for colour alone to mark a link. **Rule C-3: every accent-coloured text action also has a non-colour cue**: semibold weight plus an icon or chevron, or an underline.

**Table E. Labels on glass we do not fill.**

On iOS 26 the system draws Liquid Glass and its exact tint is not ours to compute. The bound used instead is the worst possible case: glass that adds nothing at all, so the label sits straight on the darkest ground.

| Label on native glass | Lower bound | Threshold |
|---|---|---|
| `ink.primary` text | 9.30 | 4.5 |
| `accent.strong` text (selected tab, tinted text button) | 4.95 | 4.5 |
| `accent.fill`, success, warning, danger as glyphs or dots only | 3.46, 3.90, 3.73, 3.51 | 3.0 |
| `accent.fill`, success, warning, danger as text | fail | not allowed |
| `ink.secondary` text | 4.83 | allowed over the ground, not over scrolling content |

For the iOS 18 to 25 fallback (`surface.floating`: white α 0.62 over a material):

| Backdrop under the chrome | `ink.primary` | `accent.strong` | `ink.secondary` | `accent.fill` |
|---|---|---|---|---|
| Darkest ground | 12.43 | 6.62 | 6.45 | 4.63 |
| Dense body text scrolling underneath, blurred (25% ink coverage) | 11.88 | 6.33 | 6.16 | 4.42 (fail) |
| Solid ink with no blur at all (cannot occur; shown as the floor) | 7.22 | 3.84 | 3.75 | n/a |

**Rule C-4: text on floating chrome is `ink.primary`, or `accent.strong` for the one selected or tinted item. Status colours appear there only as glyphs and dots.**

White text on an accent-tinted glass stays at or above 4.5:1 only while the rendered fill has a relative luminance of 0.183 or less; `accent.fill` with 19% of the brightest ground showing through is 4.57:1 and with 20% is 4.49:1. See the gate in 3.2.

### 2.10 Type

System fonts only. iPhone uses SF through Dynamic Type text styles, never fixed sizes. Windows uses Segoe UI Variable with fixed sizes in device-independent pixels, scaled by the user's Windows text size setting (section 6).

| Token | Role | iPhone text style (size/leading at the default size) | iPhone weight | Windows size / line height | Windows weight | Windows family |
|---|---|---|---|---|---|---|
| `type.display` | Brand title, hero figure | `.largeTitle` (34/41) | Bold | 30 / 36 | Bold | Display |
| `type.title` | Screen and dialog titles | `.title2` (22/28) | Bold | 22 / 28 | SemiBold | Display |
| `type.headline` | Card and row titles, large button labels | `.headline` (17/22) | Semibold | 15 / 20 | SemiBold | Text |
| `type.cue` | Cue text | `.title3` (20/25) | Regular | 18 / 26 | Regular | Text |
| `type.transcript` | Transcript text | `.body` (17/22) plus 3 pt line spacing | Regular | 15 / 22 | Regular | Text |
| `type.body` | Body, row labels, field text | `.body` (17/22) | Regular | 14 / 20 | Regular | Text |
| `type.label` | Regular button labels, emphasised inline labels | `.subheadline` (15/20) | Semibold | 14 / 20 | SemiBold | Text |
| `type.caption` | Supporting lines, chip labels (semibold in chips) | `.footnote` (13/18) | Regular | 13 / 18 | Regular | Text |
| `type.micro` | Speaker labels, kickers | `.caption` (12/16) | Semibold | 12 / 16 | SemiBold | Text |
| `type.mono` | Keys, addresses, codes | `.footnote` monospaced | Regular | 13 / 18 | Regular | Cascadia Mono, Consolas |

Rules:

- Minimum text size is 12 on both platforms. `.caption2` (11) is not used.
- Windows font family strings: Display is `Segoe UI Variable Display, Segoe UI Variable Text, Segoe UI`; Text is `Segoe UI Variable Text, Segoe UI`. Windows 10 falls back to Segoe UI.
- Windows weights are limited to Regular (400), SemiBold (600) and Bold (700).
- Windows line height is set explicitly with `LineStackingStrategy="BlockLineHeight"`.
- Do not set letter spacing. SF tracks itself per size, and WPF text has no tracking property; the Display optical family supplies the tighter fit at large sizes.
- Numbers that change in place (timers, countdowns, latency, percentages) use tabular figures: `.monospacedDigit()` on iPhone, `Typography.NumeralAlignment="Tabular"` on Windows.
- Sentence case everywhere. No all-caps labels.
- Reading width for transcript and cue text on Windows is capped at 720 px.

### 2.11 Spacing and layout

Base unit 4. Scale: `space.half` 2, `space.1` 4, `space.2` 8, `space.3` 12, `space.4` 16, `space.5` 20, `space.6` 24, `space.8` 32, `space.10` 40, `space.12` 48.

| Alias | iPhone | Windows | Meaning |
|---|---|---|---|
| `inset.screen` | 16 | 16 | Ground to nearest panel edge |
| `inset.panel` | 16 | 20 | Panel padding |
| `inset.card` | 16 | 16 | Card padding |
| `inset.row` | 16 horizontal | 16 horizontal | Row padding |
| `gap.panel` | 12 | 16 | Between sibling panels |
| `gap.group` | 12 | 12 | Between groups inside a panel |
| `gap.inline` | 8 | 8 | Icon to label |
| `gap.control` | 12 | 8 | Between adjacent buttons |
| `size.sidebar` | n/a | 232 | Sidebar width |
| `size.titleBar` | n/a | 48 | Custom title bar height |

### 2.12 Radii

| Token | Value | Used for |
|---|---|---|
| `radius.panel` | 30 | Panels, sidebar, dialogs |
| `radius.sheet` | 32 (top corners) | iPhone sheets on iOS 18 to 25. On iOS 26 the system sets it |
| `radius.card` | 22 | Cards, prominent cue card, QR tile, list groups inside a panel |
| `radius.field` | 16 | Text fields, banners, menus, row hover shape |
| `radius.tile` | 12 | 36-size icon tiles (10 for 32-size) |
| `radius.small` | 8 | Tooltips |
| `radius.pill` | height / 2 | Buttons, chips, toggles, segmented control |

- Corners are continuous (squircle) on iPhone: `RoundedRectangle(cornerRadius:style: .continuous)`.
- On Windows, `radius.pill` must be set to exactly half the control's height. A larger value makes WPF squash the ends into an ellipse.
- Nested shapes are concentric: inner radius = outer radius minus the gap between them, with a floor of 8. A row highlight inside a panel with 8 of padding is 22; inside 14 of padding it is 16.

### 2.13 Shadows

Canonical values, written as offset, blur and the alpha of `shade` that should be visible outside the surface:

| Token | Y offset | Blur | Alpha | Used for |
|---|---|---|---|---|
| `shadow.panel` | 12 | 32 | 0.14 | Panels |
| `shadow.raised` | 4 | 12 | 0.16 | Raised controls, prominent cards, chips that float on the ground |
| `shadow.floating` | 14 | 34 | 0.22 | Fallback floating chrome |
| `shadow.overlay` | 24 | 60 | 0.22 | Windows dialogs and menus. iPhone sheets take the system's shadow |
| `shadow.accent` | 6 | 16 | 0.28 | Accent-filled buttons |
| `shadow.knob` | 2 | 6 | 0.30 of `#0E1B33` | Toggle knob |

Both frameworks draw a shadow from the alpha of the layer that casts it, and both let that shadow show through a translucent fill. So:

- The shadow is cast by the surface's own fill layer and nothing else (the "caster", see 3.5). On iPhone the `.shadow` modifier goes on the fill shape inside `.background`, never on the container, or every child would cast one too.
- The effect's opacity is the canonical alpha divided by the fill alpha, so the visible result matches the table.
- The slight darkening under the fill is intended and is already in every figure of Table A.

| Token | Fill alpha | SwiftUI `.shadow(color:radius:y:)` | WPF `DropShadowEffect` |
|---|---|---|---|
| `shadow.panel` | 0.62 | `shade` α 0.23, radius 16, y 12 | Opacity 0.23, BlurRadius 32, ShadowDepth 12 |
| `shadow.raised` | 0.85 | `shade` α 0.19, radius 6, y 4 | Opacity 0.19, BlurRadius 12, ShadowDepth 4 |
| `shadow.floating` | material (opaque alpha) | `shade` α 0.22, radius 17, y 14 | not used |
| `shadow.overlay` | 0.97 | not used (system sheet shadow) | Opacity 0.23, BlurRadius 60, ShadowDepth 24 |
| `shadow.accent` | 1.0 | `shade` α 0.28, radius 8, y 6 | Opacity 0.28, BlurRadius 16, ShadowDepth 6 |
| `shadow.knob` | 1.0 | ink α 0.30, radius 3, y 2 | Opacity 0.30, BlurRadius 6, ShadowDepth 2 |

SwiftUI radius is half the canonical blur. WPF `Direction` is always 270 and `Color` is `shade` unless noted.

### 2.14 Blur

| Token | Canonical | iPhone, iOS 26 | iPhone, iOS 18 to 25 | Windows |
|---|---|---|---|---|
| `blur.floating` | 24 px, saturation 170% | System (Liquid Glass) | `.ultraThinMaterial` | Not used |
| `blur.overlay` | 30 px, saturation 170% | Not needed at α 0.97 | Not needed at α 0.97 | Not used |
| `blur.scrim` | 6 px | System dimming | System dimming | Not used |

The canonical numbers describe the reference look and exist for any future platform with a real backdrop filter. Neither app sets a blur radius by hand: iPhone uses system materials, and WPF has no backdrop blur. Section 3.5 explains why Windows does not need one.

### 2.15 Opacity

| Token | Value | Used for |
|---|---|---|
| `opacity.disabled` | 0.45 | Whole disabled control |
| `opacity.expired` | 0.12 | An expired QR code |
| `opacity.caretLow` | 0.30 | Low point of the live caret pulse |

Opacity is never used to make readable text quieter. Use `ink.secondary`.

### 2.16 Hit targets

| | iPhone | Windows |
|---|---|---|
| Minimum target | 44 x 44 pt | 40 x 40 px |
| Dense in-row buttons | 44 x 44 pt (visual may be smaller) | 36 x 36 px, at least 8 px apart |
| Primary actions | 52 pt high | 44 px high |
| Session control | 64 pt high | 48 px high |
| Navigation rows | 52 pt | 44 px |
| Minimum gap between targets | 8 pt | 8 px |

A control's visible shape may be smaller than its target; the target is extended with padding or `contentShape`.

### 2.17 Motion tokens

| Token | Duration | Curve | Used for |
|---|---|---|---|
| `motion.press` | 100 ms | `ease.out` | Press feedback |
| `motion.fast` | 160 ms | `ease.standard` | Hover, colour changes, toggles, interim to final text |
| `motion.base` | 240 ms | `ease.standard` | Segmented thumb, chip changes, row expand, dialog in, scroll to newest |
| `motion.emphasis` | 360 ms | `ease.emphasis` | Sheets, session control change, page change on iPhone |
| `motion.stream` | 120 ms | linear | Fade-in of a streamed text chunk |
| `motion.pulse` | 1600 ms loop | `ease.inOut` | Live dot and caret |
| `motion.spin` | 900 ms per turn | linear | Spinner |

| Curve | Definition | SwiftUI | WPF |
|---|---|---|---|
| `ease.standard` | cubic-bezier(0.2, 0, 0, 1) | `.timingCurve(0.2, 0, 0, 1, duration:)` | `KeySpline="0.2,0 0,1"` |
| `ease.out` | cubic-bezier(0, 0, 0.2, 1) | `.timingCurve(0, 0, 0.2, 1, duration:)` | `KeySpline="0,0 0.2,1"` |
| `ease.in` | cubic-bezier(0.4, 0, 1, 1) | `.timingCurve(0.4, 0, 1, 1, duration:)` | `KeySpline="0.4,0 1,1"` |
| `ease.inOut` | cubic-bezier(0.4, 0, 0.6, 1) | `.timingCurve(0.4, 0, 0.6, 1, duration:)` | `KeySpline="0.4,0 0.6,1"` |
| `ease.emphasis` | Critically damped spring, no overshoot | `.smooth(duration: 0.36)` | `ease.standard` at 360 ms |

### 2.18 Icons

- iPhone: SF Symbols, rendered at the weight of the adjacent text. Monochrome rendering only.
- Windows: line icons drawn as vector paths on a 24 px grid, 2 px stroke, round caps and joins, no fill, scaled as a whole so the stroke scales with the icon. This matches the sibling Windows apps and the roundness of SF Symbols. No icon font.
- Sizes: 16 (inside chips and compact buttons), 20 (buttons, rows), 24 (session control, empty-state tiles use 28).
- Icons take the colour of the text beside them.
- Four state glyphs are filled on both platforms because they must read at a glance: microphone, stop, pause, play.

| Meaning | SF Symbol | Windows icon name |
|---|---|---|
| Listen | `mic.fill` | `Mic` (filled) |
| Stop | `stop.fill` | `Stop` (filled) |
| Pause / resume | `pause.fill` / `play.fill` | `Pause` / `Play` (filled) |
| Cue | `sparkles` | `Sparkles` |
| Transcript | `text.quote` | `Quote` |
| Summary | `list.bullet.rectangle` | `Summary` |
| History | `clock` | `Clock` |
| Settings | `gearshape` | `Settings` |
| Phone | `iphone` | `Phone` |
| PC | `desktopcomputer` | `Monitor` |
| Pair / link | `link` | `Link` |
| QR | `qrcode` / `qrcode.viewfinder` | `Qr` |
| Copy | `doc.on.doc` | `Copy` |
| Retry / refresh | `arrow.clockwise` | `Refresh` |
| Reveal / hide | `eye` / `eye.slash` | `Eye` / `EyeOff` |
| Remove | `trash` | `Trash` |
| More | `ellipsis` | `More` |
| Close | `xmark` | `Close` |
| Done | `checkmark` | `Check` |
| Disclosure | `chevron.right` / `chevron.down` | `ChevronRight` / `ChevronDown` |
| Warning / error | `exclamationmark.triangle.fill` | `Alert` (filled) |
| Offline | `wifi.slash` | `Offline` |
| Secret | `key.fill` | `Key` |

### 2.19 Departures from the reference tokens

The approved Daylight reference is a web theme. These are the places LiveCue differs, and why.

| Reference | LiveCue | Reason |
|---|---|---|
| Panel glass `#FFFFFF` α 0.50 | `surface.panel` α 0.62 | LiveCue panels hold running text read at a glance. At 0.50, accent text is 4.36:1 and danger text 4.43:1 at the darkest point of the ground; at 0.62 they are 4.80:1 and 4.87:1. 0.62 is the value the reference already uses for its navigation glass |
| Success `#0F7A52` | `status.success` `#0B6443` | The reference green is 4.02:1 on a panel at the darkest point. `#0B6443` is 5.41:1 and is the green one of the sibling Windows apps already ships |
| One accent `#0B57D0` | Adds `accent.strong` `#0842A0` | Native Liquid Glass and bare ground are surfaces we cannot fill. `#0B57D0` is 3.46:1 there; `#0842A0` is 4.95:1 |
| Badges are a tint on the panel | Chips carry a `surface.raised` base under the tint | 4.19:1 becomes 5.11:1 for accent and danger text (Table C) |
| Field border is a white edge | `stroke.control` ink α 0.60 | A white edge on a white field is 1.15:1 against the panel. The outline is what identifies an empty field |
| Toggle track ink α 0.14 | `track.toggleOff` ink α 0.55 | 1.33:1 becomes 3.56:1 against the panel |
| Hover and press change opacity | Hover and press swap to lighter fills | Contrast can only go up in any state |
| Web font | SF and Segoe UI Variable | Native text rendering, Dynamic Type, no font to bundle |
| Gaps of 14 and 20 | 12 and 16 | Keeps everything on the 4 grid |
| Tints at α 0.10 to 0.16 | All tints α 0.12 | One rule; all figures in Table C use it |
| `backdrop-filter` blur on every panel | Blur only on iPhone floating chrome | Panels sit on a smooth gradient, where a blur changes nothing (3.5) |
| A dark theme exists | Not carried over | Section 5 |

Everything else (ground, blooms, ink, accent, warning, danger, radii, pill buttons, inverse toast, scrim) is taken from the reference unchanged.

---

## 3. Glass material recipes

### 3.1 The layer model

Both apps are built from the same four layers. What changes per platform is only how the third one is drawn.

| Layer | What lives here | How it is drawn |
|---|---|---|
| Ground | Base colour and blooms | Opaque, custom, identical on both platforms |
| Content | Panels, cards, transcript, cues, summaries, rows, fields, chips, the QR tile, buttons that sit inside panels | Our own translucent fills (`surface.panel`, `inset`, `raised`). Never system glass, never a backdrop blur |
| Chrome | Things that float above content: the iPhone tab bar, navigation bar, floating session controls; the Windows title bar buttons | iPhone: Liquid Glass on iOS 26, material recipe on iOS 18 to 25. Windows: the layered recipe in 3.5 |
| Overlay | Sheets, dialogs, menus, toasts | Near-opaque fills we control. iPhone alerts, menus and popovers are the system's |

Why the content layer is not glass: a panel on this ground has a known, computable colour, and the ground behind it is already a soft gradient. Real glass over it would add cost and uncertainty and change nothing visible.

### 3.2 iPhone on iOS 26: native Liquid Glass

Build with the iOS 26 SDK, deployment target iOS 18.0. Do not set `UIDesignRequiresCompatibility`. API names below are from the iOS 26 SDK; if a symbol differs in the installed SDK, keep the described behaviour and adjust the call.

**Where glass appears**

| Element | Implementation |
|---|---|
| Tab bar, navigation bar, toolbars | System `TabView` and `NavigationStack`, unmodified. No `toolbarBackground`, no custom bar backgrounds. `.tint(theme.accent.strong)` on the tab view, with `.tint(theme.accent.fill)` re-applied at the root of each tab's content so the stronger blue reaches only the bars |
| Alerts, confirmation dialogs, menus, popovers | System presentations, unmodified |
| Sheets | System `.sheet` for shape, grabber, detents and motion, with `.presentationBackground(theme.surface.overlay)`. Do not set `presentationCornerRadius`. Sheets hold forms, keys and codes, so their text needs a contrast we can state; a system glass sheet over arbitrary content does not give one. `#F9FBFF` at α 0.97 is also what the Daylight reference uses for its own sheets |
| Floating custom chrome (session controls over the live screen) | `.glassEffect(.regular.interactive(), in: .capsule)` for custom shapes; `.buttonStyle(.glass)` and `.buttonStyle(.glassProminent)` for buttons; every cluster wrapped in `GlassEffectContainer` |
| System controls inside content (`Toggle`, `Picker`, `Slider`, `Menu`) | System controls with `.tint(theme.accent.fill)`; they adopt the new look by themselves |

**Where glass must not appear**

- Anything in the content layer: panels, cards, transcript lines, cue and summary cards, rows, chips, the QR tile, text fields.
- Anything inside a sheet, popover or another glass element. A sheet is an overlay surface; everything inside it uses the content-layer recipes.
- Over the camera preview in the QR scanner. Use `surface.inverse` there.

**How to use it**

- **Variant.** Always `Glass.regular`. Never `.clear`; it is meant for media backgrounds and needs a dimming layer we do not have.
- **Interactivity.** `.interactive()` only on elements that respond to touch. Never on a static label or chip. It replaces our own pressed state: do not add a scale or opacity change on top.
- **Tint.** Tint means "the one primary action". The only tinted glass in the app is a `.glassProminent` button with `.tint(theme.accent.fill)`, and at most one is visible at a time. Do not tint for decoration. Do not put status colours on glass as tints; show status with a coloured glyph. Do not use `.glassEffect(.regular.tint(...))` under white text on custom shapes; how much of the background a tinted glass lets through is the system's choice, and white text needs the fill to stay dark.
- **Containers.** Two or more glass elements near each other go in one `GlassEffectContainer(spacing: 8)`. Lay the elements out 12 apart (`gap.control`), so they are separate at rest and merge only while morphing. Glass cannot sample other glass; the container is what makes a cluster render correctly.
- **Morphing.** State changes within a cluster use `glassEffectID(_:in:)` with a shared namespace inside `withAnimation(.smooth(duration: 0.36))`. If a particular control does not morph, a 240 ms cross-fade is the accepted result.
- **Shapes.** Pass the shape to the effect: `.capsule` for controls, `.rect(cornerRadius: 22)` for anything card-like, `.circle` for icon buttons. Buttons use `.buttonBorderShape(.capsule)` or `.circle`.
- **Nothing on top.** No custom stroke, shadow, blur, background or opacity on a glass view. None of the `stroke.*`, `shadow.*`, `glass.sheen` or `surface.floating` tokens apply on this path; the system draws all of it.
- **Labels.** Rule C-4: `ink.primary`, or `accent.strong` for the single selected or tinted item, or `ink.onAccent` on a prominent button. Status colours as glyphs only.
- **What scrolls underneath.** Content scrolls under the bars and under floating controls. Keep the system scroll edge effect (`.scrollEdgeEffectStyle(.soft, for: .bottom)` and `.top`). Place custom floating clusters with `safeAreaBar(edge: .bottom)` so the effect applies under them too.
- **Appearance.** The app forces light appearance (section 5), so glass always renders its light variant. Nothing dark ever scrolls under chrome.

**Gate for white text on prominent glass.** A UI test renders the prominent session control over the darkest ground and over a full transcript, samples the fill away from the label, and passes when the fill's relative luminance is 0.183 or lower (white text at 4.5:1 or better). If it fails on any supported iOS version, that control switches to the solid recipe from 3.3 on that version. This is the one place where native glass could miss AA, and the test decides it rather than an estimate.

### 3.3 iPhone on iOS 18 to 25: fallback

Same call sites, different drawing. One module owns the branch:

```swift
extension View {
    /// Chrome-layer glass. Native on iOS 26, built from a material and tokens before that.
    @ViewBuilder
    func daylightChrome<S: InsettableShape>(in shape: S, interactive: Bool = false) -> some View {
        if #available(iOS 26.0, *) {
            self.glassEffect(.regular.interactive(interactive), in: shape)
        } else {
            self.background { FallbackChrome(shape: shape) }
        }
    }
}
```

`FallbackChrome`, bottom to top:

1. `shape.fill(.ultraThinMaterial)` with `shadow.floating` (`shade` α 0.22, radius 17, y 14).
2. `shape.fill(theme.surface.floating)`: white α 0.62.
3. `shape.fill(glass.sheen)`: white α 0.28 at the top to α 0 at 40% of the height.
4. `shape.strokeBorder(stroke.edge, lineWidth: 1)`: white α 0.95 at the top to α 0.60 at the bottom.

Other fallback pieces:

| Native on iOS 26 | Fallback |
|---|---|
| `GlassEffectContainer` | A plain `HStack` or `ZStack` with `gap.control`. No merging |
| `glassEffectID` morph | `matchedGeometryEffect` on the shape plus a 240 ms cross-fade of the label |
| `.interactive()` | Pressed: scale 0.97 and fill to `surface.raised.pressed` over `motion.press` |
| `.buttonStyle(.glassProminent)` + tint | Solid capsule: `accent.fill`, `stroke.onAccent`, `shadow.accent`, label `ink.onAccent` |
| `.buttonStyle(.glass)` | `FallbackChrome` capsule, label `ink.primary` |
| System glass tab bar | System tab bar with `.toolbarBackground(.ultraThinMaterial, for: .tabBar)` and `.toolbarBackground(.visible, for: .tabBar)` |
| Sheet (system corner radius) | Same `.presentationBackground(theme.surface.overlay)`, plus `.presentationCornerRadius(32)` and a visible grabber |
| Scroll edge effect under custom chrome | `safeAreaInset(edge: .bottom)` and a mask on the scroll view that fades content to transparent over the last 32 pt above the chrome |

The content layer (panels, cards, rows, fields, chips, content buttons) is identical on both iOS paths; it never used glass.

### 3.4 Token mapping on iPhone, both paths

| Token or behaviour | iOS 26 | iOS 18 to 25 |
|---|---|---|
| `ground.*` | Custom `GroundView` behind every screen, ignoring safe areas; no system background colour visible anywhere (`.scrollContentBackground(.hidden)`) | Same |
| `surface.panel`, `surface.inset`, `surface.raised` | Shape fill plus `glass.sheen` (panels only), stroke and shadow tokens | Same |
| `surface.floating` | Replaced by `Glass.regular` | `.ultraThinMaterial` + white α 0.62 |
| `surface.overlay` | Token fill through `presentationBackground`; system corner radius | Token fill; `radius.sheet` |
| `surface.inverse`, `surface.scrim`, `surface.qr` | Token fill | Same |
| `stroke.edge`, `glass.sheen` on chrome | Not applied; system specular edge | Applied |
| `shadow.floating` | Not applied; system shadow | Applied |
| Content shadows (`shadow.panel`, `shadow.raised`, `shadow.accent`) | Applied | Same |
| `accent.fill` on a primary chrome button | `.tint(accent.fill)` on `.glassProminent` | Solid fill |
| `accent.strong` | Selected tab and tinted text on glass | Same |
| `radius.*` | Shape passed to `glassEffect(in:)` or `buttonBorderShape` | Same shapes |
| Pressed state on chrome | `.interactive()` | Scale 0.97 + `surface.raised.pressed` |
| `motion.emphasis` on chrome | Morph through `glassEffectID` | `matchedGeometryEffect` + cross-fade |
| `blur.*` | System | System materials |
| Reduce Transparency | System makes glass frostier; our content fills switch to solid twins | All fills switch to solid twins; material removed |
| Increase Contrast | System adds contrast to glass; our content uses the strong set | Strong set everywhere |

### 3.5 Windows: WPF on .NET 10

**Window backdrop**

The window is opaque and paints its own ground. No Mica, no Acrylic, no `AllowsTransparency`, and the Fluent `ThemeMode` stays off. Reasons: the ground is identical to the iPhone's, contrast does not depend on the user's wallpaper, the stock Windows 11 look was rejected, and an opaque window is what keeps ClearType working.

- Root grid, bottom to top: a `BloomLayer` (a `Grid` with `ground.base` as its background and four `Rectangle`s, each filled with a frozen `RadialGradientBrush` in absolute mapping mode, rebuilt in `ArrangeOverride` from the geometry in 2.2; `IsHitTestVisible="False"`), then the layout.
- `WindowChrome`: `CaptionHeight="48"`, `ResizeBorderThickness="6"`, `GlassFrameThickness="1"`, `CornerRadius="0"`, `UseAeroCaptionButtons="False"`. A glass frame thickness of 1 keeps the system window shadow and resize behaviour.
- On `SourceInitialized`, call `DwmSetWindowAttribute` and ignore failures (Windows 10): attribute 33 (corner preference) = 2 (round); attribute 20 (immersive dark mode) = 0.
- When maximised, add a margin to the root equal to `SystemParameters.WindowResizeBorderThickness` plus 4 on each side; a chrome-less maximised window otherwise overhangs the work area.
- Per-monitor DPI awareness (PerMonitorV2) is declared in the application manifest.

**Layered-brush glass**

Every glass surface is two sibling `Border`s in one `Grid`. This is the construction the sibling Windows apps already use.

```xml
<ControlTemplate TargetType="ContentControl">
  <Grid>
    <!-- Caster: the fill, and the only element with an Effect -->
    <Border CornerRadius="30"
            Background="{DynamicResource SurfacePanelBrush}"
            Effect="{StaticResource ShadowPanel}"/>
    <!-- Face: sheen, edge and content. No Effect, so text stays crisp -->
    <Border CornerRadius="30"
            Background="{DynamicResource GlassSheenBrush}"
            BorderBrush="{DynamicResource StrokeEdgeBrush}"
            BorderThickness="1"
            Padding="{TemplateBinding Padding}">
      <ContentPresenter/>
    </Border>
  </Grid>
</ControlTemplate>
```

| Part | Layer | Value |
|---|---|---|
| Fill | Caster `Background` | The surface token as a `SolidColorBrush` |
| Shadow | Caster `Effect` | The shadow token from 2.13 as a frozen, shared `DropShadowEffect`, `RenderingBias="Quality"` |
| Inner glow | Face `Background` | `glass.sheen`: `LinearGradientBrush` (0,0) to (0,1), `#47FFFFFF` at offset 0, `#00FFFFFF` at offset 0.4 |
| Top highlight stroke | Face `BorderBrush` | `stroke.edge`: `LinearGradientBrush` (0,0) to (0,1), `#F2FFFFFF` to `#99FFFFFF`, thickness 1 |
| Content | Face child | Text and controls |

Per surface:

| Surface | Fill | Stroke | Sheen | Shadow | Radius |
|---|---|---|---|---|---|
| Panel, sidebar | `#9EFFFFFF` | `stroke.edge` | yes | `shadow.panel` | 30 |
| Inset card | `#75FFFFFF` | `#B3FFFFFF` | no | none | 22 |
| Raised control, prominent card | `#D9FFFFFF` | `#E6FFFFFF` | no | `shadow.raised` | pill or 22 |
| Accent button | `#0B57D0` | `#4DFFFFFF` | no | `shadow.accent` | pill |
| Dialog, menu | `#F7F9FBFF` | `stroke.edge` | yes (dialog only) | `shadow.overlay` | 30 / 16 |
| Toast, tooltip | `#E60E1B33` | none | no | `shadow.raised` | 22 / 8 |

There is no backdrop blur and none is needed: every translucent surface sits on the ground or on another of our fills, and the ground is a smooth gradient whose blurred image is itself. That only holds if nothing translucent ever sits over detailed content, hence:

- **Rule W-1: on Windows nothing translucent floats over scrolling content.** Action bars are docked rows inside the panel, below the scroller. The only things that cover content are dialogs (over a scrim, α 0.97), menus (α 0.97) and toasts (inverse, α 0.90).
- **Rule W-2: panels never overlap panels.** A panel's children use `surface.inset` or `surface.raised`, which are plain fills.

**Custom title bar and window controls**

- The title bar is the top 48 px of the window and is part of the ground; it has no fill of its own. The whole strip is the drag region.
- Left, 18 px in: the brand mark at 20 px, then "LiveCue" in `type.label` at 13 px, `ink.primary`.
- Right, 8 px from the top and right edges: a glass capsule (`surface.panel` fill, `stroke.edge`, `shadow.raised`, padding 3) holding three buttons, each 44 x 34 with corner radius 17: minimise, maximise or restore, close.
- Glyphs are 10 px line icons, 1.2 px stroke, `ink.primary`.
- Hover: `state.captionHover` behind the glyph. Close hover: `#C42B1C` fill with a white glyph (5.66:1). Pressed: opacity 0.7.
- The buttons set `WindowChrome.IsHitTestVisibleInChrome="True"`, have tooltips and automation names ("Minimize", "Maximize" or "Restore", "Close"), and are not tab stops, like system caption buttons. Alt+Space and Alt+F4 keep working because the window keeps its system menu.
- Nothing else is placed in the title bar strip. The sidebar and pages start below it.
- The Snap Layouts flyout on hover over maximise is not implemented in this version; Win+Z, dragging to the screen edge and the keyboard shortcuts still work.
- When the window is inactive, the wordmark and glyphs switch to `ink.secondary`.

**Keeping text crisp**

1. `UseLayoutRounding="True"` and `SnapsToDevicePixels="True"` on the window root.
2. `TextOptions.TextFormattingMode="Display"` on the root. Override to `Ideal` on `type.display`, `type.title` and any text inside an animated transform.
3. `TextOptions.TextRenderingMode="Auto"`. The window is opaque, so ClearType applies.
4. Text must never have an ancestor with an `Effect`, an `OpacityMask`, a `BitmapCache`, or (at rest) an `Opacity` below 1. Each of these renders the subtree to an intermediate surface and drops ClearType to greyscale. This is why the shadow lives on the caster and not on the panel.
5. Transforms on text are identity at rest. Entrance animations end at exactly 0 offset and scale 1, then clear the transform.
6. The disabled state is the one exception to rule 4: `Opacity` 0.45 on the control root.
7. Icon paths set `SnapsToDevicePixels="False"` and scale with a `LayoutTransform`, so strokes keep an even weight.
8. All brushes and effects are frozen. Effects are shared through `StaticResource`; brushes are referenced through `DynamicResource` so the high-contrast dictionary can replace them.
9. Any secondary borderless window that needs transparency (for example a tray flyout) uses `surface.overlay` and sets `TextOptions.TextRenderingMode="Grayscale"` explicitly, since ClearType is unavailable there.

---

## 4. Component library

Conventions for this section:

- "Chrome placement" means floating above content on iPhone (3.1). "Content placement" means inside a panel, card, sheet or dialog. Windows has only content placement.
- Sizes are iPhone points / Windows pixels.
- "Focused" always means the focus ring from 6.6 unless stated.
- "Disabled" always means `opacity.disabled` on the whole control, no shadow, no hover, not focusable.
- Every interactive element on iPhone is a `Button` (or a system control), never a view with a tap gesture.

### 4.1 Primary pill button

The one accent-filled action on a screen.

Anatomy: capsule fill, optional leading icon, label, optional trailing icon.

| Size | iPhone | Windows |
|---|---|---|
| Large | Height 52, padding 24, `type.headline`, icon 20 | Height 44, padding 22, `type.headline`, icon 20 |
| Regular | Height 44, padding 20, `type.label`, icon 20 | Height 40, padding 20, `type.label`, icon 16 |
| Compact (inside rows only) | n/a | Height 32, padding 14, 13 px SemiBold, icon 16 |

| State | Appearance |
|---|---|
| Rest | `accent.fill`, `stroke.onAccent`, `shadow.accent`, label and icon `ink.onAccent` |
| Hover (Windows) | Fill `accent.hover`, `motion.fast` |
| Pressed | Fill `accent.pressed`; iPhone also scales to 0.97; `motion.press` |
| Focused | Focus ring |
| Disabled | Standard |
| Busy | Leading icon replaced by a 16 spinner in `ink.onAccent`; label stays (or changes to a progressive verb such as "Connecting…"); width locked to the rest width; ignores input; full opacity |

| Platform | Mapping |
|---|---|
| iPhone, chrome, iOS 26 | `Button` + `.buttonStyle(.glassProminent)` + `.tint(theme.accent.fill)` + `.controlSize(.large)` + `.buttonBorderShape(.capsule)`. Pressed state comes from the system |
| iPhone, chrome, iOS 18 to 25, and all content placement | Custom `ButtonStyle` drawing the table above |
| Windows | `Button` style `Button.Primary`: caster with `accent.fill` and `shadow.accent`, face with stroke and content. `IsDefault="True"` in dialogs |

A destructive confirmation uses the same component with `status.danger` in place of `accent.fill` (hover and pressed from 2.7). It appears only in dialogs.

### 4.2 Secondary glass button

Supporting actions. Several may appear together.

Anatomy and sizes: as 4.1.

| State | Appearance |
|---|---|
| Rest | `surface.raised`, `stroke.raised`, `shadow.raised`, label and icon `ink.primary` |
| Hover (Windows) | Fill `surface.raised.hover` |
| Pressed | Fill `surface.raised.pressed`; iPhone scales to 0.97 |
| Focused | Focus ring |
| Disabled | Standard |
| Busy | As 4.1 with the spinner in `ink.primary` |

Variants:

- **Destructive**: label and icon `status.danger`, stroke `stroke.danger`. Always the custom recipe, also in chrome, so the red text sits on a fill we control (5.79:1).
- **Quiet**: no fill, stroke or shadow at rest. Label `accent.fill` in semibold with an icon or chevron (rule C-3), or `ink.primary`. Hover shows `state.hover`, pressed `state.pressed`, in the button's capsule shape. Safe to repeat in rows because it casts no shadow. Only on `surface.panel` or denser.

| Platform | Mapping |
|---|---|
| iPhone, chrome, iOS 26 | `.buttonStyle(.glass)` + `.buttonBorderShape(.capsule)`, label `ink.primary` |
| iPhone, chrome, iOS 18 to 25 | `daylightChrome(in: Capsule(), interactive: true)` |
| iPhone, content | Custom `ButtonStyle` drawing the table above |
| Windows | `Button` style `Button.Secondary`; `Button.Quiet`; `Button.Destructive` |

### 4.3 Icon button

Anatomy: circular target, one glyph. Always has an accessibility label; on Windows also a tooltip.

| | iPhone | Windows |
|---|---|---|
| Standard | 44 x 44, glyph 20 | 40 x 40, glyph 20 |
| In rows | 44 x 44 target, 36 visual | 36 x 36, glyph 16 |

Variants: **raised** (states as 4.2), **quiet** (states as the quiet variant in 4.2, circular), **chrome** (iPhone only: `.buttonStyle(.glass)` + `.buttonBorderShape(.circle)` on iOS 26, `daylightChrome(in: Circle(), interactive: true)` before).

Busy: the glyph is replaced by a 16 spinner.

### 4.4 Segmented control

Two to four mutually exclusive options that change what is shown.

| Platform | Mapping |
|---|---|
| iPhone | System `Picker` with `.pickerStyle(.segmented)` on both iOS paths. Not restyled |
| Windows | Custom: `RadioButton`s in a `UniformGrid`, over one thumb element that is moved with a `TranslateTransform` |

Windows anatomy: trough (capsule, `track.segmented`, height 44, padding 4), equal-width segments (minimum 72 wide), thumb (capsule, height 36, radius 18, `surface.raised`, `stroke.raised`, `shadow.raised`).

| State | Appearance (Windows) |
|---|---|
| Unselected | Label `type.body` (regular), `ink.primary` |
| Hover (unselected) | `state.hover` in the segment's capsule |
| Selected | Thumb under the segment; label `type.label` (semibold), `accent.fill` |
| Pressed | `state.pressed` |
| Focused | Focus ring around the segment |
| Disabled | Standard, whole control |

The thumb slides between segments over `motion.base`. Segments have equal width so the weight change never shifts layout. Arrow keys move the selection; the group is one tab stop.

### 4.5 Toggle

| Platform | Mapping |
|---|---|
| iPhone | System `Toggle` with `.tint(theme.accent.fill)`. Not restyled |
| Windows | Custom `CheckBox` template; the whole row is the target |

Windows anatomy: track 52 x 30 (radius 15), knob 24 circle with `shadow.knob`, 3 px inset, travel 22. The row is at least 44 high: label on the left, track on the right.

| State | Appearance (Windows) |
|---|---|
| Off | Track `track.toggleOff`, knob left |
| On | Track `accent.fill`, knob right |
| Hover | Knob grows to 26 |
| Pressed | Knob stretches to 28 wide toward the other side |
| Focused | Focus ring around the track |
| Disabled | Standard |
| Busy (setting being applied) | Knob shows a 12 spinner in `ink.secondary`; ignores input |

The knob moves with a `TranslateTransform` over `motion.fast`; the track colour cross-fades over the same time.

### 4.6 Text field

Anatomy: label above (`type.caption` semibold, `ink.secondary`, 6 gap), field (radius 16, fill `surface.raised`, outline `stroke.control`), text (`type.body`, `ink.primary`), placeholder (`ink.secondary`), optional leading icon, optional trailing icon button, helper or error line below (`type.caption`, 6 gap).

| | iPhone | Windows |
|---|---|---|
| Single line | Height 48, padding 16 | Height 44, padding 14 |
| Multi-line | Minimum 96, padding 16 / 12 | Minimum 96, padding 14 / 10 |

| State | Appearance |
|---|---|
| Rest | As anatomy |
| Hover (Windows) | Fill `surface.raised.hover` |
| Focused | Outline becomes 2 wide in `accent.fill`; the content inset shrinks by 1 so nothing moves. No separate focus ring |
| Filled | As rest |
| Error | Outline 2 wide in `status.danger`; error line in `status.danger` with a leading alert glyph |
| Disabled | Standard |
| Read-only | Fill `surface.inset`, outline `stroke.hairline`, text selectable |
| Busy (validating) | Trailing 16 spinner |

Rules: the label is always visible; a placeholder is an example, never the label. Fields sit in a panel, card, sheet or dialog (rule C-2).

| Platform | Mapping |
|---|---|
| iPhone | `TextField` with `.textFieldStyle(.plain)` inside the drawn container; `axis: .vertical` for multi-line |
| Windows | `TextBox` template: one `Border` with the fill and outline around `PART_ContentHost`; caret `ink.primary`; selection `accent.fill` at α 0.30 |

### 4.7 Secure field

A text field (4.6) for keys, tokens and passwords.

- Adds a trailing reveal icon button (`eye` / `eye.slash`), 44 / 36 target, inset 4 from the edge.
- Hidden: system bullets. Revealed: `type.mono` so similar characters can be told apart.
- Revealing does not move focus or the caret.
- Reverts to hidden when the field loses focus or the app goes to the background.
- The value never appears in a tooltip, an accessibility value or a log. The accessibility value is "Hidden" or "Shown".

| Platform | Mapping |
|---|---|
| iPhone | `SecureField` and `TextField` swapped in place, sharing one `FocusState`; `.privacySensitive()`; no autocapitalisation or autocorrection |
| Windows | `PasswordBox` with the 4.6 template; swapped for a `TextBox` while revealed |

### 4.8 List row

Anatomy: optional leading icon tile, title, optional subtitle, optional trailing accessory (value text, chevron, toggle, chip or button). Rows are stacked inside a panel and separated by `stroke.hairline`, inset to the title's leading edge. No separator after the last row.

| | iPhone | Windows |
|---|---|---|
| One line | Minimum height 52 | Minimum height 48 |
| Two lines | Minimum height 64 | Minimum height 60 |
| Icon tile | 36, radius 12, glyph 20 | 32, radius 10, glyph 16 |
| Padding / gap | 16 horizontal / 12 | 16 horizontal / 12 |

Tile: `accent.tint` fill with an `accent.fill` glyph, or ink α 0.08 fill with an `ink.secondary` glyph for neutral rows.

Text: title `type.body` in `ink.primary`; subtitle `type.caption` in `ink.secondary`; trailing value `type.body` in `ink.secondary`.

| State | Appearance |
|---|---|
| Rest | No fill |
| Hover (Windows) | `state.hover`, shape concentric with the panel. In a dialog or menu: `state.hoverOnOverlay` |
| Pressed | `state.pressed`. In a sheet, dialog or menu: `state.pressedOnOverlay` |
| Selected (Windows navigation) | `surface.raised` + `stroke.raised` + `shadow.raised`; title `type.label` in `accent.fill`; icon `accent.fill` |
| Focused | Focus ring inside the row bounds |
| Disabled | Standard |
| Busy | Trailing accessory replaced by a 16 spinner |

Only rows that do something get hover and pressed states. A row that navigates shows a trailing chevron.

| Platform | Mapping |
|---|---|
| iPhone | A `Button` or `NavigationLink` with the row as its label, inside a panel in a `ScrollView`. Use `List` only where swipe actions are needed, with `.listRowBackground(Color.clear)` and the same row drawing |
| Windows | `ListBoxItem` or `RadioButton` template |

### 4.9 Card and panel

| Variant | Fill | Stroke | Shadow | Radius | Padding (iPhone / Windows) | Where |
|---|---|---|---|---|---|---|
| Panel | `surface.panel` + `glass.sheen` | `stroke.edge` | `shadow.panel` | 30 | 16 / 20 | On the ground |
| Card | `surface.inset` | `stroke.inset` | none | 22 | 16 / 16 | Inside a panel |
| Prominent card | `surface.raised` | `stroke.raised` | `shadow.raised` | 22 | 16 / 16 | On the ground or inside a panel. At most one per screen |

Optional header: title in `type.headline`, optional subtitle in `type.caption` `ink.secondary`, optional trailing quiet button or icon button; 12 below the header.

Panels and cards are not interactive by themselves. A card that acts as one button takes the row states from 4.8. Section titles that sit on the ground between panels use `type.label` in `ink.primary`, 8 above the panel.

| Platform | Mapping |
|---|---|
| iPhone | `Panel { }` and `Card { }` container views drawing a `RoundedRectangle(style: .continuous)`. Same on both iOS paths |
| Windows | `ContentControl` styles `Glass.Panel`, `Glass.Card`, `Glass.CardProminent` (3.5) |

### 4.10 Sheet and dialog

For a focused task (pairing, entering a key) or a decision.

| | iPhone | Windows |
|---|---|---|
| Container | System `.sheet` with detents | In-window modal over `surface.scrim` |
| Fill | `surface.overlay` through `presentationBackground`, on every iOS version (3.2) | `surface.overlay` + `glass.sheen`, `stroke.edge`, `shadow.overlay` |
| Radius | iOS 26: system. Before: 32 top | 30 |
| Width | Full | 480; 640 for the pairing dialog; never more than the window width minus 48 |
| Padding | 20 | 24 |
| Header | Navigation title, trailing close button | `type.title`, trailing quiet close icon button |
| Actions | Bottom, full-width, primary on top, 12 apart | Bottom right, primary rightmost, 8 apart |

Short confirmations on iPhone use the system `.alert` or `.confirmationDialog`, unstyled.

States: opening and closing (7.1); busy (the primary button is busy, other controls disabled, closing disabled only if closing would lose work).

Behaviour: focus moves into the sheet or dialog and is trapped there. Escape closes on Windows; Enter triggers the primary action. A click on the scrim closes a dialog only if it has no unsaved input. Initial focus goes to the first field, or to the least destructive button when the dialog confirms something destructive. Content inside uses the content-layer recipes: cards are `surface.inset`, buttons are 4.1 and 4.2 in content placement.

### 4.11 Toast and banner

**Toast**: a short confirmation that needs no decision ("Copied", "Paired").

- Capsule-like tile, radius 22, minimum height 48, padding 16 / 12.
- Fill `surface.inverse`, `shadow.raised`. Icon 20 and text `type.label`, both `ink.onInverse`. One optional action in `ink.onInverse`, semibold and underlined.
- iPhone: top of the screen below the navigation bar, width up to the screen minus 32. Windows: bottom centre of the content area, 24 from the bottom, width up to 480.
- Shown for 4 seconds, or 8 with an action. Pauses while hovered or focused. One at a time; a new toast replaces the current one.
- Custom on both iOS paths. It is not glass.
- Announced politely to assistive technology.
- Never used for errors.

**Banner**: a persistent message inside the content.

- Radius 16, padding 12 / 16, base `surface.raised` with the status tint over it (Table C).
- Leading 20 icon in the status colour, title in `type.label` in the status colour, body in `type.body` `ink.primary`, optional trailing quiet button.
- Variants: info (accent), success, warning, danger.
- Stays until the condition clears or it is dismissed.
- Danger and warning banners are announced assertively once when they appear.

### 4.12 Status chip

A compact, non-interactive state label.

Anatomy: capsule, base `surface.raised` plus the status tint, a leading 8 dot or 14 glyph, label in `type.caption` semibold in the status colour. Height 28, padding 10, gap 6. Add `shadow.raised` only when the chip sits directly on the ground.

| Variant | Tint and text | Leading mark |
|---|---|---|
| Live | `status.live` | Filled dot, pulsing (7.1) |
| Success | `status.success` | Filled dot |
| Warning | `status.warning` | Filled dot, or a pause glyph |
| Danger | `status.danger` | Alert glyph |
| Neutral | Ink α 0.08 tint, `ink.secondary` text | Hollow ring |

States: static. A change of variant cross-fades over `motion.base`. Busy: the mark is replaced by a 12 spinner in the text colour.

The label always carries the meaning in words. On iPhone the height grows with Dynamic Type. Chips are content: on iPhone they are never drawn as glass, also when shown near chrome.

### 4.13 Progress

| Variant | Anatomy | iPhone | Windows |
|---|---|---|---|
| Determinate bar | Track 6 high, radius 3, `track.progress`; indicator `accent.fill`; optional trailing percentage in `type.caption`, tabular | `ProgressView(value:)` with a custom `ProgressViewStyle` | `ProgressBar` template |
| Determinate ring | 28 across, 3 stroke, track `track.progress`, indicator `accent.fill`, starts at 12 o'clock, round caps | Custom `Shape` with `trim` | `Path` with an `ArcSegment` |
| Indeterminate spinner | 20 across (16 in buttons, 12 in chips), 2.5 stroke, 270-degree arc in the current text colour, one turn per `motion.spin` | `ProgressView()` with `.tint` | Custom rotating arc |
| Indeterminate bar | Same track; a 30%-wide indicator travels left to right every 1400 ms | Custom | `ProgressBar IsIndeterminate` template |

States: running, complete (the indicator fills, then the bar fades out after 400 ms or switches to `status.success` if it stays visible), paused (indicator `ink.secondary`), failed (indicator `status.danger` and a text message beside it).

Bars and rings sit on a panel or denser (rule C-2). Value changes animate over `motion.base`. Every indicator has a text label or an accessibility label saying what is in progress.

### 4.14 Empty state

Anatomy, centred, maximum width 340, padding 28: icon tile 64 (radius 22, `surface.raised` base with `accent.tint`, glyph 28 in `accent.fill`), 16 gap, title in `type.title`, 6 gap, body in `type.body` `ink.secondary` centred, optionally 16 gap and one button (4.1 regular, or 4.2).

It sits inside the panel it explains, or directly on the ground when the whole screen is empty (title and body are ink, so rule C-1 holds). No illustration, no animation. On iPhone, build it from these parts rather than `ContentUnavailableView`, so the type and tile match Windows.

### 4.15 QR code frame

**Display (Windows pairing)**

- Tile: `surface.qr` (opaque white), radius 22, `stroke.raised`, `shadow.raised`. Never glass, never translucent, never tinted.
- Code: 240 x 240 inside 12 of padding (tile 264 x 264). Never smaller than 200 x 200.
- Modules `#0E1B33` on white (17.17:1). The encoder's own quiet zone of 4 modules is kept inside the 240.
- Each module is a whole number of device pixels and at least 3. Render the bitmap at device resolution with `RenderOptions.BitmapScalingMode="NearestNeighbor"`.
- Nothing is drawn over the code: no logo, no rounded modules, no gradient.
- Below the tile, 12 gap: one line in `type.caption` `ink.secondary` with the time left in tabular figures.

| State | Appearance |
|---|---|
| Empty | Tile shows a 28 QR glyph in `ink.secondary` and one line of `type.caption` |
| Generating | Tile shows a 20 spinner |
| Active | Code visible; countdown below |
| Expiring (last 30 s) | Countdown becomes a warning chip |
| Expired | Code at `opacity.expired` (no longer scannable) with a regular primary button "New code" centred over it |
| Hidden | Same as empty, with a secondary button "Show code" |

Automation name: "Pairing QR code", plus the state. The pairing data is never exposed as text to assistive technology.

**Scanner (iPhone)**

- Full-screen camera preview in a sheet. No glass over the preview.
- Viewfinder: a centred 260 x 260 square, radius 30, marked by four corner brackets (white, 4 wide, 36 long, round caps). Outside the square the preview is dimmed with `#0E1B33` α 0.45.
- Hint: a capsule in `surface.inverse` with `type.label` `ink.onInverse`, 24 above the bottom safe area.
- On a successful read: success haptic, the brackets turn `status.success` for 400 ms, then the sheet closes.
- Camera denied: an empty state (4.14) with a button that opens Settings.

### 4.16 Device row

A list row (4.8, two-line) for one paired device.

Anatomy: icon tile with the phone glyph; title in `type.headline` (the device name); subtitle in `type.caption` `ink.secondary` (for example the last-seen time); trailing status chip; trailing "more" icon button opening a menu with Rename and Remove access.

| State | Chip | Notes |
|---|---|---|
| Online | Success, "Online" | |
| Offline | Neutral, "Offline" | Subtitle says when it was last seen |
| This device (on iPhone) | Adds a neutral "This iPhone" chip | No remove action |
| Removing | Busy row: the "more" button becomes a spinner | |
| Removed | The row fades out over `motion.base` | A toast confirms |

Remove access always asks for confirmation in a dialog with a destructive primary button. On Windows the same actions are in the row's context menu and the row is reachable by keyboard (Enter opens the menu). The accessibility label reads name, status and last seen as one sentence; the actions are exposed as custom actions.

### 4.17 Session control (the big listen and stop control)

The largest control in the app. One control, two main faces.

Anatomy: capsule, leading 24 glyph, label in `type.headline`, optional trailing visualiser (4.21, size M).

| | iPhone | Windows |
|---|---|---|
| Height | 64 | 48 |
| Width | Fills the control cluster; minimum 200, maximum 420 | Minimum 180, hugs content |
| Padding | 28 | 24 |

| State | Face | Glyph | Label | Notes |
|---|---|---|---|---|
| Idle | Accent (as 4.1 rest) | `mic.fill`, `ink.onAccent` | "Start listening" | The accent-filled control on the screen |
| Starting | Accent | 20 spinner | "Starting…" | Ignores input; same width |
| Listening | Neutral (as 4.2 rest) | `stop.fill` in `status.danger` | "Stop" in `ink.primary` | Trailing visualiser in `status.live`. No accent fill, which leaves the accent free for the cue action |
| Paused | Neutral | `stop.fill` in `status.danger` | "Stop" | Trailing visualiser at rest in `status.warning` |
| Stopping | Neutral | 20 spinner | "Stopping…" | Ignores input |
| Unavailable | Accent, disabled | `mic.fill` | "Start listening" | A line of `type.caption` nearby says why (not paired, no microphone permission) |
| Hover (Windows) | Per face, as 4.1 or 4.2 | | | |
| Pressed | Per face | | | iPhone on iOS 26: system interactive glass |
| Focused | Focus ring | | | |

| Platform | Mapping |
|---|---|
| iPhone, iOS 26 | Chrome placement. Idle faces: `.buttonStyle(.glassProminent)` + `.tint(accent.fill)`. Listening faces: `.buttonStyle(.glass)`. Both in the cluster's `GlassEffectContainer` with one `glassEffectID`, changed inside `withAnimation(.smooth(duration: 0.36))`. The glyph changes with `.contentTransition(.symbolEffect(.replace))`. Subject to the gate in 3.2 |
| iPhone, iOS 18 to 25 | Solid accent capsule (idle) and `daylightChrome` capsule (listening), matched with `matchedGeometryEffect` |
| Windows | Content placement, 4.1 and 4.2 recipes at 48 high. Used for the relay's start and stop if the functional spec keeps a manual control |

Rules:

- The stop glyph is the only red in the listening state.
- The label always says what pressing will do, never the current state. The current state is shown by the status chip.
- Haptics: 7.2.
- Accessibility label equals the visible label; the value is the state ("Listening", "Paused"); the hint is "Double tap to stop listening".

### 4.18 Live transcript line

Lives on `surface.panel`. Never on glass.

Anatomy: optional speaker label; text block; optional trailing timestamp.

| Part | Style |
|---|---|
| Speaker label | `type.micro`, `ink.secondary`; the owner's own label uses `accent.fill`. 4 above the text. Shown when the speaker changes, not on every line |
| Final text | `type.transcript`, `ink.primary` |
| Interim text | `type.transcript`, `ink.secondary`. Same font, size, weight and line spacing as final, so nothing reflows when it becomes final |
| Live caret | After the last interim character: 2 wide, cap height, `accent.fill`, pulsing between opacity 1 and `opacity.caretLow` on `motion.pulse` |
| Timestamp | `type.caption`, tabular, `ink.secondary`, trailing. Hidden at accessibility text sizes |

Spacing: 12 between turns, 4 between consecutive lines of one speaker.

| State | Appearance |
|---|---|
| Interim | As above, with caret |
| Finalising | Colour cross-fades from secondary to primary over `motion.fast`. No movement. Caret removed |
| Final | As above. Text is selectable |
| Waiting (nothing heard yet) | One line of `type.transcript` in `ink.secondary` ("Listening…") with the caret |
| Paused | Caret static in `status.warning` |

Behaviour:

- New text keeps the view pinned to the newest line (scroll over `motion.base`) only while the reader is already at the bottom.
- If the reader has scrolled up, nothing moves; a raised chip-shaped button "Jump to live" appears at the bottom of the panel.
- Each final line is one accessibility element reading "Speaker: text".
- Interim text is not exposed to assistive technology; the container is marked as updating frequently.

### 4.19 Cue card (assist card with streaming text)

The hero of the live screen.

Anatomy: the newest cue is a prominent card (4.9); older cues are plain cards and only appear inside a panel. Padding 16, gaps 12.

| Part | Style |
|---|---|
| Header | 28 circular icon tile (`accent.tint`, `sparkles` at 16 in `accent.fill`); "Cue" in `type.micro`, `accent.fill`; trailing state text in `type.caption` `ink.secondary` |
| Prompt (optional) | What the cue answers: `type.label`, `ink.secondary`, at most 2 lines |
| Body | `type.cue`, `ink.primary`, selectable |
| Footer | Left: latency or time in `type.caption`, tabular, `ink.secondary`. Right: quiet icon buttons Copy and Retry |

| State | Appearance |
|---|---|
| Waiting | Header state "Thinking…" with a 16 spinner. Body area reserves two lines of height. No skeleton, no shimmer |
| Streaming | Each chunk fades in over `motion.stream`. The live caret (4.18) follows the last character. The card grows downward over `motion.fast`; its top edge never moves. Header state "Writing…" |
| Complete | Caret removed; footer appears; header state shows the elapsed time |
| Error | Body replaced by a danger banner (4.11) with the message and a Retry quiet button. Partial text, if any, stays above it in `ink.secondary` |
| Older | Plain card; body in `type.body`; footer only Copy |
| Hover, focus (Windows) | The card itself has none; its buttons do |

Behaviour:

- When a cue starts, the list scrolls so the card's top is visible, once. It does not follow the stream.
- One soft haptic at the first chunk (7.2).
- While streaming the card is marked as updating frequently and is not read chunk by chunk. On completion the full cue is announced once.
- Copy and Retry are custom accessibility actions on the card.

### 4.20 Summary card

A plain card inside a panel, or a panel of its own on a summary screen.

Anatomy:

- Header: icon tile with the summary glyph; title "Summary" in `type.headline`; a line of `type.caption` `ink.secondary` (duration and date, tabular).
- Sections: each a `type.micro` kicker in `ink.secondary` ("Key points", "Actions") followed by items in `type.body` `ink.primary`.
- Items: a 4 dot in `ink.secondary` in a 16 hanging indent; 6 between items; 12 between sections.
- Footer: quiet buttons Copy, Share (iPhone) and Regenerate.

| State | Appearance |
|---|---|
| Generating | Header plus one line "Summarising…" with a 16 spinner, or a determinate bar when progress is known. No skeleton |
| Streaming | As the cue card: chunks fade in, caret at the end |
| Ready | As anatomy |
| Empty | One line in `type.body` `ink.secondary` ("Not enough conversation to summarise yet") |
| Error | Danger banner with Retry |
| Collapsed (history lists) | Header and the first two lines, then a quiet "Show more" |

### 4.21 Audio level and voice visualiser

Five vertical capsule bars driven by the measured microphone level. It answers one question: "can it hear me?"

| Size | Bar width | Gap | Maximum height | Box | Used in |
|---|---|---|---|---|---|
| S | 3 | 2 | 22 | 23 x 22 | Status chip, compact rows |
| M | 4 | 3 | 28 | 32 x 28 | Session control |
| L | 6 | 5 | 56 | 50 x 56 | Idle hero on the live screen |

Behaviour:

- Bar height = minimum + (maximum − minimum) × level × weight, with weights 0.45, 0.75, 1.00, 0.70, 0.40 from left to right. Minimum height equals the bar width, so a silent bar is a dot.
- Level is the microphone RMS mapped to 0 to 1, smoothed with a 60 ms attack and a 240 ms release. Redraw at no more than 24 frames per second.
- Bars are vertically centred and fully rounded.

| State | Appearance |
|---|---|
| Listening | `status.live`, moving with the level |
| Silence while listening | `status.live`, all bars at minimum |
| Idle | `ink.secondary`, minimum, static |
| Paused | `status.warning`, minimum, static |
| Unavailable | `ink.disabled`, minimum, static |

It is not a waveform and has no idle animation. Under Reduce Motion it still shows level, because that is information, but updates 4 times per second with a `motion.base` linear ease. Hidden from assistive technology; the chip and the session control carry the state in words. Contrast as a graphic: 4.80:1 on a panel, at least 3.46:1 on glass.

| Platform | Mapping |
|---|---|
| iPhone | `TimelineView(.animation(minimumInterval: 1/24, paused: !listening))` drawing five `Capsule`s |
| Windows | Five `Border`s with heights set from a 24 Hz `DispatcherTimer` (or `CompositionTarget.Rendering`, throttled). Heights snap to whole pixels |

### 4.22 Connection status indicator

Shows the link between the iPhone and the PC relay. Two forms: **inline** (an 8 mark and a `type.label` label, for rows and the sidebar) and **chip** (4.12).

| State | Mark | Colour | Label | Motion |
|---|---|---|---|---|
| Connected | Filled circle | `status.success` | "Connected" | None |
| Connecting | 12 spinner | `status.live` | "Connecting…" | Spin |
| Reconnecting | 12 spinner | `status.warning` | "Reconnecting…" | Spin |
| Offline | Hollow ring, 1.5 stroke | `status.offline` | "Offline" | None |
| Not paired | Hollow ring, 1.5 stroke | `status.offline` | "Not paired" | None |
| Needs attention | Alert glyph, 14 | `status.danger` | A short reason | None |

Each state has its own mark shape as well as its own colour, and always a text label. A change of state cross-fades over `motion.base` and is announced: politely for connected and connecting, assertively for reconnecting during a session and for needs attention. Inline labels on the ground or on glass use `ink.primary`; only the mark is coloured (rules C-1 and C-4).

### 4.23 Supporting elements (Windows)

| Element | Spec |
|---|---|
| Tooltip | `surface.inverse`, radius 8, padding 8 / 6, `type.caption` `ink.onInverse`, 500 ms delay |
| Context menu | `surface.overlay`, `stroke.edge`, `shadow.overlay`, radius 16, padding 6; items 36 high, radius 10, `type.body`; hover `state.hoverOnOverlay`, pressed `state.pressedOnOverlay`; destructive items in `status.danger`; separators `stroke.hairline` |
| Scrollbar | 12 wide gutter, no track fill, no arrow buttons; thumb 6 wide, radius 3, `control.scrollThumb`; hover α 0.40 |
| Divider | `stroke.hairline`, 1 px |
| Count badge | Capsule, minimum 20 x 20, `accent.fill`, 12 px SemiBold `ink.onAccent` (6.39:1) |
| Tray icon | The small brand glyph (8.2) with a 6 px status dot at the bottom right: `status.success` running, `status.warning` attention, none when stopped |

---

## 5. Theme policy

- **Daylight is the only theme.** There is no dark theme, no "follow system" option and no appearance setting in either app.
- **Both apps force light appearance.**
  - iPhone: `UIUserInterfaceStyle` = `Light` in Info.plist and `.preferredColorScheme(.light)` at the root. This also keeps system controls, sheets, the keyboard and Liquid Glass in their light variants.
  - Windows: the app ignores the system app mode, sets DWM attribute 20 to 0, and never loads a dark dictionary.
- **Tokens are structured for a second theme, which is not designed.**
  - Components read semantic tokens only (2.1). No component contains a hex value, an alpha or a font size.
  - iPhone: a `Theme` value type holding every token in section 2, provided through the SwiftUI environment. `Theme.daylight` is the only instance.
  - Windows: one `ResourceDictionary` (`Daylight.xaml`) with every colour and brush, merged at application level and referenced through `DynamicResource`.
  - Adding a theme later means adding one `Theme` instance and one dictionary with the same keys, then re-running the contrast tables in 2.9 for it.
- **Accessibility variants are not themes.** Solid twins, the strong colour set and Windows high contrast are derived from, or override, the active theme (section 6). They use the same mechanism: `theme.resolved(for:)` on iPhone, a second merged dictionary on Windows.

---

## 6. Accessibility

Target: WCAG 2.2 AA on both platforms.

### 6.1 Text size

**iPhone**

- Every text style in 2.10 scales with Dynamic Type through the largest accessibility size. Fixed dimensions that hold text (heights, icon tiles, chip heights) scale with `@ScaledMetric`, or are minimums.
- Content has no size cap.
- Labels inside floating chrome cap at `.accessibility2` and support the Large Content Viewer (`.accessibilityShowsLargeContentViewer()`), like system bars.
- From `.accessibility1` up: rows stack title above value; button groups stack vertically at full width; chips wrap below titles instead of sitting beside them; transcript timestamps are hidden; the session control keeps its height and its label may wrap to two lines.
- Bold Text is honoured by using text styles and semantic weights, never font names.
- Everything scrolls vertically at large sizes; nothing is truncated to fit a fixed screen.

**Windows**

- Read the system text scale (registry `HKCU\Software\Microsoft\Accessibility`, value `TextScaleFactor`, 100 to 225) at start and when it changes, and multiply every `type.*` size and line height by it.
- Layouts hold without clipping to 150%. Above that, rows wrap as on iPhone and pages scroll.
- Control heights are minimums and grow with their text.

### 6.2 Screen readers (VoiceOver and Narrator)

- Every interactive element has a name. A visible label is the name. Icon-only buttons get an explicit name that says the action ("Copy cue"), not the icon ("Two squares").
- Names do not include the control type ("Stop", not "Stop button").
- State goes in the value or state ("On", "Listening", "Expired"), not in the name.
- Decorative elements are hidden: the ground, sheen, dividers, icon tiles beside a text label, the visualiser, the live caret.
- Group what is read together: a row is one element; a cue card is one element with custom actions; a chip's mark and label are one element.
- Reading order follows visual order: status, then transcript, then cues, then controls.
- Live regions:

| Change | Announcement |
|---|---|
| Toast | Polite |
| Connection connected or connecting | Polite |
| Reconnecting during a session, needs attention, error banner | Assertive, once |
| Cue complete | The full cue text, once |
| Transcript text | Not announced; the container is marked as updating frequently and can be read on demand |
| Countdown | Not announced each second; the expiring and expired states are |

- Secrets are never in a name, value or hint (4.7).
- iPhone: `.accessibilityLabel`, `.accessibilityValue`, `.accessibilityHint`, `.accessibilityAddTraits`, `.accessibilityAction(named:)`, `AccessibilityNotification.Announcement`.
- Windows: `AutomationProperties.Name`, `.HelpText`, `.LiveSetting`, `.ItemStatus`; custom controls supply an `AutomationPeer`.
- Stable identifiers for UI tests are set separately (`accessibilityIdentifier`, `AutomationProperties.AutomationId`) and never double as labels.

### 6.3 Reduce Transparency

Triggers: iPhone `accessibilityReduceTransparency`; Windows "Transparency effects" off (registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, value `EnableTransparency` = 0).

What the glass becomes:

| Element | Becomes |
|---|---|
| `surface.panel` | `#F6F8FB`, opaque |
| `surface.inset` | `#FAFBFD` |
| `surface.raised` | `#FFFFFF` |
| `surface.floating` (fallback chrome) | `#F6F8FB`, material removed, plus a 1 `stroke.control` outline |
| `surface.overlay` | `#F8FBFF` |
| `surface.inverse` | `#0E1B33` |
| Chip and banner bases | The tint composited over white: accent `#E2EBF9`, success `#E2ECE8`, warning `#EFEAE1`, danger `#F6E5E8`, neutral `#ECEDEF` |
| `glass.sheen` | Removed |
| Native Liquid Glass (iOS 26) | Left to the system, which makes it frostier |
| Ground, blooms, strokes, shadows | Unchanged |

Text contrast on the solid twins is equal to or better than Table A (for example `accent.fill` 6.00:1 on the solid panel).

### 6.4 Increase Contrast

Trigger: iPhone `colorSchemeContrast == .increased`. (Windows uses 6.5.)

- Everything in 6.3 applies.
- The ground becomes flat `ground.base`; the blooms are not drawn.
- Coloured text and fills switch to the strong set: `accent.strong`, `status.success` strong, `status.warning` strong, `status.danger` strong. `ink.secondary` stays.
- `stroke.edge`, `stroke.raised` and `stroke.inset` are replaced by `stroke.control` (ink α 0.60), so every surface has a visible outline: 4.46:1 against the solid panel, 4.00:1 against the flat ground.
- `stroke.hairline` becomes ink α 0.30.
- Focus ring uses `accent.strong`.

Every text pairing is then at least 7:1:

| Text | Flat ground | Solid panel | Solid raised (white) |
|---|---|---|---|
| `ink.primary` | 14.45 | 16.14 | 17.17 |
| `ink.secondary` | 7.50 | 8.37 | 8.91 |
| `accent.strong` | 7.70 | 8.59 | 9.14 |
| success strong | 7.57 | 8.45 | 8.99 |
| warning strong | 8.20 | 9.16 | 9.74 |
| danger strong | 7.59 | 8.47 | 9.01 |
| White on `accent.strong` / danger strong | 9.14 / 9.01 | | |
| Strong text on its own solid chip base | accent 7.60, success 7.46, warning 8.14, danger 7.41 | | |

### 6.5 Windows high contrast (contrast themes)

Trigger: `SystemParameters.HighContrast`, re-checked on `SystemParameters.StaticPropertyChanged`. The app swaps in a `HighContrast.xaml` dictionary with the same keys. No Daylight colour survives.

| Token group | System colour |
|---|---|
| Ground and all surfaces | `SystemColors.Window` |
| `ink.primary`, `ink.secondary` | `SystemColors.WindowText` |
| `ink.disabled` | `SystemColors.GrayText` (no opacity change) |
| `accent.fill`, toggle on, progress, selection, focus ring | `SystemColors.Highlight` |
| `ink.onAccent` | `SystemColors.HighlightText` |
| Secondary button fill / label | `SystemColors.Control` / `SystemColors.ControlText` |
| Accent text actions | `SystemColors.HotTrack`, underlined |
| All strokes | 1 px `SystemColors.WindowText` on every surface, button and field |
| Status colours | `SystemColors.WindowText`; the mark shape and the label carry the meaning |

Also: blooms, sheen, shadows and tints are removed; all fills are opaque; hover and pressed states invert to `Highlight` and `HighlightText`; the QR tile stays black on white whatever the theme; the brand mark is drawn in `WindowText`.

### 6.6 Focus visuals

- **Windows.** Keyboard focus shows a 2 px ring in `stroke.focus`, 2 px outside the control, following its shape (radius = control radius + 3). It is drawn through `FocusVisualStyle`, so it appears for keyboard focus only, never on mouse click. The 2 px gap keeps the ring visible around accent-filled buttons. Ring contrast is at least 3.46:1 on any Daylight surface.
- Text fields show focus through their own outline instead (4.6).
- Tab order follows reading order. Arrow keys move within groups (segmented control, navigation, menus). Every action is reachable by keyboard. Escape closes the topmost dialog or menu. Focus returns to the invoking control when a dialog closes.
- **iPhone.** Keep the system focus appearance for hardware keyboards and Full Keyboard Access. Do not hide it. Because every interactive element is a `Button` or a system control, focus and Switch Control work without extra code.

### 6.7 Reduce Motion

Triggers: iPhone `accessibilityReduceMotion`; Windows `SystemParameters.ClientAreaAnimation == false`.

| Normally | Becomes |
|---|---|
| Slides, scales, morphs, the segmented thumb travel | 160 ms cross-fade, or an instant change |
| Live dot and caret pulse | Static, full opacity |
| Streamed text fade | Chunks appear at once |
| Scroll to newest | Jump, no animation |
| Visualiser | Updates 4 times per second (4.21) |
| Spinner | Keeps spinning; it is the only sign of work. The indeterminate bar becomes a static full-width bar pulsing in opacity between 0.4 and 1 every 1200 ms |
| Sheet and navigation transitions (iPhone) | Left to the system |

### 6.8 Minimum sizes and colour independence

- Hit targets: 2.16.
- Text: 12 minimum.
- Strokes that carry meaning: at least 1 wide and 3:1.
- Colour is never the only signal. Status always has a label and a distinct mark shape; errors have an icon and text; selection has a weight change or a shape; accent text actions have an icon or underline. On iPhone, when Differentiate Without Colour is on, status dots are replaced by their glyph equivalents (check, pause, alert, ring).
- Nothing flashes. The only loop is the 1.6 s pulse.

---

## 7. Motion and haptics

### 7.1 What animates and how

| Event | Animation |
|---|---|
| Press | Fill change over `motion.press`; iPhone custom controls also scale to 0.97 and back |
| Hover (Windows) | Fill change over `motion.fast` |
| Toggle, checkbox | Knob and colour over `motion.fast` |
| Segmented selection | Thumb slides over `motion.base` |
| Status chip or connection change | Cross-fade over `motion.base` |
| Interim to final text | Colour cross-fade over `motion.fast`; no movement |
| New transcript line | Appears in place with a fade over `motion.fast`; the view scrolls to it over `motion.base` if pinned to the bottom |
| Streaming cue or summary text | Each chunk fades in over `motion.stream`; the card grows downward over `motion.fast` |
| New cue card | Fades in and rises 8 over `motion.base` |
| Session control change of face | iOS 26: glass morph inside `.smooth(duration: 0.36)`. Fallback: matched geometry plus cross-fade. Windows: fill and label cross-fade over `motion.base` |
| Live dot, live caret | Opacity 1 to 0.30 and back, `motion.pulse`, only while listening |
| Page change (Windows) | Outgoing content fades out over 100 ms; incoming fades in and rises 8 over `motion.base`. The sidebar and title bar do not move |
| Navigation and sheets (iPhone) | System transitions |
| Dialog (Windows) | Scrim fades over `motion.fast`; the dialog fades and scales from 0.96 over `motion.base` with `ease.out`; closing is 120 ms with `ease.in` |
| Toast | Fades and moves 12 from its edge over `motion.base`; leaves over `motion.fast` |
| Progress value | `motion.base` |
| Row removed | Fade over `motion.base`, then the list closes the gap over `motion.base` |

Rules:

- Nothing animates on appearance of a screen beyond its transition. No staggered entrances.
- Nothing loops except the live pulse, spinners and the indeterminate bar.
- No bounce or overshoot anywhere.
- An animation never delays input: controls respond at the start of a transition.
- The ground never moves.
- On Windows, animate `Opacity` and `TranslateTransform` only, on the smallest subtree that needs it, and finish at exactly 1 and 0 (3.5).

### 7.2 Haptics (iPhone)

Haptics confirm things the user cannot look at. They must be quiet enough to go unnoticed by the other person in the conversation.

| Event | Feedback | SwiftUI |
|---|---|---|
| Listening actually started (microphone open) | One medium impact | `.sensoryFeedback(.impact(weight: .medium), trigger:)` |
| Listening stopped | One rigid impact, intensity 0.7 | `.sensoryFeedback(.impact(flexibility: .rigid, intensity: 0.7), trigger:)` |
| Paused, resumed | One light impact | `.impact(weight: .light)` |
| Cue arrives (first chunk) | One soft impact, intensity 0.6, once per cue | `.impact(flexibility: .soft, intensity: 0.6)` |
| Cue failed, session ended by an error | Error notification | `.error` |
| Connection lost during a session, microphone interrupted | Warning notification | `.warning` |
| Paired, QR code read | Success notification | `.success` |
| Segmented or picker change | Selection | `.selection` |
| Buttons, toggles, navigation | Nothing beyond what the system control does | |

Rules:

- Start and stop haptics fire when the state actually changes, not on the tap. A failed start gives the error haptic instead.
- Nothing fires per streamed chunk, per transcript line or on a loop.
- The audio session must allow haptics while recording (`setAllowHapticsAndSystemSoundsDuringRecording(true)`), otherwise the system mutes them during a session.
- One setting, "Haptics", on by default, turns all of the above off. The system-wide haptics setting is honoured automatically.
- No sounds.
- Windows has no haptics and plays no sounds.

---

## 8. Brand

### 8.1 Name

- The name is **LiveCue**: one word, capital L and capital C, no space or hyphen.
- Set in the platform's display face (SF on iPhone, Segoe UI Variable Display on Windows), Bold.
- At 20 and larger it is two-tone: "Live" in `ink.primary`, "Cue" in `accent.fill`. At that size it counts as large text, and accent on the darkest ground is 3.46:1 against the 3:1 threshold.
- Below 20 (the Windows title bar, list rows) it is one colour, `ink.primary`.
- Lockup: mark on the left, name on the right, gap 0.4 × the mark's height; the mark is 1.3 × the name's font size tall.
- Never outlined, never with a shadow, never on a photo or the camera preview.

### 8.2 The mark

A speech bubble holding three voice bars: someone is speaking, and LiveCue has something to say back. A blue glass bubble on a pale Daylight tile.

**Master artwork, 1024 x 1024 canvas, origin top left**

| Layer | Geometry | Paint |
|---|---|---|
| Tile | Full canvas | Linear gradient from (128, 32) to (896, 992): `#FFFFFF` at 0, `#E8F0FC` at 0.5, `#C9DCF7` at 1 |
| Tile bloom | Full canvas | Radial gradient centred (832, 896), radius 544: `#8DB9FF` α 0.60 to α 0 |
| Tile sheen | Ellipse centred (512, 32), radii 704 x 320, clipped to the canvas | Linear gradient, top to y = 416: `#FFFFFF` α 0.70 to α 0 |
| Bubble shadow | The bubble shape | Y offset 40, blur 80, `#0B57D0` α 0.30 |
| Bubble | Rounded rectangle x 232, y 222, width 560, height 460, corner radius 150, united with the tail path `M 330 640 L 296 800 C 292 818 306 826 320 816 L 508 680 Z` | Linear gradient, top to bottom of the shape: `#3D82F0` at 0, `#0B57D0` at 0.6, `#0A47AE` at 1 |
| Bubble gloss | Ellipse centred (432, 290), radii 250 x 130, rotated −12 degrees, clipped to the bubble | Linear gradient, top to bottom: `#FFFFFF` α 0.35 to α 0 |
| Bubble rim | The bubble outline, stroked 6 inside | Linear gradient, top to bottom: `#FFFFFF` α 0.55 to α 0.10 |
| Bars | Three capsules 68 wide, radius 34, centred on y = 452 at x = 392, 512 and 632, with heights 150, 270 and 200 | `#FFFFFF` |

The bars are deliberately uneven (short, tall, medium) so they read as a voice and not as a pause symbol or a signal meter. White bars on the lightest part of the bubble are 3.72:1; the bubble against the tile is 5.57:1 at its middle.

**iPhone app icon**

- Export the master as a 1024 x 1024 opaque sRGB PNG: full-bleed tile, no rounded corners, no outer rim. The system applies the mask and, on iOS 26, its own glass edge.
- The glyph (bubble and bars) stays inside the central 560 x 600, within the system's safe area.
- The three groups (tile, bubble, bars) are kept as separate layers in the generator, so a layered icon can be produced later without redrawing.

**Windows icon (.ico)**

One file with these frames:

| Frames | Drawing |
|---|---|
| 256, 128 | The master, masked to a rounded square with corner radius 23.5% of the size. Add an outer 1 px stroke `#284682` α 0.16 and, inside it, a 1.1 px rim stroked with a vertical gradient `#FFFFFF` α 1.0, α 0.35 at 55%, α 0.70 |
| 64, 48, 40, 32 | Same tile and mask, flat: tile `#EAF1FB` with a 1 px border `#284682` α 0.20; bubble solid `#0B57D0`; bars `#FFFFFF`. No bloom, sheen, gloss, rim or shadow |
| 24, 20, 16 | Glyph only, no tile, transparent background: bubble solid `#1A66DE`, bars `#FFFFFF`, drawn on the pixel grid |

`#1A66DE` for the smallest frames is 3.10:1 on a dark taskbar and 4.73:1 on a light one; the darker `#0B57D0` would be 2.55:1 on a dark taskbar. White bars on it are 5.25:1.

The 16 px frame, pixel coordinates with the origin at the top left:

- Bubble: rounded rectangle from (1, 1) to (15, 12), corner radius 3.5.
- Tail: triangle (3, 12), (2, 15), (7, 12).
- Bars, 2 wide with square ends: x 4 to 6 from y 5 to 8; x 7 to 9 from y 3 to 10; x 10 to 12 from y 4 to 9.

The 20 and 24 px frames scale that drawing by 1.25 and 1.5 and re-snap every edge to whole pixels, keeping the bars 2 and 3 px wide.

**In-app mark**

- Below 64: the bubble and bars only, flat `accent.fill` with white bars, no tile.
- 64 and above (first-run and empty screens): the bubble with its gradient, gloss and rim, no tile, with `shadow.accent`.
- Under Windows high contrast: bubble `WindowText`, bars `Window`.

Both icons are generated by a script from these numbers, not drawn by hand, so they can be regenerated.

---

## 9. Do and don't

Each failure is one that glass interfaces are known for, with the rule here that prevents it.

| Glass goes wrong when | Rule | Where |
|---|---|---|
| Text contrast is judged by eye and fails over the dark part of the background | Every pairing is computed at the darkest point of the ground. A new pairing is not allowed until it has a number | 2.9 |
| Coloured text sits on thin glass or bare background | Coloured text only on `surface.panel` or denser. On glass and ground: ink, or `accent.strong` | C-1, C-4 |
| Glass sits on glass and both turn to mud | Content is never glass. Nothing glass goes inside a sheet or another glass element. Clusters share one `GlassEffectContainer`. On Windows panels never overlap | 3.1, 3.2, W-2 |
| A busy background shows through behind text | The ground is a smooth gradient and nothing else. Reading surfaces are fills we control. Content fades out before it passes under floating chrome. Nothing dark scrolls under chrome. The camera preview gets an opaque inverse hint, not glass | 3.2, 3.3, 4.15 |
| Heavy blur covers large areas and costs frames and battery | No blur on the content layer at all. Blur exists only under small floating chrome on iPhone, and it is the system's. Windows has no blur | 2.14, 3.5 |
| Everything is translucent, so nothing has hierarchy | Five fixed elevations, each with one fill. The newest cue is the only prominent card on a screen | 2.3, 4.9 |
| Tinted glass everywhere dilutes the accent | One tinted glass element at a time, and only for the primary action | 3.2 |
| White text on tinted glass looks fine in one screenshot and fails on another background | Prominent glass is measured by a test; if it fails, the control falls back to a solid fill | 3.2 gate |
| Thin, low-contrast edges make inputs invisible | Fields and off-toggles use `stroke.control` or `track.toggleOff`, both at least 3:1 | Table D |
| Hover and pressed states darken a surface and push text below AA | On panels and cards, hover and pressed only lighten. On near-white overlays they darken by a computed amount that keeps every text colour above 4.5:1 | 2.3 |
| Shadows and effects make text soft on Windows | The shadow lives on a sibling caster layer. No effect, mask, cache or resting opacity above any text | 3.5 |
| Extra strokes and shadows are stacked on native glass until it looks fake | Nothing is drawn on top of a `glassEffect` | 3.2 |
| Status is shown by colour alone | Every status has a label and its own mark shape | 4.22, 6.8 |
| Glass is animated constantly to show off | Nothing moves unless something happened. The ground never moves | 7.1 |
| A translucent toolbar floats over a scrolling list on a platform without blur | On Windows, bars are docked rows, not overlays | W-1 |
| The QR code is styled to match the theme and stops scanning | The QR tile is opaque white with ink modules and nothing on top | 4.15 |
| Accessibility settings are ignored because "the glass is the design" | Reduce Transparency, Increase Contrast, high contrast and Reduce Motion each have a defined result | 6.3 to 6.7 |

Also:

- Do use the system control when there is one (iPhone toggles, pickers, sheets, alerts).
- Do keep text the same size and position across states.
- Do give every new component a row in the contrast tables before it ships.
- Don't add a colour, a radius, a shadow or a font size that is not a token.
- Don't use red for anything that is not stopping, deleting or failing.
- Don't put text in the accent on the accent tint without the raised base.
- Don't use all-caps, italics for state, or opacity to dim readable text.

---

## 10. Layout direction

Screens are designed in the next step. These are the constraints that step starts from.

**iPhone**

- One column of panels on the ground, 16 from the screen edges, scrolling under the bars.
- The system tab bar (three tabs at most) is the only persistent navigation. During a live session it is hidden, and a floating cluster of glass controls at the bottom takes its place: the session control, and the cue action if the functional spec keeps a manual one.
- The live screen, top to bottom: connection and session status (chips), the transcript panel, the cue cards, the control cluster. The newest cue gets the most room.
- Settings, pairing and history are panels of rows. Focused tasks (pairing, entering a key, scanning) are sheets.
- Portrait only at this stage. The layout must hold at the smallest supported width and at the largest accessibility text size by scrolling.

**Windows**

- One window: the custom title bar across the top (brand on the left, window buttons on the right), a 232 px glass sidebar on the left (navigation rows, a connection status card at the bottom), and a content area with a page title on the ground and panels below it.
- The same shell as the sibling Windows apps, so the family is recognisable.
- Pages follow the functional spec; expect status, pairing, devices and settings. The pairing page pairs the QR tile with its instructions side by side, and stacks them when the window is narrow.
- Default size 1120 x 760, minimum 900 x 600. Below 1000 px wide, two-column pages become one column. Content panels stop growing at 920 px wide and stay left-aligned.
- No floating toolbars (rule W-1). Dialogs are in-window.

---

## 11. Verification checklist

A build of either app conforms to this document when all of these hold.

1. **Reference composites.** With the app's ground forced to flat `ground.base`, a sampled pixel inside each surface matches within 2 per channel: panel `#EBEEF4`, inset card on panel `#F4F6F9`, raised control on ground `#F7F8FA`, raised on panel `#FCFDFD`, dialog `#F6F8FC`. Sample below the sheen, in the lower half of the surface. These values include the surface's own shadow showing through its fill (2.13).
2. **Darkest-point contrast.** A screenshot with a panel placed over the top-left bloom is sampled behind `ink.secondary`, `accent.fill` and `status.danger` text; each measured ratio is at least 4.5:1.
3. **Prominent glass gate** (iPhone, iOS 26): section 3.2.
4. **No stray values.** A search of the UI code finds no colour literal, alpha, font size or corner radius outside the theme definition.
5. **Glass placement** (iPhone): `glassEffect` and the glass button styles appear only in chrome-placement code, never inside a sheet or a scroll view's content.
6. **Crisp text** (Windows): no element with an `Effect`, `OpacityMask` or `BitmapCache` has a text descendant.
7. **Accessibility passes.** Each of these is exercised and screenshotted: largest Dynamic Type size; Windows text scale 150% and 225%; Reduce Transparency; Increase Contrast; Reduce Motion; Windows high contrast (one light and one dark contrast theme); keyboard-only operation on Windows; VoiceOver and Narrator reading the live screen and the pairing flow.
8. **Hit targets.** No interactive element is smaller than the minimums in 2.16.
9. **Icons.** The 16 px Windows frame is legible on both a light and a dark taskbar; the iPhone icon has no baked corners or rim.
