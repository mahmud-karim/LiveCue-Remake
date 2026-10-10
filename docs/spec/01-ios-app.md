# 01 — iPhone app layer: functional spec

Extracted from the reference app, version **0.5.8 (build 13)**. This document describes what the old iPhone app does so the remake can reproduce the same functionality with a new architecture and a new look. It describes behaviour, not code structure.

**Scope.** The SwiftUI app layer: app state, every screen, audio capture, the on-device and streaming transcribers, the relay client as the app uses it, pairing, persistence, Keychain, QR scanning, theme, UI tests, plist and project settings. The shared core library (models, OpenRouter client and stream parser, Meta protocol helpers, context builder, usage maths) is specified elsewhere; it is summarised here only where the app's behaviour depends on it.

**Redactions.** This file is public. The bundle identifier prefix, Keychain service name, repository URL and all test credentials are deliberately not reproduced. Where one matters it is described generically (for example "the relay base URL from pairing"). Third-party public API hosts are given because the remake needs them.

**Conventions.** Strings in "double quotes" are exact user-facing text. `code` values are exact identifiers, keys or constants. "PC" and "relay" both mean the owner's private Windows relay.

---

## 0. Orientation

### 0.1 Vocabulary

| Term | Meaning |
|---|---|
| Speech provider | Where audio is turned into text. Five options, one active at a time (§4). |
| Answer service (assistant provider) | Where Assist requests go. Two options: `codex` (via the PC relay) and `openrouter` (direct from the phone). |
| Session / conversation | One listening session from Start to Stop. Persisted to History. |
| Segment | A finalised piece of transcript with id, text, start and end seconds. |
| Partial | Not-yet-final live text. Shown, never stored. |
| Turn | One completed Assist answer saved in the session. |
| Memory | Rolling context carried between Assist requests (summary text plus a "through segment id" cursor). |
| Pairing | The relay base URL plus bearer token, saved together. |

### 0.2 The two independent choices

Speech provider and answer service are chosen independently. Whether the PC is needed follows from both.

| Speech provider (id) | Display name | Audio goes to | Live captions | Needs PC | Needs download | Needs key |
|---|---|---|---|---|---|---|
| `meta` | "Meta Muse · cloud" | Meta, directly from the phone | Yes | No | No | Meta API key |
| `nemotron` | "Nemotron 3.5 · PC" | PC relay WebSocket | Yes | Yes | No (PC-side) | Pairing |
| `qwen3` | "Qwen3 1.7B · PC" | PC relay WebSocket | Yes | Yes | No (PC-side) | Pairing |
| `voz` | "Voz · iPhone" | Stays on phone | No (text appears on Assist, Pause, Stop) | No | Yes | None |
| `parakeet` | "Parakeet · iPhone" | Stays on phone | Yes | No | Yes | None |

| Answer service (id) | Display name | Needs PC | Needs key | Streams text | End-of-session summary |
|---|---|---|---|---|---|
| `codex` | "PC · Codex" | Yes | Pairing | No | Yes |
| `openrouter` | "OpenRouter · direct" | No | OpenRouter API key | Yes | No |

Derived flags used throughout:

- **needsPC** = speech provider is `nemotron`/`qwen3` OR answer service is `codex`.
- **speechReady** = Meta: a Meta key is saved. PC-local: the PC model can start (§3.7). On-device: a model is loaded for the current provider.
- **systemReady** = speechReady AND (Codex: relay online; OpenRouter: key saved and a model id chosen).

---

## 1. Screen inventory and navigation

### 1.1 Navigation map

```
Root
├─ No active session → tab bar (Live · History · Settings)
│  ├─ Live tab: Home
│  │   ├─ "PC connection" / "Pair Windows PC" → Pairing screen ──(sheet)→ Scan PC QR
│  │   ├─ "Transcription & appearance" → Settings (same screen as the Settings tab, pushed)
│  │   ├─ "Assistant models & timing" → Assistant Lab
│  │   │     ├─ Answer service list
│  │   │     ├─ OpenRouter models picker      (OpenRouter only)
│  │   │     ├─ Reasoning picker              (Codex only)
│  │   │     └─ Run details                   (per comparison row)
│  │   └─ "Download a transcription model" → Model Library  (on-device provider with no model loaded)
│  ├─ History tab: History → Session detail
│  └─ Settings tab: Settings
│        ├─ Transcription (provider picker)
│        ├─ Model Library                     (on-device providers only)
│        ├─ Speech benchmark ("Model Lab")    (on-device providers only)
│        ├─ Assistant models & timing → Assistant Lab
│        ├─ Meta API key                      (Meta provider only)
│        └─ Pair Windows PC → Pairing screen
└─ Active session → Live session screen (full screen, no tab bar)
      └─ sliders icon → Assistant Lab and its sub-screens
Global overlay on everything: the "LiveCue" alert
```

There is no onboarding flow, no login, no splash content (blank system launch screen), no modal other than the QR sheet and the global alert.

### 1.2 Root container

- If a session is active the whole UI is the Live session screen in its own navigation stack. The tab bar does not exist.
- Otherwise a three-tab bar: **Live** (waveform icon), **History** (clock icon), **Settings** (gear icon). Each tab has its own navigation stack.
- Because the two containers are swapped, starting a session discards any pushed screens, and ending a session returns to the Live tab at its root.
- One global alert: title "LiveCue", message = the current global error message, single button "OK". Dismissing clears the message. A newer error replaces an older one.
- App-wide: dark appearance forced; accent tint is the theme mint; default body text 16 pt scaled with Dynamic Type.
- Background jobs owned by the root: relay health check every 5 s for as long as the root exists, plus one immediately whenever the app becomes active (§3.6).

### 1.3 Home (Live tab)

Navigation bar hidden. Laid out to fit one screen without scrolling; uses a compact variant when available height < 740 pt. At accessibility Dynamic Type sizes it becomes scrollable and uses the compact variant.

Top to bottom:

| Element | Content and rule |
|---|---|
| Artwork | Static decorative mark (rounded tile with five bars over faint wave lines). Not interactive, hidden from accessibility. |
| Title | "LiveCue" |
| Tagline | "A clearer conversation." |
| Description (two lines, centred) | Line 1: Meta → "Live captions with Meta Muse."; PC-local → "Live captions on your PC's GPU."; on-device → "Transcription on your iPhone." Line 2: "Tap Assist for an answer from your PC." (Codex) or "Tap Assist for an answer from OpenRouter." |
| Status pill | If not needsPC: "Cloud direct · no PC" when speech is Meta, otherwise "OpenRouter direct". If needsPC: "PC online" when relay online; else "PC reconnecting" when paired; else "PC offline". Mint when not needsPC or relay online, otherwise orange. |
| Readiness panel header | "System readiness" with "✓ Ready" when systemReady, else "Setup". |
| Readiness row "Transcription" | Streaming provider → its display name. On-device → loaded model display name ("Voz on Assist" / "Live Parakeet") or "Not configured". |
| Readiness row "Windows relay" | "Not required" if not needsPC; else "Paired" or "Pairing required". |
| Readiness row "Meta connection" (Meta only) | "Key saved · direct" (mint) or "Add API key" (orange). |
| Readiness row "PC speech model" (PC-local only) | Small spinner while the PC model is starting/stopping, then the PC model home label (§3.7). Mint when the model can start, otherwise orange. |
| Setup row 1 → Pairing screen | Title "PC connection" if paired else "Pair Windows PC". Detail: "Optional for current setup" if not needsPC; else if paired "Saved · connected" / "Saved · reconnecting"; else "Required". |
| Setup row 2 → Settings | Title "Transcription & appearance". Detail: PC model home label when PC-local, else "Cloud, PC or iPhone speech". |
| Setup row 3 → Assistant Lab | Title "Assistant models & timing". Detail: selected assistant model id, or "Choose an OpenRouter model" when empty. |
| Primary action | See below. |

Primary action:

- On-device provider with no model loaded: a link styled as the primary button, "Download a transcription model", to Model Library.
- Otherwise a button "Start conversation" (record icon left, arrow right). Reads "Connecting…" while a session transition is in progress. Disabled while transitioning, and for PC-local providers whenever the PC model cannot start.

Start is **not** gated on systemReady. Missing prerequisites surface as errors after the tap (§2.3).

While Home is visible and the provider is PC-local, the PC model status is polled every 2 s.

### 1.4 Live session screen

Full screen, navigation bar hidden, no tab bar. Five stacked regions; only the answer feed scrolls, the controls stay pinned.

**(a) Header.** Waveform icon, "LiveCue", and a sliders icon button (accessibility label "Assistant models & timing") that pushes Assistant Lab. The button is disabled while an Assist is running.

**(b) Status panel.**

| Element | Rule |
|---|---|
| Dot | Orange when paused, mint otherwise. |
| State label | "Updating…" while transitioning; else "Paused"; else "Listening". |
| Mini level meter | Five bars driven by microphone RMS. Each bar height = 4 + clamp(level × 12, 0…1) × {10, 16, 22, 14, 8} pt. Flat (4 pt) while paused. Eases over 0.12 s. Hidden from accessibility. |
| Elapsed | `MM:SS`, monospaced digits. Wall-clock seconds since the session was created; keeps counting while paused. |
| Cost (Meta only) | Estimated transcription cost, `$0.00000` format plus " USD" (for example "$0.00015 USD"). Absent for every other provider. |
| Provider line | Speech provider display name; Meta appends " · est. transcription". |
| Model line | Last path component of the assistant model id (text after the final "/"), or "Choose model" when empty. |

**(c) Live transcript strip.** A single tappable header "Live transcript" with a chevron (accessibility label "Expand live transcript" / "Collapse live transcript").

- Collapsed (default): a two-line preview. Shows the partial text if non-empty; else the last final segment's text; else the placeholder "Recording locally. Tap Assist to transcribe." (Voz) or "Listening for speech…" (others).
- Expanded: a scroll area of fixed height = min(180 pt, 24% of screen height) listing every final segment, then the partial text in secondary colour. Auto-scrolls to the bottom when it appears, when a segment is added and when the partial changes. Toggle animates over 0.2 s.

**(d) Answer feed** (fills remaining height, scrolls). Newest first:

1. **Error card** (when an Assist failed): heading "Assist could not finish", the error text, "No automatic retry. Any partial answer below is incomplete.", and a button "Retry same text" (disabled while assisting or when no previous request exists).
2. **In-progress card** (while assisting, or while leftover streamed text exists): stage label (§3.4) or "Incomplete answer" if an error is present, a spinner while assisting, and the streamed text so far (selectable).
3. **Answer cards**, newest first (§1.5).
4. **Empty state** (no turns, not assisting, no error): "Your assistant" / "Tap Assist when you want help with a question. Answers appear here while the conversation keeps going."

The feed scrolls to the top when a new answer lands, when an Assist starts and when an error appears.

**(e) Controls.**

| Control | Label / icon | Action | Disabled when |
|---|---|---|---|
| Pause / Resume | pause or play icon; accessibility label "Pause" / "Resume" | Toggle pause (§3.1) | assisting or transitioning |
| Assist | "Assist"; while running "Replying…" with spinner | Run an Assist (§3.4) | assisting or transitioning |
| Stop | stop icon, replaced by a spinner while transitioning; accessibility label "Stop conversation" | End the session | assisting or transitioning |

There is no instruction field, no dismissible answer sheet and no cancel button on this screen.

### 1.5 Answer card and timing view

Each saved turn renders as a card:

- Header "Assistant", plus a "Latest" badge on the most recent answer of this session.
- The detected question, unless it equals the sentinel "Latest conversation" (which OpenRouter answers always use).
- The answer text (selectable).
- If details are non-empty: a toggle "More detail" / "Hide detail" revealing the details text.
- If the turn has timing data: total time (`0.00 s`), then for answers with a reported cost "· $0.000000"; a copy button (accessibility label "Copy answer") that copies the answer text to the clipboard with no confirmation; and a collapsed disclosure "Timing & usage" containing the timing view.
- On the latest card only: "Retry same text with selected model" (disabled while assisting or when no previous request exists).

**Timing view** ("Response timing"):

| Row | Value |
|---|---|
| Caption | `<model actually used> · <reasoning effort actually used>` (from the execution record if present, otherwise the configured values) |
| "Tap → answer" | total, `0.000 s` |
| "Transcription after tap" or "Transcription (reused)" | time spent transcribing after the tap (non-zero only for Voz on a fresh request) |
| "Context + model check" | time to build the request and check the model catalogue |
| "PC round trip" or "OpenRouter round trip" | network round trip |
| OpenRouter only: "Reported request cost" | `0.000000 credits` or "Unavailable"; plus "`N` input · `M` output tokens" when usage was reported |
| Codex with execution data: "PC: Codex CLI call", "PC: relay overhead", "Transfer / client estimate" | PC-side timings; transfer estimate = max(0, round trip − relay total) |
| Codex without execution data | "PC timing unavailable. Restart the updated desktop relay." (orange) |
| Parakeet only: "Last live STT chunk" | processing time of the last live audio buffer |
| Caption | `<speech provider id> · <N> transcript characters` plus " · same-text retry" on retries |
| Footnote | Explanatory text S-20 (Codex) or S-21 (OpenRouter), §8.4 |

### 1.6 Assistant Lab

Title "Assistant Lab". Reached from Home, Settings and the live session header. Loads the model catalogue for the current answer service every time it appears.

**Section "Answer service".** A row "Provider" showing the current service name; opens a list titled "Answer service" with both services and a checkmark on the current one. Tapping one switches service (§3.8) and reloads the catalogue; the list does not close itself. Disabled while assisting.

**Section "OpenRouter API key"** (OpenRouter only; disabled while assisting):

- Secure field, placeholder "Paste API key".
- "Save key" (disabled while the field is empty). On success the field is cleared and the keyboard dismissed.
- State text "Key saved" / "No key saved".
- When a key exists: "Verify key (no inference)" (disabled while verifying) and destructive "Remove key".
- A message line (results of save/verify/remove, §8.2).
- Link "Get an OpenRouter API key" → `https://openrouter.ai/keys`.
- Disclosure text S-10.

**Section "PC assistant" / "OpenRouter assistant"** (disabled while assisting):

- The selected model id as a headline (empty when none chosen).
- OpenRouter: a row "Choose a model" / "Change model" → OpenRouter models picker; caption "Uses the selected model's default reasoning behavior."
- Codex: "Reasoning: `<effort>`"; then one row per catalogue model (name, checkmark on the selected one). Tapping a model selects it and keeps the current reasoning effort if that model supports it, otherwise switches to the model's first listed effort (fallback "low"). If the selected model is in the loaded catalogue, a row "Reasoning" (value capitalised) opens the Reasoning picker.

