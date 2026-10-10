# LiveCue for Windows: screens

Every view, overlay and dialog of the Windows app in the remake. It builds on the [design system](01-design-system.md) (DS) and on the functional spec of the old desktop window (`docs/spec/04-windows-desktop-and-ci.md`, "spec"). Static mockups are in [mockups/windows.html](mockups/windows.html); frame ids are given with each view.

The remake's Windows app is one self-contained program that hosts the relay itself. There are no scripts, no console and no separate launcher, so everything the old scripts did silently (checking prerequisites, choosing the connection mode, setting up the Tailscale mapping, resetting the pairing, printing the pairing text) has a place in this window.

Conventions:

- Text in "double quotes" is exact copy. **(new)** marks text with no counterpart in the old app; **(reworded)** marks text that replaces an old string with the same meaning. The old window used three dots and hyphens ("Thinking...", "Request failed - ready to retry"); the new text uses "…" and full stops.
- `PascalCase` after an element is its automation id, for the rebuilt window tests. Section 9 lists them and the old element names they replace.
- Sizes are device-independent pixels.
- Addresses, ports and paths in examples are placeholders.

Contents: [1 Window](#1-window) · [2 Live](#2-live) · [3 Pairing](#3-pairing) · [4 Activity](#4-activity) · [5 Settings](#5-settings) · [6 Dialogs and messages](#6-dialogs-and-messages) · [7 Resize behaviour](#7-resize-behaviour) · [8 Keyboard and accessibility](#8-keyboard-and-accessibility) · [9 Automation ids](#9-automation-ids) · [10 What changed from the old window](#10-what-changed-from-the-old-window)

---

## 1. Window

### 1.1 Navigation structure

```
Window
├─ Title bar: brand, window buttons
├─ Sidebar
│   ├─ Live        relay status and controls, phone status, transcript and reply mirror
│   ├─ Pairing     QR, PC address, reset pairing
│   ├─ Activity    the in-memory log
│   ├─ Settings    Connection · Assistant · PC speech models · About
│   └─ Status card (always visible): relay, iPhone, "Closing this window stops the relay."
└─ In-window dialogs: Reset pairing, Set up Tailscale mapping
```

Why this shape: it is the glass sidebar shell the owner approved in their other desktop app, with one item per job. The old window showed everything in one view and hid pairing and activity in overlays; with the relay now built in there is more to show (connection mode, prerequisites, speech models), and four named places are easier to find than one crowded screen. Live stays the first and default view because it is what the owner glances at.

### 1.2 Frame

| Property | Value |
|---|---|
| Title | "LiveCue" |
| Default size | 1240 x 660, centred |
| Minimum size | 960 x 620 |
| Chrome | Custom title bar, 48 high, DS 3.5: brand mark and "LiveCue" on the left; a glass capsule with Minimize, Maximize or Restore, Close on the right |
| Background | The Daylight ground (DS 2.2), opaque |
| Icon | DS 8.2 |

No tray icon, no start at sign-in, no minimise to tray.

**Closing.** Close (or Alt+F4) stops the relay and exits, with no confirmation, as before. Pending phone requests are cancelled. The sidebar says so at all times.

**Already running.** Starting the app while it is running does not open a second window. The first window comes to the front and shows a toast, "LiveCue is already running." **(new)**. If the first window cannot be reached, the second process shows a system message box with the same sentence and exits.

### 1.3 Sidebar

A panel 232 wide, the full height of the content area (DS 4.9, radius 30, padding 10).

- Four navigation rows (DS 4.8, selected state), 44 high, icon and label: "Live" (`NavLive`), "Pairing" (`NavPairing`), "Activity" (`NavActivity`), "Settings" (`NavSettings`). One is always selected. Up and Down move between them.
- A status card at the bottom (inset card, radius 20):

| Line | Content |
|---|---|
| "Relay" (kicker) | The relay indicator from 2.3 with a short label: "Stopped", "Checking…", "Cannot start yet", "Ready", "Paused", "Thinking…", "Request failed", "Relay stopped". `SidebarRelayStatus` |
| "iPhone" (kicker) | The phone indicator from 2.4. `SidebarPhoneStatus` |
| Caption | "Closing this window stops the relay." **(reworded; was "Closing LiveCue stops its relay." in the activity overlay)** |

The card is not a button. It replaces the old connection footer card; its "Scan to pair" button is now the Pairing navigation row.

### 1.4 Page layout

Each view is a page header (the view's name in `type.display`, with the view's main actions on the right) and panels below it, 16 apart. Pages do not float anything over scrolling content (DS rule W-1). Changing view cross-fades the content only (DS 7.1).

---

## 2. Live

Mockups: `win-live-stopped`, `win-live-ready`, `win-live-thinking`, `win-live-failed`, `win-live-prereq`, `win-min`.

**Purpose.** Start and stop the relay, see whether a phone is talking to it, and see the last request and the answer that went back.

### 2.1 Layout

| # | Element | Component |
|---|---|---|
| 1 | Header: "Live", then the relay controls | Page header; session control and buttons (DS 4.17, 4.2) |
| 2 | Status strip: relay status, iPhone status, PC address | Compact panel |
| 3 | Prerequisite list, only after a failed start | Panel of check rows |
| 4 | Two panels side by side, widths 0.88 : 1.12: "Incoming transcript" and "Assistant reply" | Panels, each with one inset card that fills the remaining height and scrolls |

### 2.2 Relay controls

| Relay state | Header shows |
|---|---|
| Stopped, or failed to start | "Start relay", session control idle face (accent, 48 high, play glyph). `StartRelayButton` |
| Checking | The same control, busy: spinner and "Checking…" |
| Running, accepting | Secondary button "Pause relay" (pause glyph), `PauseRelayButton`; secondary button "Stop relay" with the stop glyph in `status.danger`, `StopRelayButton` |
| Running, paused | "Resume relay" (play glyph) in the same place; "Stop relay" |

Start is the only accent control on the page. Once the relay runs there is no accent control: nothing here needs pressing.

- **Start** runs the prerequisite check (2.6), sets up the Tailscale mapping if the mode needs it and it is already approved (5.2), starts the relay and clears the activity log.
- **Pause / Resume** changes the label and the status only when the relay confirms. While paused the relay refuses assist, summary, PC model control and speech streaming, and still answers health checks. Pause is not remembered across restarts.
- **Stop relay** stops the relay (up to 2.5 s), resets the status to "Stopped" and the phone to "iPhone not connected", hides the QR. It does not clear the transcript, the reply, the activity log or the PC address.

### 2.3 Relay status

Shown in the status strip under the kicker "Relay" as an inline status indicator (DS 4.22) with the full text; the text wraps to two lines and has a tooltip with the full text. `RelayStatusText`.

| Status text | When | Mark and colour |
|---|---|---|
| "Stopped" | At launch; after Stop relay | Hollow ring, `status.offline` |
| "Checking Codex and Tailscale…" | Start pressed, check running | Spinner, `status.live` |
| "Cannot start yet. `N` items need attention." **(new)** | The check found failures (2.6) | Alert, `status.danger` |
| "Ready" | Relay ready; after Resume; after a reply while not paused | Dot, `status.success` |
| "Paused" | Relay confirmed pause; after a reply while paused | Pause glyph, `status.warning`. A caption below: "New requests from the iPhone are refused until you resume." **(new)** |
| "Thinking…" | An assist or summary request is being processed | Spinner, `status.live` |
| "Request failed. Ready to retry." **(reworded)** | A tracked request failed or timed out | Alert, `status.danger` |
| "The relay port is already in use. Close the other LiveCue relay, then click Start relay." **(reworded)** | The relay could not open its port | Alert, `status.danger` |
| "Could not start the relay." | The relay failed to start | Alert, `status.danger` |
| "Relay stopped. Click Start relay to reconnect." **(reworded)** | The relay ended by itself | Dot, `status.warning` |
| "The relay stopped responding. Stop and restart the relay." **(reworded; was "Desktop connection interrupted…")** | The window lost contact with the hosted relay | Alert, `status.danger` |

When the relay ends by itself, a fatal reason already on screen (port in use, could not start) stays visible instead of being replaced by "Relay stopped…", as before. The window does not restart the relay.

### 2.4 iPhone status

Under the kicker "iPhone". `PhoneStatusText`.

| Text | When | Mark |
|---|---|---|
| "iPhone not connected" | No authenticated request since the relay started | Hollow ring, `status.offline` |
| "iPhone connected" | Last authenticated request at most 15 s ago | Dot, `status.success` |
| "iPhone idle" | Older than 15 s | Dot, `status.warning` |

Once a phone has been seen, the indicator has a tooltip: "Last authenticated heartbeat: `N` seconds ago". The status never returns to "not connected" by timeout; it stays idle until the relay stops. "Idle" does not prove the phone is offline: iOS stops the heartbeat when the app is in the background. The tooltip adds that sentence **(new)**.

### 2.5 PC address

Right side of the status strip, kicker "PC address", the advertised HTTPS address in `type.mono`, selectable. `PcAddressText`. While the relay has never started in this run: "Shown when the relay is running" **(new)** in `ink.secondary`. After Stop it keeps the last address.

### 2.6 Prerequisite check

Pressing Start runs every check and shows all results, not just the first failure. If anything fails, a panel titled "Before the relay can start" **(new)** appears between the status strip and the two panels' place (it takes their place until the relay starts), with a "Check again" secondary button and an "Open Settings" quiet button. `PrerequisiteList`.

Each item is a check row (DS 4.8). A pass is one line. A failure has a title and a fix.

| Check | Pass text | Failure title | Fix line |
|---|---|---|---|
| Tailscale installed | "Tailscale is installed" | "Tailscale is not installed" | "Install Tailscale, sign in, then choose Check again." |
| Tailscale online | "Tailscale is online", with "This PC is `<name>`" | "Tailscale is not online" | "Open Tailscale and connect first." |
| PC name | (part of the row above) | "Tailscale did not return this PC's DNS name" | "Check that MagicDNS is on for your tailnet, then choose Check again." **(new)** |
| Codex CLI | "Codex CLI found" | "Codex CLI was not found" | "Install or update the Codex desktop app, then open it once and sign in." |
| Codex sign-in | "Codex is signed in" | "Codex is installed but not signed in" | "Open Codex and sign in with ChatGPT, then choose Check again." |
| Connection settings | "Connection settings are valid", with the mode | "The Public Funnel settings are not valid" **(reworded)** | "Open Settings and check the mode and port." |
| Tailscale mapping (Private tailnet) | "Tailscale HTTPS mapping is active" | "Tailscale HTTPS setup failed" | "Check that Tailscale is connected, or set the mapping up in Settings." |
| Relay port | "The relay port is free" | "The relay port is already in use" | "Close the other LiveCue relay, then click Start relay." |
| Saved Meta credential, only if one exists | "Meta credential unlocked" | "Cannot unlock the local Meta credential" | "Use the Windows account that saved it." |

The failure titles and fixes are the old messages (spec 2.4, 1.10) split into what is wrong and what to do. The old checks for Node.js and the relay's dependency folder have no counterpart, because the relay is part of the app; if the build keeps any runtime prerequisite, its check is one more row in this list.

### 2.7 Incoming transcript

Panel header "Incoming transcript"; under it a hint line with a microphone glyph, `TranscriptHint`; then an inset card with the kicker "From iPhone" and the text in `type.transcript`, read-only, selectable, wrapping, scrolling, `TranscriptText`.

| Event | Hint | Text |
|---|---|---|
| Initial | "Live from your iPhone" | "Your next question will appear here." in `ink.secondary` |
| Assist request received | "Received from your iPhone" | The transcript; then, when present, a blank line and "[In progress] " + the interim text; then, when present, a blank line and "Instruction: " + the instruction. The last two blocks are `ink.secondary`. If all are empty, the request as indented JSON |
| Summary request received | "Session summary requested" | As above |
| Speech status from the relay (model loading, stream status, catalogue errors) | The status message | Unchanged |
| Live speech text | Unchanged | Appended as a new line and scrolled into view. Above 64,000 characters the text is cut to the newest 48,000 |
| Clear activity | "Live from your iPhone" | Emptied |

The old decorative strip of five bars under this text is not carried over. It never moved and was explained by a tooltip saying it was not a waveform.

### 2.8 Assistant reply

Panel header "Assistant reply"; a hint line with a sparkles glyph, `ReplyHint`; an inset card with the kicker "Assistant" and the reply in `type.cue`, read-only, selectable, scrolling, `ReplyText`; under the card a caption line for PC timing, `ReplyTiming`.

| Event | Hint | Card | Caption under the card |
|---|---|---|---|
| Initial | "Powered by Codex on your PC" | "Ready when you are." / "Tap Assist on your iPhone." in `ink.secondary` | none |
| Request received | "Processing with Codex on your PC" | A spinner and "Thinking…" | none |
| Reply sent | "Sent to iPhone · `<model>` · `<effort>` · `0.00` s Codex" when the reply carries execution data, otherwise "Sent to iPhone · `0.00` s" | The answer (or the summary); then the details after a blank line; then, for a summary, a `type.micro` heading "Key points" with one item per line and a heading "Action items" with one item per line. If all are empty, the reply as JSON | With execution data: "PC relay total: `0.000` s. Codex includes startup and cloud processing." (this was a tooltip; it is now visible) |
| Request failed | "Not sent. Try Assist again." **(reworded)** | A danger banner: title "Nothing was sent to the iPhone" **(new)**, body = the relay's message ("Assistant request failed or timed out. Check Codex sign-in and retry.") | none |
| Clear activity | "Activity cleared" | Emptied | none |

### 2.9 States of the whole view

| State | Controls | Status strip | Panels |
|---|---|---|---|
| Stopped | Start relay | Stopped; iPhone not connected | Initial texts, or the last transcript and reply if the relay ran before |
| Checking | Start relay, busy | Checking… | Unchanged |
| Check failed | Start relay | Cannot start yet | Replaced by the prerequisite list |
| Ready | Pause relay, Stop relay | Ready; phone status live | Last transcript and reply |
| Paused | Resume relay, Stop relay | Paused, with its caption | Unchanged |
| Thinking | Pause or Resume, Stop relay | Thinking… | Transcript shows the request; reply shows the spinner |
| Request failed | Pause or Resume, Stop relay | Request failed. Ready to retry. | Reply shows the danger banner |
| Relay ended by itself | Start relay | Relay stopped…, or the fatal reason | Unchanged |

---

## 3. Pairing

Mockups: `win-pairing-shown`, `win-pairing-reset`.

**Purpose.** Show the QR that pairs an iPhone, show the address it will use, and reset the pairing when it must be revoked.

### 3.1 Layout

| # | Element | Component |
|---|---|---|
| 1 | Header: "Pairing" | Page header |
| 2 | Left panel, 304 wide: the QR tile and the line under it | Panel; QR code frame (DS 4.15) |
| 3 | Right panel: title, hint, buttons, PC address, verification notice, footnote | Panel |
| 4 | Reset panel, full width, at the bottom | Compact panel |

### 3.2 QR tile

`PairingQr`. DS 4.15, display.

| State | Tile | Line under the tile |
|---|---|---|
| Relay not running | Placeholder: QR glyph, "Your pairing code", "Start the relay to show the QR" **(reworded)** | none |
| Hidden | Placeholder: QR glyph, "Your pairing code", "Show the QR to pair an iPhone" **(reworded; was "Generate a QR to get started")** | "For a phone that cannot scan, show the QR first." **(new)** |
| Generating | Spinner | none |
| Shown | The code, 240 x 240 on white | A neutral chip with a clock glyph, "Hides in `M:SS`" **(new)**, counting down from 2:00; and a quiet button "Copy pairing text", `CopyPairingTextButton` |
| Last 30 seconds | As shown | The chip becomes a warning chip |

The QR hides itself 120 seconds after it was shown, when "Hide QR" is pressed, and when the relay stops. Leaving the Pairing view does not hide it; the timer keeps running.

**Copy pairing text** **(new place for an old function)**. The old terminal launcher printed the pairing text (`{"endpoint": …, "token": …}`) so that a phone whose camera could not scan could be paired by pasting. There is no terminal now, so this button copies the same text to the clipboard. It is available only while the QR is shown. It shows a toast, "Pairing text copied. It contains the pairing token; paste it only into LiveCue." **(new)**. The text itself is never displayed in the window or written to the activity log.

### 3.3 Right panel

| Element | Content |
|---|---|
| Title | "Pair your iPhone", `type.title` |
| Hint, `PairingHint` | See the table below |
| Buttons | Primary "Show QR" with the QR glyph, `ShowQrButton`, disabled until the relay is ready (caption "Start the relay first." **(new)**). Secondary "Hide QR", `HideQrButton`, disabled while nothing is shown |
| PC address | Read-only field labelled "PC address" in `type.mono` with a trailing copy icon button, `PairingAddressField` |
| Verification notice, `PairingVerifyNotice` | A banner, shown after each QR display |
| Footnote | "Pairing codes are private. Codes hide after 2 minutes. Showing the QR again keeps existing pairings valid." |

Hint:

| When | Text |
|---|---|
| Before any QR in this run | "Pair your iPhone once. Restarts and showing this QR keep existing pairings valid." |
| QR shown | "On iPhone: Pair Windows PC > Scan PC QR code, then Verify and pair. QR hides after 2 minutes." |
| Hidden by the button | "QR hidden. Existing pairing remains valid." |
| Hidden by the timer | "QR hidden for privacy. Existing pairing remains valid." |

Verification notice (the relay checks its own advertised address each time the QR is shown):

| Result | Banner |
|---|---|
| Verified, Private tailnet | Success: "Secure PC connection verified." |
| Verified, Public Funnel | Success: "Secure Funnel connection verified. The iPhone VPN is not required." |
| Not verified | Warning: "Pairing code created, but the advertised connection could not be verified. Check the PC tunnel before pairing." with a quiet button "Open Settings" **(new)**. The QR is still shown |

Showing the QR never changes the pairing: the same code is shown again and paired phones stay paired.

**First start.** When the relay starts with no saved pairing it creates one, the app switches to the Pairing view by itself and shows the QR, as the old overlay opened by itself.

### 3.4 Reset pairing

A separate panel at the bottom, so it cannot be mistaken for part of normal pairing.

- Title "Reset pairing"; caption "Creates a new pairing code. Every iPhone paired with this PC stops working until it scans the new QR." **(new)**
- Destructive secondary button "Reset pairing…", `ResetPairingButton`. Disabled while the relay is not running, with the caption "Start the relay first."

It opens the confirmation in 6.1. On confirm: the pairing is replaced, the relay keeps running, the phone status returns to "iPhone not connected", the new QR is shown, the hint reads "Pairing reset. Scan this QR on each iPhone." **(new)**, and the activity log records "Pairing reset. Every iPhone must pair again." **(new)**.

This is the old terminal-only reset. It is the only way to revoke a phone; there is still no list of devices.

---

## 4. Activity

Mockups: `win-activity`, `win-activity-empty`.

**Purpose.** A readable record of what the relay did since it started, for checking that a request arrived and what was sent back.

### 4.1 Layout

| # | Element | Component |
|---|---|---|
| 1 | Header: "Activity"; secondary button "Clear activity" with a trash glyph, `ClearActivityButton`, disabled while the log is empty | Page header |
| 2 | One panel filling the page: the log, then a footer line | Panel; log view (DS 4.23) |

Log, `ActivityLog`: each entry starts with its time (`HH:MM:SS`, `ink.secondary`) and its message; a request or reply is followed by its payload as indented JSON in `ink.secondary`.

| Entry | Text |
|---|---|
| Relay ready | "Relay started. Waiting for your iPhone." In Public Funnel mode also "Funnel enabled: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address." Then "Meta Muse is configured. The key remains on this PC." or "Meta cloud key not configured. Local transcription is still available." |
| Saved pairing found | "Saved pairing restored. Existing iPhones reconnect automatically; Show QR keeps their pairing valid." |
| QR verification | The three results from 3.3 |
| Request | "INCOMING `<kind>` [`<request id>`]" and the payload |
| Reply | "REPLY [`<request id>`] `0.00` s" and the payload |
| Request failed | The relay's message |
| Speech status | The message (model loading and unloading, stream status) |
| Relay problem | Fatal messages; "Relay diagnostic: " + the line, for internal diagnostics; "The desktop command failed. Please retry." |
| Trimmed | "[Older activity removed from memory]" as the first line, once the log passes 200,000 characters and is cut to the newest 150,000 |

Footer line: "Activity stays in memory on this PC. It is cleared when LiveCue closes and is never written to disk." **(reworded; was "Activity stays in memory on this PC")**, and on the right the entry count.

**Clear activity** empties the log, the transcript and the reply on Live, and sets the reply hint to "Activity cleared". Starting the relay also clears the log. No confirmation; nothing here is kept anyway.

### 4.2 States

| State | Panel |
|---|---|
| Empty | Empty state in the panel: a 64 icon tile with the activity glyph, title "No activity yet", body "Start the relay, then tap Assist on your iPhone." |
| With entries | The log. It follows new entries while scrolled to the end; scrolling up stops the following |

The pairing token never appears in the log. Request and reply payloads are shown in full, as before; that is the point of the log.

---

## 5. Settings

Mockups: `win-settings-connection`, `win-settings-assistant`.

**Purpose.** The decisions the old scripts made silently, made visible: how the phone reaches the PC, whether the assistant is usable, and what the PC speech models are doing.

**Layout.** One scrolling page: header "Settings", then four panels in this order. "Assistant" and "PC speech models" sit side by side when the window is wide enough (7).

### 5.1 Connection

Panel title "Connection", with a neutral chip "Applies the next time the relay starts" **(new)**. Two columns.

Left column:

| Element | Content |
|---|---|
| Mode, `ConnectionModeSelector` | Segmented control (DS 4.4): "Private tailnet" / "Public Funnel" **(new control; the old mode was set by a hand-written file)** |
| Mode explanation | Private tailnet: "Reachable only from devices signed in to your tailnet. The iPhone needs Tailscale connected." Public Funnel: "Reachable from the internet over HTTPS and protected by the pairing token, not by tailnet membership. The iPhone needs only an internet connection." **(new)** |
| HTTPS port, `HttpsPortField` | Private tailnet: read-only, "443". Public Funnel: a segmented control with "443", "8443", "10000" |
| Path | Read-only, "/" |
| PC address, `SettingsAddressField` | Read-only, `type.mono`, with a copy icon button: the address the QR will advertise, built from this PC's Tailscale name, the port and the path |
| Caption | "Changing the mode or port changes the PC address. Paired iPhones must scan a new QR to use it." **(new)** |

Right column, labelled "Tailscale mapping":

| Element | Content |
|---|---|
| Status, `MappingStatus` | Inline indicator: success dot "Active"; warning dot "Not set up"; danger alert "Tailscale is not online"; neutral "Not managed by LiveCue" (Public Funnel) |
| Button, `SetUpMappingButton` | Primary "Set up mapping…" when not set up; secondary "Check again" when active |
| "What Set up does" | The exact command LiveCue will run, in `type.mono`, and one sentence saying what it changes, for example: "Tailscale serves `<PC address>` inside your tailnet and forwards it to the LiveCue relay on this PC (port `<relay port>`, this PC only). Nothing becomes reachable from the internet." **(new)** |
| Caption | "LiveCue shows the command and its effect here and asks once more before running it. In Public Funnel mode LiveCue changes nothing in Tailscale." **(new)** |

The command and its effect are on screen before the button is pressed, and the confirmation (6.2) repeats them. Both texts come from the connection code, so what is shown is what runs. In Public Funnel mode LiveCue makes no Tailscale changes, as before: an existing gateway on the PC is expected to forward to the relay, and the right column says so.

States: relay running (controls stay editable; the chip explains when changes take effect; if the saved settings differ from what the running relay uses, a warning banner "The relay is still using the previous connection settings. Stop and start it to apply the change." **(new)**); Tailscale not installed or offline (mode and port stay editable; status says so; Set up disabled with the fix as a caption); invalid combination (cannot be entered; the controls only offer valid values).

### 5.2 Assistant

Panel title "Assistant", with a secondary button "Check again", `CheckAssistantButton`. Two rows, each a list row with a trailing inline indicator:

| Row | Found | Not found |
|---|---|---|
| "Codex executable", `CodexPathStatus` | Subtitle: the path in `type.mono`, truncated in the middle. Indicator: success check, "Found" | Subtitle: "Install or update the Codex desktop app, then open it once and sign in." Indicator: danger alert, "Not found" |
| "Codex sign-in", `CodexLoginStatus` | Subtitle: none. Indicator: success check, "Signed in" | Subtitle: "Open Codex and sign in with ChatGPT, then choose Check again." Indicator: danger alert, "Not signed in". If the executable is missing: neutral, "Not checked" |

The check runs when the view opens, on "Check again" and on Start.

### 5.3 PC speech models

Panel title "PC speech models". Read-only.

| Row | Value |
|---|---|
| "Lab folder", `SpeechLabStatus` | The folder path in `type.mono` with success "Found", or "Not set up" in `status.warning` |
| "Docker Desktop", `DockerStatus` | "Running" (success dot), "Not running" (neutral ring), or "Not installed" (warning) |
| "Loaded model", `LoadedModelStatus` | "None"; "`<model name>` · loading `N` s"; "`<model name>` · ready"; "`<model name>` · in use"; or an error message in `status.danger` |

Caption: "Read-only. Models are started and stopped from the iPhone: Settings, Transcription, PC speech model. Start opens Docker Desktop if needed; one model runs at a time." **(new)**. When the lab folder is not set up, the caption is the setup hint instead: "PC speech models are optional. Without the lab folder, choose Meta Muse, Voz or Parakeet on the iPhone." **(new)**

### 5.4 About

A compact panel: "Version" and the app version; "Licence" and "MIT"; a quiet button with the external-link glyph, "Source code on GitHub", which opens the repository in the browser.

---

## 6. Dialogs and messages

Dialogs are in-window (DS 4.10): a scrim over the whole window including the sidebar, Escape cancels, focus is trapped, and focus returns to the button that opened the dialog.

### 6.1 Reset pairing

| Part | Content |
|---|---|
| Title | "Reset pairing?" |
| Body | "This replaces the pairing code. Every iPhone paired with this PC must scan the new QR and pair again. The relay keeps running." |
| Buttons | "Cancel" (secondary, initial focus, Escape); "Reset pairing" (destructive primary). Enter does not confirm; the destructive button must be chosen |

`ResetPairingDialog`, `ConfirmResetPairingButton`.

### 6.2 Set up Tailscale mapping

| Part | Content |
|---|---|
| Title | "Set up the Tailscale mapping?" |
| Body | "LiveCue will run:" then the command in a read-only `type.mono` field, then the effect sentence from 5.1 |
| Buttons | "Cancel"; "Set up" (primary, default) |

While it runs, the primary button is busy. On success the dialog closes and the status reads "Active". On failure the dialog stays and shows a danger banner: "Tailscale HTTPS setup failed. Check that Tailscale is connected." `SetUpMappingDialog`.

### 6.3 Toasts

| Text | When |
|---|---|
| "Copied" | A copy icon button was used |
| "Pairing text copied. It contains the pairing token; paste it only into LiveCue." | Copy pairing text (8 seconds) |
| "LiveCue is already running." | A second launch |

### 6.4 Startup failures

If the app cannot build its window or start at all, a system message box titled "LiveCue" shows "LiveCue could not finish startup." followed by the reason **(reworded; the old box pointed at a diagnostic launcher that no longer exists)**.

---

## 7. Resize behaviour

The sidebar is always 232 wide and the title bar 48 high. The content area takes the rest.

| Width of the window | Live | Pairing | Settings |
|---|---|---|---|
| 960 (minimum) to 1100 | Two panels side by side at 0.88 : 1.12, each about 320 wide; the status strip wraps the PC address under the two statuses if it does not fit | QR panel 304, right panel takes the rest; the two buttons stay on one line | Connection's two columns stack; Assistant and PC speech models stack |
| 1100 to 1400 | The same proportions, wider | The same | Connection in two columns; Assistant and PC speech models side by side |
| Above 1400 | Text inside the two cards stops growing at 720 wide and stays left-aligned; the panels keep filling | The right panel's text stops at 720 | Panels fill; text stops at 720 |

Height:

- Live: the two cards take all remaining height, with a floor of 120; their text scrolls. At 620 high each card is about 250 tall.
- Pairing: the QR tile never shrinks below 200 x 200 of code. At the minimum height the page scrolls rather than squeeze the tile or the reset panel.
- Activity: the log takes all remaining height.
- Settings: always scrolls.

Maximised: the layout is the widest row above; the root gets the margin that keeps a chrome-less window inside the work area (DS 3.5). At Windows text scale above 150% (DS 6.1) every page scrolls and side-by-side panels stack.

---

## 8. Keyboard and accessibility

- Tab order: sidebar rows, page header actions, then the page top to bottom. The title bar buttons are not tab stops.
- Ctrl+1 to Ctrl+4 select Live, Pairing, Activity, Settings **(new)**. Escape closes the open dialog. The old window's only shortcut, Escape to close its overlays, is covered by that.
- Enter on "Start relay" starts; there is no global shortcut that stops the relay.
- Status changes are announced: relay status politely, "Request failed" and "Cannot start yet" assertively, phone status politely and at most once per change.
- The QR's automation name is "Pairing QR code" plus its state ("shown, hides in 1 minute 42 seconds", "hidden"). The pairing text is never exposed to assistive technology.
- The transcript, the reply and the log are read-only text that can be selected and read line by line. New speech text is not announced.
- High contrast, Reduce Motion and text scaling follow DS 6.

---

## 9. Automation ids

For the window self-test that replaces the old one (spec 1.11). Old names are the element names in the old window.

| New id | Element | Old name |
|---|---|---|
| `StartRelayButton` | Start relay | `StartButton` |
| `PauseRelayButton` | Pause relay / Resume relay | `PauseButton` |
| `StopRelayButton` | Stop relay | `StopButton` |
| `RelayStatusText` | Relay status on Live | `Status` |
| `PhoneStatusText` | iPhone status on Live | `PhoneStatus`, `PhoneDot` |
| `PcAddressText` | PC address on Live | `Endpoint` |
| `TranscriptHint`, `TranscriptText` | Incoming transcript | `TranscriptHint`, `Transcript` |
| `ReplyHint`, `ReplyText`, `ReplyTiming` | Assistant reply | `ResponseTime`, `Reply` |
| `PrerequisiteList` | Prerequisite check list | none |
| `NavLive`, `NavPairing`, `NavActivity`, `NavSettings` | Sidebar rows | `ShowPairButton`, `PairFooterButton`, `ShowActivityButton` |
| `SidebarRelayStatus`, `SidebarPhoneStatus` | Sidebar status card | none |
| `PairingQr` | QR tile | `QRCanvas`, `QRPlaceholder` |
| `ShowQrButton`, `HideQrButton` | Show QR, Hide QR | `PairButton`, `HideQRButton` |
| `PairingHint` | Hint text | `QRHint` |
| `PairingAddressField` | PC address on Pairing | `Endpoint` |
| `PairingVerifyNotice` | Verification banner | none |
| `CopyPairingTextButton` | Copy pairing text | none |
| `ResetPairingButton`, `ResetPairingDialog`, `ConfirmResetPairingButton` | Reset pairing | none |
| `ActivityLog` | Log | `Activity` |
| `ClearActivityButton` | Clear activity | `ClearButton` |
| `ConnectionModeSelector`, `HttpsPortField`, `SettingsAddressField`, `MappingStatus`, `SetUpMappingButton`, `SetUpMappingDialog` | Settings, Connection | none |
| `CodexPathStatus`, `CodexLoginStatus`, `CheckAssistantButton` | Settings, Assistant | none |
| `SpeechLabStatus`, `DockerStatus`, `LoadedModelStatus` | Settings, PC speech models | none |

The old self-test's assertions map directly: a synthetic reply of 850 ms must put "0.85" in `ReplyHint`; a pause event must switch `PauseRelayButton` to "Resume relay"; a pairing event must draw modules in `PairingQr`; "Clear activity" must empty `ActivityLog`, `TranscriptText` and `ReplyText`.

---

## 10. What changed from the old window

| Old | New | Why |
|---|---|---|
| One view with two modal overlays (pairing, activity) | Sidebar with Live, Pairing, Activity, Settings | The approved shell; more to show now that the relay is built in |
| "Pair iPhone" in the toolbar and "Scan to pair" in a footer card | The Pairing sidebar row | One way to one place |
| Connection footer card repeating the phone status | Sidebar status card, always visible | The status is visible from every view |
| "Stop relay" inside the activity overlay | In the Live header beside Pause | Stop belongs with Start |
| A prerequisite failure replaced the status with the first error | A list of every check with its result and fix | Several things can be wrong at once |
| Connection mode chosen by a hand-written file | A control in Settings with the resulting address shown | Scripts are gone; the choice needs a home |
| The Tailscale mapping was set silently on Start | Shown in Settings with the exact command and effect, set after a confirmation | The owner is careful about changes to the PC |
| Reset pairing only from a terminal | A separated, confirmed action on Pairing | No terminal any more |
| Pairing text printed by the terminal launcher | "Copy pairing text" while the QR is shown | Keeps the paste fallback the iPhone app still offers |
| PC relay total time in a tooltip | A visible caption under the reply | A glance should not need a hover |
| A static five-bar strip under the transcript | Removed | It was decoration that needed a tooltip to say it meant nothing |
| "Closing LiveCue stops its relay." in the activity overlay | In the sidebar, always visible | The consequence should be visible before closing |
| Node.js and dependency checks | Removed | The relay is part of the app |
| No Codex or speech-model status anywhere | Settings panels with what was found and the fix | These were silent script checks |

Not added, by decision: a tray icon, start at sign-in, a list of paired devices, a per-device revoke, persistence of the activity log, and a confirmation on close.

One spec item has a new home rather than an old one: the terminal launcher's clear-text pairing payload (spec 2.4) becomes "Copy pairing text". One item is intentionally gone: the decorative transcript strip. Everything else in spec section 1 is on one of the four views above.
