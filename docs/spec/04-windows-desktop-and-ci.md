# Spec 04: Windows desktop app, launch/build scripts, CI and public-source audit

Source of truth: the old LiveCue repository at version 0.5.8 (files `Desktop.xaml`, `DesktopLauncher.cs`,
`Build-LiveCueDesktop.ps1`, `Build-LiveCueIcon.ps1`, `Start-LiveCueDesktop.ps1`, `Start-LiveCueRelay.ps1`,
`Load-LiveCueConnection.ps1`, `Load-LiveCueSpeechKey.ps1`, `LiveCue Desktop.cmd`, `README.md`, `.gitignore`,
`Scripts/*`, `.github/workflows/*`, `Relay/src/desktop.ts`, `Relay/test/desktop.test.ts`).

This document describes what the old Windows side does, not how the remake should be built. Where the old
behaviour is a bug or contradicts the README, it is called out in section 8 rather than silently "fixed".

Conventions used below:

- "Loopback" means the local-only network interface of the PC. No literal addresses appear in this file.
- `<PC name>` means the PC's own MagicDNS name on the owner's private Tailscale network.
- `<data folder>` means the per-user local application data folder for LiveCue (old: `%LOCALAPPDATA%\LiveCue`).
- `<old port>` is the old relay's fixed loopback port, `47831`. The remake runs side by side and must use a
  different port and a different data folder; every place the old code hard-codes the port is listed.
- Strings in "double quotes" are exact user-facing text. A slash-separated pair such as `A / B` inside a
  string is literal.

---

## 1. Desktop window

### 1.1 What it is

A single native window (old implementation: WPF XAML loaded at runtime by a Windows PowerShell 5.1 script,
hosted by a tiny compiled launcher so no console appears). It is a companion for the relay process, not a
general settings app. It:

1. checks prerequisites and starts the relay as a child process;
2. shows relay state and whether an authenticated phone has been seen recently;
3. shows the pairing QR code on demand;
4. mirrors the text of the most recent phone request and the answer that was sent back;
5. keeps a bounded in-memory activity log;
6. pauses/resumes acceptance of new requests and stops the relay.

It has **no** tray icon, no minimise-to-tray, no start-at-sign-in, no single-instance guard, no settings
screen, no persistence of its own, and no list of paired devices (see 1.9).

### 1.2 Window frame

| Property | Old value |
| --- | --- |
| Title | "LiveCue" |
| Default size | 1240 x 660, centred on screen |
| Minimum size | 960 x 620 |
| Chrome | Custom: 62 px draggable caption area, 6 px resize border, rounded 12 px corners, no system caption buttons |
| Caption buttons | Minimize, Maximize (toggles Maximized/Normal), Close. Accessible names "Minimize", "Maximize", "Close" |
| Window icon | Multi-resolution `.ico` generated from the app PNG |
| Keyboard | `Esc` closes both overlays (pairing and activity). No other shortcuts |

### 1.3 Layout and elements

Five stacked rows plus two modal overlays.

**Row 1: title bar.** App icon (44 px) and the text "LiveCue". Caption buttons on the right.

**Row 2: status and primary actions.**

| Element | Initial state | Notes |
| --- | --- | --- |
| Phone status dot | Grey | Grey = not connected, green = connected, amber = idle |
| Phone status text | "iPhone not connected" | Tooltip (once a phone has been seen): "Last authenticated heartbeat: N seconds ago" |
| Label "Relay: " + relay status text | "Stopped" | Single line, truncated with ellipsis at about 210 px; tooltip always shows the full status text |
| Button "Start relay" | Visible, enabled | Primary style. Shares one slot with Pause |
| Button "Pause relay" / "Resume relay" | Hidden, disabled | Replaces Start while the relay is ready |
| Button "Pair iPhone" (link icon) | Enabled | Opens the pairing overlay |

**Row 3: two cards side by side** (left slightly narrower than right, about 0.88 : 1.12).

Left card, heading "Incoming transcript":

- Hint line with a microphone icon. Initial text "Live from your iPhone". Changes with relay events (1.6).
- Bubble captioned "FROM IPHONE" containing a read-only, wrapping, scrollable text area. Initial text
  "Your next question will appear here."
- A static decorative strip of five bars over a line. Tooltip "Transcript indicator (not an audio waveform)".
  It never animates.

Right card, heading "Assistant reply":

- Hint line with a star icon. Initial text "Powered by Codex on your PC". Later shows timing (1.6).
- Bubble captioned "ASSISTANT" containing a read-only, wrapping, scrollable text area. Initial text
  "Ready when you are." / "Tap Assist on your iPhone." (two lines).

Both text areas grow with the window: on every resize their maximum height is recomputed from the card
height (transcript: card body height minus 130, reply: minus 88, never below 36).

**Row 4: connection footer card.**

- Phone icon, a small dot that mirrors the phone status dot colour, a line that mirrors the phone status
  text, and the caption "Your private iPhone-to-PC connection".
- Button (accessible name "Scan to pair"): QR icon, "Scan to pair", sub-caption "Open LiveCue on your
  iPhone", chevron. Same action as "Pair iPhone".

**Row 5: bottom strip.** Link-style button "Activity details" on the left; text "Activity stays in memory on
this PC" on the right.

**Pairing overlay** (dimmed backdrop over the whole window, hidden initially). Card titled "Pair your iPhone"
with a "Close" button.

- Left: a white square (about 244 px). When no QR is displayed it shows a placeholder: QR icon,
  "Your pairing code", "Generate a QR to get started". When a QR is displayed the placeholder is hidden and
  the QR is drawn on a white 240 x 240 canvas.