**Unnamed section.**

- "Refresh PC model list" / "Refresh OpenRouter models" with a spinner while loading; disabled while loading.
- Catalogue message line (§8.2).
- Explanatory text S-11 (Codex) or S-12 (OpenRouter).

**Section "Recent comparisons".** The 50 most recent turns that have timing data, across the active session and all saved sessions, newest first. Empty text: "Assist once to start collecting results." Each row: model used; "`<speech id>` · `<effort>` · same text|fresh transcript"; "`0.00 s total` · `<service name>`"; the detected question (two lines). Tapping opens **Run details**: detected question, answer, timing view.

**OpenRouter models picker** ("OpenRouter models"). Searchable list filtered by case-insensitive match on name or id. Each row: name (checkmark if selected), id, "Input `$0.000 / 1M` · output `$0.000 / 1M`" (catalogue per-token price × 1,000,000; "Unknown" if missing, non-numeric, negative or non-finite). Tapping selects the model with reasoning effort `default` and closes the picker. Empty overlay: "No models" / "Refresh the catalog or change your search."

**Reasoning picker** ("Reasoning"). The selected model's efforts in catalogue order; `none` is labelled "None (no reasoning)", others capitalised; checkmark on the current one. Tapping selects and closes.

### 1.7 Settings

Title "Settings". Shown as a tab and also pushed from Home.

**Section "Transcription & AI"**

- Row "Transcription" (value = current provider display name) → provider picker. Disabled while a session is active, while an on-device model is being prepared, while a PC model command is in flight, or while the PC model is starting/stopping.
- On-device providers only: "Model Library" and "Speech benchmark".
- "Assistant models & timing" → Assistant Lab.
- Provider explanation: S-01 (Meta), S-02 (PC-local) or S-03 (on-device).
- Meta only: row "Meta API key" with value "Saved on iPhone" / "Required" → Meta API key screen.

**Section "PC model controls"** (PC-local providers only):

- Spinner while changing; state text = PC model state capitalised ("Stopped", "Starting", "Ready", "Stopping", "Error") or "Unavailable" when unknown; while starting/stopping also "`N` s" elapsed.
- If a different PC model is loaded: "Currently loaded: `<id>`. Start switches to your selected model when it is idle."
- The PC model message (from the relay, or a local error, §8.2).
- "Start model" — disabled when locked or already ready for the selected provider.
- "Stop model" — disabled when locked, when status is unknown, when state is `stopped`, or when the loaded model is not the selected provider.
- "Refresh status".
- Caption: during a session "End the conversation before starting or unloading a model."; otherwise S-04.
- *Locked* = a command is in flight, or the PC reports starting/stopping, or a session is active, or the PC reports the model busy, or the phone is not paired.
- Status is polled every 2 s while this section is visible.

**Section "Assistant instructions"**

- Multi-line text field (2 to 5 lines), placeholder "Optional instruction (e.g. answer briefly)". Saved on every keystroke.
- Caption "Applied to every Assist request until you change it."
- Keyboard accessory button "Done" dismisses the keyboard.

**Section "Transcription cost"**

- "Meta Muse rate" → "$0.18 / hour"; "PC-local models" → "$0 API usage".
- Explanation S-05.
- Link "Meta pricing & usage documentation" → `https://dev.meta.ai/docs/speech-to-text#pricing`.

**Section "Display"**

- "Text size" → "16 pt · follows Dynamic Type"
- "Appearance" → "Midnight Mint" (old theme name; see §10)
- "Version" → the app's marketing version (fallback literal "0.5.1" if unreadable)

**Section "Devices"** — "Pair Windows PC" → Pairing screen.

**Section "Privacy"** — text S-06.

**Provider picker** ("Transcription"). Five rows, checkmark on the current one, tap selects and closes:

| id | Row label |
|---|---|
| `meta` | "Meta Muse · live cloud" |
| `nemotron` | "Nemotron 3.5 · PC live" |
| `qwen3` | "Qwen3 1.7B · PC live" |
| `voz` | "Voz · iPhone on Assist" |
| `parakeet` | "Parakeet · iPhone live" |

Footer: "PC models run one at a time on your GPU. Start a new conversation to compare them. Neither sends audio to Meta."

### 1.8 Meta API key

Title "Meta API key". Reached from Settings when the provider is Meta.

**Section "Meta Muse · direct cloud"** (whole section disabled during a session):

- Secure field, placeholder "Paste Meta API key".
- "Save key" (disabled while empty or while a connection test runs). On success the field is cleared and the keyboard dismissed.
- State text "Key saved" / "No key saved".
- When a key exists: "Test connection (no audio)" (reads "Connecting…" and is disabled while running) and destructive "Remove key" (disabled while testing).
- Message line (§8.2).
- Link "Open Meta developer dashboard" → `https://dev.meta.ai/`.

**Section "How it works"** — texts S-07, S-08, S-09.

### 1.9 Pairing screen and QR sheet

Title "PC connection" when paired, otherwise "Pair Windows PC". On appearing, the manual address field is pre-filled with the saved relay address.

**Section "Saved PC"** (paired only):

- State: "Connected" (mint, check icon) when relay online; else "Pairing needs attention" if the relay rejected the token; else "Reconnecting" (orange, circular-arrows icon).
- The saved relay address, monospaced and selectable.
- The connection message, or by default "Your pairing is saved. The app reconnects automatically after restarting."
- "Reconnect" (reads "Checking…" and is disabled during a check) → immediate health check.
- "Pair another PC or scan a code" / "Hide pairing options" → toggles the two sections below.

**Section "Quick pairing"** (shown when not paired, or when toggled open):

- "Scan PC QR code" → opens the QR sheet.
- "Start LiveCue Relay on Windows. Paste the pairing JSON shown by the launcher, or enter the values manually."
- A monospaced multi-line text box for pasted pairing JSON.
- "Read pairing payload" → parses the box (§2.2).

**Section "Manual pairing"** (same visibility):

- Address field (URL keyboard, no auto-capitalisation). Placeholder is an example HTTPS address in the private-network DNS form `https://<pc-name>.<network>.ts.net`.
- Secure field "Pairing token".
- "Verify and pair" (disabled while either field is empty).
- When relay online: "PC connected".

**Section "Privacy"** — text S-13.

**QR sheet.** Title "Scan PC QR", toolbar button "Cancel". Full-bleed camera preview (aspect fill, black background) with a bottom caption "Point at the QR in LiveCue Desktop". Detects QR codes only. The first decoded string closes the sheet and is handled as a pasted payload. Failures close the sheet and raise the global alert (§8.1).

### 1.10 Model Library

Title "Model Library". Lists the on-device model for the **current** provider only (so exactly one entry; nothing for streaming providers).

- Intro: "Prepare the model for this tab once. The download is cached on your iPhone. Initial preparation can take longer than subsequent loads."
- Section "English models": for the entry —
  - Display name, a "RECOMMENDED" tag, "~`N` MB · `<description>`", and a green check when it is the loaded model.
  - "Download & use" (reads "Reload" once loaded); disabled while a preparation started from this screen is running. A spinner shows while running.
  - "Unload" (only when loaded; disabled while preparing).
  - Once any preparation has started in this app run: a progress bar; "`N`% setup" and "`MM:SS` elapsed" (ticks every second until finished, then frozen); the status line; a byte or file counter when available; and "Downloading and model preparation are separate steps; preparation can continue after the download reaches 100%."
- Section "Pinned source": the status line again, and "Package" → "Desert Ant 3.1.0" (Voz) or "FluidAudio 0.15.6" (Parakeet).

| Catalogue entry | Display name | Approx. size | Description |
|---|---|---|---|
| `voz` | "Voz on Assist" | 467 MB | "Transcribes when you tap Assist" |
| `parakeet` | "Live Parakeet" | 230 MB | "English live captions · 320 ms mode" |

### 1.11 Speech benchmark ("Model Lab")

Title "Model Lab". Reached from Settings → "Speech benchmark" (on-device providers only). A manual word-error-rate worksheet:

- "Purpose": "Record the same short phrase with different models, then compare processing time and word error rate. This first build provides the scoring worksheet; live benchmark capture uses the current session transcript."
- "Reference phrase": editable, default "The quick brown fox jumps over the lazy dog".
- "Model transcript": editable, default empty.
- "Word error rate": computed live as a percentage with one decimal (word-level edit distance ÷ reference word count, case-insensitive, punctuation ignored).
- "Use latest transcript": fills the model transcript with the active session's segments joined by spaces.
- "Interpretation": "Lower is better. Test in the same room and speak the same phrase for a fair comparison."

Nothing here is saved. See §11 — the "Use latest transcript" button cannot do anything useful in the old navigation.

### 1.12 History and Session detail

**History** (title "History"). All stored sessions, newest start time first.

- Empty state: "No conversations yet" / "Completed sessions will appear here."
- Row: title (two lines max); start date and time (abbreviated date, short time, device locale); "`N` transcript segments · `M` assists"; and, if the session recorded usage, a usage label:
  - Meta: "`Meta Muse · cloud` · `$0.00000` USD" plus " · incomplete" when applicable.
  - Any other provider: "`<provider name>` · Local" plus " · incomplete" when applicable. Never shows a currency amount.
  - Sessions saved without usage data show no usage label at all (no fabricated zero).
- Swipe to delete removes the session permanently, without confirmation.
- Tap → Session detail.

**Session detail** (title = session title). Sections, each only if it has content:

- "Transcription usage" (if usage was recorded): Meta → "Estimated cost"; always "Provider" (provider name, fallback "On-device"); if known "First words from stream start" and "Last stream finalization" (`0.00 s`); Meta → "Processed audio reported" (`N seconds`), "Streams" (count), "Rate saved with session" (`$0.18 / hour`), and caption S-14 (complete) or S-15 (incomplete).
- "Summary", "Key points", "Action items" (from the end-of-session summary, Codex only).
- "Transcript": every segment.
- "Assistant": for each turn, the detected question, the answer, and details if any.

Read-only. No export, share, search, rename or resume.

### 1.13 Accessibility identifiers used by the UI tests

The remake's UI tests need equivalent hooks. These are the identifiers the old tests rely on.

| Identifier | Element |
|---|---|
| `start-session`, `download-model` | Home primary action (two variants) |
| `pair-pc`, `transcription-settings`, `assistant-lab` | Home setup rows (`pair-pc` and `assistant-lab` are reused in Settings; `assistant-lab` also on the live header) |
| `home-meta-state`, `home-pc-model-state` | Home readiness values |
| `expand-transcript`, `transcript-preview`, `live-transcript` | Transcript strip toggle, collapsed preview, expanded scroll area |
| `live-transcription-cost` | Live cost label |
| `assistant-feed` | Answer feed scroll area |
| `assist-error`, `retry-failed-assist` | Error card text and retry button |
| `streaming-answer` | In-progress text |
| `assistant-answer`, `previous-assistant-answer` | Latest and older answer texts |
| `timing-disclosure`, `previous-timing`, `response-timing` | Timing disclosure (latest / older) and timing view |
| `retry-same-text` | Retry on latest card |
| `pause-session`, `assist-button`, `end-session` | Session controls |
| `speech-provider`, `provider-<id>` | Settings provider row and picker rows |
| `meta-key-settings`, `meta-key`, `save-meta-key`, `meta-key-state`, `verify-meta-key`, `remove-meta-key`, `meta-key-message` | Meta key |
| `pc-model-state`, `pc-model-message`, `start-pc-model`, `stop-pc-model`, `refresh-pc-model` | PC model controls |
| `assistant-instruction`, `finish-instruction` | Instruction field and keyboard "Done" |
| `assistant-provider`, `assistant-provider-<id>` | Answer service row and list rows |
| `openrouter-key`, `save-openrouter-key`, `openrouter-key-state`, `verify-openrouter-key`, `remove-openrouter-key` | OpenRouter key |
| `selected-assistant-model`, `openrouter-model-picker`, `choose-<model id>`, `reasoning-picker`, `effort-<effort>` | Model and reasoning selection |
| `saved-pc-state`, `saved-pc-address`, `reconnect-pc`, `show-pairing-options`, `scan-pairing-qr` | Pairing screen |
| `model-download-progress` | Model Library progress bar |
| `history-transcription-cost`, `history-local-transcription` | History usage label (Meta / other) |

The tests also assert that no element `dismiss-answer` exists (the older dismissible answer sheet must not return).

---

## 2. User flows

### 2.1 First launch

1. App opens on Home. Defaults: speech provider `meta`, answer service `codex` with model `gpt-5.6-sol` and reasoning `low`, empty instruction, nothing paired, no keys.
2. Home shows pill "PC offline", readiness "Setup", "Windows relay: Pairing required", "Meta connection: Add API key", setup row "Pair Windows PC — Required".
3. No permission prompt appears at launch. Microphone is requested at the first session start, camera at the first QR scan. The system may show its local-network prompt the first time the relay is contacted.
4. The user completes whichever prerequisites their chosen combination needs: pair the PC (§2.2), save a Meta key (§2.6), save an OpenRouter key and pick a model (§2.7), or download an on-device model (§2.8).

### 2.2 Pairing with the PC

Three entry paths end in the same two fields (address, token); pairing itself is always the explicit "Verify and pair" tap.

1. **QR scan.** "Scan PC QR code" → camera permission (first time) → point at the desktop QR → the decoded text is treated as pasted pairing JSON.
2. **Paste.** Paste pairing JSON into the box → "Read pairing payload".
3. **Manual.** Type the address and token.

Reading a payload (paths 1 and 2): the text must be JSON `{"endpoint": "...", "token": "..."}` that passes validation (below). On success the two manual fields are filled and the paste box is cleared — **nothing is saved or sent yet**. On failure the global alert shows the payload error (§8.1).

"Verify and pair":

1. Trim the address; append "/" if it does not end with one. The token is used exactly as typed.
2. Validate: total payload ≤ 8192 bytes; address scheme `https`; non-empty host; no user or password; no query or fragment; path empty or "/"; token length 16 to 512 characters with no whitespace.
3. Call the relay's pair-verify endpoint with the token (§6.1).
4. On success: save the pairing (file + Keychain, §5.3), remember the address, mark the relay online, clear any "rejected" flag, set the connection message to "Connected. Your pairing is saved on this iPhone." The screen switches to the "Saved PC" layout.
5. On failure: global alert with the categorised connection message (§8.1). Any previous pairing is left untouched.

Afterwards:

