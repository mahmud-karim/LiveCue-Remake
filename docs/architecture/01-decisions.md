# 01. Decisions and deviation register

Status: binding. Builders cite entries by id: `D-nn` (decided by the orchestrator for the owner), `A-nn`
(decided by the architect under the defect policy), and register rows such as `IOS-B2` or `RELAY-B14`.

Companion files: `02-relay-protocol.md` ("protocol") and `03-system-architecture.md` ("architecture").

Contents

1. [Conflicts and limits found](#1-conflicts-and-limits-found)
2. [Decisions made by the orchestrator](#2-decisions-made-by-the-orchestrator)
3. [Decisions made by the architect](#3-decisions-made-by-the-architect)
4. [Deviation register](#4-deviation-register)
5. [Acceptance criteria changed or retired](#5-acceptance-criteria-changed-or-retired)
6. [User-visible text changes](#6-user-visible-text-changes)
7. [Open risks and manual checks](#7-open-risks-and-manual-checks)
8. [Review rulings](#8-review-rulings)

---

## 1. Conflicts and limits found

Read this section first. Each item is a place where two decisions pull against each other, or where a
decision cannot be met in full. In every case the safer reading was taken.

| # | Conflict or limit | Resolution |
|---|---|---|
| 1 | **D-24 says the speech socket is accepted whenever authentication passes and every other refusal is a typed frame. D-18 says the relay enforces concurrency caps before doing work.** An internet edge needs a hard ceiling on upgraded sockets. | One ceiling stays outside the typed-frame rule: more than four open speech sockets are refused with HTTP `429` before the upgrade (protocol C-32, E-7). It can only be reached by a holder of the token opening five sockets at once. Every refusal a normal phone can meet (paused, busy, model changing, bad model, version mismatch, lab not set up) is a typed frame. |
| 2 | **D-24 says every error body carries a `code`.** Requests rejected by the HTTP parser itself (malformed request line, oversized headers) never reach relay code. | Those get a bare `400`, `414` or `431` with no body (protocol 5.1). The phone classifies a code-less answer by its status (protocol 13.6). Every error written by relay code has a code. |
| 3 | **D-18 requires a 43-character token shape before any work, and D-24 requires aligned limits. D-02 says to keep behaviour an old test asserted.** Old tests and fixtures used short tokens, and the old phone accepted tokens of 16 to 512 characters. | Both sides require the exact 43-character shape (protocol C-02). Old tests are carried over with fixture tokens of that shape. A phone that accepted tokens the relay always rejects would only fail later and less clearly. |
| 4 | **D-20 says connection modes change only by explicit user action. The old app published its tailnet mapping automatically at every start (AC-WIN-008), and D-01 asks for parity.** | D-20 wins. The relay starts on loopback; the first run needs one "Set up connection" click, which shows the exact command. After that the mapping persists, as it did before. |
| 5 | **D-20's command forms are not safe on every port.** Read from Tailscale's source on the research date: `tailscale serve` on a port that has Funnel on switches Funnel off for the whole port, and `tailscale funnel` on a port that has tailnet-only handlers publishes them. | The mapping manager refuses in both situations instead of running the command (architecture TS-4, TS-5). The default public port 10000 is therefore usable only when Funnel is already on for it or nothing else uses it. |
| 6 | **D-20 forbids "off" for a whole port, yet a stale mapping must be removable**, or a PC switched from Public to Private would stay reachable from the internet. | Removal uses only the path-scoped form, only for a mapping recorded in the app's own ledger, and only on an explicit click (TS-8, TS-9). Independently, a relay in Private mode refuses requests that Tailscale marks as arriving through Funnel (protocol E-1). |
| 7 | **D-22 says Codex is found through `PATH`.** On Windows a `PATH` entry for Codex is often a `.cmd` shim, and starting a shim means a command interpreter parses the arguments. | Only a real `codex.exe` is accepted, from the explicit setting, from `PATH`, or from the per-user Codex folder (architecture 3.7). An install that offers only a shim needs the explicit path setting. |
| 8 | **D-23 asks for the lab's "same hardening" and "same protocol", and D-03 and D-21 ask for side-by-side operation with the old install.** The old lab's container name, image names and port 8765 are fixed values that the old relay and scripts also use. | The protocol messages are unchanged. The container name, label, image names and the default host port (47842) are new, and the lab's `Host` check follows the configured port (architecture 5.1, protocol 11.1). |
| 9 | **D-16 says to fix the catalogue's pagination failure, and D-01 says the Codex catalogue exchange is preserved exactly.** The name of the cursor parameter could not be confirmed from a primary source. | The relay no longer treats a cursor as failure; it uses the first page (100 entries) and sends no unverified request (protocol 12.1). |
| 10 | **The design system document describes things the brief excludes**: a tray icon, a device row and a "devices" page, a QR tile with "Expired" and "New code" states, and sample button labels that differ from spec strings. | The brief wins: none of those is built. The QR is the same token every time and its states are empty, shown and hidden (architecture 3.12). Spec strings win over the design document's sample labels. The window size is the design system's and the Windows screen design's (default 1240 x 660, minimum 960 x 620), which is what old AC-WIN-002 asserted. Every other disagreement between the Windows screen design and this register is ruled on in `05-windows-architecture.md` section 1. |
| 11 | **The platform research recommends keeping the tunnel adapter as a Node module.** D-18 removes it. | D-18 wins. There is no Node.js anywhere. |

---

## 2. Decisions made by the orchestrator

Recorded, not reopened. Numbering against the architect's brief: its items 1 to 5 are D-01 to D-05; its
keep, fix and drop calls are D-06 to D-16; its items 6 to 12 are D-17 to D-23; item 13 is D-24; items 14 and
15 are D-25 and D-26; the repository layout is D-27.

### Product

| Id | Decision |
|---|---|
| D-01 | **Feature parity.** Every user-visible function and flow in the spec exists in the remake. Third-party contracts are preserved exactly: OpenRouter, Meta realtime, the Codex CLI invocation and catalogue exchange, the on-device speech packages, and the ASR lab's native relay protocol. |
| D-02 | **Defect policy.** Fix a listed defect when the fix is invisible to the user or strictly better for them. Keep any behaviour an old test asserted unless this document says otherwise. Drop dead code. Every deviation is in section 4 with its spec id. Parity before polish: no device list, no unpair, no Assist cancel button, no dark theme, no tray icon, no data migration from the old app. |
| D-03 | **Identity.** Bundle identifier `com.mahmudkarim.LiveCueRemake`, Home Screen name "LiveCue Remake", in-app brand "LiveCue", version 1.0.0 build 1, its own Keychain service string, no shared storage with the old app. It installs beside the old app and never reads its data. |
| D-04 | **One theme, "Daylight"**, light appearance forced on both platforms. Native Liquid Glass on iOS 26 behind one availability-gated wrapper file; a material-based fallback on iOS 18 to 25. Deployment target iOS 18.0. |
| D-05 | **Speech packages stay at the old exact pins**: FluidAudio 0.15.6 and desert-ant-core 3.1.0 (Voz). Voz's source-available licence means attribution in the app's About area and in the README, and a plain statement that the package reports anonymous usage to its vendor. |

### Keep, fix and drop calls

| Id | Decision |
|---|---|
| D-06 | iPhone B1: keep. On-device models are loaded explicitly each launch. The dead stored key is dropped. |
| D-07 | iPhone B2: fix. Nothing is persisted until the transcriber is connected; a failed start leaves nothing; a session cut off by an app kill is closed at its last save on the next launch. |
| D-08 | iPhone B4, B5, B7, B8, B9, B10, B11, B13, B14, B17, B18: fix. |
| D-09 | iPhone B3, B6, B15: keep. B12: keep, except that no summary is requested when the session has no transcript. |
| D-10 | iPhone B16 and core A2: fix by budgeting in bytes so that no request can exceed the relay's body limit. The session-summary transcript gets a budget too. |
| D-11 | Core A1: the app owns the memory marker for Codex answers as well. It records the last segment id when the request is built and does not trust a model-generated id. |
| D-12 | Core A4: the instruction is sent once, as the separate preference message. A3 and A6: keep. A5: malformed catalogue entries are skipped instead of failing the whole catalogue. A7, A11, A12 (sub-second dates), A18: fix. |
| D-13 | Dead or unfinished code: the transcriber status lines the old app computed are shown; the Speech benchmark worksheet stays and "Use latest transcript" uses the most recent session; everything else listed as dead is removed. |
| D-14 | **The relay's cloud speech bridge is removed.** The phone talks to the cloud speech provider directly; the PC needs no cloud speech key. `cloudSpeechReady`, the key loader and the `meta` value of the speech header go with it. The PC speech models `nemotron` and `qwen3` stay. |
| D-15 | "Show QR" redisplays the same token. "Reset pairing" is a clearly destructive desktop action with confirmation and replaces the terminal-only flag. Still one shared token and no device list. |
| D-16 | Relay quirks B1 to B22: fix them cleanly; none is worth preserving. |

### Windows

| Id | Decision |
|---|---|
| D-17 | One self-contained .NET 10 application (`net10.0-windows`, WPF, no trimming). The relay is a separate class library on Kestrel hosted in-process, plus a thin console host for headless runs and tests. No Node.js, no PowerShell helper for DPAPI (`ProtectedData` instead), no stdio control channel: the desktop talks to the relay through an in-process interface that carries the same information the old events carried. |
| D-18 | **The relay is its own internet edge.** No gateway adapter. Built into the relay and applied to every request however it arrived: bearer shape pre-check before any work, rejection of any request carrying `Origin` or `Sec-Fetch-Site`, an exact route allowlist, body caps enforced while streaming, concurrency caps, per-request deadlines, stream idle and lifetime limits, explicit HTTP timeouts, fixed error strings with no internal detail. It binds loopback only. |
| D-19 | The relay works behind a path-prefix mount: it serves its routes at root, the pairing payload's `endpoint` may contain a path, and the phone builds every URL, including the WebSocket URL, relative to that endpoint. |
| D-20 | Connection modes are managed by the desktop with explicit user action, never silently. **Private** adds a dedicated tailnet HTTPS listener; **Public** adds a Funnel path mount. The desktop inspects `tailscale serve status --json` first, only ever adds a handler for its own port and path, never replaces or removes a handler it did not create, never runs a reset or an "off" for a whole port, and never uses the bare `tailscale serve --bg <port>` form. Ports, prefix and mode are user settings with safe defaults: relay loopback port 47841, private HTTPS port 11443, public port 10000 with prefix `/livecue`. The old relay's port 47831 is never used or offered. |
| D-21 | Windows data lives under `%LOCALAPPDATA%\LiveCueRemake\`. The old `%LOCALAPPDATA%\LiveCue\` is never read or written. |
| D-22 | Codex CLI discovery: an explicit setting, else `PATH`, else the newest `codex.exe` under the per-user Codex install folder. The relay never needs an API key. |
| D-23 | The ASR lab is remade under `asr/` with the same native relay protocol (version 1), the same hardening, and launcher scripts. The lab folder is a setting. Its protocol tests run with a fake engine and need no GPU. Downloading weights and building images are documented setup steps and never run automatically. |

### Protocol, iPhone, layout

| Id | Decision |
|---|---|
| D-24 | **Relay protocol 2.** It starts from the old protocol's semantics and applies the fixes: every error body carries a stable machine-readable `code` and the phone maps codes to its own text; health reports a protocol version and the relay version; cancellation returns a truthful result and a cancelled request is distinguishable from a failure; summaries accept a client `requestId` and can be cancelled; the speech socket is accepted whenever authentication passes and reports every other refusal as a typed `error` frame followed by a close; limits on both sides are aligned and stated once; request ids have one shape everywhere. |
| D-25 | The platform-independent core is a real Swift package in a subfolder, Foundation only, tested with `swift test` on Linux and macOS. Transport sits behind a protocol, with the Apple implementation outside the Linux-compiled path. The app target is generated with XcodeGen. The research document's recommendations on Xcode pinning, language mode, signing, the availability wrapper and LiveContainer are followed. |
| D-26 | A UI-test seam equivalent to the old test mode, with the same fixtures and modifiers, and every accessibility identifier of spec 01 section 1.13. |
| D-27 | Repository layout: `ios/`, `windows/`, `asr/`, `scripts/`, `docs/`, `.github/workflows/`. |

---

## 3. Decisions made by the architect

Each follows from D-01 and D-02 unless a reason is given.

| Id | Decision | Reason |
|---|---|---|
| A-01 | Error bodies stay flat, `{"error": text, "code": CODE}`, and codes are upper-case with underscores. | The two codes the old relay already had, and its tests, keep their exact form. |
| A-02 | A request with a browser header, a malformed bearer or an unknown token gets the same, byte-identical 401. | A caller without the token learns nothing; it also matches what the old tests asserted through the proxy. |
| A-03 | Every phone request carries `X-LiveCue-Protocol: 2`; the relay refuses any other value with `PROTOCOL_MISMATCH`; health also reports the version. | Version skew between phone and PC becomes one clear message instead of odd failures. |
| A-04 | `assistant` is required in assist and summary bodies. The relay has no default model. | Cleanest fix of relay B9; the phone always sends it. |
| A-05 | Answer outcomes: `409 REQUEST_CANCELLED`, `504 ASSISTANT_TIMEOUT`, `500 ASSISTANT_FAILED`. One 60 s deadline covers body, catalogue and Codex. The phone waits 70 s. | Fixes relay B2 and B15 with one rule. |
| A-06 | Assist and summary keep sharing one slot. | Old behaviour; a second slot would be a behaviour change nobody asked for. |
| A-07 | A model load started for a speech stream continues as an ordinary operation when the phone leaves; the lease is released at once. | Fixes relay B14 without losing the `loading` frames that D-13 wants shown. |
| A-08 | A failed PC model operation is latched as `error` for 120 s, then the state follows the probe again. | Fixes relay B17 while the phone still gets to show the error. |
| A-09 | The pairing self-check carries a one-time nonce and is not counted as a phone. Authenticated speech upgrades are counted. | Fixes relay B5 and A8. |
| A-10 | In Private mode the relay refuses requests that Tailscale marks as arriving through Funnel. | See section 1, item 6. |
| A-11 | The endpoint has a strict grammar that both sides parse directly: lower-case `https`, DNS host, optional port, at most four path segments; canonical form always ends with `/`. | Two URL libraries would otherwise disagree at the edges. |
| A-12 | Byte budgets: transcript 80,000; summary transcript 100,000; partial 16,000; memory summary 24,000; instruction 4,000 characters and 8,000 bytes; lists 16 items of 256 bytes; encoded body at most 126,976 bytes, with a fitting step for heavily escaped text. | D-10. The old test of the 80,000 cap still passes unchanged for ASCII text. |
| A-13 | The phone calls the cancel route only when its own timeout passes, when the request's task is cancelled, and when a session is deleted while its summary is in flight. | D-02 forbids a cancel button; D-24 asks that cancel exists and is truthful. |
| A-14 | The relay validates Codex output against the schema itself and rebuilds the reply from an allowlist. The two schema files are unchanged. The model-written marker is validated for its type and then dropped: an assist reply always carries `memory.throughSegmentId` as `null` (section 8, X-01). | Fixes relay B10; the phone owns the marker and ignores the reply's (D-11). |
| A-15 | Windows has four shipped projects (`LiveCue.Relay`, `LiveCue.Hosting`, `LiveCue.Relay.Host`, `LiveCue.Desktop`) and a developer icon tool. Both executables are published self-contained into one folder; no single-file bundle. | A testable host layer without WPF; one copy of the runtime; no self-extraction to the temp folder. |
| A-16 | The pairing payload text is available only from the console host's `--show-pairing`. The desktop never holds it. | Fixes relay B7 and keeps a source for the phone's paste path. |
| A-17 | Start-up prerequisites stay blocking, in the order Codex found, Codex signed in, Tailscale installed, Tailscale online with a DNS name. Every check that can run is evaluated so that the window can list all results; the status line shows the first failure in that order. | Old behaviour (AC-WIN-003) without the Node.js checks; the list is the screen design's. |
| A-18 | The session has explicit states; a failure of any provider leads to Paused; on-device providers finalise first so recognised text is kept. | D-08 (B4, B5) in one rule. |
| A-19 | The relay client on the phone uses a private ephemeral session and never follows redirects, like the other two network paths. | Invisible; the old app used the shared session for relay HTTP only. |
| A-20 | CI: no test-reuse gate; a release runs the whole CI in the same run and only from `main`; the audit is a Python script that runs on every push and over history. | The old gate only ever accepted one repair; the old audit could not run in CI. |
| A-21 | Phone timeouts are restated per route (protocol 6.1): health and status 8 s, verify 15 s, models 45 s, start and stop 15 s, assist and summary 70 s, cancel 8 s. | D-24: limits aligned with the relay's deadlines. |

---

## 4. Deviation register

One row for every bug, ambiguity and dead-code item of the four specs. "Call" is keep, fix or drop; "moot"
means the thing it was about no longer exists. "By" is B (decided in the brief, section 2) or A (architect's
call). "Where" names the component of the architecture document that implements the result.

### 4.1 iPhone app (spec 01 section 11)

| Id | Finding | Call | Resulting behaviour | Where | By |
|---|---|---|---|---|---|
| IOS-B1 | On-device model selection saved but never restored | Keep; drop key | Models are loaded explicitly after each launch and each provider change. The `selectedModel` preference does not exist. | `OnDeviceModelManager`, `PreferencesStore` | B |
| IOS-B2 | Ghost sessions | Fix | Nothing is saved until the transcriber is connected. A failed start leaves nothing. At launch, a session without an end time is closed at its last save. | `SessionController`, `SessionStore` | B |
| IOS-B3 | Elapsed time includes connection and paused time | Keep | Unchanged; the same clock is the offset of resumed streams. | `SessionController` | B |
| IOS-B4 | On-device errors do not pause | Fix | A failure of any provider moves the session to Paused and shows the reason in the session notice, not an alert (`04-ios-architecture.md` 1.2). An on-device hand-off queue overflow counts as such a failure. At Voz's 30-minute cap the pending audio is transcribed as part of that pause. | `SessionController`, `VozTranscriber`, `ParakeetTranscriber` | B |
| IOS-B5 | On-device capture ignores interruptions and route changes | Fix | Observed for every provider, with the same two messages; the session pauses. | `AudioCapture` | B |
| IOS-B6 | Audio session handling differs on Pause | Keep | Released for Meta and PC speech; kept active for on-device providers. | `AudioCapture` | B |
| IOS-B7 | Stop after Pause finalises twice | Fix | Stop from Paused does not finalise again. | `SessionController` | B |
| IOS-B8 | Uncategorised transport errors | Fix | Every relay failure shows a phone-owned text chosen by code or by transport class. No raw system text. | Core `RelayClient`, `RelayMessages` | B |
| IOS-B9 | Misleading OpenRouter error | Fix | A failed catalogue fetch during Assist shows that failure, not "choose a model". | `SessionController` | B |
| IOS-B10 | Misleading prefix on pair failure | Fix | "Your pairing is saved." prefixes only health failures of the saved pairing. | `RelayMessages`, `PairingController` | B |
| IOS-B11 | "Rejected" state is sticky | Fix | The saved-PC state follows the most recent check only. | `RelayMonitor` | B |
| IOS-B12 | Summary requested for empty sessions; failure silent | Keep, one change | No summary request when the session has no segment. Failure stays silent and final. | `SessionController` | B |
| IOS-B13 | Model Library progress block is global; "Reload" does nothing | Fix | The progress block belongs to the model it was started for. "Reload" releases the model and prepares it again from the cache. | `OnDeviceModelManager`, Model Library | B |
| IOS-B14 | "follows Dynamic Type" only partly true | Fix | Every text style scales with Dynamic Type. | `DesignSystem` | B |
| IOS-B15 | Uncommitted partial lost on disconnect | Keep | Unchanged. | transcribers | B |
| IOS-B16 | Parakeet partial is unbounded | Fix | The partial is cut to its last 16,000 bytes and the body is fitted to the limit. | Core `WireBudget` | B |
| IOS-B17 | "PC connected" in the manual section reflects the saved PC | Fix | The line is not built. The Saved PC panel alone shows the state of the saved PC (`02-ios-screens.md` section 10). | Pairing | B |
| IOS-B18 | Turn timestamps stored to whole seconds | Fix | Dates are stored with milliseconds. | Core `LiveCueJSON` | B |
| IOS-A1 | Parakeet partial semantics | Keep | The partial is the last callback text; one segment is committed on Pause or Stop; no end-of-utterance commit. Device check in section 7. | `ParakeetTranscriber` | A |
| IOS-A2 | On-device model identity | Keep | The exact package pins decide the models. | `project.yml` | B |
| IOS-A3 | Start is not gated on readiness | Keep | The readiness panel is informational; missing prerequisites surface after the tap. | Home, `SessionController` | A |
| IOS-A4 | No unpair, model delete, delete confirmation, Assist cancel, idle-timer hold | Keep absent, one change | Deleting a session asks for confirmation (`02-ios-screens.md` section 13). Unpair, model delete, Assist cancel and an idle-timer hold are not added. The relay's cancel route is used internally only. | History | B |
| IOS-A5 | Two routes to Settings | Fix | One route: the Settings tab. The Home row opens the new Transcription screen; the PC speech model has its own screen; the answer service is picked in place (`02-ios-screens.md` section 1). | `AppShell`, Features | B |
| IOS-A6 | Appearance row and its test | Fix | The row reads "Daylight". | Settings | B |
| IOS-A7 | Local-network string names one VPN product | Fix | "LiveCue connects privately to your Windows PC." | `project.yml` | A |
| IOS-A8 | Data continuity | Drop | No old data is read. No migration code, including the legacy token-only pairing migration. | `Persistence` | B |
| IOS-A9 | PC status decoding is strict | Keep, with a contract | The relay always sends all five members; the phone decodes strictly. | relay `LocalModel`, core `PCModelStatus` | A |
| IOS-A10 | Asset notes disagree | Drop | Icons come from the design system only. | `LiveCue.IconTool` | A |
| IOS-A11 | What counts as a clean Pause or Stop | Keep, now defined | Completed means `endStream` was sent and the socket closed with code 1000 within 15 s. The same rule serves Meta. | `RelayTranscriber`, `MetaTranscriber` | A |
| IOS-D1 | Unused navigation-route enumeration | Drop | Not built. | none | B |
| IOS-D2 | "Is recording" flag never read | Drop | Not built. | none | B |
| IOS-D3 | Benchmarks list and result type | Drop | Not built. The worksheet stays (IOS-D6). | none | B |
| IOS-D4 | Transcriber status strings never displayed | Fix | Shown as a status line on the live screen and in Model Library. | transcribers, Live session | B |
| IOS-D5 | Animated "listening" hero mode | Drop | Not built. | none | B |
| IOS-D6 | Speech benchmark screen unfinished | Keep, one fix | The worksheet stays. "Use latest transcript" fills from the most recent session: the active one if any, otherwise the newest stored one. | Benchmark | B |
| IOS-D7 | Health reply field the app ignores | Drop | The field no longer exists. | protocol 6.2 | B |
| IOS-D8 | Stale version fallback literal | Drop | The row shows "Unknown" when the version cannot be read. | Settings | B |
| IOS-D9 | Meta key "disabled during a session" rule unreachable | Drop | The UI rule is not built. The settings logic still refuses to save, test or remove a key during a session. | Meta key | B |
| IOS-D10 | Unreachable "download a model first" alert | Drop | The check and its text are not built; the transcriber's own "Prepare Voz first." / "Prepare Parakeet first." remain. | `SessionController` | B |
| IOS-D11 | Tests assert the absence of `dismiss-answer`; a mislabelled screenshot | Drop | No such control is built and the assertion is not ported. Screenshot labels name the fixture actually used. | UI tests | B |

### 4.2 iOS core (spec 02 section 11)

| Id | Finding | Call | Resulting behaviour | Where | By |
|---|---|---|---|---|---|
| CORE-A1 | For Codex answers the memory marker comes from the model | Fix | The phone records the last segment id when a fresh request is built and stores that as the marker after success, for both answer services. The reply's value is ignored. | `ContextBuilder`, `SessionController` | B |
| CORE-A2 | Context can exceed the relay's request limit; summary transcript uncapped | Fix | Byte budgets and a fitting step keep every body at or below 126,976 bytes. The summary transcript keeps its last 100,000 bytes and flags the cut. | `WireBudget` | B |
| CORE-A3 | OpenRouter "memory" is a raw tail | Keep | Unchanged. | `OpenRouterClient` | B |
| CORE-A4 | Instruction sent to OpenRouter twice | Fix | Sent once, as the preference message. The embedded context JSON omits it. | `OpenRouterClient` | B |
| CORE-A5 | Catalogue decoding is all-or-nothing | Fix | A malformed entry is skipped; a price that is not a string counts as absent. Only a missing or non-array `data` fails the catalogue. | `OpenRouterClient` | B |
| CORE-A6 | Fixed 4096 output tokens, no reasoning control | Keep | Unchanged. | `OpenRouterClient` | B |
| CORE-A7 | Stray spaces in the live partial | Fix | Empty partials are left out of the join. | `StreamTranscript` | B |
| CORE-A8 | Partials attributed to the most recently started turn | Fix | A `transcript` event updates the turn its `turnId` names when that turn is open; otherwise the active turn; otherwise it is dropped. Every old test still passes. | `StreamTranscript` | A |
| CORE-A9 | Acknowledgement rejects any first message with a `type` key | Keep | The Meta contract is kept exactly. | `MetaRealtimeAPI` | A |
| CORE-A10 | Segment times are approximate | Keep | Unchanged. | `StreamTranscript` | A |
| CORE-A11 | Cancellation mapped inconsistently | Fix | Cancellation surfaces as cancellation from every call, never as a connectivity error. | `Transport` contract, both clients | B |
| CORE-A12 | Persisted dates lose sub-second precision | Fix | ISO 8601 UTC with three fractional digits. | `LiveCueJSON` | B |
| CORE-A13 | Parser strictness | Keep | Unchanged. | `OpenRouterClient` | A |
| CORE-A14 | Pairing validation details | Fix | The grammar of protocol 4.3. A payload needs lower-case `https`; manual entry lower-cases it. The token has one exact shape. | `RelayEndpoint`, `PairingPayload` | A |
| CORE-A15 | PC status edge cases | Keep | Label order unchanged. The protocol guarantees an integer `elapsedSeconds` and a model whenever the state is not `stopped`. | `PCModelStatus`, relay | A |
| CORE-A16 | Usage oddities | Keep | Unchanged; the old tests assert them. | `TranscriptionUsage` | A |
| CORE-A17 | Sanitizer details | Fix in part | A JSON boolean is not a number for `turnId` or `audioProcessedMs`. The redaction scope is unchanged. | `MetaRealtimeAPI` | A |
| CORE-A18 | Malformed key reported as missing key | Fix | A key that contains whitespace gets its own message (section 6). An empty key keeps the missing-key message. | `OpenRouterError`, `MetaSpeechError` | B |
| CORE-B1 | Must the remake read old history? | No | No migration. Optional members stay optional so the remake's own later versions decode. | `Domain` | B |
| CORE-B2 | `RelayExecution` reused with zeros for OpenRouter | Keep | Names kept. | `Domain` | A |
| CORE-B3 | Limits counted in different units | Fix | The protocol names the unit of every limit (protocol 1 and 2). | protocol | B |
| CORE-B4 | Catalogue sort is locale-aware | Fix | Sorted by lower-cased name, then by id, without locale rules. | `OpenRouterClient` | A |
| CORE-B5 | UI hides the sentinel "Latest conversation" | Keep | Unchanged. | `Domain`, answer card | A |
| CORE-B6 | OpenRouter answer stored untrimmed | Keep | Unchanged. | `OpenRouterClient` | A |
| CORE-B7 | Reasoning effort is a free-form string | Keep | Unchanged. | `Domain` | A |
| CORE-B8 | Nothing cancels an in-flight Assist | Keep | No user-facing cancel. Internal cancel only (A-13). | `CodexRelayAnswerService` | B |
| CORE-B9 | No usage-reporting flag sent to OpenRouter | Keep | The request shape is kept exactly. | `OpenRouterClient` | B |
| CORE-C1 | `BenchmarkResult` never used | Drop | Not built. | none | B |
| CORE-C2 | `isFinal` is always true | Drop | The member does not exist; every segment is final. | `Domain` | A |
| CORE-C3 | `structuredOutputs` never used | Keep | An old test asserts it. | `Domain` | A |
| CORE-C4 | `is_free_tier` parsed and ignored | Drop | Not parsed. | `OpenRouterClient` | A |
| CORE-C5 | `usesPC` is an alias | Drop | One property, `isPCLocal`. | `SpeechProvider` | A |
| CORE-C6 | Unused POST branch in the request helper | Drop | Not built. | `OpenRouterClient` | A |
| CORE-C7 | `recommended` is true for every entry | Keep | The "RECOMMENDED" tag is user-visible. | `TranscriptionModel` | A |
| CORE-C8 | `facts` and `openQuestions` never displayed | Keep | They are part of the Codex exchange. | `Domain` | A |
| CORE-C9 | Sanitizer copies `final` and passes `speechEnd` | Keep | An old test asserts it; the Meta contract is unchanged. | `MetaRealtimeAPI` | A |
| CORE-C10 | Relay path that proxies Meta audio | Drop | Removed (D-14). | relay | B |
| CORE-C11 | Selected on-device model written and discarded | Drop | The preference does not exist. | `PreferencesStore` | B |

Answers to the core spec's open questions (its section 11.4): (1) the app owns the marker; (2) yes, bytes, and
the summary is capped; (3) the memory tail is reproduced, the duplicated instruction is fixed; (4) no
migration; (5) the Meta rate of $0.18 per hour dated 2026-09-28 stays a compiled-in constant; (6) naming the
two public vendor API hosts is acceptable under the public-repository rule.

### 4.3 Relay (spec 03 section 10)

| Id | Finding | Call | Resulting behaviour | Where | By |
|---|---|---|---|---|---|
| RELAY-B1 | Cancel returns `{"ok":true}` with status 404 | Fix | Success is `200 {"ok":true,"cancelled":true}`; nothing to cancel is `404 REQUEST_NOT_FOUND` with a normal error body. | `Routes` | B |
| RELAY-B2 | Cancelled and timed-out requests look like failures | Fix | `409 REQUEST_CANCELLED`, `504 ASSISTANT_TIMEOUT`, `500 ASSISTANT_FAILED`. | `Answers` | B |
| RELAY-B3 | Summaries cannot be cancelled | Fix | A summary carries a client `requestId` and the cancel route covers it. | `Answers` | B |
| RELAY-B4 | `requestId` accepts anything; a bad escape gives 500 | Fix | One shape everywhere. The cancel route matches the raw target by pattern; anything else is 404. | `Edge`, `Routes` | B |
| RELAY-B5 | The self-check shows a phone as connected | Fix | The self-check carries a one-time nonce and is not counted. | `Pairing`, `Edge` | B |
| RELAY-B6 | Exit code after a listen failure | Moot, fixed | No child process. A start failure is an exception and a `RelayFaulted` event; the console host exits 3. | `LiveCue.Relay`, host | B |
| RELAY-B7 | Terminal mode prints the token on every start | Fix | Printed only on `--show-pairing`. | console host | B |
| RELAY-B8 | Start event and banner always name one model | Fix | No model in the start event or the banner. | `Diagnostics`, host | B |
| RELAY-B9 | Omitted `assistant` falls back to a default that may not exist | Fix | `assistant` is required; the relay has no default model. | `Catalog`, `Routes` | B |
| RELAY-B10 | Codex output not validated | Fix | Validated against the schema and rebuilt from an allowlist. | `Answers` | B |
| RELAY-B11 | Speech lease covers cloud streams | Moot | No cloud streams exist. | none | B |
| RELAY-B12 | Cloud path has no finalization timer | Moot | No cloud path. | none | B |
| RELAY-B13 | Cloud byte allowance counted from socket open | Moot | No cloud path. The PC path counts from `ready`, as before. | `Speech` | B |
| RELAY-B14 | Lease held for the whole model load | Fix | The lease is released when the phone leaves or after 170 s; the load continues as an ordinary operation. | `Speech`, `LocalModel` | B |
| RELAY-B15 | Catalogue time is outside the answer timer | Fix | One 60 s deadline for the whole request; the phone waits 70 s. | `Edge`, `Answers` | B |
| RELAY-B16 | A pagination cursor is total failure | Fix in part | The first page is used. No further page is requested (section 1, item 9). | `Catalog` | B |
| RELAY-B17 | PC model error state never clears by itself | Fix | Latched for 120 s; cleared at once by a usable probe or a new operation. | `LocalModel` | B |
| RELAY-B18 | Lab outbound overflow closes silently | Fix | The lab sends an `error` message before closing. | lab `server.py` | B |
| RELAY-B19 | Lab `/stream` logs exception text | Fix | Exception type name only, on both routes. | lab `server.py` | B |
| RELAY-B20 | Start script's last poll ignores `relayProtocol` | Fix | It requires `relayProtocol` 1. | `Start-ASR-Lab.ps1` | B |
| RELAY-B21 | Proxy accepts a non-numeric `Content-Length` | Moot | No proxy. The HTTP parser rejects it. | Kestrel | B |
| RELAY-B22 | Health reads the cloud key on every request | Moot | No cloud key. | none | B |
| RELAY-A1 | No device list exists | Keep | None is added. The verify route ignores its body. | `Routes` | B |
| RELAY-A2 | README and code disagree about the pairing button | Resolved | "Show QR" redisplays; "Reset pairing" revokes. The README says so. | `Pairing`, desktop | B |
| RELAY-A3 | `previousTokenHash` never expires | Drop | There is one accepted token and no legacy file format. | `Pairing` | A |
| RELAY-A4 | Cloud speech path kept for older phones | Drop | Removed. | none | B |
| RELAY-A5 | Lab runs from an unconfigurable sibling folder; setup is unscripted | Fix | The lab is in the repository; its folder is a setting; building images and fetching weights are documented manual scripts. | `asr/`, `SettingsStore` | B |
| RELAY-A6 | Proxy adapter deployed by copying | Drop | No adapter. | none | B |
| RELAY-A7 | Proxy admits only 43-character tokens | Resolved | Every token has that shape and both sides require it. | protocol C-02 | B |
| RELAY-A8 | `phone-seen` for HTTP only | Fix | Authenticated speech upgrades count too. | `Edge` | A |
| RELAY-A9 | `speech-status` reused for three things | Fix | Three events: `AssistantNotice`, `LocalModelChanged`, `SpeechStreamChanged`. | `Diagnostics` | A |
| RELAY-A10 | Phone-facing strings name products | Changed | The phone owns its texts (protocol 14). Relay texts are fixed fallbacks and no longer mention Meta. | protocol 14 | B |
| RELAY-A11 | Catalogue client reports a stale version | Fix | It reports the relay version. | `Catalog` | A |
| RELAY-A12 | Event allowlists differ by provider | Fix | One allowlist; finite numbers only; `audioProgress`, `speechEnd` and `final` are not part of the relay stream. | `Speech` | B |
| RELAY-A13 | No HTTP timeouts configured | Fix | Explicit header, body, keep-alive and route limits. | `Edge` | B |
| RELAY-A14 | Proxy limits do not match the relay's | Resolved | One set of numbers (protocol section 2). | protocol | B |
| RELAY-A15 | Lab page shows a hard-coded GPU label and "English test" | Fix, cosmetic | The GPU label is removed. "English test" stays. | lab page | A |
| RELAY-D1 | Module-level controller singleton | Drop | Not built. | none | B |
| RELAY-D2 | Lab health `ready` can never be false; unread members | Keep | Lab protocol 1 is kept exactly. | lab | B |
| RELAY-D3 | Dockerfile copies files that the bind mount hides | Drop | The image holds dependencies only. | `Dockerfile` | A |
| RELAY-D4 | `benchmark.py` and its packages; unused `curl` and `python-multipart` | Keep the tool, drop the unused | `benchmark.py`, `jiwer`, `scipy` and `soundfile` stay. `curl` and `python-multipart` are removed. | `asr/` | A |
| RELAY-D5 | `includeHidden: false` and a hidden filter | Keep | The request is kept exactly; hidden entries are still filtered. | `Catalog` | A |
| RELAY-D6 | Proxy pipes a body for every method | Moot | `GET` and `DELETE` with a body are refused. | `Edge` | B |
| RELAY-D7 | Redundant pause and unknown stdin actions accepted silently | Moot | Typed calls; a redundant pause does nothing. | `IRelay` | B |
| RELAY-T1 | Creating a relay starts a real Codex process | Fix | Catalogue warm-up is an option that tests switch off; the catalogue source is a seam. | `RelayDependencies` | A |
| RELAY-T2 | Tests need a signed-in Codex | Fix | Tests use a fixture catalogue. | relay tests | A |
| RELAY-T3 | Pairing tests need Windows DPAPI | Keep | They run in the Windows CI job; all other tests use the protector seam. | relay tests | A |
| RELAY-T4 | Tests wait on a self-check to an unreachable host | Fix | The self-check client is a seam. | `RelayDependencies` | A |
| RELAY-T5 | Verification notes quote stale test counts | Drop | Not carried. | none | A |

### 4.4 Windows desktop, scripts and CI (spec 04 section 8)

| Id | Finding | Call | Resulting behaviour | Where | By |
|---|---|---|---|---|---|
| WIN-1 | README says the pairing button rotates the token | Resolved | See RELAY-A2. | desktop, README | B |
| WIN-2 | README says to click Start; the launcher auto-starts | Resolved | The relay starts automatically once the window has rendered. The README says so. | desktop | A |
| WIN-3 | "Gated release" can be dispatched from any ref | Fix | A release runs only from `main` and runs the whole CI in the same run. | `release.yml` | A |
| WIN-4 | Default texts that are never shown | Drop | Not built. | desktop | B |
| WIN-5 | Stale names (launcher, resource, icon prompt) | Drop | Not carried. | none | B |
| WIN-6 | `-TestExpanded` leftover | Drop | Self-test arguments of architecture 3.13. | desktop | B |
| WIN-7 | Unused `model` field of the ready event | Drop | `RelayStarted` has no model. | `Diagnostics` | B |
| WIN-8 | Message names a launcher that does not exist | Drop | Start-up problems are shown in the window. | desktop | B |
| WIN-9 | Static transcript "indicator" | Drop | Not built. | desktop | A |
| WIN-10 | One bad output line stalls the event loop | Fix | Typed in-process events; an error while handling one is isolated and shown as a diagnostic. | desktop | B |
| WIN-11 | Window frozen during start | Fix | Checks run off the UI thread. | `Prerequisites`, desktop | A |
| WIN-12 | Status preserved by keyword matching | Fix | An explicit state machine; a fault text stays until the next start. | desktop | A |
| WIN-13 | Diagnostics race on process exit | Moot | No output pipe. | none | B |
| WIN-14 | Stale UI after stop or exit | Fix | Stop resets the pause flag, the hints and the timing caption (the old tooltip is now a visible caption). "Clear activity" restores the placeholders. | desktop | A |
| WIN-15 | Phone status never returns to "not connected" | Keep | Connected, then idle with the age in the tooltip; reset when the relay stops. | desktop | A |
| WIN-16 | Start re-applies a root mapping and nothing removes it | Fix | The mapping manager and its rules (architecture 3.9). | `MappingManager` | B |
| WIN-17 | Port literal in several places | Fix | One setting. | `SettingsStore` | B |
| WIN-18 | Node version not checked | Moot | No Node.js. | none | B |
| WIN-19 | Building rewrites a tracked icon | Fix | Icons are committed; a build never rewrites them. | `LiveCue.IconTool` | A |
| WIN-20 | Every QR display decrypts and verifies again | Fix | One decryption and one self-check per explicit "Show QR". Returning to the page while a QR is shown does neither. | desktop, `Pairing` | A |
| WIN-21 | Activity log shows full payloads | Keep | Memory only; the window says so. | desktop | A |
| WIN-22 | No single-instance guard | Keep | A second instance shows the port-in-use text and does not disturb the first. | desktop | A |
| WIN-23 | Launcher misreports script errors | Moot | No script launcher. | none | B |
| WIN-24 | No script creates the speech-key or connection file | Moot | No PC speech key; the connection is a setting. | none | B |
| WIN-25 | Unclear which lab folder is authoritative | Fix | A setting, defaulting to `asr\` beside the executable. | `SettingsStore` | B |
| WIN-26 | Device management | None | Not added. | none | B |
| WIN-27 | Desktop still reports the relay's Meta route | Drop | Removed. | desktop | B |
| WIN-28 | Test-reuse gate rejects normal version bumps | Drop | No reuse gate. | `release.yml` | A |
| WIN-29 | Tests on one macOS image, build on another | Fix | The IPA is built on `macos-26` with the pinned Xcode; tests run on both images. | `ci.yml`, `release.yml` | A |
| WIN-30 | Simulator-selection comment disagrees with the code | Fix | Each test leg names its exact runtime. | `select_simulator.py` | A |
| WIN-31 | Screenshot export knows only one artefact name | Drop | Every test run exports its screenshots; no separate workflow. | `ci.yml` | A |
| WIN-32 | Per-test limits only in the release workflow | Fix | One test job definition, used by both workflows. | `ci.yml` | A |
| WIN-33 | Relay tests, desktop self-test and audit outside CI | Fix | All three run on every push. | `ci.yml` | A |

---

## 5. Acceptance criteria changed or retired

Every old criterion not listed here, or in protocol section 16 (which covers all `AC-RELAY` and `UT` ids),
carries over unchanged, read with the remake's names (bundle, executable, artefact names, relay port).

### 5.1 iPhone app (`AC-IOS`)

| Id | Outcome |
|---|---|
| AC-IOS-008 | Changed: readiness reads "Ready" (a chip with a check) instead of "✓ Ready"; "Setup" is unchanged. |
| AC-IOS-020, 029 | Changed: the state label reads "Connecting…" while starting or resuming, "Pausing…" and "Stopping…" instead of "Updating…". "Listening" and "Paused" are unchanged. |
| AC-IOS-022 | Holds for the race only: the Start control is disabled with a caption while the PC model cannot start. The alert reads "PC speech model: … Open Transcription to start it or check its status." |
| AC-IOS-028 | Changed: the expanded transcript follows new text only while the reader is at its end; otherwise "Jump to live" is offered. |
| AC-IOS-043, 044, 045, 046 | Extended: they now hold for every speech provider, not only Meta and PC speech (IOS-B4, IOS-B5); for on-device providers the queue limit of 046 is 128 and has its own text. Changed: the reason is shown in the session notice, not in an alert. |
| AC-IOS-047 | Changed: after Pause the problem is reported by the session notice titled "Paused"; after Stop by the alert on Home. |
| AC-IOS-070 | Extended: copying shows the toast "Copied". |
| AC-IOS-078 | Changed: the summary is requested only when the session has at least one segment (IOS-B12). |
| AC-IOS-083 | Changed: the swipe action asks for confirmation; "Delete" then removes the session permanently (IOS-A4). |
| AC-IOS-090 | Changed: the Appearance row reads "Daylight". |
| AC-IOS-093 | Changed: choosing a provider applies immediately and stays on the Transcription screen (IOS-A5). |
| AC-IOS-095, 096, 097 | Changed: the provider's rows are shown on Transcription and on Settings; the PC model controls are the PC speech model screen, where AC-IOS-099 to 106 hold. |
| AC-IOS-104 | The "session is active" clause holds in the logic; the screen cannot be open during a session. |
| AC-IOS-109 | Changed: an empty key gets the missing-key message; a key containing whitespace gets the malformed-key message (section 6). |
| AC-IOS-113 | Holds in the settings logic; there is no UI state for it (IOS-D9). |
| AC-IOS-132 | Holds for the canonical endpoint form (protocol 4.3). |
| AC-IOS-136, 137 | Hold, with the state following the most recent check only (IOS-B11) and texts from protocol 14. |
| AC-IOS-140 | Retired: no legacy pairing exists to migrate. |
| AC-IOS-154 | Changed: the session is saved at least once per second **from the moment the transcriber is connected**; after an app kill it appears in History closed at its last save, titled by the normal rule (IOS-B2). |
| Spec 01 section 2.10 | Changed with AC-IOS-154: an interrupted session no longer keeps the title "New conversation". |

### 5.2 iOS core (`AC-CORE`)

| Id | Outcome |
|---|---|
| AC-CORE-004 to 008 | Hold with a fixture token of the 43-character shape. `short` still fails. |
| AC-CORE-011 | Holds unchanged: 100,000 `x` gives a transcript of exactly 80,000 characters, because the budget is 80,000 bytes. |
| AC-CORE-024, 081, 084 | Hold, read against the single property `isPCLocal` (CORE-C5). |
| AC-CORE-025 | Holds: all three keys still fail. The two that contain whitespace now fail as "malformed", the empty one as "missing" (CORE-A18). |
| AC-CORE-046 | Holds, and the same marker rule now applies to Codex answers (CORE-A1). |
| AC-CORE-S03 | Retired: non-final segments do not exist (CORE-C2). |
| AC-CORE-S06, S07, S08 | Replaced by protocol P2-202 and P2-203: a path prefix is allowed; the token has one exact shape. |
| AC-CORE-S09 | Changed: sort order is by lower-cased name and then id (CORE-B4); a malformed entry is skipped (CORE-A5). |
| AC-CORE-S11 | Holds; in addition the embedded context JSON has no `instruction` member (CORE-A4). |
| AC-CORE-S16 | Changed for partial attribution (CORE-A8); the other two statements hold. |
| AC-CORE-S19 | Holds; a boolean `turnId` or `audioProcessedMs` is dropped (CORE-A17). |

### 5.3 Windows window and scripts (`AC-WIN`, `AC-SCR`)

| Id | Outcome |
|---|---|
| AC-WIN-002 | Holds unchanged: the minimum window size is 960 x 620; the default is 1240 x 660 (design system section 10, Windows screens 1.2). The self-test screenshots of architecture 3.13 are taken at those two sizes. |
| AC-WIN-003 | Changed: the checks are Codex found, Codex signed in, Tailscale installed, Tailscale online with a DNS name. No Node.js or dependency checks. The first failing check's message is still the relay status; the window also lists every check with its result. |
| AC-WIN-019 | Changed: there is one way to the pairing view, the Pairing navigation row. The QR is requested with "Show QR", which is enabled when the relay runs and the connection is set up. |
| AC-WIN-006 | Changed: only "Relay started. Waiting for your iPhone." There are no speech-key lines. |
| AC-WIN-007 | Changed: the Public-mode line is shown; Tailscale configuration changes only on an explicit action. |
| AC-WIN-008 | Retired. Replaced by the "connection not set up" state and rules TS-1 to TS-10. |
| AC-WIN-009 | Holds with the new port-in-use text (section 6). |
| AC-WIN-014 | Reworded: if the relay faults while running, the window shows the fault, returns to the stopped layout and does not restart it. |
| AC-WIN-015 | Trivially true: the relay lives in the window's process. |
| AC-WIN-022 | Changed: on the very first start the QR opens by itself once the connection is set up (at once when the mapping already exists). |
| AC-WIN-034 | Holds for `Diagnostic` events. |
| AC-WIN-038 | Replaced by architecture 3.13. |
| AC-SCR-001 | Holds: one executable, no console window, automatic start. |
| AC-SCR-002 | Retired: there is no separate launcher. |
| AC-SCR-003 | Holds through `LiveCueRelayHost.exe` and `--check-only`. |
| AC-SCR-004 | Replaced by the desktop's "Reset pairing" (protocol P2-143). |
| AC-SCR-005, 006 | Replaced by endpoint resolution and settings validation (architecture 3.3, 3.5). |
| AC-SCR-007, 008 | Retired: the PC holds no speech key. |
| AC-SCR-009 | Changed: the explicit setting comes first, and only a real `codex.exe` is accepted. |
| AC-SCR-011 | Changed: the icon has the frames the design system lists (it adds 20 and 40 px). |

### 5.4 CI and audit (`AC-CI`, `AC-AUD`)

| Id | Outcome |
|---|---|
| AC-CI-002 | Holds for `ci.yml`, which now also runs the Linux, Windows, lab and audit jobs. |
| AC-CI-004 | Changed: each leg names its exact simulator runtime; a device is created if that runtime has none. |
| AC-CI-006 | Holds on two OS versions (iOS 26.2 and iOS 18.6). |
| AC-CI-009, 010, 011 | Retired: there is no reuse gate. The release requires the whole CI to pass in the same run. |
| AC-CI-013, 014, 016, 017 | Hold with the names `LiveCueRemake.app`, `LiveCueRemake.ipa` and the bundle identifier of D-03; the release also carries the Windows zip. |
| AC-CI-016 | Changed by the security review (section 8, SEC-12): a release that exists is never replaced and its tag is never moved. The release job fails when the tag exists; a corrected build is a new version. |
| AC-CI-019 | Retired: every test run exports its screenshots. |
| AC-AUD-002 | Changed: the exact-value check takes an optional local list (AUD-8). CI runs without it and says so. |
| AC-AUD-003, 004 | Hold as AUD-1 and AUD-7; AUD-2 to AUD-6 are new (private host names, addresses, personal paths, e-mail addresses, pairing data). |

---

## 6. User-visible text changes

Every string of the specs is kept verbatim except the ones below. A designer may refine the wording of a
*new* string; an *old* string changes only if it is in this table.

### 6.1 iPhone

| Where | Old | New |
|---|---|---|
| Settings, Appearance | "Midnight Mint" | "Daylight" |
| Settings, Version, when unreadable | "0.5.1" | "Unknown" |
| Local-network usage description | "LiveCue connects privately to your Windows PC over your Tailscale network." | "LiveCue connects privately to your Windows PC." |
| Every message that used to be the relay's text shown verbatim | relay text | The phone's own text for the code (protocol 14.1, 14.2) |
| Voz 30-minute message (now a session notice) | "Pending recording reached 30 minutes. Tap Assist to transcribe it." | "Pending recording reached 30 minutes and was transcribed. Resume to keep recording." |
| On-device processing fell behind (session notice) | "Transcription could not keep up. Stop and restart the session." | "Transcription could not keep up. Recording paused; resume to continue." |
| On-device processing failed during a session (session notice) | the speech package's error text | "Transcription stopped on this iPhone. Resume to continue." |
| Session notice titles (new) | none | "Recording paused"; "Paused" for a failed finalisation on Pause |
| Live session state label | "Updating…" | "Connecting…" (starting, resuming), "Pausing…", "Stopping…" |
| Home, readiness header | "✓ Ready" | A chip reading "Ready" |
| Home, rows | Readiness rows "Transcription", "Windows relay", "Meta connection", "PC speech model" and a setup row "Transcription & appearance" with the detail "Cloud, PC or iPhone speech" | One row "Transcription" whose subtitle carries "Meta connection: …" or "PC speech model: …"; the pairing row carries the relay values (`02-ios-screens.md` section 3) |
| Three messages that named "Transcription settings" (Meta missing key, Meta connection not accepted, PC model start check) | "… Transcription settings …" | "… Transcription …" (`04-ios-architecture.md` 1.3) |
| Three messages that named "Assistant settings" (OpenRouter missing key, rejected key, no model chosen) | "… Assistant settings …" | "… Assistant models & timing …" (`04-ios-architecture.md` 1.3) |
| Settings, Text size | "16 pt · follows Dynamic Type" | "Follows your iPhone's text size" |
| Speech benchmark, title and purpose | Title "Model Lab"; a purpose text of two sentences | Title "Speech benchmark"; the first sentence only |
| Model Library tag | "RECOMMENDED" | "Recommended" |
| History, delete (new) | none | The confirmation of `02-ios-screens.md` section 13 |
| Strings marked **(new)** in `02-ios-screens.md` | none | Adopted as written there |
| Saving or using a key that contains whitespace (Meta and OpenRouter) | the missing-key message, or "Enter a valid API key." | "This key contains spaces or line breaks. Paste it again exactly as issued." |
| Meta key screen, text S-07 | ends "… The key previously saved on Windows is not automatically copied; paste your Meta key here once." | That sentence is removed. The PC never holds a Meta key. |
| Settings privacy text S-06 | as in the spec | One sentence added at the end: "The Voz speech package reports anonymous usage to its vendor." |
| Settings, About (new) | none | The two attribution and usage rows of architecture 4.13 |
| Model Library, Voz entry (new) | none | The usage-reporting sentence of architecture 4.13 |
| Pairing, "Quick pairing" hint | "Start LiveCue Relay on Windows. Paste the pairing JSON shown by the launcher, or enter the values manually." | "Open LiveCue Desktop on Windows and scan its QR. Without a camera, paste the pairing text from the PC, or enter the values manually." |
| Pairing, address placeholder | `https://<pc-name>.<network>.ts.net` | `https://<pc-name>.<network>.ts.net:11443/` |
| Microphone and camera denied alerts | as in the spec | The old sentence, then: "If you use LiveContainer, allow it for LiveContainer in Settings." |
| Live session status panel (new) | none | The transcriber status line (architecture 4.7) |
| Pairing, replacing a saved PC by another host (new; section 8, SEC-13) | none | Dialog "Replace this PC?": "Replace `<current host>` with `<new host>`? Only do this with the QR shown by LiveCue Desktop on your own PC."; when the new host is not a Tailscale name also "`<new host>` is not a Tailscale address."; buttons "Replace" and "Cancel" |

### 6.2 Windows

| Where | Old | New |
|---|---|---|
| Port in use | "The relay port is already in use. Close the old LiveCue relay terminal, then click Start relay." | "Port `<n>` is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay." |
| Activity on start, speech key | "Meta Muse is configured. …" / "Meta cloud key not configured. …" | Removed |
| Activity on start, internet mode | "Funnel enabled: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address." | "Public mode: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address." |
| Connection not set up (new) | none | "Connection not set up. Choose Set up connection to let your iPhone reach this PC." |
| Pairing placeholder | "Generate a QR to get started" | "Show the QR to pair an iPhone"; while the relay is stopped "Start the relay to show the QR"; while the connection is not set up "Set up the connection first." |
| Pairing, reset (new) | none | Button "Reset pairing…"; dialog title "Reset pairing?", body "This replaces the pairing code. Every iPhone paired with this PC must scan the new QR and pair again. The relay keeps running." (the last sentence only while it runs) |
| Failed request, cancelled by the phone | "Assistant request failed or timed out. Check Codex sign-in and retry." | "Cancelled by the iPhone." |
| Punctuation of status texts | "Checking Codex and Tailscale...", "Thinking..." | "Checking Codex and Tailscale…", "Thinking…" (one ellipsis character) |
| Request failed, status | "Request failed - ready to retry" | "Request failed. Ready to retry." |
| Request failed, reply hint | "Not sent - try Assist again" | "Not sent. Try Assist again." The reply card adds the title "Nothing was sent to the iPhone" |
| Relay stopped by a fault | "Relay stopped - click Start relay to reconnect" | "Relay stopped. Click Start relay to reconnect." |
| Reply hint | "`<model>` / `<reasoningEffort>` / `<s>` s Codex", or "Sent to iPhone  /  `<s>` s" | "Sent to iPhone · `<model>` · `<effort>` · `<s>` s Codex". The form without execution data is not built: every reply carries it |
| Relay total time | Tooltip "PC relay total: `<s>` s. Codex includes startup and cloud processing." | The same text as a visible caption under the reply |
| Card captions | "FROM IPHONE", "ASSISTANT" | "From iPhone", "Assistant" (sentence case) |
| Closing notice | "Closing LiveCue stops its relay." (activity overlay) | "Closing this window stops the relay." (sidebar, always visible) |
| Activity footer | "Activity stays in memory on this PC" | "Activity stays in memory on this PC. It is cleared when LiveCue closes and is never written to disk." |
| Pairing footnote | "Pairing codes are private. Codes hide after 2 minutes." | "Pairing codes are private. Codes hide after 2 minutes. Showing the QR again keeps existing pairings valid." |
| Prerequisite failures | One message per check | The same message as the relay status, and in the check list split into a title and a fix (Windows screens 2.6) |
| Desktop startup failure box | "LiveCue could not finish startup. Run the diagnostic PowerShell launcher for details." | "LiveCue could not finish startup." and the name of the error type |
| "Desktop connection interrupted. Stop and restart the relay." | shown when the window could not read relay output | Removed: there is no output pipe (WIN-10, WIN-13) |
| Pairing text for a phone that cannot scan | Printed by the terminal launcher at every start | Not shown or copied by the desktop (A-16); printed only by `LiveCueRelayHost.exe --show-pairing` |
| Unreadable pairing file (new) | relay did not start, no guidance | "The saved pairing could not be read. Reset pairing to create a new one." |
| PC model status texts | two of them ended "No audio was sent to Meta." | That sentence is removed (protocol 9.4) |
| "Tailscale HTTPS setup failed. Check that Tailscale is connected." | shown at start | Shown when "Set up connection" fails |
| Port in use while a Tailscale mapping points at the port (new; section 8, SEC-05) | none | "Port `<n>` is in use by another program while a Tailscale mapping points at it. That program may have received your iPhone's requests, including the pairing token. Free the port, start the relay, then reset pairing." |
| Private mode with a public mapping still pointing at the relay (new; SEC-04) | none | The relay does not start: "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode." |
| Exposure warnings for mappings LiveCue does not manage, and "exposure unknown" (new; SEC-04) | none | The texts of `05-windows-architecture.md` 4.15 and 5.5 |
| Codex version and containment check (new; SEC-01) | none | Settings row "Codex version", button "Mark as checked", and the warning "Codex is version `<v>`; the containment check was last run for `<w>`. Run the containment check, then choose Mark as checked in Settings." (variants in `05-windows-architecture.md` 4.14) |
| Activity, rejected requests (new; SEC-14) | none | "Rejected since the relay started: `<a>` malformed · `<b>` with an unknown token · `<c>` through Funnel in Private mode" |
| Settings, screen capture (new; SEC-10) | none | Toggle "Hide this window from screen capture", caption "Screen sharing, recordings and screenshots show an empty area instead of this window." |

---

## 7. Open risks and manual checks

For the orchestrator to weigh before building starts.

| # | Risk | Why it matters | Suggested handling |
|---|---|---|---|
| 1 | **Nothing here was compiled.** The Swift side can only be built on hosted macOS runners; the iOS 26 symbols, strict concurrency with two third-party speech packages, and the unsigned build are proven only by the first green CI run. | A wrong assumption costs a CI cycle each time. | Build the core package and the relay first (both testable without a Mac), then the app. |
| 2 | **Public mode makes new code an internet edge**, guarded by one shared token with no lockout. | A flaw in the edge checks is exposed to the whole internet. | Give the `Edge` area of the relay its own security review and run protocol 15.1 against the published build before Public is first applied. |
| 3 | **Tailscale behaviour was read from source, not exercised**: prefix stripping, the Funnel request header, the serve-versus-funnel flag interaction, path-scoped removal, the shape of the status JSON. First use of HTTPS or Funnel on a tailnet also needs approval in the admin console, during which the CLI waits. | The mapping manager's safety rests on these facts; the PC has other live mappings. | Before the first "Set up connection" on the owner's PC, run only the two read commands and compare with architecture 3.9. Never test removal on a shared port. |
| 4 | **Device-only behaviour**: background capture, interruptions on the on-device path, Parakeet's partial semantics (IOS-A1), whether Meta closes normally after `endStream` (IOS-A11), Keychain inside LiveContainer, a long WebSocket through Funnel on a mobile network. | CI cannot see any of it. | A short manual script on a real iPhone before calling the build done. |
| 5 | **Codex integration**: only a real `codex.exe` is accepted (section 1, item 7); the catalogue stops at 100 entries; `codex login status` and `--output-schema` behave as the old relay assumed. | A Codex update can change any of these. | The relay's Codex tests use stubs; one manual run against the real CLI is needed on the PC. |
| 6 | **The lab cannot be exercised in CI or, today, on the build PC** (Docker is not running there, and the weights are absent). The upstream sources of the two models' weights are not named in the spec. | PC speech is verified only by protocol tests with fakes. | The lab builder records the weight sources in `asr/README.md`; the manual GPU check (P2-M01 to M03) is run when the owner starts Docker. |
| 7 | **Voz** is source-available, forbids interfering with its usage reporting, and makes the app contact its vendor. | The disclosure wording is the owner's to accept. | Confirm the two sentences of architecture 4.13 before release. |
| 8 | **WPF off-screen rendering on a hosted runner** (self-test screenshots, icon tool) is assumed to work. | If it does not, desktop screenshots must be taken on the PC. | Try it in the first Windows CI run; the self-test's assertions do not depend on the screenshots. |
| 9 | **The design system still describes a device row, a tray icon and "New code"** (section 1, item 10). | A screen designer could build them. | Tell the two screen designers to follow this register. |
| 10 | **Xcode 26.3 may leave the hosted images** once Xcode 27 reaches them. | The pin fails fast, by design. | Re-run the platform research when that happens; do not unpin. |
| 11 | **Phone timeouts changed** (A-21): verify 65 s to 15 s, catalogue 65 s to 45 s, assist 65 s to 70 s. | A very slow link could now fail a pairing check that used to pass. | Accept; the relay answers these routes in milliseconds. |
| 12 | **One shared answer slot** (A-06): an Assist tapped while the previous session's summary is still running gets "The PC is processing another request." | Old behaviour, but more visible now that the message is clear. | Accept, or let the orchestrator ask for two slots. |

Manual checks that no automated job covers: protocol P2-M01 to M03 (real GPU); install and launch of the IPA
through LiveContainer on a physical iPhone; microphone, background and interruption behaviour; one real
Private pairing and one real Public pairing; one real Codex answer and summary; "Reset pairing" with a paired
phone. The security review added three: the containment check against the installed Codex (test plan L-4),
and the two checks from outside the tailnet (test plan ST-27 and ST-28) before Public mode is first used.

---

## 8. Review rulings

Two reviews were made of the documents before building started: a cross-check of protocol 2 against the
iPhone design and the Windows design (items `X-nn`, mismatches, and `A-nn`, ambiguities; the working file is
local and is not in the repository), and the security review `07-security-review.md` (findings `SEC-nn`,
tests `ST-nn`). The orchestrator ruled on every item. This section records the rulings; the text of each
change is in the sections named. The `A-nn` of this section are the cross-check's ambiguities, not the
architect's decisions of section 3.

"Where": **P** `02-relay-protocol.md`, **A** `03-system-architecture.md`, **I** `04-ios-architecture.md`,
**W** `05-windows-architecture.md`, **T** `06-test-and-ci-plan.md`, **D** this file. What each builder must
do differently is listed per work package at the end of the two designs (I section 9, W section 7).

### 8.1 Cross-check: mismatches

| Id | Subject | Ruling | Where |
|---|---|---|---|
| X-01 | Marker in an assist reply | Adopted, in the orchestrator's form: the relay never forwards a model-written marker and always sends `memory.throughSegmentId` as JSON `null` (the phone owns the marker, D-11); the phone's decoder still tolerates and ignores any string there | P 8.3, 12.2, 13.5, P2-065; I 3.1.2, 3.1.5; W 4.9; T 1.4; D A-14 |
| X-02 | Step M-4 missing from the Windows command table | Adopted | W 4.10 |
| X-03 | `Date` response header | Adopted | P 5.2, 5.3; W 4.3 (K-1) |
| X-04 | Keep-alive frame is an unsolicited Pong | Adopted; the device check covers it | P C-57, 10.5; I 6.5 item 10; W 4.3 (K-2) |
| X-05 | `loading.message` and `provider` | Adopted | P 10.3 |
| X-06 | `busy` while an operation runs | Adopted | P 9.1 |
| X-07 | `MODEL_OPERATION_IN_PROGRESS` after admission | Adopted | P 9.3 rule 7, 10.6 |
| X-08 | Where a stream failure is shown | Adopted | P 14.3 |
| X-09 | Fixture for an offline PC | Adopted; no ported UI test asserts the old text "PC offline." | I 4.9; A 4.11 |
| X-10 | Fixture PC model, first status | Adopted | I 4.9 |
| X-11 | Number of cases of P2-004 | Adopted | T 1.2 |
| X-12 | Cross-reference for `PAIRING_PAYLOAD_INVALID` | Adopted | P 4.3 |
| X-13 | When step 3 of the body fitting runs | Adopted | P 13.4; I 6.1 |

### 8.2 Cross-check: ambiguities

| Id | Subject | Ruling | Where |
|---|---|---|---|
| A-01 | Transport idle timeout | Adopted: the route's phone timeout plus 10 s; 60 s or more for the speech socket | P 13.1; I 3.1.5, 3.2.1, 6.1 |
| A-02 | What the phone reports when C-59 or C-60 passes | Adopted: `L-TIMEOUT`; `L-STREAM-DROPPED` | P 13.7, 14.2; I 3.1.9, 6.1 |
| A-03 | An unknown frame type before `ready` | Adopted: ignored in every state | P 13.7; I 3.1.6, 6.1 |
| A-04 | Value of `provider`; integer members | Adopted: the model id from the header, never a label | W 4.11 |
| A-05 | Health 200 with `ok` not true | Adopted: `L-UNREADABLE` | P 13.3; I 6.1 |
| A-06 | Echo on a summary reply | Adopted: checked on assist only | P 8.3 |
| A-07 | Routes 3 to 6 not written down in the Windows design | Adopted | W 4.6, 4.10 |
| A-08 | An offered WebSocket extension | Adopted: ignored, never a reason to refuse | P 10.1; W 4.11; T 1.6 |
| A-09 | A frame larger than C-21 | Adopted: the transcript is cut further | W 4.11; T 1.6 |
| A-10 | `409 REQUEST_CANCELLED` read by the phone | Adopted: no special case | P 8.5 |
| A-11 | A slow body on a route with a short deadline | Adopted | P 5.4 |
| A-12 | Lab close while waiting for the lab's `ready` | Adopted | W 4.11 |
| A-13 | Request id text in the body and in the cancel path | Adopted: `uuidString` on the phone, ordinal comparison on the relay | I 3.1.5, 6.1; W 4.9 |
| A-14 | A `loading` frame with a missing member | Adopted: still `loading` | I 3.1.6, 6.1 |

### 8.3 Security review

| Id | Finding | Ruling | Where |
|---|---|---|---|
| SEC-01 | Codex is contained by a deny-list of one tool | **Adopted in this form.** Every Codex process gets an allowlisted environment (the list is in P 12.6). The builder reads the installed CLI's help output, passes every switch that turns off a tool, a feature or a network capability, keeps the read-only sandbox and the empty per-request working folder, and records in W 4.9 what could not be switched off. An opt-in live containment check (a canary file and a prompt-injection transcript) is added. The desktop shows the Codex version in use and warns when it changed since the last containment check. The model id shape gets a length limit, and answers are drawn as plain text on the phone. **Not adopted:** the tool-list check against a loopback stand-in, verification by executable hash, and any refusal that depends on the mode. D-01's "Codex invocation preserved exactly" gives way to this in the environment and the added switches only | P 12, 12.2, 12.4, 12.6, C-06, P2-064; A 3.5, 3.7, 3.10, 6.1, 6.4, 6.5; W 3.2, 3.4, 3.5, 3.9, 4.8, 4.9, 4.13, 4.14, 5.4, 5.5, 5.8; I 6.2, 8.6; T 1.4, 2.2, 3 (L-4), 4.6 |
| SEC-02 | The QR shows the permanent bearer token | **Not adopted for 1.0: accepted risk** (8.4). The QR keeps carrying the permanent token, as the old app did. It is the first question for the owner (8.5) | A 6.2, 6.5; D 8.4, 8.5 |
| SEC-03 | On the phone the pairing file is the authority | Adopted, at the review's minimum level: the Keychain item is the authority; the file is a fallback read only when the Keychain has no item or cannot be read; when both exist and differ the Keychain wins and the file is rewritten from it; the Keychain is never written from the file | A 4.10, 6.1, 6.2, 6.5; I 2.4, 3.2.2, 4.8, 6.2; T 4.6 |
| SEC-04 | A public mapping can outlive the owner's intent without any warning | Adopted. Exposure warnings come from live Tailscale inspection (the exposure report), not only from the ledger, and are shown on the Live page; the relay does not start in Private mode while a Funnel handler targets the relay port; the private listener may not use 443, 8443 or 10000; a settings file that fails validation keeps its valid ledger entries. **Two parts of the review's fix were not taken:** "Record this mapping as LiveCue's" (it would let the application remove a handler it did not create, against D-20 and TS-8; the removal command is shown as text instead), and the reworded self-check text (copy belongs to the screen design; W 7.4) | A 3.5, 3.9, 3.11, 6.3, 6.4; W 3.4, 3.6, 3.7, 3.9, 4.14, 4.15, 4.16, 5.4, 5.5, 5.8; D 6.2; T 2.1, 4.6 |
| SEC-05 | While LiveCue is closed, a local program can take the relay port | **Root cause accepted for 1.0** (8.4). Adopted mitigations: the port-in-use text says what may have happened when a mapping points at the port; the listen socket is opened for exclusive use | P 5.1; A 3.9, 3.11, 6.5; W 3.7, 4.3, 4.16, 5.4; D 6.2; T 4.6 |
| SEC-06 | Launcher scripts run from a configurable folder | Adopted: the relay runs only its own embedded copies of the launcher scripts, from its data folder; the lab folder supplies data only and must be a drive-letter path on a fixed local volume; the PowerShell and Tailscale paths come from operating-system APIs, not from environment variables | P 11.5, P2-095; A 3.5, 3.8, 3.11, 5.3, 5.4, 6.1; W 2.2, 3.4, 4.2, 4.10, 4.14; T 4.6 |
| SEC-07 | "Reset pairing" has a race | Adopted: hash and revocation token are one object; `TryVerify` replaces `Verify` and `CurrentRevocation` | W 3.3, 4.4, 4.7; T 4.6 |
| SEC-08 | Shape checks written `^…$` accept a trailing line feed | Adopted: every shape is a whole-text match on both sides | P 1, 4.3, P2-202; A 3.5; W 3.1, 3.6, 4.14; I 3.1.2, 6.1; T 4.6 |
| SEC-09 | The lab's HTTP routes have no `Host` rule; the container has open outbound network | Adopted for the routes. The open network is accepted for 1.0 (8.4); dependencies are pinned by hash | P 11.1, 11.3, P2-162; A 5.1, 5.2, 6.1, 6.4, 6.5; T 4.6, 5.5 |
| SEC-10 | Conversation text reaches disk in places the guarantees do not cover | Adopted: the Codex working folder moves into the data folder, gets an unguessable name and is emptied at every start; the console host prints conversation text only with `--include-content`; the desktop window is excluded from screen capture by default; the "never written to disk" statements are reworded | P 12.2; A 3.5, 3.10, 3.14, 6.5; W 3.1, 3.3, 3.4, 3.9, 4.2, 4.9, 4.16, 5.8, 5.12; D 6.2; T 2.4, 3 (L-4), 4.5, 4.6 |
| SEC-11 | `--show-pairing` prints the permanent token | **Accepted with SEC-02** (8.4). Kept as a mitigation: on the default data folder the token is printed only to an interactive console | A 3.14; W 4.16; T 2.4, 4.5, 4.6 |
| SEC-12 | Release inputs are not pinned and a release can be replaced | Adopted: actions by commit id, locked NuGet restore, committed Swift resolution file, checked XcodeGen version, no cache in a release, no release under an existing tag, build provenance attestation, lab image by digest with hashed requirements | A 3.1, 4.1, 5.1, 7.1, 7.3, 7.6; W 2.1; D 5.4; T 5, 5.2, 5.3, 5.5 |
| SEC-13 | Pairing to a different host needs no confirmation | Adopted | P 4.4; A 4.9, 6.4; I 3.1.5, 3.2.8, 5.9, 6.2, 8.4; D 6.1 |
| SEC-14 | The owner cannot see that the public address is being probed | Adopted as three counters, shown on the desktop's Activity page as plain numbers. No alerting, no lockout, no event per request | P 5.2, 5.5, P2-019; A 3.4; W 3.1, 3.9, 4.1, 4.2, 4.4, 5.7; D 6.2; T 4.6 |
| SEC-15 | One framing test expects behaviour Kestrel may not have | Adopted: the property to protect has its own test; the framing case accepts either outcome | W 4.3 (K-9); T 4.2 (S-13), 4.6 |

Security tests: every `ST-nn` that belongs to an adopted finding is in T 4.6 with the work package that owns
the code. Not added: ST-01 and ST-04 (the parts of SEC-01 that were not adopted), ST-05 (SEC-02), and ST-22
to ST-25 and ST-29, which belong to no finding and wait for a ruling.

### 8.4 Accepted risks

These are known and are not fixed in 1.0. Each is also in architecture 6.5.

| Risk | Why it is accepted | What limits it |
|---|---|---|
| **The QR carries the permanent pairing token** (SEC-02). Whoever sees the screen for a moment while the QR is shown (a shared screen, a recording, a photo) holds a credential that never expires and, in Public mode, works from anywhere, until "Reset pairing" | The old app did the same; a single-use enrolment code would change the protocol, the pairing flow on both sides and the paste path, late in the design | The QR is hidden until it is asked for: it is drawn only after "Show QR", after a confirmed "Reset pairing", and once on the very first start when the connection is set up (AC-WIN-022); returning to the Pairing page never shows it (WIN-20). It hides by itself after 120 seconds, and the matrix is released from memory when it hides. The payload is never copied or logged: the desktop never holds its text (A-16), and no event, activity line or log carries it. "Reset pairing" is one click (and one confirmation) away on the same page and revokes the token at once, open streams included. Since SEC-10 the window is also excluded from screen capture by default. A camera pointed at the screen remains |
| `--show-pairing` prints the permanent token (SEC-11) | It is the only source for the phone's paste path (A-16), on an explicit request | On the default data folder the token is printed only to an interactive console, never into a redirected output |
| A Tailscale mapping keeps forwarding while LiveCue is closed, so another program on the PC can take the relay port and receive the phone's requests, token included (SEC-05) | Closing it means removing mappings on exit, which TS-9 forbids, or replacing the bearer token by a proof of possession | The port-in-use text names the danger and tells the owner to reset pairing; the listen socket is exclusive while the relay runs. On a single-user PC the other program would need the owner's own account |
| Codex is contained by its own switches, not by the operating system; it runs as the owner (SEC-01, what remains) | A restricted Windows identity would stop Codex from reading its own sign-in | Protocol 12.6; the record of W 4.9; the live containment check; the version warning |
| The lab container can reach the internet (SEC-09) | Cutting it off needs another way to publish its port | Images built by the owner from dependencies pinned by hash; telemetry switches set |
| Inside LiveContainer, other guest apps can read the fallback pairing file, the stored sessions and the preferences (SEC-03, SEC-10) | The Keychain can fail there; the review's full level would unpair the phone on a Keychain failure | A replaced file cannot redirect the app while the Keychain holds a pairing; replacing a PC by another host needs a confirmation |
| The executables and the IPA are unsigned (SEC-12) | A personal tool; the release notes say so | Checksums and a build provenance attestation |

### 8.5 Questions for the owner

Each is a risk decision that the documents cannot make. They are the security review's questions, in the
order the orchestrator set, with what the rulings above assume until the owner answers.

1. **The QR and the permanent token (SEC-02).** Do you accept that anyone who captures the QR once has
   access until you reset pairing, with the limits listed in 8.4? The alternative, not built in 1.0, is a
   QR that carries a single-use code valid for 120 seconds, which the phone exchanges for the token. Until
   you say otherwise the risk is accepted. One detail to decide with it: on the very first start the QR
   opens by itself once the connection is set up (AC-WIN-022); that is the one moment at which it appears
   without being asked for.
2. **Is Public mode needed for 1.0?** If running Tailscale on the iPhone is acceptable, leaving Public mode
   out removes the internet edge. If it is needed, the review asks that these hold before it is first
   switched on: the containment check passed for the installed Codex (L-4); question 1 answered; the
   exposure report and check 6a built (SEC-04); the tests of test plan section 4 pass on the published
   build, and ST-27 was run once from outside the tailnet.
3. **Codex (SEC-01).** Is stronger containment of the Codex process (a restricted Windows identity) wanted
   later, at the cost of a more fragile Codex integration? For 1.0 nothing is refused when Codex updates
   itself; the desktop only warns.
4. **Mappings while LiveCue is closed (SEC-04, SEC-05).** Today a mapping stays until you remove it, and
   TS-9 forbids any write on exit. Would you rather give one standing consent for "remove my mapping when
   LiveCue closes and put it back when it starts"? It would close SEC-05.
5. **LiveContainer (SEC-03, SEC-10).** Which other apps share the container with LiveCue? The minimum level
   is built (the Keychain wins; the file stays readable). Do you want the full level (no file while the
   Keychain works; a later Keychain failure means scanning the QR again), or the history encrypted with a
   key held in the Keychain?
6. **Screen capture (SEC-10).** The window is excluded from capture by default, as a setting you can switch
   off. While it is on, your own screenshots of the window are empty too. Is that the default you want?
7. **Is the PC used under any other Windows account?** If yes, SEC-05 and SEC-06 weigh more and should be
   looked at again before first use.
8. **Releases (SEC-12).** A published tag is now immutable: a corrected build always needs a new version
   number. Confirm.
9. **Lab container network (SEC-09).** Accept that the container can reach the internet, relying on pinned
   and hashed dependencies, or do you want it cut off?