- Right: a hint paragraph (initial runtime text "Pair your iPhone once. Restarts and showing this QR keep
  existing pairings valid."), buttons "Show QR" (disabled until the relay is ready) and "Hide QR", the label
  "PC ADDRESS" above a read-only field showing the advertised endpoint URL, and the footnote "Pairing codes
  are private. Codes hide after 2 minutes."

**Activity overlay** (dimmed backdrop, hidden initially). Card titled "Activity details" with a "Close"
button.

- Body: read-only monospaced log. When the log is empty an overlay shows "No activity yet" and "Start the
  relay, then tap Assist on your iPhone."
- Footer: "Clear activity", "Stop relay" (disabled until the relay is ready) and the text "Closing LiveCue
  stops its relay."

### 1.4 Relay lifecycle as seen in the window

States of the relay status text and which controls are available:

| Status text | When | Start | Pause/Resume | Stop relay | Show QR |
| --- | --- | --- | --- | --- | --- |
| "Stopped" | At launch; after Stop relay | visible, enabled | hidden | disabled | disabled |
| "Checking Codex and Tailscale..." | Immediately after Start is pressed | disabled | hidden | disabled | disabled |
| (a prerequisite error message, see 2.4) | Start failed before the relay process existed | visible, enabled | hidden | disabled | disabled |
| "Ready" | Relay reported ready; after Resume; after a reply while not paused | hidden | "Pause relay", enabled | enabled | enabled |
| "Paused" | Relay confirmed pause; after a reply while paused | hidden | "Resume relay", enabled | enabled | enabled |
| "Thinking..." | An assist or summary request is being processed | hidden | enabled | enabled | enabled |
| "Request failed - ready to retry" | A tracked request failed or timed out | hidden | enabled | enabled | enabled |
| (fatal message from relay) | Relay could not bind its port or could not start | see next row | | | |
| "Relay stopped - click Start relay to reconnect" | Relay process exited on its own | visible, enabled | hidden | disabled | disabled |
| "Desktop connection interrupted. Stop and restart the relay." | The window failed while reading relay output | unchanged | unchanged | unchanged | unchanged |

Fatal messages produced by the relay child and shown verbatim as the status:

- "The relay port is already in use. Close the old LiveCue relay terminal, then click Start relay."
- "Could not start the relay."

When the relay process exits, the status is replaced by "Relay stopped - click Start relay to reconnect"
**unless** the current status text contains `port`, `failed` or `Could not` (so that the fatal reason stays
visible).

**Start.** Pressing "Start relay" (or launching with auto-start, 2.1):

1. disables Start and shows "Checking Codex and Tailscale...";
2. runs the prerequisite check (2.4) in check-only mode; any failure message becomes the status text and
   Start is re-enabled;
3. in private connection mode only, asks Tailscale to serve the relay port over tailnet HTTPS
   (`tailscale serve --bg <old port>`); failure shows "Tailscale HTTPS setup failed. Check that Tailscale is
   connected.";
4. starts the relay child process with redirected standard input, output and error and no window;
5. clears the activity log.

The window then waits for the relay's `ready` event (1.6).

**Pause / Resume.** Sends a pause command with the opposite of the current paused flag. The button label and
status change only when the relay confirms with a `state` event. While paused the relay refuses assist,
summary, PC-model control and speech streaming, but still answers authenticated health checks, so the phone
status keeps updating. Pause is not persisted; a restarted relay always accepts.

**Stop relay.** Sends a shutdown command, waits up to 2.5 s for the child to exit, then kills it. Resets:
status "Stopped", phone status "iPhone not connected" (grey), Start visible and enabled, Pause hidden, Stop /
Pause / Show QR disabled, QR cleared. It does not clear the transcript, reply, activity log or endpoint field.

**Closing the window** (caption Close or Alt+F4) stops the poll timer and performs Stop relay. There is no
confirmation prompt. Pending phone requests are cancelled by the relay as it shuts down.

**Relay exits by itself.** Detected by the poll timer: controls return to the stopped layout, QR cleared,
phone status reset, status text as described above. The window does **not** restart the relay.

### 1.5 Timers, intervals and limits

| Item | Value |
| --- | --- |
| Poll timer for relay output and derived state | every 150 ms |
| Relay output lines processed per tick | up to 16 from standard output, up to 1 from standard error |
| Phone "connected" threshold | last authenticated request at most 15 s ago; older = "iPhone idle" (amber) |
| Phone heartbeat (sent by the phone, for reference) | every 5 s while the iPhone app is active |
| QR auto-hide | 120 s after it was displayed |
| Graceful stop wait before kill | 2.5 s (the relay also force-exits itself 1.5 s after starting shutdown) |
| Advertised-endpoint verification timeout (relay side) | 10 s |
| Activity log cap | above 200,000 characters it is cut to the newest 150,000 with the first line "[Older activity removed from memory]" |
| Transcript area cap (streamed speech text) | above 64,000 characters it is cut to the newest 48,000 |
| Control command line length accepted by the relay | 1,024 characters; longer lines are ignored |

The phone status never returns to "iPhone not connected" by timeout. It goes connected -> idle and stays idle
until the relay stops.

### 1.6 Private control channel between window and relay

The window and the relay child talk only over the child's standard streams: one JSON object per line. Nothing
here is reachable over HTTP. Standard error lines are shown in the activity log prefixed "Relay diagnostic: ".

**Commands (window -> relay):**

| JSON | Effect |
| --- | --- |
| `{"action":"shutdown"}` | Cancel all running assistant requests, close the server, exit with code 0 |
| `{"action":"pause","paused":true\|false}` | Set accepting = not paused, then emit `state` |
| `{"action":"pair"}` | Re-read the saved pairing and emit `pairing` for the **existing** credential (see 1.8) |

Closing the relay's standard input, or SIGINT/SIGTERM, also triggers shutdown. A command that throws makes the
relay emit the notice "The desktop command failed. Please retry."

**Events (relay -> window):**

| `type` | Fields | Window reaction |
| --- | --- | --- |
| `ready` | `endpoint`, `model`, `accepting`, `cloudSpeechReady`, `port` | Status "Ready"; endpoint field = `endpoint`; Start hidden; Pause shown as "Pause relay"; Stop and Show QR enabled. Activity gets "Relay started. Waiting for your iPhone."; in Funnel mode also "Funnel enabled: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address."; then "Meta Muse is configured. The key remains on this PC." or "Meta cloud key not configured. Local transcription is still available." depending on `cloudSpeechReady` |
| `notice` | `message` | Appended to activity |
| `fatal` | `message` | Status = message; appended to activity |
| `state` | `accepting` | accepting false: status "Paused", button "Resume relay". accepting true: status "Ready", button "Pause relay" |
| `phone-seen` | `time` | Records "now" (the PC clock, not the event time); phone status "iPhone connected", green |
| `pairing` | `endpoint`, `modules` (square boolean matrix) | Opens the pairing overlay, draws the QR, starts the 120 s hide timer, sets the hint to "On iPhone: Pair Windows PC > Scan PC QR code, then Verify and pair. QR hides after 2 minutes.", sets the endpoint field |
| `request` | `requestId`, `kind` (`assist` or `summary`), `time`, `payload` | Status "Thinking..."; transcript area = `payload.transcript`, plus a blank line and "[In progress] " + `payload.partialTranscript` when present, plus a blank line and "Instruction: " + `payload.instruction` when present; if still empty, the payload as indented JSON. Transcript hint = "Session summary requested" for `summary`, otherwise "Received from your iPhone". Reply area = "Thinking...". Reply hint = "Processing with Codex on your PC". Activity gets "INCOMING <kind> [<requestId>]" followed by the full payload JSON |
| `reply` | `requestId`, `kind`, `durationMs`, `payload` | Reply area = `payload.answer`, or `payload.summary` if there is no answer; then optional `payload.details` after a blank line; optional "Key points" heading plus one `payload.keyPoints` item per line; optional "Action items" heading plus one `payload.actionItems` item per line; if still empty, the payload as JSON. Reply hint = "Sent to iPhone  /  <seconds, 2 decimals> s", or when `payload.execution` exists "<model> / <reasoningEffort> / <codex seconds, 2 decimals> s Codex" with tooltip "PC relay total: <seconds, 3 decimals> s. Codex includes startup and cloud processing.". Status = "Paused" if paused, else "Ready". Activity gets "REPLY [<requestId>] <seconds> s" followed by the full payload JSON |
| `request-error` | `requestId`, `message` | Status "Request failed - ready to retry"; reply area = message; reply hint "Not sent - try Assist again"; appended to activity. The relay's message is "Assistant request failed or timed out. Check Codex sign-in and retry." |
| `speech-status` | `message` | Appended to activity and shown as the transcript hint. Used for PC speech-model loading/unloading, cloud speech stream status and model-catalog errors |
| `speech-text` | `text` | Appended as a new line to the transcript area (subject to the 64,000 cap) and scrolled into view |

Unknown event types are ignored.

`phone-seen` is emitted for **every authenticated HTTP request** the relay receives (health, pairing
verification, catalog, assist, and so on), before routing. Unauthenticated requests never produce it.

Notices the relay emits on its own:

- at start with an existing pairing: "Saved pairing restored. Existing iPhones reconnect automatically; Show
  QR keeps their pairing valid.";
- after each QR display, the result of verifying the advertised endpoint: "Secure PC connection verified."
  (private mode), "Secure Funnel connection verified. The iPhone VPN is not required." (Funnel mode), or
  "Pairing code created, but the advertised connection could not be verified. Check the PC tunnel before
  pairing."

### 1.7 Data shown and where it comes from

| On screen | Source |
| --- | --- |
| Relay status | Window state machine driven by control-channel events and child process exit |
| Phone status and dot | Time since the last `phone-seen` event |
| PC address | `endpoint` from `ready` / `pairing`; ultimately the launcher's connection resolution (2.5) |
| QR code | `modules` from the `pairing` event. The window never receives the token as text |
| Transcript, reply, timing | `request`, `reply`, `request-error`, `speech-*` events |
| Activity log | Every notice/fatal/speech-status message, full request and reply payloads, relay standard error |

Everything is held in memory only. Nothing the window shows is written to disk, and all of it is lost when the
window closes. "Clear activity" empties the activity log, the transcript area and the reply area and sets the
reply hint to "Activity cleared".

### 1.8 Pairing QR display

- The QR encodes the JSON object `{"endpoint": <advertised HTTPS URL>, "token": <pairing token>}` at error
  correction level M with automatic version selection. It is generated inside the relay process and never
  sent to any remote QR service.
- Rendering: the matrix is drawn as black squares on a white 240 x 240 area with a quiet zone of four modules
  on every side (cell size = 240 / (module count + 8)).
- Ways to display it: "Pair iPhone" (toolbar) and "Scan to pair" (footer) open the overlay and, if the relay
  is running, immediately request the QR; "Show QR" inside the overlay requests it again. If the relay is
  not running the overlay opens with the placeholder and a disabled "Show QR".
- First ever start (no saved pairing): the relay creates a credential and emits `pairing` right after `ready`,
  so the overlay opens by itself.
- **Showing the QR never changes the credential.** The same token is redisplayed; already-paired phones stay
  paired. The saved credential is stored encrypted for the current Windows user so it can be redisplayed
  after restarts (details in the relay spec).
- Upgrade path: if the saved pairing comes from a release that stored only a hash, the first QR request
  creates a new displayable token and keeps the old hash valid as a secondary credential, so the old phone
  keeps working.
- Hiding: "Hide QR" clears it at once (hint "QR hidden. Existing pairing remains valid."); after 120 s it is
  cleared automatically (hint "QR hidden for privacy. Existing pairing remains valid."); stopping the relay
  clears it. Closing the overlay does not clear it, it only hides the overlay.
- Every QR display triggers one authenticated health request from the relay to its own advertised endpoint,
  with redirects refused, and the result is logged as a notice (1.6). The bearer is not logged.

### 1.9 Device management

There is none in the window. The old system has exactly one shared pairing credential (plus at most one
retained older hash from the upgrade path). The window cannot list phones, name them, or revoke one.

The only revocation is a full reset from the terminal launcher (`Start-LiveCueRelay.ps1 -ResetPairing`, 2.4),
which generates a new credential and invalidates every paired phone.

### 1.10 Error states (complete list)

| Situation | What the user sees |
| --- | --- |
| Launcher exe not beside the main script | Message box "Keep this launcher beside Start-LiveCueDesktop.ps1." (title "LiveCue Desktop") |
| Startup script reported errors | Message box "LiveCue could not finish startup. Run the diagnostic PowerShell launcher for details." |
| Launcher exception | Message box with the exception text |
| Prerequisite failure on Start | The message from 2.4 as the relay status; Start re-enabled |
| Tailscale serve failure on Start | "Tailscale HTTPS setup failed. Check that Tailscale is connected." |
| Port in use / relay cannot start | Fatal status text from 1.4, relay exits, Start re-enabled |
| Relay exits unexpectedly | "Relay stopped - click Start relay to reconnect" |
| Request failed or timed out | "Request failed - ready to retry", reply area shows the message, hint "Not sent - try Assist again" |
| Advertised endpoint cannot be verified | Activity notice only (1.6); the QR is still shown |
| Control command failed | Activity notice "The desktop command failed. Please retry." |
| Window cannot process relay output | "Desktop connection interrupted. Stop and restart the relay." |
| Saved speech credential cannot be decrypted | "Cannot unlock the local Meta credential. Use the Windows account that saved it." as the status |

### 1.11 Built-in self-test mode

`Start-LiveCueDesktop.ps1 -SelfTest [-EvidencePath <png>] [-TestExpanded]` builds the window without
starting a relay, feeds it synthetic events (ready with a placeholder endpoint on a reserved test domain, a
pairing matrix from a synthetic fixture, phone-seen, an assist request, a reply of 850 ms), and asserts:

- activity and reply contain the answer, transcript contains the question, the timing hint contains "0.85",
  Show QR is enabled;
- the QR canvas has drawn modules;
- a `state` event with accepting false sets the paused flag, and true clears it;
- after an optional screenshot of the rendered window to `-EvidencePath` (with `-TestExpanded` the pairing
  overlay stays open and the window is 900 x 650), invoking "Clear activity" empties transcript, reply and
  activity.

It prints "Desktop self-test passed" and exits 0, or throws. It needs Node.js and the relay's installed
dependencies for the QR fixture. It is not run by CI.

---

## 2. Launch and build scripts

### 2.1 `LiveCue Desktop.cmd` (what the owner double-clicks)

1. If `LiveCue Desktop Fluent.exe` is not beside it, run `Build-LiveCueDesktop.ps1` with Windows PowerShell,
   no profile, execution policy bypass.
2. Start `LiveCue Desktop Fluent.exe` detached.

The exe is never rebuilt once it exists.

### 2.2 `Build-LiveCueDesktop.ps1` and `DesktopLauncher.cs`

Build script, no parameters:

1. Require the 64-bit .NET Framework 4 C# compiler that ships with Windows; otherwise "The Windows .NET
   Framework C# compiler is required."
2. Locate the PowerShell automation assembly in the global assembly cache.
3. Run `Build-LiveCueIcon.ps1`.
4. Compile `DesktopLauncher.cs` as a windowed executable named `LiveCue Desktop Fluent.exe` in the
   repository root, with the generated icon. Failure: "Desktop launcher compilation failed."
5. Print "LiveCue Desktop Fluent.exe is ready."

Launcher behaviour: single-threaded-apartment windowed program that hosts the Windows PowerShell engine
in-process (execution policy bypass for that host only), runs `Start-LiveCueDesktop.ps1 -AutoStart` from its
own folder on the UI thread, and shows the message boxes listed in 1.10. Because of `-AutoStart`, the relay
start sequence begins as soon as the window has rendered; the user does not need to press "Start relay".

The exe is ignored by git (both the current name and an older name `LiveCue Desktop.exe`).

### 2.3 `Build-LiveCueIcon.ps1`

Reads `Assets/LiveCue.png`, renders it at 16, 24, 32, 48, 64, 128 and 256 px with high-quality bicubic
scaling, and writes `Assets/LiveCue.ico` as a multi-image icon whose entries are PNG-compressed, 32-bit. The
`.ico` is a tracked file and is rewritten on every exe build.

`Scripts/Prepare-MintIcon.ps1 -Source <image>` is the companion for the phone icon: it scales any image to
1024 x 1024 PNG and overwrites the iOS app icon asset.

### 2.4 `Start-LiveCueRelay.ps1` (prerequisite check and terminal launcher)

Parameters: `-CheckOnly` (validate and set up the environment, then return) and `-ResetPairing` (start the
terminal relay with a new pairing credential).

Steps, in order, with the exact failure messages:

1. Tailscale client must be installed in the default program folder: "Tailscale is not installed in Program
   Files."
2. `node` must be on PATH: "Node.js 24 or newer is required." (presence only; the version is not checked).
3. The relay's dependency folder must exist: "Relay dependencies are missing. Run npm install once inside
   the Relay folder."
4. Find the Codex CLI: first `codex` on PATH; otherwise the most recently modified `codex.exe` anywhere under
   the Codex desktop app's per-user `bin` folder. None: "Codex CLI was not found. Install or update the Codex
   desktop app, then open it once and sign in."
5. Run `codex login status`. Non-zero exit: "Codex is installed but not signed in. Open Codex and sign in
   with ChatGPT." followed by the tool's own output. On success export `LIVECUE_CODEX_EXECUTABLE`.
6. Run `tailscale status --json`. Not online: "Tailscale is not online. Open Tailscale and connect first."
   No DNS name: "Tailscale did not return this PC DNS name." The trailing dot is removed from the name.
7. Resolve the connection (2.5) and export `LIVECUE_CONNECTION_MODE` (`private` or `funnel`),
   `LIVECUE_PUBLIC_ENDPOINT` (the advertised HTTPS URL) and `LIVECUE_PORT` (the fixed `<old port>`).
8. Print "Codex ready: <path>" and "Tailscale ready: <PC name>". With `-CheckOnly`, print "LiveCue Relay
   prerequisites passed." and return here.
9. Private mode only: `tailscale serve --bg <old port>`; failure "Tailscale Serve setup failed."
10. Print "LiveCue authenticated endpoint: <URL> (<mode>)".
11. In the relay folder: load the speech key (2.6) into `LIVECUE_META_API_KEY` for this process, run the
    relay's `start` script (or `reset-pairing` with `-ResetPairing`), and always remove that variable and
    restore the folder afterwards.

The terminal relay (`Relay/src/index.ts`) is a foreground console process with no pause control and no
activity mirror. It prints "LiveCue Relay ready on <URL>", the default model line, and, when the saved
credential can be displayed, the line "Paste this pairing payload into LiveCue:" followed by the pairing JSON
in clear text and a small terminal QR, then "This pairing is saved. Restarting the relay keeps paired phones
connected." Otherwise it prints "Using the existing pairing token. Run npm run reset-pairing to pair a new
phone." This clear-text payload is the "manual JSON fallback" for phones whose camera is unavailable. It stops
on Ctrl+C.

The desktop window calls this script with `-CheckOnly` in its own process, so the exported environment
variables are inherited by the relay child it starts next.

### 2.5 `Load-LiveCueConnection.ps1`: how the advertised endpoint is chosen

`Get-LiveCueConnection <PC name>` reads an optional, hand-written file `connection.json` in `<data folder>`:

- File absent: mode `private`, endpoint `https://<PC name>/` (tailnet-only HTTPS through Tailscale Serve).
- File present: it must contain `mode` = `funnel` and an `endpoint` that is an HTTPS URL whose host is
  exactly this PC's own Tailscale DNS name, whose path is exactly `/`, with no query, fragment or user info,
  and whose port is one of 443, 8443 or 10000. Anything else: "Invalid local LiveCue Funnel configuration."
  Result: mode `funnel`, endpoint as written.

In Funnel mode the launcher makes **no** Tailscale changes; an already-running authenticated gateway on the PC
is expected to forward the relay's routes (README "Optional shared Funnel access"; the adapter is
`Relay/funnel/livecue-proxy.mjs`). Storage type: plain JSON file in the per-user local application data
folder; it contains no secret.