- The pairing is restored on every launch. The file copy is preferred; if it is missing the Keychain copy is used; if both are missing a legacy token-only Keychain item is migrated when present.
- Health is checked every 5 s and on every return to foreground while the PC is needed. The saved-PC state shows "Connected", "Reconnecting" (network/TLS problems) or "Pairing needs attention" (relay answered 401).
- There is **no unpair / forget action**. Pairing again overwrites.

### 2.3 Starting a session

Tap "Start conversation". The button shows "Connecting…" and is disabled. Checks run in this order; the first failure raises the global alert and nothing else happens:

| # | Applies to | Check | Failure message |
|---|---|---|---|
| 1 | On-device | A model is loaded for the provider | "Choose and download a transcription model first." (normally unreachable; the button is replaced by the download link) |
| 2 | Meta | A Meta key is in the Keychain | Meta missing-key message (§8.1) |
| 3 | PC-local | Refresh PC model status; model must be able to start | "PC speech model: `<home label>`. Open Transcription settings to start it or check its status." |
| 4 | PC-local | Paired; relay online (one extra health check is attempted if offline) | Not-paired message, or the current connection message |
| 5 | All | Microphone permission granted (system prompt on first use) | "Microphone permission is required for live transcription." |

Then:

6. A new session is created (title "New conversation"), the answer feed, errors, retry state and elapsed timer are reset, usage tracking is reset for the provider, and the 1-second session tick starts. The UI switches to the Live session screen immediately, showing "Updating…" with controls disabled.
7. The transcriber starts (§3.2). For Meta and PC-local this means connecting and completing the provider handshake **before** the microphone is opened; for PC-local the relay may hold the connection in a "loading" state for a long time (up to 180 s).
8. On success the screen shows "Listening". On failure the session is discarded, the UI returns to the tabs and the alert shows the error (typically "Transcription could not start. …").

### 2.4 During a session

- **Live transcript.** Behaviour depends on the provider (§3.3). Meta and PC-local show a running partial that is replaced as it grows and is committed as a segment at the end of each spoken turn. Parakeet shows a running partial and commits only when recording pauses or stops. Voz shows the placeholder until Assist, Pause or Stop triggers a batch transcription.
- **Pause / Resume.** Pause finalises what has been heard (§3.1) and shows "Paused". Resume starts recording again; for Meta and PC-local this is a brand-new stream. The elapsed clock does not stop.
- **Assist.** Tap "Assist" (§3.4). The answer appears as the newest card in the feed. For OpenRouter the text streams in; for Codex it arrives whole. Listening continues during an Assist; Pause and Stop are disabled until it finishes.
- **Changing the assistant mid-session.** The sliders icon opens Assistant Lab; a new model, effort or service applies to the next Assist and to "Retry same text with selected model".
- **Retry.** "Retry same text" (after a failure) or "Retry same text with selected model" (on the latest answer) re-sends the previous request's exact text with a new request id and the currently selected assistant.
- **Instruction.** Whatever is in Settings → Assistant instructions at the time of the request is sent. It cannot be edited from the live screen.
- **Not changeable mid-session:** speech provider, Meta key, PC model start/stop, on-device model load.

### 2.5 Stopping and summaries

Tap Stop:

1. The transcriber is finalised exactly as for Pause (errors raise the alert but do not block ending).
2. Usage is captured, audio is fully stopped, the tick stops.
3. The session gets an end time and a title: the first 48 characters of the first segment, or "Conversation" if there are no segments.
4. It is saved, History is refreshed, and the UI returns to the tabs immediately.
5. **Codex only (and only when paired):** a summary request is sent in the background (§6.1). If it succeeds and the session still exists, the session title is replaced by the summary's title and its summary, key points and action items are stored and appear in Session detail. Any failure is silent; there is no retry and no indicator.
6. **OpenRouter:** no summary request is ever made.

### 2.6 Meta key

Settings → Meta API key (provider must be Meta).

- **Save**: trimmed; rejected if empty or containing whitespace. Stored in the Keychain only. Message "Key saved securely on this iPhone. Meta connects directly; no PC needed."
- **Test connection (no audio)**: opens the Meta realtime socket, authenticates, immediately ends the stream. No microphone is touched. 15 s limit. Message "Meta connection verified directly. No microphone audio was sent." or an error.
- **Remove**: deletes the Keychain item. Message "Key removed."
- All three are refused while a session is active.

### 2.7 Answer service, OpenRouter key and model choice

- Switch service in Assistant Lab → Provider. Each service remembers its own model and effort; switching back restores them.
- **Codex**: catalogue comes from the PC. Pick a model, then optionally a reasoning effort from that model's list.
- **OpenRouter**: save a key (rejected if empty or containing whitespace → "Enter a valid API key."), optionally "Verify key (no inference)", then pick a model from the searchable catalogue. Effort is always `default`.
- Removing the OpenRouter key makes the next Assist fail inline; there is no fallback to Codex.

### 2.8 On-device models

1. Settings → Transcription → choose Voz or Parakeet. Home now shows "Download a transcription model".
2. Model Library → "Download & use". Progress, elapsed time and status are shown. Download and preparation are distinct phases; the bar can sit at 100% while preparation continues.
3. When finished the entry shows a check, the button reads "Reload", and Home shows "Start conversation".
4. "Unload" releases the model from memory (downloaded files stay cached) and Home returns to the download link.
5. **The loaded state is not restored on relaunch, and is cleared whenever the provider is changed.** After either, the user must open Model Library and tap "Download & use" again (fast, from cache).

### 2.9 PC speech models

1. Settings → Transcription → Nemotron or Qwen3. "PC model controls" appears.
2. "Start model" asks the relay to load the model. State goes "Starting" (with elapsed seconds) → "Ready", or "Error" with a message; Start is available again to retry.
3. Home mirrors this ("Loading · `N`s" → "Ready") and enables Start conversation only when ready, not in use, and the relay is online.
4. "Stop model" unloads it ("Stopping" → "Stopped").
5. Only one PC model is loaded at a time. If the other one is loaded Home shows "Different model loaded"; Start model switches when idle.

### 2.10 History

Browse, open and swipe-delete sessions (§1.12). A session interrupted by the app being killed is still present, because sessions are saved continuously (§3.9); it keeps the title "New conversation".

### 2.11 Comparison features ("Assistant Lab", transcriber comparison)

There is no side-by-side mode. Nothing runs two transcribers or two assistants at once. "Comparison" in the old app means three things:

1. **Assistant comparison.** Every answer stores which model, effort and speech provider produced it and how long each stage took. "Retry same text with selected model" re-asks the identical text with a different selection. Assistant Lab → "Recent comparisons" lists the results across sessions.
2. **Speech provider comparison.** Done by hand: end the conversation, switch provider, start a new one. History records per-session provider, time to first words and finalisation time.
3. **Word-error-rate worksheet.** The "Speech benchmark" screen (§1.11), manual input only.

### 2.12 Error recovery summary

| Situation | What the user sees | Recovery |
|---|---|---|
| Stream drops, provider error, upload cannot keep up (Meta / PC-local) | Session auto-pauses; global alert with the reason | Tap Resume → new stream. No automatic reconnect. |
| Phone call or other audio interrupts (Meta / PC-local) | Auto-pause + alert "Microphone interrupted. Resume when your call or other audio finishes." | Tap Resume when ready. Not resumed automatically. |
| Microphone device removed (Meta / PC-local) | Auto-pause + alert "Microphone route changed. Resume to use the current microphone." | Tap Resume. |
| Assist fails or is cut short | Inline error card; any streamed text stays visible labelled "Incomplete answer"; nothing saved | "Retry same text", or tap Assist again for a fresh request. |
| PC unreachable | Pill "PC reconnecting"; saved-PC "Reconnecting" with message | Automatic re-check every 5 s; "Reconnect" button. |
| Relay rejects token | Saved-PC "Pairing needs attention" | Scan the PC's QR and pair again. |
| PC model failed to load | State "Error" + message; Home "Error · open Settings" | "Start model" again. |
| On-device model preparation fails | Alert with the library's error; status "Preparation failed" | "Download & use" again. |
| Finalising on Pause/Stop fails | Alert "Could not finalize the transcript. Some final words may be missing." | None needed; session continues or ends. |
| Voz buffer reaches 30 minutes without transcription | Alert "Pending recording reached 30 minutes. Tap Assist to transcribe it."; capture stops | Assist, then Pause and Resume to restart capture (§11). |

---

## 3. State and logic in the app layer

### 3.1 Session state machine

States are derived from four flags: *active session exists*, *paused*, *transitioning*, *assisting*.

| State | Flags | Live label | Notes |
|---|---|---|---|
| Idle | no session | — | Tabs visible. |
| Starting (pre-checks) | no session, transitioning | Home button "Connecting…" | Checks 1–5 of §2.3. |
| Starting (connecting) | session, transitioning | "Updating…" | Live screen visible, controls disabled, elapsed clock already running. |
| Listening | session, not paused | "Listening" | |
| Paused | session, paused | "Paused" | By user, or automatically after a stream failure. |
| Pausing / Resuming / Ending | session, transitioning | "Updating…" | Controls disabled. |
| Assisting | overlay on Listening or Paused | (unchanged) | Assist button "Replying…"; Pause, Stop and the header link disabled. |

Every user action is single-flight:

| Action | Allowed when | Effect |
|---|---|---|
| Start | Idle, not transitioning | §2.3 |
| Pause | session, not assisting, not transitioning | Finalise the transcriber (below), mark paused, capture usage. A finalise error raises the alert; the session stays paused. |
| Resume | same | Set the stream time offset to the current elapsed seconds, start the transcriber again, capture usage. On error: stay paused, raise the alert. |
| Assist | session, not assisting, not transitioning | §3.4 |
| Stop | session, not assisting, not transitioning | §2.5 |
| (automatic) stream failure | streaming provider, any time | Tear down audio and socket, mark paused, raise the alert. |

What "finalise" does on Pause and Stop:

| Provider | Finalise behaviour |
|---|---|
| Meta, PC-local | Stop capture; flush queued audio including a final short frame; send `{"type":"endStream"}`; wait for the server to finish and close. Hard limit 15 s, after which the socket is cancelled. If the end message cannot be sent: error "Could not finalize the transcript. Some final words may be missing." Then close everything and deactivate the audio session. |
| Parakeet | Stop capture; let queued buffers finish processing; ask the recogniser for its final text; if non-empty commit it as **one** segment; clear the partial. Audio session stays active. |
| Voz | Stop capture; let queued buffers drain; transcribe all pending audio (same as on Assist). Audio session stays active. |

There is no cancellation of an in-flight Assist and no cancellation of Start once connecting, other than the timeouts in §3.6.

### 3.2 Audio capture pipeline

Common to all providers:

- Audio session: category play-and-record, mode **measurement** (no voice processing), options **default to speaker** and **allow Bluetooth hands-free**. Activated when capture starts. Deactivated (notifying other apps) on full stop.
- Capture taps the hardware input in its native format with a requested buffer of **2048 frames**, and converts each buffer to the provider's target format.
- No preferred sample rate or I/O buffer duration is requested. No audio is ever written to disk.
- If the input reports a zero sample rate (or zero channels, on-device path) capture fails with "No usable microphone."

| | Meta | PC-local | On-device (Voz, Parakeet) |
|---|---|---|---|
| Target format | 16-bit signed PCM, mono, **24 000 Hz** | 16-bit signed PCM, mono, **16 000 Hz** | 32-bit float, mono, **16 000 Hz** |
| Network framing | **80 ms** frames = 3840 bytes, binary WebSocket messages | **80 ms** frames = 2560 bytes | none |
| Hand-off queue bound | 64 converted buffers; when full, new audio is refused | same | 128 converted buffers |
| Queue overflow | Stream failure: "Network cannot keep up. Recording paused; resume to retry." (auto-pause) | same | Alert "Transcription could not keep up. Stop and restart the session." Nothing is stopped or paused. |
| Send failure | Stream failure: "Audio connection failed. Resume to reconnect." | same | n/a |
| Microphone opens | Only after the provider handshake succeeds | Only after the relay says `ready` | Immediately |
| Level meter source | RMS of each sent frame (samples ÷ 32768) | same | RMS of each converted buffer |
| Interruption began | Stream failure: "Microphone interrupted. Resume when your call or other audio finishes." | same | **Not observed** |
| Route change (previous device unavailable) | Stream failure: "Microphone route changed. Resume to use the current microphone." | same | **Not observed** |
| Interruption ended / new device attached | Not handled; user resumes manually | same | Not handled |

Background behaviour: the app declares the **audio** background mode and does nothing else for background operation. The intent is that, with an active recording session, capture, streaming, the 1-second tick and relay polling continue when the app is backgrounded or the phone is locked. No test covers this, and the old release notes say real-device microphone behaviour was still to be checked. There are no lock-screen controls, no Now Playing entry, no Live Activity, and the screen idle timer is not disabled. A paused Meta/PC-local session has released the audio session, so iOS may suspend the app in the background.

### 3.3 Transcript segmentation, merging and finalisation

| | Meta / PC-local (streaming) | Parakeet | Voz |
|---|---|---|---|
| Partial text | Cumulative per spoken turn: each `transcript` event replaces the text of the turn most recently started. If several turns are open their partials are joined with a space in turn-number order. | Whatever the recogniser's partial callback last reported. | None. |
| Segment committed | On `speechComplete` for a turn: text trimmed; empty text is dropped; the turn's partial is removed. Repeated or late events for a completed turn are ignored. | Only on Pause/Stop: the recogniser's final text for the whole recording stretch becomes one segment. | On Assist, Pause and Stop: one segment per batch transcription with new words. |
| Segment times | start = processed-audio time at `speechStart`; end = processed-audio time at `speechComplete`; both in seconds, plus the **stream offset** (elapsed session seconds when this stream was started; 0 for the first). | start 0, end 0 | start 0, end = duration of the transcribed audio |
| Ordering | After every commit the session's segments are re-sorted by start time (turns can complete out of order). | Append order. | Append order. |
| On disconnect | Any uncommitted partial stays on screen until Resume, then is discarded. It is never committed. | — | — |

Voz batch details:

- Captured audio accumulates in memory. Hard cap **30 minutes** of pending audio (16 000 × 60 × 30 samples); on reaching it capture stops and the alert in §2.12 is raised.
- A batch transcribes everything pending. Only words whose end time is after the previous cursor are kept, joined with spaces.
- Afterwards the buffer is trimmed to its last **2 seconds** (32 000 samples) as overlap and the cursor is set to the end of that overlap, so the next batch has leading context without repeating words.
- A batch whose result arrives after capture was stopped or restarted is discarded.

