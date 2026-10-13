# LiveCue for iPhone: screens

Every screen and sheet of the iPhone app in the remake. It builds on the [design system](01-design-system.md) (referred to as DS, with section numbers) and on the functional spec of the old app (`docs/spec/01-ios-app.md`, referred to as "spec"). Static mockups of each screen are in [mockups/ios.html](mockups/ios.html); frame ids are given with each screen.

Conventions:

- Text in "double quotes" is exact copy. Text marked **(new)** does not exist in the old app; text marked **(reworded)** replaces an old string with the same meaning.
- `code` after an element is its accessibility identifier. All identifiers from spec 1.13 are kept on the equivalent element; section 15 lists them all.
- Sizes are points. Component names (panel, card, row, chip, banner, disclosure) are the DS components of the same name.
- The Home Screen name of the app is "LiveCue Remake". Inside the app the brand is "LiveCue".

Contents: [1 Navigation](#1-navigation) · [2 Rules for every screen](#2-rules-for-every-screen) · [3 Home](#3-home) · [4 Live session](#4-live-session) · [5 Assistant Lab](#5-assistant-lab) · [6 Settings](#6-settings) · [7 Transcription](#7-transcription) · [8 PC speech model](#8-pc-speech-model) · [9 Meta API key](#9-meta-api-key) · [10 Pairing and the QR sheet](#10-pairing-and-the-qr-sheet) · [11 Model Library](#11-model-library) · [12 Speech benchmark](#12-speech-benchmark) · [13 History and Session detail](#13-history-and-session-detail) · [14 Messages](#14-messages) · [15 Identifier index](#15-identifier-index) · [16 What changed from the old app](#16-what-changed-from-the-old-app)

---

## 1. Navigation

```
Root
├─ No active session → tab bar: Live · History · Settings
│  ├─ Live tab: Home
│  │   ├─ row "Transcription"                → Transcription
│  │   ├─ row "PC connection" / "Pair Windows PC" → Pairing ──(sheet)→ Scan PC QR
│  │   ├─ row "Assistant models & timing"    → Assistant Lab
│  │   └─ "Download a transcription model"   → Model Library   (on-device provider, nothing loaded)
│  ├─ History tab: History → Session detail
│  └─ Settings tab: Settings
│        ├─ Provider                 → Transcription
│        │     ├─ Meta API key       (Meta)
│        │     ├─ PC speech model    (Nemotron, Qwen3)
│        │     ├─ Model Library      (Voz, Parakeet)
│        │     └─ Speech benchmark   (Voz, Parakeet)
│        ├─ the same provider rows, directly
│        ├─ Assistant models & timing → Assistant Lab
│        │     ├─ OpenRouter models   (OpenRouter)
│        │     ├─ Reasoning           (Codex)
│        │     └─ Run details         (per comparison row)
│        └─ PC connection            → Pairing
└─ Active session → Live session (full screen, no tab bar)
      └─ sliders button → Assistant Lab and its sub-screens
Over everything: the "LiveCue" alert
```

Why this shape:

- **Three tabs, as before.** Live is where a conversation starts, History is the record, Settings is everything else. A fourth tab would not earn its place.
- **One route into Settings.** The old Home row "Transcription & appearance" pushed the whole Settings screen into the Live tab, so the same screen lived in two stacks. That row now opens **Transcription**, the one sub-screen it was really for. Settings itself is reached only from its tab.
- **Transcription is a screen, not a picker.** The old picker closed as soon as a provider was tapped, and what that provider needed (a key, a PC model, a download) was back on Settings. The provider list and the provider's own setup rows are now on one screen, so choosing and finishing setup happen in one place.
- **PC speech model has its own screen.** It was a section in the middle of Settings that polled the PC every 2 seconds. On its own screen the polling has a clear start and end, and Home can link straight to it.
- **Answer service is chosen in place.** With two options, a row that opened a two-row list was a wasted step. The two services are choice rows at the top of Assistant Lab.
- **The session screen replaces the tabs**, as before. Starting discards pushed screens; ending returns to Home.

No onboarding wizard, login or splash content. The only modal surfaces are the QR sheet, the delete confirmation and the "LiveCue" alert.

---

## 2. Rules for every screen

**Structure**

- Every screen draws the Daylight ground behind everything (DS 2.2). Content is one column of panels, 16 from the screen edges, 12 apart (DS 2.11).
- Tab roots (History, Settings) use a system large title. Pushed screens use an inline title and the system back button. Home and Live session hide the navigation bar.
- Tab bar, navigation bars and the floating controls are chrome (DS 3.1): native Liquid Glass on iOS 26, the fallback recipe before. Everything else is content.
- Content scrolls under the chrome and fades out before it reaches floating controls (DS 3.2, 3.3).
- Appearance is forced light (DS 5).

**The "LiveCue" alert**

System alert, title "LiveCue", message = the current global error, one button "OK". A newer error replaces an older one. It is used for errors that happen while no live session is on screen (start checks, a failed start, pairing, QR, model preparation, a failed finalise on Stop). Errors during a session are shown on the session screen instead (4.3, 4.5).

**Compact height**

When the usable height is under 740, screens that are meant to fit without scrolling (Home, Live session) use the compact rules given with them. All other screens simply scroll.

**Accessibility text sizes** (DS 6.1)

From `.accessibility1` up, on every screen: rows stack title above value; value rows put the value on its own line; button pairs stack vertically at full width; chips drop below the text they follow; icon tiles stay. Every screen scrolls. Floating controls stay pinned and cap at `.accessibility2` with the Large Content Viewer.

**States every interactive element has**

Rest, pressed, focused (hardware keyboard), disabled, busy, as defined per component in DS 4. A disabled element that is disabled for a reason the user can fix always has a caption nearby saying why.

**Toasts**

One toast exists: "Copied" **(new)**, after "Copy answer". DS 4.11.

---

## 3. Home

Mockups: `ios-home-ready`, `ios-home-setup`.

**Purpose.** Show at a glance whether a conversation can start, link to whatever is missing, and start it.

**Navigation.** Root of the Live tab. Navigation bar hidden. Does not scroll at normal text sizes.

**Layout, top to bottom**

| # | Element | Component and tokens | Content |
|---|---|---|---|
| 1 | Brand mark | In-app mark, 68 tall, gradient version (DS 8.2). Hidden from accessibility | |
| 2 | Name | `type.display`, two-tone (DS 8.1) | "LiveCue" |
| 3 | Tagline | `type.body`, `ink.secondary` | "A clearer conversation." |
| 4 | Description | `type.caption`, `ink.secondary`, centred, two lines | Line 1: "Live captions with Meta Muse." (Meta) / "Live captions on your PC's GPU." (PC-local) / "Transcription on your iPhone." (on-device). Line 2: "Tap Assist for an answer from your PC." (Codex) / "Tap Assist for an answer from OpenRouter." |
| 5 | Status chip | Status chip with `shadow.raised` (it sits on the ground) | See table below |
| 6 | First-run banner | Info banner, only in the first-run state | "Finish setup" / "The rows below show what is missing. Tap one to fix it." **(new)** |
| 7 | Readiness panel | Panel, header "System readiness" with a trailing chip; three value rows | See below |
| 8 | Start control | Session control, idle face (DS 4.17), floating above the tab bar | See below |

Status chip:

| Condition | Chip |
|---|---|
| PC not needed, speech is Meta | Success, "Cloud direct · no PC" |
| PC not needed, otherwise | Success, "OpenRouter direct" |
| PC needed, relay online | Success, "PC online" |
| PC needed, paired, relay offline | Warning with spinner, "PC reconnecting" |
| PC needed, not paired | Neutral with hollow ring, "PC offline" |

Readiness panel header chip: success with check, "Ready", when the system is ready (spec 0.2); otherwise warning, "Setup".

Readiness rows (value rows, DS 4.8; each navigates and shows a chevron):

| Row | Title | Value (first line, right) | Subtitle | Opens |
|---|---|---|---|---|
| `transcription-settings` | "Transcription" | Streaming provider: its display name. On-device: "Voz on Assist" / "Live Parakeet", or "Not configured" in `status.warning` | Meta: "Meta connection: " + "Key saved · direct" (`status.success`) or "Add API key" (`status.warning`); the state text is `home-meta-state`. PC-local: "PC speech model: " + the home label from spec 3.7, with a 12 spinner while starting or stopping, in `status.success` when a session can start and `status.warning` otherwise; the state text is `home-pc-model-state`. On-device: the model description, or "Download a model to start" **(new)** | Transcription |
| `pair-pc` | "PC connection" when paired, else "Pair Windows PC" | "Not required" / "Paired" / "Pairing required" | "Optional for current setup" / "Saved · connected" / "Saved · reconnecting" / "Required" (`status.warning`) | Pairing |
| `assistant-lab` | "Assistant models & timing" | none | The selected assistant model id, or "Choose an OpenRouter model" in `status.warning` | Assistant Lab |

The old readiness panel and the three setup rows below it said the same things twice. They are one panel now: each readiness line is the row that fixes it. Every old value is still shown.

Start control:

| State | Face | Label | Identifier |
|---|---|---|---|
| Default | Accent, `mic.fill` | "Start conversation" | `start-session` |
| A start is in progress | Accent, spinner, ignores input | "Connecting…" | `start-session` |
| PC-local provider whose model cannot start | Accent, disabled. A caption above it: "PC speech model: `<home label>`. Open Transcription to start it." **(reworded)** | "Start conversation" | `start-session` |
| On-device provider with no model loaded | Accent, `arrow.down.circle`, navigates to Model Library | "Download a transcription model" | `download-model` |

Start is not gated on readiness, as before: a missing prerequisite is reported by the alert after the tap (spec 2.3, section 14).

**States**

| State | What differs |
|---|---|
| Ready | Chip success; header chip "Ready"; no banner |
| Setup needed | Header chip "Setup"; rows with a problem show it in `status.warning` |
| First run (system not ready and History empty) | Adds the banner. This is the whole first-run experience |
| PC model starting or stopping | Spinner in the Transcription row; PC model status refreshed every 2 s while Home is visible |
| Starting | Start control busy; rows stay tappable |
| Compact height | Mark 48 tall; name in `type.title`; tagline hidden; rows keep 52 minimum height |
| Accessibility text size | Screen scrolls; mark hidden; description left-aligned; the Start control stays pinned |

**Accessibility.** Reading order: name, tagline, description, status chip, banner, "System readiness, Ready" (one heading), three rows (each read as title, value, subtitle), Start control. The Start control's hint is "Starts listening" **(new)**.

---

## 4. Live session

Mockups: `ios-live-listening`, `ios-live-answers`, `ios-live-paused`, `ios-live-error`, `ios-live-connecting`.

**Purpose.** This is the product. During a conversation it shows that LiveCue is hearing, what it heard, and the answer to the last Assist, and it offers exactly three actions.

**Navigation.** Replaces the tab bar while a session exists. In: Start on Home. Out: Stop (back to Home), or the sliders button (pushes Assistant Lab, back returns here). Navigation bar hidden.

### 4.1 Layout

Five regions, top to bottom. Only the answer feed scrolls; the cluster is pinned.

| # | Region | Height | Component |
|---|---|---|---|
| 1 | Status panel | Hugs content, about 100 | Compact panel (DS 4.9) |
| 2 | Session notice | Only when present | Warning banner (DS 4.11) |
| 3 | Transcript panel | Collapsed: header plus a two-line preview. Expanded: header plus a scroll area of min(180, 24% of the screen height) | Compact panel |
| 4 | Answer feed, `assistant-feed` | Fills the rest; scrolls under the cluster | Cards on the ground |
| 5 | Control cluster | 64, pinned 34 above the bottom edge | Live control cluster (DS 4.17) |

The newest answer always has the most room and the largest text on the screen (`type.cue`), per DS principle 5.

### 4.2 Status panel

Three lines.

| Line | Leading | Trailing |
|---|---|---|
| A | State chip, then the level meter (visualiser size S, DS 4.21) | Elapsed time in `type.label`, tabular, `MM:SS`; then the sliders icon button (quiet, 44) `assistant-lab`, label "Assistant models & timing", disabled while an Assist runs |
| B | Provider line, `type.caption` `ink.secondary`: the speech provider's display name; Meta appends " · est. transcription" | Meta only: the estimated cost, `$0.00000` plus " USD", tabular, `live-transcription-cost` |
| C | Status line, `type.caption` `ink.secondary`, `transcriber-status` **(new identifier)**: the latest connection or timing message (table below) | Assistant model: the text after the last "/" of the model id, or "Choose model" |

Elapsed time counts wall-clock seconds from session creation and keeps counting while paused, as before.

State chip, level meter and status line by session state:

| Session state | Chip | Level meter | Status line |
|---|---|---|---|
| Starting, or resuming | Live with spinner, "Connecting…" | Idle (`ink.secondary`, minimum) | "Connecting to `<provider display name>`…", then for PC-local, while the PC loads the model, "Loading `<provider short name>` on PC · `N` s. Microphone starts when ready.", for example "Loading Nemotron 3.5 on PC · 12 s. Microphone starts when ready." |
| Listening | Live with pulsing dot, "Listening" | Moving with the level | Meta: "Meta direct · `0.00` s setup". PC-local: "Connected · `0.00` s setup". After the first text arrives: "First words · `0.00` s from stream start". Parakeet: "Last buffer processing: `N` ms". Voz: "Recording on this iPhone" **(new)**, then after each batch "`0.0` s audio → `0.00` s transcription" |
| Pausing | Warning with spinner, "Pausing…" **(new)** | Flat | "Finishing the transcript…" **(new)** |
| Paused by the user | Warning with pause glyph, "Paused" | Flat, `status.warning` | The last listening line plus " · Final `0.00` s" when a finalisation time is known |
| Paused automatically | Same chip, plus the session notice (4.3) | Flat, `status.warning` | "Disconnected · resume to retry" |
| Stopping | Neutral with spinner, "Stopping…" **(new)** | Flat | "Finishing the transcript…" |

These lines are the strings the old app computed and never displayed (spec 11.3, D4). "Connecting…", "Pausing…" and "Stopping…" replace the single old label "Updating…".

### 4.3 Session notice

A warning banner between the status panel and the transcript, `session-notice` **(new identifier)**. It appears when the session pauses itself or loses something, and stays until Resume succeeds or the session ends. It replaces the modal alert for these cases: a modal mid-conversation needs a tap to dismiss, and a banner that stays explains the paused state for as long as it lasts.

- Title: "Recording paused" **(new)**. Body: the reason, from section 14.
- Announced assertively once; warning haptic (DS 7.2).
- On-device errors (Voz reaching its 30-minute cap, Parakeet failing or falling behind) now pause the session and show this notice, like stream failures do.
- A failed finalise on Pause shows the notice with the title "Paused" and the body "Could not finalize the transcript. Some final words may be missing."
- A failed Resume shows the notice with the body "Transcription could not start. " followed by the reason; the session stays paused.

### 4.4 Transcript panel

Header: a disclosure row (DS 4.24), "Live transcript", `expand-transcript`, accessibility label "Expand live transcript" / "Collapse live transcript". Collapsed by default. The toggle animates over `motion.base`.

| Mode | Content |
|---|---|
| Collapsed | A two-line preview, `transcript-preview`, in `type.transcript`. It shows the interim text if there is any (`ink.secondary`, with the live caret); else the last final segment (`ink.primary`); else the placeholder (`ink.secondary`) |
| Expanded | A scroll area, `live-transcript`, listing every final segment as a transcript line (DS 4.18, 12 apart) and then the interim text. It stays pinned to the newest line while the reader is at the bottom; if they scroll up nothing moves and a "Jump to live" chip-button appears **(new)** |

Placeholders: "Recording locally. Tap Assist to transcribe." (Voz); "Listening for speech…" (others); while connecting, "Listening starts when the PC model is ready." (PC-local) or "Listening starts when the connection is ready." (others) **(new)**.

There are no speaker labels; the product does not separate speakers. Interim text keeps the same metrics as final text, so nothing reflows when it becomes final.

### 4.5 Answer feed

`assistant-feed`. Newest first. It scrolls to the top when a new answer lands, when an Assist starts and when an error appears.

**1. Error card** (an Assist failed). A prominent card containing:

- a danger banner, title "Assist could not finish", body = the error text, `assist-error` (section 14; always a categorised message, never raw system text);
- a caption, "No automatic retry. Any partial answer below is incomplete.";
- a secondary button with a refresh glyph, "Retry same text", `retry-failed-assist`, disabled while assisting or when there is no previous request.

**2. In-progress card** (an Assist is running, or streamed text is left over from a failure). A cue card (DS 4.19):

- header kicker "Assistant"; trailing a 12 spinner and the stage label: "Transcribing…", "Thinking…", "Checking model…" or "Replying…";
- body: the streamed text so far, `streaming-answer`, `type.cue`, selectable, with the live caret while streaming;
- it is the prominent card while it is the newest thing in the feed. When an error card is above it, it becomes a plain card in a compact panel, its trailing label reads "Incomplete answer" in `status.danger`, and its text turns `ink.secondary`.

**3. Latest answer.** A prominent cue card:

| Part | Content |
|---|---|
| Header | Kicker "Assistant"; trailing live chip "Latest" |
| Question | The detected question in `type.label` `ink.secondary`, two lines at most. Omitted when it is the sentinel "Latest conversation" |
| Answer | `assistant-answer`, `type.cue`, selectable |
| Details | Only if details exist: a quiet button "More detail" / "Hide detail" that reveals them in `type.body` `ink.secondary` |
| Footer | Only with timing data: total time `0.00 s`, then "· $0.000000" when a cost was reported, in `type.caption` tabular; trailing quiet icon button, label "Copy answer", which copies the answer and shows the toast "Copied" |
| Timing | Only with timing data: a disclosure row "Timing & usage", `timing-disclosure`, closed by default, containing the timing view (4.6) |
| Retry | Quiet button with a refresh glyph, "Retry same text with selected model", `retry-same-text`, disabled while assisting or when there is no previous request |

**4. Earlier answers.** A panel titled "Earlier answers" **(new)** holding one plain card per older turn, newest first. Same parts as the latest answer with these differences: no "Latest" chip; the total time sits in the header; answer text is `type.body`, `previous-assistant-answer`; the timing disclosure is `previous-timing`; no retry button.

**5. Empty state** (no turns, not assisting, no error). On the ground: a 64 icon tile with `sparkles`, title "Your assistant", body "Tap Assist when you want help with a question. Answers appear here while the conversation keeps going."

Only assistant answer text is ever shown. There is no dismiss control on an answer (`dismiss-answer` must not exist).

### 4.6 Timing view

`response-timing`. Detail rows (DS 4.8) inside the disclosure. Heading text for assistive technology: "Response timing".

| Row | Value |
|---|---|
| Caption | "`<model used>` · `<reasoning effort used>`" |
| "Tap → answer" | `0.000 s` |
| "Transcription after tap" or "Transcription (reused)" | `0.000 s` |
| "Context + model check" | `0.000 s` |
| "PC round trip" or "OpenRouter round trip" | `0.000 s` |
| OpenRouter: "Reported request cost" | `0.000000 credits` or "Unavailable"; then a caption "`N` input · `M` output tokens" when usage was reported |
| Codex with PC timings: "PC: Codex CLI call", "PC: relay overhead", "Transfer / client estimate" | `0.000 s` each |
| Codex without PC timings | A caption in `status.warning`: "PC timing unavailable. Restart the updated desktop relay." |
| Parakeet: "Last live STT chunk" | `N ms` |
| Caption | "`<speech provider id>` · `<N>` transcript characters", plus " · same-text retry" on retries |
| Footnote | Spec text S-20 (Codex) or S-21 (OpenRouter), `type.caption` `ink.secondary` |

### 4.7 Control cluster

DS 4.17, live control cluster.

| Control | Identifier | Face | Label for assistive technology | Disabled when |
|---|---|---|---|---|
| Pause / Resume | `pause-session` | 52 glass circle; `pause.fill`, or `play.fill` while paused | "Pause" / "Resume" | An Assist is running, or the session is changing state |
| Assist | `assist-button` | 64 accent capsule; `sparkles` and "Assist". Busy: spinner and "Replying…" | "Assist" | An Assist is running (busy), or the session is changing state |
| Stop | `end-session` | 52 glass circle; `stop.fill` in `status.danger`. A spinner while the session is changing state | "Stop conversation" | An Assist is running, or the session is changing state |

No instruction field, no answer sheet and no cancel on this screen, as before.

### 4.8 The screen in each state

| State | Status panel | Notice | Transcript | Feed | Cluster |
|---|---|---|---|---|---|
| Connecting (start) | "Connecting…", progress on line C | none | Connecting placeholder | Empty state | All disabled; Stop shows a spinner |
| Listening, nothing asked yet | "Listening" | none | Interim text or placeholder | Empty state | All enabled |
| Assist running | Unchanged; sliders button disabled | none | Keeps updating | In-progress card on top | Assist busy; Pause and Stop disabled |
| Answer saved | Unchanged | none | | Latest card on top, earlier answers below | All enabled |
| Assist failed | Unchanged | none | | Error card, then the incomplete text if any, then saved answers | All enabled |
| Paused by the user | "Paused" | none | Last text stays | Unchanged | Leading control shows Resume |
| Paused automatically | "Paused", "Disconnected · resume to retry" | "Recording paused" and the reason | Last text stays, including uncommitted interim text | Unchanged | Leading control shows Resume |
| Stopping | "Stopping…" | none | | Unchanged | All disabled; Stop shows a spinner |

**Compact height.** The transcript preview is one line; status lines B and C merge into one (provider left, model right; the status line replaces the provider line for 4 seconds when it changes). The cluster keeps its sizes.

**Accessibility text size.** Line A wraps: chip and meter, then elapsed time and the sliders button. Lines B and C stack every item. The transcript preview allows three lines. The answer feed keeps `type.cue`. Timing rows stack key above value. The cluster keeps its sizes; "Assist" may shrink to its glyph with the Large Content Viewer.

**Accessibility.** Order: state chip and elapsed time (one element: "Listening, 4 minutes 12 seconds"), provider, cost, status line, assistant model, sliders button, notice, transcript header, transcript, feed, cluster. The transcript is marked as updating frequently and is not announced. A finished answer is announced once, in full. State changes of the chip are announced politely; the notice assertively. The level meter and caret are hidden.

**As built (WP-10).** The state element of line A has the chip's text as its label and the spoken elapsed time as its value ("Listening", "4 minutes 12 seconds"), so a test can find it by the label alone. The collapsed preview shows the end of a text that is longer than its lines, because the newest words matter. An earlier answer keeps its "Copy answer" button; the line beside it shows only a reported cost, since its total time is in the header. The status line may take two lines, because the PC loading text is longer than one. In the timing view, token counts and the transcript length are plain digits without a thousands separator.

**Haptics.** DS 7.2: medium impact when listening actually starts, rigid impact on stop, light on pause and resume, one soft impact when an answer starts to arrive, error on a failed Assist, warning with a notice.

---

## 5. Assistant Lab

Mockups: `ios-assistant-codex`, `ios-assistant-openrouter`, `ios-openrouter-models`.

**Purpose.** Choose where answers come from and which model produces them, and compare how past answers performed.

**Navigation.** Title "Assistant Lab". In: Home row, Settings row, the sliders button on the live screen (all `assistant-lab`). The model catalogue for the current service loads every time the screen appears. While an Assist is running, every control in panels 1 to 3 is disabled.

**Layout, top to bottom**

**Panel 1, "Answer service".** Two choice rows; the panel's selected value is `assistant-provider`.

| Row | Title | Subtitle **(new)** |
|---|---|---|
| `assistant-provider-codex` | "PC · Codex" | "Through your paired PC. Adds a summary when you stop." |
| `assistant-provider-openrouter` | "OpenRouter · direct" | "Straight from this iPhone. Answers stream in." |

Tapping a row switches the service at once (each service remembers its own model and effort) and reloads the catalogue.

**Panel 2, the assistant.** Title "PC assistant" (Codex) or "OpenRouter assistant".

- The selected model id in `type.headline`, monospaced, `selected-assistant-model`; empty when none is chosen.
- Codex: a caption "Reasoning: `<effort>`"; one choice row per catalogue model, `choose-<model id>`; then, if the selected model is in the catalogue, a navigating row "Reasoning" with the capitalised effort as its value, `reasoning-picker`. Picking a model keeps the current effort if that model supports it, otherwise takes the model's first listed effort.
- OpenRouter: a navigating row "Choose a model" / "Change model", `openrouter-model-picker`; a caption "Uses the selected model's default reasoning behavior."

**Panel 3, "OpenRouter API key"** (OpenRouter only).

- Secure field (DS 4.7), placeholder "Paste API key", `openrouter-key`.
- Primary button "Save key", `save-openrouter-key`, disabled while the field is empty. On success the field clears and the keyboard closes.
- A status chip: success "Key saved" or neutral "No key saved", `openrouter-key-state`.
- With a saved key: secondary button "Verify key (no inference)", `verify-openrouter-key` (busy while verifying), and destructive secondary button "Remove key", `remove-openrouter-key`.
- A message line for the result of save, verify or remove (section 14), in `status.success` or `status.danger` with a glyph.
- Quiet button with the external-link glyph, "Get an OpenRouter API key".
- Caption: spec text S-10.

On the OpenRouter variant with no key saved, panel 3 moves above panel 2, because the key comes first.

**Panel 4** (no title).

- Quiet button with a refresh glyph, "Refresh PC model list" / "Refresh OpenRouter models"; busy while loading.
- The catalogue message (section 14).
- Caption: spec text S-11 (Codex) or S-12 (OpenRouter).

**Panel 5, "Recent comparisons".** The 50 most recent turns with timing data across all sessions, newest first, as navigating rows: title = the model used; subtitle lines "`<speech id>` · `<effort>` · same text" or "… · fresh transcript", "`0.00 s total` · `<service name>`", and the detected question (two lines). Empty text: "Assist once to start collecting results."

**Sub-screens**

| Screen | Content |
|---|---|
| OpenRouter models (title "OpenRouter models") | System search field, placeholder "Search models", matching name or id without regard to case. One row per model: name (choice row; `choose-<model id>`), the id in `type.mono`, and "Input `$0.000 / 1M` · output `$0.000 / 1M`" or "Unknown" for a missing price. Tapping selects the model with effort `default` and returns. Empty state: "No models" / "Refresh the catalog or change your search." |
| Reasoning (title "Reasoning") | One choice row per effort of the selected model, in catalogue order, `effort-<effort>`: "None (no reasoning)" for `none`, the others capitalised. Tapping selects and returns |
| Run details (title "Run details") | Panels: the detected question; the answer in `type.cue`; the timing view (4.6), open |

**As built (WP-10).** Three things differ from the text above, because the components give no other way. A choice row has no icon tile and one caption, so the two service rows have no tile, a Codex row is titled with the model's catalogue name (its id is the headline above the rows), and in the OpenRouter picker the id and the prices are the two caption lines of the row; the id is not in `type.mono` there. A banner needs a title: the catalogue failure banner is titled "Model list not loaded" **(new)**. The three panels of Run details are titled "Detected question", "Answer" and "Response timing" **(new)**; the first is left out for the sentinel question. The caption "Available when the current answer finishes." sits above panel 1. "Verify key (no inference)" and "Remove key" sit side by side where they fit and one under the other where they do not. The search field of the OpenRouter picker is always in view.

**States.** Loading (refresh button busy, rows stay); catalogue failed (danger banner with the catalogue message in panel 4); not paired with Codex selected (panel 2 shows only the message "Pair your PC first." and a quiet button "Pair Windows PC" **(new)**); Assist running (panels 1 to 3 disabled, with a caption "Available when the current answer finishes." **(new)**).

---

## 6. Settings

Mockup: `ios-settings`.

**Purpose.** Everything that is set once and left alone.

**Navigation.** Root of the Settings tab, large title "Settings". It cannot be open during a session (the tabs are gone), so nothing here needs a "session active" state.

**Layout, top to bottom**

| Panel | Rows |
|---|---|
| "Transcription" | Navigating row "Provider", value = the provider's display name, `speech-provider` → Transcription. Then the provider's own rows, exactly as on the Transcription screen (section 7). Then the provider explanation: spec text S-01 (Meta), S-02 (PC-local) or S-03 (on-device) |
| "Assistant" | Navigating row "Assistant models & timing" with the model id as subtitle, `assistant-lab`. Then a multi-line field (2 to 5 lines) labelled "Assistant instructions", placeholder "Optional instruction (e.g. answer briefly)", `assistant-instruction`, saved on every keystroke; caption "Applied to every Assist request until you change it."; keyboard accessory button "Done", `finish-instruction` |
| "Devices" | Navigating row, `pair-pc`: "PC connection" with the saved state as subtitle when paired, else "Pair Windows PC" |
| "Transcription cost" | Detail rows "Meta Muse rate" → "$0.18 / hour" and "PC-local models" → "$0 API usage"; spec text S-05; quiet button with the external-link glyph, "Meta pricing & usage documentation" |
| "Display" | Detail row "Text size" → "Follows your iPhone's text size" **(reworded; it is now true)** |
| "About" **(new)** | Detail rows "Appearance" → "Daylight"; "Version" → the app's version, or "Unknown" if it cannot be read. Then two captions: "Speech packages" with the attribution text, and "Usage reporting" with the Voz usage text (both in `03-system-architecture.md` section 4.13) |
| "Privacy" | Spec text S-06, with the sentence added by `01-decisions.md` section 6.1 |

The "Provider" row is disabled, with a caption saying why **(new)**, while an on-device model is being prepared ("Available when the model has finished preparing.") or while a PC model command is in flight or the PC reports starting or stopping ("Available when the PC model has finished changing.").

---

## 7. Transcription

Mockup: `ios-transcription`.

**Purpose.** Choose the speech provider and finish whatever it needs.

**Navigation.** Title "Transcription". In: Home row `transcription-settings`, Settings row `speech-provider`. Out: back, or one of the provider rows below.

**Panel 1: the five providers** as choice rows.

| Identifier | Title |
|---|---|
| `provider-meta` | "Meta Muse · live cloud" |
| `provider-nemotron` | "Nemotron 3.5 · PC live" |
| `provider-qwen3` | "Qwen3 1.7B · PC live" |
| `provider-voz` | "Voz · iPhone on Assist" |
| `provider-parakeet` | "Parakeet · iPhone live" |

Caption: "PC models run one at a time on your GPU. Start a new conversation to compare them. Neither sends audio to Meta."

Tapping a row selects it and stays on this screen, so the second panel can show what the new provider needs. The rows are disabled under the same conditions as the "Provider" row in Settings, with the same caption.

**Panel 2: the selected provider**, titled with its display name.

| Provider | Rows |
|---|---|
| Meta | Navigating row "Meta API key", value "Saved on iPhone" or "Required" (`status.warning`), `meta-key-settings`. Spec text S-01 |
| Nemotron, Qwen3 | Navigating row "PC speech model", value = the home label (spec 3.7). Spec text S-02 |
| Voz, Parakeet | Navigating rows "Model Library" (value: "Ready" or "Not loaded" **(new)**) and "Speech benchmark". Spec text S-03 |

---

## 8. PC speech model

Mockup: `ios-pc-model`.

**Purpose.** Start, stop and watch the speech model on the PC. Shown only for Nemotron and Qwen3.

**Navigation.** Title "PC speech model". In: the row on Transcription or Settings. The PC model status is refreshed every 2 seconds while this screen is visible.

**Layout.** One panel titled with the provider's display name.

| Element | Content | Identifier |
|---|---|---|
| State line | A 20 spinner while changing; the state in `type.title`: "Stopped", "Starting", "Ready", "Stopping", "Error", or "Unavailable" when unknown. While starting or stopping, the elapsed "`N` s" at the trailing edge | `pc-model-state` |
| Message | The PC's message, or a local one (section 14), `type.body` | `pc-model-message` |
| Other model loaded | Info banner "Currently loaded: `<id>`" / "Start switches to your selected model when it is idle." | |
| Start | Primary button with a play glyph, "Start model". Disabled when locked, or already ready for this provider | `start-pc-model` |
| Stop | Secondary button with a stop glyph, "Stop model". Disabled when locked, when the status is unknown, when the state is stopped, or when the loaded model is not this provider | `stop-pc-model` |
| Refresh | Quiet button with a refresh glyph, "Refresh status" | `refresh-pc-model` |
| Caption | Spec text S-04 | |

Locked = a command is in flight, or the PC reports starting or stopping, or the PC reports the model busy, or the phone is not paired.

**States.** Unknown ("Unavailable", message "Checking PC model…" or "Pair your PC first."); stopped; starting; ready; ready and in use (state "Ready", a neutral chip "In use" **(new)**, both buttons disabled); stopping; error (state in `status.danger`, the message says why, Start enabled to retry).

---

## 9. Meta API key

Mockup: `ios-meta-key`.

**Purpose.** Save, test and remove the Meta key. Shown only when the provider is Meta.

**Navigation.** Title "Meta API key". In: the row `meta-key-settings`.

**Panel 1, "Meta Muse · direct cloud"**

- Secure field, placeholder "Paste Meta API key", `meta-key`.
- Primary button "Save key", `save-meta-key`, disabled while the field is empty or a test is running.
- Status chip: success "Key saved" or neutral "No key saved", `meta-key-state`.
- With a saved key: secondary button "Test connection (no audio)", `verify-meta-key`, busy with the label "Connecting…" while it runs; destructive secondary button "Remove key", `remove-meta-key`, disabled while testing.
- Message line, `meta-key-message` (section 14).
- Quiet button with the external-link glyph, "Open Meta developer dashboard".

**Panel 2, "How it works".** Spec texts S-07, S-08 and S-09 as three captions.

---

## 10. Pairing and the QR sheet

Mockups: `ios-pairing-new`, `ios-pairing-saved`, `ios-qr-scan`.

**Purpose.** Connect this iPhone to the relay on the PC, and show the state of that connection.

**Navigation.** Title "PC connection" when paired, otherwise "Pair Windows PC". In: Home or Settings row `pair-pc`. The QR sheet opens over it. On appearing, the address field is filled with the saved address.

**Layout when paired**

**Panel "Saved PC"**

| Element | Content | Identifier |
|---|---|---|
| State chip | Success with check, "Connected"; warning with spinner, "Reconnecting"; danger with alert glyph, "Pairing needs attention" (the relay rejected the token). A later plain network failure returns it to "Reconnecting" | `saved-pc-state` |
| Address | The saved address, `type.mono`, selectable | `saved-pc-address` |
| Message | The connection message (section 14); by default "Your pairing is saved. The app reconnects automatically after restarting." | |
| Reconnect | Secondary button with a refresh glyph, "Reconnect"; busy with "Checking…" during a check | `reconnect-pc` |
| Disclosure | Row "Pair another PC or scan a code" (open: "Hide pairing options"), revealing the two panels below | `show-pairing-options` |

**Layout when not paired, or when the disclosure is open**

**Panel "Quick pairing"**

- Primary button, large, full width, with the QR glyph: "Scan PC QR code", `scan-pairing-qr` → QR sheet.
- Caption **(reworded for the new Windows app)**: "Open LiveCue Desktop on Windows and scan its QR. Without a camera, paste the pairing text from the PC, or enter the values manually." LiveCue Desktop itself never shows the pairing text; it comes from the PC's command-line host.
- Multi-line monospaced field, placeholder "Paste pairing text".
- Secondary button "Read pairing payload", disabled while the field is empty. It fills the two fields below and clears this one; nothing is saved or sent yet.

**Panel "Manual pairing"**

- Field "PC address": URL keyboard, no auto-capitalisation, placeholder `https://<pc-name>.<network>.ts.net:11443/`.
- Secure field "Pairing token".
- Primary button, full width: "Verify and pair", disabled while either field is empty, busy while verifying.

The old line "PC connected" under the manual fields is dropped: it described the saved PC while the user was typing a different one (spec 11.1, B17). The saved PC's state is in its own panel.

**Panel "Privacy".** Spec text S-13.

**QR sheet**

DS 4.15, scanner. A sheet with a full-bleed camera preview.

- Top: title "Scan PC QR"; button "Cancel".
- Centre: the 260 viewfinder with four corner brackets; the preview outside it is dimmed.
- Bottom: a capsule on `surface.inverse`, "Point at the QR in LiveCue Desktop".
- The first QR code read closes the sheet and is handled exactly like pasted pairing text. Success haptic.
- Camera denied, unavailable, or scanner failure: the sheet closes and the alert shows the message (section 14).

**States.** Not paired; paired and connected; paired and reconnecting; pairing needs attention; verifying (button busy, fields disabled); verify failed (alert; fields keep their text; an existing pairing is untouched).

---

## 11. Model Library

Mockup: `ios-model-library`.

**Purpose.** Download and load the on-device model for the current provider. Shown only for Voz and Parakeet; it lists exactly one model.

**Navigation.** Title "Model Library". In: the row on Transcription or Settings, or "Download a transcription model" on Home.

**Layout**

- Caption on the ground: "Prepare the model for this tab once. The download is cached on your iPhone. Initial preparation can take longer than subsequent loads."
- Section title "English models", then one panel for the model:

| Element | Content |
|---|---|
| Name | "Voz on Assist" or "Live Parakeet", `type.headline`; a live chip "Recommended"; when it is the loaded model, a success chip with check, "Ready" |
| Description | "~467 MB · Transcribes when you tap Assist" or "~230 MB · English live captions · 320 ms mode" |
| Main button | Primary: "Download & use"; "Reload" once loaded. Busy and disabled while preparing |
| Unload | Secondary button "Unload", only when loaded; disabled while preparing |
| Usage reporting | Voz only, a caption **(new)**: "The Voz package reports anonymous usage to its vendor, Desert Ant Labs. LiveCue cannot turn this off." |
| Progress block | Only while this model is being prepared, or after its preparation failed: a determinate bar, `model-download-progress`; "`N`% setup" and "`MM:SS` elapsed"; the status line (section 14); a byte or file counter when known; the caption "Downloading and model preparation are separate steps; preparation can continue after the download reaches 100%." |

- Section title "Pinned source", then a panel with detail rows "Status" → the status line, and "Package" → "Desert Ant 3.1.0" (Voz) or "FluidAudio 0.15.6" (Parakeet).

The progress block belongs to its model and disappears when that model is ready. It no longer lingers after completion or shows another model's last run (spec 11.1, B13).

**States.** Not loaded; preparing (progress block, buttons busy); ready (chip "Ready", "Reload", "Unload"); failed (progress block stays with the bar in `status.danger`, status "Preparation failed", main button enabled to try again; the reason is in the alert).

---

## 12. Speech benchmark

Mockup: `ios-benchmark`.

**Purpose.** A manual word-error-rate worksheet for comparing speech models. Shown only for Voz and Parakeet. Nothing is saved.

**Navigation.** Title "Speech benchmark" (the old title "Model Lab" did not match the row that opened it). In: the row on Transcription or Settings.

**Layout**

| Panel | Content |
|---|---|
| "Purpose" | "Record the same short phrase with different models, then compare processing time and word error rate." |
| Worksheet | Multi-line field "Reference phrase", default "The quick brown fox jumps over the lazy dog". Multi-line field "Model transcript", empty by default. Secondary button "Use latest transcript" with the caption "Fills the box with your most recent conversation, active or saved." **(new)** |
| "Word error rate" | The rate as a percentage with one decimal in `type.display`, tabular, updated as either field changes. Caption "Lower is better. Test in the same room and speak the same phrase for a fair comparison." |

"Use latest transcript" fills the model transcript with the segments of the most recent session, joined by spaces: the active session if there is one, otherwise the newest saved one. With no sessions it is disabled and the caption reads "No conversations yet." The rate shows "–" while the reference phrase is empty.

---

## 13. History and Session detail

Mockups: `ios-history`, `ios-history-empty`, `ios-session-detail`.

### History

**Purpose.** Every stored conversation, newest first.

**Navigation.** Root of the History tab, large title "History". Out: Session detail.

**Layout.** One panel of navigating rows:

| Line | Content |
|---|---|
| Title | The session title, `type.headline`, two lines at most |
| Date | Start date and time, abbreviated date and short time in the device locale |
| Counts | "`N` transcript segments · `M` assists" |
| Usage | Only if usage was recorded. Meta: "Meta Muse · cloud · `$0.00000` USD", `history-transcription-cost`. Any other provider: "`<provider name>` · Local", `history-local-transcription`. Either may end with " · incomplete". Never a made-up zero |

**Delete.** Swiping a row reveals "Delete". It opens a system confirmation: title "Delete this conversation?", message "Its transcript and answers are removed from this iPhone. This cannot be undone.", buttons "Delete" (destructive) and "Cancel" **(new)**. On Delete the row fades out.

**States.** Empty: a 64 icon tile with the clock glyph, title "No conversations yet", body "Completed sessions will appear here.", on the ground. With items. A session cut off by the app closing appears closed at its last save and titled by the normal rule: the first 48 characters of its first segment, or "Conversation".

### Session detail

**Purpose.** Read one conversation. Read-only: no export, share, search, rename or resume.

**Navigation.** Title = the session title, truncated. In: a History row.

**Layout.** One panel per section, each only if it has content:

| Panel | Content |
|---|---|
| "Transcription usage" | Detail rows: "Estimated cost" (Meta); "Provider" (the provider name, or "On-device"); "First words from stream start" and "Last stream finalization" as `0.00 s`, when known; for Meta also "Processed audio reported" (`N seconds`), "Streams" (a count) and "Rate saved with session" (`$0.18 / hour`); then spec text S-14 (complete) or S-15 (incomplete) as a caption |
| "Summary" | The summary text, `type.body` |
| "Key points" | A list, DS 4.20 item style |
| "Action items" | A list |
| "Transcript" | Every segment as a transcript line |
| "Assistant" | For each turn: the detected question in `type.label` `ink.secondary`, the answer in `type.body`, and the details if any, as plain cards |

---

## 14. Messages

Every message the old app produced is still produced. This section says where each one appears now. Strings are from spec section 8 unless marked.

### Where errors appear

| Situation | Surface |
|---|---|
| Start checks fail, or the transcriber cannot start (the session is discarded) | "LiveCue" alert, on Home |
| The session pauses itself or loses audio; Resume fails; finalising fails on Pause | Session notice (4.3) |
| An Assist fails | Error card (4.5) |
| Pairing, QR, model preparation | "LiveCue" alert |
| Finalising fails on Stop | "LiveCue" alert, on Home |
| Key and catalogue actions | Inline message line on their screen |

### Session notices

| Body | Cause |
|---|---|
| "Network cannot keep up. Recording paused; resume to retry." | Upload queue overflow |
| "Audio connection failed. Resume to reconnect." | Sending audio failed |
| "Microphone interrupted. Resume when your call or other audio finishes." | Audio interruption |
| "Microphone route changed. Resume to use the current microphone." | Input device went away |
| "Meta transcription stopped. Check API access, billing, or rate limits before retrying." | Meta error event |
| "Could not connect directly to Meta. Check this iPhone's internet connection and retry. No PC is required for Meta transcription." | Meta socket failed |
| The phone's own text for the relay's error code (`02-relay-protocol.md` section 14.1) | The relay ended the PC speech stream or refused it: not authorised, another stream active, relay paused, model changing, and every other code |
| The phone's text for the condition (`02-relay-protocol.md` section 14.2) | The PC speech connection failed without a code: unreachable, secure connection failed, dropped. Raw system error text and the relay's own text are never shown |
| "Transcription could not start. " + the reason | Resume failed |
| "Pending recording reached 30 minutes and was transcribed. Resume to keep recording." **(reworded)** | Voz pending-audio cap. The session is now paused and the pending audio has been transcribed as part of that pause |
| "Transcription could not keep up. Recording paused; resume to continue." **(reworded)** | On-device processing fell behind. The session is now paused |
| "Transcription stopped on this iPhone. Resume to continue." **(new)** | On-device processing failed. Replaces the library's raw error text |

### Assist errors

The inline errors of spec 8.3 are unchanged. Two cases change:

| Text | Cause |
|---|---|
| The phone's text for the code or condition (`02-relay-protocol.md` sections 14.1 and 14.2) | A Codex Assist failed. Replaces raw system error text and relay-provided text (spec 11.1, B8) |
| The OpenRouter error text of the failed fetch (spec 8.3) | The OpenRouter catalogue was empty and fetching it failed. Replaces the misleading "Choose an OpenRouter model…" (spec 11.1, B9) |

### Alerts on Home and Pairing

The messages of spec 8.1 that are not session notices, unchanged, with these adjustments:

- "PC speech model: `<home label>`. Open Transcription to start it or check its status." **(reworded: "Transcription settings" is now "Transcription")**.
- "Add your Meta API key in Transcription. It stays in this iPhone's Keychain." **(reworded likewise)**.
- A failed "Verify and pair" no longer starts with "Your pairing is saved." (spec 11.1, B10); that prefix is used only for health-check failures of the saved pairing.
- "Use the pairing QR from LiveCue Desktop. It must contain an HTTPS PC address and a valid pairing token." is unchanged and also covers pasted text.

### Inline status lines

Saved PC, Meta key, OpenRouter key, catalogue, PC model and Model Library lines are those of spec 8.2, unchanged, except that a failure reported by the PC is worded by `02-relay-protocol.md` section 14. Messages name "Assistant models & timing" and "Transcription" where the old text said "Assistant settings" and "Transcription settings"; the exact strings are in `04-ios-architecture.md` section 1.3.

---

## 15. Identifier index

Every identifier from spec 1.13, and where it is now.

| Identifier | Screen | Element |
|---|---|---|
| `start-session`, `download-model` | Home | Start control (two variants) |
| `pair-pc` | Home; Settings | Readiness row; Devices row |
| `transcription-settings` | Home | Readiness row "Transcription" (now opens Transcription) |
| `assistant-lab` | Home; Settings; Live session | Readiness row; Assistant row; sliders button |
| `home-meta-state`, `home-pc-model-state` | Home | State text in the Transcription row's subtitle |
| `expand-transcript`, `transcript-preview`, `live-transcript` | Live session | Transcript header, collapsed preview, expanded scroll area |
| `live-transcription-cost` | Live session | Status panel, line B |
| `assistant-feed` | Live session | Answer feed |
| `assist-error`, `retry-failed-assist` | Live session | Error card |
| `streaming-answer` | Live session | In-progress card text |
| `assistant-answer`, `previous-assistant-answer` | Live session | Latest and earlier answer text |
| `timing-disclosure`, `previous-timing`, `response-timing` | Live session; Run details | Timing disclosure (latest, earlier), timing view |
| `retry-same-text` | Live session | Latest answer |
| `pause-session`, `assist-button`, `end-session` | Live session | Control cluster |
| `speech-provider` | Settings | "Provider" row |
| `provider-<id>` | Transcription | Provider choice rows |
| `meta-key-settings` | Transcription; Settings | "Meta API key" row |
| `meta-key`, `save-meta-key`, `meta-key-state`, `verify-meta-key`, `remove-meta-key`, `meta-key-message` | Meta API key | As named |
| `pc-model-state`, `pc-model-message`, `start-pc-model`, `stop-pc-model`, `refresh-pc-model` | PC speech model | As named |
| `assistant-instruction`, `finish-instruction` | Settings | Instruction field, keyboard "Done" |
| `assistant-provider`, `assistant-provider-<id>` | Assistant Lab | Answer service value and choice rows |
| `openrouter-key`, `save-openrouter-key`, `openrouter-key-state`, `verify-openrouter-key`, `remove-openrouter-key` | Assistant Lab | OpenRouter API key panel |
| `selected-assistant-model`, `openrouter-model-picker`, `choose-<model id>`, `reasoning-picker`, `effort-<effort>` | Assistant Lab and its pickers | As named |
| `saved-pc-state`, `saved-pc-address`, `reconnect-pc`, `show-pairing-options`, `scan-pairing-qr` | Pairing | As named |
| `model-download-progress` | Model Library | Progress bar |
| `history-transcription-cost`, `history-local-transcription` | History | Usage line |
| `session-notice` **(new)** | Live session | Session notice banner |
| `transcriber-status` **(new)** | Live session | Status panel, line C, the status line |

`dismiss-answer` must not exist anywhere.

Flows that tests written for the old app will find different: tapping `transcription-settings` lands on Transcription, not Settings; tapping a `provider-<id>` row stays on Transcription; `assistant-provider-<id>` rows are on Assistant Lab itself, with no list to open; PC model controls are one screen deeper; in-session errors are a banner, not an alert; deleting a session needs a confirming tap.

---

## 16. What changed from the old app

Everything the old app could do is still reachable. These are the differences a reviewer should know about.

| Old | New | Why |
|---|---|---|
| Home had a readiness panel and three setup rows saying the same things | One panel; each readiness line is the row that fixes it | Half the reading, nothing lost |
| Home row "Transcription & appearance" pushed Settings | Row "Transcription" opens the Transcription screen | Orchestrator decision 7; one route into Settings |
| Provider picker closed on selection | Selection stays on a screen that also shows the provider's setup | Choose and finish in one place |
| PC model controls were a section of Settings | Own screen | Clear start and end for the 2-second polling; linkable from Home |
| Answer service was a row opening a two-row list | Two choice rows in place | One step fewer |
| Live status label "Updating…" for every transition | "Connecting…", "Pausing…", "Stopping…" and a status line with the connection steps and timings | Decision 2 |
| Stream failures raised a modal alert | A session notice that stays until Resume | A modal interrupts the conversation; a banner explains the paused state for as long as it lasts |
| On-device errors left the screen saying "Listening" | They pause the session and show a notice | Decision 3 |
| Raw system error text for some PC failures | Categorised messages | Decision 3 |
| Model Library progress stayed forever and followed the wrong model | Per model, gone when ready | Decision 4 |
| Swipe-delete was immediate | Confirmation first | Decision 6 |
| "Text size: 16 pt · follows Dynamic Type" | "Follows your iPhone's text size" | Decision 5; all text now uses text styles |
| "Appearance: Midnight Mint" | "Appearance: Daylight" | Decision 1 |
| "Use latest transcript" always produced nothing | Uses the most recent session, active or saved | Decision 8 |
| Speech benchmark titled "Model Lab" | "Speech benchmark" | Matches the row that opens it |
| Older answers were cards as loud as the latest | Latest is the one prominent card; the rest sit in "Earlier answers" | DS principle 5 |
| Copy gave no feedback | Toast "Copied" | The action was invisible |
| "PC connected" under the manual pairing fields | Removed | It described a different PC than the one being typed |
| Pairing caption named the terminal launcher | Says to scan the QR in LiveCue Desktop, or to paste the pairing text from the PC | The launcher no longer exists, and the desktop window never shows the pairing text |
| Home artwork: tile with five bars over wave lines | The new brand mark | DS 8.2 |

Not added, by decision: an unpair button, a cancel for a running Assist, a device list, a dark theme, an onboarding flow, and any way to edit the instruction from the live screen.

Nothing in the spec was left without a home. Two things are honoured in a reduced form: the old static "comparison" of speech providers is still done by hand through History (the per-session provider and timings are in Session detail), and the Speech benchmark remains a manual worksheet.
