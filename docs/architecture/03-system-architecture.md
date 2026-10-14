# 03. System architecture

Status: binding for component boundaries, names, file formats and rules. Internal class design inside each
component is left to the two detailed designs (iPhone, Windows) that follow this document; section 8 lists
what they still decide and what they may not change.

Companion files: `01-decisions.md` (decisions and the deviation register) and `02-relay-protocol.md` (the
phone-to-relay contract, cited here as "protocol" with its section or constant id).

Contents

1. [System overview](#1-system-overview)
2. [Repository layout](#2-repository-layout)
3. [Windows solution](#3-windows-solution)
4. [iPhone app](#4-iphone-app)
5. [ASR lab](#5-asr-lab)
6. [Security model](#6-security-model)
7. [CI outline](#7-ci-outline)
8. [Hand-off to the detailed designs](#8-hand-off-to-the-detailed-designs)

---

## 1. System overview

### 1.1 Components

```
 iPhone (LiveCue Remake)                                   Windows PC (one user account)
 ┌──────────────────────────────┐                          ┌─────────────────────────────────────────────┐
 │ SwiftUI app                  │   HTTPS / WSS            │ Tailscale daemon (TLS ends here)            │
 │  session, audio, 5 speech    │ ───────────────────────► │   Private: tailnet HTTPS listener           │
 │  providers, 2 answer services│   protocol 2             │   Public:  Funnel path mount (prefix cut)   │
 │ LiveCueCore (Foundation only)│                          │            │ plain HTTP/1.1, loopback only  │
 └──────────────────────────────┘                          │            ▼                                │
        │            │                                     │ LiveCueRemake.exe (WPF, .NET 10)            │
        │            │                                     │  ┌───────────────────────────────────────┐  │
        │            └── HTTPS ──► OpenRouter (answers)    │  │ LiveCue.Relay (Kestrel, in-process)   │  │
        └── WSS ──► Meta realtime (cloud speech)           │  │  edge rules, routes, pairing, speech  │  │
                                                           │  └───────┬───────────────┬───────────────┘  │
                                                           │          │ child process │ loopback WS/HTTP │
                                                           │          ▼               ▼                  │
                                                           │   Codex CLI        ASR lab container        │
                                                           │   (own sign-in)    (Docker, GPU, 2 models)  │
                                                           └─────────────────────────────────────────────┘
```

### 1.2 Who talks to whom

| From | To | Channel | Contract |
|---|---|---|---|
| Phone | Relay | HTTPS and WSS through the Tailscale daemon | Protocol sections 3 to 10 |
| Phone | Meta realtime ASR | WSS, direct, key in the first message | Spec 02 section 5, unchanged |
| Phone | OpenRouter | HTTPS, direct | Spec 02 section 4, unchanged |
| Phone | On-device speech packages | In-process | FluidAudio 0.15.6, desert-ant-core 3.1.0 (Voz), exact pins |
| Desktop window | Relay | In-process interface (3.4). No network, no pipe. | This file |
| Relay | Codex CLI | Child process per request | Protocol section 12 |
| Relay | ASR lab | Loopback HTTP and WebSocket; two launcher scripts | Protocol section 11 |
| Desktop | Tailscale CLI | Child process, read-mostly | 3.9 |

The PC holds no cloud speech key and no model API key. The relay has no cloud speech path.

### 1.3 Identity

| Item | Value |
|---|---|
| iPhone bundle identifier | `com.mahmudkarim.LiveCueRemake` |
| Home Screen name / in-app brand | "LiveCue Remake" / "LiveCue" |
| Version / build | 1.0.0 / 1 (iPhone, relay and desktop share the version) |
| iPhone Keychain service | `com.mahmudkarim.LiveCueRemake` |
| Windows executable / window title | `LiveCueRemake.exe` / "LiveCue" |
| Windows data folder | `%LOCALAPPDATA%\LiveCueRemake\` |
| Relay loopback port (default) | 47841 |
| Lab host port (default) | 47842 |

Nothing of the old app is read or written: not its bundle, Keychain items, files, `%LOCALAPPDATA%\LiveCue\`,
container, images or port 47831.

---

## 2. Repository layout

```
ios/
  project.yml                     XcodeGen definition; the Xcode project is generated, never committed
  LiveCueCore/                    Swift package, Foundation only
    Package.swift
    Sources/LiveCueCore/          Domain, Coding, Context, Transcript, Usage, Relay, Meta, OpenRouter, Transport, WER
    Tests/LiveCueCoreTests/
  App/                            app target sources
    AppShell/  Features/  Session/  Speech/  Audio/  Answers/  Connectivity/  Persistence/
    DesignSystem/  Platform/  TestSeam/  Resources/
  UITests/                        XCUITest target
  Support/                        Simulator.entitlements, icon generator input
windows/
  LiveCueRemake.sln
  global.json                     SDK 10.0.401, rollForward latestFeature
  Directory.Build.props           version, nullable, warnings as errors
  Directory.Packages.props        central package versions
  src/
    LiveCue.Relay/                class library: protocol 2 server
    LiveCue.Hosting/              class library: settings, prerequisites, Tailscale, Codex discovery, supervisor
    LiveCue.Relay.Host/           console host (LiveCueRelayHost.exe)
    LiveCue.Desktop/              WPF app (LiveCueRemake.exe)
  tests/
    LiveCue.Relay.Tests/
    LiveCue.Hosting.Tests/
    LiveCue.Desktop.Tests/
  tools/
    LiveCue.IconTool/             draws the brand mark (design system 8.2) and writes the .ico and the iOS 1024 px PNG
asr/
  server.py  engines/  static/  benchmark.py  Dockerfile  requirements/
  Start-ASR-Lab.ps1  Stop-ASR-Lab.ps1  Build-ASR-Images.ps1  Get-ASR-Weights.ps1
  tests/test_protocol.py
  private/                        git-ignored: models/<model>/, sample.wav
scripts/
  audit_public_source.py          public-source audit (7.7)
  ci/select_simulator.py  ci/boot_simulator.py
  verify-pc-asr.ps1               manual GPU check (protocol P2-M01 to M03)
docs/
  spec/  architecture/  design/
.github/workflows/
  ci.yml  release.yml
```

Rules:

- `docs/orchestration/` and every `*.local.*` file stay git-ignored. `.gitignore` also ignores
  `pairing*.json`, `relay.json`, `asr/private/`, `*.wav`, `*.ipa`, `*.xcresult`, build output and every secret
  file type listed in spec 04 section 2.9.
- No file in the repository contains a real hostname, tailnet name, non-loopback address, user path, e-mail
  address, token or key. The audit (7.7) enforces this.

---

## 3. Windows solution

### 3.1 Toolchain and projects

.NET 10, SDK 10.0.401 pinned by `global.json`. Every project targets `net10.0-windows`, C# with nullable
enabled and warnings as errors. No Node.js anywhere. No PowerShell except the two lab launcher scripts.

| Project | Kind | References | Responsibility |
|---|---|---|---|
| `LiveCue.Relay` | Class library | `Microsoft.AspNetCore.App` (framework reference), `Net.Codecrete.QrCodeGenerator`, `System.Security.Cryptography.ProtectedData` | Everything in `02-relay-protocol.md`: Kestrel listener, edge rules, routes, pairing store, QR matrix, catalogue, answer runs, PC model control, speech bridge, self-check. No WPF, no settings file, no Tailscale knowledge. |
| `LiveCue.Hosting` | Class library | `LiveCue.Relay` | What a host needs around the relay: data folder, `settings.json`, prerequisite checks, Codex discovery, Tailscale status and mapping manager, endpoint resolution, relay supervisor. No WPF. |
| `LiveCue.Relay.Host` | Console executable `LiveCueRelayHost.exe` | `LiveCue.Hosting` | Thin headless host (3.14). |
| `LiveCue.Desktop` | WPF executable `LiveCueRemake.exe` | `LiveCue.Hosting` | Window, view models, Daylight theme, activity, pairing display, settings UI, self-test mode. |
| `LiveCue.Relay.Tests` | xUnit | `LiveCue.Relay` | Protocol section 15, side R. |
| `LiveCue.Hosting.Tests` | xUnit | `LiveCue.Hosting` | Settings validation, endpoint resolution, Codex discovery, mapping manager rules (3.9) with a fake CLI runner. |
| `LiveCue.Desktop.Tests` | xUnit | `LiveCue.Desktop` | View-model behaviour (3.12) and the self-test run (3.13). |
| `LiveCue.IconTool` | Developer tool (WPF, not published) | none | Draws the brand mark from the design system's numbers and writes the `.ico` frames and the iPhone 1024 px PNG. Run by hand when the mark changes. |

Dependency rules, enforced by a test that inspects assembly references: `LiveCue.Relay` references no other
project; `LiveCue.Hosting` references no WPF assembly; `LiveCue.Desktop` never uses `IPairingExport` or `StoredPairingExport` (3.4).

Allowed NuGet packages: the two named above, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
and `CommunityToolkit.Mvvm` in the desktop project. Any other package needs the orchestrator's approval,
because the owner allows package restores only, not new tooling. Restores are locked: every project commits
its `packages.lock.json` (`RestorePackagesWithLockFile` in `Directory.Build.props`), and CI restores with
`--locked-mode`, so a dependency of a dependency cannot change without a commit.

### 3.2 Relay library: internal boundaries

The public surface is small: `RelayFactory`, `IRelay`, `IPairingExport`, `PairingMaintenance`, `StoredPairingExport`,
`RelayOptions`, `RelayDependencies`, the event records of 3.4 and the protocol constants. Everything else is
internal.

| Area (namespace) | Owns | Protocol sections |
|---|---|---|
| `Protocol` | Constants C-xx, error codes, fixed texts, wire records | 2, 14 |
| `Edge` | The check pipeline E-1 to E-7, body reader, deadlines, client-abort handling, fixed-header error writer, presence | 5 |
| `Routes` | The ten routes and their check order | 6, 8.2 |
| `Pairing` | Token generation, hashing, `pairing.json`, DPAPI protector, payload text, QR matrix, self-check | 4 |
| `Catalog` | Codex `app-server` exchange, cache, retry, last-good, offer filter, selection | 7, 12.1 |
| `Answers` | The single slot, run registry by `requestId`, Codex `exec` runner, output validation, `execution` timings | 8, 12.2 |
| `LocalModel` | Controller state machine, lab probe, script runner | 9, 11 |
| `Speech` | Admission, lease, lab socket, frame filter, limiter, watchdogs | 10 |
| `Diagnostics` | Event channel, diagnostic sink | 3.4, 3.10 |

Seams. Each collaborator below is an internal interface with one production implementation and is
replaceable through `RelayDependencies` (used only by tests and by the self-test): the clock
(`TimeProvider`), the answer backend, the catalogue source, the lab runtime (probe, start, stop), the lab
socket connector, the pairing protector, the self-check HTTP client, the process runner. With these
replaced, the whole of protocol section 15 side R runs without Codex, Docker, a GPU or the network.

Kestrel is configured exactly as protocol 5.1. The relay is started with `StartAsync` and never with a
blocking run call, because the WPF dispatcher owns the main thread. A speech request keeps its request
pipeline running until its socket is closed.

### 3.3 Hosting library

| Part | Does |
|---|---|
| `DataFolder` | Resolves `%LOCALAPPDATA%\LiveCueRemake\`, creates it, never touches `%LOCALAPPDATA%\LiveCue\`. |
| `SettingsStore` | Loads, validates and atomically saves `settings.json` (3.5). |
| `Prerequisites` | The ordered checks of 3.11. |
| `CodexLocator` | 3.7. |
| `TailscaleCli` | Runs the Tailscale CLI with fixed argument lists and a time limit; returns exit code and standard output only for the two JSON status commands. |
| `MappingManager` | 3.9. |
| `EndpointResolver` | Builds the advertised endpoint from the mode, the ports, the prefix and this PC's Tailscale DNS name, in the canonical form of protocol 4.3. |
| `RelaySupervisor` | Composes `RelayOptions`, creates and starts an `IRelay`, forwards its events, stops it. One relay instance per start; a restarted relay is a new instance and is always accepting. |

Endpoint resolution:

| Mode | Advertised endpoint |
|---|---|
| Private | `https://<dns name>:<privateHttpsPort>/` |
| Public | `https://<dns name>:<publicHttpsPort><publicPathPrefix>/` |

`<dns name>` is `Self.DNSName` from `tailscale status --json` with the trailing dot removed, lower-cased. It
is read at each start and never stored.

### 3.4 In-process interface between host and relay

This replaces the old child process, its stdin commands and its stdout events. It carries the same
information.

```csharp
public sealed record RelayOptions(
    int Port,                      // loopback port; 0 picks a free port (tests only)
    string AdvertisedEndpoint,     // canonical, protocol 4.3
    ConnectionMode Mode,           // Private or Public (protocol E-1)
    string DataDirectory,          // pairing.json, schemas\, lab\ and work\ live here (3.5)
    string? CodexExecutablePath,   // absolute path of codex.exe, or null when none was found
    string? LabFolder,             // null = PC speech models not configured
    int LabPort,
    string RelayVersion,
    bool WarmCatalogOnStart = true);

public interface IRelay : IAsyncDisposable
{
    RelayStatus Status { get; }                     // Stopped, Starting, Running, Stopping, Faulted
    bool Accepting { get; }                         // false while paused
    int BoundPort { get; }
    ChannelReader<RelayEvent> Events { get; }

    Task StartAsync(CancellationToken ct);          // throws RelayStartException(FaultCode) on failure
    Task StopAsync(CancellationToken ct);           // graceful, bounded (3.11)
    Task SetAcceptingAsync(bool accepting);         // pause / resume
    Task<PairingQr> ShowPairingAsync(CancellationToken ct);    // same token; starts a self-check
    Task<PairingQr> ResetPairingAsync(CancellationToken ct);   // new token; revokes the old one
}

public sealed record PairingQr(string Endpoint, bool[][] Modules);   // square, true = dark, no quiet zone

public interface IPairingExport      // implemented by the relay object; read by the relay's own tests, by no host
{
    Task<string> GetPairingPayloadAsync(CancellationToken ct);  // the payload text of protocol 4.2
}

public static class PairingMaintenance  // usable while no relay is running (recovery from an unreadable file)
{
    public static Task ResetStoredPairingAsync(string dataDirectory, CancellationToken ct);
}

public static class StoredPairingExport // the stored pairing's payload, read without a relay: --show-pairing only
{
    public static Task<string?> ReadPayloadAsync(string dataDirectory, string advertisedEndpoint, CancellationToken ct);
}
```

Commands, mapped from the old control channel:

| Old command | New call | Notes |
|---|---|---|
| `{"action":"pause","paused":true/false}` | `SetAcceptingAsync(false/true)` | The window changes its label only when `AcceptingChanged` arrives. Calling it with the current value does nothing and raises no event. |
| `{"action":"pair"}` | `ShowPairingAsync` | Never rotates. |
| `{"action":"shutdown"}`, closing stdin | `StopAsync`, then dispose | |
| terminal `--reset-pairing` | `ResetPairingAsync` (running) or `PairingMaintenance` (not running) | Reached only through the desktop's confirmed, destructive action. |

Events. Every record derives from `RelayEvent` and has `DateTimeOffset At`.

| Event | Members | Raised when |
|---|---|---|
| `RelayStarted` | `Endpoint`, `Port`, `Mode`, `ProtocolVersion`, `RelayVersion`, `PairingWasCreated` | The listener is bound. |
| `RelayStopped` | `Reason` (`Requested`, `Fault`) | The listener is closed. |
| `RelayFaulted` | `Code` (`PORT_IN_USE`, `PAIRING_STORE_UNREADABLE`, `INVALID_ENDPOINT`, `START_FAILED`, `INTERNAL`), `Message` | Start failed, or a fatal runtime fault. |
| `AcceptingChanged` | `Accepting` | After a pause or resume took effect. |
| `PhoneSeen` | none | Protocol 5.5 presence. |
| `PairingShown` | `Endpoint` | `ShowPairingAsync` or `ResetPairingAsync` produced a QR. The matrix itself is only in the call's return value. |
| `SelfCheckCompleted` | `Result` (`Verified`, `VerifiedThroughFunnel`, `NotVerified`) | Protocol 4.5. |
| `Notice` | `Kind` (`PairingRestored`, `PairingReset`), `Message` | Informational. |
| `RequestStarted` | `RequestId`, `Kind` (`Assist`, `Summary`), `Model`, `ReasoningEffort`, `Transcript?`, `PartialTranscript?`, `Instruction?`, `PayloadJson` | An answer request passed validation (protocol R-7). |
| `RequestCompleted` | `RequestId`, `Kind`, `DurationMs` (= `relayTotalMs`), `CodexMs`, `Model`, `ReasoningEffort`, `PayloadJson` | An answer request succeeded. |
| `RequestFailed` | `RequestId`, `Kind`, `Code` (a wire code, or `CLIENT_DISCONNECTED`), `Message` | An answer request ended any other way after `RequestStarted`. |
| `AssistantNotice` | `Code`, `Message` | The catalogue could not be read, or a selection was not in it. |
| `LocalModelChanged` | `Status` (the five-member status object), `Message` | A model operation began or ended. |
| `SpeechStreamChanged` | `Phase` (`Loading`, `Ready`, `Stopped`, `Failed`), `Model`, `Code?`, `Message` | Stream progress. |
| `SpeechText` | `Text` | Each final transcript segment. |
| `EdgeStatistics` | `Malformed`, `TokenNotAccepted`, `FunnelInPrivateMode` (totals since this relay instance started) | The rejected-request counters of protocol 5.5: at most once per 60 s and only when a total changed; once more just before `RelayStopped` when a total changed since the last one. The desktop shows the three totals on the Activity page as plain numbers. It is not an activity line and raises no alert. |
| `Diagnostic` | `Source`, `Message` | 3.10. |

The old `speech-status` event carried three unrelated things; they are now `AssistantNotice`,
`LocalModelChanged` and `SpeechStreamChanged`. The old `ready` event's `model` and `cloudSpeechReady`
members are gone.

Delivery rules:

- Events go through one bounded channel (capacity 4,096, oldest dropped when full, drops counted). Raising an
  event never blocks a request. Order is preserved.
- Events are raised on thread-pool threads. The desktop marshals to the dispatcher. An exception while the
  desktop handles one event is caught, shown as a diagnostic and never stops later events.
- **No event contains the token or an `Authorization` value.** Conversation text does appear
  (`RequestStarted`, `RequestCompleted`, `SpeechText`) and must stay in memory.

Interface tests (they replace the old child-process tests):

| Id | Statement |
|---|---|
| I-01 | Starting on port 0 with an empty data folder raises `RelayStarted` with `PairingWasCreated` true and no `PairingShown`. `ShowPairingAsync` then returns a square matrix with at least 21 rows and raises `PairingShown`. |
| I-02 | `SetAcceptingAsync(false)` raises `AcceptingChanged(false)`; an assist is then `503 RELAY_PAUSED` and health is still 200. |
| I-03 | With a backend stub that never returns and one open speech stream, `StopAsync` completes within 2.5 s, the run was killed, the stream got `RELAY_STOPPING`, `RelayStopped(Requested)` was raised and the port can be bound again. |
| I-04 | Starting on a port that is in use throws `RelayStartException` with `PORT_IN_USE` and raises `RelayFaulted`; nothing is left listening. |
| I-05 | A new relay instance created after a stop is accepting, whatever the previous instance's pause state was. |
| I-06 | Starting with an existing pairing raises `Notice(PairingRestored)` and no `PairingShown`. |
| I-07 | Every event record serialises without a member named `token`, and the protocol's P2-145 holds. |

### 3.5 Data folder and file formats

```
%LOCALAPPDATA%\LiveCueRemake\
  settings.json        owned by LiveCue.Hosting
  pairing.json         owned by LiveCue.Relay
  schemas\             assist.schema.json, summary.schema.json, rewritten from embedded copies at each start
  lab\                 Start-ASR-Lab.ps1, Stop-ASR-Lab.ps1, LabCommon.ps1: the relay's own copies, rewritten
                       from embedded copies at each start; the only launcher scripts the relay ever runs (3.8)
  work\                one folder per answer request in flight (protocol 12.2); emptied at each start
```

Both JSON files are UTF-8, two-space indented, written atomically (write `<name>.pending`, then replace).
Nothing else is persisted by LiveCue: no request log, no transcript, no audio, no activity. The one place
where conversation text touches the disk is `work\<request>\output.json`, which Codex writes (the answer and
the rolling memory, in plain text) and which the relay deletes when the request ends; a folder left behind
by a crash or a kill is deleted at the next relay start. After a failed validation of `settings.json` a
copy `settings.json.invalid` may exist beside it (below).

**`settings.json`**

```json
{
  "schemaVersion": 1,
  "relayPort": 47841,
  "connectionMode": "private",
  "privateHttpsPort": 11443,
  "publicHttpsPort": 10000,
  "publicPathPrefix": "/livecue",
  "codexExecutablePath": null,
  "codexContainmentCheckedVersion": null,
  "asrLabFolder": null,
  "asrLabPort": 47842,
  "hideFromScreenCapture": true,
  "ownedMappings": []
}
```

| Key | Rule | Default |
|---|---|---|
| `relayPort` | Integer 1024 to 65535. **Never 47831.** Not equal to `asrLabPort`. | 47841 |
| `connectionMode` | `private` or `public`. | `private` |
| `privateHttpsPort` | Integer 1 to 65535, not equal to `publicHttpsPort`, and **never 443, 8443 or 10000**: a tailnet-only listener is kept off the ports on which Funnel can be switched on. | 11443 |
| `publicHttpsPort` | One of 443, 8443, 10000 (the ports Tailscale Funnel can use). | 10000 |
| `publicPathPrefix` | Matches `^/[a-z0-9][a-z0-9-]{0,31}$` as a whole text (protocol section 1, shapes: a value with a trailing line break is refused). Never `/`. | `/livecue` |
| `codexExecutablePath` | `null`, or the absolute path of an existing file named `codex.exe`. | `null` |
| `codexContainmentCheckedVersion` | `null`, or the Codex version text (3.7: 1 to 64 characters of letters, digits, space, `.`, `_`, `+`, `-`) for which the owner last confirmed the live containment check. Written only by "Mark as checked" in the Assistant settings. | `null` |
| `asrLabFolder` | `null`, or the absolute path of a folder that contains both launcher scripts. The path must start with a drive letter and lie on a fixed local volume: a UNC path (`\\host\share\…`), a device-namespace path (`\\?\…`, `\\.\…`), a relative or drive-relative path and a network or removable drive are refused when the settings are saved and again each time the folder is used. `null` means: use `asr\` beside the executable when it contains both scripts, otherwise PC speech is "not configured". The two script files only mark a folder as a lab folder; the relay never runs them (3.8). | `null` |
| `asrLabPort` | Integer 1024 to 65535, never 47831 or 8765, not equal to `relayPort`. No UI; file only. | 47842 |
| `hideFromScreenCapture` | Boolean. While `true` the desktop window is excluded from screen capture, screen sharing and recording (the operating system shows it as an empty area), so the QR, transcripts and answers cannot be captured by accident. | `true` |
| `ownedMappings` | The ledger of 3.9: entries `{"mode","httpsPort","path","target","createdAt"}`. | `[]` |

A missing file means defaults. A file that fails validation is left untouched; the desktop reports it and
runs on defaults until the user saves settings. 47831 is never offered, suggested or accepted anywhere.

One part of a failed file is not dropped: the ledger. Every `ownedMappings` entry of that file that is valid
by itself is kept and is part of the settings the application runs on, so that a mapping LiveCue created
stays known and removable. The first save writes those entries back, and before it replaces the file it
copies the failed file to `settings.json.invalid` (replacing an older copy), so that nothing the user wrote
is lost. A file that is not JSON at all has no entry to keep and is copied the same way.

**`pairing.json`**

```json
{
  "schemaVersion": 1,
  "tokenHash": "64 lower-case hex characters: SHA-256 of the UTF-8 token",
  "protectedToken": "base64 of the DPAPI-protected token",
  "createdAt": "2026-10-10T08:15:30Z"
}
```

Load rules: `tokenHash` must match `^[a-f0-9]{64}$` as a whole text; `protectedToken` must decrypt and hash to `tokenHash`.
Only "file does not exist" leads to creating a pairing. Every other load problem is the fault
`PAIRING_STORE_UNREADABLE`; the relay does not start and the file is not modified. There is no plaintext
member, no previous-token member and no legacy format.

### 3.6 DPAPI

The relay calls `ProtectedData.Protect` and `Unprotect` in-process with scope "current user" and the fixed
optional entropy `LiveCueRemake pairing v1` (UTF-8). No helper process. The pairing file therefore works only
for the Windows account that created it. Failures surface as `PAIRING_STORE_UNREADABLE` with a fixed text;
the exception text is not shown or logged.

### 3.7 Codex integration

Discovery (`CodexLocator`), first match wins:

1. `codexExecutablePath` from settings, when set. If the file is missing this is a prerequisite failure, not
   a fall-through.
2. The first file named exactly `codex.exe` found by walking `PATH`. Shims such as `codex.cmd` or `codex.ps1`
   are ignored: only a real executable is ever started, so no command interpreter parses arguments.
3. The most recently modified `codex.exe` under the per-user Codex install folder
   `%LOCALAPPDATA%\OpenAI\Codex\bin\`, searched at most three levels deep.

The resolved path is used both for answers and for the catalogue. Sign-in is checked by running
`<codex> login status` (limit 15 s); a non-zero exit is "not signed in". Its output is discarded.

**Version and the containment check.** Codex updates itself, so the tool set behind the relay can change
without any change in LiveCue. Two things make that visible; neither refuses anything.

- The version text of the Codex in use is read by running `<codex> --version` (limit 15 s, at most 256 bytes
  of output kept): its first line, when that is 1 to 64 characters of letters, digits, space, `.`, `_`, `+`
  and `-`; anything else is "unknown". It is the one piece of Codex output LiveCue ever shows (3.10). The
  desktop's Assistant settings show it beside the Codex path.
- `settings.json` remembers the version for which the owner last ran the live containment check
  (`codexContainmentCheckedVersion`; the check is test plan L-4). It is written only when the owner chooses
  "Mark as checked" in the Assistant settings. When the version in use is unknown or is not that version,
  prerequisite 2 still passes, but its row, the Assistant settings and one activity line per start carry the
  warning "Codex is version `<v>`; the containment check was last run for `<w>`." (or "… has not been run
  yet."), followed by "Run the containment check, then choose Mark as checked in Settings."

There is no hash of the executable and no refusal by mode: an answer is never refused because of the Codex
version.

Child process rules are in protocol section 12, with the allowlisted environment and the tool switches of
protocol 12.6. In addition, every Codex process is placed in a Windows job
object with kill-on-close, so no Codex process outlives the application. Lab launcher scripts are **not**
placed in that job, because they start Docker Desktop and a container that must outlive a relay restart.

### 3.8 Lab integration

The relay's lab runtime (protocol 9.2 and 11.5) is given the lab folder and the lab port in `RelayOptions`.
It never starts the lab on its own: a model is loaded only when a paired phone asks (route 5, or a speech
stream that needs it). Stopping the relay or closing the window does not stop the container. Building the
images and downloading weights are manual, documented steps (section 5) and are never triggered by the
application.

The launcher scripts the relay runs are its own: `Start-ASR-Lab.ps1`, `Stop-ASR-Lab.ps1` and the shared
`LabCommon.ps1` that both load from their own folder are embedded in `LiveCue.Relay` (the build embeds the
three files of `asr/`, so there is one source), written to `<data folder>\lab\`
at each start as the schema files are, checked against the embedded bytes again before each run, and
started with the PowerShell of the system directory that the operating-system API reports (protocol 11.5).
The configured lab folder is passed to the start script as `-LabFolder` and supplies data only: weights and
the Python service, mounted read-only into a container without capabilities. A script that someone edited
in the lab folder is therefore never run by the relay, whoever may write to that folder.

### 3.9 Tailscale mapping manager

The desktop manages how the relay is reached. The relay itself only ever listens on loopback.

| Mode | What exists in Tailscale | Who can reach the relay's edge |
|---|---|---|
| Private | A dedicated tailnet HTTPS listener on `privateHttpsPort`, root path, proxying to `http://127.0.0.1:<relayPort>` | Devices in the owner's tailnet |
| Public | A Funnel path mount `publicPathPrefix` on `publicHttpsPort`, proxying to the same target; Tailscale strips the prefix | Anyone on the internet who knows the address; only a holder of the pairing token gets past step E-3 |

The manager has exactly four write commands. Arguments are passed as a list, never through a shell.

| Action | Command |
|---|---|
| Apply Private | `tailscale serve --bg --https=<privateHttpsPort> http://127.0.0.1:<relayPort>` |
| Apply Public | `tailscale funnel --bg --https=<publicHttpsPort> --set-path=<publicPathPrefix> http://127.0.0.1:<relayPort>` |
| Remove own Private mapping | `tailscale serve --https=<privateHttpsPort> --set-path=/ off` |
| Remove own Public mapping | `tailscale funnel --https=<publicHttpsPort> --set-path=<publicPathPrefix> off` |

And two read commands: `tailscale status --json` and `tailscale serve status --json`.

The serve status JSON is read as: `TCP` (port to `{HTTPS, HTTP, TCPForward, …}`), `Web` (`"host:port"` to
`{Handlers: {mount: {Proxy | Path | Text | Redirect}}}`), `AllowFunnel` (`"host:port"` to boolean),
`Foreground` (session id to the same shape) and `Services`. A port is **occupied** when any of those
structures, including any foreground session and any service, mentions it. A handler is **ours** when its
port and mount are the configured ones and its `Proxy` is exactly `http://127.0.0.1:<relayPort>`.

**Exposure report.** Every read of the serve status also yields the list of **every** handler, on any port
and mount and in any of those structures, whose `Proxy` is exactly `http://127.0.0.1:<relayPort>`, each with
whether Funnel is on for its port and whether the ledger has an entry for it. This list, not the ledger, is
what the desktop's exposure warnings are made from: it is what Tailscale actually forwards to the relay
port, whoever created it. A handler that is in the ledger is offered "Remove…" as before (TS-8). A handler
that is not in the ledger is never removed by the application; the warning shows the exact removal command
as text for the owner to run in a terminal, and "Check again". When the serve status cannot be read the
report is "unknown", and the desktop says so instead of showing nothing.

Safety rules. Each is a test in `LiveCue.Hosting.Tests` against a fake CLI runner.

| Id | Rule |
|---|---|
| TS-1 | Every action begins with `tailscale serve status --json`. If it fails, times out or cannot be parsed into the shape above, nothing is written. |
| TS-2 | Only the four write commands above can be produced, with validated integers and a prefix that matches the settings rule. |
| TS-3 | Never produced, in any circumstance: `reset`; `off` without `--set-path`; `tailscale serve --bg <port>` without `--https=`; `--yes`; any `--tcp` or `--tls-terminated-tcp` form; any command for a port or path other than the configured one. |
| TS-4 | Apply Private is allowed only when `privateHttpsPort` is unoccupied, or occupied by our handler alone with Funnel off. (Running `serve` on a port that has Funnel on would switch Funnel off for that whole port.) It is never produced for port 443, 8443 or 10000 (3.5): a tailnet-only root handler on a port where Funnel can later be switched on by anything else would be published with it. |
| TS-5 | Apply Public is allowed only when the mounts `<prefix>` and `<prefix>/` are absent or ours, the port has no TCP forwarding, and either Funnel is already on for that port or the port is unoccupied. (Turning Funnel on for a port that has tailnet-only handlers would publish them.) |
| TS-6 | No write when `relayPort` is 47831 or the target is anything but `http://127.0.0.1:<relayPort>`. |
| TS-7 | After every write the status is read again. Every entry that is not ours must be identical to the snapshot taken before. Any difference is reported prominently and kept in the activity list; nothing is repaired automatically. |
| TS-8 | A mapping is removed only if it is in the `ownedMappings` ledger and still matches exactly. A handler that merely looks like ours but is not in the ledger is reported as "present" and is never removed. |
| TS-9 | **No write happens without an explicit user action** on a dialog that shows the exact command. Never at start, never on stop or exit, never as a side effect of changing the mode or a port. |
| TS-10 | Each CLI call is limited to 30 s. Output is discarded except for the two status commands. A write that times out is reported with a fixed text that tells the user to run the shown command in a terminal to see Tailscale's own instructions (first use of HTTPS or Funnel needs approval in the Tailscale admin console). |

What the desktop shows from the manager's read-only inspection at each start and on demand:

| State of the selected mode's mapping | Desktop behaviour |
|---|---|
| Present | Relay runs; "Show QR" is available. |
| Absent, can be added | Relay runs on loopback. The status says the connection is not set up and offers "Set up connection". "Show QR" is disabled until the mapping is present. |
| Blocked (TS-4 or TS-5) | Same, with the reason and a pointer to the port or prefix setting. |
| Our mapping for the *other* mode still exists | A standing warning with "Remove". In Private mode the relay additionally refuses requests that arrived through Funnel (protocol E-1), so a stale public mount cannot reach it. |
| The exposure report lists a handler that is neither the selected mode's mapping nor in the ledger (made by hand after a timed-out set-up, left by a process that died between the command and the ledger write, or made by other software) | A standing warning on the Live page and in Settings: what it is (port, path, whether Funnel publishes it), the exact removal command as text, and "Check again". LiveCue never runs that command. |
| Private mode is selected and the exposure report lists a handler with Funnel on | **The relay does not start** (3.11, check 6a). The status line says "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode." The warning above shows the handler and how to remove it. Rule E-1 stays as the second line of defence. |
| The serve status cannot be read | The relay starts; the Live page and Settings say that Tailscale's configuration could not be read and that exposure is unknown, with "Check again". |

Applying a mapping adds a ledger entry; removing one deletes it. Mappings are persistent Tailscale
configuration and stay after the relay stops, as in the old app. That has a cost the owner should know
(6.5): while LiveCue is closed, a mapping keeps forwarding to a loopback port that nothing of LiveCue is
listening on.

### 3.10 Logging and what is never logged

There are no log files. The relay writes nothing to disk except `pairing.json`, the two schema files and its
launcher-script copies under `lab\`; and, for the life of one answer request, Codex writes its output
file in that request's folder under `work\` (3.5).

| Sink | Content |
|---|---|
| Desktop activity list (memory, bounded) | Notices, faults, request and reply payloads, speech status and text, diagnostics prefixed `Relay diagnostic: `. Cleared by "Clear activity" and when the window closes. |
| Console host standard output | The same events as text or JSON lines (3.14). |

Never logged, shown or placed in an event, by any component:

- the pairing token, in whole or in part; the `Authorization` header; the pairing payload text; the DPAPI
  blob;
- the self-check nonce;
- output of Codex, PowerShell, Docker or Tailscale, and exception messages from them (a diagnostic carries
  the exception **type name** and a fixed description only). The one exception is the Codex version text of
  3.7, and only after it passed its fixed shape;
- forwarding headers and client addresses;
- audio, ever.

Conversation text (transcripts, answers) is shown in the desktop's memory only. LiveCue itself never writes
it to disk. It is written to disk only as Codex's output file, in the per-request working folder, for the
life of one request (protocol 12.2). The console host prints it only when `--include-content` is given
(3.14).

### 3.11 Start-up, prerequisites and shutdown

Start (automatic once the window has rendered, and on "Start relay"). All checks run off the UI thread; the
window stays responsive and shows "Checking Codex and Tailscale..." with Start disabled.

| Order | Check | Failure text (status line; Start is re-enabled) |
|---|---|---|
| 1 | Data folder usable; `settings.json` loads or defaults apply | "LiveCue could not open its data folder." |
| 2 | Codex CLI found (3.7) | "Codex CLI was not found. Install or update the Codex desktop app, then open it once and sign in." |
| 3 | Codex signed in | "Codex is installed but not signed in. Open Codex and sign in with ChatGPT." |
| 4 | Tailscale CLI found (`Tailscale\tailscale.exe` under the Program Files folder that the known-folder API reports, not the environment variable; else `PATH`) | "Tailscale is not installed in Program Files." |
| 5 | Tailscale running and online | "Tailscale is not online. Open Tailscale and connect first." |
| 6 | Tailscale reports this PC's DNS name | "Tailscale did not return this PC DNS name." |
| 6a | Private mode only: one read-only mapping inspection (3.9) before the relay starts. The exposure report holds no handler with Funnel on. A serve status that cannot be read does not fail this check | "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode." The relay is not started. (Console host: exit code 2.) |
| 7 | Relay starts (`StartAsync`) | `PORT_IN_USE`: "Port `<n>` is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay." `PORT_IN_USE` while the exposure report lists a mapping to that port: "Port `<n>` is in use by another program while a Tailscale mapping points at it. That program may have received your iPhone's requests, including the pairing token. Free the port, start the relay, then reset pairing." `PAIRING_STORE_UNREADABLE`: "The saved pairing could not be read. Reset pairing to create a new one." Other: "Could not start the relay." |
| 8 | Mapping inspection (3.9), read-only | Not a failure; shown as connection state. |

Stop ("Stop relay", closing the window): the relay stops accepting, cancels every answer run (process tree
killed), ends every speech stream with `RELAY_STOPPING`, closes the listener and aborts connections still
open after 2.5 s. Closing the window needs no confirmation and always stops the relay. Pause is not
persisted. Tailscale mappings and the lab container are left as they are.

If the relay faults while running, the window shows the fault text and returns to the stopped layout. It
does not restart the relay by itself. A second instance of the application fails at check 7 with the
port-in-use text and does not disturb the first.

### 3.12 Desktop application

The window follows the design system: one window, custom title bar, a sidebar and pages, Daylight theme,
light appearance forced, opaque ground, no Mica or Acrylic, default size 1240 x 660 and minimum 960 x 620.
There is no tray icon, no start at sign-in, no device list and no dark theme.

Pages and the functions each must provide (layout is the Windows design's job; the functions are fixed):

| Page | Functions carried over from the old window |
|---|---|
| Live | Relay status text; phone presence (dot, text, tooltip with the age of the last authenticated request); Start relay / Pause relay / Resume relay in one slot; "Incoming transcript" card with hint line; "Assistant reply" card with hint line and timing; the connection line that mirrors phone presence. |
| Pairing | QR tile with placeholder; "Show QR", "Hide QR"; PC address field (read-only, shows the advertised endpoint); pairing hints; 120 s auto-hide; **"Reset pairing"** (destructive style, confirmation dialog that says every paired iPhone stops working until it scans the new QR). |
| Activity | The bounded log; "Clear activity"; "Stop relay"; empty state; "Activity stays in memory on this PC". |
| Settings | Connection mode with the state of each mapping and its "Set up" / "Remove" actions (3.9); relay port, private HTTPS port, public port and prefix; Codex path (shows the discovered path when not set); speech lab folder; an About block with version and licences. |

State machines owned by the desktop view models (plain enums, no keyword matching on status text):

| Machine | States |
|---|---|
| Relay status | `Stopped`, `Checking`, `PrerequisiteFailed(text)`, `Starting`, `Ready`, `Paused`, `Thinking`, `RequestFailed`, `Faulted(text)`. A fault text stays until the next start. After a request completes or fails the status returns to `Ready` or `Paused` according to the pause flag. |
| Phone presence | `NotConnected` (no `PhoneSeen` since the relay started), `Connected` (last `PhoneSeen` at most 15 s ago), `Idle` (older). It returns to `NotConnected` only when the relay stops. |
| QR | `Empty`, `Shown(until)`, `Hidden(reason)`. Shown for 120 s. The token is the same every time; no state is called "expired" and no control says "new code". |

Event handling (texts are the old ones unless `01-decisions.md` section 6 lists a change):

| Event | Reaction |
|---|---|
| `RelayStarted` | Status `Ready`; PC address shown; activity "Relay started. Waiting for your iPhone."; in Public mode also "Public mode: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address." |
| A `PairingQr` returned by `ShowPairingAsync` or `ResetPairingAsync` | Open the pairing page, draw the QR with a four-module quiet zone, start the 120 s timer. On a first start (`RelayStarted.PairingWasCreated` true) the desktop calls `ShowPairingAsync` by itself as soon as the selected mode's mapping is present: at once if it already is, otherwise right after "Set up connection" succeeds. |
| `SelfCheckCompleted` | Exactly one activity line: "Secure PC connection verified.", "Secure Funnel connection verified. The iPhone VPN is not required." or "Pairing code created, but the advertised connection could not be verified. Check the PC tunnel before pairing." |
| `PhoneSeen` | Presence `Connected`. |
| `AcceptingChanged` | Status `Paused` / `Ready`, button "Resume relay" / "Pause relay". |
| `RequestStarted` | Status `Thinking`; transcript card = transcript, then "[In progress] " + partial, then "Instruction: " + instruction when present; hint "Received from your iPhone" or "Session summary requested"; reply card "Thinking..."; activity "INCOMING `<kind>` [`<id>`]" + payload. |
| `RequestCompleted` | Reply card = answer or summary, then details, "Key points", "Action items" when present; hint "`<model>` / `<effort>` / `<codex seconds>` s Codex" with the relay total as tooltip; activity "REPLY [`<id>`] `<seconds>` s" + payload. |
| `RequestFailed` | Status `RequestFailed`; reply card shows a fixed text for the code ("Assistant request failed or timed out. Check Codex sign-in and retry." for failures and timeouts; "Cancelled by the iPhone." for `REQUEST_CANCELLED` and `CLIENT_DISCONNECTED`); hint "Not sent - try Assist again". |
| `AssistantNotice`, `LocalModelChanged`, `SpeechStreamChanged` | Activity line; transcript hint shows the message. |
| `SpeechText` | Appended as a new line to the transcript card. |
| `RelayFaulted`, `RelayStopped` | Stopped layout: QR cleared, presence reset, pause flag reset, hints back to their initial texts. Transcript, reply and activity are kept until cleared. |

Limits kept from the old window: activity above 200,000 characters is cut to the newest 150,000 with the
first line "[Older activity removed from memory]"; the transcript card above 64,000 characters is cut to the
newest 48,000; Esc closes any open dialog. "Clear activity" empties activity, transcript and reply, shows
their placeholders again and sets the reply hint to "Activity cleared".

### 3.13 Self-test and screenshot mode

`LiveCueRemake.exe --self-test [--screenshots <folder>] [--scale 1|1.5]`

- Builds the real window with a scripted relay (an `IRelay` test double inside the desktop project) and an
  in-memory settings store. **No port is bound, no Tailscale or Codex command runs, nothing is read from or
  written to the data folder, no network is used.**
- Feeds synthetic events: started with an endpoint on a reserved test domain, a pairing matrix made from a
  synthetic payload, phone seen, an assist request, a reply of 850 ms, pause and resume, a failed request,
  speech status and text, a port-in-use fault.
- Asserts: reply and activity contain the answer; the transcript contains the question; the timing hint
  contains "0.85"; "Show QR" is enabled; the QR tile has drawn modules and a quiet zone; pause and resume
  flip the pause state and label; a fault returns the stopped layout and keeps its text; "Clear activity"
  empties the three areas; the activity cap holds; no visible text contains the synthetic token.
- With `--screenshots`, renders each scene off-screen to `<scene>.png` at 1240 x 660 and at 960 x 620:
  `live-stopped`, `live-ready`, `live-thinking`, `live-reply`, `live-request-failed`, `live-paused`,
  `pairing-empty`, `pairing-qr`, `pairing-reset-confirm`, `activity-empty`, `activity-filled`,
  `settings-private`, `settings-public`, `settings-blocked`, `fault-port-in-use`.
- Prints `Desktop self-test passed` and exits 0; on the first failed assertion prints the assertion name and
  exits 1. It runs in CI on every push (7.4) and its screenshots are uploaded.

### 3.14 Console host

`LiveCueRelayHost.exe` runs the same supervisor without a window. It is for headless use and for the manual
GPU check, not the everyday entry point.

| Argument | Effect |
|---|---|
| (none) | Run the prerequisite checks of 3.11, start the relay with the saved settings, print status lines, stop on Ctrl+C. Exit code 0 after a clean stop. |
| `--check-only` | Run the checks, print "LiveCue Relay prerequisites passed.", exit 0; or print the failure text and exit 2. |
| `--show-pairing` | Print the pairing payload text and a terminal QR once, then exit 0. It starts no relay and never binds the relay port, so it works while LiveCue Desktop is running: it reads the settings and the stored pairing (`pairing.json`) and resolves the advertised endpoint the way a start does (`--endpoint` when given, otherwise the mode, ports and prefix of the settings with this PC's Tailscale name). When no pairing has been created yet, the pairing cannot be read or the endpoint cannot be resolved, it prints the reason and exits 2. **This is the only place the token is ever printed, and only on this explicit request.** It is the source for the phone's paste-in pairing path. |
| `--events json` | Print each relay event as one JSON line instead of text. |
| `--endpoint <url>`, `--port <n>`, `--data-dir <path>`, `--codex <path>`, `--lab <folder>`, `--mode private|public` | Override settings for this run. With `--endpoint`, the Tailscale checks are skipped. |

There is no reset-pairing argument. Exit codes: 0 clean, 2 prerequisite failure, 3 relay start fault.

Two rules added by the security review:

- **`--show-pairing` prints the token only to an interactive console** when it works on the default data
  folder. If standard output is redirected there (to a file, a pipe, a wrapper that keeps output), the host
  prints one fixed line that contains nothing of the payload and exits 2. A run with an explicit
  `--data-dir` may be redirected: that is how the manual GPU check reads the throw-away pairing of its
  temporary folder.
- **Conversation text is not printed by default.** With `--events json`, the members `Transcript`,
  `PartialTranscript`, `Instruction` and `PayloadJson` of the request events and `Text` of `SpeechText` are
  left out of the lines unless the argument `--include-content` is also given. The text form never prints
  them.

The refusal of check 6a (3.11) is exit code 2, like a prerequisite failure.

### 3.15 Build and publish

- `dotnet test windows/LiveCueRemake.sln -c Release` runs every Windows test.
- Publish: both executables are published **self-contained** for `win-x64` into one folder so they share one
  copy of the runtime. No trimming (unsupported for WPF), no single-file bundle. The folder also receives
  `asr\` (without `private\`), `LICENSE` and `THIRD-PARTY-NOTICES.md`. The published artefact is
  `LiveCueRemake-win-x64.zip` plus a SHA-256 file.
- The application manifest declares per-monitor DPI awareness (PerMonitorV2).
- The icons (the Windows `.ico` and the iPhone 1024 px PNG) are drawn by `LiveCue.IconTool` from the design
  system's numbers, using the WPF renderer that is already part of the toolchain, and are committed. A normal
  build never rewrites them. The tool is not published.

---

## 4. iPhone app

### 4.1 Toolchain and project

| Item | Value |
|---|---|
| Xcode | 26.3, pinned on every macOS job with `DEVELOPER_DIR`; a job fails at once if that folder is missing. Not Xcode 27. |
| Project | XcodeGen 2.46.0 from `ios/project.yml`. The generated project is never committed. CI checks the installed XcodeGen version and fails when it is another one. |
| Deployment target, devices | iOS 18.0, iPhone only, portrait only |
| App target | `LiveCueRemake`. Swift 5 language mode with `SWIFT_STRICT_CONCURRENCY: complete`. `SWIFT_DEFAULT_ACTOR_ISOLATION` left unset; state owners are annotated `@MainActor` explicitly. |
| Core package | `ios/LiveCueCore`, tools version 6.0, Swift 6 language mode, platforms iOS 18 and macOS 15, no dependencies. Linked statically as a local package. |
| Speech packages | `FluidAudio` exactly 0.15.6 (product `FluidAudio`); `desert-ant-core` exactly 3.1.0 (product `Voz`). No other third-party code. Their own dependencies are pinned too: the Swift package resolution file is committed as `ios/Package.resolved`, copied into the generated project before packages are resolved, and every build runs with automatic resolution disabled, so a build fails when the file would have to change. |
| UI tests | Target `LiveCueRemakeUITests`. The one scheme runs the core package tests and the UI tests, with coverage. |
| Signing | Off by default (`CODE_SIGNING_ALLOWED`, `CODE_SIGNING_REQUIRED` = `"NO"`, empty identity, manual style). Simulator tests turn on ad hoc signing on the command line and use `Support/Simulator.entitlements`, scoped to `sdk=iphonesimulator*`, so Keychain calls work. Device builds carry no entitlements. |
| Shipped build | Release configuration, unsigned, arm64, packaged as `Payload/LiveCueRemake.app` in `LiveCueRemake.ipa`, for LiveContainer. |
| Must not rely on | Entitlements, Keychain access groups, app groups, custom URL schemes, universal links, extensions, widgets, Live Activities, push or local notifications. |

Info.plist keys (all through `info.properties` in `project.yml`):

| Key | Value |
|---|---|
| `CFBundleDisplayName` | LiveCue Remake |
| `CFBundleShortVersionString` / `CFBundleVersion` | 1.0.0 / 1 (from `MARKETING_VERSION` / `CURRENT_PROJECT_VERSION`) |
| `UIUserInterfaceStyle` | Light |
| `UILaunchScreen` | empty dictionary |
| `UIBackgroundModes` | `audio` |
| `UISupportedInterfaceOrientations` | portrait |
| `NSMicrophoneUsageDescription` | "LiveCue transcribes audio on this iPhone, sends it to your PC for local speech models, or streams it directly to Meta for cloud transcription, depending on your selected provider." |
| `NSCameraUsageDescription` | "LiveCue scans the pairing QR code displayed on your Windows PC." |
| `NSLocalNetworkUsageDescription` | "LiveCue connects privately to your Windows PC." |

`UIDesignRequiresCompatibility` is not set. There are no App Transport Security exceptions.

> **Amended by `04-ios-architecture.md`.** Row "UI tests": the one scheme runs three bundles. The hosted
> unit-test target `LiveCueRemakeTests` (sources `ios/AppTests`) sits between the core package tests and
> the UI tests (R-31).

### 4.2 Core package `LiveCueCore`

Rule: every source file imports `Foundation` and nothing else. No `URLSession`, no `FoundationNetworking`, no
Apple-only framework. The package builds and its tests pass with `swift test` on Linux (`swift:6.2-noble`)
and on macOS. Tests use XCTest.

| Folder | Public types | Source of the rules |
|---|---|---|
| `Domain` | `Session`, `TranscriptSegment`, `AssistantTurn`, `SessionMemory`, `SessionNotes`, `AssistRequest`, `SummaryRequest`, `AssistResponse`, `SummaryResponse`, `AssistantProvider`, `AssistantConfiguration`, `AssistantModelOption`, `RelayExecution`, `AssistantUsage`, `AssistPerformance`, `TranscriptionUsage`, `SpeechStreamUsage`, `TranscriptionModel`, `SpeechProvider`, `PCModelStatus`, `PairingPayload` | Spec 02 sections 1, 7, 8, 9, as changed by the register |
| `Coding` | `LiveCueJSON` (the one encoder and decoder configuration) | Dates as ISO 8601 UTC with exactly three fractional digits (`2026-10-01T14:03:22.123Z`); decoding accepts zero to nine. Absent optionals are omitted. UUIDs upper case. |
| `Context` | `ContextBuilder` (assist and summary requests), `WireBudget` | Spec 02 section 3; protocol 13.4 |
| `Transcript` | `StreamTranscript` | Spec 02 section 6, as changed by the register |
| `Relay` | `RelayConstants` (protocol C-xx), `RelayEndpoint` (grammar, canonical form, URL building), `RelayClient`, `RelayFailure` (wire codes and `L-` conditions), `RelayMessages` (protocol 14), `RelaySpeechFrame`, `RelaySpeechNegotiation` (protocol 13.7 steps 1 to 2 and 6 to 7 as a pure state machine) | Protocol sections 3, 4.3, 13, 14 |
| `Meta` | `MetaRealtimeAPI`, `MetaSpeechError` | Spec 02 section 5, unchanged contract |
| `OpenRouter` | `OpenRouterClient`, `OpenRouterError` (the stream parser stays internal) | Spec 02 section 4, unchanged contract |
| `Transport` | `HTTPTransport`, `HTTPRequest`, `HTTPResponseHead`, `WebSocketTransport`, `WebSocketConnection`, `WebSocketMessage`, `TransportFailure`, `WebSocketUpgradeRejected` | below |
| `WER` | `WordErrorRate` | Spec 02 section 3.4 |

Transport is behind protocols so that the core never touches the network API that Linux lacks:

```swift
public struct HTTPRequest: Sendable {
    public var method: String; public var url: URL; public var headers: [String: String]
    public var body: Data?; public var timeout: TimeInterval
}
public struct HTTPResponseHead: Sendable { public var status: Int; public var headers: [String: String] }

public protocol HTTPTransport: Sendable {
    func send(_ request: HTTPRequest) async throws -> (HTTPResponseHead, Data)
    func stream(_ request: HTTPRequest) async throws -> (HTTPResponseHead, AsyncThrowingStream<Data, Error>)
}

public enum TransportFailure: Error, Sendable { case cancelled, timedOut, tls, unreachable, other }
public struct WebSocketUpgradeRejected: Error, Sendable { public var status: Int }

public enum WebSocketMessage: Sendable { case text(String), data(Data), closed(code: Int, reason: String) }
public protocol WebSocketConnection: Sendable {
    func send(text: String) async throws
    func send(data: Data) async throws
    func receive() async throws -> WebSocketMessage
    func close(code: Int, reason: String) async
}
public protocol WebSocketTransport: Sendable {
    func open(url: URL, headers: [String: String], maximumMessageSize: Int) async throws -> any WebSocketConnection
}
```

Contract for every implementation: ephemeral session, no cookies, no cache, redirects never followed (a 3xx
is returned as a response); transport errors are thrown only as `TransportFailure`, and a refused upgrade as
`WebSocketUpgradeRejected`; task cancellation always surfaces as `TransportFailure.cancelled`. The Apple
implementations (`URLSessionHTTPTransport`, `URLSessionWebSocketTransport`) live in `ios/App/Platform/` and
are the only place that maps system error codes to `TransportFailure`. Core tests use fakes.

### 4.3 App target: modules and composition

| Folder | Role |
|---|---|
| `AppShell` | App entry, the root container that swaps between the tab bar and the live session screen, the single global "LiveCue" alert, composition of the environment. |
| `Features` | One folder per screen of spec 01 section 1: Home, LiveSession, AssistantLab, Settings, MetaKey, Pairing (with the QR sheet), ModelLibrary, Benchmark, History. Views and their presentation logic only. |
| `Session` | `SessionController`: the state machine (4.4), the assist pipeline (4.5), the 1 s tick, the end-of-session summary. |
| `Speech` | The `SpeechTranscriber` protocol, its implementations (4.7), `OnDeviceModelManager`. |
| `Audio` | `AudioCapture` (4.6) and the audio session policy. |
| `Answers` | The `AnswerService` protocol and its two implementations (4.8). |
| `Connectivity` | `RelayMonitor` (health and PC model polling, protocol 13.3), `PairingController`. |
| `Persistence` | `SessionStore`, `PreferencesStore`, `SecretStore`, `PairingStore` (4.10). |
| `DesignSystem` | `Theme`, tokens, shared components, and `LiquidGlass.swift` (4.12). |
| `Platform` | URLSession transports, QR scanner, permission requests, clipboard. |
| `TestSeam` | Launch-argument parsing and the fixture implementations (4.11). |

Composition rules:

- One `AppEnvironment` value is built once at launch, either `live` or `fixture(TestMode)`. It holds the
  protocol-typed dependencies. There are no singletons and no global state besides it.
- Features and `SessionController` depend on protocols only (`SpeechTranscriber` factory, `AnswerService`
  factory, `RelayGateway`, the four stores, a clock, permission providers). That is what lets the test seam
  replace everything that touches a microphone, a network or a real secret.
- State owners are `@MainActor` and observable: `AppModel` (navigation container, global alert),
  `SessionController`, `RelayMonitor`, `AssistantSettings`, `SpeechSettings`, `HistoryModel`.
- Every user-visible function, flow, string and accessibility identifier of spec 01 exists, except where
  `01-decisions.md` lists a change. The design system's sample labels do not override spec strings.

### 4.4 Session state machine

Explicit states replace the old four flags. `transitioning` means the state is `preflight`, `connecting`,
`pausing`, `resuming` or `ending`. `assisting` is an independent overlay.

| From | Trigger | To | Effects |
|---|---|---|---|
| `idle` | Start tapped | `preflight` | Run the start checks of spec 01 section 2.3, in order. |
| `preflight` | A check fails | `idle` | Global alert. Nothing was created. |
| `preflight` | All checks pass | `connecting` | Create the session **in memory** (title "New conversation"); reset feed, errors, retry state and elapsed time; attach a usage object; start the 1 s tick; show the live screen ("Updating…"); start the transcriber with a new generation id. |
| `connecting` | Transcriber reports started | `listening` | **First save.** The session exists in storage from here on. |
| `connecting` | Start fails or times out | `idle` | The session is discarded; nothing was saved. Tick stops. Alert "Transcription could not start. …". |
| `listening` | Pause tapped (not assisting) | `pausing` | Finalise the transcriber (spec 01 section 3.1). |
| `pausing` | Finalise returns | `paused` | A finalise error raises the alert "Could not finalize the transcript. Some final words may be missing." Capture usage; save. |
| `listening` | The transcriber reports a failure | `paused` | Tear down capture and socket; alert with the reason; the usage stream becomes interrupted; save. For an on-device provider the normal finalisation runs first, so recognised text is kept. A running assist continues. |
| `paused` | Resume tapped (not assisting) | `resuming` | Stream offset = current elapsed seconds; start the transcriber with a new generation id. |
| `resuming` | Started / failed | `listening` / `paused` | On failure: alert, stay paused. |
| `listening` | Stop tapped (not assisting) | `ending` | Finalise; errors alert and do not block. |
| `paused` | Stop tapped (not assisting) | `ending` | **No second finalisation.** |
| `ending` | Done | `idle` | Capture usage; stop audio fully; stop the tick; set the end time and the title (first 48 characters of the first segment, or "Conversation"); save; refresh History; return to the tabs. Then, in the background, the summary request when protocol 13.5 allows it. |

Rules around it:

- Every action is single-flight. Pause, Assist and Stop are disabled while transitioning or assisting.
- The tick fires once per second while a session exists, also while paused: it advances the elapsed clock
  (wall clock since the session was created), copies usage into the session and saves, **but only once the
  session has had its first save**.
- Generations: every transcriber start gets a fresh id. Events, audio and batch results carrying an older id
  are dropped. Assist results, streamed text and summary results for a session that is no longer the active
  or stored one are dropped silently.
- A failure of any provider leads to `paused`. The old app left on-device failures in "Listening".
- **Launch recovery**: at launch, before History loads, every stored session without an end time is closed:
  end time = the time of its last save, title by the rule above, every usage stream still marked streaming
  becomes interrupted.
- Nothing is user-cancellable (no cancel for Start, Assist or a summary), as in the old app.

> **Superseded in part by `04-ios-architecture.md`.** While the live screen is up, a problem is shown as a
> persistent notice on that screen, not as the global alert (R-03):
>
> - Row `listening` to `paused` ("alert with the reason"): a notice titled "Recording paused" (R-03).
> - Row `pausing` to `paused` ("A finalise error raises the alert"): a notice titled "Paused". On Stop the
>   same failure is the global alert, shown on Home after the return (R-27).
> - Row `resuming` ("On failure: alert, stay paused"): a notice titled "Recording paused" with
>   "Transcription could not start. " and the reason (R-28).
> - Row `preflight` to `connecting` (the label "Updating…"): the labels are "Connecting…", "Pausing…" and
>   "Stopping…" (R-04).
>
> A check that fails before a session exists, and a start that fails, still raise the global alert.

### 4.5 Assist pipeline

The steps of spec 01 section 3.4 hold, with these changes:

| Step | Change |
|---|---|
| Request building | `ContextBuilder` applies the byte budgets of protocol 13.4, for both answer services. |
| Model check, OpenRouter | When the catalogue is empty and fetching it fails, the assist fails with that failure's text, not with "choose a model". |
| Marker | Recorded for both services when a fresh request is built; applied to the saved memory after success (protocol 13.5). A retry does not move it. |
| OpenRouter context | The embedded context JSON omits `instruction`; the instruction is sent once, as the separate preference message. |
| Errors | Every relay failure is shown through protocol 14; no raw system text. |
| Timeout | When the phone's 70 s limit passes, the request is abandoned and one cancel is sent (protocol 13.5). |

### 4.6 Audio pipeline

One `AudioCapture` component serves all providers. Numbers are those of spec 01 section 3.2.

| Aspect | Rule |
|---|---|
| Audio session | Category play-and-record, mode measurement, options default-to-speaker and Bluetooth hands-free. Activated when capture starts. On Pause it is released for Meta and PC speech and kept active for on-device providers. Deactivated (notifying others) on full stop. |
| Tap | Hardware input in its native format, requested buffer 2,048 frames. No preferred sample rate. Zero sample rate or zero channels: "No usable microphone." |
| Conversion | Meta: PCM16 mono 24,000 Hz. PC speech: PCM16 mono 16,000 Hz. On-device: Float32 mono 16,000 Hz. |
| Framing | Streaming providers: 80 ms frames (3,840 bytes for Meta, 2,560 bytes for PC speech); the remainder is flushed as one shorter frame when capture stops. |
| Hand-off queue | 64 converted buffers for streaming providers; overflow is a stream failure ("Network cannot keep up. Recording paused; resume to retry."). 128 for on-device providers; overflow raises the alert "Transcription could not keep up. Stop and restart the session." and drops the buffer. |
| Level | RMS of each frame or buffer, published at most 24 times per second. |
| Interruptions and route loss | Observed for **every** provider. Interruption began: failure "Microphone interrupted. Resume when your call or other audio finishes." Previous input device unavailable: failure "Microphone route changed. Resume to use the current microphone." Nothing resumes by itself. |
| Storage | Audio is never written to disk. |
| Microphone timing | Streaming providers open the microphone only after the provider handshake (`ready` or the Meta acknowledgement). On-device providers open it at once. |

> **Superseded in part by `04-ios-architecture.md`.** Row "Hand-off queue", the clause "128 for on-device
> providers; overflow raises the alert … and drops the buffer": an on-device overflow is a failure like any
> other. The normal finalisation runs first, so recognised text is kept; the session then pauses with the
> notice "Transcription could not keep up. Recording paused; resume to continue." (R-07, confirmed by the
> orchestrator). The failures of the row "Interruptions and route loss" are shown as that notice too (R-03).

### 4.7 Speech providers behind one interface

Five providers, one protocol. `SessionController` knows only this:

```swift
@MainActor protocol SpeechTranscriber: AnyObject {
    var provider: SpeechProvider { get }
    var events: AsyncStream<TranscriberEvent> { get }
    func start(_ start: TranscriberStart) async throws     // returns once audio is flowing
    func finalize() async throws                            // the provider's Pause/Stop rule
    func transcribePending() async throws -> TimeInterval   // Voz: batch now and return the seconds spent; others return 0
    func shutdown() async                                    // release everything; idempotent
}

struct TranscriberStart { let generation: UUID; let streamOffsetSeconds: Double }

enum TranscriberEvent {                 // every event carries the generation it belongs to
    case status(String)                 // status line, see below
    case partial(String)
    case segment(TranscriptSegment)
    case level(Float)                   // 0...1
    case usage(UsageUpdate)             // begin, update(sentMs, processedMs, hasTranscript), finish(completed), firstText(ms), finalization(ms)
    case lastChunk(milliseconds: Double)
    case failed(TranscriberFailure)     // after a successful start; leads to `paused`
}
```

| Provider id | Implementation | Start | Finalise | Notes |
|---|---|---|---|---|
| `meta` | `MetaTranscriber` | Connect to the fixed Meta endpoint, send the handshake, require the acknowledgement, then open the microphone. 30 s limit. | Stop capture, flush, send `endStream`, wait up to 15 s for a normal close. | Contract of spec 02 section 5, unchanged. Uses `StreamTranscript`. |
| `nemotron`, `qwen3` | `RelayTranscriber(model:)` | Protocol 13.7 steps 1 to 3. | Protocol 13.7 steps 5 to 6. | Uses `StreamTranscript`. `loading` frames update the status line. |
| `voz` | `VozTranscriber` | Needs a prepared recogniser; microphone at once. | Stop capture, drain, transcribe everything pending. | Batch rules of spec 01 section 3.3 (30-minute cap, 2 s overlap, word-time cursor). Reaching the cap is a failure: the controller pauses, the pending audio is transcribed, and the alert says so. |
| `parakeet` | `ParakeetTranscriber` | Reset the recogniser, register the partial callback, microphone at once. | Stop capture, drain, take the final text, commit it as one segment. | FluidAudio streaming manager, 320 ms chunks, 640 ms debounce, CPU and Neural Engine. No end-of-utterance commit. |
| (fixtures) | `FixtureTranscriber` | 4.11 | | |

`OnDeviceModelManager` owns preparation, progress and unloading for Voz and Parakeet: one recogniser in
memory at a time; "Download & use" prepares; "Reload" on a loaded model releases it and prepares it again
from the cached files; "Unload" releases it; the loaded state is not restored at launch and is cleared when
the provider changes. Its progress block belongs to the model it was started for and is hidden for any
other. Model and cache folders are marked excluded from backup after preparation.

**Status line.** Each transcriber publishes a one-line status that the old app computed and never showed. It
appears on the live screen's status panel (accessibility identifier `transcriber-status`) and in Model
Library. Texts: "Connecting to `<provider name>`…"; "Loading `<provider name>` on PC · `<N>` s. Microphone
starts when ready."; "Meta direct · `0.00` s setup"; "Connected · `0.00` s setup"; "First words · `0.00` s
from stream start", later with " · Final `0.00` s" appended; "Disconnected · resume to retry"; "Last buffer
processing: `N` ms" (Parakeet); "`0.0` s audio → `0.00` s transcription" (Voz); and the preparation and
capture states listed in spec 01 section 8.2.

No provider ever falls back to another.

> **Completed by `04-ios-architecture.md`.** The printed `TranscriberEvent` says in a comment that every
> event carries its generation but has no member for it: `TranscriberEvent` is a struct of `generation`
> and `kind`, where `kind` has exactly the seven cases printed above, and `SpeechTranscriber` gains
> `emittedEventCount` (R-24). In the table, row `voz`, "the alert says so" is the session notice (R-03),
> with the text of R-06. In the status line, "Loading `<provider name>` on PC" uses the provider's short
> name (R-15).

### 4.8 Answer services behind one interface

```swift
protocol AnswerService: Sendable {
    var provider: AssistantProvider { get }
    var supportsSummary: Bool { get }                       // true for Codex only
    func loadCatalogue() async throws -> [AssistantModelOption]
    func answer(_ request: AssistRequest,
                onProgress: @escaping @Sendable (String) async -> Void) async throws -> AssistResponse
    func summarize(_ request: SummaryRequest) async throws -> SummaryResponse
    func cancel(requestId: UUID) async
}
```

| Provider id | Implementation | Catalogue | Answer | Summary | Cancel |
|---|---|---|---|---|---|
| `codex` | `CodexRelayAnswerService` over `RelayClient` | Route 3, fetched before every assist | Route 7, whole reply at once; echo check | Route 8 | Route 9 |
| `openrouter` | `OpenRouterAnswerService` over `OpenRouterClient` | Public model list; fetched when none is loaded | Streamed; `onProgress` receives the whole text so far | Never called | Cancels the task |

Keys: the OpenRouter key and the Meta key are read from `SecretStore` at the moment of use and passed to the
core client for that one call. The answer path never sees the pairing token of the other path.

### 4.9 Connectivity

`RelayMonitor` implements protocol 13.3 and the PC model status logic of spec 01 section 3.7 (ready for the
selected provider; can start a session; the Home label table). It exposes: paired or not, online or not, the
saved-PC state and message, and the PC model status. It makes no request at all when the PC is not needed.

`PairingController` implements the three entry paths of spec 01 section 2.2 on top of `PairingPayload`
(protocol 4.3): reading a payload only fills the two fields; "Verify and pair" calls route 2 and saves on
200. When a pairing is already saved and the new address names another host, "Verify and pair" first asks
"Replace `<current host>` with `<new host>`? Only do this with the QR shown by LiveCue Desktop on your own
PC.", with one more line when the new host is not a Tailscale name; nothing is sent until the user confirms
(`04-ios-architecture.md` 5.9). "PC connected" in the manual section is shown only when the relay is online and the address field
equals the saved endpoint.

> **Superseded by `04-ios-architecture.md`.** The last sentence is moot: nothing reads "PC connected"
> under the manual fields any more. The Saved PC panel carries the connection state (R-10).

### 4.10 Persistence, preferences, Keychain

No data of the old app is read. There is no migration code.

**Preferences (`UserDefaults.standard`)**

| Key | Type | Default |
|---|---|---|
| `assistantInstruction` | string | "" |
| `speechProvider` | string (`meta`, `nemotron`, `qwen3`, `voz`, `parakeet`) | `meta`, also for unknown values |
| `assistantConfiguration` | JSON `{model, reasoningEffort, provider}` | `{gpt-5.6-sol, low, codex}` |
| `assistantConfiguration.codex`, `assistantConfiguration.openrouter` | same JSON | per spec 02 section 2.2 |

The old `selectedModel` and `relayEndpoint` keys do not exist.

**Keychain** (generic password, service `com.mahmudkarim.LiveCueRemake`, accessible after first unlock, this
device only, **no access group**, no synchronisation)

| Account | Content | Test-mode account |
|---|---|---|
| `relayPairing` | Pairing JSON `{endpoint, token}`. The authority for the pairing (below) | `relayPairing-tests` |
| `meta-api-key` | Meta API key | `meta-api-key-ui-test` |
| `openrouter-api-key` | OpenRouter API key | `openrouter-api-key-ui-test` |

**Files**

| Location | Content | Protection |
|---|---|---|
| Application Support / `pairedPC.json` (`pairedPC-tests.json` in test mode) | Pairing JSON: a fallback copy, read only when the Keychain gives no pairing | Atomic write; complete until first user authentication; excluded from backup |
| Application Support / default SwiftData store | Sessions | Folder protected until first user authentication; in memory only in test mode |
| Library caches chosen by the speech packages | On-device model files | Managed by the packages; excluded from backup |

**The Keychain item is the authority for the pairing; the file is a fallback.** Other apps in the same
LiveContainer can read and replace the file, and a replaced file must never redirect the app to another
server.

- Load: the Keychain item first. When it holds a valid pairing, that is the pairing; if the file is missing
  or holds anything else, the file is rewritten from the Keychain item. The file is read only when the
  Keychain has no item or cannot be read (or holds text that is not a valid pairing). **The Keychain is
  never written from the file.**
- Save: the Keychain item first. When that write succeeds, the file is rewritten best effort. When it
  fails, the file must be written, and an older Keychain item is then deleted so that it cannot win at the
  next load; if an older item can be neither replaced nor deleted, the save fails and the file is put back
  to what the Keychain holds. After a save that succeeded, the next load returns that pairing.

The file exists because Keychain access inside LiveContainer can fail, and a phone that loses its pairing on
every Keychain hiccup would be worse (6.5). API keys are stored in the Keychain only; if the Keychain
refuses, the key is not saved anywhere.

**Session rows**: one row per session with a unique id, start time, last-saved time, title, and a JSON
payload of the whole `Session` encoded with `LiveCueJSON`. Rows that fail to decode are skipped. Save
cadence: the first save at `connecting` to `listening`, then when a segment is committed, after every
successful assist, on every tick, on Pause, Resume and Stop.

> **Superseded in part by `04-ios-architecture.md`.** Sessions are not kept in SwiftData. The row
> "Application Support / default SwiftData store" of the Files table and the paragraph "Session rows"
> give way to a file store: one JSON file per session, `<session id>.json`, in the folder `Sessions`
> under Application Support, encoded with `LiveCueJSON` together with its last-saved time, written
> atomically, in a folder protected until first user authentication; a file that does not decode is
> skipped (R-33, an orchestrator ruling). Sessions stay in memory only in test mode, and the save cadence
> is unchanged.

### 4.11 Test seam

The app switches to fixtures when launched with `-ui-testing`. The mechanism is the `fixture` environment of
4.3; the behaviours are those of spec 01 appendix A, unchanged:

- Sessions in memory only; test Keychain accounts and the test pairing file.
- Speech provider forced to Meta; answer configuration reset to the default; a placeholder Meta key present;
  the app reports itself paired and the relay online.
- No microphone prompt, no audio capture, no network request of any kind.
- Start commits the fixture segment "What is the main advantage of local transcription?".
- For streaming providers a fixture usage stream gains one second of audio per tick while not paused; Pause
  closes it, Resume opens a new one.
- Assist shows the partial text "Your audio stays on the iPhone," for 0.4 s, then returns the fixed answer
  with detected question, details, memory and execution timings; OpenRouter runs also carry fixture token
  counts and cost.
- Codex catalogue fixture: `gpt-6-astra`, `gpt-6-luna`, `gpt-5.6-sol`, `gpt-5.6-luna`, `gpt-5.3-codex-spark`;
  the two 5.6 models include `none`, the others offer low, medium, high.
- OpenRouter catalogue fixture: one model `test/direct` with effort `default`. Key verification succeeds
  without a request.
- PC model fixture: starts stopped; Start and Stop pass through a changing state for 1 s.
- On-device preparation fixture: 50% for 2 s, then ready.

| Argument | Effect |
|---|---|
| `-on-device-ready` | Provider Parakeet with its model already loaded. |
| `-fluid-ready` | Provider Fluid Speech Mini with its model already loaded (D-29). It comes first when both are given. |
| `-meta-no-key` | Remove the test Meta key at launch. |
| `-meta-key-persistence` | Leave the test Meta key as it was. |
| `-openrouter-ui-reset` | Remove the test OpenRouter key at launch. |
| `-openrouter-persistence-test` | Keep the saved answer configuration. |
| `-pc-offline` | Relay reported unreachable (`L-UNREACHABLE`). |
| `-pc-catalog-slow` | Codex catalogue takes 8 s. |
| `-pc-model-slow-start` | PC model start takes 6 s. |
| `-pc-model-start-error` | PC model start ends in error with a message mentioning Docker Desktop. |
| `-assist-fails-once` | The first fresh assist of a session shows a partial answer and then fails with the OpenRouter "could not complete" error; a retry succeeds. |
| `-pairing-persistence-test` | Use the real pairing storage logic with the test names. |
| `-save-pairing-fixture` | Save a synthetic pairing at launch: a reserved test domain, an explicit port, a trailing slash, and a token of the shape protocol C-02 requires. |
| `-drop-test-keychain` | Delete the test Keychain copy of the pairing, then reload from the file. |

Every accessibility identifier of spec 01 section 1.13 exists on the element it names. The old tests T1 to
T16 are ported; each takes screenshots as test attachments so CI can export them.

### 4.12 Liquid Glass wrapper and theme

- **One file, `ios/App/DesignSystem/LiquidGlass.swift`, contains every iOS 26 symbol** the app uses
  (`glassEffect`, `Glass`, `GlassEffectContainer`, `glassEffectID`, the glass button styles, `safeAreaBar`,
  `scrollEdgeEffectStyle`, `tabBarMinimizeBehavior`, `ToolbarSpacer`). Each use sits inside
  `if #available(iOS 26.0, *)` with the material-based fallback of the design system (section 3.3) in the
  other branch. No iOS 26 type appears in a signature. The patterns of `00-platform-research.md` section 1.6
  are used as written.
- The rest of the app calls only the wrapper (`daylightChrome(in:interactive:)` and its companions for the
  container, the button, the bottom bar and the matched id).
- CI fails if any of those symbols appears in another Swift file (7.3).
- Glass is used for the chrome layer only; content sits on the design system's own fills.
- One theme, Daylight. Light appearance is forced by the Info.plist key and by `preferredColorScheme(.light)`
  at the root. A `Theme` value in the environment holds every token; views contain no literal colour, size or
  radius.
- Every text style scales with Dynamic Type.

### 4.13 Attribution and disclosures

Settings gains an About block (it also holds the existing Appearance and Version rows):

| Row or text | Content |
|---|---|
| Appearance | "Daylight" |
| Version | The bundle's marketing version; "Unknown" if it cannot be read. |
| Speech packages | "On-device speech uses Voz by Desert Ant Labs (desert-ant-core 3.1.0, Desert Ant Labs Source-Available License 1.0) and FluidAudio 0.15.6 (Apache License 2.0)." |
| Usage reporting | "The Voz package reports anonymous usage to its vendor, Desert Ant Labs. LiveCue cannot turn this off." |

The same two statements appear in the repository README. The Model Library shows the usage-reporting
sentence under the Voz entry. Because of it, no text in the app may claim that nothing but the chosen
providers is contacted.

When microphone or camera access is denied, the alert names both places to change it: the iPhone's Settings
for a normal install, and LiveContainer's own entry there when the app runs inside LiveContainer. The README
tells LiveContainer users to enable "Spoof SDK Version" if bars and sheets do not look like iOS 26.

---

## 5. ASR lab

The lab is remade under `asr/`. It keeps native relay protocol 1 (protocol section 11), the same two fixed
models, the same container hardening and the same launcher behaviour. Its names and port are new so that it
can never collide with, or be mistaken for, an old lab on the same PC.

### 5.1 Container facts

| Item | Value |
|---|---|
| Container name | `livecue-remake-asr` (one container; one model loaded at a time) |
| Ownership label | `com.livecueremake.component=asr-lab` |
| Images | `livecue-remake-asr-nemotron:1.0`, `livecue-remake-asr-qwen3:1.0` (Dockerfile targets `nemotron` and `qwen3` over a shared `base` stage) |
| Base image | `python:3.12-slim`, referenced in the Dockerfile by digest (`python:3.12-slim@sha256:…`), not by the movable tag alone. Python requirements are installed with `pip install --require-hashes` from files that carry a hash for every package |
| GPU | `--gpus all`. The service refuses to load without CUDA. |
| Hardening | `--cap-drop ALL`, `--security-opt no-new-privileges`, `--pids-limit 512`, `--shm-size 1g` |
| Run-time environment | `VLLM_NO_USAGE_STATS=1`, `DO_NOT_TRACK=1`, `GRADIO_ANALYTICS_ENABLED=False`, `ASR_HOST_PORT=<lab port>` |
| Image environment | `PYTHONUNBUFFERED=1`, `PIP_DISABLE_PIP_VERSION_CHECK=1`, `HF_HUB_OFFLINE=1`, `HF_HUB_DISABLE_TELEMETRY=1`, `ASR_MODEL=<target>`; the `qwen3` image also `VLLM_WORKER_MULTIPROC_METHOD=spawn` |
| Port | `-p 127.0.0.1:<lab port>:8765`, default lab port 47842. Never published on another address. |
| Mounts, both read-only | `<lab folder>\private\models\<model>` to `/models`; `<lab folder>` to `/app` |
| Command | `python server.py` (uvicorn on 8765, access log off, WebSocket message limit 65,536 bytes) |

The image supplies the dependencies only; the Python code is the bind-mounted folder, so a code change needs
no image rebuild. The Dockerfile does not copy the service files. The service makes no network calls:
weights are downloaded beforehand and the telemetry switches above are set. Those switches are requests, not
a barrier: the container runs on Docker's default bridge network and could reach the internet. That is
accepted for 1.0 (6.5), because the images are built by the owner from dependencies pinned by hash.

Pinned dependencies are those of spec 03 section 9.3, minus the two that nothing uses (`python-multipart`
and the `curl` OS package). `httpx` and `pytest` are test-only requirements.

### 5.2 Service structure

| File | Role |
|---|---|
| `server.py` | FastAPI app: `/health`, `/`, `/sample`, WebSocket `/relay` and `/stream`, admission rules, the single-session lock, windowing, queues and timers. One check in front of every route, HTTP and WebSocket, applies the `Host` rule of protocol 11.1; the three HTTP routes also apply its `Origin` and `Sec-Fetch-Site` rules. Contains no model code. |
| `engines/nemotron.py`, `engines/qwen3.py` | The two real engines behind one small interface (load, warm up, recognise a window as a stream of partial and final texts). Settings are those of spec 03 section 5.4 (models table). |
| `engines/fake.py` | Test engine: a partial `test caption` for every audio chunk and a final `test caption` at end of input. Selected only by the tests; no image sets it. |
| `static/index.html` | The lab test page (spec 03 section 5.6), without the hard-coded GPU label. |
| `benchmark.py` | The manual benchmark tool, unchanged in behaviour. |

Start-up order is unchanged: load weights, warm up on 2 s of synthetic silence, then start the HTTP server.

### 5.3 Launcher scripts

`Start-ASR-Lab.ps1 [-LabFolder <folder>] [-Model nemotron|qwen3] [-Port <n>] [-NoBrowser] [-EnsureDocker]`
(defaults: the script's own folder, `nemotron`, 47842). `<lab folder>` below is `-LabFolder`: run by hand
from `asr\` it is that folder; run by the relay, whose copy of the script lives in the data folder
(3.8), it is the configured lab folder. The shared `LabCommon.ps1` is always loaded from the folder of the
script that runs, never from `-LabFolder`.

1. Require `docker.exe` on `PATH` ("Docker Desktop is required.").
2. Check the engine with `docker info`. If it is down: without `-EnsureDocker` fail ("Start Docker Desktop
   first, then run this launcher again."); with it, find the Docker Desktop executable relative to
   `docker.exe`, verify that its Authenticode signature is valid and issued to the Docker publisher, start it
   hidden and poll every 2 s for up to 2 minutes.
3. Require `<lab folder>\private\models\<model>\config.json` ("Model weights have not been downloaded.").
4. If a container with the fixed name exists: probe health; if busy, fail ("An ASR session is active. Stop it
   before switching models."); if already ready with the requested model and `relayProtocol` 1, finish;
   otherwise verify the ownership label (else fail, "Container name belongs to another workload; refusing to
   replace it."), stop it (15 s grace) and remove it.
5. `docker run -d` with the options of 5.1.
6. Poll every 2 s for up to 8 minutes: the container must still be running (else print its last 40 log lines
   and fail), and health must report ready with the requested model **and `relayProtocol` 1**.

`Stop-ASR-Lab.ps1 [-Port <n>]`:

1. Require `docker.exe`.
2. If the engine is not running, or the container does not exist, succeed without doing anything. It never
   starts Docker in order to stop a container.
3. Verify the ownership label (else fail, "Container is not owned by LiveCue. Refusing to stop it.").
4. If health reports busy, fail ("An ASR session is active. Stop it before unloading the model.").
5. `docker stop --timeout 15`. The container is stopped, not removed. Images and weights are untouched.

The relay always calls Start with `-LabFolder <lab folder> -NoBrowser -EnsureDocker` (protocol 11.5), and it
calls its own copies of the scripts, never the files in the lab folder.

### 5.4 Setup, done by hand

Documented in `asr/README.md` and never run by the application, by a test or by CI:

1. Install Docker Desktop with NVIDIA GPU support.
2. `Build-ASR-Images.ps1` builds the two images.
3. `Get-ASR-Weights.ps1 -Model <model>` downloads one model's weights into `private\models\<model>\`. The
   upstream repository of each model is recorded in `asr/README.md` by the lab builder; the spec does not
   name them (open item, see `01-decisions.md` section 7).
4. Optionally place a mono 16 kHz PCM16 WAV at `private\sample.wav` for the lab page.
5. Point LiveCue Desktop at the folder (Settings, speech lab folder) unless it is the `asr\` folder beside
   the executable. The folder must be on a fixed local drive (3.5). The README tells the owner to unpack the
   Windows zip under their own user profile, not in a folder that other Windows accounts can write to.

### 5.5 Tests

`asr/tests/test_protocol.py` runs the lab statements of protocol section 15.8 with the fake engine: no GPU,
no model, no Docker. It runs in CI on Linux.

---

## 6. Security model

### 6.1 Trust boundaries

| Boundary | Trusted side | Untrusted side | What enforces it |
|---|---|---|---|
| Internet or tailnet to the relay | The relay | Every caller until step E-3 passes | Protocol 5.2. The Tailscale daemon only carries bytes and ends TLS; the relay does not rely on it for any check. |
| Relay to Codex | The relay | Request content (transcripts, instructions) | Protocol 12.6: content is embedded as delimited untrusted JSON; the process gets an allowlisted environment; the shell tool and every other tool, feature and network switch the installed Codex offers are turned off; an empty per-request working folder; model and effort taken only from the relay's own catalogue. The read-only sandbox is kept but is not relied on for confidentiality. Capabilities the installed Codex cannot switch off are listed in `05-windows-architecture.md` 4.9. |
| Relay to lab | Both are local | Other local web pages | Loopback-only publish; `Host` rule on every lab route and `Origin` rules (protocol 11.1); fixed model names; launcher scripts that are the relay's own embedded copies. |
| Desktop to relay | Same process | n/a | No network control surface exists. |
| Phone to cloud providers | The phone | The provider's responses | Fixed hosts; keys only in the documented place; redirects refused; provider text never shown raw. |
| LiveContainer guests | n/a | Other guest apps can read and replace this app's files | Secrets go to the Keychain, and the Keychain item is the authority for the pairing: a replaced pairing file cannot redirect the app while the Keychain holds a pairing (4.10). The readable fallback file is the one deliberate exception (6.5). |

### 6.2 Secrets

| Secret | Where it lives | How it travels | Never |
|---|---|---|---|
| Pairing token | PC: SHA-256 hash and a DPAPI-protected copy in `pairing.json`, bound to the Windows account. Phone: Keychain item (the authority) and a fallback copy in the protected pairing file. | Inside the QR (screen to camera) or pasted text; then as the bearer header over TLS. The QR carries the permanent token: an accepted risk (6.5). | Written in plaintext on the PC; logged; put in an event; shown as text by the desktop; put in a URL. |
| Meta API key | Phone Keychain only | Inside the first WebSocket message to the fixed Meta endpoint | Sent to the PC, placed in a URL or header, written to a file. |
| OpenRouter API key | Phone Keychain only | Bearer header to the fixed OpenRouter host | Sent to the PC; sent to the public model-list call. |
| Codex sign-in | Codex's own store on the PC | Used by the Codex process itself | Read, copied or logged by LiveCue. |
| Tailscale identity | Tailscale | n/a | Touched by LiveCue. |

The PC holds **no** speech key and no model API key.

### 6.3 What each connection mode exposes

| | Private | Public |
|---|---|---|
| Tailscale object | Tailnet HTTPS listener on its own port | Funnel path mount on a Funnel port |
| Who can open a TCP connection to the edge | Devices in the owner's tailnet (subject to its access rules) | Anyone on the internet |
| What an unauthenticated caller can do | Receive the uniform 401 | Receive the uniform 401 |
| What a token holder can do | Everything in protocol section 6 | The same |
| What is never reachable | The lab port, the desktop's controls, the file system, any other relay path, any other local service | The same |
| Phone needs | The Tailscale VPN | Ordinary internet |
| Extra guard | The relay does not start while a Funnel handler targets its port (3.11, check 6a); requests marked by Tailscale as arriving through Funnel are refused (E-1); the private listener is never on a port that Funnel can use (3.5) | none |

### 6.4 Threats and answers

| Threat | Answer |
|---|---|
| Internet scanning and guessing | 256-bit random token; uniform 401 before any work; shape pre-check; no route disclosure; connection and request caps. |
| A web page in a browser on any device calling the relay | Any request with `Origin` or `Sec-Fetch-Site` is refused; no CORS headers exist; cookies are ignored. |
| Oversized or slow requests | Body cap enforced while streaming; header, body and route deadlines; minimum data rate; keep-alive limit; connection cap. |
| Stream abuse by a token holder | Rate limiter, frame size limit, idle and lifetime limits, one lease, socket ceiling. |
| Steering the PC through request content | Fixed allowlists for speech models; catalogue-only answer models; no phone-supplied path, URL, executable or script; exact route allowlist; request ids of one fixed shape. |
| Prompt injection in a transcript | The containment of protocol 12.6 (allowlisted environment, every available tool switch off, empty working folder), checked against the installed Codex by the opt-in canary run; schema-validated output rebuilt from an allowlist; answers drawn as plain text on the phone. The untrusted-data framing and the sandbox flag are kept but are not counted as controls. |
| Leaking internals to the phone | Fixed error texts; stubs in tests prove no private text escapes (P2-011). |
| A lost or shared phone | "Reset pairing" on the desktop revokes the token at once, including open streams. |
| Changing other software's Tailscale configuration | Rules TS-1 to TS-10. |
| Mode confusion (believing the relay is private while a public mount remains) | The exposure report is read from Tailscale's live configuration, not from the ledger, and is shown on the Live page (3.9); in Private mode the relay does not start while a Funnel handler targets its port; Private mode refuses Funnel-marked requests; the private listener cannot be put on a Funnel-capable port. |
| A web page rebinding its own host name to the lab's loopback port | The lab's `Host`, `Origin` and `Sec-Fetch-Site` rules apply to its HTTP routes as well as to its WebSockets (protocol 11.1). |
| A replaced pairing file or a hostile QR redirecting the phone to another server | The Keychain item is the authority for the pairing (4.10); replacing a saved PC by another host needs a confirmation that names both hosts (4.9). |
| Secrets in the public repository | The audit (7.7) on every push and over history. |

### 6.5 Accepted residual risks

- One shared token: every paired phone has the same rights, and revoking one revokes all.
- No lockout or delay on failed tokens; the token's size is the protection.
- A process running as the same Windows user can read the DPAPI copy and call the loopback relay.
- Inside LiveContainer, other guest apps can read the pairing file, and with it the token. It is kept
  because Keychain access there can fail, and a phone that loses its pairing on every Keychain hiccup would
  be worse. They cannot redirect the app by replacing the file while the Keychain holds a pairing (4.10);
  when the Keychain holds none, the file is the only copy and a replaced file would be believed.
- Inside LiveContainer, other guest apps can also read the stored sessions (the conversation history) and
  the preferences. Neither is encrypted.
- The desktop's activity list shows full transcripts and answers to anyone at the PC while the window is
  open. It is memory only. Screen capture is blocked by default (3.5, `hideFromScreenCapture`); a camera
  pointed at the screen is not.
- **The QR carries the permanent token** (`01-decisions.md`, review rulings, SEC-02): whoever photographs or
  otherwise captures the QR once holds the pairing until "Reset pairing". What limits it: the QR is shown
  only when asked for (and once on the very first start), hides after 120 s, is never copied or logged, is
  excluded from screen capture by default, and "Reset pairing" is one click away.
- **A mapping outlives the application.** While LiveCue is closed, its Tailscale mapping keeps forwarding
  to the relay's loopback port. Another program on the PC that binds that port in the meantime receives the
  phone's requests, bearer token included. On a single-user PC that needs the owner's own account, which
  could read the token anyway; on a PC with other Windows accounts it is a real exposure. It is made
  visible, not closed: the port-in-use text of 3.11 says what may have happened and tells the owner to reset
  pairing.
- **Codex is contained by switches, not by the operating system.** The agent runs as the owner. What it can
  reach is bounded by protocol 12.6 and by whatever the installed Codex version honours; capabilities that
  version cannot switch off are listed in `05-windows-architecture.md` 4.9, and a Codex update can change
  the list. The canary run and the version warning (3.7) are how a change is noticed.
- The lab container is not cut off from the internet (5.1). Its dependencies are pinned by hash.
- The Voz package contacts its vendor (4.13).

---

## 7. CI outline

### 7.1 Principles

Free GitHub-hosted standard runners in a public repository. No repository secrets. No job contacts a speech
or model provider; every test uses fixtures. Toolchains are pinned and printed. Default permission is
`contents: read`; only the release job may write.

Inputs are pinned too (`06-test-and-ci-plan.md` section 5): every action is referenced by its full commit
id; NuGet restores are locked (3.1); the Swift package resolution file is committed (4.1); the installed
XcodeGen version is checked; a release never restores a cache; a release can never be made again under a tag
that already exists; and the two release archives carry a build provenance attestation.

### 7.2 Workflows

| File | Triggers | Jobs |
|---|---|---|
| `ci.yml` | Push to `main` and to a work-package branch (`wp/**`), manual dispatch, and `workflow_call` (so the release reuses it). One run per ref; a newer run cancels the older. | `audit`, `core-linux`, `asr-protocol`, `windows`, `ios-tests` (two legs) |
| `release.yml` | Manual dispatch only, input `release_tag` (required) | `checks`, then `ci.yml` as a called workflow, then `ipa`, `windows-publish`, `release` |

### 7.3 Swift jobs

**`core-linux`** (`ubuntu-latest`, container `swift:6.2-noble`, 10 minutes):
`swift test --package-path ios/LiveCueCore`. Also runs the glass lint: a text search that fails if any of
`glassEffect`, `GlassEffectContainer`, `glassEffectID`, `.glass`, `safeAreaBar`, `scrollEdgeEffectStyle`,
`tabBarMinimizeBehavior` or `ToolbarSpacer` appears in a Swift file other than
`ios/App/DesignSystem/LiquidGlass.swift`, or if any file under `ios/LiveCueCore/Sources` imports something
other than `Foundation`.

**`ios-tests`** (needs `core-linux`; matrix, `fail-fast: false`; 40 minutes each; `DEVELOPER_DIR` set to
Xcode 26.3 on both):

| Leg | Runner | Simulator runtime | Covers |
|---|---|---|---|
| glass | `macos-26` | iOS 26.2 | Native Liquid Glass path |
| fallback | `macos-15` | iOS 18.6 | Material fallback path |

Steps of each leg:

1. Check out. Fail if the pinned Xcode folder is missing; print the Xcode version.
2. `brew install xcodegen`; fail unless `xcodegen --version` reports the version of 4.1; `xcodegen generate`
   in `ios/`; copy `ios/Package.resolved` into the generated project, then resolve packages with automatic
   resolution disabled.
3. `scripts/ci/select_simulator.py --runtime <version>`: only iPhone devices on exactly that runtime; device
   preference iPhone 17 Pro, iPhone 17, iPhone 16 Pro, iPhone 16, then any iPhone by name; if the runtime
   has no iPhone device, create one; fail with a clear message if the runtime is not installed.
4. `scripts/ci/boot_simulator.py <id>`: at most three attempts, 180 s each, shutting the device down between
   attempts.
5. `xcodebuild test` for the scheme on that simulator: parallel testing off, per-test time limits on
   (default 120 s, maximum 180 s), result bundle `artifacts/Tests-<leg>.xcresult`, ad hoc signing arguments,
   full log saved.
6. Always: save a final screenshot; export every test attachment from the result bundle to
   `artifacts/screenshots-<leg>/`.
7. Always: upload `artifacts/` as `ios-test-evidence-<leg>`, 14 days.

Both legs must pass. At most two macOS jobs run at once, inside the free plan's limit of five.

### 7.4 Windows job

**`windows`** (`windows-latest`, 30 minutes): print `dotnet --version` (must satisfy `global.json`);
`dotnet restore`; `dotnet build -c Release`; `dotnet test -c Release` (relay, hosting and desktop tests; the
DPAPI tests run here because they need Windows); publish as 3.15; run
`LiveCueRemake.exe --self-test --screenshots artifacts/desktop`; upload the test results and the screenshots
as `windows-evidence` (14 days) and the publish folder as `windows-build` (14 days). No Tailscale, Codex or
Docker is needed or present.

### 7.5 Lab job

**`asr-protocol`** (`ubuntu-latest`, Python 3.12, 10 minutes): install `asr/requirements/test.txt`; run
`pytest asr/tests`. No torch, no model, no GPU.

### 7.6 Release

1. **`checks`** (`ubuntu-latest`): the workflow runs from `main`; `release_tag` equals `v` + the iPhone
   marketing version in `ios/project.yml` and `v` + the version in `windows/Directory.Build.props`.
2. **`ci.yml`** in full. Nothing below starts unless every job in it succeeded in this same run. There is no
   way to reuse an earlier run.
3. **`ipa`** (`macos-26`, Xcode 26.3, 35 minutes): generate the project; build Release for the iPhone OS SDK,
   generic destination, signing disabled; then fail unless: the app bundle and its executable exist; the
   bundle identifier is `com.mahmudkarim.LiveCueRemake`; the version matches the tag; the executable is
   arm64. Package `Payload/LiveCueRemake.app` as `LiveCueRemake.ipa` with `ditto`, test the archive, write
   `LiveCueRemake.ipa.sha256`. Upload as an artefact (14 days).
4. **`windows-publish`** (`windows-latest`): publish as 3.15, zip, checksum, upload (14 days).
5. **`release`** (`ubuntu-latest`, `contents: write`, and for the attestation `id-token: write` and
   `attestations: write`): fail if the tag or a release for it already exists (a corrected build is a new
   version; nothing published is ever replaced); otherwise attest the two archives and create the release
   for the tag at the built commit. Title "LiveCue Remake `<tag>`". Notes state that the IPA
   is unsigned and for LiveContainer, that tests passed in this run, and the Voz attribution and usage
   statement. Then download every asset again into a clean folder and fail unless each checksum matches, the
   IPA passes an archive test and lists `Payload/LiveCueRemake.app/LiveCueRemake`, and the zip lists
   `LiveCueRemake.exe`.

### 7.7 Public-source audit

`scripts/audit_public_source.py`, Python standard library only, one command, exit code 0 or 1. It runs
locally before a push and in CI (`audit` job, `ubuntu-latest`, checkout with full history).

Scope: every tracked file and every untracked file that is not ignored; with `--history` (always on in CI)
also every blob reachable from any ref. Each file is read as bytes and searched as UTF-8 and as UTF-16.

| Rule | Fails on |
|---|---|
| AUD-1 Secret shapes | An OpenRouter-style key (`sk-or-v1-` and at least 20 key characters); a token starting `LLM_` with at least 20 key characters; a PEM private-key header; a GitHub token (`gh`, one of `p o u s r`, `_`, at least 30 letters or digits). |
| AUD-2 Pairing data | A 43-character base64url string next to `Bearer ` or next to a `"token"` member, unless it is a listed synthetic fixture; a `protectedToken` value longer than 64 characters. |
| AUD-3 Private host names | Any real host name ending in `.ts.net` (labels made of letters, digits and hyphens; a placeholder written in angle brackets does not count); a default Windows computer name (`DESKTOP-` or `LAPTOP-` followed by 7 or 8 letters or digits). |
| AUD-4 Addresses | An IPv4 literal outside `127.0.0.0/8`, `0.0.0.0` and the three documentation ranges; a Tailscale IPv6 prefix. |
| AUD-5 Personal paths | A Windows profile path (`<drive>:\Users\<name>` with a name other than `Public`, `Default` or a CI account), a macOS or Linux home path with a name other than the CI accounts, and an absolute path on a drive other than `C:`. |
| AUD-6 E-mail addresses | Any address whose domain is not a reserved example domain or a GitHub no-reply domain. |
| AUD-7 Forbidden file names | `relay.json`, `pairing*.json`, `auth.json`, `*.env`, `*.pem`, `*.key`, `*.p12`, `*.mobileprovision`, `*key*.xml`, `*credential*.xml`, `*.wav`, `*.m4a`, `*.caf`, `*.sqlite*`, `*.local.*`, anything under `docs/orchestration/`. |
| AUD-8 Exact local values | With `--local-values <file outside the repository>`: every line of that file, as UTF-8, as UTF-16 and Base64-encoded. Without the option the report says "exact-value check not performed" and does not claim it passed. |

Synthetic fixtures that must be allowed are listed one per line in `scripts/audit-allowlist.txt`, each with a
comment. The report names the rule, the path and the line number, **never the matched text**. A clean run
prints the number of files examined.

### 7.8 What CI does not prove

Real microphone behaviour, background audio, real provider access, LiveContainer Keychain behaviour, the two
on-device models on a device, the lab with a real GPU, and Tailscale mappings. These are manual checks
(`01-decisions.md` section 7).

---

## 8. Hand-off to the detailed designs

| | The iPhone design decides | The Windows design decides |
|---|---|---|
| Free | View hierarchy and screen layouts; component composition from the design system; where the transcriber status line sits; internal types of each module; how `AppEnvironment` is passed; the UI test list ported from T1 to T16 and the screenshots each takes. | Page layouts and XAML structure; view-model classes; the internal classes of the relay areas in 3.2; how Kestrel middleware is arranged to realise protocol 5.2; test class organisation; the icon tool. |
| Fixed | The protocol; the core package's public names in 4.2 and its Foundation-only rule; the two interfaces of 4.7 and 4.8; the state machine of 4.4; preference keys, Keychain accounts and file names of 4.10; the test seam of 4.11; the single glass file; every spec string and identifier not listed as changed. | The protocol; project boundaries and reference rules of 3.1; the interface and events of 3.4; both file formats of 3.5; discovery order of 3.7; rules TS-1 to TS-10 and the four commands of 3.9; the never-logged list of 3.10; start-up order and texts of 3.11; self-test flags, scenes and guarantees of 3.13; console host arguments of 3.14. |

A detailed design that finds one of the fixed items unworkable stops and reports it to the orchestrator; it
does not work around it locally, because the other side is built against the same text.