All committed segments are final. Nothing merges or edits a segment after commit.

### 3.4 Assist requests

**Trigger.** Only a manual tap: the Assist button, or one of the two retry buttons. There is no automatic assist, no question detection on the phone, no debounce and no rate limit beyond "one at a time".

**Pipeline** (fresh request):

1. Clear the previous error and any leftover streamed text.
2. Preconditions, reported inline (not as an alert): Codex without a pairing → not-paired message; OpenRouter without a key → OpenRouter missing-key message.
3. Mark assisting. Snapshot the current assistant configuration; that snapshot is used for the whole request.
4. Stage "Transcribing…" (Voz) or "Thinking…" (others). For Voz, run a batch transcription now (§3.3) and time it.
5. Build the request from the session (§3.4.1). If both the transcript and the partial are empty → inline error "No speech was recognized yet."
6. Stage "Checking model…".
   - Codex: fetch the PC catalogue **every time**. A network error ends the Assist with that error.
   - OpenRouter: use the already loaded catalogue; fetch it only if it is empty.
   - The selected model id with the selected effort must be present. If not: OpenRouter → "Choose an OpenRouter model in Assistant settings first."; Codex → "`<model>` / `<effort>` is not in this PC's current catalog. Open Assistant models & timing and choose an available combination. Your selection has not changed." The selection is never changed automatically.
7. Stage "Thinking…". Remember this request for retries. Remember the id of the session's last segment as the "through" cursor.
8. Send (§6). OpenRouter streams: each update replaces the streamed text and sets stage "Replying…"; updates are ignored if the session has changed. Codex returns one complete response.
9. Codex only: the response must carry an execution record whose model and effort equal the snapshot; otherwise fail with "The PC did not confirm the selected model. Restart the updated LiveCue Desktop app, then retry."
10. Success: build a turn (instruction used, detected question, answer, details, timing record), append it, replace the session memory with the response's memory, mark it latest, clear streamed text, save the session. If the session ended or changed meanwhile, the result is dropped silently.
11. Failure at any step after 3: the error text goes to the inline error card. Streamed text is left in place and labelled "Incomplete answer". No turn is saved; memory and context are not advanced.
12. Always: clear assisting and the stage.

**Retry** ("reuse text"): skips step 4; reuses the remembered request (transcript, partial, instruction, memory) with a **new request id** and the **current** assistant configuration; does not move the "through" cursor; records the timing as "reused". If nothing is remembered it behaves like a fresh request.

#### 3.4.1 Request contents (built by the core library)

- Final segments from two before the first segment not yet covered by memory, through the end, joined with newlines, truncated to the **last 80 000 characters**. "Covered by memory" means up to and including the segment whose id equals the memory's through-segment id; if that id is unknown or absent, everything is sent.
- The current partial text, trimmed, omitted if empty.
- The instruction, trimmed, omitted if empty.
- The current memory, the session id, a fresh request id, and the assistant configuration.

#### 3.4.2 Memory after an answer

- Codex: whatever memory object the relay returns.
- OpenRouter: built locally — summary = last **6000 characters** of (previous summary, newline, transcript sent, newline, partial sent, newline, "ASSISTANT: " + answer); through-segment id = the remembered cursor. Detected question is always the sentinel "Latest conversation"; details are always empty.

#### 3.4.3 Timing record saved with each turn

Assistant configuration used; speech provider id; reused flag; transcription-after-tap ms (Voz fresh requests only, else 0); context + model check ms (includes any catalogue fetch); round-trip ms; total ms (tap to answer); last live chunk ms (Parakeet only); transcript character count (transcript + partial); execution record from the response (model, effort, PC timings, and for OpenRouter the reported token usage and cost). All clocks are monotonic.

### 3.5 Streaming display

- OpenRouter text is shown as it arrives in the in-progress card, replacing the whole text on each update (cumulative, not appended by the UI).
- On success the in-progress card disappears and the finished answer appears as a card in the same position at the top.
- On failure the partial text remains until the next Assist or retry starts.
- Only assistant answer text is ever shown; provider reasoning, tool calls and raw provider errors are never rendered (enforced in the core stream parser).

### 3.6 Timers, polling, timeouts, retries

| What | Value | Notes |
|---|---|---|
| Session tick | 1 s | Increments elapsed; copies usage into the session (streaming providers); saves the session. Runs while a session exists, including while paused. |
| Relay health poll | 5 s after each check completes | Whole app lifetime; skipped entirely when the PC is not needed or nothing is paired. Also run once at launch, on every return to foreground, on "Reconnect", and once inside Start if a PC-local provider finds the relay offline. Never overlaps itself. |
| PC model status poll | 2 s after each check completes | While Home is visible with a PC-local provider, and while "PC model controls" is visible. Also after every successful health check for PC-local providers. Never overlaps itself or a start/stop command. |
| Model Library elapsed | 1 s | Display only. |
| Relay request timeout (default) | 65 s | Pair-verify, models, assist, summary. |
| Relay health / PC model status timeout | 8 s | |
| Relay PC model start/stop timeout | 10 s | The command returns immediately; progress comes from polling. |
| Meta connect + handshake | 30 s | Socket cancelled on expiry. |
| Meta connection test | 15 s | |
| PC speech connect + model loading | 180 s | Covers the relay's `loading` phase. |
| Finalise (Pause/Stop) drain | 15 s | Socket cancelled on expiry. |
| OpenRouter catalogue / key check | 20 s | |
| OpenRouter chat request | 65 s idle, 120 s total | |
| WebSocket maximum message | 128 KiB | Meta and relay. |
| Automatic retries | **None** | No request is ever retried automatically. Polling is the only repetition. |

### 3.7 PC model status logic

Status from the relay: `state` (`stopped`, `starting`, `ready`, `stopping`, `error`), `model` (loaded or targeted model id, may be null), `busy`, `elapsedSeconds`, `message`.

- **Ready for the selected provider** = state `ready` and model equals the selected provider id.
- **Can start a session** = relay online AND ready for the selected provider AND not busy AND no start/stop command in flight.
- Refresh: only for PC-local providers. Not paired → status unknown, message "Pair your PC first." Request failure → status unknown, message "Could not check the PC model. Keep the updated LiveCue Desktop running. " + categorised connection message.
- Start/Stop command: refused locally during a session, while another command is in flight, or while the PC reports starting/stopping. The relay's immediate reply replaces the status. On failure only the message changes (categorised connection message).

Home label:

| Condition (first match wins) | Label |
|---|---|
| Not paired | "Pair PC first" |
| Relay offline | "PC offline" |
| Status unknown | "Checking…" |
| A model is reported, it is not the selected one, and state is not `stopped` | "Different model loaded" |
| `starting` | "Loading · `N`s" |
| `stopping` | "Stopping · `N`s" |
| `ready` and busy | "In use" |
| `ready` | "Ready" |
| `error` | "Error · open Settings" |
| anything else | "Stopped · open Settings" |

### 3.8 Answer-service switching and catalogue loading

- Switching is refused while assisting. The outgoing service's configuration is saved under its own key; the incoming one is restored, or defaults to Codex `gpt-5.6-sol` / `low`, or OpenRouter with an empty model and effort `default`. The loaded catalogue and its message are cleared.
- Every change to the configuration (model, effort, service) is saved immediately, both as "current" and under the current service's key.
- Catalogue loads are tagged. If the service changes while a load is in flight, a new load starts for the new service and the stale result (or error) is discarded. A second load for the same service while one is running is ignored.
- Codex catalogue: requires pairing ("Pair your PC first." otherwise). Success message "From this PC's Codex catalog. Access still depends on your subscription." Failure: "Could not load models. Restart the updated PC app and check the connection. " + error.
- OpenRouter catalogue: no key needed. Models are kept only if they accept and produce text and their id does not contain ":batch"; sorted by name, case-insensitive. Success message "Direct from OpenRouter. Choose a model; API charges are separate from your Codex subscription." Failure: the OpenRouter error text.

### 3.9 Persistence cadence

- The active session is saved when a segment is committed, after every successful Assist, on every 1-second tick, on Pause/Resume, and on Stop.
- The History list in memory is refreshed only at launch, on Stop, on delete, and when a summary arrives.
- Consequence: a session exists in storage from about one second after Start, even if it never ends cleanly.

### 3.10 Usage and cost tracking

Every session records a usage object for its provider (rate **$0.18/hour** for Meta, 0 otherwise; pricing date `2026-09-28`). For streaming providers each stream (one per Start/Resume) tracks milliseconds sent, provider-reported processed milliseconds, whether any transcript arrived, and a state (streaming, completed, interrupted).