### 2.6 `Load-LiveCueSpeechKey.ps1`: how the speech key is loaded

`Get-LiveCueSpeechKey` reads an optional file `meta-stt-key.xml` in `<data folder>`. Storage type: a
PowerShell-serialised credential object whose secret part is encrypted with the Windows data protection API
for the current user, so only the same Windows account on the same PC can read it. Absent file: returns
nothing (cloud speech through the relay is simply "not configured"). Unreadable: "Cannot unlock the local
Meta credential. Use the Windows account that saved it."

No script in the repository creates this file; the owner saved it by hand.

How the key travels: the desktop puts it only into the environment of the relay child process
(`LIVECUE_META_API_KEY`) and removes it from its own start-info and variables right after the child starts.
The relay strips it from the environment of every assistant (Codex) subprocess. It is never written to
logs, events, the QR or the repository.

The assistant model needs no key on the PC: it uses the Codex CLI's own ChatGPT sign-in, checked in 2.4.

Pairing state (`relay.json` in `<data folder>`) is owned by the relay; see the relay spec.

### 2.7 Relay supervision summary

- The relay is a direct child of the window (desktop mode) or of the terminal (terminal mode). No Windows
  service, scheduled task, or watchdog exists.
- Command line: `node --experimental-strip-types <relay>/src/desktop.ts`, working folder = the relay folder,
  no shell, no console window.
- The relay binds only to loopback on `<old port>` and refuses to start in desktop mode unless the advertised
  endpoint is HTTPS ("Start this service through LiveCue Desktop with Tailscale connected.").
- Liveness is inferred from the process handle on the 150 ms timer. There is no automatic restart and no
  back-off; the user presses Start again.
- Shutdown is cooperative (command, then 2.5 s, then kill). If the window dies, the relay sees its standard
  input close and shuts itself down, so no orphan relay is left listening.
- The Tailscale Serve mapping created in private mode is persistent Tailscale configuration and is **not**
  removed on stop or exit.

### 2.8 Other files in `Scripts/`

| File | Purpose |
| --- | --- |
| `Audit-PublicSource.ps1` | Pre-publication secret guard, section 4 |
| `Simulator.entitlements` | Entitlements applied only to simulator builds: application identifier and keychain access group equal to the app bundle identifier, debugging allowed. Lets Keychain-backed code run under ad-hoc signing in simulator tests |
| `select_simulator.py`, `boot_simulator.py` | CI simulator selection and boot, section 3 |
| `verify_test_reuse.py`, `test_verify_test_reuse.py` | CI test-reuse gate and its unit tests, section 3 |
| `Test-CloudRelay.mjs` | Manual, paid smoke test of the relay's cloud speech proxy. Takes a mono 16-bit 24 kHz WAV under 30 s, requires the speech key in the environment ("Credential not loaded." otherwise), starts an in-process relay on an ephemeral loopback port with a fixed synthetic token, streams the audio plus one second of silence in 80 ms frames in real time, ends the stream, and requires at least one partial and one final transcript within 45 s. Prints a one-line JSON result. Never run by CI |

### 2.9 `.gitignore` (what the old repo refuses to track)

Build output and tooling: `.DS_Store`, `.build/`, `DerivedData/`, the generated Xcode project,
`Relay/node_modules/`, `Relay/coverage/`, `*.xcresult`, `*.ipa`, `*.sha256`, `artifacts/`, both launcher exe
names.

Secrets and private data ("must never be published"): `.env`, `.env.*` (except `.env.example`), `*.pem`,
`*.key`, `*.p12`, `*.mobileprovision`, `relay.json`, `pairing*.json`, `auth.json`, `*key*.xml`,
`*credential*.xml`, `*secret*.json`, `*.log`, `*.wav`, `*.m4a`, `*.caf`, `*.sqlite`, `*.sqlite-*`,
`*.bundle`.

---

## 3. CI workflows

All three run on free GitHub-hosted runners in a public repository, need no repository secrets (only the
automatic workflow token), and never contact a speech or model provider. Every job pins the toolchain with
`DEVELOPER_DIR` pointing at Xcode 26.3. The Xcode project is generated in CI with XcodeGen (`brew install
xcodegen`, `xcodegen generate`) from `project.yml`; it is not committed.

### 3.1 "iOS simulator tests" (`ios-simulator.yml`)