- Billable seconds per stream: while streaming, floor(max(sent, processed) ÷ 1000); once interrupted with no transcript, 0; otherwise floor((processed, or sent if never reported) ÷ 1000).
- Estimated cost = total billable seconds × rate ÷ 3600, shown as `$0.00000`.
- A stream is *completed* only if the user ended it and the socket closed normally; otherwise *interrupted*.
- The session is *incomplete* if any stream was interrupted, or completed without any processed-audio report.
- Also recorded: time to first words (set once per session, by the first stream that produces text, measured from that stream's start), and the last finalisation time (PC-local: on `streamComplete`; Meta: when the socket closes normally after the end message).
- Cost is displayed only for Meta. PC-local sessions track streams and timings but display "Local". On-device sessions record an empty usage object.

### 3.11 Cancellation and stale results

Nothing is user-cancellable: there is no cancel for Start, for an Assist, for a summary, for a catalogue load or for a model preparation. What the app does instead:

- **Timeouts** end stuck operations (§3.6).
- **Stream generations.** Each start of a transcriber gets a fresh token. Callbacks, audio frames, partials and batch results that belong to an older token are ignored, so a stopped or restarted stream cannot write into the session.
- **Assist results** are dropped silently if the session they were requested for is no longer the active one; streamed text updates are likewise ignored.
- **Catalogue results** are dropped if the answer service changed while loading (§3.8).
- **Summary results** are dropped if the session was deleted before the summary arrived.
- **Stop is blocked** (button disabled) until a running Assist finishes or times out; it does not abort it.

---

## 4. Speech providers as the app uses them

### 4.1 Choosing

Settings → Transcription (§1.7). The choice is saved immediately. Changing it clears the loaded on-device model selection. It cannot be changed during a session, during an on-device model preparation, or while a PC model is starting/stopping.

### 4.2 Meta Muse (cloud, direct from phone)

- Endpoint and handshake in §6.3. Model `muse-voice-transcribe-1.0`, audio `PCM_24KHZ`, endpointing mode, cumulative partials, audio-progress events on.
- The key travels only inside the first WebSocket message, never in a URL or header.
- Start sequence: connect → send handshake → first reply must be an acknowledgement (no `type`, non-empty `sessionId`) → start microphone.
- No model download. Language is whatever the provider detects.
- Each Start/Resume is a new billable stream. A disconnect pauses; nothing reconnects by itself.
- Provider error events are replaced by a fixed generic message; the key is redacted from any transcript text; transcript text is capped at 32 000 characters per event.

### 4.3 Nemotron 3.5 and Qwen3 1.7B (PC GPU, via relay)

- Audio is sent to the relay's speech WebSocket (§6.2) with the selected model id in a header.
- Start sequence: connect → zero or more `loading` events → `ready` event that must echo the selected provider id and sample rate 16 000 → start microphone. A mismatch fails with "PC did not confirm the selected speech model. Update/restart LiveCue Desktop and its tunnel." Anything other than `ready` fails with the relay's message or "PC transcription is not ready."
- The model must already be "Ready" before Start is allowed (§3.7). Loading, switching and unloading are driven from Settings.
- No speech charges. One PC model at a time.

### 4.4 Voz (on-device, batch)

- Library: Desert Ant core package, product **Voz**, exact version **3.1.0**. The app constructs the library's default recogniser with a progress callback; it does not name a model file itself.
- Preparation progress: fraction, bytes done / total (formatted as file sizes). Status "Downloading Voz…" until the fraction reaches 1, then "Download complete · preparing Neural Engine…", then "Ready".
- Records continuously; transcribes only on Assist, Pause and Stop (§3.3). Returns word timings, which the app uses for the overlap logic.
- English.

### 4.5 Parakeet (on-device, live)

- Library: **FluidAudio**, exact version **0.15.6**, streaming end-of-utterance recogniser. Configuration: compute units CPU + Neural Engine; chunk size **320 ms**; end-of-utterance debounce **640 ms**. Models are loaded into the library's default cache location.
- Preparation progress: fraction (clamped 0…1) and phase — "Checking model files…", "Downloading Parakeet…" with "`n` / `m` files", "Download complete · preparing model…", then "Ready".
- At session start the recogniser is reset and a partial-text callback is registered. Each converted audio buffer is fed in; the per-buffer processing time is recorded.
- A processing error raises the alert with the library's message and stops capture.
- Segments are committed only on Pause/Stop (§3.3).
- English.

### 4.6 Loading rules shared by Voz and Parakeet

- Only one on-device recogniser is held in memory. Preparing one releases the other.
- "Download & use" on an already loaded model returns immediately as "Ready".
- Preparation cannot start during a session or while another preparation is running.
- Failure: status "Preparation failed" and the library's error in the global alert. Elapsed time freezes when preparation ends either way.
- "Unload" stops any capture and releases both recognisers. It does not delete downloaded files; the app has no way to delete them.

---

## 5. Settings and persisted values

### 5.1 User defaults

| Key | Type | Default | Written when | Effect |
|---|---|---|---|---|
| `assistantInstruction` | string | "" | every edit | Sent with every Assist. |
| `speechProvider` | string (`meta`, `nemotron`, `qwen3`, `voz`, `parakeet`) | `meta` (also used for unknown values) | provider chosen | Active speech provider. |
| `selectedModel` | string (`voz` / `parakeet`) or absent | absent | on-device model prepared; removed on Unload | Written but **not honoured at launch** — the selection always starts empty (§11). |
| `relayEndpoint` | string | "" | successful pairing | Legacy copy of the relay address; used only for migrating old token-only pairings. |
| `assistantConfiguration` | JSON `{model, reasoningEffort, provider}` | `{gpt-5.6-sol, low, codex}` | every configuration change | Current answer service, model, effort. `provider` missing in old data means `codex`. |
| `assistantConfiguration.codex` | same JSON | — | every change while on Codex; on switching away | Remembered Codex selection. |
| `assistantConfiguration.openrouter` | same JSON | `{"" , default, openrouter}` when first selected | every change while on OpenRouter; on switching away | Remembered OpenRouter selection. |

### 5.2 Keychain

Generic-password items under one service name (equal to the app's bundle identifier). Accessibility: **after first unlock, this device only**. No access group, no iCloud sync.

| Account | Content | Used for |
|---|---|---|
| `relayPairing` | Pairing JSON `{endpoint, token}` | Secondary copy of the pairing. |
| `relayToken` | Bare token (legacy) | Read only, for one-time migration together with `relayEndpoint`. |
| `meta-api-key` | Meta API key | Meta transcription. |
| `openrouter-api-key` | OpenRouter API key | OpenRouter answers. |

Under UI-test mode the accounts are `relayPairing-tests`, `meta-api-key-ui-test`, `openrouter-api-key-ui-test`, so tests never touch real secrets.

Keys are never written anywhere else, never shown after entry, and never sent anywhere except their own provider. Key and token entry fields are masked, with auto-capitalisation off (and autocorrection off for the two API-key fields).

### 5.3 Files

| Location | Content | Protection |
|---|---|---|
| Application Support / `pairedPC.json` (`pairedPC-tests.json` in UI tests) | Pairing JSON `{endpoint, token}` | Written atomically; complete-until-first-user-authentication; **excluded from backups**. |
| Application Support / default SwiftData store | Sessions (§5.4) | Directory set to complete-until-first-user-authentication. In-memory only in UI tests. |
| Library caches chosen by the speech libraries | On-device model files | Managed by the libraries. |

Pairing load order: file → (and refresh the Keychain copy from it) → Keychain copy → legacy migration. Pairing save: file first (must succeed), Keychain copy best-effort. This double storage exists because the app is sideloaded in a container whose Keychain access can change.

### 5.4 Session record

One stored row per session: unique id, start time, title, and a JSON payload holding the whole session. Dates are ISO 8601 (whole seconds). Rows that fail to decode are skipped silently.

| Field | Notes |
|---|---|
| `id`, `title`, `startedAt`, `endedAt?` | Title default "New conversation". |
| `segments[]` | `id`, `text`, `startSeconds`, `endSeconds`, `isFinal` (always true) |
| `assistantTurns[]` | `id`, `createdAt`, `request?` (instruction used), `detectedQuestion`, `answer`, `details`, `performance?` (§3.4.3) |
| `memory` | `summary`, `facts[]`, `openQuestions[]`, `throughSegmentId?` |
| `notes` | `summary`, `keyPoints[]`, `actionItems[]` |
| `transcriptionUsage?` | `provider`, `hourlyRateUSD`, `pricingDate`, `firstTextMs?`, `finalizationMs?`, `streams[]` (`id`, `sentMs`, `processedMs?`, `hasTranscript`, `state`) |

### 5.5 Not persisted

Loaded on-device model, loaded catalogue, PC model status, relay online state, connection messages, key-operation messages, the remembered request for retry, expanded/collapsed UI state, benchmark worksheet text.

---

## 6. Networking from the app's side

### 6.1 Relay HTTP API

Base = the relay address from pairing (always HTTPS, always ends with "/"). Every request: `Authorization: Bearer <pairing token>`, `Content-Type: application/json`, JSON bodies with ISO 8601 dates and omitted nulls. Uses the system's shared URL session (default redirect and cache behaviour).

| Method and path | When | Request body | Response the app reads | Timeout |
|---|---|---|---|---|
| `GET /v1/health` | Health poll (§3.6) | — | `{ok: bool}` (other fields ignored). Any 2xx decodable reply = online. | 8 s |
| `POST /v1/pair/verify` | "Verify and pair" | `{"device": "LiveCue iPhone"}` | `{ok: bool}` | 65 s |
| `GET /v1/models` | Assistant Lab appears; "Refresh PC model list"; before **every** Codex Assist | — | `{models: [{id, name, reasoningEfforts: [string]}]}` | 65 s |
| `GET /v1/local-model` | PC model polling; before Start (PC-local) | — | `{state, model (nullable), busy, elapsedSeconds, message}` — all but `model` required | 8 s |
| `POST /v1/local-model/start` | "Start model" | `{"model": "nemotron" \| "qwen3"}` | Same status object (relay replies 202) | 10 s |
| `POST /v1/local-model/stop` | "Stop model" | same | same | 10 s |
| `POST /v1/assist` | Codex Assist | `{assistant: {model, reasoningEffort, provider}, requestId, sessionId, instruction?, memory: {summary, facts, openQuestions, throughSegmentId?}, transcript, partialTranscript?}` | `{detectedQuestion, answer, details, memory: {…}, execution: {model, reasoningEffort, codexMs, relayTotalMs, requestReadMs, relayOverheadMs}}` | 65 s |
| `POST /v1/session-summary` | After Stop, Codex only, in the background | `{sessionId, transcript (all segments joined by newlines), memory, assistant}` | `{title, summary, keyPoints: [string], actionItems: [string]}` | 65 s |

Response handling, identical for all:

| Outcome | App error |
|---|---|
| Malformed base address | "The relay address is invalid." |
| HTTP 401 | "The PC no longer recognizes this pairing. Scan its pairing QR once to reconnect." |
| Other non-2xx | The `error` string from a JSON body `{"error": "..."}` if present; otherwise "Relay error (`<status>`)." |
| 2xx but not decodable | "The relay returned an unreadable response." |
| Transport failure | System error; categorised only where §8.1 says so. |

The relay also offers a request-cancel endpoint; **the app never calls it**.

### 6.2 Relay speech WebSocket (Nemotron, Qwen3)

- URL: the relay address with scheme `wss` and path `/v1/speech` (query and fragment dropped). The saved address must be HTTPS with a host, else "Pair your PC first."; if the URL cannot be formed, "Invalid PC address."
- Headers: `Authorization: Bearer <pairing token>`, `X-LiveCue-Speech-Model: nemotron|qwen3`.
- Private ephemeral connection: no cookies, no cache, **redirects refused**. Max message 128 KiB.

Phone → relay: binary messages of raw PCM (16-bit little-endian, mono, 16 kHz, 80 ms each, last one may be shorter); one text message `{"type":"endStream"}` to finish.

Relay → phone, JSON objects:

| `type` | Fields the app uses | App behaviour |
|---|---|---|
| `loading` | `message` | Before ready only; keeps waiting (default text "Loading model on PC…"). |
| `ready` | `provider`, `sampleRate` (integer) | Must match the selection; then the microphone starts. |
| `speechStart` | `turnId`, `audioProcessedMs` | Opens a turn. |
| `transcript` | `transcript`, `audioProcessedMs` | Cumulative partial for the open turn. First non-empty one records time-to-first-words. |
| `speechComplete` | `turnId`, `transcript`, `audioProcessedMs` | Commits a segment. |
| `audioProgress` | `audioProcessedMs` | Usage only. |
| `streamComplete` | — | Records finalisation time. |
| `error` | `message` | Stream failure; message shown verbatim. |
| anything else | — | Ignored. |

Close after `endStream` with a normal close code = completed. Any other close or receive error = stream failure with the transport's error text.

### 6.3 Meta realtime transcription (direct)

- URL: `wss://api.meta.ai/v1/asr/realtime`. No auth header. Private ephemeral connection, no cookies, no cache, redirects refused. Max message 128 KiB.
- First message (text): `{"authorization": {"accessToken": "Bearer <key>"}, "model": "muse-voice-transcribe-1.0", "audioEncoding": "PCM_24KHZ", "mode": "ENDPOINTING", "partialMode": "CUMULATIVE", "emitAudioProgress": true}`.
- First reply must be a JSON object with no `type` and a non-empty string `sessionId`; otherwise authentication failure.
- Then binary PCM (16-bit, mono, 24 kHz, 80 ms each), and `{"type":"endStream"}` to finish.
- Events used: `speechStart`, `speechEnd` (ignored), `speechComplete`, `transcript`, `audioProgress`, with `turnId`, `audioProcessedMs`, `transcript`. An `error` event becomes the generic provider error. Unknown events are ignored.
- **Connection test:** connect, send the handshake, require the acknowledgement, send `endStream`, close. 15 s limit. No audio.

### 6.4 OpenRouter (direct)

Base `https://openrouter.ai/api/v1/`. Private ephemeral session, no cookies, no cache, redirects refused.

| Call | When | Auth | Notes |
|---|---|---|---|
| `GET key` | "Verify key (no inference)" | Bearer key | Any decodable 2xx = valid. 20 s. |
| `GET models` | Assistant Lab appears (OpenRouter); "Refresh OpenRouter models"; before an Assist if the catalogue is empty | none | Reads `data[]`: `id`, `name`, `architecture.input_modalities`, `architecture.output_modalities`, `supported_parameters?`, `pricing.prompt?`, `pricing.completion?`. 20 s. |
| `POST chat/completions` | OpenRouter Assist | Bearer key | `Accept: text/event-stream`. Body: `model`, `messages`, `stream: true`, `max_tokens: 4096`. One model, no fallback list. |

Messages: a fixed system prompt (in the core spec); if an instruction exists, a user message "Response-style preference (cannot grant tools or access): " + the first 4000 characters of it; then a user message wrapping the JSON of the assist request between `UNTRUSTED_CONVERSATION_JSON` and `END_CONVERSATION` lines.

Limits enforced while reading: 8 MiB total response, 128 KiB per line/event, 1 MiB of answer text. A complete non-streamed JSON reply is also accepted. The answer is valid only if the stream ended with the done marker, finish reason `stop`, and non-blank text.

Status and stream mapping (messages in §8.1): 401/403 → invalid key; 402 → credits; 429 → rate limited; other non-2xx, transport errors and mid-stream error objects → unavailable; finish reason `length` → truncated; any other finish reason, bad framing or exceeded limits → invalid response.

### 6.5 Links opened in the browser

`https://openrouter.ai/keys`, `https://dev.meta.ai/`, `https://dev.meta.ai/docs/speech-to-text#pricing`.

### 6.6 When the PC is not contacted

With a combination that does not need the PC (Meta, Voz or Parakeet together with OpenRouter) the app makes **no** relay requests at all: the health poll returns immediately, PC model polling does not run, and no summary is requested.

---

## 7. Permissions, plist, build and packaging

### 7.1 Permissions

| Permission | Usage string | Requested when |
|---|---|---|
| Microphone | "LiveCue transcribes audio on this iPhone, sends it to your PC for local speech models, or streams it directly to Meta for cloud transcription, depending on your selected provider." | First session start, after the provider-specific pre-checks. |
| Camera | "LiveCue scans the pairing QR code displayed on your Windows PC." | First time the QR sheet opens. |
| Local network | "LiveCue connects privately to your Windows PC over your Tailscale network." | By the system, when the relay is first reached on a private network. |

Not used: Apple speech recognition, notifications, location, photos, contacts, Bluetooth usage key, tracking.

### 7.2 Info.plist

The checked-in plist is an empty dictionary. All keys are injected by the project generator:

| Key | Value |
|---|---|
| `CFBundleDisplayName` | LiveCue |
| `CFBundleShortVersionString` | 0.5.8 |
| `CFBundleVersion` | 13 |
| `UILaunchScreen` | empty dictionary (blank system launch screen) |
| `UIBackgroundModes` | `audio` |
| `UISupportedInterfaceOrientations` | portrait only |
| `NSMicrophoneUsageDescription`, `NSCameraUsageDescription`, `NSLocalNetworkUsageDescription` | as §7.1 |

No App Transport Security exceptions (all traffic is HTTPS/WSS), no URL schemes, no associated domains, no app groups, no push, no scene configuration.

### 7.3 Build and bundle

| Item | Value |
|---|---|
| Project definition | XcodeGen `project.yml` is the source of truth; the Xcode project is generated and not committed. |
| Deployment target | iOS 18.0 |
| Devices | iPhone only |
| Toolchain | Xcode 26 (CI pinned to 26.3, simulator runtimes up to iOS 26.2); Swift language mode 5; strict concurrency "targeted". |
| Targets | App; core framework (shared logic); core unit tests; UI tests. |
| Bundle identifier | `<owner reverse-DNS prefix>.LiveCue` (prefix redacted here; see the reference `project.yml`). Core framework uses the same prefix. |
| Keychain service name | Same string as the app bundle identifier. |
| Signing | Disabled. CI produces an **unsigned arm64 device build**, packages it as an IPA, and publishes it for sideloading inside a container app (LiveContainer). Not an App Store or TestFlight build. |
| Entitlements | None for device builds. Simulator builds only: application identifier, a Keychain access group equal to the bundle identifier, and debugger attach. |
| App icon | One 1024×1024 universal image, full-bleed, no pre-rounded corners. |
| Versioning rule | Stay below 1.0; patch increments per iteration; the release tag must equal "v" + marketing version; build number increases independently. |

### 7.4 Third-party packages

| Package | Source | Version rule | Product used | Purpose |
|---|---|---|---|---|
| Desert Ant core | `github.com/Desert-Ant-Labs/desert-ant-core` | exactly **3.1.0** | `Voz` | On-device batch transcription |
| FluidAudio | `github.com/FluidInference/FluidAudio` | exactly **0.15.6** | `FluidAudio` | On-device streaming transcription (Parakeet) |

No other dependencies. System frameworks: SwiftUI, SwiftData, AVFoundation, CoreML, Security, Combine, UIKit (clipboard only).

### 7.5 Test setup

- UI tests launch the app with a test-mode argument that swaps in fixtures (Appendix A). No test touches a real provider, relay, key or microphone.
- CI runs unit and UI tests serially on an Apple-silicon simulator and stores screenshots as evidence. The on-device speech library's simulator build requires Apple silicon.

---

## 8. User-facing strings that carry meaning

Labels and short captions are given in place in §1. This section collects messages produced by logic, and the long explanatory texts. All strings are English; the old app has no localisation.

### 8.1 Messages shown in the global "LiveCue" alert

| Message | Cause |
|---|---|
| "Choose and download a transcription model first." | Start with an on-device provider and no loaded model. |
| "Add your Meta API key in Transcription settings. It stays in this iPhone's Keychain." | Start with Meta and no key. |
| "PC speech model: `<home label>`. Open Transcription settings to start it or check its status." | Start with a PC-local provider whose model cannot start. |
| "Pair this iPhone with the Windows relay first." | PC route needed, nothing paired. |
| (current connection message, §8.2) | PC route needed, relay still offline after one re-check. |
| "Microphone permission is required for live transcription." | Microphone denied. |
| "Transcription could not start. `<reason>`" | Streaming transcriber failed before the microphone opened. Reasons below. |
| "Network cannot keep up. Recording paused; resume to retry." | Upload queue overflow. |
| "Audio connection failed. Resume to reconnect." | Sending audio failed. |
| "Microphone interrupted. Resume when your call or other audio finishes." | Audio interruption began. |
| "Microphone route changed. Resume to use the current microphone." | Input device went away. |
| "Meta transcription stopped. Check API access, billing, or rate limits before retrying." | Meta sent an error event. |
| "Could not connect directly to Meta. Check this iPhone's internet connection and retry. No PC is required for Meta transcription." | Meta socket failed or closed unexpectedly. |
| (relay-provided text, verbatim) | The relay sent a speech `error` event. |
| "Could not finalize the transcript. Some final words may be missing." | The end-of-stream message could not be sent. |
| "Prepare Voz first." / "Prepare Parakeet first." | On-device start without a loaded recogniser. |
| "No usable microphone." | Input format invalid. |
| "Transcription could not keep up. Stop and restart the session." | On-device processing queue overflow. |
| "Pending recording reached 30 minutes. Tap Assist to transcribe it." | Voz pending-audio cap. |
| "Use the pairing QR from LiveCue Desktop. It must contain an HTTPS PC address and a valid pairing token." | Pairing payload failed validation. |
| "Camera access is required. Enable it in Settings, or paste the pairing payload instead." | Camera denied. |
| "Camera is unavailable. Paste the pairing payload instead." | No usable camera. |
| "QR scanning is unavailable." | Scanner could not be configured. |
| (categorised connection message, below) | "Verify and pair" failed. |
| (library error text) | On-device model preparation or live processing failed. |

Reasons appended to "Transcription could not start.":

- Meta: the missing-key message; "Meta did not accept the connection. Check the API key, account access, and billing in Transcription settings."; or the could-not-connect message above.
- PC: "Pair your PC first."; "Invalid PC address."; the relay's message or "PC transcription is not ready."; "PC did not confirm the selected speech model. Update/restart LiveCue Desktop and its tunnel."; "Unexpected speech response."; "No usable microphone."; or raw transport error text.

**Categorised connection message.** Optional prefix "Your pairing is saved. " (when a pairing exists and has not been rejected), then:

| Underlying failure | Text |
|---|---|
| TLS or certificate failure | "The secure connection to your PC failed. Check the PC tunnel and retry. Scanning the QR again will not repair this connection." |
| No internet, connection lost, timeout, cannot connect, host not found, DNS failure | "Your PC is currently unreachable. Keep LiveCue Desktop running and check your internet connection. The app will reconnect automatically." |
| Anything else | The underlying error text (relay errors from §6.1, or system text). |

### 8.2 Inline status messages

| Where | Message | When |
|---|---|---|
| Saved PC | "Connected. Your pairing is saved on this iPhone." | Health check or pairing succeeded. |
| Saved PC | "Your pairing is saved. The app reconnects automatically after restarting." | No message yet. |
| Saved PC | categorised connection message; for 401 the rejected-pairing message without prefix | Health check failed. |
| Meta key | "Key saved securely on this iPhone. Meta connects directly; no PC needed." | Saved. |
| Meta key | "Could not save the key in Keychain (status `N`). No insecure fallback was used." | Keychain write failed. |
| Meta key | "Key removed." / "Keychain could not remove this key (status `N`). Try again." | Remove. |
| Meta key | "Meta connection verified directly. No microphone audio was sent." | Test passed. |
| Meta key | Meta missing-key / not-accepted / could-not-connect messages | Save validation or test failed. |
| OpenRouter key | "Enter a valid API key." | Empty or contains whitespace. |
| OpenRouter key | "Key saved securely on this iPhone." | Saved. |
| OpenRouter key | "Could not save the key in Keychain (status `N`). No insecure fallback was used." | Keychain write failed. |
| OpenRouter key | "Key removed." / "Keychain could not remove this key (status `N`). Try again." | Remove. |
| OpenRouter key | "Key verified. No inference request was made." | Verify passed. |
| OpenRouter key | OpenRouter error text (§8.3) | Verify failed. |
| Catalogue | "From this PC's Codex catalog. Access still depends on your subscription." | Codex catalogue loaded. |
| Catalogue | "Pair your PC first." | Codex, not paired. |
| Catalogue | "Could not load models. Restart the updated PC app and check the connection. `<error>`" | Codex catalogue failed. |
| Catalogue | "Direct from OpenRouter. Choose a model; API charges are separate from your Codex subscription." | OpenRouter catalogue loaded. |
| PC model | "Checking PC model…" | Before the first status arrives. |
| PC model | "Pair your PC first." | Not paired. |
| PC model | "Could not check the PC model. Keep the updated LiveCue Desktop running. `<categorised connection message>`" | Status request failed. |
| PC model | (relay-provided message) | Normal case; text comes from the relay. |
| Model Library status | "Prepare a model"; "Downloading and preparing `<name>`…"; "Downloading Voz…"; "Download complete · preparing Neural Engine…"; "Checking model files…"; "Downloading Parakeet…"; "Download complete · preparing model…"; "Ready"; "Preparation failed"; "Listening"; "Recording"; "Transcribing…"; "Paused" | Transcriber status line. |

### 8.3 Assist stage labels and inline errors

Stage labels: "Transcribing…", "Thinking…", "Checking model…", "Replying…". Error-state label: "Incomplete answer".

| Inline error | Cause |
|---|---|
| "Pair this iPhone with the Windows relay first." | Codex, no pairing. |
| "No speech was recognized yet." | Nothing to send. |
| "`<model>` / `<effort>` is not in this PC's current catalog. Open Assistant models & timing and choose an available combination. Your selection has not changed." | Codex selection missing from the fresh catalogue. |
| "The PC did not confirm the selected model. Restart the updated LiveCue Desktop app, then retry." | Codex response without a matching execution record. |
| Relay errors from §6.1, including relay-provided text | Codex request rejected. |
| "Add your OpenRouter API key in Assistant settings first." | OpenRouter, no key. |
| "Choose an OpenRouter model in Assistant settings first." | OpenRouter, no usable model selected. |
| "OpenRouter rejected this API key. Replace it in Assistant settings." | 401 / 403 |
| "OpenRouter credits are insufficient. Check your OpenRouter account balance or key spending limit." | 402 |
| "OpenRouter rate-limited this request. Wait briefly and try again." | 429 |
| "OpenRouter could not complete this request. Check your connection or try again later. No fallback service was used." | Other failures, transport errors, mid-stream errors. |
| "OpenRouter returned an incomplete or unreadable answer. No fallback model was used." | Malformed or incomplete response. |
| "The selected model reached the response limit before finishing. Try a shorter question or another model." | Truncated answer. |

### 8.4 Long explanatory texts

| Id | Where | Text |
|---|---|---|
| S-01 | Settings, Meta | "Audio streams directly from this iPhone to Meta. No PC or model download. Cloud usage is billed by Meta. Recording stops if the connection fails; resume to reconnect." |
| S-02 | Settings, PC-local | "Audio streams to the model on your PC's GPU. No speech API charges. Keep Docker Desktop and LiveCue Desktop running. Switching models may take a minute; recording starts only when ready." |
| S-03 | Settings, on-device | "Audio is transcribed on this iPhone. Only text is sent to your PC." (Codex) / "Audio is transcribed on this iPhone. Only text is sent to OpenRouter when you tap Assist." |
| S-04 | PC model controls | "Start opens Docker Desktop if needed. Stop unloads only LiveCue’s speech model; weights stay installed and Docker remains open. One PC model runs at a time." |
| S-05 | Settings, cost | "Live estimates use audio sent; completed streams use Meta's reported processed audio, rounded down to whole seconds per stream. Silence sent to Meta counts as audio. Credits and billing adjustments are not included. Interrupted streams may have incomplete usage. This estimates transcription only, not assistant usage." |
| S-06 | Settings, privacy | "Meta and OpenRouter API keys stay in this iPhone's Keychain and go only to their respective services. Cloud audio goes directly to Meta; cloud Assist text goes directly to OpenRouter. PC-local speech and Codex Assist still use your paired PC. Transcripts and replies are saved in iPhone history. Obtain permission before recording others." |
| S-07 | Meta key | "Your key is stored only in this iPhone's Keychain, not in app source, history, your PC, or GitHub. Audio connects directly to Meta over TLS. The key previously saved on Windows is not automatically copied; paste your Meta key here once." |
| S-08 | Meta key | "For a completely PC-free conversation, also choose OpenRouter in Assistant models & timing and save its key. Codex CLI and PC-local speech still need the PC." |
| S-09 | Meta key | "Testing connects to Meta but sends no microphone audio. Actual transcription is billable. Disconnections pause recording; there are no automatic paid retries or fallback providers." |
| S-10 | OpenRouter key | "Stored only in this iPhone's Keychain and sent directly to OpenRouter—not your PC or GitHub. Each Assist sends transcript context and can incur API charges. Stop does not make another paid request. Meta cloud and on-iPhone transcription do not need a PC; PC speech models still do." |
| S-11 | Assistant Lab, Codex | "Selection applies to your next Assist and session summary. None disables reasoning on supported models; it does not remove network or CLI startup time. Astra and Spark start at Low. No model is guaranteed to be fastest; test the same text." |
| S-12 | Assistant Lab, OpenRouter | "Answers bypass the PC and Codex CLI. Models may differ in availability and price. There are no automatic paid retries or fallback models." |
| S-13 | Pairing, privacy | "Your pairing is saved in iPhone Keychain and a protected file in this app, excluded from backups. Meta mode sends audio directly from this iPhone to Meta. Nemotron and Qwen3 send audio to your PC only. Voz and Parakeet transcribe on this iPhone. OpenRouter answers bypass the PC; Codex answers need it. Desktop activity stays in memory." |
| S-14 | Session detail, complete usage | "Calculated from reported audio usage; not a confirmed charge. Credits and billing adjustments may change your bill." |
| S-15 | Session detail, incomplete usage | "Connection ended before all usage could be confirmed. This is an incomplete estimate, not a Meta invoice." |
| S-20 | Timing view, Codex | "PC timings are parts of the round trip, not extra time. Codex includes CLI startup, cloud processing and output handling—not pure inference. Transfer is an estimate including encoding/decoding. Live transcription runs before the tap, so it is not part of tap-to-answer time." |
| S-21 | Timing view, OpenRouter | "OpenRouter round trip includes network, provider processing and response decoding—not pure inference. Cost is reported by OpenRouter after the request; missing cost is not treated as free. Speech transcription is a separate service." |

---

## 9. Acceptance criteria

Source column: **T1–T16** = derived from an existing UI test (key below); **L** = observable behaviour of the old app with no UI test. Tests run against fixtures (Appendix A).

| Key | Old UI test |
|---|---|
| T1 | Caption strip, inline answers, expand transcript, pinned controls |
| T2 | Partial Assist failure is inline; manual retry recovers |
| T3 | Meta key persists; all-cloud conversation works without PC |
| T4 | Direct OpenRouter key persistence; three Assists without PC |
| T5 | PC model start/stop in Settings |
| T6 | PC model start-up error allows retry |
| T7 | Pairing survives relaunch and a missing Keychain copy |
| T8 | PC-local speech choices hide cost in conversation and history |
| T9 | Home shows PC model loading, ready and stopped without scrolling |
| T10 | Selecting a different Codex model at low effort can Assist |
| T11 | Assistant selection and timing |
| T12 | Conversation and Assist flow, then on-device download flow |
| T13 | Pairing offers the QR scanner |
| T14 | Settings shows theme name and cloud disclosure |
| T15 | Instructions live only in Settings and persist |
| T16 | Focused conversation: cost, pause, history |

### 9.1 Launch and Home

| ID | Statement | Source |
|---|---|---|
| AC-IOS-001 | When the app launches with no session in progress, then a tab bar with exactly "Live", "History" and "Settings" is shown with Live selected. | T16, L |
| AC-IOS-002 | When Home is shown at default text size on a standard iPhone, then the pairing row, the assistant row and Start are all tappable without scrolling, and the bottom of Start is above the top of the tab bar. | T16 |
| AC-IOS-003 | When Home is shown, then no transcription cost is visible. | T16 |
| AC-IOS-004 | When the app is freshly installed, then Home shows "PC offline", "Setup", "Pairing required" and "Add API key", and the defaults are Meta speech with Codex `gpt-5.6-sol` / `low`. | L |
| AC-IOS-005 | When speech is Meta and answers are OpenRouter, then the pill reads "Cloud direct · no PC"; when speech is on-device and answers are OpenRouter, then it reads "OpenRouter direct"; both regardless of PC reachability. | T3, T4 |
| AC-IOS-006 | When the PC is needed, then the pill reads "PC online" if reachable, "PC reconnecting" if paired but unreachable, "PC offline" if unpaired. | L |
| AC-IOS-007 | When speech is Meta and a key is saved, then Home shows "Key saved · direct". | T3 |
| AC-IOS-008 | When and only when speech is ready and the answer service is ready (§0.2), then readiness reads "✓ Ready"; otherwise "Setup". | L |
| AC-IOS-009 | When an on-device provider is selected and its model is not loaded, then Home shows "Download a transcription model" instead of Start, and tapping it opens Model Library. | T12 |
| AC-IOS-010 | When a streaming provider is selected, then the download action is never shown. | T3, T8 |
| AC-IOS-011 | When a PC-local provider is selected, then Home shows a "PC speech model" row and Start is disabled unless the relay is online, the selected model is ready, it is not in use and no start/stop command is in flight. | T6, T9 |
| AC-IOS-012 | When a PC model is loading, then the Home label contains "Loading" and Start is disabled; when loading finishes, then without any user action the label becomes "Ready" and Start becomes enabled and tappable, with the transcription-settings row still tappable. | T9 |
| AC-IOS-013 | When the PC model has been stopped, then the Home label contains "Stopped" and Start is disabled. | T9 |
| AC-IOS-014 | When a PC model start has failed, then the Home label contains "Error" and Start is disabled. | T6 |
| AC-IOS-015 | When the system text size is an accessibility size, then Home scrolls rather than clipping. | L |

### 9.2 Starting a session

| ID | Statement | Source |
|---|---|---|
| AC-IOS-016 | When Start is tapped with Meta selected and no key saved, then an alert whose message contains "Meta API key" appears and no session starts. | T3 |
| AC-IOS-017 | When microphone permission is denied, then the alert "Microphone permission is required for live transcription." appears and no session starts. | L |
| AC-IOS-018 | When a start is in progress, then Start reads "Connecting…" and is disabled. | L |
| AC-IOS-019 | When a session starts, then the tab bar disappears and Pause, Assist and Stop are tappable. | T1, T16 |
| AC-IOS-020 | When speech is Meta or PC-local, then the microphone is opened only after the provider handshake succeeds; until then the live screen shows "Updating…" with controls disabled. | L |
| AC-IOS-021 | When the transcriber fails to start, then the app returns to the tabs and shows an alert beginning "Transcription could not start." | L |
| AC-IOS-022 | When Start is tapped with a PC-local provider whose model is not ready, then an alert beginning "PC speech model:" appears and no session starts. | L |
| AC-IOS-023 | When a new session starts, then the answer feed is empty, no error is shown, and elapsed reads 00:00. | L |
| AC-IOS-024 | When the answer service is not configured, then Start still works; the problem is reported on the first Assist. | L |

### 9.3 Live session screen

| ID | Statement | Source |
|---|---|---|
| AC-IOS-025 | When a session starts, then the transcript strip is collapsed and no scrollable transcript exists. | T1 |
| AC-IOS-026 | When the strip is tapped, then a scrollable transcript listing every committed segment appears; when tapped again, then it disappears; Stop stays tappable throughout. | T1, T12 |
| AC-IOS-027 | When collapsed, then the preview shows the partial text if any, else the last committed segment, else the provider's placeholder. | L |
| AC-IOS-028 | When the expanded transcript receives new text, then it scrolls to the newest text. | L |
| AC-IOS-029 | When listening, paused or transitioning, then the state label reads "Listening", "Paused" or "Updating…" respectively. | T3, T8 |
| AC-IOS-030 | When a session is active, then elapsed advances once per second in `MM:SS`, including while paused. | L |
| AC-IOS-031 | When speech is Meta, then a live cost label is shown; when speech is Nemotron, Qwen3, Voz or Parakeet, then none is shown. | T16, T8, T12 |
| AC-IOS-032 | When Meta audio is streaming, then the cost label leaves "$0.00000 USD" within 5 seconds. | T16 |
| AC-IOS-033 | When a Meta session is paused, then the cost label does not change. | T16 |
| AC-IOS-034 | When a session is active, then no instruction field is present on the live screen. | T16 |
| AC-IOS-035 | When a session is active, including while paused, then no tab bar is present. | T1, T16 |
| AC-IOS-036 | When answers are shown, then the answer feed occupies more than half the screen height. | T1 |
| AC-IOS-037 | When sound is captured, then the level meter moves; when paused, then it is flat. | L |
| AC-IOS-038 | When a model id contains "/", then the status panel shows only the part after the last "/"; when no model is chosen, then "Choose model". | L |

### 9.4 Pause, resume, stream failures

| ID | Statement | Source |
|---|---|---|
| AC-IOS-039 | When Pause is tapped, then "Paused" is shown; when tapped again, then "Listening" is shown. | T3, T8 |
| AC-IOS-040 | When pausing, then the provider is finalised as in §3.1 and any final text is committed before "Paused" appears. | L |
| AC-IOS-041 | When resuming Meta or PC-local, then a new stream is opened, the session's usage gains one stream, and later segment times are offset by the elapsed seconds at resume. | L |
| AC-IOS-042 | When an Assist is running or a transition is in progress, then Pause, Assist and Stop are disabled. | L |
| AC-IOS-043 | When a Meta or PC-local stream fails for any reason, then the session becomes Paused, an alert states the reason, and no reconnection is attempted until Resume is tapped. | L |
| AC-IOS-044 | When an audio interruption begins during a Meta or PC-local session, then AC-IOS-043 applies with "Microphone interrupted. Resume when your call or other audio finishes." and nothing resumes automatically. | L |
| AC-IOS-045 | When the input device disappears during a Meta or PC-local session, then AC-IOS-043 applies with "Microphone route changed. Resume to use the current microphone." | L |
| AC-IOS-046 | When more than 64 converted audio buffers are waiting to upload, then AC-IOS-043 applies with "Network cannot keep up. Recording paused; resume to retry." | L |
| AC-IOS-047 | When finalising takes longer than 15 seconds, then the connection is cut, an alert reports the problem, the stream is recorded as interrupted, and the transition still completes. | L |
| AC-IOS-048 | When the app is backgrounded or the phone locked during listening, then capture and transcription continue (intended via the audio background mode; never verified by a test). | L |

### 9.5 Assist

| ID | Statement | Source |
|---|---|---|
| AC-IOS-049 | When Assist completes, then the answer appears inline as the newest card at the top of the feed, with no alert, no sheet and no dismiss control. | T1, T3, T4, T8, T12 |
| AC-IOS-050 | When N Assists have completed in a session, then exactly one answer is marked latest and N−1 are shown as previous. | T1 |
| AC-IOS-051 | When an Assist completes, then Pause, Assist and Stop are tappable again. | T1 |
| AC-IOS-052 | When an Assist is running, then the Assist button reads "Replying…" with a spinner. | L |
| AC-IOS-053 | When Assist is used three times in a row, then all three succeed. | T1, T3, T4 |
| AC-IOS-054 | When nothing has been recognised, then Assist shows inline "No speech was recognized yet." and sends nothing. | L |
| AC-IOS-055 | When answers are Codex and nothing is paired, then Assist shows the not-paired message inline with no alert. | L |
| AC-IOS-056 | When answers are OpenRouter and no key is saved, then Assist shows an inline error containing "OpenRouter API key" with no alert. | T4 |
| AC-IOS-057 | When an Assist fails after partial text arrived, then an inline error is shown, the partial text stays visible labelled "Incomplete answer", no completed answer card exists, no alert or sheet appears, and Stop is tappable. | T2 |
| AC-IOS-058 | When "Retry same text" is tapped after a failure and succeeds, then the error and partial text disappear and exactly one answer exists with no previous answers. | T2 |
| AC-IOS-059 | When an Assist fails, then no turn is saved and the conversation memory is unchanged. | L, T2 |
| AC-IOS-060 | When "Retry same text with selected model" is tapped, then the same text is re-sent with a new request id and the currently selected assistant, and the new answer's timing shows "Transcription (reused)". | T11 |
| AC-IOS-061 | When an Assist is running or no earlier request exists, then both retry buttons are disabled. | L |
| AC-IOS-062 | When answers are Codex, then the PC catalogue is fetched before every Assist; if the selected model and effort are not in it, an inline error names the combination and the selection is left unchanged. | L |
| AC-IOS-063 | When a Codex response does not echo the selected model and effort, then the Assist fails with "The PC did not confirm the selected model. …" and nothing is saved. | L |
| AC-IOS-064 | When answers are OpenRouter, then answer text appears progressively and the stage reads "Replying…" once text arrives. | L |
| AC-IOS-065 | When speech is Voz, then Assist first transcribes the pending audio (stage "Transcribing…") and commits the new text before sending. | L |
| AC-IOS-066 | When no one taps Assist or a retry button, then no Assist request is ever sent. | L |
| AC-IOS-067 | When the session is paused, then Assist still works. | L |
| AC-IOS-068 | When the model or effort is changed in Assistant Lab during a session, then the next Assist uses the new selection. | T11 |
| AC-IOS-069 | When the timing disclosure of a Codex answer is opened, then it contains a row starting "PC: Codex CLI call". | T11 |
| AC-IOS-070 | When an answer card is shown, then its text is selectable, a copy button copies the answer, total time is shown, a reported cost is shown when present, and non-empty details can be toggled. | L |
| AC-IOS-071 | When a new answer lands, an Assist starts, or an error appears, then the feed scrolls to the top. | L |
| AC-IOS-072 | When the answer came from OpenRouter, then no detected-question line is shown; when from Codex, then it is. | L |
| AC-IOS-073 | When an instruction is saved in Settings, then it is sent (trimmed) with every Assist; when empty, nothing is sent for it. | L |

### 9.6 Stop, summary, History

| ID | Statement | Source |
|---|---|---|
| AC-IOS-074 | When Stop is tapped, then the app returns to the tabs with Home and Start visible. | T1, T12, T16 |
| AC-IOS-075 | When a session has ended, then History lists it with "`N` transcript segments · `M` assists". | T12 |
| AC-IOS-076 | When a session ends, then its title is the first 48 characters of its first segment, or "Conversation" if it has none. | L |
| AC-IOS-077 | When Stop is tapped while paused, then the session ends normally. | T8 |
| AC-IOS-078 | When a session ends with Codex answers and a pairing, then a summary is requested after the UI has returned; on success the History title is replaced and Summary, Key points and Action items appear in the detail; on failure nothing is shown and nothing is retried. | L |
| AC-IOS-079 | When a session ends with OpenRouter answers, then no request is made. | L |
| AC-IOS-080 | When a Meta session ends, then its History row shows the same cost that the live screen showed at the end. | T16, T3 |
| AC-IOS-081 | When a PC-local session ends, then its History row shows "… · Local" with no currency symbol and no cost label. | T8 |
| AC-IOS-082 | When there are no sessions, then History shows "No conversations yet" / "Completed sessions will appear here." | L |
| AC-IOS-083 | When a History row is swiped to delete, then the session is removed permanently. | L |
| AC-IOS-084 | When a session is opened, then usage, summary, key points, action items, transcript and assistant sections appear only if they have content (§1.12). | L |
| AC-IOS-085 | When History is shown, then sessions are ordered newest start first. | L |
| AC-IOS-086 | When any stream of a session was interrupted, then History appends " · incomplete" and the detail shows the incomplete-estimate caption. | L |
| AC-IOS-087 | When the app is relaunched, then earlier sessions are still in History. | L |
| AC-IOS-088 | When a session has no recorded usage, then no usage label or section is shown for it. | L |

### 9.7 Settings

| ID | Statement | Source |
|---|---|---|
| AC-IOS-089 | When speech is Meta, then Settings shows text containing "Audio streams directly from this iPhone to Meta". | T14 |
| AC-IOS-090 | When Settings is shown, then an Appearance row names the current theme (old value "Midnight Mint"; the remake's value is a design decision, §11). | T14 |
| AC-IOS-091 | When an instruction is typed in Settings and the app is relaunched, then the same instruction is shown. | T15 |
| AC-IOS-092 | When the keyboard's "Done" is tapped, then the keyboard is dismissed. | T15 |
| AC-IOS-093 | When the provider picker is opened, then five providers are listed; choosing one applies immediately and returns to Settings. | T5, T12 |
| AC-IOS-094 | When a session is active, an on-device model is preparing, or a PC model is changing, then the provider row is disabled. | L |
| AC-IOS-095 | When and only when the provider is on-device, then "Model Library" and "Speech benchmark" rows are shown. | L |
| AC-IOS-096 | When and only when the provider is Meta, then a "Meta API key" row is shown with "Saved on iPhone" or "Required". | T3 |
| AC-IOS-097 | When and only when the provider is PC-local, then "PC model controls" is shown. | T5 |
| AC-IOS-098 | When Settings is shown, then the app's marketing version is displayed. | L |

### 9.8 PC model controls

| ID | Statement | Source |
|---|---|---|
| AC-IOS-099 | When "Start model" is tapped and loading succeeds, then the state becomes "Ready", Start model is disabled and Stop model is enabled. | T5, T8 |
| AC-IOS-100 | When "Stop model" is tapped and unloading succeeds, then the state becomes "Stopped", Start model is enabled and Stop model is disabled. | T5 |
| AC-IOS-101 | When either PC-local provider is selected, then AC-IOS-099 and AC-IOS-100 hold for it. | T5 |
| AC-IOS-102 | When loading fails, then the state is "Error", the relay's message is shown, and Start model is enabled for a retry. | T6 |
| AC-IOS-103 | When the PC model is starting or stopping, then a spinner and elapsed seconds are shown and both buttons are disabled. | L |
| AC-IOS-104 | When a session is active, the PC reports the model busy, or nothing is paired, then both buttons are disabled. | L |
| AC-IOS-105 | When a different PC model is loaded, then a note names it and Stop model is disabled. | L |
| AC-IOS-106 | When the controls are visible, then status refreshes about every 2 seconds without user action. | L |

### 9.9 Meta key

| ID | Statement | Source |
|---|---|---|
| AC-IOS-107 | When no key is saved, then the state reads "No key saved"; when a valid key is saved, then it reads "Key saved" and the field is emptied. | T3 |
| AC-IOS-108 | When a key has been typed or saved, then its characters never appear as visible text anywhere. | T3 |
| AC-IOS-109 | When an empty key or one containing whitespace is saved, then it is rejected with the missing-key message. | L |
| AC-IOS-110 | When "Test connection (no audio)" succeeds, then the message contains "No microphone audio" and the microphone was not used. | T3 |
| AC-IOS-111 | When the app is relaunched, then a saved Meta key is still present. | T3 |
| AC-IOS-112 | When the key is removed, then the state reads "No key saved" and Start raises the missing-key alert. | T3 |
| AC-IOS-113 | When a session is active, then the key cannot be saved, tested or removed. | L |

### 9.10 Assistant Lab

| ID | Statement | Source |
|---|---|---|
| AC-IOS-114 | When Assistant Lab appears, then the catalogue for the current answer service is loaded. | T10, T11 |
| AC-IOS-115 | When a Codex model row is tapped, then it becomes selected and its id is shown as the selected model. | T11 |
| AC-IOS-116 | When the reasoning picker is opened, then the selected model's efforts are listed; choosing "None (no reasoning)" results in "Reasoning: none". | T11 |
| AC-IOS-117 | When a different Codex model is selected, then the current effort is kept if supported, otherwise the model's first effort is used; an Assist with it then succeeds. | T10 |
| AC-IOS-118 | When the service is switched to OpenRouter, then the key section appears; saving a key shows "Key saved" and the key is never visible. | T4, T3 |
| AC-IOS-119 | When "Verify key (no inference)" is tapped, then only a key-validity check is made. | T4 |
| AC-IOS-120 | When the OpenRouter picker is used, then models can be searched by name or id, prices per million tokens are shown, and choosing a model closes the picker and shows its id as selected. | T4 |
| AC-IOS-121 | When the app is relaunched, then the OpenRouter key and the chosen model are still present. | T4 |
| AC-IOS-122 | When the OpenRouter key is removed, then the state reads "No key saved". | T4 |
| AC-IOS-123 | When the service is switched while the previous service's catalogue is still loading, then the new service's catalogue appears within 5 seconds and the stale result is never shown. | T4 |
| AC-IOS-124 | When switching between services, then each service's own model and effort are restored. | L |
| AC-IOS-125 | When a session is active, then Assistant Lab is reachable from the live screen; while an Assist runs the link and the service, key and model sections are disabled. | T11, L |
| AC-IOS-126 | When timed answers exist, then "Recent comparisons" lists up to 50, newest first, across all sessions, each opening its run details. | L |

### 9.11 Pairing and connectivity

| ID | Statement | Source |
|---|---|---|
| AC-IOS-127 | When the pairing screen is opened (after revealing pairing options if already paired), then "Scan PC QR code" is offered. | T13 |
| AC-IOS-128 | When a valid payload is scanned or pasted and read, then the address and token fields are filled, the paste box is emptied, and nothing is saved until "Verify and pair" is tapped. | L |
| AC-IOS-129 | When a payload is not valid (§2.2 rules), then the alert shows the pairing-payload error. | L |
| AC-IOS-130 | When "Verify and pair" succeeds, then the pairing is saved, the screen shows "Connected" and "Connected. Your pairing is saved on this iPhone." | L |
| AC-IOS-131 | When "Verify and pair" fails, then an alert explains why and any earlier pairing is unchanged. | L |
| AC-IOS-132 | When the app is relaunched after pairing, then the saved address is shown exactly as saved, including port and trailing slash. | T7 |
| AC-IOS-133 | When the Keychain copy of the pairing is missing, then the pairing is still restored from the protected file. | T7 |
| AC-IOS-134 | When paired, then "Reconnect" is available and the QR option is hidden until pairing options are revealed. | T7 |
| AC-IOS-135 | When "Reconnect" is tapped and the PC is reachable, then the state contains "Connected". | T7 |
| AC-IOS-136 | When the relay answers 401, then the state reads "Pairing needs attention" with the rejected-pairing message. | L |
| AC-IOS-137 | When the relay is unreachable or TLS fails, then the state reads "Reconnecting" with the matching message prefixed "Your pairing is saved." | L |
| AC-IOS-138 | When camera access is denied, then an alert says so and paste and manual pairing remain usable. | L |
| AC-IOS-139 | When "Cancel" is tapped in the scanner, then it closes with no change. | L |
| AC-IOS-140 | When only a legacy token-only pairing exists, then it is migrated to the current format on launch. | L |
| AC-IOS-141 | When the pairing file is written, then it is excluded from backups and protected until first unlock. | L |
| AC-IOS-142 | When the PC is needed and a pairing exists, then relay health is checked at launch, on each return to foreground, and every 5 seconds. | L |
| AC-IOS-143 | When the selected combination does not need the PC, then no request of any kind is sent to the relay. | L |
| AC-IOS-144 | When Meta, the relay speech socket or OpenRouter responds with a redirect, then it is not followed. | L |

### 9.12 On-device models

| ID | Statement | Source |
|---|---|---|
| AC-IOS-145 | When "Download & use" is tapped, then a progress bar appears; when preparation completes, then the button reads "Reload" and Home offers Start. | T12 |
| AC-IOS-146 | When a model is preparing, then percentage, elapsed time, a status line and a byte or file counter are shown. | L |
| AC-IOS-147 | When "Unload" is tapped, then the model is released and Home offers the download action again. | L |
| AC-IOS-148 | When preparation fails, then an alert shows the error and the status reads "Preparation failed". | L |
| AC-IOS-149 | When the app is relaunched or the provider is changed, then the on-device model must be loaded again before Start is offered. | L |
| AC-IOS-150 | When a Parakeet session runs, then no cost is shown and Assist works. | T12 |

### 9.13 Platform and data handling

| ID | Statement | Source |
|---|---|---|
| AC-IOS-151 | When the device is rotated, then the app stays in portrait; the app is iPhone-only. | L |
| AC-IOS-152 | When an API key is stored, then it exists only in the Keychain (this device only, after first unlock) and is sent only to its own provider. | L |
| AC-IOS-153 | When audio is captured, then none of it is written to storage. | L |
| AC-IOS-154 | When a session is in progress, then it is saved at least once per second, so a killed app still shows the session in History. | L |

---

## 10. Old visual design (being replaced)

Described only so the remake knows what it supersedes. None of this is a requirement.

- **Theme "Midnight Mint".** Dark only (dark appearance forced). Near-black teal background (#0B1414), slightly lighter card colour, mint accent (#2EE6B6), deep teal secondary (#0E3D3A). Orange for warnings and "needs attention". Stock green checks and an indigo "RECOMMENDED" tag appear inside standard lists.
- **Surfaces.** Custom rounded panels (20 pt radius) with a faint gradient and a thin mint outline, brighter for the highlighted (latest / in-progress) card.
- **Buttons.** Primary action: full-width mint gradient capsule, 56 pt tall, dark text. Secondary session controls: 44 pt circular teal buttons with a mint outline.
- **Type.** System font. 16 pt body, 30–36 pt Home title, 20 pt live header, 11–14 pt metadata.
- **Home.** A static hero mark (rounded tile with five bars over faint wave lines), title, tagline, status pill, readiness panel, three setup rows, primary button pinned at the bottom.
- **Live screen.** Compact status bar, two-line caption strip, inline answer feed, three pinned controls ("Layout B" in the old release notes).
- **Everything else** (Settings, pairing, Assistant Lab, Model Library, History, pickers) uses stock grouped forms and lists, some with the themed background.
- **App icon.** Three luminous mint/aqua vertical rounded bars, the centre one taller with a diagonal speech tail, on a deep teal field. 1024 px, full-bleed.

---

## 11. Ambiguities, bugs and dead code

Flagged, not resolved. Each needs a keep / fix / drop decision.

### 11.1 Likely bugs

| # | Finding |
|---|---|
| B1 | **On-device model selection is saved but never restored.** The saved value is read and then unconditionally cleared at launch, so Voz/Parakeet must be re-loaded from Model Library after every launch and after every provider change. May be deliberate (the model must be in memory), but the stored key then serves no purpose. |
| B2 | **Ghost sessions.** The session tick starts before the transcriber connects and saves the session every second. If connecting takes over a second and then fails, an empty session titled "New conversation" remains in storage and appears in History later. Sessions cut off by an app kill likewise stay with no end time and the default title. |
| B3 | **Elapsed time includes connection and paused time**, and that same clock is used as the timestamp offset for resumed streams. The first stream's timestamps therefore lag the clock by the connection time. Timestamps are never displayed, so the effect is only on segment ordering. |
| B4 | **On-device errors do not pause.** When Voz hits its 30-minute cap or Parakeet throws while processing, capture stops and an alert appears, but the screen still says "Listening". Recovery needs Pause then Resume. |
| B5 | **On-device capture ignores interruptions and route changes.** Only the Meta/PC-local path observes them. |
| B6 | **Audio session handling differs on Pause**: released for Meta/PC-local, kept active for on-device. Background survival while paused therefore differs by provider. |
| B7 | **Stop after Pause finalises twice.** Harmless for Meta, PC-local and Voz. For Parakeet the recogniser is asked for its final text a second time; whether that returns nothing or repeats the last text depends on the library. |
| B8 | **Uncategorised transport errors.** Codex Assist network failures, and PC speech socket failures including HTTP upgrade rejections (unauthorised, relay busy, relay paused, model changing), surface raw system error text instead of the friendly connection messages. |
| B9 | **Misleading OpenRouter error.** If the catalogue is empty and fetching it fails during an Assist, the user is told to choose a model rather than that the network failed. |
| B10 | **Misleading prefix on pair failure.** A failed "Verify and pair" is prefixed "Your pairing is saved." whenever an older pairing exists. |
| B11 | **"Rejected" state is sticky.** After one 401 the saved-PC state keeps reading "Pairing needs attention" through later plain network failures, until a check succeeds. |
| B12 | **Summary is requested for empty sessions** and its failure (including "relay busy") is silent and final. |
| B13 | **Model Library progress block is global.** It stays visible after completion and appears under whichever model is listed, so after switching provider it shows the other model's last preparation. "Reload" on a loaded model does nothing. |
| B14 | **"16 pt · follows Dynamic Type" is only partly true.** Most labels use fixed point sizes that do not scale; only unstyled text scales. |
| B15 | **Uncommitted partial text is lost on disconnect.** It stays visible while paused (and would be sent as the partial in an Assist) but is discarded on Resume. |
| B16 | **Parakeet partial is unbounded.** No segment is committed until Pause/Stop, the partial is sent whole with each Assist, and only the committed transcript has the 80 000-character cap. The relay rejects bodies over 128 KB. |
| B17 | **"PC connected" in the manual pairing section** reflects the currently saved PC, even while the user is typing details for a different one. |
| B18 | **Turn timestamps are stored to whole seconds**, so the order of comparison rows created within the same second is undefined. |

### 11.2 Ambiguities

| # | Finding |
|---|---|
| A1 | **Parakeet partial semantics.** Whether the library's partial callback is cumulative for the whole recording or resets after each detected end of utterance is not visible from the app. The app never commits on end-of-utterance. Needs a check against the library on a real device. |
| A2 | **On-device model identity.** The app names no model files; both libraries download their own defaults for the pinned versions. Exact model identifiers and download hosts are implied by the package versions, not stated in the app. |
| A3 | **Start is not gated on readiness.** The readiness panel is informational only. Unclear whether that is intended. |
| A4 | **No unpair, no model delete, no delete confirmation, no Assist cancel, no idle-timer hold.** Absent in the old app; "same functionality" means they stay absent unless decided otherwise. The relay does expose a request-cancel endpoint that nothing calls. |
| A5 | **Two routes to Settings** (tab, and pushed from Home as "Transcription & appearance"). Looks like a leftover from an earlier layout. |
| A6 | **Appearance row and its UI test** assert the old theme name. The remake has a new design; the row's content and AC-IOS-090 need redefining. |
| A7 | **Local-network usage string** names one specific VPN product, although the relay can also be reached through a public HTTPS tunnel. |
| A8 | **Data continuity.** Keeping existing history, keys and pairing requires the same bundle identifier, Keychain service and account names, file names and session JSON shape. Not stated whether the remake must read old data. |
| A9 | **PC model status decoding is strict.** A status reply missing `busy`, `elapsedSeconds` or `message` is treated as unreadable. Relay and app must agree on always sending them. |
| A10 | **Asset notes disagree.** One asset note describes a "Fluent Blue" Windows icon; the other says the desktop icon files are the Midnight Mint artwork, and quotes a 22-point main title the UI does not use. Neither affects the iPhone app. |
| A11 | **What counts as a clean Pause/Stop.** A user-ended stream is "completed" only if the socket then closes with the normal close code. Any other close, including the app's own 15-second cut-off, is reported as a connection failure with an alert and marks usage incomplete. Whether Meta always closes normally after the end message cannot be told from the code. |

### 11.3 Dead or unfinished code

| # | Finding |
|---|---|
| D1 | An unused navigation-route enumeration (models, history, settings, model lab). |
| D2 | An "is recording" flag that is set but never read. |
| D3 | A benchmarks list and benchmark-result type that are never filled or shown. |
| D4 | **Transcriber timing/diagnostic strings are computed but never displayed**: "Connecting to `<provider>`…", the relay's loading messages, "Meta direct · 0.00 s setup", "Connected · 0.00 s setup", "First words · 0.00 s from stream start", " · Final 0.00 s", "Disconnected · resume to retry", "Last buffer processing: `N` ms", "`0.0` s audio → `0.00` s transcription". Consequence: while a PC model loads at Start, the user sees only "Updating…". |
| D5 | The animated "listening" mode of the hero artwork is never used; only the static Home variant is. |
| D6 | **Speech benchmark screen is unfinished.** Its own text calls it a "first build" worksheet. "Use latest transcript" reads the active session, but the screen is reachable only when no session is active, so it always produces empty text. |
| D7 | The relay's health reply includes a PC-side cloud-speech readiness field the app ignores; the relay's own Meta proxy route is not used by this version. |
| D8 | The Settings version row falls back to a stale literal ("0.5.1"). |
| D9 | The Meta key section's "disabled during a session" rule is unreachable (the screen cannot be opened during a session). |
| D10 | The "Choose and download a transcription model first." alert is unreachable from the UI. |
| D11 | UI tests assert the absence of a `dismiss-answer` control from an earlier design. One test's screenshot is labelled for Voz but runs the Meta fixture. |

---

## Appendix A — Test mode and fixtures in the old app

The old app switches to fixtures when launched with a test-mode argument. The remake needs an equivalent seam for its UI tests; the mechanism is free, the behaviours below are what the tests rely on.

**Base test mode** (`-ui-testing`):

- Sessions are stored in memory only; Keychain accounts and the pairing file use separate test names.
- Speech provider forced to Meta; answer configuration reset to the default; a placeholder Meta key is present; the app reports itself paired and the relay online.
- No microphone prompt, no audio capture, no network requests of any kind.
- Start commits one fixture segment: "What is the main advantage of local transcription?".
- For streaming providers a fixture usage stream gains one second of audio per tick while not paused; Pause closes it, Resume opens a new one.
- Assist shows the partial text "Your audio stays on the iPhone," for 0.4 s, then returns a fixed answer with detected question (the fixture segment), details, memory and execution timings; OpenRouter runs also carry fixture token counts and cost.
- Codex catalogue fixture: five models (`gpt-6-astra`, `gpt-6-luna`, `gpt-5.6-sol`, `gpt-5.6-luna`, `gpt-5.3-codex-spark`); the two 5.6 models include effort `none`, the others offer low/medium/high.
- OpenRouter catalogue fixture: one model `test/direct` with effort `default`. Key verification succeeds without a request.
- PC model fixture: starts as stopped; Start/Stop pass through a changing state for 1 s, then ready/stopped.
- On-device preparation fixture: 50% for 2 s, then ready.

**Modifiers:**

| Argument | Effect |
|---|---|
| `-on-device-ready` | Provider Parakeet with its model already loaded. |
| `-meta-no-key` | Remove the test Meta key at launch. |
| `-meta-key-persistence` | Leave the test Meta key as it was (do not re-seed). |
| `-openrouter-ui-reset` | Remove the test OpenRouter key at launch. |
| `-openrouter-persistence-test` | Keep the saved answer configuration instead of resetting it. |
| `-pc-offline` | Relay reported offline ("PC offline."). |
| `-pc-catalog-slow` | Codex catalogue takes 8 s. |
| `-pc-model-slow-start` | PC model start takes 6 s. |
| `-pc-model-start-error` | PC model start ends in error with a message mentioning Docker Desktop. |
| `-assist-fails-once` | The first fresh Assist of a session shows a partial answer and then fails with the OpenRouter "could not complete" error; a retry succeeds. |
| `-pairing-persistence-test` | Use real pairing storage logic (test names) instead of "always paired". |
| `-save-pairing-fixture` | Save a synthetic pairing (reserved test domain, explicit port, trailing slash) at launch. |
| `-drop-test-keychain` | Delete the test Keychain copy of the pairing, then reload from the file. |