| Item | Value |
| --- | --- |
| Triggers | manual dispatch; push to `main`; every pull request |
| Concurrency | one run per ref (group `livecue-simulator-<ref>`), newer run cancels the older |
| Permissions | contents: read |
| Runner | `macos-26` (Apple Silicon is required by a pinned on-device speech dependency's simulator artefact) |
| Job timeout | 40 minutes |

Steps:

1. Check out.
2. Print the Xcode version.
3. Install XcodeGen and generate the project.
4. Choose a simulator (3.4) and expose its identifier as a step output.
5. Boot it (3.5), step timeout 10 minutes.
6. `xcodebuild test` for the app scheme on that simulator, parallel testing disabled, result bundle written
   to `artifacts/LiveCueTests.xcresult`, ad-hoc signing enabled for the simulator
   (`CODE_SIGNING_ALLOWED=YES CODE_SIGN_STYLE=Automatic CODE_SIGN_IDENTITY=-`), console output also saved to
   `artifacts/xcodebuild.log`, pipeline fails if xcodebuild fails. The scheme runs both the unit-test bundle
   and the UI-test bundle and gathers coverage.
7. Always (even after failure, 2-minute limit, each command best-effort): boot the simulator, save a final
   screenshot to `artifacts/final-screen.png`, export every test attachment from the result bundle to
   `artifacts/screenshots`.
8. Always: upload `artifacts/` as artefact `livecue-test-evidence`, warn if empty, retention 14 days.

### 3.2 "Test and build LiveContainer IPA" (`ios-release.yml`)

Trigger: manual dispatch only, with inputs:

- `release_tag` (required, default the current version tag, for example `v0.5.8`);
- `reuse_simulator_run` (optional run id; "Optional passing simulator run from the last 24h; only
  bundle-version metadata repairs may reuse it").

Default permissions: contents: read.

**Job 1, `verify-test-reuse`** (`ubuntu-latest`, 5 minutes, permissions contents: read + actions: read,
output `reused`):

1. Check out with full history.
2. Run the Python unit tests for the reuse gate.
3. Run the reuse gate (3.6) with the workflow token and the optional run id.

**Job 2, `simulator-test`** (`macos-26`, 40 minutes; needs job 1; skipped when `reused` is `true`): the same
sequence as 3.1 with these differences: the boot step first prints the Xcode version and the installed
runtimes; the test step has a 20-minute limit and enables per-test timeouts (default allowance 120 s,
maximum 180 s); the artefact is named `livecue-release-test-evidence` (retention 14 days).

**Job 3, `build-and-release`** (`macos-15`, 35 minutes, permissions contents: write; needs jobs 1 and 2;
runs only if job 1 succeeded **and** (job 2 succeeded **or** `reused` is `true`), evaluated even when job 2
was skipped):

1. Check out, install XcodeGen, generate the project.
2. Build the unsigned device app: `xcodebuild build`, Release configuration, iPhone OS SDK, generic iOS
   destination, derived data under the runner temp folder, signing fully disabled
   (`CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY=""`), log saved.
3. Package and validate, failing on the first violated check:
   - the `.app` folder exists in `Release-iphoneos` and contains the main executable;
   - `CFBundleIdentifier` equals the app's expected bundle identifier;
   - the release tag equals `v` + `CFBundleShortVersionString`;
   - the executable is reported as arm64;
   - copy the app to `Payload/LiveCue.app` and zip `Payload` (keeping the parent folder) into `LiveCue.ipa`
     with `ditto`;
   - the archive passes an integrity test;
   - write `LiveCue.ipa.sha256` (SHA-256 in `shasum` format).
4. Upload artefact `livecue-device-build` (the IPA, the checksum file and the device build log), retention 14
   days.
5. Publish: if a release for the tag already exists, replace its two assets and repoint the release at the
   current commit; otherwise create the release at the current commit. Title "LiveCue <tag>". Notes:
   "Unsigned iPhoneOS IPA for personal use through LiveContainer. Simulator tests passed before this build."
6. Re-download the two published assets into a clean folder and verify: checksum matches, archive integrity
   passes, the listing contains `Payload/LiveCue.app/LiveCue`.

The IPA is unsigned, intended to be installed through LiveContainer, and is not an App Store or TestFlight
build.

### 3.3 "Export simulator screenshots" (`export-evidence.yml`)

Manual dispatch with a required `run_id` ("Build run containing livecue-release-test-evidence"). Permissions
contents: read + actions: read. One job on `macos-15`, 10 minutes: download the artefact
`livecue-release-test-evidence` from that run, export the attachments of `LiveCueTests.xcresult`, upload them
as artefact `inspected-simulator-screenshots` with 7 days retention.

### 3.4 Simulator selection (`Scripts/select_simulator.py`)

1. List available simulator devices as JSON.
2. Consider only iOS runtimes whose version is **not newer than 26.2** (the SDK shipped with the pinned
   Xcode), and only devices that are available and whose name starts with "iPhone".
3. If none: fail with "No available iPhone simulator was found".
4. Sort by newest runtime first, then by this preference order: iPhone 17 Pro Max, iPhone 17 Pro, iPhone 17,
   iPhone 16 Pro Max, iPhone 16 Pro, iPhone 16, then any other iPhone by name.
5. Print "Selected <device> on <runtime>" to standard error and the device identifier to standard output.

### 3.5 Simulator boot (`Scripts/boot_simulator.py <device id>`)

Up to three attempts. Each attempt boots the device if needed and waits for boot completion with a 180 s
limit ("Simulator boot attempt N/3"). On timeout or failure, unless it was the third attempt, print "Cold boot
stalled; shutting down this test simulator before retry.", shut the device down (20 s limit, failure
ignored) and retry. The third failure fails the step.

### 3.6 Test-reuse gate (`Scripts/verify_test_reuse.py`)

Purpose: allow a release to skip the 20-40 minute simulator job when the only change since a recently tested
commit is a repair of bundle-version metadata. It fails closed: any doubt raises an error and blocks the
release.

With no run id supplied it writes `reused=false` and exits successfully. With a run id, all of the following
must hold, otherwise the job fails:

1. the id is digits only;
2. the run belongs to this repository, is a run of the release workflow file, and is completed;
3. the run was created between 0 and 24 hours ago;
4. that run has a job named `simulator-test` whose conclusion is success;
5. the run's commit is a full 40-character hash and is an ancestor of the commit being released;
6. the files changed between that commit and the current one are a subset of: `project.yml`, the release
   workflow file, the gate script and its test file;
7. `project.yml` before and after are identical once the two bundle-version fields are masked. Each of
   `CFBundleShortVersionString` and `CFBundleVersion` must occur exactly once and be well formed (a literal
   `x.y.z` / integer, or the build-setting placeholder), else the gate fails;
8. the `DEVELOPER_DIR` line of the release workflow is unchanged.

Success prints "Verified passing simulator run <id> at <commit>; app source/tests unchanged, bundle-version
metadata repair only." and writes `reused=true`.

The unit tests cover: the version repair is accepted; any change to app source, UI tests, the package lock
file or the boot script is rejected; any other `project.yml` change is rejected; duplicate or malformed
version fields are rejected.

### 3.7 What CI depends on in `project.yml`

Deployment target iOS 18.0; iPhone only; portrait only; two pinned Swift packages (exact versions); targets:
core framework, app, unit tests, UI tests; one scheme that tests both bundles with coverage; marketing version
and build number as build settings; signing disabled for device builds; simulator-only entitlements file.
The app bundle identifier and the marketing version are the two values the release job asserts.

### 3.8 What CI does **not** cover

- The relay's Node test suite (it needs Windows user-scoped encryption) is never run in CI.
- The desktop self-test is never run in CI. There is no Windows runner at all.
- The public-source audit is not run in CI (it needs the local secret, section 4).
- No signing, no notarisation, no device tests.

---

## 4. Public-source audit (`Scripts/Audit-PublicSource.ps1`)

A local, manual gate run before pushing to the public repository. It is not a git hook and not part of CI.

1. Load the locally stored speech key (2.6). If it is not available the script **refuses to pass**: "Local
   credential unavailable; exact-key audit not performed."
2. Build the file list from git: every tracked file plus every untracked file that is not ignored,
   de-duplicated. Paths that no longer exist on disk are skipped.
3. For each file, read the raw bytes and decode them twice, as UTF-8 and as UTF-16 little-endian. Flag the
   file if any of these is true:
   - **Exact secret:** the UTF-8 or UTF-16 text contains the real key, or the UTF-8 text contains the Base64
     encoding of the key;
   - **Generic secret shapes** (UTF-8 text only):
     - a token starting with `LLM_` followed by at least 20 key characters, when not preceded by a letter,
       digit or underscore (the script does not say which provider this shape belongs to; presumably the
       cloud speech key);
     - an OpenRouter-style key: the `sk-or-v1-` prefix followed by at least 20 key characters;
     - a PEM private-key header (unqualified, RSA, EC or OPENSSH);
     - a GitHub token: `gh` + one of `p o u s r` + `_` followed by at least 30 letters or digits;
   - **Forbidden path names** (case-insensitive, anywhere in the relative path): `meta-stt-key`,
     `relay.json`, `auth.json`, a path ending in `.env`, a path ending in `.wav`, or `credential` followed
     later by `.xml` at the end.
4. Forget the key. If anything was flagged: fail with "Publication blocked; inspect these paths locally:
   <comma-separated paths>". Otherwise print "Public-source audit passed: N files; no exact API key, encoded
   key, private key or credential file."

Limits of the old guard, relevant when writing its replacement: it scans the working tree only, not git
history; it does not look for hostnames, tailnet names, addresses, e-mail addresses, pairing tokens or
transcripts; the generic patterns are not applied to UTF-16 text; it cannot run where the local key is
absent.

---

## 5. README claims

Every promise the old README makes to a user. Items about the iPhone app are listed for completeness and are
specified in detail in the iOS spec files.

### 5.1 Product and platform

1. LiveCue is a personal iPhone conversation assistant with public source and an optional private Windows
   relay.
2. Requires iOS 18 or later.
3. The IPA is unsigned, meant for LiveContainer, and is not an App Store/TestFlight build.
4. Development versions stay below 1.0 with patch increments; build numbers may rise independently; the
   release workflow checks that its tag matches the IPA version.
5. Default body text is 16 pt and follows Dynamic Type.

### 5.2 Conversation screen (0.5.8)

6. Compact listening/status bar, a two-line expandable live-caption strip, and an inline answer feed with the
   newest answer first and earlier answers still readable.
7. Pause, Assist and Stop stay pinned outside the scrolling content.
8. Cost is shown only for cloud transcription; a small microphone meter reacts to speech.
9. Partial answer text is shown live while it streams.
10. A failed or truncated answer is labelled incomplete, is never saved as a completed answer and never
    advances the conversation context; an inline error offers an explicit retry with the same text.
11. Conversation context is a bounded recent tail kept locally, not a model-written summary.
12. Provider reasoning and raw provider error payloads are never shown.
13. Usage comes from the provider's final accounting frame when supplied; missing cost is shown as unknown,
    never as free.
14. No automatic retries and no model fallback.
15. While a conversation runs there are no navigation tabs until Stop (0.5.1); optional assistant
    instructions persist in Settings.
16. The conversation counter updates once per second; History stores a transcription **estimate** (rate
    $0.18 per hour, checked 2026-09-28), not an invoice, with pauses/reconnects and incomplete-usage status;
    credits, taxes and assistant usage are excluded; local speech has no cloud charge; old history without
    usage stays readable and never shows a made-up zero cost.

### 5.3 Speech

17. Cloud speech ("Meta Muse") streams directly from the iPhone to the provider, not through Windows; no
    model download; language auto-detected.
18. The key is pasted once in Transcription settings, saved to the iPhone Keychain, shown masked, can be
    tested without sending microphone audio, and can be removed (which blocks new cloud streams).
19. Keys are excluded from settings export, history and source; used only at the fixed provider destination
    over TLS; redirects are rejected; authentication errors never show raw provider responses.
20. Two on-device options ("Voz on Assist" batch and "Live Parakeet" streaming) work fully on the iPhone,
    English only, and need preparation in the Model Library, which shows download/setup percentage, byte and
    file counters and elapsed time, without promising a remaining time.
21. Two PC-local GPU speech models (Nemotron, Qwen3) send audio to the PC. From Settings the phone can
    start/stop the model service, see loading time and readiness, and retry failed startups. Start launches
    Docker Desktop if needed; Stop unloads only LiveCue's idle model and keeps images and weights. Model
    operations require a saved pairing and refuse to interrupt an active transcription.
22. The transcription provider can only be changed after ending the current conversation.
23. Phone audio uses bounded buffers and 80 ms frames; pause/resume starts a fresh cloud stream; a
    disconnection pauses recording with no automatic paid retry and no provider fallback.
24. Cloud "first partial" timing is measured from stream start and includes speaking time.

### 5.4 Answers

25. Assist sends text (never audio) to the selected answer service: Codex CLI on the Windows PC, or
    OpenRouter directly from the iPhone.
26. OpenRouter: key saved in the iPhone Keychain through a masked field, optional verification without an
    inference request, searchable model catalogue with per-million-token prices (catalogue prices, not a
    quote), provider-default reasoning, streamed plain-text answers for every model.
27. PC and OpenRouter selections are saved separately; removing the key stops access with no silent switch
    to the PC route.
28. Stopping a session does not send an OpenRouter summary request.
29. Run details record provider-reported cost in credits and token usage when available.
30. PC route: the phone refreshes an authenticated model catalogue from the PC, chooses a model and a
    supported reasoning level; the choice also applies to session summaries; the service tier stays default;
    the catalogue comes from the same Codex executable that answers, cached for five minutes with last-good
    recovery.
31. Each saved answer records model, reasoning level, speech mode, transcript length and timings. "Retry same
    text" reuses the exact text, instruction and context with a new request id and the currently selected
    assistant settings, without re-transcribing.
32. Timing breakdown: tap-to-answer, pending speech after the tap, context preparation, full round trip, PC
    Codex invocation time and remaining relay overhead, with the stated caveats.
33. A fully PC-free setup exists (cloud speech + OpenRouter). In that setup the app does not contact the
    saved PC, and Home shows key readiness and that Windows is not required.

### 5.5 Pairing and connection

34. Pair once. The iPhone stores address and credential together (Keychain plus a protected, backup-excluded
    file); older Keychain pairings migrate automatically; LiveContainer updates must reuse the existing data
    container.
35. Windows stores a redisplayable QR credential encrypted for the current Windows user. Restarting the
    desktop or showing the QR again keeps existing phones authorised. Only an explicit reset revokes.
36. Upgrading from hash-only PC credentials keeps the old phone's token valid.
37. The connection screen restores a saved PC, offers Reconnect, distinguishes rejection from temporary
    network/TLS failure, retries automatically after the app is reopened, and explains that a TLS failure
    cannot be repaired by rescanning the QR.
38. Pairing flow on the iPhone: Pair Windows PC -> Scan PC QR code -> Verify and pair. If the camera is
    denied, a manual JSON payload from the terminal launcher can be pasted.

### 5.6 Windows companion

39. Requirements: Windows PowerShell, Node.js 24+, Codex CLI signed in to ChatGPT, Tailscale on PC and phone.
40. First-time setup: install dependencies once in the relay folder, double-click the desktop launcher, start
    the relay; the desktop checks Codex and Tailscale and shows the PC endpoint.
41. The companion can start and stop the relay, pause new requests, show recent authenticated phone activity,
    and show the exact incoming text context and the returned answer.
42. "Last seen" is driven by a phone heartbeat every five seconds while the app is active and does not prove
    the phone is offline when iOS backgrounds the app.
43. Closing the window stops its relay and cancels pending requests.
44. Activity is bounded in memory, cleared on close, and never written to conversation logs.
45. The QR hides after two minutes and is never sent to a remote QR service.
46. Desktop control uses a private pipe to the relay, not HTTP endpoints.
47. The terminal launcher remains available.
48. The relay binds only to loopback; Tailscale exposes it as private HTTPS inside the tailnet.
49. After updating the PC source, restart the desktop so the running relay picks up changes; no pairing reset
    is needed.
50. The README also states that the desktop pairing button generates a **new** QR that invalidates the old
    pairing and that it can rotate the token. The code does not do this (section 8, item 1).

### 5.7 Optional internet (Funnel) access

51. An existing authenticated gateway on the PC can forward only LiveCue's declared HTTP routes and its
    speech WebSocket to the loopback relay.
52. The relay validates the pairing token before every HTTP operation and speech connection; the gateway
    never reads pairing files; LiveCue rejects browser origins, ignores cookies and does not accept the
    gateway's browser session.
53. Enabled by a local `connection.json` (mode `funnel`, root HTTPS endpoint with port). The launcher checks
    the host is this PC, advertises that address in the QR, and leaves Tailscale mappings untouched. Without
    the file, private mode stays.
54. The phone must scan a new QR to change its saved endpoint; with Funnel the phone needs only ordinary
    internet while the PC keeps Tailscale and LiveCue running.
55. Generating a QR verifies the advertised health endpoint without logging the bearer.
56. Funnel access is internet-reachable and protected by the random pairing token, not tailnet membership.

### 5.8 Privacy

57. Audio is never saved by LiveCue. Transcripts, answers and notes stay on the iPhone until deleted.
58. The desktop shows PC-routed conversation text in bounded memory only, never in disk logs.
59. Provider keys entered on the phone stay in the iPhone Keychain, each sent only to its own provider. No
    credentials are needed for GitHub builds.
60. For older phones only, Windows can still decrypt its own stored speech credential into the relay's
    environment; assistant subprocesses never inherit it; the current phone app never uses that relay route.
61. Users must obtain consent before recording; provider processing follows the providers' terms.

### 5.9 Development, releases and boundaries

62. `project.yml` is the single source for the Xcode project.
63. GitHub's macOS runners run simulator unit and UI tests, capture evidence, then separately build an
    unsigned ARM64 device app.
64. The release packages `Payload/LiveCue.app` as `LiveCue.ipa`, publishes it to the public GitHub release,
    downloads it again and verifies checksum and structure.
65. Builds never contact the speech provider or use provider credentials; simulator tests use synthetic
    credentials and fixtures and make no paid requests.
66. Never commit pairing payloads, local logs, credentials or personal conversation data. Publishing the
    source does not expose or grant access to a running relay.
67. Relay assistant defaults: a fixed default model with low reasoning, default speed tier, structured
    output, an empty temporary working folder, read-only sandbox, shell disabled.
68. Not included: diarisation, text-to-speech, invisible overlay, lock-screen control, arbitrary model URLs.
    Microphone hardware behaviour, real provider access and LiveContainer Keychain behaviour need a physical
    iPhone check.

---

## 6. Acceptance criteria

The remake uses a different relay port and data folder from the old install. "The relay port" below means the
remake's configured port.

### 6.1 Desktop window

- **AC-WIN-001:** On launch the window shows relay status "Stopped", phone status "iPhone not connected" with
  a grey indicator, an enabled "Start relay", no Pause control, a disabled "Stop relay" and a disabled "Show
  QR".
- **AC-WIN-002:** The window cannot be resized below 960 x 620 and offers working minimise, maximise/restore
  and close controls with accessible names.
- **AC-WIN-003:** Starting the relay first verifies, in this order: Tailscale installed, Node.js present,
  relay dependencies installed, Codex CLI found, Codex signed in, Tailscale online with a DNS name. The first
  failing check shows its specific message as the relay status and leaves Start enabled.
- **AC-WIN-004:** While checks run the status reads "Checking Codex and Tailscale..." and Start is disabled.
- **AC-WIN-005:** When the relay reports ready: status is "Ready", Start is replaced by an enabled "Pause
  relay", "Stop relay" and "Show QR" are enabled, and the PC address field shows the advertised HTTPS
  endpoint.
- **AC-WIN-006:** On ready the activity log contains "Relay started. Waiting for your iPhone." and exactly
  one of the two speech-key lines, matching whether a speech key was loaded.
- **AC-WIN-007:** In Funnel mode the activity log additionally states that the iPhone needs internet and not
  the VPN, and the window makes no change to Tailscale configuration.
- **AC-WIN-008:** In private mode the relay port is published over tailnet-only HTTPS before the relay
  starts; if that fails the status is "Tailscale HTTPS setup failed. Check that Tailscale is connected." and
  no relay process is left running.
- **AC-WIN-009:** If the relay port is already in use the status shows the port-in-use message, the relay
  process ends, Start is enabled again, and the message is not overwritten by the generic "Relay stopped"
  text.
- **AC-WIN-010:** Pressing "Pause relay" results in status "Paused" and the label "Resume relay" only after
  the relay confirms; assist and summary requests from the phone are then refused with a "paused" error
  while authenticated health checks still succeed.
- **AC-WIN-011:** Pressing "Resume relay" returns status to "Ready" and the label to "Pause relay", and
  requests are accepted again.
- **AC-WIN-012:** "Stop relay" ends the relay process within 3 seconds (graceful first, forced after 2.5 s),
  frees the port, and returns the window to the AC-WIN-001 control state.
- **AC-WIN-013:** Closing the window stops the relay; no relay process and no listener on the relay port
  remain afterwards.
- **AC-WIN-014:** If the relay process is terminated externally, within one second the window shows "Relay
  stopped - click Start relay to reconnect", re-enables Start, disables Stop/Pause/Show QR, clears any QR
  and resets the phone status. It does not restart the relay by itself.
- **AC-WIN-015:** If the window process is terminated, the relay process exits by itself within 2 seconds.
- **AC-WIN-016:** Every authenticated HTTP request from the phone turns the phone status to "iPhone
  connected" (green). An unauthenticated request does not.
- **AC-WIN-017:** 16 seconds after the last authenticated request the phone status reads "iPhone idle"
  (amber) and its tooltip reports the age in seconds; a new authenticated request returns it to connected.
- **AC-WIN-018:** The footer connection line and dot always show the same text and colour as the main phone
  status.
- **AC-WIN-019:** "Pair iPhone" and "Scan to pair" both open the pairing overlay; with the relay running a
  QR is displayed without a further click; with the relay stopped the placeholder and a disabled "Show QR"
  are shown.
- **AC-WIN-020:** The displayed QR decodes to a JSON object with exactly the advertised HTTPS endpoint and
  the pairing token, and has a quiet zone of at least four modules on a white background.
- **AC-WIN-021:** Displaying the QR any number of times does not change the stored pairing credential, and a
  phone paired before still authenticates afterwards.
- **AC-WIN-022:** On the very first start with no saved pairing, the pairing overlay opens with a QR
  automatically after ready.
- **AC-WIN-023:** The QR disappears 120 seconds after being displayed, on "Hide QR", and when the relay
  stops; each case leaves a hint stating that existing pairing remains valid (timeout: "QR hidden for
  privacy. Existing pairing remains valid.").
- **AC-WIN-024:** After each QR display the activity log gains exactly one verification notice (verified
  private, verified Funnel, or could-not-verify), and no log line, event or on-screen text ever contains the
  pairing token.
- **AC-WIN-025:** An incoming assist request sets status "Thinking...", shows the request transcript (plus
  "[In progress] ..." and "Instruction: ..." sections when supplied), sets the transcript hint to "Received
  from your iPhone", shows "Thinking..." in the reply area, and logs "INCOMING assist [id]" with the payload.
- **AC-WIN-026:** An incoming summary request behaves like AC-WIN-025 with the hint "Session summary
  requested" and the log prefix "INCOMING summary".
- **AC-WIN-027:** A reply shows the answer (or the summary), then details, "Key points" and "Action items"
  sections when present; the timing hint shows model, reasoning level and Codex seconds when execution data
  exists, otherwise "Sent to iPhone  /  N s"; the status returns to "Ready" or "Paused" according to the
  pause state; the log gains "REPLY [id] N s" with the payload.
- **AC-WIN-028:** A failed or timed-out request shows "Request failed - ready to retry", the error message
  in the reply area and "Not sent - try Assist again" as the hint.
- **AC-WIN-029:** Relay speech status messages appear both in the activity log and as the transcript hint;
  completed speech segments are appended line by line to the transcript area.
- **AC-WIN-030:** The activity log never exceeds 200,000 characters; when trimmed it starts with "[Older
  activity removed from memory]". The transcript area never exceeds 64,000 characters.
- **AC-WIN-031:** "Clear activity" empties the activity log, transcript area and reply area and sets the
  reply hint to "Activity cleared"; the empty activity log shows "No activity yet".
- **AC-WIN-032:** Pressing Esc closes any open overlay; "Close" in each overlay closes only that overlay.
- **AC-WIN-033:** Nothing shown in the window (transcripts, replies, activity) is written to disk by the
  desktop app, and all of it is gone after the window is closed and reopened.
- **AC-WIN-034:** The relay's diagnostic (standard error) output appears in the activity log with the prefix
  "Relay diagnostic: ".
- **AC-WIN-035:** The window has no tray icon, does not register itself to start with Windows, and does not
  keep the relay alive after it is closed.
- **AC-WIN-036:** The desktop exposes no HTTP endpoint for control; pause, pair and shutdown are impossible
  from the network.
- **AC-WIN-037:** Relay request handling is not blocked while the window's overlays are open or the window
  is minimised.
- **AC-WIN-038:** A headless self-test mode exists that drives the window with synthetic events and fails
  (non-zero exit) if any of: reply text, transcript text, timing text, QR rendering, pause state or
  clear-activity behaviour is wrong; it can save a screenshot of the rendered window.

### 6.2 Launch, build and configuration

- **AC-SCR-001:** A single double-click entry point opens the window without a console window and starts the
  relay automatically once the window has rendered.
- **AC-SCR-002:** If the launcher is separated from the files it needs, it shows a clear message box instead
  of failing silently.
- **AC-SCR-003:** A terminal launcher exists that performs the same prerequisite checks, prints the
  advertised endpoint and mode, and runs the relay in the foreground; a check-only switch stops after the
  checks with "prerequisites passed".
- **AC-SCR-004:** A pairing-reset switch on the terminal launcher creates a new credential; after it, a
  previously paired phone is rejected with an authentication error.
- **AC-SCR-005:** With no local connection file the advertised endpoint is `https://<PC name>/` style
  tailnet HTTPS and the mode is private.
- **AC-SCR-006:** A local connection file is accepted only if mode is `funnel`, the URL is HTTPS, the host
  equals this PC's own Tailscale DNS name, the path is the root, there is no query/fragment/user info, and
  the port is an allowed Funnel port; every other content is rejected with the invalid-configuration
  message and the relay does not start.
- **AC-SCR-007:** The speech key is read only from user-scoped encrypted storage outside the repository; if
  it is absent the relay starts and reports cloud speech as not configured; if it cannot be decrypted the
  start fails with the "Cannot unlock" message.
- **AC-SCR-008:** The speech key is present in the relay process environment only; it is absent from the
  desktop process after start, from every assistant subprocess, from all logs and from all events.
- **AC-SCR-009:** The Codex executable is resolved from PATH first, then from the newest copy under the
  Codex desktop app's per-user folder; the resolved path is what the relay uses for answers and for the
  model catalogue.
- **AC-SCR-010:** The relay listens on loopback only; a connection attempt to the relay port from another
  machine on the LAN is refused.
- **AC-SCR-011:** The app icon file contains 16, 24, 32, 48, 64, 128 and 256 px images generated from the
  single source PNG.
- **AC-SCR-012:** Build output (executables, packages, result bundles) and every secret-bearing file type
  listed in 2.9 are ignored by git.
- **AC-SCR-013:** Starting a second instance while one relay is running produces the port-in-use message in
  the second window and does not disturb the first.

### 6.3 CI

- **AC-CI-001:** All workflows run on free GitHub-hosted runners in a public repository and require no
  repository secrets.
- **AC-CI-002:** Simulator tests run on every push to the default branch, on every pull request, and on
  manual dispatch; a newer run on the same ref cancels the older one.
- **AC-CI-003:** The Xcode toolchain version is pinned in one place per workflow and printed in the log.
- **AC-CI-004:** The simulator is chosen by script: only iPhone devices, never a runtime newer than the
  pinned SDK, newest eligible runtime first, then the preferred device order; the job fails with a clear
  message if none exists.
- **AC-CI-005:** Simulator boot is attempted at most three times with a 180 s limit each, shutting the
  device down between attempts; the third failure fails the job.
- **AC-CI-006:** The test run executes both unit and UI tests with parallel testing disabled and produces a
  result bundle and a full build log.
- **AC-CI-007:** Evidence (result bundle, log, final screenshot, exported attachments) is uploaded even when
  tests fail, with 14 days retention.
- **AC-CI-008:** The release workflow can only be started manually and requires a release tag input.
- **AC-CI-009:** The release build job does not start unless the simulator test job succeeded in the same
  run, or the reuse gate returned `reused=true`.
- **AC-CI-010:** The reuse gate rejects: a non-numeric run id; a run from another repository or workflow; an
  unfinished run; a run older than 24 hours; a run whose simulator job did not succeed; a commit that is not
  an ancestor; any changed file outside the four allowed files; any `project.yml` difference other than the
  two bundle-version fields; a duplicated or malformed version field; a changed toolchain pin.
- **AC-CI-011:** The reuse gate's own unit tests run before the gate in every release.
- **AC-CI-012:** The device build is Release, iPhone OS SDK, generic destination, with code signing
  disabled, and succeeds without any certificate or provisioning profile.
- **AC-CI-013:** Packaging fails unless: the app bundle and its executable exist; the bundle identifier
  equals the expected value; the release tag equals `v` + the marketing version; the executable is arm64.
- **AC-CI-014:** The IPA is a zip whose top-level folder is `Payload` containing the `.app`; it passes an
  archive integrity test; a SHA-256 checksum file is produced next to it.
- **AC-CI-015:** The IPA, checksum and device build log are uploaded as a workflow artefact with 14 days
  retention.
- **AC-CI-016:** The workflow creates the GitHub release for the tag at the built commit, or, if it exists,
  replaces both assets and repoints it; the notes state that the IPA is unsigned and for LiveContainer.
- **AC-CI-017:** After publishing, the workflow downloads the release assets again and fails unless the
  checksum matches, the archive is intact and it contains the app executable at the expected path.
- **AC-CI-018:** Only the release job has write permission to repository contents; every other job is
  read-only.
- **AC-CI-019:** A manual workflow can take a run id and publish that run's test screenshots as a 7-day
  artefact.
- **AC-CI-020:** No CI job contacts a speech or model provider or needs a provider key; tests use fixtures.
- **AC-CI-021:** The published IPA installs and launches through LiveContainer on a physical iPhone running
  a supported iOS version (manual check).

### 6.4 Public-source audit

- **AC-AUD-001:** A single command audits every tracked and every untracked-but-not-ignored file and exits
  non-zero listing the offending paths if anything is found.
- **AC-AUD-002:** It detects the real locally stored speech key in UTF-8, in UTF-16 and Base64-encoded.
- **AC-AUD-003:** It detects, without knowing any real key, the generic shapes in section 4 (`LLM_`-prefixed
  key, OpenRouter key, PEM private-key header, GitHub token).
- **AC-AUD-004:** It blocks files whose path matches the forbidden names in section 4 regardless of content.
- **AC-AUD-005:** It reports only paths, never the matched secret text.
- **AC-AUD-006:** If the exact-key check cannot be performed it says so explicitly and does not report a
  pass for that check.
- **AC-AUD-007:** A clean repository prints a pass line that includes the number of files examined.

---

## 7. Old visual design, in brief (being replaced)

Theme name "Midnight Mint". Dark only.

- Background: diagonal gradient from near-black teal `#0B1414` to `#0E2525`. Title bar: darker teal gradient
  ending in `#0E3D3A`, with a one-pixel divider.
- Cards: `#142326`, one-pixel border `#294446`, 14 px radius, 22 px padding, a faint mint glow.
- Bubbles inside cards: gradient `#112C2C` to `#0E2424`, 18 px radius.
- Text: `#F1FAF8`; secondary `#A5BDBB`; accent mint `#2EE6B6`; hover/secondary accent aqua `#6EE7F4`.
- Primary buttons: vertical mint gradient `#2EE6B6` to `#20BDA0` with dark text. Secondary buttons: `#142B2C`
  with border `#294446`. 10 px radius. Hover = aqua border and 85 % opacity, pressed = 65 %, keyboard focus =
  2 px mint border, disabled = 38 %.
- Status colours: grey `#A1B1C8`, green `#35B665`, amber `#D9A23E`.
- Typeface Segoe UI, 16 px base, 22 px card headings, 25 px overlay titles, 14 px captions; monospaced
  Consolas 14 px for the activity log. Icons from the Segoe MDL2 icon font.
- Overlays: 73 % black scrim with a centred card. QR on pure white with 12 px rounded container.
- App icon: three luminous mint/aqua rounded equaliser bars, the middle one extending into a speech tail, on a
  deep teal square.

---

## 8. Ambiguities, bugs and dead code

### Contradictions between README and code

1. **Pairing rotation.** The README says the desktop pairing button generates a new QR that invalidates the
   old pairing and can rotate the token. Since 0.5.3 the button only redisplays the saved credential. The only
   rotation is the terminal reset switch. The window has no revoke control at all.
2. **"Click Start relay".** The README tells the user to click Start after opening the desktop, but the
   normal launcher auto-starts the relay.
3. **"Gated release".** The release can be dispatched from any ref; the only gates are the test job or the
   reuse gate.

### Dead or stale items

4. The layout file's default texts "Generate QR" and "Already paired? You're all set. Start the relay to
   generate a QR for a new pairing." are overwritten at startup and never shown. The placeholder still says
   "Generate a QR to get started" although the button says "Show QR".
5. The launcher is still called `LiveCue Desktop Fluent.exe`, the mint gradient resource is named "Blue",
   and `Assets/PROMPT.md` describes a superseded blue icon. `.gitignore` still lists an older exe name.
6. `-TestExpanded` is a leftover name; it only keeps the pairing overlay open for a screenshot.
7. The `model` field of the `ready` event is unused by the window.
8. The "diagnostic PowerShell launcher" named in a message box does not exist as a separate file; it means
   running the main script directly.
9. The transcript "indicator" is a static decoration.

### Bugs and fragile behaviour

10. **Stalled event loop.** If handling one relay output line throws (malformed JSON or an unexpected
    payload), the window shows "Desktop connection interrupted..." and never advances past that line: the
    same line is retried on every tick and all later events are lost until the relay is stopped and started.
11. **Frozen window during start.** Prerequisite checks (including the Codex sign-in check and Tailscale
    calls) run on the UI thread, so the window is unresponsive and the "Checking..." text may never paint.
12. **Status preservation by keyword.** The rule that keeps fatal messages matches the words `port`,
    `failed`, `Could not`. "Request failed - ready to retry" also matches, so a relay crash right after a
    failed request leaves that text instead of "Relay stopped...". A fatal message not containing those words
    would be overwritten.
13. **Diagnostics race.** Standard error is drained one line per tick. If the process exits while lines are
    still buffered, the next line triggers an error ("Desktop connection interrupted...") and remaining
    diagnostics are lost.
14. **Stale UI after stop/exit.** The paused flag, endpoint field, transcript, reply, transcript hint and the
    timing tooltip are not reset when the relay stops or exits. "Clear activity" does not reset hints or
    status and leaves the text areas blank instead of restoring their placeholders.
15. **Phone status never times out to "not connected"** while the relay runs; a phone that left hours ago
    still reads "iPhone idle".
16. **Tailscale mapping side effects.** In private mode every start re-applies a root HTTPS mapping for the
    relay port and nothing ever removes it. On a PC that already uses the root path for something else this
    silently replaces that mapping.
17. **Fixed port in several places.** The port literal appears in the terminal launcher (twice), in the
    desktop start code and as the relay default.
18. **Node version not checked**, although the message claims 24 or newer is required.
19. **Icon churn.** Building the exe rewrites a tracked icon file.
20. **Every QR display** (including merely reopening the overlay) re-reads and decrypts the stored
    credential and performs a network verification call, each adding a log line.
21. **Activity log exposes full payloads.** Complete transcripts and answers are visible to anyone at the PC
    while the window is open (memory only, by design, but undocumented in the UI beyond "Activity stays in
    memory on this PC").
22. **No single-instance guard.** A second window simply fails with the port message.
23. **Launcher error detection.** Any non-terminating script error makes the launcher show the "could not
    finish startup" box after the window closes, even if the session worked.

### Missing pieces and unclear intent

24. No script creates the encrypted speech-key file or the connection file; the README documents only the
    latter's format.
25. The relay's PC speech-model control expects helper scripts in a **sibling folder of the repository**
    (`LiveCue-ASR`), while the repository ships the same script names under `Relay/asr`. Which location is
    authoritative on an installed PC is not stated anywhere.
26. "Device management" was requested for this spec; the old app has none (1.9). Whether the remake should
    add per-device credentials is a product decision, not existing behaviour.
27. The speech-through-relay route and its "Meta Muse is configured" line exist only for older phone
    versions; the current phone app streams directly to the provider. The desktop still reports it.

### CI oddities

28. The reuse gate masks the two Info.plist version keys, but real version bumps now change the
    `MARKETING_VERSION` / `CURRENT_PROJECT_VERSION` build settings, which are not masked. A normal version
    bump is therefore always rejected by the gate; it only ever accepted the one-off repair it was written
    for. It also allows the gate script and the release workflow themselves to change between the tested and
    released commits.
29. Tests run on `macos-26` but the device build and the screenshot export run on `macos-15` with the same
    pinned Xcode path; this depends on that Xcode being present on both images.
30. The simulator-selection comment says it avoids "older first-boot runtimes", but the code only excludes
    newer ones; older runtimes are merely sorted last.
31. The screenshot-export workflow only understands the release workflow's artefact name, not the artefact
    of the ordinary simulator workflow.
32. Per-test time limits exist only in the release workflow's test step, not in the push/PR workflow.
33. Relay tests, the desktop self-test and the public-source audit are outside CI entirely, so nothing
    automated protects the Windows side or the publication guard.
