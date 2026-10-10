# 05. Windows architecture

Status: binding for every builder of `windows/` and `scripts/`. It is the detailed design that
`03-system-architecture.md` section 8 hands off. Cited as "W-design".

Companion files: `01-decisions.md` ("decisions"), `02-relay-protocol.md` ("protocol"),
`03-system-architecture.md` ("architecture"), `../design/01-design-system.md` ("DS"),
`../design/03-windows-screens.md` ("screens"), `06-test-and-ci-plan.md` ("test plan").

Rules for reading:

- A number that the protocol defines is cited by its id (`C-43`) and never restated. A number that only the
  architecture defines is cited by its section. Numbers that this file adds are in the table of section 4.1 and
  have ids `W-nn`.
- A code block that starts with `// file:` is the exact content of a contract file (the `// file:` line names
  the file and is not part of it). Contract files are created
  by work package WP-W00 and are frozen afterwards: a later package that needs a change stops and asks the
  orchestrator.
- "The foundation" is WP-W00. Work packages of this file are numbered `WP-Wnn`, with a W, so that they
  cannot be mistaken for the iPhone design's `WP-nn`.

Contents

1. [Reconciliation](#1-reconciliation)
2. [File map](#2-file-map)
3. [Contracts](#3-contracts)
4. [Relay internals](#4-relay-internals)
5. [Desktop internals](#5-desktop-internals)
6. [Work packages](#6-work-packages)
7. [Changes after review](#7-changes-after-review)

---

## 1. Reconciliation

The screen design and its mockup were written while the decisions, the protocol and the architecture were
still moving. Rule applied to every row: **the screen documents govern layout, copy and navigation; the
decisions and the protocol govern behaviour and which features exist.** "O" marks a ruling the orchestrator
gave; "W" a ruling made here by the same rule. Each row names the files that were edited so that the documents
now agree.

| # | Topic | The screen design said | Decisions, protocol or architecture say | Ruling | By | Edited |
|---|---|---|---|---|---|---|
| R-01 | Copy pairing text | A "Copy pairing text" button and toast on the Pairing page | A-16: the desktop never holds the pairing text; the console host's `--show-pairing` is its only source | Not built. The button, its toast, its automation id and the "show the QR first" helper line are removed | O | screens 3.2, 6.3, 9, 10; mockup |
| R-02 | Excluded items | DS still describes a tray icon, a device row, and "Expired" / "New code" QR states | Decisions section 1 item 10 | None is built. The screens and the mockup contained none; nothing to edit | O | none |
| R-03 | Connection section | "Set up" described loosely; Public mode "not managed by LiveCue" | Architecture 3.9, TS-1 to TS-10 | The section follows the mapping manager exactly: the command shown is the one the manager built, and no mapping is written without a click on a dialog that shows it | O | screens 5.1, 6.2; mockup |
| R-04 | Window size | Default 1240 x 660, minimum 960 x 620 (also DS section 10 and old AC-WIN-002) | Decisions 5.3 and architecture 3.12, 3.13: 1120 x 760 and 900 x 600, citing DS section 10, which does not say that | Layout: 1240 x 660 default, 960 x 620 minimum. Self-test screenshots use those two sizes. Architecture 3.12 and 3.13 now give the same two sizes | O | decisions section 1 item 10, 5.3; architecture 3.12, 3.13 |
| R-05 | Second instance | The first window comes forward and shows "LiveCue is already running." | WIN-22, AC-SCR-013, architecture 3.11: no single-instance guard; the second window shows the port-in-use text | No guard, no toast. The second instance fails at relay start and says so | W | screens 1.2, 6.3 |
| R-06 | Start and Tailscale | Start "sets up the Tailscale mapping if the mode needs it" | D-20, TS-9: never at start | Start runs no Tailscale write. A missing mapping is the "connection not set up" state (R-14) | W | screens 2.1, 2.2, 2.6 |
| R-07 | Prerequisite check | Runs every check, shows all results; status "Cannot start yet. N items need attention."; rows for a Meta credential, the mapping, the relay port and "connection settings" | A-17, architecture 3.11, AC-WIN-003: fixed order, blocking, the first failing check's text is the relay status | Both hold. The six checks of architecture 3.11 are evaluated in that order; a check whose precondition failed is "Not checked"; the status line shows the first failure's architecture text; the list (layout) shows every row in that order with the screen design's title and fix. Rows removed: Meta credential (D-14), mapping (TS-9), relay port (a start fault, not a check). "Connection settings" becomes "Data folder and settings" (check 1). "Cannot start yet" stays as the sidebar's short label only | W | screens 2.3, 2.6; decisions A-17; mockup |
| R-08 | Relay status rows | "The relay stopped responding…"; "Relay stopped. Click Start relay to reconnect." for a relay that ended by itself | WIN-10, WIN-13: no pipe to lose; AC-WIN-014: a fault while running shows the fault and the stopped layout | The "stopped responding" row is removed. "Relay stopped. Click Start relay to reconnect." is the text of a runtime fault (`RelayFaulted` with `Internal`). Rows added for the unreadable pairing file and the data folder | W | screens 2.3 |
| R-09 | Reworded old strings | New punctuation and several reworded old strings | Decisions section 6: an old string changes only if listed there | Copy is the screen design's. Every reworded old string is now listed in decisions 6.2, so that rule keeps holding and architecture 3.12 ("the old texts unless decisions section 6 lists a change") stays true | W | decisions 6.2 |
| R-10 | Port-in-use text | "The relay port is already in use. Close the other LiveCue relay, then click Start relay." | Decisions 6.2 and architecture 3.11 (fixed): "Port `<n>` is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay." | The decisions text: it names the port and the setting, both of which the screen design predates | W | screens 2.3, 2.6 |
| R-11 | Public-mode and Meta activity lines | "Funnel enabled: …" and the two "Meta …" lines | Decisions 6.2: "Public mode: …"; D-14: no PC speech key | "Public mode: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address." The Meta lines do not exist | W | screens 4.1; mockup |
| R-12 | Failed request card | Body is "the relay's message" | Architecture 3.12, decisions 6.2: a fixed desktop text per outcome; "Cancelled by the iPhone." for `REQUEST_CANCELLED` and `CLIENT_DISCONNECTED` | Title "Nothing was sent to the iPhone" (screens); body is the desktop's fixed text for the outcome code | W | screens 2.8, 4.1 |
| R-13 | Reply hint without execution data | "Sent to iPhone · `0.00` s" | Protocol 8.3: `execution` is always present | Not built (dead branch, D-02) | W | screens 2.8 |
| R-14 | Connection not set up | No such state | Architecture 3.9, decisions 6.2: the status says so and offers "Set up connection"; "Show QR" is disabled until the mapping is present | A warning banner in the Live status strip with the decisions text and a "Set up connection…" button that opens the dialog of screens 6.2. On Pairing, "Show QR" is disabled with the caption "Set up the connection first." | W | screens 2.1, 2.9, 3.2, 3.3, 9 |
| R-15 | Button label | "Set up mapping…" | Decisions 6.2 and architecture 3.9, 3.12 name the control "Set up connection" | "Set up connection…" on Live and in Settings. The dialog title "Set up the Tailscale mapping?" stays | W | screens 5.1; mockup |
| R-16 | First start | The app switches to Pairing and shows the QR when the relay starts | AC-WIN-022 as changed: once the connection is set up | The QR opens by itself when the first start's mapping is present: at once if it already is, otherwise right after "Set up connection" succeeds | W | screens 3.3 |
| R-17 | Reset pairing while stopped | Disabled while the relay is not running | Protocol 4.4, architecture 3.4, 3.11: "Reset pairing" is the recovery from an unreadable pairing file, with no relay running | Enabled whenever no start or stop is in progress. With the relay stopped it calls `PairingMaintenance`; the new QR is shown after the next start | W | screens 3.4, 6.1 |
| R-18 | Phone status after reset | Returns to "iPhone not connected" | Architecture 3.12, WIN-15: presence returns to not connected only when the relay stops | Presence is not touched by a reset | W | screens 3.4 |
| R-19 | Clear activity and Stop | Cards are "emptied"; Stop keeps everything | WIN-14, architecture 3.12: placeholders return; Stop resets the hints and the timing text | "Clear activity" restores both placeholders; Stop resets the two hints and clears the timing caption | W | screens 2.2, 2.7, 2.8; decisions WIN-14 |
| R-20 | Settings, Connection | Private port fixed at 443, path `/`; Public "changes nothing in Tailscale"; no relay port | D-20, architecture 3.5, 3.9: relay port, private HTTPS port, public port and prefix are settings; both modes are managed; a stale mapping of the other mode is a standing warning with "Remove" | Rewritten to those settings, with a mapping status for the selected mode, the other mode's warning, the blocked reasons of TS-4 and TS-5, and an explicit "Save" | W | screens 5.1, 6.2, 9; mockup |
| R-21 | Settings, Assistant and speech | Read-only status rows; a "Docker Desktop" row; "Loaded model" | Architecture 3.12: Codex path and speech lab folder are settings. No component inspects Docker | Codex path and lab folder are editable fields (blank means automatic) with their status rows. The Docker row is not built. "Loaded model" stays and is driven by `LocalModelChanged` and `SpeechStreamChanged` events only | W | screens 5.2, 5.3, 9; mockup |
| R-22 | About | Version, "MIT", source link | Architecture 3.12: "version and licences" | A "Third-party notices" button is added; it opens `THIRD-PARTY-NOTICES.md` beside the executable | W | screens 5.4 |
| R-23 | Where "Stop relay" lives | Live header | Architecture 3.12 lists it under Activity | Layout: the Live header | W | none |
| R-24 | Connection line | Sidebar status card | Architecture 3.12: a "connection line" on Live | Layout: the sidebar card, visible on every page | W | none |
| R-25 | Relay total time | A visible caption under the reply | Architecture 3.12: a tooltip | Layout: the caption (`ReplyTiming`) | W | decisions WIN-14 wording |
| R-26 | Reply hint format | "Sent to iPhone · `<model>` · `<effort>` · `0.00` s Codex" | Architecture 3.12: "`<model>` / `<effort>` / `<s>` s Codex" | Copy: the screen design's; listed in decisions 6.2 | W | decisions 6.2 |
| R-27 | Pairing placeholder and reset wording | "Show the QR to pair an iPhone"; dialog "Reset pairing?" with its own body; button "Reset pairing…" | Decisions 6.2: "Show the QR to pair"; "Every paired iPhone stops working until it scans the new QR. Reset pairing?" | Copy: the screen design's refinements of new strings. The meaning required by architecture 3.12 is kept | W | decisions 6.2 |
| R-28 | Example addresses and the example command | `https://pc.example.test/`; `tailscale serve --bg 8787`; a lab folder on a second drive | D-20: default private port 11443; the bare `serve --bg <port>` form is forbidden (TS-3); the audit rejects paths on other drives (AUD-5) | Examples show `https://pc.example.test:11443/`, the command `tailscale serve --bg --https=11443 http://127.0.0.1:47841`, and a lab folder under `C:\` | W | mockup; screens 5.1 |
| R-29 | Sample speech lines in Activity | "Loading Nemotron 3.5 on PC…", "Nemotron 3.5 is ready." | The relay's fixed event texts (section 4.12) | The mockup shows the real texts | W | mockup |
| R-30 | Starting clears the log | "Starting the relay also clears the log" | Architecture 3.10 names only "Clear activity" and closing | Kept (spec 04 section 1.4 step 5, D-01): the log is cleared when a start has passed its checks and is about to start the relay. Transcript and reply are not | W | none |
| R-31 | Mode names | "Private tailnet", "Public Funnel" | "Private", "Public" | No conflict: the two control labels are the screen design's; setting values, events and the activity line use the decisions' names | W | none |
| R-32 | Assistant check on opening Settings | "The check runs when the view opens" | WIN-11: checks never run on the UI thread | The rows show the latest result; opening the view starts a check only when none exists yet in this run | W | screens 5.2 |
| R-33 | Startup message box reason | "followed by the reason" | Architecture 3.10: exception text from tools is never shown | The reason is the exception's type name | W | screens 6.4 |
| R-34 | Self-test scenes | Twelve mockup frames | Architecture 3.13: fifteen scene names (fixed) | The fifteen scenes are built; section 5.13 maps each to a mockup frame | W | none |
| R-35 | Opening the Pairing view | Shows the placeholder; the QR appears on "Show QR" | Old AC-WIN-019: a QR is displayed "without a further click"; WIN-20: one decryption and one self-check per explicit "Show QR" | Navigation requests nothing; "Show QR" does. AC-WIN-019 is recorded as changed | W | decisions 5.3 |
| R-36 | "The desktop command failed. Please retry." | Listed as a relay problem in Activity | RELAY-D7: there is no command channel | Kept as the activity line for an action of the window that threw (pause, show QR, reset, a mapping call) | W | screens 4.1 |

Two protocol sentences cannot be met to the letter with Kestrel and the framework's WebSocket. Both are
invisible to a conforming phone. The orchestrator accepted both, and protocol 5.3 and 10.5 now describe what
Kestrel does (section 4.3, notes K-1 and K-2).

---

## 2. File map

One line per file: path, responsibility, owner. Owners are the work packages of section 6. A file has exactly
one owner. "WP-W00, frozen" marks a contract file. "spike" marks a file that the foundation carries over from
the `spike/windows` branch.

### 2.1 Solution level

| Path | Responsibility | Owner |
|---|---|---|
| `windows/LiveCueRemake.sln` | Every project below | WP-W00 |
| `windows/global.json`, `windows/nuget.config`, `windows/.editorconfig` | SDK pin (architecture 3.1), one package source, code style (spike) | WP-W00 |
| `windows/Directory.Build.props` | Version `1.0.0`, target, nullable, warnings as errors, artefact output, test defaults (spike) | WP-W00 |
| `windows/Directory.Packages.props` | The six allowed packages, pinned (spike; tests use `xunit` 2.9.3) | WP-W00 |
| `packages.lock.json` beside every project file | The locked restore of architecture 3.1 (`RestorePackagesWithLockFile` is set in `Directory.Build.props`); committed, and changed only together with a package version | WP-W00 |
| `windows/THIRD-PARTY-NOTICES.md` | Licences of the runtime and the two shipped packages | WP-W00 |
| `windows/tests/Shared/DotnetHost.cs` | Finds the `dotnet` host that runs the tests (`DOTNET_HOST_PATH`, else `LIVECUE_DOTNET_EXE`, else `PATH`); linked into every test project | WP-W00, frozen |
| `windows/tests/Shared/ManualTimeProvider.cs` | A `TimeProvider` the test advances by hand, with timers | WP-W00, frozen |
| `windows/tests/Shared/TempFolder.cs` | A temporary data folder that is deleted on dispose | WP-W00, frozen |
| `windows/tests/Shared/LiveFactAttribute.cs` | `[LiveFact("LIVECUE_LIVE_CODEX")]`: skipped unless that variable is `1`. A live test also carries `[Trait("Category", "Live")]` | WP-W00, frozen |
| `windows/tests/LiveCue.TestChild/` (`LiveCue.TestChild.csproj`, `Program.cs`) | A console program for process-runner tests: echoes arguments and environment names, reads standard input, writes N bytes, sleeps, spawns a grandchild, plays a stub Codex `app-server` | WP-W00 creates; WP-W03 owns `Program.cs` |

### 2.2 `windows/src/LiveCue.Relay/`

| Path | Responsibility | Owner |
|---|---|---|
| `LiveCue.Relay.csproj` | Framework reference, two packages, embedded schemas, the three lab script files embedded as linked resources from `asr/` (4.10), `InternalsVisibleTo` the test project | WP-W00 |
| `Contracts/RelayOptions.cs` | `RelayOptions`, `ConnectionMode`, `RelayStatus` | WP-W00, frozen |
| `Contracts/IRelay.cs` | `IRelay`, `PairingQr`, `IPairingExport` | WP-W00, frozen |
| `Contracts/RelayEvents.cs` | Every event record and its enums | WP-W00, frozen |
| `Contracts/RelayFaults.cs` | `RelayFaultCode`, `RelayStartException` | WP-W00, frozen |
| `Contracts/RelayDependencies.cs` | The seam holder | WP-W00, frozen |
| `Contracts/RelayFactory.cs` | `RelayFactory`, `PairingMaintenance`, `StoredPairingExport` (one-line forwards) | WP-W00, frozen |
| `Protocol/ProtocolConstants.cs` | C-01 to C-73, the route deadlines of protocol 6.1, the `W-nn` numbers | WP-W00, frozen |
| `Protocol/RelayCodes.cs` | Every wire code as a string constant, plus `CLIENT_DISCONNECTED` | WP-W00, frozen |
| `Protocol/RelayErrors.cs` | `RelayError` and the table code to status to text of protocol 14.1, with pre-built bodies | WP-W00, frozen |
| `Protocol/FixedTexts.cs` | MSG-1 to MSG-9, notice texts, desktop speech texts, model labels (section 4.12) | WP-W00, frozen |
| `Protocol/WireJson.cs` | Compact UTF-8 JSON writing with the relaxed encoder; depth-limited parsing | WP-W00, frozen |
| `Processes/IProcessRunner.cs` | `IProcessRunner`, `IProcessSession`, `ProcessRequest`, `ProcessResult` | WP-W00, frozen |
| `Processes/ProcessRunner.cs` | Production runner: no shell, hidden, argument list, time limit, output cap, tree kill | WP-W03 |
| `Processes/JobObject.cs` | The kill-on-close job object | WP-W03 |
| `Processes/ChildEnvironment.cs` | The three environment policies | WP-W03 |
| `Core/RelayContext.cs` | What every area receives at construction | WP-W00, frozen |
| `Core/IRelayEventSink.cs`, `Core/IAccessState.cs`, `Core/RelayException.cs` | Internal contracts shared by all areas | WP-W00, frozen |
| `Core/SpeechLease.cs` | The one speech lease (C-34); complete in the foundation | WP-W00, frozen |
| `Core/RelayServices.cs`, `Core/RelayComposition.cs` | The area instances and the one place that constructs them | WP-W00, frozen |
| `Core/RelayHost.cs` | `IRelay` and `IPairingExport`: start, stop, pause, faults | WP-W01 |
| `Core/AccessState.cs` | Pause flag and stopping token | WP-W01 |
| `Diagnostics/EventChannel.cs` | The bounded event channel and the diagnostic sink | WP-W01 |
| `Edge/RouteTable.cs` | The ten rows of protocol 6.1 with their deadlines and flags | WP-W00, frozen |
| `Edge/RouteContext.cs` | What a route handler sees | WP-W00, frozen |
| `Edge/KestrelSetup.cs` | Listener and limits of protocol 5.1 | WP-W01 |
| `Edge/ConnectionGate.cs` | C-30 over every connection | WP-W01 |
| `Edge/EdgePipeline.cs` | Steps E-1 to E-8, deadlines, client abort, the last-resort 500 | WP-W01 |
| `Edge/BearerCheck.cs` | E-1 and E-2 as pure functions | WP-W01 |
| `Edge/RequestGauge.cs` | C-31 | WP-W01 |
| `Edge/BodyReader.cs` | Protocol 5.4 | WP-W01 |
| `Edge/ResponseWriter.cs` | Protocol 5.3 | WP-W01 |
| `Routes/IRouteHandler.cs` | The handler contract | WP-W00, frozen |
| `Routes/HealthRoute.cs`, `Routes/PairVerifyRoute.cs` | Routes 1 and 2 | WP-W01 |
| `Routes/ModelsRoute.cs`, `Routes/AssistRoute.cs`, `Routes/SummaryRoute.cs`, `Routes/CancelRoute.cs` | Routes 3, 7, 8, 9 | WP-W03 |
| `Routes/LocalModelStatusRoute.cs`, `Routes/LocalModelStartRoute.cs`, `Routes/LocalModelStopRoute.cs` | Routes 4, 5, 6 | WP-W04 |
| `Routes/SpeechRoute.cs` | Route 10: C-32, the upgrade, hand-over to the bridge | WP-W05 |
| `Pairing/IPairingService.cs`, `Pairing/IPairingProtector.cs`, `Pairing/IPairingFiles.cs`, `Pairing/ISelfCheckClient.cs` | Pairing contracts and seams | WP-W00, frozen |
| `Pairing/AdvertisedEndpoint.cs` | The grammar of protocol 4.3 and the canonical form (public, static) | WP-W02 |
| `Pairing/PairingToken.cs` | Generation, hashing, constant-time comparison (spike) | WP-W02 |
| `Pairing/PairingFile.cs`, `Pairing/PairingStore.cs` | The file format of architecture 3.5, load rules, atomic replace | WP-W02 |
| `Pairing/PairingFiles.cs`, `Pairing/DpapiPairingProtector.cs` | Production seams | WP-W02 |
| `Pairing/PairingPayload.cs`, `Pairing/QrMatrix.cs` | The payload text of protocol 4.2 and its module matrix | WP-W02 |
| `Pairing/SelfCheck.cs`, `Pairing/SelfCheckClient.cs` | Protocol 4.5 | WP-W02 |
| `Pairing/PairingService.cs` | `IPairingService`: load or create, verify, show, reset, export, revocation | WP-W02 |
| `Pairing/StoredPairingReset.cs` | What `PairingMaintenance` forwards to | WP-W02 |
| `Pairing/StoredPairingPayload.cs` | What `StoredPairingExport` forwards to: the stored pairing's payload text, read without a relay and without writing | WP-W02 |
| `Catalog/ICatalogSource.cs`, `Catalog/ICatalogReader.cs` | Catalogue contracts | WP-W00, frozen |
| `Catalog/CodexCatalogSource.cs` | Protocol 12.1 | WP-W03 |
| `Catalog/CachedCatalogSource.cs`, `Catalog/CatalogReader.cs`, `Catalog/OfferFilter.cs`, `Catalog/AssistantSelection.cs` | Protocol 7.1, 7.2, 7.3 | WP-W03 |
| `Answers/IAnswerBackend.cs`, `Answers/IAnswerCoordinator.cs` | Answer contracts | WP-W00, frozen |
| `Answers/AnswerCoordinator.cs`, `Answers/AnswerSlot.cs`, `Answers/RunRegistry.cs` | Protocol 8.2, C-33, cancellation by id | WP-W03 |
| `Answers/CodexAnswerBackend.cs`, `Answers/CodexArguments.cs`, `Answers/Prompts.cs` | Protocol 12.2, 12.3 | WP-W03 |
| `Answers/AnswerValidator.cs`, `Answers/ExecutionTimings.cs` | Protocol 12.5 validation and rebuild, protocol 8.3 timings | WP-W03 |
| `Answers/SchemaFiles.cs`, `Answers/Schemas/assist.schema.json`, `Answers/Schemas/summary.schema.json` | The embedded schemas and their rewrite into the data folder at start | WP-W03 |
| `LocalModel/ILabRuntime.cs`, `LocalModel/ILocalModelController.cs` | PC model contracts | WP-W00, frozen |
| `LocalModel/LocalModelController.cs` | Protocol 9.3 | WP-W04 |
| `LocalModel/LabRuntime.cs`, `LocalModel/LabHealthParser.cs`, `LocalModel/LabScripts.cs` | Probe, the two script command lines of protocol 11.5, the relay's own script copies under `lab\` | WP-W04 (the foundation adds `LabScripts.WriteCopies` as a working method, because `RelayHost` calls it at start) |
| `Speech/ISpeechBridge.cs`, `Speech/ILabSocketConnector.cs` | Speech contracts | WP-W00, frozen |
| `Speech/SpeechBridge.cs`, `Speech/SpeechStream.cs`, `Speech/SpeechAdmission.cs` | Protocol 10.2, 10.4 | WP-W05 |
| `Speech/PhoneSocket.cs`, `Speech/SpeechFrames.cs` | Receive with the C-20 bound, the relay's own frames, the close handshake | WP-W05 |
| `Speech/LabSocketConnector.cs`, `Speech/LabEventFilter.cs` | The lab socket of protocol 11.3 and the allowlist of protocol 10.3 | WP-W05 |
| `Speech/AudioLimiter.cs`, `Speech/StreamWatchdogs.cs` | Protocol 10.5 | WP-W05 |

### 2.3 `windows/src/LiveCue.Hosting/`

| Path | Responsibility | Owner |
|---|---|---|
| `LiveCue.Hosting.csproj` | References `LiveCue.Relay`; `InternalsVisibleTo` its tests | WP-W00 |
| `Contracts/RelaySettings.cs` | `RelaySettings`, `OwnedMapping`, defaults | WP-W00, frozen |
| `Contracts/SettingsValidation.cs` | `SettingsField`, `SettingsProblemCode`, `SettingsProblem`, `ISettingsValidator` | WP-W00, frozen |
| `Contracts/ISettingsStore.cs` | `ISettingsStore`, `SettingsLoadResult` | WP-W00, frozen |
| `Contracts/IHostFileSystem.cs` | File-system seam of the hosting library | WP-W00, frozen |
| `Contracts/Prerequisites.cs` | `IPrerequisiteChecker`, the report types | WP-W00, frozen |
| `Contracts/Codex.cs` | `ICodexLocator`, `ICodexSignInCheck` | WP-W00, frozen |
| `Contracts/Tailscale.cs` | `ITailscaleCli`, node status, `ServeSnapshot`, and `MappingCommand` complete with its four factories | WP-W00, frozen |
| `Contracts/Mapping.cs` | `IMappingManager`, states, refusals, plans, results | WP-W00, frozen |
| `Contracts/IEndpointResolver.cs` | Endpoint resolution | WP-W00, frozen |
| `Contracts/IRelaySupervisor.cs` | The supervisor and its events; `IRelayFactory` | WP-W00, frozen |
| `Contracts/HostingFactory.cs` | `HostingServices` and `HostingFactory.Create` | WP-W00, frozen |
| `DataFolder.cs`, `HostFileSystem.cs` | The data folder of architecture 3.5; production file system | WP-W06 |
| `SettingsJson.cs`, `SettingsValidator.cs`, `SettingsStore.cs` | Format, rules and atomic save of `settings.json` | WP-W06 |
| `LabFolderResolver.cs` | `asrLabFolder`, or `asr\` beside the executable | WP-W06 |
| `CodexLocator.cs`, `CodexSignInCheck.cs` | Architecture 3.7: discovery, the sign-in check and the version read | WP-W06 |
| `PrerequisiteChecker.cs`, `PrerequisiteTexts.cs` | Architecture 3.11 checks 1 to 6 and their fixed texts | WP-W06 |
| `EndpointResolver.cs` | Architecture 3.3 | WP-W06 |
| `RelaySupervisor.cs`, `ProductionRelayFactory.cs` | Start sequence, event forwarding, stop | WP-W06 |
| `Tailscale/TailscaleLocator.cs`, `Tailscale/TailscaleCli.cs` | The CLI with fixed argument lists and the limit of TS-10 | WP-W07 |
| `Tailscale/TailscaleStatusParser.cs`, `Tailscale/ServeStatusParser.cs` | The two JSON readers | WP-W07 |
| `Tailscale/MappingRules.cs` | Occupied, ours, TS-4, TS-5, TS-6, TS-8 as pure functions | WP-W07 |
| `Tailscale/SnapshotDiff.cs` | TS-7 | WP-W07 |
| `Tailscale/MappingManager.cs`, `Tailscale/MappingTexts.cs` | `IMappingManager`; effect and refusal texts | WP-W07 |

### 2.4 `windows/src/LiveCue.Relay.Host/`

| Path | Responsibility | Owner |
|---|---|---|
| `LiveCue.Relay.Host.csproj` | `LiveCueRelayHost.exe`; references `LiveCue.Hosting` | WP-W00 |
| `Program.cs`, `HostProgram.cs`, `HostArguments.cs` | `Main` forwards to `HostProgram.RunAsync(args, services, output, cancel)`, which tests call with fake services (`services` is a function from the parsed `HostArguments` to `HostingServices`); arguments and exit codes of architecture 3.14 | WP-W06 |
| `ConsoleEventPrinter.cs` | Events as text lines or JSON lines | WP-W06 |
| `TerminalQr.cs` | The terminal QR of `--show-pairing` | WP-W06 |

### 2.5 `windows/src/LiveCue.Desktop/`

| Path | Responsibility | Owner |
|---|---|---|
| `LiveCue.Desktop.csproj`, `app.manifest` | `LiveCueRemake.exe`, PerMonitorV2 | WP-W00 |
| `Assets/LiveCue.ico` | The committed icon | WP-W00 commits a placeholder; WP-W12 the final frames |
| `Contracts/Indicators.cs` | `StatusMark`, `StatusTone`, `StatusIndicator`, `BannerModel` | WP-W00, frozen |
| `Contracts/IDesktopSession.cs` | The desktop's one model of the relay; enums of the three state machines | WP-W00, frozen |
| `Contracts/Services.cs` | `IUiDispatcher`, `IClipboard`, `IExternalLauncher`, `IDialogHost`, `IDialogViewModel`, `IToastHost`, `INavigator` | WP-W00, frozen |
| `Contracts/ShellViewModel.cs`, `Contracts/LiveViewModel.cs`, `Contracts/PairingViewModel.cs`, `Contracts/ActivityViewModel.cs`, `Contracts/SettingsViewModel.cs`, `Contracts/Dialogs.cs` | View-model interfaces: every observable property and command | WP-W00, frozen |
| `Contracts/AutomationIds.cs` | Every automation id of screens section 9 as a constant | WP-W00, frozen |
| `App.xaml`, `App.xaml.cs` | Resource merge order, start-up, exit (spike) | WP-W08 |
| `Composition/AppEnvironment.cs`, `Composition/AppServices.cs` | Command line; the composition root | WP-W08 |
| `Infrastructure/WpfUiDispatcher.cs`, `Infrastructure/WpfClipboard.cs`, `Infrastructure/ShellLauncher.cs`, `Infrastructure/SafeInvoke.cs` | Production services; exception isolation per event | WP-W08 |
| `Interop/NativeMethods.cs` | DWM attributes, console attach (spike); `SetWindowDisplayAffinity` and `GetWindowDisplayAffinity` (5.12) | WP-W08 |
| `Theming/ThemeManager.cs`, `Theming/ShadowColorExtension.cs` | The theme slot (spike) | WP-W08 |
| `Theming/TextScale.cs`, `Theming/SystemPreferences.cs` | Windows text scale; transparency and animation settings | WP-W08 |
| `Themes/Daylight.xaml`, `Themes/HighContrast.xaml` | Colour tokens (spike) | WP-W08 |
| `Themes/Metrics.xaml`, `Themes/Typography.xaml`, `Themes/Icons.xaml`, `Themes/Interaction.xaml` | Tokens that do not change with the theme (spike) | WP-W08 |
| `Themes/Glass.xaml`, `Themes/Buttons.xaml`, `Themes/Navigation.xaml`, `Themes/Status.xaml`, `Themes/Rows.xaml`, `Themes/Chrome.xaml`, `Themes/Supporting.xaml` | Component styles (spike) | WP-W08 |
| `Themes/Fields.xaml`, `Themes/Dialogs.xaml` | Text field, segmented control, dialog, banner, toast (keys listed in 5.3) | WP-W08 (the foundation adds every key with a plain style) |
| `Controls/BloomLayer.cs`, `Controls/BrandMark.cs`, `Controls/Icon.cs`, `Controls/ListRow.cs`, `Controls/StatusChip.cs` (with the mark control `StatusMarkView`, renamed from the spike's `StatusMark` because the contract's enum has that name), `Controls/Pill.cs`, `Controls/SplitRowPanel.cs`, `Controls/Glass.cs`, `Controls/ButtonFills.cs`, `Controls/ButtonAssist.cs`, `Controls/ChromeState.cs`, `Controls/Converters.cs` | Shared controls (spike) | WP-W08 |
| `Controls/StatusIndicatorView.cs`, `Controls/SegmentedControl.cs`, `Controls/Banner.cs`, `Controls/CheckRow.cs`, `Controls/Spinner.cs`, `Controls/TwoColumnPanel.cs`, `Controls/PageLayout.cs` | Shared controls the spike lacks | WP-W08 (the foundation adds a minimal class for each) |
| `Shell/MainWindow.xaml`, `Shell/MainWindow.xaml.cs` | Chrome, sidebar, page host, dialog and toast layers | WP-W08 |
| `Shell/ShellViewModel.cs`, `Shell/Navigator.cs`, `Shell/DialogHost.cs`, `Shell/ToastHost.cs`, `Shell/PageTemplates.xaml` | Navigation, dialogs, toasts, page-to-view mapping | WP-W08 |
| `Session/DesktopSession.cs`, `Session/RelayStatusMachine.cs`, `Session/PresenceTracker.cs`, `Session/StatusTexts.cs` | `IDesktopSession`: events to state | WP-W08 |
| `Text/ShellText.cs` | Strings of the shell, the sidebar and the session's own messages | WP-W08 |
| `Live/LivePage.xaml`, `Live/LivePage.xaml.cs`, `Live/LiveViewModel.cs`, `Live/TranscriptModel.cs`, `Live/ReplyModel.cs`, `Live/PrerequisiteRows.cs`, `Text/LiveText.cs` | The Live page | WP-W09 |
| `Activity/ActivityPage.xaml`, `Activity/ActivityPage.xaml.cs`, `Activity/ActivityViewModel.cs`, `Activity/ActivityLog.cs`, `Activity/PayloadFormatter.cs`, `Text/ActivityText.cs` | The Activity page and the bounded log | WP-W09 |
| `Pairing/PairingPage.xaml`, `Pairing/PairingPage.xaml.cs`, `Pairing/PairingViewModel.cs`, `Pairing/QrCountdown.cs`, `Controls/QrCodeView.cs`, `Text/PairingText.cs` | The Pairing page and the QR drawing | WP-W10 |
| `Pairing/ResetPairingDialog.xaml`, `Pairing/ResetPairingDialog.xaml.cs`, `Pairing/ResetPairingDialogViewModel.cs` | The reset confirmation | WP-W10 |
| `Settings/SettingsPage.xaml`, `Settings/SettingsPage.xaml.cs`, `Settings/SettingsViewModel.cs`, `Settings/SettingsForm.cs`, `Settings/AboutInfo.cs`, `Text/SettingsText.cs` | The Settings page | WP-W10 |
| `Settings/MappingDialog.xaml`, `Settings/MappingDialog.xaml.cs`, `Settings/MappingDialogViewModel.cs` | The set-up and remove confirmation | WP-W10 |
| `SelfTest/SelfTestRunner.cs`, `SelfTest/SelfTestConsole.cs`, `SelfTest/VisualChecks.cs` | Entry, output, pixel checks (spike) | WP-W11 |
| `SelfTest/ScriptedRelay.cs`, `SelfTest/ScriptedHosting.cs`, `SelfTest/SyntheticData.cs` | The `IRelay` double; scripted prerequisites, mapping manager and settings store; the synthetic endpoint, token and payloads | WP-W11 |
| `SelfTest/Scenes.cs`, `SelfTest/SceneRenderer.cs`, `SelfTest/SelfTestAssertions.cs` | The fifteen scenes, off-screen rendering, the assertions of architecture 3.13 | WP-W11 |

### 2.6 Tests and tools

| Path | Responsibility | Owner |
|---|---|---|
| `windows/tests/LiveCue.Relay.Tests/LiveCue.Relay.Tests.csproj` | xUnit project | WP-W00 |
| `…/Support/RelayTestHost.cs` | Starts a real `IRelay` on port 0 with every seam faked; gives an `HttpClient`, a raw socket client and a `ClientWebSocket` factory | WP-W00, frozen |
| `…/Support/Fakes/*.cs` | `FakeAnswerBackend`, `FakeCatalogSource`, `FakeLabRuntime`, `FakeLabSocketConnector`, `FakeLocalModelController`, `FakePairingProtector`, `InMemoryPairingFiles`, `FakeSelfCheckClient`, `FakeProcessRunner`, `EventCollector`: one file each, behaving as protocol section 15 describes its fakes | WP-W00, frozen |
| `…/Support/RawHttp.cs`, `…/Support/TestTokens.cs`, `…/Support/Bodies.cs` | Hand-written HTTP/1.1 requests over a socket; tokens of shape C-02; the standard assist and summary bodies | WP-W00, frozen |
| `…/Contracts/*.cs` | Constants, error table, event shape (I-07), dependency rules | WP-W00 |
| `…/Edge/*.cs`, `…/Host/*.cs` | Protocol 15.1, 15.2; I-01 to I-06 | WP-W01 |
| `…/Pairing/*.cs` | Protocol 15.7 | WP-W02 |
| `…/Processes/*.cs`, `…/Catalog/*.cs`, `…/Answers/*.cs` | Protocol 15.3, 15.4 | WP-W03 |
| `…/LocalModel/*.cs` | Protocol 15.5 | WP-W04 |
| `…/Speech/*.cs` | Protocol 15.6 | WP-W05 |
| `…/Conformance/*.cs` | Cross-area sweeps and the security list (test plan section 4) | WP-W13 |
| `windows/tests/LiveCue.Hosting.Tests/LiveCue.Hosting.Tests.csproj`, `…/Support/*.cs` (`FakeTailscale.cs`, `FakeHostFileSystem.cs`, `InMemorySettingsStore.cs`, `FakeRelay.cs`, `ServeStatusFixtures.cs`) | Project and shared fakes. The project also references `LiveCue.Relay.Host` (the console host's tests) and links the relay tests' `FakeProcessRunner.cs` | WP-W00, frozen |
| `…/Settings/*.cs`, `…/Codex/*.cs`, `…/Prerequisites/*.cs`, `…/Endpoint/*.cs`, `…/Supervisor/*.cs`, `…/ConsoleHost/*.cs`, `…/LiveSmoke/CodexLiveTests.cs` | Hosting core tests and the opt-in Codex smoke | WP-W06 |
| `…/Tailscale/*.cs` | TS-1 to TS-10 and the parsers | WP-W07 |
| `windows/tests/LiveCue.Desktop.Tests/LiveCue.Desktop.Tests.csproj`, `…/Support/*.cs` (`FakeSession.cs`, `ImmediateDispatcher.cs`, `FakeClipboard.cs`, `FakeLauncher.cs`, `RecordingDialogHost.cs` (with `RecordingNavigator` and `RecordingToastHost`), `StaThread.cs`) | Project and shared fakes | WP-W00, frozen |
| `…/Session/*.cs`, `…/Shell/*.cs`, `…/Theme/*.cs` | State machines, navigation, resource keys | WP-W08 |
| `…/Live/*.cs`, `…/Activity/*.cs` | Live and Activity view models, the log bounds | WP-W09 |
| `…/Pairing/*.cs`, `…/Settings/*.cs` | Pairing and Settings view models, both dialogs | WP-W10 |
| `…/SelfTest/*.cs` | The self-test run and its guarantees | WP-W11 |
| `windows/tools/LiveCue.IconTool/` (`LiveCue.IconTool.csproj`, `Program.cs`, `MarkDrawing.cs`, `IcoWriter.cs`) | Draws DS 8.2 and writes the `.ico` frames and the iPhone 1024 px PNG | WP-W12 |

### 2.7 `scripts/` and `.github/`

| Path | Responsibility | Owner |
|---|---|---|
| `scripts/audit_public_source.py`, `scripts/audit-allowlist.txt` | The public-source audit (test plan section 6) | WP-W14 |
| `scripts/tests/test_audit_public_source.py` | Its unit tests | WP-W14 |
| `scripts/ci/changed_areas.py`, `scripts/ci/check_release_version.py`, `scripts/ci/verify_release_assets.py`, `scripts/ci/tests/test_*.py` for each | CI helpers and their unit tests | WP-W14 |
| `scripts/publish-windows.ps1` | Publishes both executables into one folder, adds `asr\`, the licence and the notices, checks the result, zips and checksums | WP-W00 writes a first version; WP-W14 owns it |
| `scripts/ci/select_simulator.py`, `boot_simulator.py`, `require_xcode.sh`, `generate_project.sh`, `xcodebuild_logged.sh`, `check_test_results.py`, `export_screenshots.py`, `validate_ipa.py` and their tests | iPhone CI helpers (from `spike/ios-ci`) | the iPhone side |
| `scripts/verify-pc-asr.ps1` | The manual GPU check (protocol P2-M01 to M03) | WP-W12 |
| `scripts/smoke/openrouter_catalogue.py` | The opt-in public catalogue fetch (test plan section 3) | WP-W12 |
| `.github/workflows/windows.yml` | The `windows` job of the test plan, on pushes to `main` and to `wp/**` branches and by hand, until WP-W14 folds every workflow into `ci.yml` | WP-W00, then WP-W14 |
| `.github/workflows/ci.yml` | Every job of the test plan; takes the `windows` job over from `windows.yml` | WP-W14 |
| `.github/workflows/release.yml`, `.github/actions/ios-simulator-tests/action.yml` | Release; the one simulator test definition | WP-W14 |

---

## 3. Contracts

Everything in this section crosses a project or work-package boundary. The foundation creates each file
exactly as written, with the stub bodies shown, and the solution compiles and its tests pass before any other
package starts. Five of the files then get their real bodies from the foundation itself and hold more than
their block shows, with the declarations as printed: `RelayFactory.cs` (its forwards), `RelayErrors.cs` (the
whole table, and `All`), `RouteTable.cs` (the ten `Rows`, `Row(id)` and `IsRequestIdShape`),
`ProtocolConstants.cs` and `HostingFactory.cs` (`Create`). Visibility rule: a type is `public` only when another assembly needs it. Seams are `internal`
and reach the tests through `InternalsVisibleTo`; the one public seam is the process runner, because the
hosting library runs the Tailscale and Codex command lines through the same runner (one implementation of
"no shell, hidden, argument list, time limit, tree kill").

Conventions that hold for every contract unless its comment says otherwise:

- Every method is safe to call from any thread. Nothing in `LiveCue.Relay` or `LiveCue.Hosting` touches a
  dispatcher or captures a synchronisation context.
- A `CancellationToken` that fires ends the call with `OperationCanceledException`.
- No exception message, event or return value ever carries the token, an `Authorization` value, the pairing
  payload, a nonce, or output of Codex, PowerShell, Docker or Tailscale (architecture 3.10).
- Time comes from the `TimeProvider` in `RelayDependencies.Clock`. No code calls `DateTime.UtcNow`,
  `Stopwatch`, `Task.Delay(TimeSpan)` or `new CancellationTokenSource(TimeSpan)` without that provider.

### 3.1 Relay: public surface

```csharp
// file: windows/src/LiveCue.Relay/Contracts/RelayOptions.cs
namespace LiveCue.Relay;

/// <summary>How the relay is reached. Decides step E-1 of the protocol.</summary>
public enum ConnectionMode
{
    /// <summary>Tailnet only. Requests marked as arriving through Funnel are refused.</summary>
    Private,

    /// <summary>Funnel path mount. Funnel-marked requests are served.</summary>
    Public,
}

/// <summary>Life cycle of one relay instance. An instance is started once and never restarted.</summary>
public enum RelayStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted,
}

/// <summary>
/// Everything one relay instance is told. Immutable. Contains no secret.
/// Validation happens in <see cref="IRelay.StartAsync"/>, not in the constructor.
/// </summary>
/// <param name="Port">Loopback port. 0 picks a free port (tests only). The supervisor never passes 47831.</param>
/// <param name="AdvertisedEndpoint">Protocol 4.3. Canonicalised at start; a text that fails the grammar is the
/// fault <see cref="RelayFaultCode.InvalidEndpoint"/>.</param>
/// <param name="Mode">Protocol E-1.</param>
/// <param name="DataDirectory">Existing folder. Holds <c>pairing.json</c>, <c>schemas\</c>, the relay's own
/// launcher-script copies in <c>lab\</c> and the per-request Codex folders in <c>work\</c>
/// (architecture 3.5).</param>
/// <param name="CodexExecutablePath">Absolute path of <c>codex.exe</c>, or null when none was found. With null,
/// the catalogue is unavailable and every answer run fails; the relay still starts.</param>
/// <param name="LabFolder">Folder with the two launcher scripts, or null: PC speech is "not configured".</param>
/// <param name="LabPort">Host port of the lab (protocol 11.1).</param>
/// <param name="RelayVersion">Reported by health and sent to the catalogue process.</param>
/// <param name="WarmCatalogOnStart">Reads the catalogue once after start and ignores the outcome.</param>
public sealed record RelayOptions(
    int Port,
    string AdvertisedEndpoint,
    ConnectionMode Mode,
    string DataDirectory,
    string? CodexExecutablePath,
    string? LabFolder,
    int LabPort,
    string RelayVersion,
    bool WarmCatalogOnStart = true);
```

```csharp
// file: windows/src/LiveCue.Relay/Contracts/IRelay.cs
using System.Threading.Channels;

namespace LiveCue.Relay;

/// <summary>
/// One relay instance: the in-process replacement of the old child process (architecture 3.4).
/// All members are thread-safe. Disposing a running relay stops it first.
/// </summary>
public interface IRelay : IAsyncDisposable
{
    /// <summary>Current life-cycle state. Changes before the matching event is published.</summary>
    RelayStatus Status { get; }

    /// <summary>False while paused. A new instance is always accepting.</summary>
    bool Accepting { get; }

    /// <summary>The bound loopback port while Running, otherwise 0.</summary>
    int BoundPort { get; }

    /// <summary>
    /// Every event, in order. One bounded channel (W-01), oldest dropped when full. Single reader.
    /// Completed after <see cref="RelayStopped"/> has been written.
    /// </summary>
    ChannelReader<RelayEvent> Events { get; }

    /// <summary>
    /// Validates the endpoint, loads or creates the pairing, writes the schema files, binds the listener.
    /// On success publishes <see cref="RelayStarted"/> (and <see cref="Notice"/> PairingRestored for an
    /// existing pairing) and returns with Status Running. On failure publishes <see cref="RelayFaulted"/>,
    /// leaves nothing listening, sets Status Faulted and throws <see cref="RelayStartException"/>.
    /// A second call throws <see cref="InvalidOperationException"/>.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Graceful, bounded by W-02: stops accepting, kills every answer run, ends every speech stream with
    /// RELAY_STOPPING, closes the listener, aborts what is still open. Publishes <see cref="RelayStopped"/>.
    /// Idempotent; never throws for a relay that is already stopped or faulted.
    /// </summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>
    /// Pause (false) or resume (true). Takes effect before the task completes; then publishes
    /// <see cref="AcceptingChanged"/>. Calling it with the current value does nothing and publishes nothing.
    /// Throws <see cref="InvalidOperationException"/> unless Running.
    /// </summary>
    Task SetAcceptingAsync(bool accepting);

    /// <summary>
    /// Decrypts the saved token and returns the matrix of the same pairing payload. The file is not
    /// modified. Publishes <see cref="PairingShown"/>, then starts one self-check in the background whose
    /// result arrives as <see cref="SelfCheckCompleted"/>. Calls are serialised with ResetPairingAsync.
    /// Throws <see cref="InvalidOperationException"/> unless Running.
    /// </summary>
    Task<PairingQr> ShowPairingAsync(CancellationToken ct);

    /// <summary>
    /// Replaces the pairing (protocol 4.4, "Reset pairing"). The old token is rejected from the moment the
    /// task completes; in-flight answers of the old token end with 401 and open streams with
    /// PAIRING_CHANGED. Publishes <see cref="Notice"/> PairingReset, <see cref="PairingShown"/>, then
    /// self-checks as above. If the file cannot be replaced the old pairing stays valid and the call throws
    /// <see cref="IOException"/> with a fixed message.
    /// </summary>
    Task<PairingQr> ResetPairingAsync(CancellationToken ct);
}

/// <summary>What the desktop may hold of a pairing: the advertised endpoint and the module matrix.</summary>
/// <param name="Endpoint">Canonical advertised endpoint.</param>
/// <param name="Modules">Square, row-major, true = dark, no quiet zone. At least 21 rows.</param>
public sealed record PairingQr(string Endpoint, bool[][] Modules);

/// <summary>
/// The pairing payload as text (protocol 4.2). Implemented by the relay object. The relay's own tests read
/// the accepted token through it; no host resolves it (the console host's <c>--show-pairing</c> reads the
/// stored pairing through <see cref="StoredPairingExport"/>). <c>LiveCue.Desktop</c> never references this
/// type; a test proves it.
/// </summary>
public interface IPairingExport
{
    /// <summary>Throws <see cref="InvalidOperationException"/> unless the relay is Running.</summary>
    Task<string> GetPairingPayloadAsync(CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Contracts/RelayFaults.cs
namespace LiveCue.Relay;

/// <summary>Why a relay could not start or had to stop. Wire names are in <see cref="RelayFaultCodes"/>.</summary>
public enum RelayFaultCode
{
    PortInUse,
    PairingStoreUnreadable,
    InvalidEndpoint,
    StartFailed,
    Internal,
}

/// <summary>The upper-case names of architecture 3.4, for JSON lines and logs.</summary>
public static class RelayFaultCodes
{
    public static string Name(RelayFaultCode code) => code switch
    {
        RelayFaultCode.PortInUse => "PORT_IN_USE",
        RelayFaultCode.PairingStoreUnreadable => "PAIRING_STORE_UNREADABLE",
        RelayFaultCode.InvalidEndpoint => "INVALID_ENDPOINT",
        RelayFaultCode.StartFailed => "START_FAILED",
        _ => "INTERNAL",
    };
}

/// <summary>Thrown by <see cref="IRelay.StartAsync"/>. The message is a fixed English sentence per code.</summary>
public sealed class RelayStartException : Exception
{
    public RelayStartException(RelayFaultCode code, string fixedMessage)
        : base(fixedMessage)
    {
        Code = code;
    }

    public RelayFaultCode Code { get; }
}
```

```csharp
// file: windows/src/LiveCue.Relay/Contracts/RelayEvents.cs
namespace LiveCue.Relay;

/// <summary>
/// Base of every event (architecture 3.4). Published on thread-pool threads, never under a lock that a
/// request holds. No event has a member that carries the token or an Authorization value.
/// </summary>
public abstract record RelayEvent(DateTimeOffset At);

public enum RelayStopReason { Requested, Fault }

public enum SelfCheckResult { Verified, VerifiedThroughFunnel, NotVerified }

public enum NoticeKind { PairingRestored, PairingReset }

public enum AnswerKind { Assist, Summary }

public enum LocalModelState { Stopped, Starting, Ready, Stopping, Error }

public enum SpeechStreamPhase { Loading, Ready, Stopped, Failed }

/// <summary>The listener is bound.</summary>
public sealed record RelayStarted(
    DateTimeOffset At,
    string Endpoint,
    int Port,
    ConnectionMode Mode,
    int ProtocolVersion,
    string RelayVersion,
    bool PairingWasCreated) : RelayEvent(At);

/// <summary>The listener is closed. Always the last event of an instance that had started.</summary>
public sealed record RelayStopped(DateTimeOffset At, RelayStopReason Reason) : RelayEvent(At);

/// <summary>Start failed, or a fatal runtime fault (then followed by RelayStopped with Reason Fault).</summary>
public sealed record RelayFaulted(DateTimeOffset At, RelayFaultCode Code, string Message) : RelayEvent(At);

/// <summary>A pause or resume took effect.</summary>
public sealed record AcceptingChanged(DateTimeOffset At, bool Accepting) : RelayEvent(At);

/// <summary>Protocol 5.5 presence: one per request that passed E-3, except the self-check.</summary>
public sealed record PhoneSeen(DateTimeOffset At) : RelayEvent(At);

/// <summary>ShowPairingAsync or ResetPairingAsync produced a matrix. The matrix is only in the return value.</summary>
public sealed record PairingShown(DateTimeOffset At, string Endpoint) : RelayEvent(At);

/// <summary>Protocol 4.5. Exactly one per PairingShown.</summary>
public sealed record SelfCheckCompleted(DateTimeOffset At, SelfCheckResult Result) : RelayEvent(At);

/// <summary>Informational. Message is a fixed text of section 4.12.</summary>
public sealed record Notice(DateTimeOffset At, NoticeKind Kind, string Message) : RelayEvent(At);

/// <summary>An answer request passed R-7. PayloadJson is the request body as compact JSON.</summary>
public sealed record RequestStarted(
    DateTimeOffset At,
    string RequestId,
    AnswerKind Kind,
    string Model,
    string ReasoningEffort,
    string? Transcript,
    string? PartialTranscript,
    string? Instruction,
    string PayloadJson) : RelayEvent(At);

/// <summary>An answer request succeeded. DurationMs equals relayTotalMs. PayloadJson is the reply.</summary>
public sealed record RequestCompleted(
    DateTimeOffset At,
    string RequestId,
    AnswerKind Kind,
    double DurationMs,
    double CodexMs,
    string Model,
    string ReasoningEffort,
    string PayloadJson) : RelayEvent(At);

/// <summary>
/// An answer request ended any other way after RequestStarted. Code is a wire code of
/// <see cref="Protocol.RelayCodes"/>, or CLIENT_DISCONNECTED. Message is the relay text of that code.
/// </summary>
public sealed record RequestFailed(
    DateTimeOffset At,
    string RequestId,
    AnswerKind Kind,
    string Code,
    string Message) : RelayEvent(At);

/// <summary>The catalogue could not be read, or a selection was not in it.</summary>
public sealed record AssistantNotice(DateTimeOffset At, string Code, string Message) : RelayEvent(At);

/// <summary>The five-member status object of protocol 9.1.</summary>
public sealed record LocalModelStatus(
    LocalModelState State,
    string? Model,
    bool Busy,
    int ElapsedSeconds,
    string Message);

/// <summary>A model operation began or ended. Message equals Status.Message.</summary>
public sealed record LocalModelChanged(DateTimeOffset At, LocalModelStatus Status, string Message) : RelayEvent(At);

/// <summary>Stream progress. Code is set only for Failed. Message is a fixed text of section 4.12.</summary>
public sealed record SpeechStreamChanged(
    DateTimeOffset At,
    SpeechStreamPhase Phase,
    string Model,
    string? Code,
    string Message) : RelayEvent(At);

/// <summary>The text of one speechComplete frame, cut like the frame (C-22).</summary>
public sealed record SpeechText(DateTimeOffset At, string Text) : RelayEvent(At);

/// <summary>
/// The rejected-request counters of protocol 5.5: totals since this instance started. Published at most
/// once per W-13 and only when a total changed, and once more before RelayStopped when a total changed
/// since the last one. Never published per request.
/// </summary>
/// <param name="Malformed">Refused at E-1 for Origin or Sec-Fetch-Site, or at E-2.</param>
/// <param name="TokenNotAccepted">Refused at E-3: a well-formed token that is not the accepted one.</param>
/// <param name="FunnelInPrivateMode">Refused at E-1 in Private mode for Tailscale-Funnel-Request.</param>
public sealed record EdgeStatistics(
    DateTimeOffset At,
    long Malformed,
    long TokenNotAccepted,
    long FunnelInPrivateMode) : RelayEvent(At);

/// <summary>An internal problem: an exception type name and a fixed description, never exception text.</summary>
public sealed record Diagnostic(DateTimeOffset At, string Source, string Message) : RelayEvent(At);
```

```csharp
// file: windows/src/LiveCue.Relay/Contracts/RelayDependencies.cs
using LiveCue.Relay.Answers;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Speech;

namespace LiveCue.Relay;

/// <summary>
/// The seams (architecture 3.2). Production code passes null or a default instance. A null member means
/// "use the production implementation". Used by tests, and by the hosting library for Clock and Processes.
/// </summary>
public sealed class RelayDependencies
{
    /// <summary>The only source of time and timers.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Runs Codex and the lab scripts.</summary>
    public IProcessRunner? Processes { get; init; }

    /// <summary>Parent of the per-request Codex folders. Null means DataDirectory\work (protocol 12.2),
    /// which the relay empties at each start. A folder given here is never emptied by the relay.</summary>
    public string? TempRoot { get; init; }

    internal IPairingProtector? PairingProtector { get; init; }

    internal IPairingFiles? PairingFiles { get; init; }

    internal ISelfCheckClient? SelfCheckClient { get; init; }

    internal ICatalogSource? CatalogSource { get; init; }

    internal IAnswerBackend? AnswerBackend { get; init; }

    internal ILabRuntime? LabRuntime { get; init; }

    internal ILabSocketConnector? LabSocketConnector { get; init; }

    /// <summary>Tests of the speech bridge only: replaces the whole PC model controller (the "prepare
    /// stub" of protocol 15.6). Null everywhere else.</summary>
    internal ILocalModelController? LocalModelController { get; init; }
}
```

```csharp
// file: windows/src/LiveCue.Relay/Contracts/RelayFactory.cs
namespace LiveCue.Relay;

/// <summary>The only way to obtain a relay.</summary>
public static class RelayFactory
{
    /// <summary>
    /// Creates an instance in state Stopped. Does no I/O and never throws for bad option values;
    /// <see cref="IRelay.StartAsync"/> reports them. Throws only <see cref="ArgumentNullException"/>.
    /// </summary>
    public static IRelay Create(RelayOptions options, RelayDependencies? dependencies = null)
        => throw new NotImplementedException();
}

/// <summary>Pairing work that needs no running relay: the recovery from an unreadable pairing file.</summary>
public static class PairingMaintenance
{
    /// <summary>
    /// Replaces <c>pairing.json</c> in <paramref name="dataDirectory"/> with a new pairing, atomically,
    /// whatever the old file contained. Must not be called while a relay runs on that folder (the supervisor
    /// guarantees it). Throws <see cref="IOException"/> with a fixed message when the file cannot be written.
    /// </summary>
    public static Task ResetStoredPairingAsync(string dataDirectory, CancellationToken ct)
        => throw new NotImplementedException();
}

/// <summary>
/// The pairing payload of the stored pairing, read without a relay: what the console host's
/// <c>--show-pairing</c> prints. It binds no port and only reads, so it may be called while a relay (the
/// desktop app) runs on the same folder. <c>LiveCue.Desktop</c> never references this type; a test proves it.
/// </summary>
public static class StoredPairingExport
{
    /// <summary>
    /// Reads <c>pairing.json</c> in <paramref name="dataDirectory"/>, decrypts the token and returns the
    /// payload text of protocol 4.2 for <paramref name="advertisedEndpoint"/> in canonical form. Returns
    /// null when no pairing file exists. The file is never modified. Throws <see cref="ArgumentException"/>
    /// for an endpoint that fails the grammar of protocol 4.3, and <see cref="IOException"/> with a fixed
    /// message when the file exists and cannot be read.
    /// </summary>
    public static Task<string?> ReadPayloadAsync(string dataDirectory, string advertisedEndpoint, CancellationToken ct)
        => throw new NotImplementedException();
}
```

The foundation's real bodies are `Core.RelayHost.Create(options, dependencies ?? new RelayDependencies())`,
`Pairing.StoredPairingReset.RunAsync(dataDirectory, protector: null, files: null, ct)` and
`Pairing.StoredPairingPayload.ReadAsync(dataDirectory, advertisedEndpoint, protector: null, files: null, ct)`.

**`Protocol/ProtocolConstants.cs`** (`public static class ProtocolConstants`, namespace
`LiveCue.Relay.Protocol`). It is the only C# file in which a protocol number is written. Its values are those
of protocol section 2 and of the route table in protocol 6.1; `ProtocolConstantsTests` checks each one. Member
names:

| Member | Id | Member | Id |
|---|---|---|---|
| `ProtocolVersion` | C-01 | `TokenLength` | C-02 |
| `RequestIdMaxLength` | C-03 | `SpeechModels` | C-04 |
| `ReasoningEfforts` | C-05 | `AlwaysNoneModels` | C-07 |
| `MaxRequestBodyBytes` | C-10 | `MaxRequestLineBytes`, `MaxHeaderBytes`, `MaxHeaderCount` | C-19 |
| `MaxPhoneFrameBytes` | C-20 | `MaxRelayFrameBytes` | C-21 |
| `MaxTranscriptUnits` | C-22 | `MaxLabMessageBytes` | C-23 |
| `MaxCodexOutputBytes` | C-24 | `MaxSpeechTextBytes` | C-25 |
| `ModelIdMaxLength` | C-06 | `EdgeStatisticsInterval` | W-13 |
| `MaxConnections` | C-30 | `MaxRequestsInFlight` | C-31 |
| `MaxSpeechSockets` | C-32 | `HeadersTimeout`, `BodyTimeout`, `KeepAliveTimeout` | C-40, C-41, C-42 |
| `AnswerDeadline` | C-43 | `CatalogAttemptTimeout`, `CatalogAttempts`, `CatalogRetryGap`, `CatalogCacheLife` | C-44 |
| `LabProbeTimeout` | C-45 | `LabStartTimeout`, `LabStopTimeout` | C-46 |
| `LabHandshakeTimeout` | C-47 | `ModelErrorLatch` | C-48 |
| `SelfCheckTimeout` | C-49 | `LoadingFrameInterval` | C-50 |
| `ModelLoadLimit` | C-51 | `AudioIdleLimit` | C-52 |
| `FinalizeLimit` | C-53 | `StreamLimit` | C-54 |
| `SocketLifetime` | C-55 | `ControlChangeLimit` | C-56 |
| `KeepAliveFrameInterval` | C-57 | `CloseHandshakeWait` | C-58 |
| `AudioSampleRate`, `AudioFrameBytes` | C-70, C-71 | `AllowanceBaseBytes`, `AllowanceBytesPerMs` | C-72 |
| `LabBacklogBytes` | C-73 | `HealthDeadline` … `CancelDeadline` (one per route) | protocol 6.1 |

Members beyond the table: `MinBodyBytesPerSecond` and `MinBodyRateGrace` (protocol 5.1), `MaxJsonDepth`
(protocol 5.4), `LabRelayProtocol` (protocol 9.2), `ForbiddenPort` (47831, which a relay never binds), and the
numbers of section 4.1: `EventChannelCapacity` and `DroppedEventReportInterval` (W-01), `StopBudget` (W-02),
`MaxPhoneQueueFrames` (W-03), `PhoneWriteStall` (W-04), `MaxStateFileBytes` (W-05), `MaxProbeBodyBytes` (W-06),
`MaxSelfChecks` (W-07), `KilledProcessWait` (W-08), `FolderDeleteAttempts` and `FolderDeleteGap` (W-09),
`DesktopTick` (W-10), `WindowCloseWait` (W-11), `SelfTestWatchdog` (W-12).

The two patterns C-03 and C-06 are checked by hand-written functions in `Edge/RouteTable.cs`
(`IsRequestIdShape`) and `Catalog/OfferFilter.cs` (`IsModelIdShape`), not by `Regex`.

**`Protocol/RelayCodes.cs`** (`public static class RelayCodes`): one `public const string` per code of
protocol 14.1, named in Pascal case (`Unauthorized = "UNAUTHORIZED"`), plus
`ClientDisconnected = "CLIENT_DISCONNECTED"`, which is never sent on the wire.

**Whole-text shapes** (protocol section 1). Every shape of the protocol and of the settings file is matched
against the whole text: a hand-written loop over exactly the characters given, or a `Regex` anchored with
`\A` and `\z`. `^` and `$` are never used for a shape, because `$` also matches before a final line feed.
This holds for C-02 (the bearer check of P-3), C-03, C-06, the prefix, the token hash, the ledger path and
the Codex version text.

### 3.2 Relay: process runner (public seam)

```csharp
// file: windows/src/LiveCue.Relay/Processes/IProcessRunner.cs
namespace LiveCue.Relay.Processes;

/// <summary>Which environment variables a child process receives.</summary>
public enum EnvironmentPolicy
{
    /// <summary>The parent's environment unchanged. Used for the Tailscale CLI.</summary>
    Inherit,

    /// <summary>Only the names of protocol 12.6, compared without regard to case: the thirteen names of
    /// protocol 11.5, plus HOMEDRIVE, HOMEPATH and CODEX_HOME, plus HTTPS_PROXY, HTTP_PROXY, ALL_PROXY and
    /// NO_PROXY when the parent has them. Used for every Codex process: catalogue, sign-in check, version
    /// read and answer run. (Before the review this member was WithoutLiveCueVariables.)</summary>
    CodexAllowlist,

    /// <summary>Only the thirteen names of protocol 11.5, compared without regard to case. Used for the
    /// lab launcher scripts.</summary>
    LabScriptAllowlist,
}

/// <summary>One process to run. No member is ever passed through a shell.</summary>
public sealed record ProcessRequest
{
    /// <summary>Absolute path of an .exe file. A relative path or another extension is an
    /// <see cref="ArgumentException"/>.</summary>
    public required string FileName { get; init; }

    /// <summary>Passed one by one through ProcessStartInfo.ArgumentList, never joined into a string.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public EnvironmentPolicy Environment { get; init; } = EnvironmentPolicy.Inherit;

    /// <summary>Written to standard input as UTF-8 without a byte-order mark, then the pipe is closed.
    /// Null closes standard input at once. Ignored by StartSessionAsync.</summary>
    public string? StandardInput { get; init; }

    /// <summary>Bytes of standard output to keep. 0 discards standard output. Output beyond the limit ends
    /// the process with <see cref="ProcessOutcome.OutputLimitExceeded"/>.</summary>
    public int MaxStandardOutputBytes { get; init; }

    /// <summary>Wall-clock limit, measured with the runner's TimeProvider.</summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>True places the process in the kill-on-close job object, so it cannot outlive this
    /// application (architecture 3.7), and a time-out or cancellation kills its whole process tree.
    /// False for the lab launcher scripts only: the process is not placed in the job, and a time-out or
    /// cancellation kills that one process and leaves its descendants (Docker Desktop) alone.</summary>
    public bool KillWithApplication { get; init; } = true;
}

public enum ProcessOutcome
{
    /// <summary>The process ended by itself. ExitCode is meaningful.</summary>
    Exited,
    TimedOut,
    Cancelled,
    OutputLimitExceeded,
    StartFailed,
}

/// <param name="StandardOutput">At most MaxStandardOutputBytes bytes; empty unless Outcome is Exited.</param>
public sealed record ProcessResult(ProcessOutcome Outcome, int ExitCode, ReadOnlyMemory<byte> StandardOutput);

/// <summary>Thrown by <see cref="IProcessSession"/> and by StartSessionAsync. Fixed message per outcome.</summary>
public sealed class ProcessFailedException : Exception
{
    public ProcessFailedException(ProcessOutcome outcome)
        : base("The child process failed: " + outcome + ".")
    {
        Outcome = outcome;
    }

    public ProcessOutcome Outcome { get; }
}

/// <summary>
/// A line-oriented conversation with a running process (the Codex catalogue exchange). One writer and one
/// reader at a time. Disposing closes standard input and kills the whole process tree; it never throws.
/// </summary>
public interface IProcessSession : IAsyncDisposable
{
    /// <summary>Writes the line and a line feed as UTF-8 and flushes.</summary>
    ValueTask WriteLineAsync(string line, CancellationToken ct);

    /// <summary>The next line without its terminator, or null at end of output. Throws
    /// <see cref="ProcessFailedException"/> for OutputLimitExceeded and TimedOut.</summary>
    ValueTask<string?> ReadLineAsync(CancellationToken ct);
}

/// <summary>
/// Starts child processes. Always: UseShellExecute false, CreateNoWindow true, standard error discarded,
/// the process killed on time-out, cancellation, output overflow and dispose (with its whole tree when
/// KillWithApplication is true).
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs to completion. Never throws for a process failure: the outcome says what happened, and for
    /// Cancelled the tree is already dead when the task completes. Throws only ArgumentException.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct);

    /// <summary>Starts the process for a line exchange. Throws <see cref="ProcessFailedException"/> with
    /// StartFailed when it cannot be started.</summary>
    Task<IProcessSession> StartSessionAsync(ProcessRequest request, CancellationToken ct);
}
```

### 3.3 Relay: internal contracts between areas

These are the declarations the five relay packages build against. Namespaces follow the folders.

```csharp
// file: windows/src/LiveCue.Relay/Core/IRelayEventSink.cs
namespace LiveCue.Relay.Core;

/// <summary>Where every area publishes. Publishing never blocks and never throws.</summary>
internal interface IRelayEventSink
{
    /// <summary>The clock's current time, for the At member of a new event.</summary>
    DateTimeOffset Now { get; }

    void Publish(RelayEvent relayEvent);

    /// <summary>Publishes a Diagnostic whose message is "<paramref name="fixedText"/> (TypeName)".
    /// The exception's own message and stack are never used.</summary>
    void Diagnostic(string source, string fixedText, Exception? exception = null);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Core/IAccessState.cs
namespace LiveCue.Relay.Core;

/// <summary>Pause and shutdown, as every area observes them.</summary>
internal interface IAccessState
{
    /// <summary>False while paused.</summary>
    bool Accepting { get; }

    /// <summary>Cancelled when the relay next stops accepting. While paused: already cancelled.
    /// A fresh token is issued on resume.</summary>
    CancellationToken WhileAccepting { get; }

    /// <summary>Cancelled when StopAsync begins or a fatal fault is raised. Never reset.</summary>
    CancellationToken Stopping { get; }
}
```

```csharp
// file: windows/src/LiveCue.Relay/Core/RelayException.cs
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Core;

/// <summary>Carries a wire error out of area code. The edge turns it into the response of that error.</summary>
internal sealed class RelayException : Exception
{
    public RelayException(RelayError error)
        : base(error.Text)
    {
        Error = error;
    }

    public RelayError Error { get; }
}
```

```csharp
// file: windows/src/LiveCue.Relay/Protocol/RelayErrors.cs
namespace LiveCue.Relay.Protocol;

/// <summary>One row of protocol 14.1: status, code and the fixed relay text.</summary>
internal readonly record struct RelayError(int Status, string Code, string Text);

/// <summary>
/// The table of protocol 14.1. One static readonly member per code, named like RelayCodes.
/// Body(error) returns the pre-built UTF-8 bytes of {"error":Text,"code":Code}; Frame(error) the bytes of
/// {"type":"error","code":Code,"message":Text}. ForCode returns null for an unknown code.
/// </summary>
internal static partial class RelayErrors
{
    public static ReadOnlyMemory<byte> Body(RelayError error) => throw new NotImplementedException();

    public static ReadOnlyMemory<byte> Frame(RelayError error) => throw new NotImplementedException();

    public static RelayError? ForCode(string code) => throw new NotImplementedException();
}
```

```csharp
// file: windows/src/LiveCue.Relay/Core/SpeechLease.cs
namespace LiveCue.Relay.Core;

/// <summary>The one speech lease (C-34). Complete as written; no package changes it.</summary>
internal sealed class SpeechLease
{
    private int _held;

    /// <summary>True while a stream holds the lease. Read by route 4 (busy) and by M-6.</summary>
    public bool IsHeld => Volatile.Read(ref _held) == 1;

    /// <summary>Step S-6. Returns a handle whose Dispose releases the lease exactly once, or null.</summary>
    public IDisposable? TryAcquire()
        => Interlocked.CompareExchange(ref _held, 1, 0) == 0 ? new Handle(this) : null;

    private sealed class Handle(SpeechLease owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Volatile.Write(ref owner._held, 0);
            }
        }
    }
}
```

```csharp
// file: windows/src/LiveCue.Relay/Core/RelayContext.cs
using LiveCue.Relay.Processes;

namespace LiveCue.Relay.Core;

/// <summary>What every area receives at construction. One per relay instance.</summary>
internal sealed class RelayContext
{
    public RelayContext(
        RelayOptions options,
        RelayDependencies dependencies,
        IRelayEventSink events,
        IAccessState access,
        IProcessRunner processes,
        string advertisedEndpoint)
    {
        Options = options;
        Dependencies = dependencies;
        Events = events;
        Access = access;
        Processes = processes;
        AdvertisedEndpoint = advertisedEndpoint;
    }

    public RelayOptions Options { get; }

    public RelayDependencies Dependencies { get; }

    public TimeProvider Clock => Dependencies.Clock;

    public IRelayEventSink Events { get; }

    public IAccessState Access { get; }

    /// <summary>Dependencies.Processes, or the production runner.</summary>
    public IProcessRunner Processes { get; }

    /// <summary>Options.AdvertisedEndpoint in canonical form.</summary>
    public string AdvertisedEndpoint { get; }

    /// <summary>Dependencies.TempRoot, or DataDirectory\work. Never the OS temp folder.</summary>
    public string TempRoot => Dependencies.TempRoot ?? Path.Combine(Options.DataDirectory, "work");
}
```

```csharp
// file: windows/src/LiveCue.Relay/Edge/RouteTable.cs
namespace LiveCue.Relay.Edge;

/// <summary>The ten routes, numbered as in protocol 6.1.</summary>
internal enum RouteId
{
    Health = 1,
    PairVerify = 2,
    Models = 3,
    LocalModelStatus = 4,
    LocalModelStart = 5,
    LocalModelStop = 6,
    Assist = 7,
    SessionSummary = 8,
    CancelRequest = 9,
    Speech = 10,
}

/// <param name="Target">The exact raw request target; for route 9 the prefix "/v1/requests/".</param>
internal sealed record RouteEntry(
    RouteId Id,
    string Method,
    string Target,
    bool ReadsBody,
    bool ServedWhilePaused,
    TimeSpan Deadline);

/// <summary>Step E-4 as a pure function. The foundation implements it completely.</summary>
internal static partial class RouteTable
{
    /// <summary>
    /// Ordinal, case-sensitive comparison of method and raw target with the ten rows. Route 9 matches when
    /// the rest of the target is 1 to C-03 characters of [A-Za-z0-9_-]; that rest is returned as
    /// <paramref name="requestId"/>. Returns null for anything else. Route 10 is returned for
    /// GET /v1/speech whether or not the request is an upgrade; the pipeline turns a non-upgrade into 404.
    /// </summary>
    public static RouteEntry? Match(string method, string rawTarget, out string? requestId)
        => throw new NotImplementedException();
}
```

```csharp
// file: windows/src/LiveCue.Relay/Edge/RouteContext.cs
using System.Net.WebSockets;
using System.Text.Json;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Edge;

/// <summary>Why a route's work token was cancelled. Evaluated in this order.</summary>
internal enum RouteCancellation
{
    None,
    ClientAborted,
    RelayStopping,
    PairingRevoked,
    DeadlineExceeded,
}

/// <summary>Result of protocol 5.4. Exactly one of Error and Root is set.</summary>
/// <param name="Utf8">The body bytes as received.</param>
/// <param name="Root">The parsed top-level object, detached from its document.</param>
/// <param name="ReadMs">Milliseconds from header arrival to the parsed body (requestReadMs).</param>
internal readonly record struct BodyResult(RelayError? Error, ReadOnlyMemory<byte> Utf8, JsonElement Root, double ReadMs);

/// <summary>The I/O a handler may do. Implemented by the edge; faked in handler unit tests.</summary>
internal interface IRouteIo
{
    Task<BodyResult> ReadJsonBodyAsync(RouteContext context);

    /// <summary>Writes one JSON document with the three fixed headers. A second write on the same request
    /// throws <see cref="InvalidOperationException"/>.</summary>
    Task WriteJsonAsync(RouteContext context, int status, ReadOnlyMemory<byte> utf8Json);

    /// <summary>Accepts the WebSocket upgrade with no subprotocol and no compression.</summary>
    Task<WebSocket> AcceptWebSocketAsync(RouteContext context);
}

/// <summary>
/// One authenticated request that passed steps E-1 to E-7. Handlers do the route's own checks (E-8),
/// then either write exactly one response, or throw <see cref="Core.RelayException"/>, or let an
/// <see cref="OperationCanceledException"/> of <see cref="Work"/> escape. The edge answers the last two.
/// </summary>
internal sealed class RouteContext
{
    public required HttpContext Http { get; init; }

    public required RouteEntry Route { get; init; }

    /// <summary>Route 9 only: the id from the target.</summary>
    public string? RequestId { get; init; }

    /// <summary>Clock timestamp taken when the pipeline first saw the request.</summary>
    public required long ArrivalTimestamp { get; init; }

    /// <summary>Cancelled at the route deadline, on client abort, on relay stop and on pairing reset.</summary>
    public required CancellationToken Work { get; init; }

    public required CancellationToken ClientAborted { get; init; }

    public required CancellationToken PairingRevoked { get; init; }

    public required CancellationToken RelayStopping { get; init; }

    public required IRouteIo Io { get; init; }

    /// <summary>The first cause that applies, in the order of <see cref="RouteCancellation"/>.</summary>
    public RouteCancellation Cancellation =>
        ClientAborted.IsCancellationRequested ? RouteCancellation.ClientAborted
        : RelayStopping.IsCancellationRequested ? RouteCancellation.RelayStopping
        : PairingRevoked.IsCancellationRequested ? RouteCancellation.PairingRevoked
        : Work.IsCancellationRequested ? RouteCancellation.DeadlineExceeded
        : RouteCancellation.None;

    public Task<BodyResult> ReadJsonBodyAsync() => Io.ReadJsonBodyAsync(this);

    public Task WriteJsonAsync(int status, ReadOnlyMemory<byte> utf8Json) => Io.WriteJsonAsync(this, status, utf8Json);

    public Task WriteErrorAsync(RelayError error) => Io.WriteJsonAsync(this, error.Status, RelayErrors.Body(error));

    public Task<WebSocket> AcceptWebSocketAsync() => Io.AcceptWebSocketAsync(this);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Routes/IRouteHandler.cs
using LiveCue.Relay.Edge;

namespace LiveCue.Relay.Routes;

/// <summary>One route. Stateless; one instance serves every request of its route.</summary>
internal interface IRouteHandler
{
    Task HandleAsync(RouteContext context);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Pairing/IPairingService.cs
namespace LiveCue.Relay.Pairing;

/// <summary>The pairing area as the edge, the host, the answers and the speech bridge use it.</summary>
internal interface IPairingService
{
    /// <summary>
    /// Start: loads pairing.json or, only when the file does not exist, creates a pairing. Returns true
    /// when one was created. Any other load problem throws RelayStartException(PairingStoreUnreadable) and
    /// leaves the file untouched.
    /// </summary>
    Task<bool> LoadOrCreateAsync(CancellationToken ct);

    /// <summary>
    /// Step E-3: SHA-256 of the 43 characters against the accepted hash, constant time. Allocation-free.
    /// The accepted hash and its revocation token are one immutable object, read once, at the moment of the
    /// call: on true, <paramref name="revoked"/> is the token of exactly the pairing that was verified, and
    /// it is cancelled when that pairing is revoked by a reset, even a reset that completes between this
    /// call and its return. On false it is CancellationToken.None. There is no separate "current
    /// revocation" member: reading the two apart would let one request survive a reset.
    /// </summary>
    bool TryVerify(ReadOnlySpan<char> token, out CancellationToken revoked);

    /// <summary>Protocol 5.5: true when <paramref name="headerValue"/> equals the nonce of the self-check
    /// in progress (constant time). False when no check is in progress.</summary>
    bool IsSelfCheckNonce(ReadOnlySpan<char> headerValue);

    /// <summary>The edge reports that the self-check request arrived, and whether it carried
    /// Tailscale-Funnel-Request.</summary>
    void NoteSelfCheckArrival(bool throughFunnel);

    Task<PairingQr> ShowAsync(CancellationToken ct);

    Task<PairingQr> ResetAsync(CancellationToken ct);

    Task<string> GetPayloadAsync(CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Pairing/IPairingProtector.cs
namespace LiveCue.Relay.Pairing;

/// <summary>DPAPI seam (architecture 3.6). Failures throw <see cref="PairingProtectionException"/>.</summary>
internal interface IPairingProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> protectedBytes);
}

/// <summary>Fixed message. The cause is never attached as InnerException.</summary>
internal sealed class PairingProtectionException : Exception
{
    public PairingProtectionException()
        : base("Protected pairing storage is not available.")
    {
    }
}
```

```csharp
// file: windows/src/LiveCue.Relay/Pairing/IPairingFiles.cs
namespace LiveCue.Relay.Pairing;

/// <summary>File-system seam of the pairing store. Paths are absolute.</summary>
internal interface IPairingFiles
{
    bool Exists(string path);

    /// <summary>Throws IOException or UnauthorizedAccessException like File.ReadAllBytes.</summary>
    byte[] ReadAllBytes(string path);

    /// <summary>Writes path + ".pending", flushes it to disk, then replaces <paramref name="path"/> in one
    /// rename. Either the old or the new content exists afterwards, never a mixture.</summary>
    void WriteAtomic(string path, ReadOnlySpan<byte> contents);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Pairing/ISelfCheckClient.cs
namespace LiveCue.Relay.Pairing;

/// <summary>Self-check seam (protocol 4.5).</summary>
internal interface ISelfCheckClient
{
    /// <summary>
    /// One GET with the three headers of protocol 4.5, redirects refused, bounded by <paramref name="ct"/>.
    /// True only for status 200 whose body has ok true and protocol 2. Every failure is false; only
    /// cancellation of <paramref name="ct"/> by the caller throws.
    /// </summary>
    Task<bool> GetHealthAsync(Uri healthUrl, string token, string nonce, CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Catalog/ICatalogSource.cs
namespace LiveCue.Relay.Catalog;

/// <summary>What is kept of one entry of Codex's model list (protocol 12.1).</summary>
internal sealed record RawCatalogEntry(
    string Model,
    string? DisplayName,
    bool Hidden,
    IReadOnlyList<string> ReasoningEfforts);

/// <summary>Thrown for one failed read attempt. Fixed message.</summary>
internal sealed class CatalogReadException : Exception
{
    public CatalogReadException()
        : base("The catalogue could not be read.")
    {
    }
}

/// <summary>Catalogue seam: one read attempt of the raw list.</summary>
internal interface ICatalogSource
{
    /// <summary>Throws <see cref="CatalogReadException"/> on any failure.</summary>
    Task<IReadOnlyList<RawCatalogEntry>> ReadAsync(CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Catalog/ICatalogReader.cs
namespace LiveCue.Relay.Catalog;

/// <summary>An offered model (protocol 7.1). ReasoningEfforts is never empty.</summary>
internal sealed record ModelOffer(string Id, string Name, IReadOnlyList<string> ReasoningEfforts);

/// <summary>The catalogue with retry, last-good and isolation (protocol 7.2).</summary>
internal interface ICatalogReader
{
    /// <summary>
    /// Returns a list the caller owns. Throws RelayException(ASSISTANT_CATALOG_UNAVAILABLE) when three
    /// attempts failed and there has never been a good catalogue. <paramref name="ct"/> ends the caller's
    /// wait only; a shared read in progress continues for the other callers.
    /// </summary>
    Task<IReadOnlyList<ModelOffer>> ReadAsync(CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Answers/IAnswerBackend.cs
namespace LiveCue.Relay.Answers;

/// <param name="RequestBodyUtf8">The request body as compact JSON; embedded whole in the prompt.</param>
internal sealed record AnswerRun(
    AnswerKind Kind,
    string RequestId,
    string Model,
    string ReasoningEffort,
    ReadOnlyMemory<byte> RequestBodyUtf8);

/// <summary>Answer seam: one run of the answer process.</summary>
internal interface IAnswerBackend
{
    /// <summary>
    /// Returns the bytes of the output file, unvalidated, or null when the run produced no usable file
    /// (could not start, non-zero exit, no file, larger than C-24). When <paramref name="ct"/> fires, the
    /// process tree is killed and OperationCanceledException is thrown. The working folder is gone when the
    /// task completes, whatever the outcome.
    /// </summary>
    Task<byte[]?> RunAsync(AnswerRun run, CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Answers/IAnswerCoordinator.cs
using LiveCue.Relay.Edge;

namespace LiveCue.Relay.Answers;

/// <summary>Routes 7, 8 and 9 behind one object: the slot, the registry and the check order R-1 to R-8.</summary>
internal interface IAnswerCoordinator
{
    /// <summary>Handles one assist or summary request to its response and its desktop events.</summary>
    Task HandleAsync(RouteContext context, AnswerKind kind);

    /// <summary>Route 9: kills the run with that id. False when no request with that id is in flight.</summary>
    bool TryCancel(string requestId);

    /// <summary>Number of requests holding the slot: 0 or 1.</summary>
    int InFlight { get; }
}
```

```csharp
// file: windows/src/LiveCue.Relay/LocalModel/ILabRuntime.cs
namespace LiveCue.Relay.LocalModel;

/// <summary>The four members of the lab's health reply that the relay uses (protocol 11.2).</summary>
internal sealed record LabHealth(bool Ready, string? Model, bool Busy, int RelayProtocol);

/// <summary>Lab seam: probe, start, stop (protocol 9.2). Never throws except for cancellation.</summary>
internal interface ILabRuntime
{
    /// <summary>Null means "no lab": any failure, time-out or unreadable reply.</summary>
    Task<LabHealth?> ProbeAsync(CancellationToken ct);

    /// <summary>Runs the start script for one of C-04. True for exit code 0.</summary>
    Task<bool> StartAsync(string model, CancellationToken ct);

    /// <summary>Runs the stop script. True for exit code 0.</summary>
    Task<bool> StopAsync(CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Relay/LocalModel/ILocalModelController.cs
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.LocalModel;

/// <summary>Result of a start or stop command. Exactly one of Refusal and Status is set.</summary>
/// <param name="Began">True: an operation began (202). False with a Status: nothing needed doing (200).</param>
internal readonly record struct LocalModelCommand(RelayError? Refusal, LocalModelStatus? Status, bool Began);

internal enum PrepareOutcome
{
    Ready,

    /// <summary>The lab reports a running session (MODEL_BUSY).</summary>
    Busy,

    /// <summary>Another operation began between step S-5 and this call (MODEL_OPERATION_IN_PROGRESS).</summary>
    OperationInProgress,

    /// <summary>The load this call waited for failed (MODEL_LOAD_FAILED).</summary>
    LoadFailed,
}

/// <summary>Protocol 9.3. The caller has already validated the model name against C-04.</summary>
internal interface ILocalModelController
{
    /// <summary>True when a lab folder is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>True while a start or stop operation runs (C-35; steps M-5 and S-5).</summary>
    bool OperationRunning { get; }

    /// <summary>Route 4. Never fails for lab reasons.</summary>
    Task<LocalModelStatus> GetStatusAsync(CancellationToken ct);

    /// <summary>Steps M-4 to M-6 and rule 5. The operation it begins is not bound to <paramref name="ct"/>.</summary>
    Task<LocalModelCommand> StartAsync(string model, CancellationToken ct);

    /// <summary>Steps M-4 to M-7 and rule 6.</summary>
    Task<LocalModelCommand> StopAsync(string model, CancellationToken ct);

    /// <summary>
    /// Rule 7. <paramref name="ct"/> ends the caller's wait only: an operation that was begun continues
    /// as an ordinary operation (decision A-07). The caller checks OperationRunning before calling.
    /// </summary>
    Task<PrepareOutcome> PrepareAsync(string model, CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Speech/ILabSocketConnector.cs
namespace LiveCue.Relay.Speech;

internal enum LabMessageKind
{
    /// <summary>A text message. Bytes holds it.</summary>
    Text,

    /// <summary>The lab closed the socket (a close frame, or the connection ended).</summary>
    Closed,

    /// <summary>A binary message, or a message larger than C-23.</summary>
    Invalid,
}

internal readonly record struct LabMessage(LabMessageKind Kind, ReadOnlyMemory<byte> Bytes);

/// <summary>Thrown when the lab socket cannot be opened or fails. Fixed message.</summary>
internal sealed class LabSocketException : Exception
{
    public LabSocketException()
        : base("The lab socket failed.")
    {
    }
}

/// <summary>One open socket to the lab's /relay endpoint (protocol 11.3). One sender and one receiver.</summary>
internal interface ILabSocket : IAsyncDisposable
{
    ValueTask SendAudioAsync(ReadOnlyMemory<byte> frame, CancellationToken ct);

    /// <summary>Sends exactly {"type":"endStream"} as a text message.</summary>
    ValueTask SendEndStreamAsync(CancellationToken ct);

    /// <summary>Throws <see cref="LabSocketException"/> on a transport failure.</summary>
    ValueTask<LabMessage> ReceiveAsync(CancellationToken ct);
}

/// <summary>Lab socket seam.</summary>
internal interface ILabSocketConnector
{
    /// <summary>Opens the socket within C-47. Throws <see cref="LabSocketException"/> on any failure.</summary>
    Task<ILabSocket> ConnectAsync(CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Relay/Speech/ISpeechBridge.cs
using System.Net.WebSockets;

namespace LiveCue.Relay.Speech;

/// <summary>What the speech route hands over after the upgrade.</summary>
/// <param name="ProtocolHeader">Value of X-LiveCue-Protocol, or null when absent or repeated.</param>
/// <param name="ModelHeader">Value of X-LiveCue-Speech-Model, or null when absent or repeated.</param>
/// <param name="UpgradeTimestamp">Clock timestamp of the upgrade; C-51 and C-55 count from it.</param>
internal sealed record SpeechConnection(
    WebSocket Socket,
    string? ProtocolHeader,
    string? ModelHeader,
    long UpgradeTimestamp,
    CancellationToken PairingRevoked);

/// <summary>Protocol section 10 from admission to the last close.</summary>
internal interface ISpeechBridge
{
    /// <summary>
    /// Runs one accepted socket: admission S-1 to S-6, the stream, the close. Completes when the phone
    /// socket and the lab socket are both closed and the lease is released. Never throws.
    /// </summary>
    Task RunAsync(SpeechConnection connection);
}
```

**Composition.** `Core/RelayComposition.cs` is the one place that constructs areas. These constructor
signatures are part of the contract; the foundation's stub classes have them, and a package that replaces a
stub keeps them.

| Class | Constructor |
|---|---|
| `Pairing.PairingService` | `(RelayContext context)` |
| `Catalog.CatalogReader` | `(RelayContext context)`; builds `CachedCatalogSource` over `Dependencies.CatalogSource ?? new CodexCatalogSource(context)` |
| `Answers.AnswerCoordinator` | `(RelayContext context, ICatalogReader catalog)`; backend is `Dependencies.AnswerBackend ?? new CodexAnswerBackend(context)` |
| `LocalModel.LocalModelController` | `(RelayContext context, SpeechLease lease)`; runtime is `Dependencies.LabRuntime ?? new LabRuntime(context)`. The composition uses `Dependencies.LocalModelController` instead when a test set it |
| `Speech.SpeechBridge` | `(RelayContext context, ILocalModelController localModel, SpeechLease lease)`; connector is `Dependencies.LabSocketConnector ?? new LabSocketConnector(context)` |
| `Routes.HealthRoute`, `Routes.PairVerifyRoute` | `(RelayContext context)` |
| `Routes.ModelsRoute` | `(RelayContext context, ICatalogReader catalog)` |
| `Routes.AssistRoute`, `Routes.SummaryRoute`, `Routes.CancelRoute` | `(IAnswerCoordinator answers)` |
| `Routes.LocalModelStatusRoute`, `Routes.LocalModelStartRoute`, `Routes.LocalModelStopRoute` | `(RelayContext context, ILocalModelController localModel)` |
| `Routes.SpeechRoute` | `(RelayContext context, ISpeechBridge bridge)` |
| `Edge.EdgePipeline` | `(RelayContext context, RelayServices services)` |

`Core.RelayServices` is a sealed class with `required init` properties `Pairing`, `Catalog`, `Answers`,
`LocalModel`, `Speech`, `Lease` and `Routes` (`IReadOnlyDictionary<RouteId, IRouteHandler>`).

### 3.4 Hosting: settings

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/RelaySettings.cs
using LiveCue.Relay;

namespace LiveCue.Hosting;

/// <summary>One entry of the ledger of architecture 3.9: a mapping this application created.</summary>
/// <param name="Path">"/" for Private; the path prefix for Public.</param>
/// <param name="Target">Always http://127.0.0.1:&lt;relay port at creation&gt;.</param>
public sealed record OwnedMapping(
    ConnectionMode Mode,
    int HttpsPort,
    string Path,
    string Target,
    DateTimeOffset CreatedAt);

/// <summary>
/// The content of settings.json (architecture 3.5). Immutable; "with" produces changed copies. The default
/// instance is what a missing file means. Compare two instances with <see cref="SameValues"/>, because the
/// ledger is a list.
/// </summary>
public sealed record RelaySettings
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>The old relay's port. Never accepted, offered or suggested anywhere.</summary>
    public const int ForbiddenPort = 47831;

    /// <summary>The lab's port inside its container; not usable as a host port setting.</summary>
    public const int LabContainerPort = 8765;

    public static RelaySettings Default { get; } = new();

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public int RelayPort { get; init; } = 47841;

    public ConnectionMode ConnectionMode { get; init; } = ConnectionMode.Private;

    public int PrivateHttpsPort { get; init; } = 11443;

    public int PublicHttpsPort { get; init; } = 10000;

    public string PublicPathPrefix { get; init; } = "/livecue";

    public string? CodexExecutablePath { get; init; }

    /// <summary>The Codex version text (architecture 3.7) for which the owner last confirmed the live
    /// containment check, or null. Written only by "Mark as checked" in the Assistant settings.</summary>
    public string? CodexContainmentCheckedVersion { get; init; }

    public string? AsrLabFolder { get; init; }

    public int AsrLabPort { get; init; } = 47842;

    /// <summary>True: the desktop window is excluded from screen capture (architecture 3.5).</summary>
    public bool HideFromScreenCapture { get; init; } = true;

    public IReadOnlyList<OwnedMapping> OwnedMappings { get; init; } = [];

    /// <summary>Member-wise comparison, the ledger compared entry by entry in order.</summary>
    public bool SameValues(RelaySettings other)
        => other is not null
            && this with { OwnedMappings = [] } == other with { OwnedMappings = [] }
            && OwnedMappings.SequenceEqual(other.OwnedMappings);
}
```

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/SettingsValidation.cs
namespace LiveCue.Hosting;

public enum SettingsField
{
    SchemaVersion,
    RelayPort,
    ConnectionMode,
    PrivateHttpsPort,
    PublicHttpsPort,
    PublicPathPrefix,
    CodexExecutablePath,
    CodexContainmentCheckedVersion,
    AsrLabFolder,
    AsrLabPort,
    HideFromScreenCapture,
    OwnedMappings,
}

public enum SettingsProblemCode
{
    /// <summary>schemaVersion is not 1.</summary>
    UnsupportedVersion,

    /// <summary>A number outside its range.</summary>
    OutOfRange,

    /// <summary>Port 47831, or 8765 for the lab port.</summary>
    Forbidden,

    /// <summary>Equal to the other port it must differ from.</summary>
    Duplicate,

    /// <summary>Not one of the allowed values (mode, public port); a private port of 443, 8443 or 10000;
    /// a lab folder that is not a drive-letter path on a fixed local volume (UNC, device namespace,
    /// relative, drive-relative, network or removable drive).</summary>
    NotAllowed,

    /// <summary>Does not match its pattern as a whole text (prefix, ledger entry, Codex version text).</summary>
    BadFormat,

    /// <summary>Not an absolute path, or the file or folder does not exist.</summary>
    NotFound,

    /// <summary>The file is not named codex.exe.</summary>
    WrongFileName,

    /// <summary>The folder lacks one of the two launcher scripts.</summary>
    MissingScripts,
}

public sealed record SettingsProblem(SettingsField Field, SettingsProblemCode Code);

/// <summary>The rules of architecture 3.5 (table in section 4.14 of the W-design). Pure apart from the
/// file-system probes for the two paths.</summary>
public interface ISettingsValidator
{
    /// <summary>Every rule. Used before a save. Empty when valid. Order follows <see cref="SettingsField"/>.</summary>
    IReadOnlyList<SettingsProblem> Validate(RelaySettings settings);

    /// <summary>The rules that need no file system (everything except NotFound, MissingScripts and the
    /// fixed-local-volume half of the lab folder rule; the path-form half is checked here). Used when the
    /// file is loaded, so that a folder deleted later does not invalidate the whole file.</summary>
    IReadOnlyList<SettingsProblem> ValidateStructure(RelaySettings settings);

    /// <summary>True when one ledger entry passes the ownedMappings rule by itself. Used to keep the
    /// valid entries of a file that failed validation as a whole (architecture 3.5).</summary>
    bool IsValidLedgerEntry(OwnedMapping entry);
}
```

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/ISettingsStore.cs
namespace LiveCue.Hosting;

public enum SettingsFileState
{
    /// <summary>No file: defaults apply.</summary>
    Missing,
    Loaded,

    /// <summary>The file parsed but failed validation. It is left untouched; defaults apply, except
    /// that every ledger entry of the file that is valid by itself is kept.</summary>
    Invalid,

    /// <summary>The file could not be read or is not the expected JSON. Left untouched; defaults apply.</summary>
    Unreadable,
}

/// <param name="Settings">What to run on: the file's values when Loaded; otherwise the defaults, and for
/// Invalid the defaults with OwnedMappings set to the file's entries that are valid by themselves.</param>
public sealed record SettingsLoadResult(
    RelaySettings Settings,
    SettingsFileState State,
    IReadOnlyList<SettingsProblem> Problems);

/// <summary>Thrown by Save for settings that fail validation.</summary>
public sealed class SettingsValidationException : Exception
{
    public SettingsValidationException(IReadOnlyList<SettingsProblem> problems)
        : base("The settings are not valid.")
    {
        Problems = problems;
    }

    public IReadOnlyList<SettingsProblem> Problems { get; }
}

/// <summary>Owns settings.json. Not thread-safe for concurrent Save calls; the supervisor and the desktop
/// session serialise them.</summary>
public interface ISettingsStore
{
    /// <summary>Never throws.</summary>
    SettingsLoadResult Load();

    /// <summary>
    /// Validates, then writes atomically (write settings.json.pending, then replace). Throws
    /// <see cref="SettingsValidationException"/> or <see cref="IOException"/>. A failed save leaves the
    /// previous file intact. When the file on disk is Invalid or Unreadable, its bytes are first copied to
    /// settings.json.invalid (replacing an older copy); if that copy fails, nothing is written.
    /// </summary>
    void Save(RelaySettings settings);
}
```

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/IHostFileSystem.cs
namespace LiveCue.Hosting;

public sealed record FoundFile(string Path, DateTime LastWriteTimeUtc);

/// <summary>File-system and environment seam of the hosting library.</summary>
public interface IHostFileSystem
{
    /// <summary>The folder of the running executable (AppContext.BaseDirectory).</summary>
    string BaseDirectory { get; }

    /// <summary>The Program Files folder from the known-folder API
    /// (Environment.GetFolderPath(SpecialFolder.ProgramFiles)), never from an environment variable.</summary>
    string ProgramFilesDirectory { get; }

    /// <summary>True when <paramref name="path"/> starts with a drive letter, a colon and a backslash and
    /// that drive is a fixed local volume (DriveType.Fixed). False for every other form and on any error.</summary>
    bool IsOnFixedLocalDrive(string path);

    string? GetEnvironmentVariable(string name);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    byte[] ReadAllBytes(string path);

    /// <summary>Writes path + ".pending", flushes, then replaces the file in one rename.</summary>
    void WriteAllBytesAtomic(string path, byte[] contents);

    /// <summary>Files named exactly <paramref name="fileName"/> (ordinal, ignoring case) under
    /// <paramref name="directory"/>, at most <paramref name="maxDepth"/> levels below it. Never throws;
    /// folders that cannot be read are skipped.</summary>
    IReadOnlyList<FoundFile> FindFiles(string directory, string fileName, int maxDepth);
}
```

### 3.5 Hosting: prerequisites and Codex

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/Codex.cs
namespace LiveCue.Hosting;

public enum CodexSource { None, Setting, SearchPath, UserInstall }

public enum CodexProblem
{
    None,

    /// <summary>codexExecutablePath is set and the file is missing. Not a fall-through.</summary>
    ConfiguredFileMissing,
    NotFound,
}

public sealed record CodexLocation(CodexSource Source, string? Path, CodexProblem Problem);

/// <summary>Discovery order of architecture 3.7. Only a file named exactly codex.exe is ever returned.</summary>
public interface ICodexLocator
{
    CodexLocation Locate(string? configuredPath);
}

/// <summary>The two questions asked of the Codex executable itself (architecture 3.7). Both run it with
/// the CodexAllowlist environment.</summary>
public interface ICodexSignInCheck
{
    /// <summary>Runs "&lt;codex&gt; login status" (limit in architecture 3.7); its output is discarded.
    /// True for exit code 0. False for anything else, including a time-out.</summary>
    Task<bool> IsSignedInAsync(string codexPath, CancellationToken ct);

    /// <summary>
    /// Runs "&lt;codex&gt; --version" (limit 15 s, at most 256 bytes of output kept). Returns the first
    /// line, trimmed, when the exit code is 0 and the line is 1 to 64 characters of letters, digits,
    /// space, '.', '_', '+' and '-'. Null for anything else. This is the only Codex output that is ever
    /// kept or shown. Never throws except for cancellation.
    /// </summary>
    Task<string?> ReadVersionAsync(string codexPath, CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/Prerequisites.cs
namespace LiveCue.Hosting;

/// <summary>Checks 1 to 6 of architecture 3.11, numbered as there.</summary>
public enum PrerequisiteId
{
    DataFolder = 1,
    CodexFound = 2,
    CodexSignedIn = 3,
    TailscaleInstalled = 4,
    TailscaleOnline = 5,
    TailscaleDnsName = 6,
}

public enum PrerequisiteState
{
    Passed,
    Failed,

    /// <summary>Not evaluated because a check it depends on failed, or because it was skipped on request.</summary>
    NotChecked,
}

/// <param name="Detail">For a passed CodexFound: the path. For a passed TailscaleDnsName: the name.
/// Otherwise null. Never tool output.</param>
public sealed record PrerequisiteResult(PrerequisiteId Id, PrerequisiteState State, string? Detail);

/// <param name="CodexPathOverride">Console host --codex. Null means the setting.</param>
/// <param name="SkipTailscale">Console host --endpoint: checks 4 to 6 are NotChecked.</param>
public sealed record PrerequisiteOptions(
    string DataDirectory,
    string? CodexPathOverride = null,
    bool SkipTailscale = false);

/// <summary>All six results, always in id order.</summary>
/// <param name="CodexVersion">The version text of the Codex that was found (ICodexSignInCheck.ReadVersionAsync),
/// or null when no Codex was found or its version could not be read.</param>
public sealed record PrerequisiteReport(
    IReadOnlyList<PrerequisiteResult> Results,
    SettingsLoadResult SettingsLoad,
    string? CodexExecutablePath,
    string? DnsName,
    string? CodexVersion = null)
{
    /// <summary>The first failed check in id order, or null.</summary>
    public PrerequisiteResult? FirstFailure => Results.FirstOrDefault(r => r.State == PrerequisiteState.Failed);

    public bool Passed => FirstFailure is null;

    /// <summary>
    /// True when a Codex was found and its version is unknown or is not the version of the last
    /// containment check (settings, CodexContainmentCheckedVersion; ordinal comparison). A warning only:
    /// it never makes a check fail and nothing is refused because of it (architecture 3.7).
    /// </summary>
    public bool ContainmentCheckIsStale =>
        CodexExecutablePath is not null
        && (CodexVersion is null
            || !string.Equals(CodexVersion, SettingsLoad.Settings.CodexContainmentCheckedVersion, StringComparison.Ordinal));
}

/// <summary>
/// Evaluates the checks in order, each at most once. Dependencies: every check needs 1; 3 needs 2; 5 needs
/// 4; 6 needs 5. A check whose dependency failed is NotChecked. Blocking calls run on the thread pool.
/// Never throws except for cancellation.
/// </summary>
public interface IPrerequisiteChecker
{
    Task<PrerequisiteReport> CheckAsync(PrerequisiteOptions options, CancellationToken ct);
}
```

### 3.6 Hosting: Tailscale and the mapping manager

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/Tailscale.cs
using System.Globalization;
using System.Text.RegularExpressions;

namespace LiveCue.Hosting;

/// <summary>What is read from "tailscale status --json".</summary>
/// <param name="Online">BackendState is "Running" and Self.Online is true.</param>
/// <param name="DnsName">Self.DNSName without its trailing dot, lower-cased; null when empty or when it
/// fails the host rule of protocol 4.3.</param>
public sealed record TailscaleNodeStatus(bool Online, string? DnsName);

public enum TailscaleReadFailure { None, NotInstalled, Failed, TimedOut, Unparseable }

/// <summary>Exactly one of Value and Failure is meaningful.</summary>
public sealed record TailscaleRead<T>(T? Value, TailscaleReadFailure Failure)
    where T : class;

/// <summary>One web handler of the serve configuration.</summary>
/// <param name="Port">The port after the last colon of the "host:port" key.</param>
/// <param name="Mount">The handler's mount point, for example "/" or "/livecue".</param>
/// <param name="Kind">"Proxy", "Path", "Text", "Redirect", or "Other".</param>
/// <param name="Target">The value of that member, for example "http://127.0.0.1:47841".</param>
/// <param name="Foreground">True when the handler belongs to a foreground session or a service.</param>
public sealed record ServeHandler(int Port, string Mount, string Kind, string Target, bool Foreground);

/// <summary>
/// "tailscale serve status --json", read as architecture 3.9 describes. Leaves is the whole document
/// flattened to (JSON pointer, compact JSON of the leaf), sorted by pointer; rule TS-7 compares it.
/// </summary>
public sealed record ServeSnapshot(
    IReadOnlyList<ServeHandler> Handlers,
    IReadOnlyCollection<int> MentionedPorts,
    IReadOnlyCollection<int> TcpForwardPorts,
    IReadOnlyCollection<int> FunnelPorts,
    IReadOnlyList<KeyValuePair<string, string>> Leaves);

public enum MappingCommandKind { ApplyPrivate, ApplyPublic, RemovePrivate, RemovePublic }

/// <summary>
/// One of the four write commands of architecture 3.9 and nothing else (rules TS-2, TS-3, TS-6).
/// Instances exist only through the four factories, which validate every value. Complete as written.
/// </summary>
public sealed partial class MappingCommand
{
    private MappingCommand(MappingCommandKind kind, int httpsPort, string path, int relayPort, string[] arguments)
    {
        Kind = kind;
        HttpsPort = httpsPort;
        Path = path;
        RelayPort = relayPort;
        Arguments = arguments;
    }

    public MappingCommandKind Kind { get; }

    public int HttpsPort { get; }

    /// <summary>"/" for the private forms; the prefix for the public forms.</summary>
    public string Path { get; }

    /// <summary>The relay port of the target; 0 for the remove forms.</summary>
    public int RelayPort { get; }

    /// <summary>The arguments after the executable, in order.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>"tailscale " + the arguments joined by single spaces. This exact text is what the user is
    /// shown before the command runs.</summary>
    public string DisplayText => "tailscale " + string.Join(' ', Arguments);

    public static string TargetFor(int relayPort)
        => "http://127.0.0.1:" + relayPort.ToString(CultureInfo.InvariantCulture);

    public static MappingCommand ApplyPrivate(int privateHttpsPort, int relayPort)
    {
        RequirePrivatePort(privateHttpsPort);
        RequireRelayPort(relayPort);
        return new(MappingCommandKind.ApplyPrivate, privateHttpsPort, "/", relayPort,
            ["serve", "--bg", "--https=" + Text(privateHttpsPort), TargetFor(relayPort)]);
    }

    public static MappingCommand ApplyPublic(int publicHttpsPort, string prefix, int relayPort)
    {
        RequireFunnelPort(publicHttpsPort);
        RequirePrefix(prefix);
        RequireRelayPort(relayPort);
        return new(MappingCommandKind.ApplyPublic, publicHttpsPort, prefix, relayPort,
            ["funnel", "--bg", "--https=" + Text(publicHttpsPort), "--set-path=" + prefix, TargetFor(relayPort)]);
    }

    public static MappingCommand RemovePrivate(int privateHttpsPort)
    {
        RequirePort(privateHttpsPort, 1);
        return new(MappingCommandKind.RemovePrivate, privateHttpsPort, "/", 0,
            ["serve", "--https=" + Text(privateHttpsPort), "--set-path=/", "off"]);
    }

    public static MappingCommand RemovePublic(int publicHttpsPort, string prefix)
    {
        RequireFunnelPort(publicHttpsPort);
        RequirePrefix(prefix);
        return new(MappingCommandKind.RemovePublic, publicHttpsPort, prefix, 0,
            ["funnel", "--https=" + Text(publicHttpsPort), "--set-path=" + prefix, "off"]);
    }

    // \A and \z, not ^ and $: "$" also matches before a final line feed, which would let a prefix that ends
    // in a line break reach the command line and the confirmation dialog (protocol section 1, shapes).
    [GeneratedRegex(@"\A/[a-z0-9][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void RequirePort(int port, int minimum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, minimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
    }

    // A tailnet-only listener is never created on a port on which Funnel can be switched on
    // (architecture 3.5 and TS-4). RemovePrivate keeps RequirePort: removing is always allowed.
    private static void RequirePrivatePort(int port)
    {
        RequirePort(port, 1);
        if (port is 443 or 8443 or 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
    }

    private static void RequireRelayPort(int relayPort)
    {
        RequirePort(relayPort, 1024);
        ArgumentOutOfRangeException.ThrowIfEqual(relayPort, RelaySettings.ForbiddenPort);
    }

    private static void RequireFunnelPort(int port)
    {
        if (port is not (443 or 8443 or 10000))
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
    }

    private static void RequirePrefix(string prefix)
    {
        if (prefix is null || !PrefixPattern().IsMatch(prefix))
        {
            throw new ArgumentException("The path prefix is not valid.", nameof(prefix));
        }
    }
}

public enum TailscaleWriteOutcome { Succeeded, Failed, TimedOut, NotInstalled }

/// <summary>
/// The Tailscale CLI: two read commands and the four write commands. Every call is limited as rule TS-10
/// says. Output is discarded except for the two JSON status commands. Never throws except for cancellation.
/// </summary>
public interface ITailscaleCli
{
    /// <summary>Check 4 of architecture 3.11: the resolved path, or null when Tailscale is not installed.</summary>
    string? ExecutablePath { get; }

    /// <summary>tailscale status --json</summary>
    Task<TailscaleRead<TailscaleNodeStatus>> ReadStatusAsync(CancellationToken ct);

    /// <summary>tailscale serve status --json</summary>
    Task<TailscaleRead<ServeSnapshot>> ReadServeStatusAsync(CancellationToken ct);

    /// <summary>Runs one write command. Called by the mapping manager only.</summary>
    Task<TailscaleWriteOutcome> RunAsync(MappingCommand command, CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/Mapping.cs
using LiveCue.Relay;

namespace LiveCue.Hosting;

public enum MappingState
{
    /// <summary>The serve configuration could not be read.</summary>
    Unknown,

    /// <summary>No handler at the configured port and mount; it can be added.</summary>
    Absent,

    /// <summary>Our handler exists and is in the ledger.</summary>
    Present,

    /// <summary>A handler with our port, mount and target exists but is not in the ledger. Usable; never
    /// removed by this application (TS-8).</summary>
    PresentNotOwned,

    /// <summary>It cannot be added; BlockedBy says why.</summary>
    Blocked,
}

/// <summary>Why the manager will not run a command. Texts are in section 4.15 of the W-design.</summary>
public enum MappingRefusal
{
    None,
    TailscaleNotInstalled,

    /// <summary>TS-1: the status command failed or timed out.</summary>
    StatusUnavailable,

    /// <summary>TS-1: the status JSON does not have the expected shape.</summary>
    StatusUnreadable,

    /// <summary>TS-2: a port or the prefix fails the settings rules.</summary>
    InvalidSettings,

    /// <summary>TS-6.</summary>
    ForbiddenRelayPort,

    /// <summary>TS-4: the private port is occupied by something that is not our handler alone.</summary>
    PrivatePortOccupied,

    /// <summary>TS-4: the private port has Funnel on.</summary>
    PrivatePortHasFunnel,

    /// <summary>TS-5: the mount, or the mount with a trailing slash, exists and is not ours.</summary>
    PublicMountOccupied,

    /// <summary>TS-5: the public port forwards raw TCP.</summary>
    PublicPortHasTcpForward,

    /// <summary>TS-5: the public port is occupied and Funnel is off for it.</summary>
    PublicPortWouldPublishOthers,

    /// <summary>Apply: our handler is already there. Nothing is run.</summary>
    AlreadyPresent,

    /// <summary>Remove: there is no handler at that port and mount.</summary>
    NotPresent,

    /// <summary>TS-8: the handler is not in the ledger.</summary>
    NotInLedger,

    /// <summary>TS-8: the ledger entry and the live handler differ.</summary>
    LedgerMismatch,

    /// <summary>The plan no longer matches the current settings; the user must look at the command again.</summary>
    PlanOutOfDate,
}

public enum MappingAction { ApplyPrivate, ApplyPublic, RemovePrivate, RemovePublic }

/// <summary>State of one mode's mapping for the current settings.</summary>
public sealed record MappingInspection(
    ConnectionMode Mode,
    MappingState State,
    MappingRefusal BlockedBy,
    bool InLedger,
    int HttpsPort,
    string Path);

/// <summary>
/// One row of the exposure report (architecture 3.9): a live handler, on any port and mount and in any
/// structure of the serve status, whose kind is Proxy and whose target is exactly
/// MappingCommand.TargetFor(relay port). It is what Tailscale forwards to the relay, whoever created it.
/// </summary>
/// <param name="FunnelOn">Funnel is on for that port: the handler is reachable from the internet.</param>
/// <param name="Foreground">It belongs to a foreground session or a service.</param>
/// <param name="LedgerEntry">The ledger entry with the same mode, port, path and target, or null.</param>
public sealed record ExposedHandler(
    int HttpsPort,
    string Mount,
    bool FunnelOn,
    bool Foreground,
    OwnedMapping? LedgerEntry);

/// <summary>Result of one read-only inspection.</summary>
/// <param name="StaleOwned">Ledger entries that still match a live handler exactly and are not the selected
/// mode's mapping at its current port and path: the other mode's mapping, or one left at an old port.
/// Each is a standing warning with "Remove".</param>
/// <param name="StatusFailure">None when the serve status was read.</param>
/// <param name="Exposure">The exposure report, sorted by port and then mount (ordinal). Empty when the
/// serve status could not be read: StatusFailure then says that exposure is unknown, not absent.</param>
public sealed record ConnectionInspection(
    MappingInspection Private,
    MappingInspection Public,
    IReadOnlyList<OwnedMapping> StaleOwned,
    TailscaleReadFailure StatusFailure,
    DateTimeOffset At,
    IReadOnlyList<ExposedHandler> Exposure)
{
    public MappingInspection For(ConnectionMode mode) => mode == ConnectionMode.Private ? Private : Public;

    /// <summary>True when some handler that forwards to the relay port is published through Funnel. In
    /// Private mode the supervisor does not start the relay while this is true (architecture 3.11, 6a).</summary>
    public bool HasFunnelExposure => Exposure.Any(handler => handler.FunnelOn);

    /// <summary>The rows that are not in the ledger: shown with the removal command as text, never removed
    /// by the application (TS-8).</summary>
    public IEnumerable<ExposedHandler> UnownedExposure => Exposure.Where(handler => handler.LedgerEntry is null);
}

/// <summary>What a confirmation dialog shows and what will run if it is confirmed.</summary>
/// <param name="EffectText">One sentence in plain language (section 4.15 of the W-design).</param>
/// <param name="Entry">For a removal: the ledger entry to remove. Null for an apply.</param>
public sealed record MappingPlan(
    MappingAction Action,
    MappingCommand Command,
    string EffectText,
    OwnedMapping? Entry);

public enum MappingOutcome
{
    /// <summary>The command ran, the result was verified and the ledger was saved.</summary>
    Done,

    /// <summary>Nothing was run. Refusal says why.</summary>
    Refused,
    CommandFailed,

    /// <summary>TS-10: the user is told to run the shown command in a terminal.</summary>
    CommandTimedOut,

    /// <summary>The command reported success but the re-read does not show the expected state.</summary>
    NotVerified,

    /// <summary>The mapping changed but settings.json could not be saved.</summary>
    LedgerNotSaved,
}

/// <param name="OthersChanged">TS-7: an entry that is not ours differs from the snapshot taken before.
/// Reported prominently; nothing is repaired.</param>
/// <param name="Settings">The settings after the action, with the ledger as saved.</param>
public sealed record MappingResult(
    MappingOutcome Outcome,
    MappingRefusal Refusal,
    bool OthersChanged,
    RelaySettings Settings,
    ConnectionInspection? After);

/// <summary>
/// Architecture 3.9. InspectAsync and Plan never write. ExecuteAsync is the only member that can run a
/// write command; it is called only from a confirmation dialog that showed plan.Command.DisplayText
/// (rule TS-9). Never throws except for cancellation.
/// </summary>
public interface IMappingManager
{
    /// <summary>Reads the serve status once and classifies both modes for <paramref name="settings"/>.</summary>
    Task<ConnectionInspection> InspectAsync(RelaySettings settings, CancellationToken ct);

    /// <summary>
    /// Pure. The apply command of <paramref name="mode"/> and its effect text, from settings alone. Returns
    /// null and a refusal (InvalidSettings or ForbiddenRelayPort) when no valid command exists.
    /// </summary>
    MappingPlan? PlanApply(ConnectionMode mode, RelaySettings settings, string? dnsName, out MappingRefusal refusal);

    /// <summary>Pure. The remove command for one ledger entry, built from the entry's own port and path.</summary>
    MappingPlan PlanRemoval(OwnedMapping entry, string? dnsName);

    /// <summary>
    /// TS-1: reads the status. Refuses with PlanOutOfDate when an apply plan differs from the plan of the
    /// settings saved now, or a removal's entry is no longer in the ledger. Applies TS-4, TS-5 or TS-8.
    /// Runs the command. Reads the status again and verifies the result. Updates and saves the ledger.
    /// Applies TS-7.
    /// </summary>
    Task<MappingResult> ExecuteAsync(MappingPlan plan, CancellationToken ct);
}
```

### 3.7 Hosting: endpoint, supervisor, composition

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/IEndpointResolver.cs
namespace LiveCue.Hosting;

/// <summary>Architecture 3.3: the advertised endpoint of a mode.</summary>
public interface IEndpointResolver
{
    /// <summary>
    /// Private: https://&lt;dns&gt;:&lt;privateHttpsPort&gt;/ . Public: https://&lt;dns&gt;:&lt;publicHttpsPort&gt;&lt;prefix&gt;/ .
    /// The port is always written. Returns the canonical form of protocol 4.3, or null when the result
    /// fails that grammar.
    /// </summary>
    string? Resolve(RelaySettings settings, string dnsName);
}
```

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/IRelaySupervisor.cs
using System.Threading.Channels;
using LiveCue.Relay;

namespace LiveCue.Hosting;

/// <summary>Creates relay instances. The production factory calls RelayFactory.Create; the desktop's
/// self-test and the tests substitute a scripted relay.</summary>
public interface IRelayFactory
{
    IRelay Create(RelayOptions options);
}

public enum SupervisorState { Stopped, Checking, Starting, Running, Stopping }

/// <summary>Per-run overrides of the console host (architecture 3.14). Null members mean "the setting".</summary>
public sealed record RelayOverrides(
    string? Endpoint = null,
    int? Port = null,
    string? CodexPath = null,
    string? LabFolder = null,
    ConnectionMode? Mode = null);

public sealed record SupervisorOptions(
    string DataDirectory,
    string RelayVersion,
    RelayOverrides? Overrides = null);

/// <summary>Everything a host observes, in order, on one bounded channel (W-01).</summary>
public abstract record HostEvent(DateTimeOffset At);

/// <summary>A start began: the checks are running.</summary>
public sealed record CheckingStarted(DateTimeOffset At) : HostEvent(At);

/// <summary>The checks finished, passed or not.</summary>
public sealed record PrerequisitesChecked(DateTimeOffset At, PrerequisiteReport Report) : HostEvent(At);

/// <summary>The checks passed and a relay instance is about to start. Hosts clear their activity here.</summary>
public sealed record RelayStarting(DateTimeOffset At, RelayOptions Options) : HostEvent(At);

/// <summary>One event of the current relay instance, unchanged.</summary>
public sealed record RelayEventForwarded(DateTimeOffset At, RelayEvent Event) : HostEvent(At);

/// <summary>A read-only mapping inspection finished (step 8 of architecture 3.11, or on demand).</summary>
public sealed record ConnectionInspected(DateTimeOffset At, ConnectionInspection Inspection) : HostEvent(At);

public enum StartOutcome
{
    Started,
    PrerequisiteFailed,
    RelayFault,
    AlreadyRunning,

    /// <summary>Check 6a of architecture 3.11: Private mode, and a handler with Funnel on forwards to the
    /// relay port. No relay instance was created. FailureText carries the status line.</summary>
    ExposedInPrivateMode,
}

/// <param name="FailureText">The status-line text of architecture 3.11 for the failure, or null.</param>
public sealed record StartResult(
    StartOutcome Outcome,
    PrerequisiteReport? Report,
    RelayFaultCode? Fault,
    string? FailureText);

/// <summary>
/// Architecture 3.3 and 3.11: one relay instance per start. Thread-safe; Start, Stop and
/// ResetStoredPairing are serialised. No member ever runs a Tailscale write command.
/// </summary>
public interface IRelaySupervisor : IAsyncDisposable
{
    SupervisorState State { get; }

    /// <summary>The running instance, or null. The desktop uses it for pause and pairing only.</summary>
    IRelay? Relay { get; }

    /// <summary>The settings of the last load: at construction, at each start and after each save.</summary>
    RelaySettings Settings { get; }

    /// <summary>The options of the running instance, or null. Lets a host tell whether saved settings
    /// differ from what the relay uses.</summary>
    RelayOptions? RunningOptions { get; }

    ChannelReader<HostEvent> Events { get; }

    /// <summary>
    /// Steps 1 to 8 of architecture 3.11, check 6a included. Never throws for an expected failure: the
    /// result and the events carry it. With State other than Stopped it returns AlreadyRunning and does
    /// nothing. For ExposedInPrivateMode it publishes ConnectionInspected with the inspection that showed
    /// the exposure, creates no relay and returns with State Stopped.
    /// </summary>
    Task<StartResult> StartAsync(CancellationToken ct);

    /// <summary>Stops and disposes the instance. Idempotent. Bounded by W-02 plus one second.</summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>Steps 1 to 6 only. Publishes PrerequisitesChecked. Does not change State.</summary>
    Task<PrerequisiteReport> CheckAsync(CancellationToken ct);

    /// <summary>Step 8 on demand. Publishes ConnectionInspected.</summary>
    Task<ConnectionInspection> InspectConnectionAsync(CancellationToken ct);

    /// <summary>Validates and saves, then reloads Settings. Does not restart the relay.</summary>
    void SaveSettings(RelaySettings settings);

    /// <summary>PairingMaintenance for the data folder. Throws InvalidOperationException unless Stopped.</summary>
    Task ResetStoredPairingAsync(CancellationToken ct);
}
```

```csharp
// file: windows/src/LiveCue.Hosting/Contracts/HostingFactory.cs
using LiveCue.Relay.Processes;

namespace LiveCue.Hosting;

/// <summary>The hosting library, composed. Both executables build exactly one.</summary>
public sealed record HostingServices(
    ISettingsStore Settings,
    ISettingsValidator Validator,
    IPrerequisiteChecker Prerequisites,
    ICodexLocator Codex,
    ITailscaleCli Tailscale,
    IMappingManager Mappings,
    IEndpointResolver Endpoints,
    IRelaySupervisor Supervisor);

/// <summary>Replacements for tests and for the desktop's self-test. Null means production.</summary>
public sealed record HostingOverrides
{
    public TimeProvider? Clock { get; init; }

    public IProcessRunner? Processes { get; init; }

    public IHostFileSystem? Files { get; init; }

    public ISettingsStore? Settings { get; init; }

    public IPrerequisiteChecker? Prerequisites { get; init; }

    public ITailscaleCli? Tailscale { get; init; }

    public IMappingManager? Mappings { get; init; }

    public IRelayFactory? RelayFactory { get; init; }
}

public static class HostingFactory
{
    /// <summary>Does no I/O beyond resolving the Tailscale executable path lazily on first use.</summary>
    public static HostingServices Create(SupervisorOptions options, HostingOverrides? overrides = null)
        => throw new NotImplementedException();
}
```

Constructor signatures that `HostingFactory.Create` relies on (stubs in the foundation, kept by WP-W06 and
WP-W07): `HostFileSystem()`; `SettingsValidator(IHostFileSystem files)`;
`SettingsStore(string dataDirectory, IHostFileSystem files, ISettingsValidator validator)`;
`CodexLocator(IHostFileSystem files)`; `CodexSignInCheck(IProcessRunner processes)`;
`TailscaleCli(IProcessRunner processes, IHostFileSystem files)`;
`PrerequisiteChecker(ISettingsStore settings, ICodexLocator codex, ICodexSignInCheck signIn, ITailscaleCli tailscale, IHostFileSystem files)`;
`EndpointResolver()`;
`MappingManager(ITailscaleCli tailscale, ISettingsStore settings, ISettingsValidator validator, TimeProvider clock)`;
`ProductionRelayFactory(TimeProvider clock, IProcessRunner processes)`;
`RelaySupervisor(SupervisorOptions options, ISettingsStore settings, IPrerequisiteChecker prerequisites, IMappingManager mappings, IEndpointResolver endpoints, IRelayFactory relays, IHostFileSystem files, TimeProvider clock)`.

Text helpers the review added, with fixed signatures and the same owners as the ones listed next:
`PrerequisiteTexts.StartFault(RelayFaultCode code, int port, bool mappingPointsAtPort)`, an overload beside
the two-parameter form, returns the second port-in-use text of architecture 3.11 when the flag is true and
otherwise what the two-parameter form returns;
`PrerequisiteTexts.ExposedInPrivateMode` (a constant) is the text of check 6a;
`PrerequisiteTexts.ContainmentWarning(string? version, string? checkedVersion)` returns the warning of
architecture 3.7;
`MappingTexts.Exposure(ExposedHandler handler)` and `MappingTexts.ManualRemoval(ExposedHandler handler)`
return the sentence and the removal command text of an exposure warning (4.15).

Static text helpers with fixed signatures (WP-W06 and WP-W07 own the files):
`PrerequisiteTexts.Failure(PrerequisiteId id)` returns the status-line text of architecture 3.11;
`PrerequisiteTexts.StartFault(RelayFaultCode code, int port)` returns the step 7 texts;
`MappingTexts.Effect(MappingAction action, string? endpoint, int relayPort)`;
`MappingTexts.Refusal(MappingRefusal refusal, int httpsPort, string path)`. `PrerequisiteTexts` and
`MappingTexts` are `public static` classes in namespace `LiveCue.Hosting`, because the desktop shows their texts;
the classes behind `HostingFactory.Create` are `internal`, and `Processes.ProcessRunner` is `public`.

### 3.8 Desktop: services and the session

All desktop contracts are in namespace `LiveCue.Desktop`. View models are consumed through these interfaces:
a page's XAML binds to the property names below, the self-test reads them, and unit tests construct the
concrete classes with fakes. Every member is read and written on the UI thread only.

```csharp
// file: windows/src/LiveCue.Desktop/Contracts/Indicators.cs
namespace LiveCue.Desktop;

/// <summary>The mark of an inline status indicator (DS 4.22).</summary>
public enum StatusMark { Ring, Dot, Spinner, Pause, Alert, Check }

/// <summary>The status colour token of DS 2.7.</summary>
public enum StatusTone { Offline, Live, Success, Warning, Danger }

/// <summary>One inline indicator: mark, colour, visible text and an optional tooltip.</summary>
public sealed record StatusIndicator(StatusMark Mark, StatusTone Tone, string Text, string? ToolTip = null);

public enum BannerTone { Info, Success, Warning, Danger }

/// <summary>A banner (DS 4.11). ActionLabel is the label of its one quiet button, or null.</summary>
public sealed record BannerModel(BannerTone Tone, string? Title, string Body, string? ActionLabel = null);

public enum AppPage { Live, Pairing, Activity, Settings }
```

```csharp
// file: windows/src/LiveCue.Desktop/Contracts/Services.cs
using System.ComponentModel;
using System.Windows.Input;

namespace LiveCue.Desktop;

/// <summary>The UI thread. The only way a view model gets work onto it.</summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    /// <summary>Queues the action. Returns at once. An exception in the action is routed to the session's
    /// diagnostic handler, never to the caller.</summary>
    void Post(Action action);
}

public interface IClipboard
{
    /// <summary>False when the clipboard is unavailable. Never throws.</summary>
    bool TrySetText(string text);
}

/// <summary>Opens something outside the app. Never throws.</summary>
public interface IExternalLauncher
{
    bool OpenUrl(Uri url);

    bool OpenFile(string path);
}

public interface IToastHost
{
    /// <summary>One toast at a time; a new one replaces the current one (DS 4.11).</summary>
    void Show(string text);
}

public interface INavigator
{
    AppPage Current { get; }

    void Navigate(AppPage page);
}

/// <summary>A dialog shown in the window (DS 4.10). Escape runs CancelCommand.</summary>
public interface IDialogViewModel : INotifyPropertyChanged
{
    string Title { get; }

    /// <summary>True while the confirmed action runs: the confirm button is busy, everything else and
    /// Escape are disabled.</summary>
    bool IsBusy { get; }

    ICommand CancelCommand { get; }

    /// <summary>Raised when the dialog is finished, confirmed or not.</summary>
    event EventHandler? Closed;
}

public interface IDialogHost
{
    IDialogViewModel? Current { get; }

    /// <summary>Shows the dialog over a scrim, traps focus, and restores focus to the invoking control
    /// when it closes. A second dialog while one is open is ignored.</summary>
    void Show(IDialogViewModel dialog);
}
```

```csharp
// file: windows/src/LiveCue.Desktop/Contracts/IDesktopSession.cs
using System.ComponentModel;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop;

/// <summary>The relay status machine of architecture 3.12.</summary>
public enum RelayUiState
{
    Stopped,
    Checking,
    PrerequisiteFailed,
    Starting,
    Ready,
    Paused,
    Thinking,
    RequestFailed,
    Faulted,
}

/// <summary>The phone presence machine of architecture 3.12.</summary>
public enum PhonePresence { NotConnected, Connected, Idle }

/// <summary>Moments that more than one page reacts to.</summary>
public enum SessionSignal
{
    /// <summary>A start passed its checks. The activity log is cleared; transcript and reply are kept.</summary>
    RelayStarting,

    /// <summary>The relay stopped or faulted: stopped layout (QR cleared, hints reset).</summary>
    RelayEnded,

    /// <summary>"Clear activity": log, transcript and reply are emptied.</summary>
    ActivityCleared,
}

public enum SessionMessageKind { Info, Warning, Error }

/// <summary>An activity line produced by the desktop itself (mapping results, settings problems, a failed
/// command), as opposed to one derived from a relay event.</summary>
public sealed record SessionMessage(DateTimeOffset At, SessionMessageKind Kind, string Text);

/// <summary>
/// The desktop's one model of the supervisor and the relay. It reads the supervisor's events on a
/// background task, posts each to the UI thread, updates its own state, then raises its events. An
/// exception thrown by a handler of one event is caught, reported as a "Relay diagnostic: " activity line
/// and never stops later events (WIN-10). Every member is used on the UI thread; every command method is
/// single-flight (a second call while one runs returns at once) and never throws.
/// </summary>
public interface IDesktopSession : INotifyPropertyChanged
{
    RelayUiState State { get; }

    /// <summary>The text of PrerequisiteFailed and Faulted; null in every other state. A fault text stays
    /// until the next start.</summary>
    string? StateText { get; }

    RelayFaultCode? FaultCode { get; }

    /// <summary>True in Ready, Paused, Thinking and RequestFailed.</summary>
    bool IsRelayRunning { get; }

    bool IsPaused { get; }

    PhonePresence Presence { get; }

    /// <summary>Time of the last PhoneSeen since the relay started; null before the first.</summary>
    DateTimeOffset? LastPhoneSeen { get; }

    /// <summary>Endpoint of the last RelayStarted. Kept after the relay stops.</summary>
    string? AdvertisedEndpoint { get; }

    /// <summary>The report of the most recent check, or null before the first.</summary>
    PrerequisiteReport? Prerequisites { get; }

    /// <summary>The most recent mapping inspection, or null before the first.</summary>
    ConnectionInspection? Connection { get; }

    /// <summary>True when the relay runs and the selected mode's mapping is Present or PresentNotOwned.</summary>
    bool IsConnectionReady { get; }

    /// <summary>The settings as last loaded or saved.</summary>
    RelaySettings Settings { get; }

    /// <summary>State of settings.json at the last load.</summary>
    SettingsLoadResult SettingsLoad { get; }

    /// <summary>True when the relay runs with a port, mode or endpoint that differs from Settings.</summary>
    bool SettingsDifferFromRunning { get; }

    /// <summary>Every relay event, after the session updated its own state.</summary>
    event EventHandler<RelayEvent>? RelayEventReceived;

    /// <summary>A matrix returned by ShowPairing or ResetPairing, including the automatic first-start one.</summary>
    event EventHandler<PairingQr>? PairingQrProduced;

    event EventHandler<SessionSignal>? Signal;

    event EventHandler<SessionMessage>? MessageRaised;

    /// <summary>Checks, then starts. Called once by the shell when the window has rendered, and by Start.</summary>
    Task StartAsync();

    Task StopAsync();

    /// <summary>Pause when accepting, resume when paused. State changes only on AcceptingChanged.</summary>
    Task TogglePauseAsync();

    /// <summary>Checks only ("Check again"). Also refreshes the mapping inspection.</summary>
    Task CheckAgainAsync();

    /// <summary>One ShowPairingAsync. Does nothing unless IsConnectionReady.</summary>
    Task ShowPairingAsync();

    /// <summary>Reset through the running relay, or through the supervisor when stopped. True on success.</summary>
    Task<bool> ResetPairingAsync();

    /// <summary>Pure: the plan that sets up the selected mode's mapping, or null with the refusal.</summary>
    MappingPlan? PlanSetUp(out MappingRefusal refusal);

    /// <summary>Pure: the plan that removes one mapping of the ledger.</summary>
    MappingPlan PlanRemoval(OwnedMapping entry);

    /// <summary>Runs a confirmed plan off the UI thread. Called by the mapping dialog only (TS-9).</summary>
    Task<MappingResult> ExecuteMappingAsync(MappingPlan plan);

    /// <summary>Validates and saves. Returns the problems; empty means saved.</summary>
    IReadOnlyList<SettingsProblem> SaveSettings(RelaySettings settings);

    /// <summary>Raises ActivityCleared.</summary>
    void ClearActivity();
}
```

### 3.9 Desktop: view models

```csharp
// file: windows/src/LiveCue.Desktop/Contracts/ShellViewModel.cs
using System.ComponentModel;
using System.Windows.Input;

namespace LiveCue.Desktop;

/// <summary>The window: navigation, the sidebar status card, the dialog and toast layers.</summary>
public interface IShellViewModel : INotifyPropertyChanged, INavigator
{
    /// <summary>Two-way: the selected sidebar row.</summary>
    AppPage SelectedPage { get; set; }

    ILiveViewModel Live { get; }

    IPairingViewModel Pairing { get; }

    IActivityViewModel Activity { get; }

    ISettingsViewModel Settings { get; }

    /// <summary>The view model of SelectedPage; the page host's content.</summary>
    object CurrentPage { get; }

    /// <summary>Sidebar card, "Relay" line: the short labels of screens 1.3.</summary>
    StatusIndicator SidebarRelayStatus { get; }

    /// <summary>Sidebar card, "iPhone" line: the same indicator as the Live page.</summary>
    StatusIndicator SidebarPhoneStatus { get; }

    IDialogViewModel? ActiveDialog { get; }

    string? ToastText { get; }

    /// <summary>Parameter: an <see cref="AppPage"/>. Bound to Ctrl+1 to Ctrl+4 and the sidebar rows.</summary>
    ICommand NavigateCommand { get; }

    /// <summary>Escape: cancels the open dialog, if any and if it is not busy.</summary>
    ICommand EscapeCommand { get; }

    /// <summary>Called once by the window on its first ContentRendered: starts the relay (WIN-2).</summary>
    Task OnWindowRenderedAsync();

    /// <summary>Called by the window's Closing handler: stops the relay, bounded (section 5.12).</summary>
    Task OnWindowClosingAsync();
}
```

```csharp
// file: windows/src/LiveCue.Desktop/Contracts/LiveViewModel.cs
using System.ComponentModel;
using System.Windows.Input;
using LiveCue.Hosting;

namespace LiveCue.Desktop;

/// <summary>One row of the prerequisite list (screens 2.6).</summary>
/// <param name="Subtitle">The fix line for a failure; the detail line for a pass; null otherwise.</param>
public sealed record PrerequisiteRow(PrerequisiteId Id, PrerequisiteState State, string Title, string? Subtitle);

/// <summary>The Live page (screens section 2).</summary>
public interface ILiveViewModel : INotifyPropertyChanged
{
    /// <summary>RelayStatusText: mark, colour and the full status text of screens 2.3.</summary>
    StatusIndicator RelayStatus { get; }

    /// <summary>The caption under "Paused"; null otherwise.</summary>
    string? RelayStatusCaption { get; }

    /// <summary>PhoneStatusText.</summary>
    StatusIndicator PhoneStatus { get; }

    /// <summary>PcAddressText: the endpoint, or the "Shown when the relay is running" placeholder.</summary>
    string PcAddress { get; }

    bool HasPcAddress { get; }

    /// <summary>The "connection not set up" banner (R-14), or null.</summary>
    BannerModel? ConnectionBanner { get; }

    /// <summary>The exposure warning (architecture 3.9, section 5.5), or null: a mapping that LiveCue does
    /// not manage, or a public one in Private mode, forwards to the relay port; or the serve status could
    /// not be read. Its one action opens Settings.</summary>
    BannerModel? ExposureBanner { get; }

    /// <summary>True in Stopped, Checking, PrerequisiteFailed, Starting and Faulted.</summary>
    bool IsStartVisible { get; }

    /// <summary>True in Checking and Starting: the control shows a spinner and "Checking…".</summary>
    bool IsStartBusy { get; }

    string StartLabel { get; }

    /// <summary>True while the relay runs: Pause or Resume, and Stop.</summary>
    bool AreRunningControlsVisible { get; }

    /// <summary>"Pause relay" or "Resume relay".</summary>
    string PauseResumeLabel { get; }

    bool IsPaused { get; }

    /// <summary>True in PrerequisiteFailed: the list replaces the two panels.</summary>
    bool ArePrerequisitesVisible { get; }

    IReadOnlyList<PrerequisiteRow> Prerequisites { get; }

    string TranscriptHint { get; }

    /// <summary>The transcript, then appended speech lines. Bounded (section 5.7).</summary>
    string TranscriptText { get; }

    /// <summary>The "[In progress] …" and "Instruction: …" blocks, drawn in ink.secondary; empty when none.</summary>
    string TranscriptSecondaryText { get; }

    bool IsTranscriptPlaceholder { get; }

    string ReplyHint { get; }

    /// <summary>The answer or summary and, after a blank line, the details.</summary>
    string ReplyText { get; }

    IReadOnlyList<string> ReplyKeyPoints { get; }

    IReadOnlyList<string> ReplyActionItems { get; }

    bool IsReplyPlaceholder { get; }

    bool IsReplyThinking { get; }

    /// <summary>The danger banner of a failed request, or null.</summary>
    BannerModel? ReplyFailure { get; }

    /// <summary>ReplyTiming: the caption under the reply card, or null.</summary>
    string? ReplyTiming { get; }

    ICommand StartRelayCommand { get; }

    ICommand PauseResumeCommand { get; }

    ICommand StopRelayCommand { get; }

    ICommand CheckAgainCommand { get; }

    ICommand OpenSettingsCommand { get; }

    /// <summary>Opens the mapping dialog for the selected mode.</summary>
    ICommand SetUpConnectionCommand { get; }

    ICommand CopyPcAddressCommand { get; }
}
```

```csharp
// file: windows/src/LiveCue.Desktop/Contracts/PairingViewModel.cs
using System.ComponentModel;
using System.Windows.Input;

namespace LiveCue.Desktop;

/// <summary>The QR machine of architecture 3.12.</summary>
public enum QrState { Empty, Shown, Hidden }

public enum QrHiddenReason { None, Button, Timer, RelayEnded }

/// <summary>What the tile draws.</summary>
public enum QrTileFace
{
    /// <summary>Placeholder, "Start the relay to show the QR".</summary>
    RelayNotRunning,

    /// <summary>Placeholder, "Set up the connection first." (R-14).</summary>
    ConnectionNotSetUp,

    /// <summary>Placeholder, "Show the QR to pair an iPhone".</summary>
    Placeholder,

    /// <summary>Spinner while ShowPairing or ResetPairing runs.</summary>
    Generating,

    /// <summary>The code.</summary>
    Code,
}

/// <summary>The Pairing page (screens section 3).</summary>
public interface IPairingViewModel : INotifyPropertyChanged
{
    QrState QrState { get; }

    QrHiddenReason HiddenReason { get; }

    QrTileFace TileFace { get; }

    /// <summary>The matrix while Shown; null otherwise. Cleared, not just hidden, when the QR hides.</summary>
    bool[][]? QrModules { get; }

    /// <summary>The second placeholder line for the current TileFace.</summary>
    string PlaceholderLine { get; }

    /// <summary>"Hides in M:SS" while Shown; empty otherwise.</summary>
    string CountdownText { get; }

    /// <summary>True in the last 30 seconds.</summary>
    bool IsCountdownWarning { get; }

    /// <summary>The state part of the automation name: "shown, hides in 1 minute 42 seconds" or "hidden".</summary>
    string QrAutomationStatus { get; }

    /// <summary>PairingHint.</summary>
    string Hint { get; }

    bool CanShowQr { get; }

    /// <summary>"Start the relay first." or "Set up the connection first."; null when CanShowQr.</summary>
    string? ShowQrCaption { get; }

    bool CanHideQr { get; }

    /// <summary>PairingAddressField: the advertised endpoint, or empty.</summary>
    string PcAddress { get; }

    /// <summary>PairingVerifyNotice: the result of the last self-check; null until one completes and after
    /// the QR hides.</summary>
    BannerModel? VerifyNotice { get; }

    bool CanResetPairing { get; }

    ICommand ShowQrCommand { get; }

    ICommand HideQrCommand { get; }

    ICommand CopyAddressCommand { get; }

    /// <summary>Opens the reset confirmation.</summary>
    ICommand ResetPairingCommand { get; }

    ICommand OpenSettingsCommand { get; }
}
```

```csharp
// file: windows/src/LiveCue.Desktop/Contracts/ActivityViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace LiveCue.Desktop;

/// <summary>One log entry (screens 4.1).</summary>
/// <param name="TimeText">HH:mm:ss in local time; empty for the trim marker.</param>
/// <param name="Payload">Indented JSON of a request or reply; null otherwise.</param>
public sealed record ActivityEntry(DateTimeOffset At, string TimeText, string Message, string? Payload, bool IsTrimMarker = false);

/// <summary>The Activity page (screens section 4).</summary>
public interface IActivityViewModel : INotifyPropertyChanged
{
    /// <summary>Oldest first. When the log was trimmed, entry 0 is the trim marker.</summary>
    ReadOnlyObservableCollection<ActivityEntry> Entries { get; }

    bool IsEmpty { get; }

    /// <summary>"N entries" (the marker is not counted); empty when the log is empty.</summary>
    string EntryCountText { get; }

    /// <summary>The whole log as text, as it would be copied: for tests and the self-test.</summary>
    string Text { get; }

    /// <summary>The three totals of the latest EdgeStatistics event (protocol 5.5), shown as plain numbers
    /// with fixed captions. 0 until the first event and again from each relay start. They are not log
    /// entries, are not cleared by "Clear activity" and raise no alert.</summary>
    long RejectedMalformed { get; }

    long RejectedTokenNotAccepted { get; }

    long RejectedFunnelInPrivateMode { get; }

    ICommand ClearActivityCommand { get; }
}
```

```csharp
// file: windows/src/LiveCue.Desktop/Contracts/SettingsViewModel.cs
using System.ComponentModel;
using System.Windows.Input;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop;

/// <summary>One stale mapping of ours and the sentence that describes it.</summary>
public sealed record StaleMappingRow(OwnedMapping Entry, string Text);

/// <summary>One row of the exposure report that is not in the ledger: the sentence that describes it and
/// the removal command as text for the owner to run. LiveCue never runs it. CommandText is empty when no
/// command can be shown safely (4.15).</summary>
public sealed record UnownedExposureRow(ExposedHandler Handler, string Text, string CommandText);

/// <summary>The Settings page (screens section 5). Field properties are two-way and hold the text as
/// typed; nothing is saved until SaveCommand.</summary>
public interface ISettingsViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
{
    ConnectionMode Mode { get; set; }

    string ModeExplanation { get; }

    string RelayPortText { get; set; }

    string PrivateHttpsPortText { get; set; }

    /// <summary>443, 8443 or 10000.</summary>
    int PublicHttpsPort { get; set; }

    string PublicPathPrefix { get; set; }

    /// <summary>SettingsAddressField: the endpoint the QR would advertise for the form as it stands, or a
    /// placeholder when Tailscale's name is unknown.</summary>
    string PcAddress { get; }

    /// <summary>MappingStatus for the selected mode.</summary>
    StatusIndicator MappingStatus { get; }

    /// <summary>The blocked reason, or the fix when Tailscale is missing or offline; null otherwise.</summary>
    string? MappingCaption { get; }

    /// <summary>The exact command that Set up would run for the saved settings; empty when none.</summary>
    string MappingCommandText { get; }

    string MappingEffectText { get; }

    bool CanSetUpMapping { get; }

    /// <summary>The selected mode's own mapping when it is Present and in the ledger; null otherwise.
    /// "Remove…" is shown when it is not null.</summary>
    OwnedMapping? CurrentOwnedMapping { get; }

    /// <summary>One standing warning per stale mapping of ours (the other mode's, or one at an old port),
    /// each with a "Remove…" button.</summary>
    IReadOnlyList<StaleMappingRow> StaleMappings { get; }

    /// <summary>One standing warning per handler that forwards to the relay port and is not in the ledger,
    /// each with its removal command as selectable text and no button but "Check again".</summary>
    IReadOnlyList<UnownedExposureRow> UnownedExposures { get; }

    /// <summary>"Tailscale's configuration could not be read, so LiveCue cannot tell what is exposed.", or
    /// null when the last inspection read the serve status.</summary>
    string? ExposureUnknownText { get; }

    /// <summary>"The relay is still using the previous connection settings…", or null.</summary>
    BannerModel? RestartNotice { get; }

    /// <summary>A problem with settings.json at load (running on defaults), or null.</summary>
    BannerModel? SettingsFileNotice { get; }

    /// <summary>Blank means automatic.</summary>
    string CodexPathText { get; set; }

    /// <summary>CodexPathStatus: found path or the fix.</summary>
    StatusIndicator CodexPathStatus { get; }

    string? CodexPathDetail { get; }

    /// <summary>CodexLoginStatus.</summary>
    StatusIndicator CodexLoginStatus { get; }

    /// <summary>CodexVersionStatus: the version text of the Codex in use, or "Unknown" (architecture 3.7).
    /// Success tone when the containment check was run for this version, warning tone otherwise.</summary>
    StatusIndicator CodexVersionStatus { get; }

    /// <summary>The containment warning of architecture 3.7, or "Containment check run for this
    /// version."; null when no Codex was found.</summary>
    string? CodexVersionDetail { get; }

    /// <summary>True when a version was read and it is not the recorded one.</summary>
    bool CanMarkContainmentChecked { get; }

    bool IsCheckingAssistant { get; }

    /// <summary>Blank means automatic.</summary>
    string LabFolderText { get; set; }

    /// <summary>SpeechLabStatus.</summary>
    StatusIndicator LabFolderStatus { get; }

    string? LabFolderDetail { get; }

    /// <summary>LoadedModelStatus.</summary>
    StatusIndicator LoadedModelStatus { get; }

    string SpeechCaption { get; }

    /// <summary>HideFromCaptureToggle: two-way, part of the form like every field; takes effect on the
    /// window when the settings are saved.</summary>
    bool HideFromScreenCapture { get; set; }

    string Version { get; }

    /// <summary>True when the form differs from the saved settings.</summary>
    bool HasChanges { get; }

    /// <summary>Enabled when HasChanges and the form has no errors.</summary>
    ICommand SaveCommand { get; }

    ICommand DiscardCommand { get; }

    /// <summary>Opens the mapping dialog to set up the selected mode.</summary>
    ICommand SetUpMappingCommand { get; }

    /// <summary>Opens the mapping dialog to remove. Parameter: an <see cref="OwnedMapping"/>.</summary>
    ICommand RemoveMappingCommand { get; }

    ICommand CheckMappingCommand { get; }

    ICommand CheckAssistantCommand { get; }

    /// <summary>"Mark as checked": saves the settings with CodexContainmentCheckedVersion set to the
    /// version in use. It changes nothing else, and unsaved form changes are not saved with it.</summary>
    ICommand MarkContainmentCheckedCommand { get; }

    ICommand CopyAddressCommand { get; }

    ICommand OpenSourceCommand { get; }

    ICommand OpenNoticesCommand { get; }
}
```

```csharp
// file: windows/src/LiveCue.Desktop/Contracts/Dialogs.cs
using System.Windows.Input;
using LiveCue.Hosting;

namespace LiveCue.Desktop;

/// <summary>Screens 6.1. Initial focus is on Cancel; Enter does not confirm.</summary>
public interface IResetPairingDialogViewModel : IDialogViewModel
{
    string Body { get; }

    /// <summary>A failure of the reset, shown in the dialog; null otherwise.</summary>
    BannerModel? Failure { get; }

    ICommand ConfirmCommand { get; }
}

/// <summary>Screens 6.2: set up or remove one mapping. The only caller of ExecuteMappingAsync.</summary>
public interface IMappingDialogViewModel : IDialogViewModel
{
    MappingAction Action { get; }

    /// <summary>"LiveCue will run:".</summary>
    string Intro { get; }

    /// <summary>plan.Command.DisplayText, verbatim.</summary>
    string CommandText { get; }

    string EffectText { get; }

    /// <summary>"Set up" or "Remove".</summary>
    string ConfirmLabel { get; }

    /// <summary>True for the remove forms: the confirm button is the destructive style.</summary>
    bool IsDestructive { get; }

    /// <summary>Failure of the last attempt; the dialog stays open. Null otherwise.</summary>
    BannerModel? Failure { get; }

    ICommand ConfirmCommand { get; }
}
```

`Contracts/AutomationIds.cs` is a `public static class AutomationIds` with one `const string` per id of
screens section 9, named exactly like its value. Views set `AutomationProperties.AutomationId` from it with
`x:Static`; the self-test finds elements by it. The review added six elements that screens section 9 does
not list yet; their ids are constants of the same class: `ExposureBanner`, `ActivityRejectedCounts`,
`UnownedExposureList`, `CodexVersionStatus`, `MarkContainmentCheckedButton`, `HideFromCaptureToggle`
(section 7.4, hand-off 1).

Constructor signatures the composition root relies on (stubs in the foundation, kept by the owning package):
`DesktopSession(IRelaySupervisor supervisor, IMappingManager mappings, IEndpointResolver endpoints, IUiDispatcher ui, TimeProvider clock)`;
`Navigator()`, `DialogHost()`, `ToastHost(IUiDispatcher ui, TimeProvider clock)` (the three small observable
classes in `Shell/` that implement `INavigator`, `IDialogHost` and `IToastHost`, created first);
`ShellViewModel(IDesktopSession session, ILiveViewModel live, IPairingViewModel pairing, IActivityViewModel activity, ISettingsViewModel settings, Navigator navigator, DialogHost dialogs, ToastHost toasts)`;
`LiveViewModel(IDesktopSession session, INavigator navigator, IDialogHost dialogs, IClipboard clipboard, IToastHost toasts, TimeProvider clock, IUiDispatcher ui)`;
`ActivityViewModel(IDesktopSession session, TimeProvider clock)`;
`PairingViewModel(IDesktopSession session, INavigator navigator, IDialogHost dialogs, IClipboard clipboard, IToastHost toasts, TimeProvider clock, IUiDispatcher ui)`;
`SettingsViewModel(IDesktopSession session, IDialogHost dialogs, IClipboard clipboard, IToastHost toasts, IExternalLauncher launcher, HostingServices hosting, IUiDispatcher ui)`;
`ResetPairingDialogViewModel(IDesktopSession session)`;
`MappingDialogViewModel(IDesktopSession session, MappingPlan plan)`.
`ShellViewModel` forwards `SelectedPage`, `ActiveDialog` and `ToastText` to those three objects.

---

## 4. Relay internals

### 4.1 Numbers this design adds

Every other number is a protocol constant (cited by id) or an architecture value (cited by section).

| Id | Value | Meaning |
|---|---|---|
| W-01 | architecture 3.4 | Capacity of the event channel; oldest dropped; drops counted and reported once per 60 s as a `Diagnostic` |
| W-02 | architecture 3.11 | Budget of `StopAsync` before open connections are aborted |
| W-03 | 256 frames | Frames queued toward the phone. A 257th aborts the socket (the phone is not reading) |
| W-04 | 10 s | One frame not written to the phone within this time aborts the socket |
| W-05 | 64 KiB | Largest `pairing.json` or `settings.json` that is read; larger is "unreadable" |
| W-06 | 4 KiB | Largest self-check or lab-probe response body that is read; larger is a failure |
| W-07 | 4 | Self-checks that may be in progress at once; a fifth cancels the oldest (reported `NotVerified`) |
| W-08 | 5 s | Wait for a killed child process to exit before it is abandoned |
| W-09 | 3 tries, 100 ms apart | Deleting a Codex working folder that is still locked; then a `Diagnostic` |
| W-10 | 1 s | Interval of the desktop's presence and countdown tick |
| W-11 | W-02 plus 1 s | What closing the window waits for the relay before the process exits anyway |
| W-12 | 120 s | Watchdog of the self-test: no result means exit code 3 |
| W-13 | 60 s | Shortest time between two `EdgeStatistics` events (protocol 5.5) |

### 4.2 Start, stop, pause, faults (`Core/RelayHost.cs`)

`StartAsync`, in order. The first failing step publishes `RelayFaulted`, completes the event channel, sets
`Faulted` and throws `RelayStartException`; nothing is left listening and nothing was written except as noted.

| Step | Action | Fault |
|---|---|---|
| 1 | Status `Starting`. A second call throws `InvalidOperationException` | |
| 2 | Canonicalise `AdvertisedEndpoint` (protocol 4.3). The canonical text is what is advertised (P2-148) | `InvalidEndpoint` |
| 3 | `Port` is 0 or in 1024 to 65535, and is not 47831. `DataDirectory` exists | `StartFailed` |
| 4 | `Pairing.LoadOrCreateAsync` | `PairingStoreUnreadable` |
| 5 | Rewrite `schemas\assist.schema.json` and `schemas\summary.schema.json` from the embedded copies. Rewrite `lab\Start-ASR-Lab.ps1`, `lab\Stop-ASR-Lab.ps1` and `lab\LabCommon.ps1` from the embedded copies (`LabScripts.WriteCopies`, protocol 11.5). When `Dependencies.TempRoot` is null: create `work\` and delete everything in it (protocol 12.2); a folder that cannot be deleted is left and reported as a `Diagnostic`, not a fault | `StartFailed` |
| 6 | Build and start the Kestrel application (4.3). An address-in-use error from the listener is recognised by its exception type, not its text | `PortInUse`, otherwise `StartFailed` |
| 7 | Read `BoundPort`; Status `Running`; publish `RelayStarted`; for an existing pairing publish `Notice(PairingRestored)` | |
| 8 | When `WarmCatalogOnStart` and a Codex path exists: start one catalogue read and ignore its outcome | |

Fixed fault messages: `PortInUse` "The relay port is already in use."; `PairingStoreUnreadable` "The saved
pairing could not be read."; `InvalidEndpoint` "The advertised endpoint is not valid."; `StartFailed` "Could
not start the relay."; `Internal` "The relay stopped after an internal error." Hosts show their own texts
(architecture 3.11), chosen by code.

`StopAsync`: Status `Stopping`; cancel `Access.Stopping`. That one token makes every answer run kill its
process and end, and every speech stream send `RELAY_STOPPING` and close with 1001. In-flight HTTP requests are
not answered: their connections are aborted with the listener (the phone sees a transport failure, or the
edge proxy's 502, which it shows as `L-NO-RELAY`). Then wait until no request and no speech socket remains or
until C-58 has passed, call the application's `StopAsync` with a token that fires when W-02 has passed since
step one, dispose it, set Status `Stopped`, publish a last `EdgeStatistics` when a counter changed since the
previous one, publish `RelayStopped(Requested)` and complete the event channel.
The lab container and any running lab script are left alone (architecture 3.8).

Pause: `SetAcceptingAsync(false)` flips `Access.Accepting`, cancels `Access.WhileAccepting`, then publishes
`AcceptingChanged`. Open streams end with `RELAY_PAUSED` through that token; an answer run and a model
operation already in progress continue. Resume issues a fresh `WhileAccepting` token. Pause is not stored.

Runtime fault: an exception that escapes the pipeline's last-resort handler in a way that leaves the listener
unusable, or the event pump failing, publishes `RelayFaulted(Internal)`, then performs the stop sequence and
publishes `RelayStopped(Fault)`. The relay never restarts itself.

### 4.3 Listener (`Edge/KestrelSetup.cs`)

The application is built with `WebApplication.CreateEmptyBuilder` and `UseKestrelCore`: no configuration
files or environment variables are read, no logging provider exists, no host filtering, no endpoint routing,
no developer exception page. The pipeline is `UseWebSockets` followed by one terminal delegate
(`EdgePipeline.InvokeAsync`). It is started with `StartAsync`, never a blocking run call.

| Kestrel setting | Value |
|---|---|
| `Listen` | `IPAddress.Loopback` and `Options.Port` only; `ListenOptions.Protocols = Http1` |
| Listen socket | Created through `SocketTransportOptions.CreateBoundListenSocket` with `ExclusiveAddressUse` true, so no second socket can share the port while the relay runs (protocol 5.1). To confirm on .NET 10 whether Kestrel already sets it; the explicit setting stays either way |
| Connection middleware | `ConnectionGate` (below) |
| `AddServerHeader` | false |
| `Limits.MaxRequestLineSize`, `MaxRequestHeadersTotalSize`, `MaxRequestHeaderCount` | C-19 |
| `Limits.RequestHeadersTimeout`, `KeepAliveTimeout` | C-40, C-42 |
| `Limits.MinRequestBodyDataRate` | the rate and grace period of protocol 5.1 |
| `Limits.MaxRequestBodySize` | C-10 (a second line behind `BodyReader`) |
| `Limits.MaxConcurrentConnections` | C-30 (a second line behind `ConnectionGate`) |
| `Limits.MaxConcurrentUpgradedConnections` | C-32 |
| `WebSocketOptions.KeepAliveInterval` | C-57; `KeepAliveTimeout` left infinite |

`ConnectionGate` is a connection middleware (`ListenOptions.Use`). It counts every open connection, upgraded
or not, and aborts a new one when the count would pass C-30. Kestrel's own two limits count plain and upgraded
connections separately and would together allow C-30 plus C-32.

Notes on what Kestrel does and cannot do:

| Id | Fact | Consequence |
|---|---|---|
| K-1 | Kestrel adds a `Date` header to every response and has no switch for it | Every relay response carries `Date` next to the headers of protocol 5.3. The three 401 answers are identical in every byte the relay writes; `Date` varies with the clock only, never with the failing step. P2-008 is tested against the allowlist `Content-Type`, `Cache-Control`, `X-Content-Type-Options`, `Content-Length`, `Date`, `Connection`. Accepted by the orchestrator: protocol 5.3 now reads "besides `Content-Length`, `Date` and the connection headers" |
| K-2 | The framework WebSocket sends a real Ping only in the mode that also aborts the socket when no Pong answers, and it skips Pings while data arrives. Without that mode it sends an unsolicited Pong frame every interval | The keep-alive frame of C-57 is an unsolicited Pong, and nothing is required of the phone. A phone ignores it exactly as it would answer a Ping invisibly. Accepted by the orchestrator: protocol 10.5 now says "keep-alive frame" and names the unsolicited Pong. Hand-written WebSocket framing on an internet edge was rejected as the larger risk |
| K-3 | The framework WebSocket has no maximum message size | `PhoneSocket` receives into a buffer of C-20 plus one byte and stops at the first byte beyond C-20 |
| K-4 | After a response to a request whose body the application did not read, Kestrel reads and discards the rest of the body unless the connection is closing | Every response written before the body was completely read carries `Connection: close`. No relay code and no Kestrel code reads a body before E-3 passes |
| K-5 | A body below the minimum data rate makes the body read throw `BadHttpRequestException` | `BodyReader` turns it into `408 REQUEST_TIMEOUT` when the response has not started; otherwise Kestrel's bare 408 stands. Both are allowed by protocol 5.1 |
| K-6 | A request line or header block that breaks C-19 or C-40 never reaches the pipeline | Bare 400, 408, 414 or 431 from the server core, as protocol 5.1 says |
| K-7 | `IHttpRequestFeature.RawTarget` is the request target exactly as sent | E-4 compares that text and never a decoded path |
| K-8 | A header that is present with an empty value is still a key of the header dictionary | E-1 uses `ContainsKey`; a test sends `Origin:` with no value |
| K-9 | For a request with both `Content-Length` and `Transfer-Encoding`, Kestrel is expected to let `Transfer-Encoding` win and drop `Content-Length` instead of rejecting (to confirm on .NET 10) | Either outcome is accepted: a bare 400, or chunked framing with the connection closed after the response (test plan ST-21). What must hold is that the request is never framed by `Content-Length`. **No relay code parses framing by hand to "fix" this.** The defence against request smuggling through the proxy's reused loopback connection is K-4, and it has its own test (ST-20) |

### 4.4 The request pipeline (`Edge/EdgePipeline.cs`)

One method, steps in this order. "Stops" means the step writes its answer and the request ends there.

| Step | Name | Does | Stops with |
|---|---|---|---|
| P-0 | `ConnectionGate` | C-30, before any byte is parsed | connection aborted |
| P-1 | `Arrive` | Takes the arrival timestamp from the clock. Notes whether the request declares a body (`Content-Length` above 0, or `Transfer-Encoding`) | |
| P-2 | `RefuseBrowserAndFunnel` (E-1) | `Origin` or `Sec-Fetch-Site` present; in Private mode also `Tailscale-Funnel-Request` present | `401 UNAUTHORIZED` |
| P-3 | `CheckBearerShape` (E-2) | `Authorization` has exactly one value; its length is 7 plus C-02; it starts with `Bearer ` (ordinal); the rest is in the token alphabet | `401 UNAUTHORIZED` |
| P-4 | `CheckToken` (E-3) | `Pairing.TryVerify(token, out revoked)`: one call gives the result and the revocation token of exactly the pairing that was verified. That token is the request's `PairingRevoked` | `401 UNAUTHORIZED` |
| P-5 | `Presence` | When `X-LiveCue-Self-Check` has exactly one value and `Pairing.IsSelfCheckNonce` is true: `NoteSelfCheckArrival(funnel header present)` and no event. Otherwise publish `PhoneSeen` | |
| P-6 | `MatchRoute` (E-4) | `RouteTable.Match(method, RawTarget)`. Route 10 also needs `WebSockets.IsWebSocketRequest` | `404 NOT_FOUND` |
| P-7 | `Speech branch` | Route 10 only: call `SpeechRoute` now and skip P-8 to P-11. The route does E-7 for sockets itself, before the upgrade | see 4.11 |
| P-8 | `CheckProtocolHeader` (E-5) | `X-LiveCue-Protocol` has exactly one value and it is the text of C-01 | `400 PROTOCOL_MISMATCH` |
| P-9 | `RefuseBodyOnGetDelete` (E-6) | Routes that read no body: the request declares none | `400 INVALID_REQUEST` |
| P-10 | `EnterGauge` (E-7) | C-31; the count covers requests that passed this step and have not ended | `429 TOO_MANY_REQUESTS` |
| P-11 | `Run` (E-8) | Builds the `RouteContext` with `Work` = route deadline counted from P-1, linked with client abort, relay stop and pairing revocation; awaits the handler | see below |
| P-12 | `Finish` | Leaves the gauge; disposes the token sources | |

The three 401 answers come from one pre-built byte array through one code path. A 401, 404, 400 or 429 on a
request that declared a body carries `Connection: close` (K-4).

**Rejected-request counters** (protocol 5.5). Three `long` fields of the pipeline, each changed only with
`Interlocked.Increment`, on the 401 path and nowhere else: P-2 refusing for `Tailscale-Funnel-Request` in
Private mode increments `FunnelInPrivateMode` (checked first, so such a request is counted there even when it
also carries a browser header); P-2 refusing for `Origin` or `Sec-Fetch-Site`, and P-3, increment
`Malformed`; P-4 increments `TokenNotAccepted`. Nothing else happens on that path: no allocation, no event,
no clock read. A clock timer of W-13, created at start, publishes one `EdgeStatistics` with the three totals
when at least one changed since the last publication, and `StopAsync` publishes a last one (4.2). The
counters belong to the relay instance and start at zero with it.

What P-11 does when the handler does not write a response itself:

| Handler outcome | Answer |
|---|---|
| Throws `RelayException` | The response of its error |
| `OperationCanceledException`, cause `ClientAborted` | None; the connection is gone |
| `OperationCanceledException`, cause `RelayStopping` | None; the connection is aborted |
| `OperationCanceledException`, cause `PairingRevoked` | `401 UNAUTHORIZED` |
| `OperationCanceledException`, cause `DeadlineExceeded` | `504 ASSISTANT_TIMEOUT` on routes 7 and 8, otherwise `504 DEADLINE_EXCEEDED` |
| Any other exception | `Diagnostic("edge", "A request failed unexpectedly.", exception)`; `500 INTERNAL_ERROR` if no response has started, otherwise the connection is aborted |

### 4.5 Bodies and responses (`Edge/BodyReader.cs`, `Edge/ResponseWriter.cs`)

`BodyReader.ReadAsync`, in the order of protocol 5.4:

1. `Content-Length` above C-10: `413`, nothing read.
2. Read `Request.BodyReader` in a loop under a token that fires at C-41 after P-1 and is linked to `Work`.
   Count bytes; stop at the first byte beyond C-10: `413`.
3. When that token fires: if the C-41 timer is the one that fired (checked first, so it also wins a tie with
   the route deadline), `408 REQUEST_TIMEOUT`; otherwise the cancellation escapes and P-11 answers it.
   A `BadHttpRequestException` is mapped by its status: 408 and 413 to their coded errors, anything else to
   `400 INVALID_REQUEST`.
4. Parse with `Utf8JsonReader` options `MaxDepth` 32 (protocol 5.4 rule 4), no comments, no trailing commas.
   Invalid UTF-8, invalid JSON, an empty body, or a top level that is not an object: `400 INVALID_JSON`.
5. Return the bytes, the detached root element and `ReadMs`.

`ResponseWriter.WriteJsonAsync` sets the status, the three headers of protocol 5.3 and `Content-Length`, and
writes the bytes in one call. It refuses a second write and any status from 300 to 399. JSON is produced with
`Utf8JsonWriter`, not indented, with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so non-ASCII text is
written as UTF-8.

### 4.6 Routes 1, 2, 3 and 9

| Route | Algorithm |
|---|---|
| 1 `HealthRoute` | Writes the pre-built document of protocol 6.2. No other work |
| 2 `PairVerifyRoute` | Reads the body (5.4 rules); on an error writes it; otherwise writes `{"ok":true}`. Records nothing |
| 3 `ModelsRoute` | `Catalog.ReadAsync(Work)`. On `ASSISTANT_CATALOG_UNAVAILABLE`: publish `AssistantNotice` and answer 503. Otherwise 200 with `{"models":[…]}` as in protocol 6.4, each offer written as `id`, `name`, `reasoningEfforts` (an empty list gives `{"models":[]}`). Served while paused |
| 9 `CancelRoute` | `Answers.TryCancel(RequestId)`: true gives `200 {"ok":true,"cancelled":true}`; false gives `404 REQUEST_NOT_FOUND`. A target that does not match the pattern never reaches the route (P-6 answers 404 `NOT_FOUND`) |

### 4.7 Pairing (`Pairing/`)

**Token.** 32 bytes from `RandomNumberGenerator`, encoded with `Base64Url` (C-02). Hash: SHA-256 of the 43
ASCII bytes. `TryVerify` encodes the characters into a stack buffer, hashes into a stack buffer and compares
with `CryptographicOperations.FixedTimeEquals` against the accepted hash. The accepted hash and the
revocation source of that pairing are the two fields of **one immutable object** (`AcceptedPairing`, private
to `PairingService`), held in one field and read once per call through `Volatile.Read` (protocol E-3: "read
at this moment"). `TryVerify` returns the token of that same object. A request can therefore never be
verified against one pairing and linked to the revocation token of the next.

**File.** `pairing.json` exactly as architecture 3.5: UTF-8 without a byte-order mark, two-space indent, line
feeds, the four members in the order `schemaVersion`, `tokenHash`, `protectedToken`, `createdAt`; `createdAt`
is UTC to the second with a `Z`. `protectedToken` is Base64 of `ProtectedData.Protect(token bytes, entropy,
CurrentUser)` with the entropy of architecture 3.6.

**Load** (`PairingStore.Load`): the file does not exist: create. Otherwise every one of these must hold, or
the result is `PairingStoreUnreadable` and the file is not touched: at most W-05 bytes; a JSON object;
`schemaVersion` is the number 1; `tokenHash` matches `^[a-f0-9]{64}$`; `protectedToken` is Base64 that
unprotects to exactly C-02 ASCII characters of the token alphabet; their SHA-256 equals `tokenHash`
(constant time). A leftover `pairing.json.pending` is ignored at load and overwritten by the next write.
A first pairing that cannot be protected or written is the same fault, and leaves no file.

**Atomic write** (`PairingFiles.WriteAtomic`): create `pairing.json.pending` with `FileShare.None`, write,
`Flush(flushToDisk: true)`, close, then `File.Move(pending, final, overwrite: true)`.

**Plaintext in memory.** The token is not kept between uses. It exists as a byte buffer only inside `Create`,
`Load` (to verify), `Show`, `Reset`, `GetPayload` and a running self-check, and every buffer is cleared with
`CryptographicOperations.ZeroMemory` in a `finally`. The only `string` that ever holds it is the return value
of `GetPayloadAsync` and of `StoredPairingPayload.ReadAsync` (console host, `--show-pairing`), and the
argument a running self-check hands to `ISelfCheckClient.GetHealthAsync`, whose signature takes text.

**Payload and matrix.** The payload bytes are `{"endpoint":"` + canonical endpoint + `","token":"` + token +
`"}`; both values consist of characters that need no JSON escaping, so no serialiser is involved and the text
is exact (protocol 4.2). `QrMatrix.Encode` calls `QrCode.EncodeSegments` with one
`DataSegment.MakeSegment(DataSegmentMode.Binary, ...)` segment (byte mode), `Ecc.Medium`, versions 1 to 40 and
**`boostEcl: false`** (the library would otherwise raise the level above M when space allows). These are the
names of the pinned package version 3.2.1, which has no mask argument: the encoder always chooses the mask.
`DataSegment.FromBinaryData` and `QrCode.EncodeBinary` are not used, because they may split the bytes into
segments of other modes or raise the level. The result is copied to `bool[size][size]`, row-major, with no
quiet zone.

**Show** (`PairingService.ShowAsync`, one at a time): read and verify the file as in Load; build the matrix;
publish `PairingShown`; start the self-check; return. One decryption per call (WIN-20). The file must hold the
pairing the relay accepts: when it is missing, fails a load rule or holds another token, the call throws
`IOException` with the fixed text "The saved pairing could not be read." and nothing is shown or checked.
`GetPayloadAsync` reads the file the same way.

**Reset** (`ResetAsync`): create a token; protect it; write the file atomically; only then build a new
`AcceptedPairing` (the new hash and a new revocation source), swap it in with one `Interlocked.Exchange`, and
cancel the revocation source of the object that was swapped out; publish `Notice(PairingReset)`; continue as
Show with the token in hand. If the write throws, nothing changed and the call throws `IOException`.
Because hash and source travel together, every request that passed E-3 with the old token, whenever it read
the object, holds a token that this reset cancels (test plan ST-12).

**Self-check** (`SelfCheck.RunAsync`, background): create a nonce (16 random bytes, `Base64Url`); register it
(at most W-07 at once); call `ISelfCheckClient.GetHealthAsync(endpoint + "v1/health", token, nonce)` under a
C-49 token; result `NotVerified` when it returns false or times out, otherwise `VerifiedThroughFunnel` when
`NoteSelfCheckArrival(true)` was recorded for this nonce, otherwise `Verified`; unregister the nonce; publish
`SelfCheckCompleted`. The production client is an `HttpClient` over `SocketsHttpHandler` with
`AllowAutoRedirect`, `UseCookies` and `UseProxy` all false; it reads at most W-06 bytes. Each check opens
exactly one connection and closes it: the handler's connect callback refuses a second one, because the HTTP
stack would otherwise repeat a request whose connection was closed before any answer. `NoteSelfCheckArrival`
carries no nonce: it counts for the check whose nonce `IsSelfCheckNonce` matched just before it on the same
thread, which is how P-5 calls the two. A check that is cut off because the relay is stopping publishes
nothing (the last event stays `RelayStopped`).

**`PairingMaintenance`** forwards to `StoredPairingReset.RunAsync`, which creates a token and writes the file
with the production protector and files, replacing whatever was there. One case writes nothing: a data
folder that does not exist holds no pairing, so the call succeeds without creating the folder or a file, and
the next start (whose check 1 creates the folder) creates the first pairing. An existing folder always gets
a new file, also when it held none.

**`StoredPairingExport`** forwards to `StoredPairingPayload.ReadAsync`, which reads and verifies the file as in
Load with the production protector and files, never writes, and returns the payload text for the canonical
form of the endpoint it is given, or null when there is no file. An unreadable file is `IOException` with a
fixed message. It needs no relay, so it works while one runs on the same folder.

### 4.8 Catalogue (`Catalog/`)

Three layers, composed as `CatalogReader` over `CachedCatalogSource` over the source.

| Layer | Algorithm |
|---|---|
| `CodexCatalogSource` (one attempt, protocol 12.1) | No Codex path: fail. `StartSessionAsync` with arguments `app-server`, `--listen`, `stdio://`; environment `CodexAllowlist`; output limit C-24; time limit C-44 (per attempt). Write line 1; read lines until an object with `id` 1 (skip lines that are not JSON objects or carry another id); `error` present: fail. Write lines 3 and 4. Read until `id` 2; fail on `error` or when `result.data` is not an array. Map each element that has a string `model` to `RawCatalogEntry`; `hidden` is truthy unless it is absent, `false` or `null`; efforts are the string values of `supportedReasoningEfforts[].reasoningEffort`. `nextCursor` is ignored. Dispose the session in every case (closes standard input, kills the tree). Any `ProcessFailedException`, end of output or malformed step is `CatalogReadException` |
| `CachedCatalogSource` | Holds the last successful list and its time. Fresh for the cache life of C-44: return it. Otherwise join the read in progress or start one; that read runs under `Access.Stopping`, not under a caller's token, and callers wait with `WaitAsync(ct)`. Success replaces the cache; failure caches nothing |
| `CatalogReader` | Up to the attempts of C-44, with the gap of C-44 between them: read the source, apply `OfferFilter`, store the result as last good (also when it is empty), return a deep copy. After the last failed attempt: a deep copy of last good when one exists, otherwise `RelayException(ASSISTANT_CATALOG_UNAVAILABLE)` |
| `OfferFilter` (protocol 7.1) | Drop hidden entries and ids that fail C-06 (`gpt-` followed by one or more of lower-case letters, digits, `.`, `-`, and nothing else; at most `ModelIdMaxLength` characters in all). Keep efforts that are in C-05, in order, without duplicates; for ids in C-07 remove `none` and put it first. Drop an entry left with no effort. `Name` is the display name when it is a non-empty string, otherwise the id |
| `AssistantSelection.Validate` (protocol 7.3) | `assistant` absent, not an object, or `model` or `reasoningEffort` absent or not a string: `INVALID_ASSISTANT`. An offer with that id (ordinal) whose list contains that effort (ordinal): accepted. Otherwise `ASSISTANT_SELECTION_UNAVAILABLE` |

### 4.9 Answers and the Codex run (`Answers/`)

`AnswerCoordinator.HandleAsync`, the check order of protocol 8.2:

| Step | Action | Ends with |
|---|---|---|
| R-1 | `Access.Accepting` is false | `503 RELAY_PAUSED` |
| R-2 | `AnswerSlot.TryEnter` (C-33, an interlocked flag); released in a `finally` that covers everything below | `429 ASSISTANT_BUSY` |
| R-3 | `ReadJsonBodyAsync` | its error |
| R-4 | `requestId` is a string of the shape C-03 | `400 INVALID_REQUEST_ID` |
| R-5 | `AssistantSelection` shape check | `400 INVALID_ASSISTANT` |
| R-6 | `Catalog.ReadAsync(Work)` | `AssistantNotice`, then `503 ASSISTANT_CATALOG_UNAVAILABLE` |
| R-7 | Selection is in the catalogue | `AssistantNotice`, then `400 ASSISTANT_SELECTION_UNAVAILABLE` |
| | Publish `RequestStarted` (`Transcript`, `PartialTranscript`, `Instruction` are the body's members when they are strings; `PayloadJson` is the compact body). Register the run in `RunRegistry` under its id (compared ordinally, so letter case matters) with a token source linked to `Work` | |
| R-8 | `IAnswerBackend.RunAsync`; then `AnswerValidator` | see below |

Outcomes of R-8, first match wins; each publishes one `RequestFailed` with the same code unless it says
otherwise:

| What happened | Response | Event code |
|---|---|---|
| Route 9 cancelled this id | `409 REQUEST_CANCELLED` | `REQUEST_CANCELLED` |
| Cause `ClientAborted` | none | `CLIENT_DISCONNECTED` |
| Cause `RelayStopping` | none | `RELAY_STOPPING` |
| Cause `PairingRevoked` | `401 UNAUTHORIZED` | `UNAUTHORIZED` |
| Cause `DeadlineExceeded` | `504 ASSISTANT_TIMEOUT` | `ASSISTANT_TIMEOUT` |
| Backend returned null, or the output fails validation | `500 ASSISTANT_FAILED` | `ASSISTANT_FAILED` |
| Valid output | `200`; publishes `RequestCompleted` instead | |

A deadline, abort or revocation before `RequestStarted` was published escapes to the pipeline (4.4) and
publishes no request event.

**Timings** (protocol 8.3), all from the clock's timestamps: `requestReadMs` from the body result;
`codexMs` around `RunAsync`; `relayTotalMs` from P-1 to the return of `RunAsync`; each rounded to one decimal;
`relayOverheadMs` is `max(0, relayTotalMs - codexMs)` computed from the rounded values, so P2-055 holds
exactly.

**`CodexAnswerBackend.RunAsync`** (protocol 12.2):

1. No Codex path: return null.
2. Create `<TempRoot>\` plus 32 lower-case hex characters taken from `RandomNumberGenerator` (16 bytes).
   `TempRoot` is `<DataDirectory>\work` unless a test set another (3.3). The folder is created so that an
   existing folder of that name is an error (check `Directory.Exists` first and treat it as a failed run;
   `Directory.CreateDirectory` alone succeeds silently on an existing folder). A failure here returns null.
3. Arguments, one list element each, in this order: `exec`, `--ephemeral`, `--ignore-user-config`,
   `--ignore-rules`, `--skip-git-repo-check`, `--sandbox`, `read-only`, `--disable`, `shell_tool`, then the
   containment arguments of the table below, then `-m`,
   the model, `-c`, `model_reasoning_effort="<effort>"`, `-c`, `service_tier="default"`, `--output-schema`,
   `<DataDirectory>\schemas\assist.schema.json` or `summary.schema.json`, `-o`, `<folder>\output.json`, `-C`,
   the folder, `-`. The two `-c` values contain their double quotes as characters of the argument.
4. Standard input: the instruction paragraph of protocol 12.3, two line feeds, `UNTRUSTED_INPUT_JSON`, a line
   feed, the request body re-written compactly from the parsed root (member order and duplicates kept), a
   line feed, `END_UNTRUSTED_INPUT_JSON`. No trailing line feed.
5. `IProcessRunner.RunAsync` with working directory the folder, environment `CodexAllowlist`,
   standard output discarded, time limit C-43, `KillWithApplication` true, under the run's token.
6. `Cancelled`: throw `OperationCanceledException`. `Exited` with code 0 and an `output.json` of at most C-24
   bytes: return its bytes. Anything else: null.
7. `finally`: delete the folder recursively (W-09). A folder that survives (a crash, a kill, three failed
   deletes) is removed by the next relay start (4.2 step 5).

**Containment of the answer run** (protocol 12.6). `Answers/CodexArguments.cs` holds the containment
arguments as one fixed list. The builder of WP-W03 fills that list and this table in the same pull request
(the one place where a wave 1 package edits this document): read `codex --help`, `codex exec --help` and
the feature list they name on the build PC, add an argument for every switch that turns off a tool, a
feature or a network capability, and write down what has no switch. Reading help output runs no model call.
Nothing below was verified by the reviewers or by this document: every row marked "to fill" is the
builder's to establish.

| Item | Value |
|---|---|
| Codex version the list was read from | to fill (the text `codex --version` prints) |
| Always passed | `--sandbox read-only`, `--disable shell_tool`, `--ephemeral`, `--ignore-user-config`, `--ignore-rules`, `--skip-git-repo-check` |
| Containment arguments, in order | to fill: one row per switch, with the capability it turns off (at least: patch application, image viewing by local path, web search, configured tool servers, project instruction files, wherever that version has a switch) |
| Capabilities with **no** switch in that version | to fill; each one is a residual risk named in architecture 6.5 |
| Is an unknown `--disable` name an error or only a warning? | to fill. If it is only a warning, a renamed feature would silently come back; say so here |
| Does `--ignore-user-config` also drop the owner's retention or analytics choices? | to fill |
| Result of the live containment check (test plan L-4) for that version | to fill: date and PASS or FAIL, never the canary value |

The environment of the run is the `CodexAllowlist` policy (4.13). The read-only sandbox and the empty
working folder stay, and neither is counted as protecting the owner's files (protocol 12.6).

**`AnswerValidator`** (decision A-14): parses the bytes and checks the fixed shape by hand, no schema library.
Assist: an object whose `detectedQuestion`, `answer` and `details` are strings and whose `memory` is an object
with string `summary`, arrays of strings `facts` and `openQuestions`, and `throughSegmentId` that is a string
or `null`. Summary: an object with strings `title` and `summary` and arrays of strings `keyPoints` and
`actionItems`. A missing or wrongly typed member fails; any other member is dropped. The reply is written
member by member in the order of protocol 8.3, then `execution`. In an assist reply `memory.throughSegmentId`
is always written as JSON `null`, whatever string the model wrote (protocol 8.3, P2-065): the model's marker
is validated for its type and then discarded.

### 4.10 PC model control (`LocalModel/LocalModelController.cs`)

State held: `operation` (kind, model, start timestamp, task) or none; `latch` (kind, model, failure
timestamp) or none. A semaphore of one guards every change of `operation`. "Usable" is protocol 9.2.
`leaseHeld` is `SpeechLease.IsHeld`.

`GetStatusAsync`:

| Condition, first match | State | Model | Busy | Elapsed | Message |
|---|---|---|---|---|---|
| No lab folder | `stopped` | null | false | 0 | MSG-2 |
| A start is running | `starting` | requested | false | whole seconds since it began | MSG-6 |
| A stop is running | `stopping` | requested | false | whole seconds since it began | MSG-7 |
| Probe is usable (clears the latch) | `ready` | probe's | lab busy or `leaseHeld` | 0 | MSG-5 when busy, else MSG-4 |
| Latch set less than C-48 ago | `error` | latch's | `leaseHeld` | 0 | MSG-8 for a start, MSG-9 for a stop |
| Otherwise (an expired latch is cleared) | `stopped` | null | `leaseHeld` | 0 | MSG-3 |

MSG-1 is the message of the controller's stored status before its first probe; route 4 always probes, so it
is never sent.

**Routes 4, 5 and 6** (`Routes/LocalModelStatusRoute.cs`, `LocalModelStartRoute.cs`,
`LocalModelStopRoute.cs`). Route 4: `GetStatusAsync(Work)`, then `200` with the status object. Routes 5 and
6, in the order of protocol 6.6: M-1 `Access.Accepting` is false: `503 RELAY_PAUSED`, before the body is
read; M-2 `ReadJsonBodyAsync`, and on an error write it; M-3 `model` is not a string equal to one of C-04
(ordinal): `400 INVALID_MODEL`; then `StartAsync` or `StopAsync` (M-4 to M-7). A `Refusal` is written as its
error; otherwise the status object, with `202` when `Began` is true and `200` when it is false.

The status object is written with `Utf8JsonWriter`, never by serialising the record: exactly the members
`state`, `model`, `busy`, `elapsedSeconds`, `message`, in that order. `state` is the lower-case name
(`stopped`, `starting`, `ready`, `stopping`, `error`). `model` is a string or JSON `null`, never omitted.
`busy` is a JSON boolean. `elapsedSeconds` is a JSON integer with no fraction. `message` is the text of
protocol 9.4. The phone decodes this object strictly (P2-219): a missing member or a number in quotes fails
every status poll.

Commands. Each row is evaluated under the semaphore, in order.

| Command | Check or action | Result |
|---|---|---|
| Start, Stop | `IsConfigured` is false (M-4) | `503 LAB_NOT_CONFIGURED` |
| Start, Stop | An operation is running (M-5) | `409 MODEL_OPERATION_IN_PROGRESS` |
| Start, Stop | `leaseHeld` (M-6) | `409 MODEL_BUSY` |
| Start, Stop | Probe; the lab reports busy (M-6) | `409 MODEL_BUSY` |
| Start | Probe usable and its model is the requested one | Status `ready`, `Began` false (200) |
| Stop | Probe usable and its model is another one (M-7) | `409 MODEL_MISMATCH` |
| Start, Stop | Otherwise: clear the latch; set `operation`; publish `LocalModelChanged` with the beginning status; start the background part | Beginning status, `Began` true (202) |

Background part, never bound to a request or to a phone:

| Operation | Steps | On success | On failure |
|---|---|---|---|
| Start | `ILabRuntime.StartAsync(model)`; then probe | Requires the probe usable with the requested model: clear `operation`; publish `LocalModelChanged` (`ready`) | Clear `operation`; set the latch; publish `LocalModelChanged` (`error`, MSG-8) |
| Stop | `ILabRuntime.StopAsync()` | Clear `operation`; publish `LocalModelChanged` (`stopped`, MSG-3) | Clear `operation`; set the latch; publish (`error`, MSG-9) |

`PrepareAsync(model, ct)`: under the semaphore: an operation is running gives `OperationInProgress`; probe;
lab busy gives `Busy`; usable with the requested model gives `Ready`; otherwise begin a start exactly as
above. Then, outside the semaphore, wait for that operation's task with `WaitAsync(ct)`: `Ready` or
`LoadFailed`. Cancelling `ct` ends the wait and nothing else.

When the relay stops, the controller stops waiting for a running script and does not kill it.

`LabRuntime`: the probe is `GET http://127.0.0.1:<LabPort>/health` with a client that uses no proxy and no
redirects, limited by C-45 and W-06; it returns a `LabHealth` only for a JSON object whose `ready` and `busy`
are booleans and whose `relayProtocol` is a number. The two scripts run through `IProcessRunner` with exactly
the commands of protocol 11.5: `FileName` is `WindowsPowerShell\v1.0\powershell.exe` under
`Environment.SystemDirectory` (the system-directory API; never `%SystemRoot%` or any other environment
variable), the arguments are separate list elements, environment `LabScriptAllowlist`, output discarded,
limits C-46, `KillWithApplication` false.

**Script copies** (`LabScripts`). Three files are embedded resources of `LiveCue.Relay`, linked from `asr/`
so that there is one source: `Start-ASR-Lab.ps1`, `Stop-ASR-Lab.ps1`, and `LabCommon.ps1`, which the two
launchers dot-source from their own folder. `LabScripts.WriteCopies`
writes all three to `<DataDirectory>\lab\` at relay start (4.2 step 5). Immediately before each run
`LabRuntime` compares the launcher it is about to start **and** `LabCommon.ps1` with the embedded bytes and
rewrites a file that differs; when they cannot be made equal the run is a failure and nothing is started. The `-File` argument is always
`<DataDirectory>\lab\Start-ASR-Lab.ps1` or `…\Stop-ASR-Lab.ps1`, never a path inside `LabFolder`; the start
command carries `-LabFolder <LabFolder>`. Before each run `LabRuntime` also checks that `LabFolder` is a
drive-letter path (`X:\…`) and not a UNC or device-namespace path; a folder that fails is treated as "no lab
folder" (`IsConfigured` false, MSG-2), the same answer the hosting library gives at save time (4.14).

### 4.11 Speech bridge (`Speech/`)

**`SpeechRoute`** (before the upgrade): increments the open-socket count; above C-32 it decrements and
answers `429 TOO_MANY_REQUESTS`. Otherwise it reads the two headers, accepts the upgrade (an offered
`Sec-WebSocket-Extensions` or subprotocol is ignored and never a reason to refuse, protocol 10.1), awaits
`ISpeechBridge.RunAsync`, and decrements in a `finally`. So a socket that is being refused counts until it
is closed. A header that is absent or sent more than once is handed over as absent; the upgrade is accepted
all the same, and admission refuses it with a frame.

Two things the server core adds here. It keeps its own count of upgraded connections with the same cap
(4.3) and gives a place back a moment after the route's `finally` has run; an accept that it refuses in that
moment (`InvalidOperationException` before the response has started) is answered with the same 429. And
when the bridge returns with the socket in any state but `Closed` (the stream aborted it), the route also
aborts the connection (`HttpContext.Abort`). On the build PC the server core resets such a connection by
itself; the explicit abort keeps that from depending on how it ends an upgraded request whose output a phone
that does not read has left unsent.

**Structure.** One `SpeechStream` per socket, run as a single loop over one mailbox (an unbounded channel of
internal events). Everything else only posts to the mailbox: the phone receive loop, the lab receive loop,
the prepare and connect tasks, the timers (created from the clock) and the three tokens (`WhileAccepting`,
`PairingRevoked`, `Stopping`). Only the loop changes state and decides what is sent. Two writer loops drain
the phone queue (W-03, W-04) and the lab queue (C-73). The loop never waits for anything but the next
event, so a slow phone or a slow lab cannot keep it from seeing a timer.

Three rules that the framework's WebSocket and the manual clock impose:

- The socket allows one send at a time, so every frame **and the close frame** go through the one phone
  writer. A cancelled receive or send aborts a framework WebSocket, so no phone I/O is given a
  cancellation token; the stream aborts the socket itself when the table says so.
- The mailbox stays short whatever the other side sends: the phone reader and the lab reader each wait until
  the loop has taken the message they posted last. At most one message from the phone and one from the lab
  wait at any time.
- Inside one step of the loop, timers are set and events are published **before** a frame is queued, and the
  phone writer sets the W-04 timer (for the close frame also the C-58 timer) before the write. Whoever sees
  a frame arrive can rely on the state that frame announces. The tests depend on it: they advance the clock
  right after a frame.

**Receiving from the phone** (`PhoneSocket`): messages are assembled into a buffer of C-20 plus one byte. A
message that reaches that last byte posts `FrameTooLarge` once, and from then on nothing is assembled or
handed on: whatever still arrives is read into the same buffer and thrown away, so that the phone's close
frame is still seen and the close handshake can complete. The same holds from the moment the stream enters
`Closing`. A close frame, or a transport error in either direction, posts `PhoneGone`.

**Admission** (protocol 10.2), in order, first failure wins: S-1 `ProtocolHeader` is the text of C-01; S-2
`ModelHeader` is one of C-04; S-3 `Access.Accepting`; S-4 `LocalModel.IsConfigured`; S-5 not
`LocalModel.OperationRunning`; S-6 `Lease.TryAcquire`. A failure goes to `Fail(code, 1008)` from the state
`Admitting`.

**Transition table.** States: `Admitting`, `Preparing`, `Connecting`, `AwaitingLabReady`, `Streaming`,
`Finalizing`, `Closing`, `Closed`. An event not listed for a state is ignored.

| State | Event | Action | Next |
|---|---|---|---|
| `Admitting` | admitted | Send `loading` (0 s). Start timers: loading (every C-50), load limit (C-51 from the upgrade), lifetime (C-55 from the upgrade). Publish `SpeechStreamChanged(Loading)`. Start `PrepareAsync` | `Preparing` |
| `Preparing` | prepare `Ready` | Start `ConnectAsync` (C-47) | `Connecting` |
| `Preparing` | prepare `Busy` / `OperationInProgress` / `LoadFailed` | `Fail(MODEL_BUSY)` / `Fail(MODEL_OPERATION_IN_PROGRESS)` / `Fail(MODEL_LOAD_FAILED)` | `Closing` |
| `Connecting` | connected | Start the lab receive loop | `AwaitingLabReady` |
| `Connecting` | connect failed | `Fail(LAB_CONNECTION_FAILED)` | `Closing` |
| `AwaitingLabReady` | lab text that is an object with `type` `ready`, `model` equal to the requested model and `sampleRate` 16000 | Stop the loading and load-limit timers. Send `ready`. Record the ready timestamp. Start the idle timer (C-52) and the stream timer (C-54). Publish `SpeechStreamChanged(Ready)` | `Streaming` |
| `AwaitingLabReady` | any other lab text message, or a message of kind `Invalid` | `Fail(MODEL_NOT_READY)` | `Closing` |
| `Preparing`, `Connecting`, `AwaitingLabReady` | loading timer | Send `loading` with whole seconds since the upgrade | same |
| `Preparing`, `Connecting`, `AwaitingLabReady` | load-limit timer | `Fail(MODEL_LOAD_TIMEOUT)` | `Closing` |
| `Preparing`, `Connecting`, `AwaitingLabReady`, `Finalizing` | any phone message | `Fail(AUDIO_OUT_OF_SEQUENCE)` | `Closing` |
| `Streaming` | phone binary, length 0 | Dropped | same |
| `Streaming` | phone binary, odd length | `Fail(AUDIO_LIMIT)` | `Closing` |
| `Streaming` | phone binary, cumulative bytes above C-72 | `Fail(AUDIO_LIMIT)` | `Closing` |
| `Streaming` | phone binary, lab queue would pass C-73 | `Fail(AUDIO_LIMIT)` | `Closing` |
| `Streaming` | phone binary, otherwise | Queue it for the lab unchanged. Restart the idle timer | same |
| `Streaming` | phone text larger than C-25, or not a JSON object whose `type` is the string `endStream` | `Fail(UNSUPPORTED_COMMAND)` | `Closing` |
| `Streaming` | phone text `endStream` | Queue the end-of-stream marker behind the audio. Stop the idle timer. Start the finalize timer (C-53) | `Finalizing` |
| `Streaming` | idle timer | `Fail(AUDIO_IDLE_TIMEOUT)` | `Closing` |
| `Streaming`, `Finalizing` | lab text, not a JSON object; or a lab message of kind `Invalid` | `Fail(LAB_PROTOCOL_ERROR)` | `Closing` |
| `Streaming`, `Finalizing` | lab `error` | `Fail(LAB_INFERENCE_FAILED)` | `Closing` |
| `Streaming`, `Finalizing` | lab `speechStart`, `transcript`, `speechComplete` | Send the filtered frame. For `speechComplete` with a string `transcript`: publish `SpeechText` | same |
| `Streaming` | lab `streamComplete` | `Fail(LAB_PROTOCOL_ERROR)` | `Closing` |
| `Finalizing` | lab `streamComplete` | Send the filtered frame. Close with 1000 and an empty reason. Publish `SpeechStreamChanged(Stopped)` | `Closing` |
| `Finalizing` | finalize timer | `Fail(FINALIZE_TIMEOUT)` | `Closing` |
| `Streaming`, `Finalizing` | stream timer | `Fail(SESSION_LIMIT)` | `Closing` |
| `AwaitingLabReady`, `Streaming`, `Finalizing` | lab closed | `Fail(LAB_DISCONNECTED)` | `Closing` |
| `AwaitingLabReady`, `Streaming`, `Finalizing` | lab receive or send threw `LabSocketException` | `Fail(LAB_CONNECTION_FAILED)` | `Closing` |
| any before `Closing` | lab message of another `type`, or `audioProgress` | Dropped | same |
| any before `Closing` | `FrameTooLarge` | `Fail(FRAME_TOO_LARGE)`, close code 1009 | `Closing` |
| any before `Closing` | `WhileAccepting` cancelled | `Fail(RELAY_PAUSED)` | `Closing` |
| any before `Closing` | `PairingRevoked` cancelled | `Fail(PAIRING_CHANGED)` | `Closing` |
| any before `Closing` | `Stopping` cancelled | `Fail(RELAY_STOPPING)`, close code 1001 | `Closing` |
| any before `Closing` | `PhoneGone` | Cancel prepare wait and connect. Publish `SpeechStreamChanged(Stopped)` | `Closed` |
| any | lifetime timer; phone queue overflow (W-03); phone write stall (W-04) | Abort the phone socket | `Closed` |
| `Closing` | phone close frame received, or C-58 passed since the close was sent | Abort the socket if it is still open | `Closed` |
| `Closed` | (entry) | Stop every timer. Dispose the lab socket if `Closing` has not done so. Release the lease. Publish `SpeechStreamChanged(Stopped)` when the stream was admitted and has published no ending yet. Answer the phone's close frame with 1000 when the phone closed first and nothing is queued; otherwise abort the socket unless its close handshake is complete. Complete `RunAsync` | |

`Fail(code)` does this once per stream (a second failure is ignored, so exactly one `error` frame is ever
sent): queue the `error` frame of that code; after it is written, send the close frame with the close code of
protocol 10.6 (1008 from `Admitting`, 1009 for `FRAME_TOO_LARGE`, 1001 for `RELAY_STOPPING`, otherwise 1011)
and the code as the reason; start the C-58 wait; publish `SpeechStreamChanged(Failed, code, relay text)`
unless the failure happened in `Admitting`.

How the table is read, where it leaves a choice:

- A row for one state wins over an "any" row for the same event. In `AwaitingLabReady` every lab text other
  than the expected `ready` is `MODEL_NOT_READY`, an event of a type the relay does not know included: the
  lab's first message must be `ready`. In `Preparing`, `Connecting`, `AwaitingLabReady` and `Finalizing`
  every phone message is `AUDIO_OUT_OF_SEQUENCE`, an empty frame and a text of an unknown type included
  (the one rule of protocol 10.5). Only a message beyond C-20 is `FRAME_TOO_LARGE` instead, in every state
  before `Closing`.
- Entering `Closing`, by `Fail` or by the clean end, stops every timer but the lifetime, cancels the wait on
  prepare, the connect and both lab loops, and disposes the lab socket at once: it has no further use, and
  the lab ends its session when the connection ends. A lab socket that the connector still delivers
  afterwards is disposed unused. The lease is not released earlier for it (see below).
- An admitted stream publishes exactly one ending: `Failed` from `Fail`, otherwise `Stopped`. The rows that
  abort the socket (lifetime, W-03, W-04) publish `Stopped` on entering `Closed` when nothing was published
  before.
- The C-58 wait is counted from the moment the writer reaches the close frame. A frame before it that cannot
  be written is ended by W-04.
- C-55 is a last line. Every state has a shorter limit of its own (C-51 while loading, C-52 and C-54 while
  streaming, C-53 while finalizing, W-04 and C-58 while closing), so with those working the lifetime never
  fires first; when it does fire, the socket is aborted whatever the state.
- An empty audio frame while streaming is dropped and is not audio: the idle limit keeps counting.

Lease (decision A-07): a failure or `PhoneGone` in `Preparing` releases the lease at that moment, before the
close handshake, and cancels only the wait on `PrepareAsync`. In every later state the lease is released on
entering `Closed`, when both sockets are closed. So after `ready` a failed stream holds the lease until the
phone has answered the close or C-58 has passed. One socket may outlive the lease by a moment: when the
phone closed first, its close frame is answered after the lease is released, since the stream is over for
the phone already.

**Frames the relay builds** (`SpeechFrames`), members in this order: `loading`: `type`, `provider`,
`elapsedSeconds`, `message`; `ready`: `type`, `provider`, `sampleRate`, `handshakeMs` (milliseconds since the
upgrade, one decimal); `error`: `type`, `code`, `message`. In both `loading` and `ready`, `provider` is the
requested model id exactly as it arrived in `X-LiveCue-Speech-Model` (`nemotron` or `qwen3`), never a label
of 4.12: the phone compares it with the id it sent. `elapsedSeconds` and `sampleRate` are written as JSON
integers (`16000`, never `16000.0`).

**`LabEventFilter`** rebuilds the four lab-derived types from the allowlist of protocol 10.3, members in the
order `type`, `turnId`, `audioProcessedMs`, `finalizationMs`, `transcript`. A number is forwarded as its
original token when it is a JSON number that parses to a finite `double`; `transcript` when it is a string,
cut to C-22 code units, one fewer when the last kept unit is a high surrogate. Everything else is left out,
and so is a `transcript` whose escapes name half a surrogate pair (it is valid JSON, but not text).
If the encoded frame would still be longer than C-21 (possible only when the text is mostly characters that
the relay's writer escapes), `transcript` is cut further by the same rule until the frame fits: the longest
prefix that fits is kept. `SpeechText` carries the text as the frame carries it.

What the relay's writer escapes: `WireJson` uses the framework's relaxed encoder, which leaves the basic
plane as UTF-8 but still writes most control characters as `\u00XX` (six bytes) and a character outside the
basic plane as an escaped surrogate pair (twelve bytes instead of four). To a JSON reader that is the same
text, and the phone decodes it. It is not what protocol section 1 says ("non-ASCII text as UTF-8, not as
`\u` escapes"), and it is why a transcript of emoji alone would pass C-21 without the further cut.
`WireJson` is a frozen file: this is handed to the orchestrator and not changed here.

**`AudioLimiter`**: allowed bytes at a moment are the C-72 formula applied to the whole milliseconds since
`ready`, from the clock. **`LabSocketConnector`**: a `ClientWebSocket` to `ws://127.0.0.1:<LabPort>/relay`,
no proxy, no `Origin`, no offered extension or subprotocol, keep-alive off, connect bounded by C-47 on the
clock; it reports a binary message or a message longer than C-23 as `Invalid` (at once, without assembling
it; the rest of that message is skipped before the next one is read), a close frame or a prematurely closed
connection as `Closed`, and every other failure as `LabSocketException`. Disposing aborts the connection
without a close handshake. A refused loopback connection takes Windows about two seconds to report, inside
C-47.

### 4.12 Fixed texts (`Protocol/FixedTexts.cs`)

Model labels: `nemotron` is "Nemotron", `qwen3` is "Qwen3" (the labels of the relay's own `INVALID_MODEL`
text). They fill `<model>` in the `loading` frame's message (protocol 10.3) and in the texts below.

| Where | Text |
|---|---|
| `Notice(PairingRestored)` | "Saved pairing restored. Existing iPhones reconnect automatically; Show QR keeps their pairing valid." |
| `Notice(PairingReset)` | "Pairing reset. Every iPhone must pair again." |
| `SpeechStreamChanged(Loading)` | "Loading `<label>` on the PC GPU…" |
| `SpeechStreamChanged(Ready)` | "`<label>` ready · PC GPU · no speech API cost." |
| `SpeechStreamChanged(Stopped)` | "`<label>` transcription stopped." |
| `SpeechStreamChanged(Failed)`, `AssistantNotice`, `RequestFailed` | The relay text of the code (protocol 14.1) |
| `LocalModelChanged` | The status message (MSG-3 to MSG-9) |

### 4.13 Processes (`Processes/`)

`ProcessRunner(TimeProvider clock)`. For every process: `ProcessStartInfo` with `ArgumentList`,
`UseShellExecute` false, `CreateNoWindow` true, all three streams redirected, the environment cleared and
refilled by policy (`ChildEnvironment`). Standard error is read and discarded. Standard input is written as
UTF-8 without a byte-order mark and closed. The time limit is a clock timer. With `KillWithApplication` the
process handle is assigned to one job object created with the kill-on-job-close limit and held for the life
of the runner; ending a process is `Kill(entireProcessTree: true)` and a wait of W-08. Without it the process
is not in the job and ending it is `Kill()` of that process only. Exceptions from `Process.Start` become
`StartFailed`; their messages are dropped.

`ChildEnvironment` fills the cleared environment by policy, comparing names without regard to case and
copying a value only when the parent has that variable: `Inherit` copies everything; `LabScriptAllowlist`
copies the thirteen names of protocol 11.5; `CodexAllowlist` copies those thirteen, `HOMEDRIVE`, `HOMEPATH`,
`CODEX_HOME`, `HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY` and `NO_PROXY` (protocol 12.6). The lists are two
`static readonly` arrays in that file and are written nowhere else.

### 4.14 Hosting: settings, discovery, prerequisites

**`settings.json`** (architecture 3.5): UTF-8, two-space indent, the twelve members in the order of the
architecture's example, `connectionMode` and each `ownedMappings[].mode` as `"private"` or `"public"`,
`createdAt` as UTC to the second. Unknown members are ignored at load and not written back. A member with the
wrong JSON type makes the file `Unreadable`. The two members the review added
(`codexContainmentCheckedVersion`, `hideFromScreenCapture`) may be absent from a file: absent means `null`
and `true`.

**A file that fails validation** (`SettingsStore`, architecture 3.5). `Load` returns `Invalid`, the problems,
and as `Settings` the defaults with `OwnedMappings` set to those entries of the file for which
`ISettingsValidator.IsValidLedgerEntry` is true, in file order. The file itself is not touched by `Load`.
`Save`, when the file on disk is `Invalid` or `Unreadable`, first writes that file's bytes to
`settings.json.invalid` (atomically, replacing an older copy) and only then replaces `settings.json`; when
the copy cannot be written the save throws `IOException` and nothing changed.

| Field | Rule | Problem code |
|---|---|---|
| `schemaVersion` | equals 1 | `UnsupportedVersion` |
| `relayPort` | 1024 to 65535; not 47831; not equal to `asrLabPort` | `OutOfRange`; `Forbidden`; `Duplicate` |
| `connectionMode` | `private` or `public` | `NotAllowed` |
| `privateHttpsPort` | 1 to 65535; not 443, 8443 or 10000 (the ports Funnel can use); not equal to `publicHttpsPort` | `OutOfRange`; `NotAllowed`; `Duplicate` |
| `publicHttpsPort` | 443, 8443 or 10000 | `NotAllowed` |
| `publicPathPrefix` | matches `^/[a-z0-9][a-z0-9-]{0,31}$` as a whole text | `BadFormat` |
| `codexExecutablePath` | null, or: an absolute path; file name is `codex.exe` without regard to case; the file exists | `NotFound`; `WrongFileName`; `NotFound` |
| `codexContainmentCheckedVersion` | null, or 1 to 64 characters of letters, digits, space, `.`, `_`, `+`, `-` | `BadFormat` |
| `asrLabFolder` | null, or: a drive-letter path (`X:\…`), so not a UNC path, not a device-namespace path (`\\?\`, `\\.\`), not relative and not drive-relative (`X:folder`); on a fixed local volume (`IHostFileSystem.IsOnFixedLocalDrive`); an existing folder; it holds `Start-ASR-Lab.ps1` and `Stop-ASR-Lab.ps1` | `NotAllowed`; `NotAllowed`; `NotFound`; `MissingScripts` |
| `asrLabPort` | 1024 to 65535; not 47831 and not 8765 (a duplicate of `relayPort` is reported on `relayPort`) | `OutOfRange`; `Forbidden` |
| `hideFromScreenCapture` | a JSON boolean (any other type makes the file `Unreadable`) | none |
| `ownedMappings[]` | `httpsPort` 1 to 65535; `path` is `/` for private, the prefix pattern for public; `target` is `http://127.0.0.1:` plus a port from 1024 to 65535 | `BadFormat` |

Every pattern is a whole-text match (protocol section 1): the prefix, the version text and the ledger `path`
are checked with a character loop or with `\A…\z`, so a value that ends in a line feed is `BadFormat`.

`ValidateStructure` is every rule except the three that ask the file system (the file exists; the folder is
on a fixed local volume; the folder exists and holds the scripts). The path-form half of the lab folder rule
needs no file system and is part of `ValidateStructure`.

**Lab folder** (`LabFolderResolver`): `asrLabFolder` when it is set and still holds both scripts; when it is
null, `<BaseDirectory>\asr` if that holds both scripts; otherwise null ("not configured"). Each time it
resolves (every relay start), the folder it would return must again be a drive-letter path on a fixed local
volume; one that is not gives null. So a `settings.json` edited by hand to a network share is "not
configured", not a path that reaches a process.

**Codex** (`CodexLocator`, architecture 3.7): (1) the configured path: exists gives `Setting`, missing gives
`ConfiguredFileMissing`; (2) each `PATH` entry in order: a file named exactly `codex.exe` gives `SearchPath`
(`.cmd` and `.ps1` shims are never considered); (3) the most recently written `codex.exe` under
`%LOCALAPPDATA%\OpenAI\Codex\bin\`, at most three levels deep, gives `UserInstall`; else `NotFound`.

**Tailscale** (`TailscaleLocator`): `Tailscale\tailscale.exe` under `IHostFileSystem.ProgramFilesDirectory`
(the known-folder API, not the `ProgramFiles` environment variable), else the first `tailscale.exe` on
`PATH`.

**Codex version** (`CodexSignInCheck.ReadVersionAsync`, architecture 3.7): arguments `--version`; environment
`CodexAllowlist`; 256 bytes of standard output kept; limit 15 s. The first line, trimmed, is returned when
the exit code is 0 and the line is 1 to 64 characters of letters, digits, space, `.`, `_`, `+`, `-`;
otherwise null. `PrerequisiteChecker` calls it once per check run when check 2 passed, whatever check 3
says, and puts the result in `PrerequisiteReport.CodexVersion`. `IsSignedInAsync` also runs with
`CodexAllowlist`.

**Checks** (`PrerequisiteChecker`), with the status-line texts of architecture 3.11:

| Id | Passes when | Detail |
|---|---|---|
| 1 `DataFolder` | The folder exists or was created and can be listed. `settings.json` is then loaded; an `Invalid` or `Unreadable` file is not a failure (defaults apply and the report carries the load result) | |
| 2 `CodexFound` | `ICodexLocator.Locate(override or setting)` has no problem | the path |
| 3 `CodexSignedIn` | `ICodexSignInCheck` is true | |
| 4 `TailscaleInstalled` | `ITailscaleCli.ExecutablePath` is not null | |
| 5 `TailscaleOnline` | `ReadStatusAsync` succeeded and `Online` is true | |
| 6 `TailscaleDnsName` | `DnsName` is not null | the name |

Check 6a of architecture 3.11 is not a `PrerequisiteId`: it is a step of the supervisor (4.16), because it
needs the mapping manager and applies to one mode only. The containment warning is not a check either: check
2 passes, and `PrerequisiteReport.ContainmentCheckIsStale` carries the warning, whose text is
`PrerequisiteTexts.ContainmentWarning(version, checkedVersion)`: "Codex is version `<v>`; the containment
check was last run for `<w>`." (with "Codex's version could not be read" in place of the first clause when
the version is null, and "the containment check has not been run yet" when nothing was recorded), then
"Run the containment check, then choose Mark as checked in Settings."

**Settled by the build (WP-W06)**, where the text above left a choice open:

- `settings.json` is written without a byte-order mark, with line feeds and one final line feed, like
  `pairing.json`. A byte-order mark in front of a file is accepted when it is read.
- "Wrong JSON type" and "wrong value" are told apart, so that as much of a damaged file as possible stays
  `Invalid` (which keeps the ledger) rather than `Unreadable` (which keeps nothing). `Unreadable`: not a JSON
  object; a member of the ten of the first format missing; a member, or a member of a ledger entry, of the
  wrong JSON type; a ledger entry that is not an object or lacks one of its five members. `Invalid`: a number
  that is not a 32-bit integer (`OutOfRange`, `NotAllowed` or `UnsupportedVersion` by its field); a mode text
  other than the two words (`NotAllowed`); a ledger entry whose mode is neither word or whose `createdAt` is
  not an ISO 8601 time (`BadFormat`, that entry only).
- A ledger `target` must be the port in its plain decimal form: `http://127.0.0.1:047841` is `BadFormat`.
- `createdAt` is stored as UTC to the second. An entry that is saved and loaded again therefore differs from
  the one in memory by its fraction of a second: code that looks for "this entry" in the ledger of a later
  load compares mode, port, path and target, or builds its entries with whole seconds.
- The "letters" and "digits" of the Codex version text are the ASCII ones.
- `LabFolderResolver` holds a lab folder given for one run (the console host's `--lab`) to the same rule as
  the setting.
- `CodexLocator`: the configured path must be absolute; an entry of `PATH` that is not an absolute path is
  skipped (it would depend on the current folder); `%LOCALAPPDATA%` is read through
  `IHostFileSystem.GetEnvironmentVariable`, the only source the seam offers.
- `ReadVersionAsync` and `IsSignedInAsync` answer "unknown" and "not signed in" for a path the process runner
  refuses to start, instead of throwing.

### 4.15 Hosting: the mapping manager (`Tailscale/`)

**Reading the serve status** (`ServeStatusParser`). The document must be a JSON object; otherwise
`Unparseable`. `TCP`, `Web`, `AllowFunnel`, `Foreground` and `Services` are each optional but, when present,
must be objects; inside `Foreground` and `Services` every value is read with the same three top-level shapes.
A port is taken from a `TCP` key (a number) and from the text after the last colon of a `Web` or
`AllowFunnel` key. `MentionedPorts` is every port seen anywhere. `TcpForwardPorts` are `TCP` entries with a
`TCPForward` member. `FunnelPorts` are `AllowFunnel` entries whose value is `true`. A `Web` handler's kind is
the first of `Proxy`, `Path`, `Text`, `Redirect` it has. `Leaves` is the whole document flattened with JSON
pointers (RFC 6901).

**Terms.** For a port `p`: *occupied* means `p` is in `MentionedPorts`. A handler is *ours* for
(`p`, mount, relay port) when its port and mount are those, its kind is `Proxy`, its target is exactly
`MappingCommand.TargetFor(relayPort)` and it is not a foreground or service handler. A port *holds only ours*
when our handler is its only handler and it has no TCP forwarding. For TS-4 and TS-5 a port that holds only
ours counts as unoccupied.

**Inspection of one mode** (`MappingRules.Inspect`), first match:

| Mode | Condition | State |
|---|---|---|
| both | Tailscale not installed / status failed / status unparseable | `Unknown` with that refusal |
| Private (`p` = private port, mount `/`) | our handler exists | `Present` when the ledger has (private, `p`, `/`, same target), else `PresentNotOwned` |
| Private | another handler is mounted at `/` on `p`, or `p` is occupied by anything else | `Blocked(PrivatePortHasFunnel)` when Funnel is on for `p`, else `Blocked(PrivatePortOccupied)` |
| Private | otherwise | `Absent` |
| Public (`p` = public port, mount = prefix) | our handler exists and Funnel is on for `p` | `Present` or `PresentNotOwned` as above |
| Public | a handler that is not ours is mounted at the prefix or at the prefix plus `/` | `Blocked(PublicMountOccupied)` |
| Public | `p` forwards TCP | `Blocked(PublicPortHasTcpForward)` |
| Public | Funnel is on for `p`, or `p` is unoccupied | `Absent` |
| Public | otherwise | `Blocked(PublicPortWouldPublishOthers)` |

`StaleOwned`: every ledger entry whose handler is still ours by the entry's own port, path and target, and
which is not the selected mode's entry for the current port and path.

**Exposure report** (`MappingRules.Exposure`, architecture 3.9). From the same snapshot: every
`ServeHandler` whose `Kind` is `Proxy` and whose `Target` is exactly
`MappingCommand.TargetFor(settings.RelayPort)`, on any port, at any mount, foreground or not. For each:
`FunnelOn` is "its port is in `FunnelPorts`"; `LedgerEntry` is the ledger entry with the same port, the same
path (the mount), the same target, and mode Public when `FunnelOn`, else Private; or null. Sorted by port,
then mount. It is computed from what Tailscale reports, never from the ledger, so it also shows a handler
made by hand after a timed-out set-up (TS-10), one left by a process that died between steps 5 and 8 below,
one whose ledger was lost, and one that other software pointed at the relay port. When the status could not
be read, `Exposure` is empty and `StatusFailure` says so: the desktop then shows "unknown", never "nothing".

What the desktop does with a row (5.5, 5.8): the selected mode's own mapping at its configured port and path
is not a warning; a row with a ledger entry is the `StaleOwned` warning with "Remove…"; a row without one is
an `UnownedExposureRow` with its removal command as text. No removal plan can exist for a row without a
ledger entry (TS-8).

**`ExecuteAsync(plan)`**:

1. No executable: `Refused(TailscaleNotInstalled)`.
2. Read the serve status into `before` (TS-1). Failure: `Refused(StatusUnavailable)` or
   `Refused(StatusUnreadable)`. Nothing is written.
3. Load the saved settings. Apply plan: build the plan again from them; a different argument list is
   `Refused(PlanOutOfDate)`. Removal plan: the entry must still be in the ledger, else
   `Refused(PlanOutOfDate)`.
4. Apply: inspect that mode on `before`. `Present` or `PresentNotOwned`: `Refused(AlreadyPresent)`.
   `Blocked`: `Refused` with its reason (TS-4, TS-5). Removal (TS-8): no handler at the entry's port and path:
   `Refused(NotPresent)` and the stale entry is dropped from the ledger; a handler there that is not exactly
   the entry's (kind, target): `Refused(LedgerMismatch)`. A handler that is ours by shape but has no ledger
   entry can never be planned for removal, because removal plans are made from ledger entries only
   (`NotInLedger` is what the page shows for it).
5. Run `plan.Command` (TS-10). `Failed` or `TimedOut` is remembered as the outcome.
6. Read the serve status into `after`. Failure: outcome `NotVerified`.
7. Verify: after an apply our handler exists (and Funnel is on, for Public); after a removal no handler is at
   that port and path. A command that reported success but fails this is `NotVerified`.
8. Ledger: add the entry (apply) or delete it (removal); drop every entry that no longer matches a live
   handler; save. A failed save is `LedgerNotSaved`.
9. TS-7: compare `before.Leaves` with `after.Leaves`. Ignore leaves under `/Web/<host>:<p>/Handlers/<mount>`
   for our port and mount, and, only when `p` was unoccupied or held only ours in `before`, leaves under
   `/TCP/<p>`, `/AllowFunnel/<host>:<p>` and `/Web/<host>:<p>`. Any other difference sets `OthersChanged`.
10. Return the outcome, the settings as saved and the inspection of `after`.

The ledger entry is written only after the mapping was verified, so the application never claims a handler it
did not see itself create. If the process dies between steps 5 and 8, the handler is `PresentNotOwned`: it
works, and the Settings page shows the command the user can run by hand to remove it.

**Texts** (`MappingTexts`). `<address>` is the advertised endpoint of that mode, or "this PC's Tailscale
address" when the name is unknown.

| Key | Text |
|---|---|
| Effect, apply Private | "Tailscale serves `<address>` inside your tailnet and forwards it to the LiveCue relay on this PC (port `<relay port>`, this PC only). Nothing becomes reachable from the internet." |
| Effect, apply Public | "Tailscale Funnel publishes `<address>` on the internet and forwards it to the LiveCue relay on this PC (port `<relay port>`, this PC only). Anyone can reach the address; only an iPhone with the pairing token gets past it." |
| Effect, remove Private | "Tailscale stops serving `<address>` inside your tailnet. iPhones paired with that address can no longer reach this PC." |
| Effect, remove Public | "Tailscale stops publishing `<address>` on the internet. iPhones paired with that address can no longer reach this PC." |
| `StatusUnavailable`, `StatusUnreadable` | "LiveCue could not read Tailscale's configuration, so it changed nothing. Check that Tailscale is running, then choose Check again." |
| `TailscaleNotInstalled` | "Tailscale is not installed in Program Files." |
| `InvalidSettings` | "The connection settings are not valid. Check the ports and the path, then save." |
| `ForbiddenRelayPort` | "Port 47831 belongs to the old LiveCue and is never used. Choose another relay port." |
| `PrivatePortOccupied` | "Port `<n>` already has other Tailscale handlers. Choose another private HTTPS port." |
| `PrivatePortHasFunnel` | "Port `<n>` has Funnel turned on. A private listener there would turn Funnel off for the whole port. Choose another private HTTPS port." |
| `PublicMountOccupied` | "The path `<path>` on port `<n>` is already used by something else. Choose another path." |
| `PublicPortHasTcpForward` | "Port `<n>` forwards raw TCP and cannot take a Funnel path. Choose another public port." |
| `PublicPortWouldPublishOthers` | "Port `<n>` has tailnet-only handlers. Turning Funnel on there would publish them to the internet. Choose a port that already has Funnel on, or an unused one." |
| `AlreadyPresent` | "The connection is already set up." |
| `NotPresent` | "That mapping no longer exists in Tailscale. LiveCue removed it from its own list." |
| `NotInLedger` | "This mapping was not created by LiveCue, so LiveCue will not remove it." |
| `LedgerMismatch` | "The mapping no longer matches what LiveCue created, so LiveCue will not remove it." |
| `PlanOutOfDate` | "The settings changed while this dialog was open. Close it and look at the command again." |
| `CommandFailed` | "Tailscale HTTPS setup failed. Check that Tailscale is connected." (decisions 6.2); for a removal: "Tailscale could not remove the mapping. Check that Tailscale is connected." |
| `CommandTimedOut` (TS-10) | "Tailscale did not finish in time. Run the command shown above in a terminal to see Tailscale's own instructions. The first use of HTTPS or Funnel on a tailnet must be approved in the Tailscale admin console." |
| `NotVerified` | "Tailscale reported success, but the mapping is not in its configuration. Choose Check again." |
| `LedgerNotSaved` | "The mapping changed, but LiveCue could not save its settings. LiveCue will treat the mapping as not its own." |
| `OthersChanged` (TS-7) | "Warning: other entries of Tailscale's serve configuration changed during this command. LiveCue repaired nothing. Review them with: tailscale serve status" |
| `Exposure(handler)`, Funnel on | "A Tailscale Funnel mapping on port `<n>`, path `<mount>`, publishes this relay on the internet. LiveCue did not create it and will not remove it." |
| `Exposure(handler)`, Funnel off | "A Tailscale mapping on port `<n>`, path `<mount>`, forwards to this relay inside your tailnet. LiveCue did not create it and will not remove it." |
| `Exposure(handler)`, foreground or service | The sentence above, then: "It belongs to a running Tailscale session or service; end that to remove it." |
| `ManualRemoval(handler)` | Funnel on: `tailscale funnel --https=<n> --set-path=<mount> off`. Funnel off: `tailscale serve --https=<n> --set-path=<mount> off`. Empty for a foreground or service handler, and empty when `<mount>` is not `/` or `/` followed by 1 to 128 characters of letters, digits, `.`, `_`, `~`, `-` and `/` (whole text): a mount with any other character is never put into a command the owner is asked to paste. The Settings row then reads "Remove it with Tailscale's own tools: tailscale serve status" |
| Exposure unknown | "Tailscale's configuration could not be read, so LiveCue cannot tell what is exposed. Choose Check again." |

`ManualRemoval` returns text only. It never builds a `MappingCommand`, so nothing it returns can be run by
the application (TS-2, TS-3 and TS-8 are untouched).

### 4.16 Hosting: endpoint, supervisor, console host

`EndpointResolver.Resolve`: builds `https://<dns>:<privateHttpsPort>/` or
`https://<dns>:<publicHttpsPort><prefix>/` and passes it through `AdvertisedEndpoint.TryCanonicalize`.

`RelaySupervisor.StartAsync` (architecture 3.11), serialised by a semaphore of one:

| Step | Action | Events |
|---|---|---|
| 1 | State `Checking` | `CheckingStarted` |
| 2 | `IPrerequisiteChecker.CheckAsync`; adopt the loaded settings | `PrerequisitesChecked` |
| 3 | A failure: State `Stopped`; return `PrerequisiteFailed` with `PrerequisiteTexts.Failure(first id)` | |
| 4 | Build `RelayOptions`: port, mode, endpoint (the override, else `Resolve`; null is treated as fault `InvalidEndpoint`), data folder, the Codex path of the report, `LabFolderResolver`, lab port, version | |
| 4a | Check 6a of architecture 3.11. Only when the mode is Private and no endpoint override is in force: `IMappingManager.InspectAsync`. When `HasFunnelExposure`: State `Stopped`; return `ExposedInPrivateMode` with `PrerequisiteTexts.ExposedInPrivateMode`; no relay is created. A status that could not be read does not stop the start | `ConnectionInspected`, only when it refuses |
| 5 | State `Starting`; create the relay through `IRelayFactory`; start the pump that forwards its events | `RelayStarting`, then `RelayEventForwarded` for each |
| 6 | `relay.StartAsync`. `RelayStartException`: dispose the relay; State `Stopped`; return `RelayFault` with `PrerequisiteTexts.StartFault(code, port, mappingPointsAtPort)`. For `PortInUse`, and unless an endpoint override is in force, the supervisor first runs one read-only `InspectAsync` (publishing `ConnectionInspected`); `mappingPointsAtPort` is true when its `Exposure` is not empty, which selects the second port-in-use text of architecture 3.11. The `RelayFaulted` event of the relay still carries the relay's own fixed message; hosts show the supervisor's text | |
| 7 | State `Running` | |
| 8 | Unless an endpoint override is in force: `IMappingManager.InspectAsync` | `ConnectionInspected` |

A start fault that happens before a relay exists (no endpoint in step 4, or a relay port of 47831, which never
reaches a relay) is published as `RelayEventForwarded(RelayFaulted)` with the step 7 text, so that a host can
follow events alone. The events a relay publishes during its own start are forwarded as soon as its
`StartAsync` returns, in order and before anything else; the pump forwards every later one.

When the pump forwards `RelayStopped(Fault)`, the supervisor disposes that instance and sets State `Stopped`.
`StopAsync` calls the relay's `StopAsync`, waits for the pump to finish, disposes the instance.
`ProductionRelayFactory.Create` calls `RelayFactory.Create(options, new RelayDependencies { Clock, Processes })`.

**Console host** (`LiveCue.Relay.Host/Program.cs`, architecture 3.14): parse the arguments (unknown argument:
print usage, exit 2); build `HostingServices`; `--check-only`: `CheckAsync`, print "LiveCue Relay
prerequisites passed." and exit 0, or print the failure text and exit 2. Otherwise `StartAsync`: a
prerequisite failure prints its text and exits 2; a relay fault prints its text and exits 3. Then print each
event (`ConsoleEventPrinter`: one text line, or with `--events json` one JSON object with `type` and the
record's members, enums as their names). With `--show-pairing` nothing is started and no port is bound, so it works
while the desktop app runs: run the checks (the Codex results are ignored), take the endpoint override or
resolve the endpoint from the report's settings and DNS name exactly as step 4 does, call
`StoredPairingExport.ReadPayloadAsync(dataFolder, endpoint)`, print the payload text on one line and the
terminal QR (two modules per character row using half-block characters, four-module quiet zone) once, and
exit 0. No pairing file, an unreadable one, or no endpoint prints a fixed text and exits 2. Ctrl+C: `StopAsync`, exit 0. The host never prints the
payload without that flag and has no reset argument.

What the review added to the console host (architecture 3.14). First, `--show-pairing` on the default data
folder needs an interactive console: when standard output is redirected the host prints one fixed line that
contains nothing of the payload and exits 2, before it reads the pairing. A run with an explicit
`--data-dir` may be redirected, which is what `scripts/verify-pc-asr.ps1` relies on. "Redirected" is
`Console.IsOutputRedirected`, read once in `Program.Main` and handed to `HostProgram.RunAsync` with the
output writer, so the tests set it. Second, `ConsoleEventPrinter` never prints conversation text in text
mode, and in JSON mode it leaves out the members `Transcript`, `PartialTranscript`, `Instruction` and
`PayloadJson` of the request events and `Text` of `SpeechText`, unless the new argument `--include-content`
is given. Third, a start that returns `ExposedInPrivateMode` prints its text and exits 2, like a
prerequisite failure. Fourth, a stale containment check (`ContainmentCheckIsStale`) prints its warning line
once after the checks and changes no exit code.

**Settled by the build (WP-W06)**, where the text above left a choice open:

- Supervisor. The three inspections of a start (step 4a, the one after `PortInUse`, step 8) are made for the
  settings in force for that run, the overrides of the run applied: the exposure report is about the port the
  relay really asks for. `InspectConnectionAsync` on demand uses the saved settings. A start that arrives
  while a stored-pairing reset holds the gate waits for it; one that arrives while another start or a stop is
  under way returns `AlreadyRunning` at once. `StopAsync` gives the relay W-02 plus one second, on the
  supervisor's clock, for its stop and its dispose together, and then abandons that instance.
- `EndpointResolver` removes one trailing dot from the name and nothing else: a name with a space or a line
  break gives null.
- `HostProgram.RunAsync(args, services, output, cancel)`: `output` is a `HostOutput(Writer, IsRedirected)`.
- Exit codes: 0 after a clean stop, a passed `--check-only` and a shown pairing; 2 for a usage error, a failed
  prerequisite, the refusal of check 6a and a pairing that cannot be shown; 3 for a relay start fault and for
  a relay that stops by itself while the host runs (the host then ends instead of waiting for Ctrl+C; it
  never restarts the relay).
- Arguments. `--data-dir`, `--codex` and `--lab` are made absolute against the current folder. A `--data-dir`
  that is the previous LiveCue's `%LOCALAPPDATA%\LiveCue` folder, or lies inside it, is a usage error.
- Text lines. A `RelayFaulted` event prints `Relay fault: <CODE>.`; the text of architecture 3.11 is the
  host's last line. `PhoneSeen` prints "iPhone seen." once until another line is printed. `--check-only`
  prints its result line first and the containment warning after it.
- JSON lines (`--events json`): `type` is the record's name, then `at`, then the record's other members in
  camel case; an event of the relay is written as itself, not wrapped in `RelayEventForwarded`. So that every
  line of a run is one JSON object, the host's own lines are objects too:
  `{"type":"Warning","message":…}` for the containment warning and
  `{"type":"Failure","message":…,"exitCode":…}` for the text it ends with. `--check-only` and
  `--show-pairing` print text in either form.
- `--show-pairing`: a failed check other than the two Codex checks prints its text of architecture 3.11; no
  endpoint prints "The PC address could not be resolved."; no pairing file prints "No pairing has been
  created yet. Start LiveCue once, then run this again."; an unreadable one prints the text that
  architecture 3.11 gives for `PAIRING_STORE_UNREADABLE`. The line for a redirected output on the default
  folder is "The pairing is shown only in an interactive console. Run --show-pairing in a terminal window,
  without redirecting its output."
- Terminal QR. `TerminalQr` encodes the payload text itself, with the parameters of `QrMatrix.Encode` (4.7)
  and the QR package the relay already ships, because the relay's public surface hands out a matrix only from
  a running relay. Light modules are the written characters and dark modules are left empty, so that the
  code is the right way round on a dark terminal. For this one run the console is switched to UTF-8 and
  switched back; a redirected output keeps its encoding.

---

## 5. Desktop internals

The desktop starts from the `spike/windows` window, which already proves the ground, the two-layer glass, the
custom chrome, the theme slot and off-screen rendering. Layout and copy come from screens; this section says
how they are built. MVVM helpers come from `CommunityToolkit.Mvvm` (`ObservableObject`, `RelayCommand`,
`AsyncRelayCommand`); the spike's own `ObservableObject` and `Commands` files are deleted by WP-W08.

### 5.1 Window chrome (`Shell/MainWindow.xaml`)

| Item | Value |
|---|---|
| Size | Default 1240 x 660, centred; `MinWidth` 960, `MinHeight` 620 (R-04) |
| Title, icon | "LiveCue"; `Assets/LiveCue.ico` |
| Backdrop | Opaque. No Mica, no Acrylic, no `AllowsTransparency`, no `ThemeMode` (DS 3.5) |
| `WindowChrome` | The values of DS 3.5 |
| `SourceInitialized` | The two `DwmSetWindowAttribute` calls of DS 3.5; failures ignored |
| Maximised | Root margin of DS 3.5 |
| Root | `UseLayoutRounding`, `SnapsToDevicePixels`, `TextOptions.TextFormattingMode="Display"` (DS 3.5, "keeping text crisp") |
| Layers, bottom to top | `BloomLayer`; title bar (48) over the layout grid (sidebar 232, page host); dialog layer (scrim and card); toast layer |
| Title bar | Brand mark 20 and "LiveCue" on the left; the glass capsule with three caption buttons on the right; nothing else. Automation names "Minimize", "Maximize" or "Restore", "Close". Not tab stops |
| Inactive window | Wordmark and glyphs switch to `InkSecondaryBrush` |

### 5.2 Navigation

- The sidebar is a `ListBox` (`Nav.List`, items `Nav.Item`) bound two-way to `IShellViewModel.SelectedPage`;
  one row is always selected; Up and Down move the selection. Ids `NavLive`, `NavPairing`, `NavActivity`,
  `NavSettings`.
- The page host is a `ContentControl` bound to `CurrentPage`. `Shell/PageTemplates.xaml` maps each of the four
  view-model classes to its page view with a `DataTemplate`. All four view models live for the whole run, so
  the QR countdown and the log keep running while another page is shown (WIN-20).
- A page change cross-fades the page host over `MotionBase`; with Reduce Motion it is instant (DS 6.7).
- `InputBindings` on the window: Ctrl+1 to Ctrl+4 run `NavigateCommand`; Escape runs `EscapeCommand`.
- The sidebar status card binds to `SidebarRelayStatus` and `SidebarPhoneStatus`; its caption is "Closing this
  window stops the relay."
- View models navigate through `INavigator` only: `OpenSettingsCommand`, and the Pairing view model when a
  QR is produced while another page is shown (first start, reset).

### 5.3 Resource dictionaries

Merge order in `App.xaml` (the spike's): the theme slot (`Daylight.xaml`, replaced by `HighContrast.xaml`),
`Metrics.xaml`, `Typography.xaml`, `Icons.xaml`, `Interaction.xaml`, then the component dictionaries, then
`Shell/PageTemplates.xaml`.

**Token keys.** A DS token becomes a resource key by removing the dots and capitalising each part, with a
suffix for its type: `Brush` for a brush, `Color` for a colour, none for numbers, radii and effects.
`surface.panel` is `SurfacePanelBrush`; `shadow.raised` is `ShadowRaised`; `radius.card` is `RadiusCard`.
Views and styles reference colour keys through `DynamicResource` (so the contrast dictionary can replace
them) and effects through `StaticResource` inside `Daylight.xaml` only.

| Dictionary | Keys (each token of the DS section named) |
|---|---|
| `Daylight.xaml` and `HighContrast.xaml` (identical key sets; a test compares them) | DS 2.2: `GroundBaseBrush`, `GroundBloom1Color` to `GroundBloom4Color`. DS 2.3: `SurfacePanelBrush`, `SurfaceInsetBrush`, `SurfaceRaisedBrush`, `SurfaceRaisedHoverBrush`, `SurfaceRaisedPressedBrush`, `SurfaceOverlayBrush`, `SurfaceInverseBrush`, `SurfaceScrimBrush`, `SurfaceQrBrush`, `StateHoverBrush`, `StatePressedBrush`, `StateHoverOnOverlayBrush`, `StatePressedOnOverlayBrush`, `StateCaptionHoverBrush`, `StateCaptionCloseHoverBrush`. DS 2.4: `StrokeEdgeBrush`, `StrokeRaisedBrush`, `StrokeInsetBrush`, `StrokeOnAccentBrush`, `StrokeHairlineBrush`, `StrokeControlBrush`, `StrokeDangerBrush`, `StrokeFocusBrush`, `GlassSheenBrush`. DS 2.5: `InkPrimaryBrush`, `InkSecondaryBrush`, `InkDisabledBrush`, `InkOnAccentBrush`, `InkOnInverseBrush`. DS 2.6: `AccentFillBrush`, `AccentHoverBrush`, `AccentPressedBrush`, `AccentStrongBrush`, `AccentTintBrush`, `AccentTextBrush`. DS 2.7: `StatusLiveBrush`, `StatusSuccessBrush`, `StatusWarningBrush`, `StatusDangerBrush`, `StatusOfflineBrush`, each with its `…TintBrush`, plus `StatusDangerHoverBrush`, `StatusDangerPressedBrush`. DS 2.8: `TrackProgressBrush`, `TrackSegmentedBrush`, `TrackToggleOffBrush`, `ControlKnobBrush`, `ControlScrollThumbBrush`, `ControlScrollThumbHoverBrush`, `ShadeColor`. DS 4.15: `QrModuleBrush` (the QR ink; black in the contrast dictionary). DS 8.2: `BrandMarkBubbleBrush`, `BrandMarkBarsBrush`. DS 2.15: `OpacityDisabled`. DS 2.13: `ShadowPanel`, `ShadowRaised`, `ShadowOverlay`, `ShadowAccent`, `ShadowKnob` |
| `Metrics.xaml` | DS 2.11, 2.12, 2.16, 2.17: `RadiusPanel`, `RadiusPanelCompact`, `RadiusCard`, `RadiusField`, `RadiusTile`, `RadiusSmall`, `InsetScreen`, `InsetPanel`, `InsetPanelCompact`, `InsetCard`, `GapPanel`, `GapGroup`, `GapInline`, `GapControl`, `SizeSidebar`, `SizeTitleBar`, `SizeQrPanel`, `SizeReadingMax`, the button, row, chip and icon sizes, `MotionPress`, `MotionFast`, `MotionBase`, `MotionEmphasis`, `EaseStandard`, `EaseOut`, `EaseIn`, `EaseInOut` |
| `Typography.xaml` | DS 2.10: `FontFamilyDisplay`, `FontFamilyText`, `FontFamilyMono`; for each of the ten type tokens `Type<Name>Size`, `Type<Name>LineHeight` and the `TextBlock` style `Type.<Name>` (`Type.Display` … `Type.Mono`) |
| `Icons.xaml` | One `Geometry` per icon of DS 2.18 that the screens use, keyed `Icon.<Name>` |

**Two-layer glass** (`Glass.xaml`, DS 3.5). A surface that casts a shadow is two sibling `Border`s in one
`Grid`: the *caster* carries the fill and the only `Effect` and has no children; the *face* carries the
sheen, the edge stroke and the content and has no `Effect`, so text keeps ClearType. No element with an
`Effect`, an `OpacityMask` or a `BitmapCache` has a text descendant; a test walks the visual tree of every
self-test scene and fails on one (DS section 11 item 6). Rules W-1 and W-2 of DS 3.5 hold: nothing
translucent floats over scrolling content, and panels never overlap panels.

**Component style keys.** The foundation makes every key exist (the spike's style where it has one, otherwise
a plain style); WP-W08 gives each its DS appearance. Pages use only these keys and the token keys.

| Key | DS | Key | DS |
|---|---|---|---|
| `Glass.Panel`, `Glass.PanelCompact`, `Glass.Card`, `Glass.CardProminent` | 4.9 | `Button.Primary`, `Button.Primary.Session`, `Button.Secondary`, `Button.Secondary.Session`, `Button.Quiet`, `Button.Destructive`, `Button.DestructivePrimary` | 4.1, 4.2, 4.17 |
| `Button.Icon`, `Button.Icon.Quiet` | 4.3 | `Segmented` (style of `Controls.SegmentedControl`) | 4.4 |
| `Field.Text`, `Field.ReadOnly` | 4.6 | `Nav.List`, `Nav.Item` | 4.8 |
| `Row.List`, `Row.Check` (styles of `ListRow`, `CheckRow`) | 4.8 | `Dialog.Scrim`, `Dialog.Card` | 4.10 |
| `Toast`, `Banner` (style of `Controls.Banner`, tone from its `Tone` property) | 4.11 | `Chip` (style of `StatusChip`) | 4.12 |
| `Indicator` (style of `StatusIndicatorView`) | 4.22 | `Spinner` | 4.13 |
| `Log.Text`, `Page.Scroller`, `Divider`, implicit `ToolTip` and `ScrollBar` styles | 4.23 | `Chrome.CaptionCapsule`, `Chrome.CaptionButton`, `Chrome.CaptionButton.Close`, `Chrome.Wordmark` | 3.5 |
| `Focus.Pill`, `Focus.Row` | 6.6 | `EmptyState` | 4.14 |

### 5.4 The session: events to state (`Session/DesktopSession.cs`)

One background task reads `IRelaySupervisor.Events` and posts each event to the UI thread. There, inside one
`try` per event (WIN-10): the session updates its own state as below, then raises `RelayEventReceived`,
`Signal` or `MessageRaised`. An exception from the session or from any subscriber becomes one
`SessionMessage` "Relay diagnostic: An event could not be shown (`<exception type name>`)." and the next
event is processed normally.

| Event | Session state |
|---|---|
| `CheckingStarted` | `Checking` |
| `PrerequisitesChecked` | Stores the report. Failed: `PrerequisiteFailed`, `StateText` = `PrerequisiteTexts.Failure(first id)`. When `Report.ContainmentCheckIsStale` and the pair (version, recorded version) differs from the pair last warned about in this run of the app: raises `SessionMessage(Warning, PrerequisiteTexts.ContainmentWarning(…))` once |
| `StartAsync` returned `ExposedInPrivateMode` (a result, not an event) | `Faulted`; `FaultCode` null; `StateText` = the result's `FailureText` ("A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode."). The inspection that `ConnectionInspected` delivered just before stays stored, so the Live page and Settings show the handler |
| `StartAsync` returned `RelayFault` | `StateText` = the result's `FailureText`, replacing what the `RelayFaulted` row below set: this is how the second port-in-use text (architecture 3.11) reaches the window |
| `RelayStarting` | `Starting`; raises `Signal(RelayStarting)` |
| `RelayStarted` | `Ready`; stores the endpoint; presence `NotConnected`; when `PairingWasCreated`, sets "show the QR when the connection is ready" |
| `ConnectionInspected` | Stores it; recomputes `IsConnectionReady`; when that just became true and the flag above is set: clears the flag and calls `ShowPairingAsync` |
| `AcceptingChanged` | `IsPaused`; `Ready` or `Paused` unless the state is `Thinking` (then it changes when the request ends) |
| `PhoneSeen` | `LastPhoneSeen` = the PC clock now; presence `Connected` |
| `RequestStarted` | `Thinking` |
| `RequestCompleted` | `Paused` when `IsPaused`, else `Ready` |
| `RequestFailed` | `RequestFailed`. The next `RequestStarted`, pause or resume leaves it |
| `RelayFaulted` | `Faulted`; `FaultCode`; `StateText` by code: `PortInUse`, `PairingStoreUnreadable` and "other" as architecture 3.11 step 7; `Internal` (a fault while running): "Relay stopped. Click Start relay to reconnect." |
| `RelayStopped` | `Requested`: `Stopped`. `Fault`: stays `Faulted`. Both: presence `NotConnected`, `LastPhoneSeen` null, `IsPaused` false, `IsConnectionReady` false; raises `Signal(RelayEnded)`. The endpoint is kept |
| every other relay event | No state change; forwarded |

Presence: a clock timer (W-10) runs while the relay runs; `Connected` becomes `Idle` when the last
`PhoneSeen` is older than the threshold of architecture 3.12. It never becomes `NotConnected` by time.

Command methods run the supervisor or relay call on the thread pool and return on the UI thread. A call that
throws raises `SessionMessage(Error, "The desktop command failed. Please retry.")`. `ShowPairingAsync` and
`ResetPairingAsync` raise `PairingQrProduced` with the returned matrix; with the relay stopped,
`ResetPairingAsync` calls the supervisor's `ResetStoredPairingAsync`, clears a `PairingStoreUnreadable` fault
back to `Stopped`, raises `SessionMessage(Info, "Pairing reset. Every iPhone must pair again.")` and sets the
"show the QR when the connection is ready" flag. `ExecuteMappingAsync` turns its result into messages
(4.15 texts): one for the outcome, and a `Warning` for `OthersChanged`; a `Done` result stores the new
inspection, which can trigger the automatic first QR.

**Status texts** (`Session/StatusTexts.cs`; screens 2.3 and 1.3, decisions 6.2):

| State | Live status (mark, tone) | Sidebar label |
|---|---|---|
| `Stopped` | "Stopped" (ring, offline) | "Stopped" |
| `Checking`, `Starting` | "Checking Codex and Tailscale…" (spinner, live) | "Checking…" |
| `PrerequisiteFailed` | `StateText` (alert, danger) | "Cannot start yet" |
| `Ready` | "Ready" (dot, success) | "Ready" |
| `Paused` | "Paused" (pause, warning); caption "New requests from the iPhone are refused until you resume." | "Paused" |
| `Thinking` | "Thinking…" (spinner, live) | "Thinking…" |
| `RequestFailed` | "Request failed. Ready to retry." (alert, danger) | "Request failed" |
| `Faulted` | `StateText` (alert, danger; dot, warning for `Internal`) | "Relay stopped" |

Phone: "iPhone not connected" (ring, offline); "iPhone connected" (dot, success); "iPhone idle" (dot,
warning). Tooltip once a phone was seen: "Last authenticated heartbeat: `N` seconds ago. Idle does not prove
the phone is offline: iOS stops the heartbeat when the app is in the background."

### 5.5 Live page (`Live/`)

Bindings: one element per automation id of screens section 9.

| Id | Element | Bound to |
|---|---|---|
| `StartRelayButton` | `Button.Primary.Session`; busy shows the spinner | `StartRelayCommand`, `IsStartVisible`, `IsStartBusy`, `StartLabel` |
| `PauseRelayButton`, `StopRelayButton` | `Button.Secondary.Session` | `PauseResumeCommand`, `PauseResumeLabel`, `StopRelayCommand`, `AreRunningControlsVisible` |
| `RelayStatusText`, `PhoneStatusText` | `StatusIndicatorView` | `RelayStatus` (+ `RelayStatusCaption`), `PhoneStatus` |
| `PcAddressText` | Selectable `Type.Mono` text; copy icon button | `PcAddress`, `HasPcAddress`, `CopyPcAddressCommand` |
| `ConnectionBanner`, `LiveSetUpConnectionButton` | `Banner` (warning) with its button | `ConnectionBanner`, `SetUpConnectionCommand` |
| `ExposureBanner` | `Banner` (danger when a row has Funnel on in Private mode, otherwise warning) with the action "Open Settings"; shown in every state, also while the relay is stopped | `ExposureBanner`, `OpenSettingsCommand` |
| `PrerequisiteList` | Panel "Before the relay can start" with `Row.Check` rows, "Check again", "Open Settings" | `ArePrerequisitesVisible`, `Prerequisites`, `CheckAgainCommand`, `OpenSettingsCommand` |
| `TranscriptHint`, `TranscriptText` | Hint line; read-only selectable text in a `Glass.Card` that scrolls to the end on append | `TranscriptHint`, `TranscriptText`, `TranscriptSecondaryText`, `IsTranscriptPlaceholder` |
| `ReplyHint`, `ReplyText`, `ReplyTiming` | Hint line; `Type.Cue` text, then the two headed lists; spinner; danger `Banner`; caption | `ReplyHint`, `ReplyText`, `ReplyKeyPoints`, `ReplyActionItems`, `IsReplyThinking`, `ReplyFailure`, `ReplyTiming` |

`LiveViewModel` reactions (texts in `Text/LiveText.cs`; they are the strings of screens 2.7 and 2.8):

| Trigger | Transcript | Reply |
|---|---|---|
| Start of the app; `Signal(ActivityCleared)` | Hint "Live from your iPhone"; placeholder | Placeholder; hint "Powered by Codex on your PC" at start, "Activity cleared" after a clear; timing null |
| `RequestStarted` | Text = `Transcript`; secondary = "[In progress] " + partial, a blank line, "Instruction: " + instruction (each only when present); when all three are empty, text = the payload as indented JSON. Hint "Received from your iPhone" or "Session summary requested" | Thinking; hint "Processing with Codex on your PC"; failure and timing null |
| `RequestCompleted` | unchanged | From `PayloadJson`: `answer`, else `summary`; then a blank line and `details`; `keyPoints` and `actionItems` as lists; when all are empty, the payload as indented JSON. Hint "Sent to iPhone · `<model>` · `<effort>` · `<CodexMs / 1000, two decimals>` s Codex". Timing "PC relay total: `<DurationMs / 1000, three decimals>` s. Codex includes startup and cloud processing." |
| `RequestFailed` | unchanged | Banner: title "Nothing was sent to the iPhone"; body "Cancelled by the iPhone." for `REQUEST_CANCELLED` and `CLIENT_DISCONNECTED`, otherwise "Assistant request failed or timed out. Check Codex sign-in and retry." Hint "Not sent. Try Assist again." |
| `AssistantNotice`, `LocalModelChanged`, `SpeechStreamChanged` | Hint = the event's message | unchanged |
| `SpeechText` | The text is appended as a new line; above the upper bound of architecture 3.12 the text is cut to its newest part of the lower bound, starting at a character boundary | unchanged |
| `Signal(RelayEnded)` | Hint back to "Live from your iPhone"; text kept | Hint back to "Powered by Codex on your PC"; timing null; text kept |

`ConnectionBanner` is shown when the relay runs and the selected mode's mapping is `Absent`, `Blocked` or
`Unknown`: body "Connection not set up. Choose Set up connection to let your iPhone reach this PC." (for
`Blocked` and `Unknown`, followed by the reason text of 4.15); action "Set up connection…", enabled only for
`Absent`. Prerequisite rows use the titles and fixes of screens 2.6; a `NotChecked` row shows its pass title
in `ink.secondary` with the subtitle "Not checked". The row of check 2, when it passed and
`ContainmentCheckIsStale`, shows the containment warning (4.14) as its subtitle instead of the path.

`ExposureBanner`, from `session.Connection` (first match; null when there is no inspection yet):

| Condition | Banner |
|---|---|
| The selected mode is Private and `HasFunnelExposure` | Danger. Title "This relay is reachable from the internet". Body "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode." |
| `UnownedExposure` is not empty | Warning. Body "`<N>` Tailscale mapping(s) that LiveCue does not manage point at this relay. Open Settings to see them and how to remove them." |
| `StatusFailure` is not `None` and Tailscale is installed | Warning. Body: the "Exposure unknown" text of 4.15 |
| otherwise | null (stale mappings of ours keep their own warning in Settings, as before) |

Numbers are formatted with the invariant culture.

### 5.6 Pairing page (`Pairing/`)

| Id | Element | Bound to |
|---|---|---|
| `PairingQr` | `Controls.QrCodeView` inside the white tile; placeholder; spinner; countdown chip below | `TileFace`, `QrModules`, `PlaceholderLine`, `CountdownText`, `IsCountdownWarning`, `QrAutomationStatus` |
| `PairingHint` | Text | `Hint` |
| `ShowQrButton`, `HideQrButton` | `Button.Primary`, `Button.Secondary` | `ShowQrCommand`, `CanShowQr`, `ShowQrCaption`, `HideQrCommand`, `CanHideQr` |
| `PairingAddressField` | `Field.ReadOnly`, copy icon button | `PcAddress`, `CopyAddressCommand` |
| `PairingVerifyNotice` | `Banner` | `VerifyNotice`, `OpenSettingsCommand` |
| `ResetPairingButton` | `Button.Destructive` "Reset pairing…" | `ResetPairingCommand`, `CanResetPairing` |

State (`PairingViewModel`, architecture 3.12 QR machine):

| Trigger | Result |
|---|---|
| `ShowQrCommand` | `TileFace` `Generating`; `session.ShowPairingAsync()` |
| `PairingQrProduced` | `QrState` `Shown`; modules stored; hide time = now plus the duration of architecture 3.12; hint "On iPhone: Pair Windows PC > Scan PC QR code, then Verify and pair. QR hides after 2 minutes." (after a reset: "Pairing reset. Scan this QR on each iPhone."); `VerifyNotice` null; navigates to Pairing |
| Countdown tick (W-10) | `CountdownText` "Hides in `M:SS`"; warning in the last 30 seconds (screens 3.2); at zero: `Hidden(Timer)` |
| `HideQrCommand` | `Hidden(Button)` |
| `Signal(RelayEnded)` | `Hidden(RelayEnded)` when a QR was shown; `VerifyNotice` null |
| Any `Hidden` | Modules set to null (the matrix is released, not just covered); hint "QR hidden. Existing pairing remains valid." for the button, "QR hidden for privacy. Existing pairing remains valid." for the timer |
| `SelfCheckCompleted` | `VerifyNotice`: success "Secure PC connection verified."; success "Secure Funnel connection verified. The iPhone VPN is not required."; warning "Pairing code created, but the advertised connection could not be verified. Check the PC tunnel before pairing." with action "Open Settings" |

`TileFace`: `Code` when shown; `Generating` while a call runs; `RelayNotRunning` when the relay is not
running; `ConnectionNotSetUp` when it runs and `IsConnectionReady` is false; otherwise `Placeholder`.
`CanShowQr` is `IsConnectionReady`. `CanResetPairing` is true unless the session state is `Checking` or
`Starting` or a call runs.

**Drawing** (`Controls/QrCodeView.cs`, DS 4.15): a `FrameworkElement` that draws in `OnRender`. With `n`
modules per side and the monitor's device scale `s`: module size in device pixels is
`floor(240 * s / (n + 8))`, at least 3; if the code would then be narrower than 200 DIP it is one pixel
more. The drawn square is `(n + 8)` modules: a white rectangle (`SurfaceQrBrush`) with the dark modules
(`QrModuleBrush`) inset by four modules on every side, every edge on a device pixel. Nothing is drawn over
it. The element's automation name is "Pairing QR code" and its status text is `QrAutomationStatus`; the
matrix is never exposed as text. The left panel is as wide as the tile plus its padding, at least
`SizeQrPanel`.

### 5.7 Activity page (`Activity/`)

`ActivityLog` is the model: a list of entries and a running character count. The count of an entry is the
length of its time text, a space, its message, and, when it has a payload, a line break and the payload,
plus one line break.

Bounds (architecture 3.12): when the count passes the upper bound, whole entries are removed from the front
until the count is at most the lower bound; if the one remaining entry is still larger, its payload is cut to
its newest part that fits; then entry 0 is the trim marker "[Older activity removed from memory]". So the
log never exceeds the upper bound and, once trimmed, starts with the marker (AC-WIN-030).

Lines (`ActivityViewModel`; texts in `Text/ActivityText.cs`):

| Trigger | Entry |
|---|---|
| `Signal(RelayStarting)`, `Signal(ActivityCleared)` | The log is emptied |
| `RelayStarted` | "Relay started. Waiting for your iPhone."; in Public mode also "Public mode: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address." |
| `Notice` | Its message |
| `SelfCheckCompleted` | The same three texts as 5.6; exactly one line per event (AC-WIN-024) |
| `RequestStarted` | "INCOMING assist [`<id>`]" or "INCOMING summary [`<id>`]"; payload = `PayloadJson` indented by two spaces |
| `RequestCompleted` | "REPLY [`<id>`] `<DurationMs / 1000, two decimals>` s"; payload likewise |
| `RequestFailed` | The same body text as the Live banner |
| `AssistantNotice`, `LocalModelChanged`, `SpeechStreamChanged` | The message |
| `RelayFaulted` | The session's `StateText` |
| `Diagnostic` | "Relay diagnostic: " + its message (AC-WIN-034) |
| `MessageRaised` | Its text |
| `EdgeStatistics` | **No entry.** The three counter properties take the event's totals |
| `Signal(RelayStarting)` | Also: the three counters return to 0 (a new relay instance counts from zero) |

**Rejected-request counters** (`ActivityRejectedCounts`; architecture 3.4): one caption line above the log,
always visible, in `Type.Caption`: "Rejected since the relay started: `<a>` malformed · `<b>` with an
unknown token · `<c>` through Funnel in Private mode". Plain numbers in the invariant culture. No colour
change, no banner, no toast and no log entry at any value; "Clear activity" does not reset them.

The page: `ClearActivityButton` (`ClearActivityCommand`, calls `session.ClearActivity()`, disabled while
empty); `ActivityLog` is a virtualised `ItemsControl` of entries in `Log.Text` style inside a `Glass.Card`,
following new entries only while scrolled to the end; the empty state of screens 4.2; the footer text and
`EntryCountText`.

### 5.8 Settings page (`Settings/`)

`SettingsForm` holds the editable copy; `HasChanges` compares it with `session.Settings`; errors are reported
through `INotifyDataErrorInfo` from `ISettingsValidator.Validate` of the form's value, mapped to the field
texts of `Text/SettingsText.cs`. `SaveCommand` calls `session.SaveSettings`; nothing in Tailscale changes.

| Id | Element | Bound to |
|---|---|---|
| `ConnectionModeSelector` | `Segmented`: "Private tailnet", "Public Funnel" | `Mode`, `ModeExplanation` |
| `RelayPortField` | `Field.Text` | `RelayPortText` |
| `PrivateHttpsPortField` | `Field.Text`; visible in Private mode, with a read-only path "/" | `PrivateHttpsPortText` |
| `PublicHttpsPortSelector`, `PublicPathPrefixField` | `Segmented` 443, 8443, 10000; `Field.Text`; visible in Public mode | `PublicHttpsPort`, `PublicPathPrefix` |
| `SettingsAddressField` | `Field.ReadOnly`, copy icon button | `PcAddress`, `CopyAddressCommand` |
| `SaveSettingsButton`, `DiscardSettingsButton` | `Button.Secondary`, `Button.Quiet` | `SaveCommand`, `DiscardCommand`, `HasChanges` |
| `SettingsFileNotice`, `RestartNotice` | `Banner` (warning) | `SettingsFileNotice`, `RestartNotice` |
| `MappingStatus` | `StatusIndicatorView` and caption | `MappingStatus`, `MappingCaption` |
| `MappingCommandText` | Read-only `Type.Mono` field and the effect sentence | `MappingCommandText`, `MappingEffectText` |
| `SetUpMappingButton`, `CheckMappingButton`, `RemoveMappingButton` | `Button.Primary` "Set up connection…"; `Button.Secondary` "Check again"; `Button.Destructive` "Remove…" | `SetUpMappingCommand`, `CanSetUpMapping`, `CheckMappingCommand`, `RemoveMappingCommand` with `CurrentOwnedMapping` |
| `StaleMappingList` | One warning `Banner` per row, each with "Remove…" | `StaleMappings`, `RemoveMappingCommand` |
| `UnownedExposureList` | One warning `Banner` per row: the sentence, the removal command in a read-only selectable `Type.Mono` field (hidden when empty), and no button of its own; the section's "Check again" refreshes it. A caption line shows `ExposureUnknownText` when it is not null | `UnownedExposures`, `ExposureUnknownText`, `CheckMappingCommand` |
| `CodexPathField`, `CodexPathStatus`, `CodexLoginStatus`, `CheckAssistantButton` | `Field.Text` (placeholder "Automatic"); two `Row.List` rows with indicators; `Button.Secondary` | `CodexPathText`, `CodexPathStatus`, `CodexPathDetail`, `CodexLoginStatus`, `CheckAssistantCommand`, `IsCheckingAssistant` |
| `CodexVersionStatus`, `MarkContainmentCheckedButton` | A third `Row.List` row "Codex version" with an indicator and a detail line; `Button.Quiet` "Mark as checked" | `CodexVersionStatus`, `CodexVersionDetail`, `MarkContainmentCheckedCommand`, `CanMarkContainmentChecked` |
| `SpeechLabFolderField`, `SpeechLabStatus`, `LoadedModelStatus` | `Field.Text` (placeholder "Automatic"); two rows | `LabFolderText`, `LabFolderStatus`, `LabFolderDetail`, `LoadedModelStatus`, `SpeechCaption` |
| `HideFromCaptureToggle` | A check-box row "Hide this window from screen capture", caption "Screen sharing, recordings and screenshots show an empty area instead of this window." | `HideFromScreenCapture` |
| `SourceCodeButton`, `ThirdPartyNoticesButton` | `Button.Quiet` | `OpenSourceCommand`, `OpenNoticesCommand`; `Version` |

Mapping status for the selected mode, from `session.Connection`: `Present` "Active" (dot, success);
`PresentNotOwned` "Active, not created by LiveCue" (dot, success) with the `NotInLedger` text as caption;
`Absent` "Not set up" (dot, warning); `Blocked` "Blocked" (alert, danger) with the reason as caption;
`Unknown` "Tailscale is not online" or the status text of 4.15 (alert, danger). `MappingCommandText` and
`MappingEffectText` come from `session.PlanSetUp`, so what is shown is what would run; they are computed from
the **saved** settings, and while the form has unsaved changes the button is disabled with the caption "Save
the settings first." `CanSetUpMapping` is true only for `Absent` with no unsaved changes. Each
`StaleMappings` row reads "LiveCue's `<Private tailnet or Public Funnel>` mapping at `<address>` still
exists." `RestartNotice` is shown when `session.SettingsDifferFromRunning`.

`UnownedExposures` has one row per `session.Connection.UnownedExposure` entry that is not the selected
mode's own mapping (a `PresentNotOwned` mapping keeps its "Active, not created by LiveCue" status and is
also listed here, because it is the row whose removal command the owner needs): `Text` is
`MappingTexts.Exposure(handler)`, `CommandText` is `MappingTexts.ManualRemoval(handler)`. The page has no
control that runs that command. A private port of 443, 8443 or 10000 is a field error on
`PrivateHttpsPortField`: "Ports 443, 8443 and 10000 can be published by Tailscale Funnel. Choose another port
for the private listener." A lab folder that is not on a fixed local drive is a field error on
`SpeechLabFolderField`: "Choose a folder on a local drive of this PC. Network shares are not used."

Codex rows show `session.Prerequisites` results 2 and 3 (R-32); `CheckAssistantCommand` calls
`session.CheckAgainAsync()`. The "Codex version" row shows `session.Prerequisites.CodexVersion`: the version
text with the detail "Containment check run for this version." (check, success) when it equals
`Settings.CodexContainmentCheckedVersion`; otherwise the version text, or "Unknown", with the containment
warning of 4.14 as its detail (alert, warning). `MarkContainmentCheckedCommand` is enabled when a version was
read and it is not the recorded one; it calls
`session.SaveSettings(session.Settings with { CodexContainmentCheckedVersion = version })`, built from the
**saved** settings, so unsaved edits in the form are neither saved nor lost, and then `CheckAgainAsync()`.
The button only records the owner's statement that the containment check (test plan L-4) passed for that
version; the application cannot run the check itself.

`HideFromScreenCapture` is a form field like the others: it is saved by `SaveCommand`, and the window
follows the saved value (5.12). `LoadedModelStatus`: "Not reported yet" until an event arrives; then from the
latest `LocalModelChanged` status ("`<label>` · loading", "`<label>` · ready", the error message, or "None"
for `stopped`) and from `SpeechStreamChanged` (`Ready` gives "`<label>` · in use"; `Stopped` or `Failed`
gives "`<label>` · ready"). `OpenSourceCommand` opens the repository page; `OpenNoticesCommand` opens
`THIRD-PARTY-NOTICES.md` beside the executable; both through `IExternalLauncher`.

### 5.9 Dialogs and toasts

`DialogHost` shows one `IDialogViewModel` in the dialog layer: scrim over the whole window including the
sidebar, the card `Dialog.Card` (width per DS 4.10), focus moved into the dialog and trapped
(`KeyboardNavigation.TabNavigation="Cycle"`), the previously focused element restored on close. Escape and a
click on the scrim run `CancelCommand` unless `IsBusy`.

| Dialog | Behaviour |
|---|---|
| Reset pairing (`ResetPairingDialog`, `ConfirmResetPairingButton`) | Texts of screens 6.1; with the relay stopped the body omits "The relay keeps running." Initial focus on Cancel; the confirm button is `Button.DestructivePrimary` and is not the default button. Confirm: busy, `session.ResetPairingAsync()`; true closes the dialog; false shows `Failure` "The pairing could not be reset. Check that LiveCue can write its data folder, then retry." |
| Mapping (`SetUpMappingDialog`, `RemoveMappingDialog`; one class, two ids by `Action`) | Title "Set up the Tailscale mapping?" or "Remove the Tailscale mapping?"; "LiveCue will run:"; `CommandText` in a read-only `Type.Mono` field; `EffectText`; buttons "Cancel" and "Set up" (`Button.Primary`, default) or "Remove" (`Button.DestructivePrimary`, not default). Confirm: busy, `session.ExecuteMappingAsync(plan)`. `Done` closes. Anything else stays open with `Failure` = the 4.15 text of the outcome or refusal |

The mapping dialog view model is the only code that calls `ExecuteMappingAsync` (TS-9); a test fails if any
other type in the desktop assembly references that member.

`ToastHost`: one toast at a time at the bottom centre of the content area (DS 4.11), shown for 4 s. The only
toast text is "Copied".

### 5.10 Contrast themes and system preferences

| Setting | Source | Behaviour |
|---|---|---|
| Contrast theme | `SystemParameters.HighContrast`, re-read on `StaticPropertyChanged` | `ThemeManager` swaps the theme slot to `HighContrast.xaml` while the app runs (DS 6.5): every surface `Window`, every ink `WindowText`, accent `Highlight`, 1 px `WindowText` strokes on every surface, button and field; no blooms (`BloomLayer` draws the base only), sheen, shadows or tints; status is carried by mark and label; the QR stays black on white; the brand mark is drawn in `WindowText` |
| Light appearance | none | The Windows app mode is ignored; DWM attribute 20 is 0; there is no dark dictionary (D-04) |
| Text scale | Registry value of DS 6.1, read at start and on `SystemEvents.UserPreferenceChanged` | `TextScale` rewrites the `Type<Name>Size` and `Type<Name>LineHeight` resources; layouts hold to 150%, above that the page scrolls and side-by-side panels stack |
| Transparency effects off | Registry value of DS 6.3 | `ThemeManager` overlays the solid twins of DS 6.3 on the Daylight keys and removes the sheen |
| Animations off | `SystemParameters.ClientAreaAnimation` | Cross-fades and the segmented thumb travel become instant; spinners keep spinning (DS 6.7) |

### 5.11 Threading

| Work | Runs on | Returns to the UI by |
|---|---|---|
| Prerequisite checks, relay start and stop, Tailscale reads and writes, settings load and save, pairing show and reset | Thread pool: every session command wraps the supervisor call in `Task.Run` and awaits it from the UI thread | The awaiting continuation |
| Reading supervisor events | One background task per session | `IUiDispatcher.Post`, one event at a time, in order |
| Relay requests, speech streams, timers | Kestrel and thread-pool threads inside the relay | They never touch the UI; they publish events |
| Presence tick, QR countdown, toast timer | Clock timers | `IUiDispatcher.Post` |
| Rendering, bindings, view-model state | UI thread only | |

The UI thread never blocks on a task. `LiveCue.Relay` and `LiveCue.Hosting` never see a dispatcher, so relay
request handling is not delayed while a dialog is open or the window is minimised (AC-WIN-037).

### 5.12 Start-up and closing (`App.xaml.cs`, `Composition/`)

Start-up: parse the command line (`AppEnvironment`); a usage error prints to the console and exits 2. Create
`ThemeManager`. In self-test mode run 5.13. Otherwise: `HostingFactory.Create` with the data folder of
architecture 3.5 and the version of the assembly; build the session, the view models and the window; show
it; on the first `ContentRendered` call `OnWindowRenderedAsync`, which calls `session.StartAsync()` once
(WIN-2, AC-SCR-001). If anything before the window is shown throws, a system message box titled "LiveCue"
shows "LiveCue could not finish startup." and the exception's type name, and the process exits 1 (R-33).

An exception that reaches the dispatcher is handled, reported as a "Relay diagnostic: " line, and the app
continues.

Screen capture (architecture 3.5, `hideFromScreenCapture`): when the window's handle exists, and again
whenever `session.Settings` changes, `MainWindow` calls `SetWindowDisplayAffinity` (declared in
`Interop/NativeMethods.cs`) with "exclude from capture" when the saved setting is true and with "none" when
it is false. A call that fails (an older Windows build) is reported once as a `SessionMessage(Warning, "This
version of Windows cannot hide the window from screen capture.")` and is not retried. The self-test's
off-screen rendering does not go through screen capture and is unaffected; in self-test mode the call is not
made at all.

Closing (caption button, Alt+F4, the system menu): the first `Closing` is cancelled, the window is disabled,
`OnWindowClosingAsync` awaits `session.StopAsync()` for at most W-11, then the window closes for real. No
confirmation. `OnExit` disposes the hosting services, bounded by the same time, so a stuck stop cannot keep
the process alive. Tailscale mappings and the lab container are left as they are. A second instance needs no
special code: its relay start fails with `PortInUse` and its window shows that text (R-05).

### 5.13 Self-test and screenshot mode (`SelfTest/`)

`LiveCueRemake.exe --self-test [--screenshots <folder>] [--scale 1|1.5]` (architecture 3.13; sizes per R-04).

**Isolation, by construction.** The composition root is called with `HostingOverrides` in which every
outward-facing part is scripted: `IRelayFactory` returns `ScriptedRelay`; `ISettingsStore` is in memory;
`IPrerequisiteChecker` and `IMappingManager` return what the scene needs; `ITailscaleCli` is never reached;
`IProcessRunner` and `IHostFileSystem` are instances that record what is asked of them, and the self-test
fails if a process is started or a file is read or written (the supervisor's question whether a bundled
`asr\` folder exists is answered "no" and is not a failure); `IClipboard` and `IExternalLauncher` are recording fakes. The real `RelaySupervisor`, the real
`DesktopSession`, the real view models, views and theme are used. So no port is bound, no command runs,
the data folder is neither read nor written, and no network is used. The only files written are the
screenshots and the log, and only with `--screenshots`.

**Synthetic data** (`SyntheticData`): endpoint `https://pc.example.test:11443/`; a token of 43 repeated
characters; a matrix produced by encoding the synthetic payload with the same `QrMatrix` code; request id
`selftest-0001`; the question "What is the main advantage of local transcription?"; the answer "Your audio
stays on the phone."; `CodexMs` 850; `DurationMs` 912.

**Run.** The window is created at the default size, positioned far off-screen, not activated and not shown
in the taskbar (the spike's method). After each step the runner waits until the dispatcher is idle, runs the
step's assertions, and renders the scene if screenshots were requested.

| # | Step | Scene | Assertions (name: what must hold) |
|---|---|---|---|
| 1 | Window shown, nothing started | `live-stopped` | `initial-layout`: status "Stopped"; phone "iPhone not connected"; Start visible and enabled; Pause and Stop not visible; "Show QR" disabled |
| 2 | Activity page | `activity-empty` | `activity-empty`: empty state visible; "Clear activity" disabled |
| 3 | Pairing page | `pairing-empty` | `pairing-placeholder`: placeholder visible; no modules |
| 4 | `StartAsync` with passing checks, an existing pairing and a present mapping; then `PhoneSeen` | `live-ready` | `ready-layout`: status "Ready"; Pause "Pause relay"; Stop enabled; address shown; phone "iPhone connected"; activity contains "Relay started. Waiting for your iPhone."; `show-qr-enabled` |
| 5 | "Show QR"; scripted matrix; `SelfCheckCompleted(Verified)` | `pairing-qr` | `qr-drawn`: the tile's rendered pixels contain dark modules; `qr-quiet-zone`: a white border at least four modules wide surrounds them; `qr-notice`: exactly one verification line in the activity |
| 6 | "Reset pairing…" | `pairing-reset-confirm` | `reset-dialog`: dialog open, focus on Cancel; Escape closes it and the pairing is unchanged |
| 7 | `RequestStarted` (assist) | `live-thinking` | `thinking`: status "Thinking…"; transcript contains the question; reply shows "Thinking…" |
| 8 | `RequestCompleted` | `live-reply` | `reply-text`: reply and activity contain the answer; `timing-hint`: `ReplyHint` contains "0.85"; status "Ready" |
| 9 | Pause, then resume (through the scripted relay's `AcceptingChanged`) | `live-paused` (taken while paused) | `pause`: paused flag set and label "Resume relay"; `resume`: cleared and "Pause relay" |
| 10 | `RequestStarted`, then `RequestFailed(ASSISTANT_FAILED)` | `live-request-failed` | `request-failed`: status "Request failed. Ready to retry."; banner title and body; hint "Not sent. Try Assist again." |
| 11 | `SpeechStreamChanged(Loading)`, `SpeechStreamChanged(Ready)`, `SpeechText("Local words")` | none | `speech`: the transcript hint equals the last message; the transcript ends with "Local words"; both messages are in the activity |
| 12 | Activity page | `activity-filled` | `activity-lines`: "INCOMING assist [selftest-0001]" and "REPLY [selftest-0001] 0.91 s" present |
| 13 | Settings page, Private, mapping present | `settings-private` | `settings-command`: the command text equals `MappingCommand.ApplyPrivate(11443, 47841).DisplayText` |
| 14 | Mode switched to Public in the form and saved; scripted inspection `Absent` | `settings-public` | `settings-public`: the command text equals `MappingCommand.ApplyPublic(10000, "/livecue", 47841).DisplayText`; "Set up connection…" enabled; no write command reached the scripted manager |
| 15 | Scripted inspection `Blocked(PublicPortWouldPublishOthers)` | `settings-blocked` | `settings-blocked`: status "Blocked"; the reason text shown; "Set up connection…" disabled |
| 16 | Stop; then a start whose scripted relay throws `PortInUse` | `fault-port-in-use` | `fault`: stopped layout (Start visible, QR cleared, phone reset); the status still shows the port-in-use text with the port number |
| 17 | "Clear activity" | none | `clear`: log empty; transcript and reply show their placeholders; reply hint "Activity cleared" |
| 18 | 400 synthetic entries of 1,000 characters into the real log | none | `activity-cap`: the count never exceeded the upper bound; entry 0 is the trim marker |
| 19 | End | none | `no-token`: no text, tooltip or automation name collected from the visual tree in any step contains the synthetic token; `no-side-effects`: the recording process runner, file system, clipboard and launcher saw no call; `crisp-text`: no element with an `Effect`, `OpacityMask` or `BitmapCache` has a text descendant |

**Files written** with `--screenshots <folder>`: for each of the fifteen scenes `<scene>.png` at 1240 x 660
and `<scene>-min.png` at 960 x 620 (the window is resized and laid out again for the second), each rendered
with `RenderTargetBitmap` at 96 DPI times the scale; and `self-test.log` with every line that was printed.

**Output and exit codes.** Each assertion prints "ok `<name>`". Success prints "Desktop self-test passed" and
exits 0. The first failed assertion prints "Desktop self-test FAILED: `<name>`" and exits 1; a usage error
exits 2; the watchdog (W-12) exits 3. Because the executable is a window program, output goes to the
redirected standard output or to the parent console (the spike's `SelfTestConsole`).

Scenes and the mockup frames they correspond to: `live-stopped` / `win-live-stopped`; `live-reply` /
`win-live-ready`; `live-thinking` / `win-live-thinking`; `live-request-failed` / `win-live-failed`;
`pairing-qr` / `win-pairing-shown`; `pairing-reset-confirm` / `win-pairing-reset`; `activity-filled` /
`win-activity`; `activity-empty` / `win-activity-empty`; `settings-private` / `win-settings-connection`;
every `-min` file / `win-min`. `live-ready`, `live-paused`, `pairing-empty`, `settings-public`,
`settings-blocked` and `fault-port-in-use` have no frame; `win-live-prereq` and `win-settings-assistant` have
no scene and are covered by view-model tests.

### 5.14 Icon tool (`windows/tools/LiveCue.IconTool/`)

A WPF console program, not published. `LiveCue.IconTool <repository root>` draws the mark from the numbers
of DS 8.2 with `DrawingVisual` and writes `windows/src/LiveCue.Desktop/Assets/LiveCue.ico` (frames 256, 128,
64, 48, 40, 32, 24, 20, 16, each a 32-bit PNG entry, drawn per size as DS 8.2 says) and the iPhone 1024 px
PNG at the path the iPhone design names. It is run by hand; a normal build never runs it (WIN-19). A test
checks that the committed `.ico` holds exactly those nine frame sizes.

---

## 6. Work packages

### 6.1 Rules for every package

- A package changes only the files it owns (section 2). It never changes a frozen file; if it needs to, it
  stops and tells the orchestrator.
- No new NuGet package, no new tool. Builds use the portable SDK through the environment variable
  `LIVECUE_DOTNET_EXE` (the full path of `dotnet.exe`); CI uses the SDK on the runner.
- No test and no manual run binds port 47831. Tests bind port 0 and use `TempFolder`. Nothing reads or writes
  `%LOCALAPPDATA%\LiveCue\` or the real `%LOCALAPPDATA%\LiveCueRemake\`.
- No test runs Codex, Tailscale, PowerShell or Docker, except the opt-in live tests of the test plan.
- Definition of done, in addition to each package's own: `dotnet build windows/LiveCueRemake.sln -c Release`
  has no warning; `dotnet test windows/LiveCueRemake.sln -c Release` passes, old tests included;
  `python scripts/audit_public_source.py` passes (once WP-W14 has landed it); every test the test plan
  assigns to the package exists under the name given there.
- Sizes are lines of C# and XAML, production plus tests: S under 600, M 600 to 1,500, L 1,500 to 3,000,
  XL above.

### 6.2 WP-W00 Foundation (first, alone)

**Goal.** A solution in which every project, every contract and every seam of this document exists, compiles
and is wired, so that each later package only replaces bodies in files it owns.

**Starts from** the `spike/windows` branch: its solution files, its theme dictionaries and controls, its
window, its pairing-token helper and its protocol constants are carried over and moved to the paths of
section 2.

**Creates** every file marked WP-W00 in section 2, and a first version of every other file of the four shipped
projects, so that nothing is missing:

| Area | First version |
|---|---|
| Contract files | Exactly as section 3, including `ProtocolConstants`, `RelayCodes`, `RelayErrors`, `FixedTexts`, `RouteTable`, `SpeechLease`, `MappingCommand`, complete |
| Relay edge and host | A working minimum taken from the spike: listener on loopback, P-1 to P-9 and P-11 with `RelayException` handling (the spike already had E-1, E-5, E-6 and presence), `BodyReader` rules 1, 2 and 4, `ResponseWriter`, `RelayHost` start, stop, pause flag and event channel. No deadlines, gauges, gate or presence nonce yet |
| Routes 1 and 2 | Complete |
| Pairing | A stub that creates a token in memory at start, verifies it, exports the payload, returns a fixed 21 x 21 matrix, and on reset replaces the token and cancels the revocation token (so the answer and speech packages can test a reset on their own branches) |
| Catalogue, answers, PC model, speech | Stubs with the fixed constructors: the catalogue returns the fake source's entries unfiltered or an empty list; assist and summary answer `500 ASSISTANT_FAILED`; route 9 answers `404 REQUEST_NOT_FOUND`; the PC model reports MSG-2 and refuses with `LAB_NOT_CONFIGURED`; the speech bridge sends `LAB_NOT_CONFIGURED` and closes with 1008 |
| Process runner | A stub that returns `StartFailed` |
| Hosting | `HostingFactory.Create` complete; an in-memory settings store; a checker that passes when an endpoint override is given and otherwise fails check 4; a Tailscale CLI that reports "not installed"; a mapping manager that inspects as `Unknown` and refuses; a supervisor with the full step order of 4.16 |
| Console host | Arguments parsed; runs the supervisor; prints events as text |
| Desktop | The spike's window moved to `Shell/`; every style key of 5.3; first-version view models that implement the interfaces (the relay state, the controls and the addresses follow the session; everything else is fixed); a session that forwards the commands and follows the supervisor's events for the relay state; the app starts and shows the four pages |
| Tests | `Shared/`, every `Support/` folder with its fakes, `LiveCue.TestChild`, and the foundation tests of the test plan (section 1.1 there) |
| CI | `.github/workflows/windows.yml` with the `windows` job of the test plan, and a first version of `scripts/publish-windows.ps1` |

**Provides.** Every contract. **Consumes.** Nothing.

**Done when** the general definition holds, each of the three test projects has at least one passing test,
`LiveCueRemake.exe` starts on the build PC with the relay port set to a free port, and the published folder
runs on a PC without .NET installed. **Size** L.

### 6.3 Wave 1: eleven packages, mutually independent

After WP-W00 is merged, these packages share no file and depend only on contracts. Any eight run at once;
WP-W02 and WP-W04 are the smallest and are the two to queue when only eight builders are available. WP-W14
touches no Windows source and can run beside any of them.

| Id | Goal | Owns (section 2) | Consumes | Provides | Tests it adds (test plan) | Done when | Size |
|---|---|---|---|---|---|---|---|
| WP-W01 | The relay as its own internet edge, and the `IRelay` life cycle | `Core/RelayHost.cs`, `Core/AccessState.cs`, `Diagnostics/`, `Edge/` except the two frozen files, routes 1 and 2, tests `Edge/`, `Host/` | `IPairingService`, `RouteTable`, `RelayErrors`, `RelayServices` | `IRouteIo`, `IAccessState`, `IRelayEventSink`, `IRelay` | Section 1.2: P2-001 to P2-021, I-01 to I-06 | Every row of 4.2 to 4.6 is implemented; the listener test shows one loopback endpoint; a raw-socket test proves each K note | L |
| WP-W02 | Pairing: token, file, DPAPI, payload, matrix, self-check, reset | `Pairing/` except the four frozen files, tests `Pairing/` | `RelayContext`, `IRelayEventSink` | `IPairingService`, `AdvertisedEndpoint`, `PairingMaintenance` behaviour | Section 1.3: P2-140 to P2-148 | 4.7 implemented; the DPAPI tests pass on Windows; no test leaves a file outside its temp folder | M |
| WP-W03 | Child processes, the catalogue and the answer pipeline | `Processes/` except the contract, `Catalog/` and `Answers/` except the frozen files, routes 3, 7, 8, 9, `LiveCue.TestChild/Program.cs`, tests `Processes/`, `Catalog/`, `Answers/` | `RouteContext`, `IAccessState`, `IRelayEventSink` | `IProcessRunner` implementation, `ICatalogReader`, `IAnswerCoordinator` | Section 1.4: P2-030 to P2-064, P2-149 | 4.8, 4.9, 4.13 implemented; a killed run leaves no process and no folder; the stub Codex exchange passes | L |
| WP-W04 | PC model control | `LocalModel/` except the frozen files, routes 4, 5, 6, tests `LocalModel/` | `SpeechLease`, `IProcessRunner`, `RouteContext` | `ILocalModelController`, `ILabRuntime` implementation | Section 1.5: P2-080 to P2-095 | 4.10 implemented; the latch test runs on the manual clock | M |
| WP-W05 | The speech bridge | `Speech/` except the frozen files, `Routes/SpeechRoute.cs`, tests `Speech/` | `ILocalModelController` (faked through `RelayDependencies.LocalModelController`), `SpeechLease`, `IAccessState`, `RouteContext` | `ISpeechBridge`, `ILabSocketConnector` implementation | Section 1.6: P2-100 to P2-121 | Every row of the 4.11 table has a test; every watchdog test runs on the manual clock | L |
| WP-W06 | Hosting core and the console host | `LiveCue.Hosting/` except `Contracts/` and `Tailscale/`, `LiveCue.Relay.Host/`, hosting tests except `Tailscale/` | `ITailscaleCli`, `IMappingManager` (fakes), `IRelay`, `IProcessRunner` | `ISettingsStore`, `ISettingsValidator`, `ICodexLocator`, `ICodexSignInCheck`, `IPrerequisiteChecker`, `IEndpointResolver`, `IRelaySupervisor`, the two text helpers of `PrerequisiteTexts` | Section 2.1 to 2.4 | 4.14 and 4.16 implemented; `LiveCueRelayHost.exe --check-only` and `--endpoint … --port 0 --data-dir <temp>` behave as architecture 3.14 on the build PC | L |
| WP-W07 | Tailscale reading and the mapping manager | `LiveCue.Hosting/Tailscale/`, tests `Tailscale/` | `IProcessRunner` (fake), `ISettingsStore`, `ISettingsValidator` | `ITailscaleCli`, `IMappingManager`, `MappingTexts` | Section 2.5: TS-1 to TS-10 and every refusal | 4.15 implemented; the argument-list test proves only the four command forms can leave the process; **no test and no manual run executes a Tailscale write command on the build PC** | M |
| WP-W08 | Desktop core: chrome, navigation, session, theme | Every desktop file marked WP-W08, tests `Session/`, `Shell/`, `Theme/` | `IRelaySupervisor`, `IMappingManager`, page view-model interfaces (stubs) | `IDesktopSession`, `IShellViewModel`, `INavigator`, `IDialogHost`, `IToastHost`, every style key | Section 2.6 | 5.1 to 5.4 and 5.9 to 5.12 implemented; the contrast dictionary has the same keys as Daylight; the window matches the mockup's shell at both sizes | L |
| WP-W09 | Live and Activity pages | `Live/`, `Activity/`, `Text/LiveText.cs`, `Text/ActivityText.cs`, tests `Live/`, `Activity/` | `IDesktopSession` (`FakeSession`), style keys | `ILiveViewModel`, `IActivityViewModel` | Section 2.7 | 5.5 and 5.7 implemented; every string of screens sections 2 and 4 is present verbatim | M |
| WP-W10 | Pairing and Settings pages and both dialogs | `Pairing/`, `Settings/`, `Controls/QrCodeView.cs`, `Text/PairingText.cs`, `Text/SettingsText.cs`, tests `Pairing/`, `Settings/` | `IDesktopSession` (`FakeSession`), `HostingServices`, style keys | `IPairingViewModel`, `ISettingsViewModel`, the two dialog view models | Section 2.8 | 5.6, 5.8 and the dialogs of 5.9 implemented; the test that only the mapping dialog references `ExecuteMappingAsync` passes | L |
| WP-W14 | CI, the audit, release | `scripts/` except the iPhone helpers and the WP-W12 files, `.github/` | The `windows` job of WP-W00; the iPhone helpers | `ci.yml`, `release.yml`, the audit | Test plan sections 5 and 6 | A push to a work-package branch that touches only `windows/` starts no macOS job; the audit passes on the tree and on history, or its findings are listed for the orchestrator | M |

### 6.4 Wave 2: three packages

| Id | Needs | Goal | Owns | Tests it adds | Done when | Size |
|---|---|---|---|---|---|---|
| WP-W11 | WP-W08, WP-W09, WP-W10 | The self-test and screenshot mode | `SelfTest/`, tests `SelfTest/` | Test plan section 2.9 | 5.13 implemented; all thirty screenshots are produced on the build PC and on the Windows runner; each matches its mockup frame in layout | M |
| WP-W12 | WP-W05, WP-W06 | Icon tool, the manual GPU check, the opt-in catalogue fetch | `windows/tools/LiveCue.IconTool/`, `Assets/LiveCue.ico`, `scripts/verify-pc-asr.ps1`, `scripts/smoke/` | Icon frame test; test plan section 3 | The icon holds the nine frames; the two scripts print their usage and refuse bad input; neither is run by any test | S |
| WP-W13 | WP-W01 to WP-W05 | Relay conformance and security sweep across areas | Tests `Conformance/` | Test plan sections 1.7 and 4 | Every P2 statement of side R has a passing test through a real loopback listener; every item of the security list passes | M |

### 6.5 Merge order

1. WP-W00.
2. Wave 1 in any order: the packages are file-disjoint, so merges cannot conflict. Suggested order, so that
   each merge is checked against the most complete base: WP-W14, WP-W01, WP-W02, WP-W03, WP-W04, WP-W05, WP-W06,
   WP-W07, WP-W08, WP-W09, WP-W10. Every merge runs the whole Windows job; a failure that appears only after a
   merge belongs to the package merged last.
3. WP-W13, then WP-W11, then WP-W12.

A change to a frozen file is a separate, orchestrator-approved commit on `main`, merged before the packages
that need it and announced to every running builder.

---

## 7. Changes after review

Two reviews changed this document after WP-W00 had started from its earlier text: the cross-check of the
protocol against both designs (items `X-nn` and `A-nn`) and the security review `07-security-review.md`
(findings `SEC-nn`, tests `ST-nn`). The rulings are in decisions section 8. The sections above already hold
the new text. This section says, per work package, what its builder must do **differently from the text as
it stood before**, so that the difference reaches the code. 7.1 lists every change to a frozen contract file;
they are the foundation's to make, each as the orchestrator-approved commit that 6.5 describes.

### 7.1 Frozen contract files that change

Paths are under `windows/src/`. "Comment" means the declaration is unchanged and only its documentation
changed.

| # | File | Change | From |
|---|---|---|---|
| 1 | `LiveCue.Relay/Contracts/RelayOptions.cs` | Comment of `DataDirectory`: it also holds `lab\` and `work\` | SEC-06, SEC-10 |
| 2 | `LiveCue.Relay/Contracts/RelayEvents.cs` | New record `EdgeStatistics(DateTimeOffset At, long Malformed, long TokenNotAccepted, long FunnelInPrivateMode) : RelayEvent(At)` | SEC-14 |
| 3 | `LiveCue.Relay/Contracts/RelayDependencies.cs` | Comment of `TempRoot`: null means `DataDirectory\work`, which the relay empties at start; a folder given here is never emptied | SEC-10 |
| 4 | `LiveCue.Relay/Core/RelayContext.cs` | Body of `TempRoot`: `Dependencies.TempRoot ?? Path.Combine(Options.DataDirectory, "work")`. It no longer uses `Path.GetTempPath()` | SEC-10 |
| 5 | `LiveCue.Relay/Protocol/ProtocolConstants.cs` | New members `ModelIdMaxLength` (64; C-06) and `EdgeStatisticsInterval` (60 s; W-13) | SEC-01, SEC-14 |
| 6 | `LiveCue.Relay/Processes/IProcessRunner.cs` | In `EnvironmentPolicy`, the member `WithoutLiveCueVariables` is **replaced** by `CodexAllowlist`; every use is renamed with it | SEC-01 |
| 7 | `LiveCue.Relay/Pairing/IPairingService.cs` | `bool Verify(ReadOnlySpan<char> token)` and the property `CancellationToken CurrentRevocation` are **replaced** by `bool TryVerify(ReadOnlySpan<char> token, out CancellationToken revoked)` | SEC-07 |
| 8 | `LiveCue.Hosting/Contracts/RelaySettings.cs` | New properties `string? CodexContainmentCheckedVersion` (null) and `bool HideFromScreenCapture` (true) | SEC-01, SEC-10 |
| 9 | `LiveCue.Hosting/Contracts/SettingsValidation.cs` | `SettingsField` gains `CodexContainmentCheckedVersion` (after `CodexExecutablePath`) and `HideFromScreenCapture` (after `AsrLabPort`); `ISettingsValidator` gains `bool IsValidLedgerEntry(OwnedMapping entry)`; comments of `NotAllowed`, `BadFormat` and `ValidateStructure` | SEC-01, SEC-04, SEC-06, SEC-08, SEC-10 |
| 10 | `LiveCue.Hosting/Contracts/ISettingsStore.cs` | Comments: `Invalid` keeps the file's valid ledger entries in `Settings`; `Save` first copies a failed file to `settings.json.invalid` | SEC-04 |
| 11 | `LiveCue.Hosting/Contracts/IHostFileSystem.cs` | New members `string ProgramFilesDirectory { get; }` and `bool IsOnFixedLocalDrive(string path)` | SEC-06 |
| 12 | `LiveCue.Hosting/Contracts/Codex.cs` | `ICodexSignInCheck` gains `Task<string?> ReadVersionAsync(string codexPath, CancellationToken ct)`; its summary comment | SEC-01 |
| 13 | `LiveCue.Hosting/Contracts/Prerequisites.cs` | `PrerequisiteReport` gains the last positional parameter `string? CodexVersion = null` and the computed `bool ContainmentCheckIsStale` | SEC-01 |
| 14 | `LiveCue.Hosting/Contracts/Tailscale.cs` | `PrefixPattern` is `@"\A/[a-z0-9][a-z0-9-]{0,31}\z"` (it was `^…$`); `ApplyPrivate` calls the new private method `RequirePrivatePort`, which refuses 443, 8443 and 10000; `RemovePrivate` is unchanged | SEC-08, SEC-04 |
| 15 | `LiveCue.Hosting/Contracts/Mapping.cs` | New record `ExposedHandler(int HttpsPort, string Mount, bool FunnelOn, bool Foreground, OwnedMapping? LedgerEntry)`; `ConnectionInspection` gains the last positional parameter `IReadOnlyList<ExposedHandler> Exposure` and the computed `HasFunnelExposure` and `UnownedExposure` | SEC-04 |
| 16 | `LiveCue.Hosting/Contracts/IRelaySupervisor.cs` | `StartOutcome` gains `ExposedInPrivateMode`; comment of `StartAsync` | SEC-04 |
| 17 | `LiveCue.Desktop/Contracts/LiveViewModel.cs` | `ILiveViewModel` gains `BannerModel? ExposureBanner { get; }` | SEC-04 |
| 18 | `LiveCue.Desktop/Contracts/ActivityViewModel.cs` | `IActivityViewModel` gains `long RejectedMalformed`, `long RejectedTokenNotAccepted`, `long RejectedFunnelInPrivateMode` (get only) | SEC-14 |
| 19 | `LiveCue.Desktop/Contracts/SettingsViewModel.cs` | New record `UnownedExposureRow(ExposedHandler Handler, string Text, string CommandText)`; `ISettingsViewModel` gains `UnownedExposures`, `ExposureUnknownText`, `CodexVersionStatus`, `CodexVersionDetail`, `CanMarkContainmentChecked`, `MarkContainmentCheckedCommand` and the two-way `HideFromScreenCapture` | SEC-04, SEC-01, SEC-10 |
| 20 | `LiveCue.Desktop/Contracts/AutomationIds.cs` | Six new constants: `ExposureBanner`, `ActivityRejectedCounts`, `UnownedExposureList`, `CodexVersionStatus`, `MarkContainmentCheckedButton`, `HideFromCaptureToggle` | SEC-04, SEC-14, SEC-01, SEC-10 |
| 21 | `windows/tests/LiveCue.Hosting.Tests/Support/FakeHostFileSystem.cs` | Implements the two new members of `IHostFileSystem`, both settable by a test | SEC-06 |

Not changed, although a ruling touches their area: `Contracts/IRelay.cs`, `RelayFaults.cs`, `RelayFactory.cs`,
`Protocol/RelayCodes.cs` and `RelayErrors.cs` (no new wire code and no new fault code: the refusal by Codex
version that the review proposed was not adopted), `Edge/RouteTable.cs` and `RouteContext.cs`,
`Contracts/IDesktopSession.cs` (the session shows the new start outcome through its existing members, 5.4),
`Contracts/HostingFactory.cs` and every constructor signature of 3.3 and 3.7.

Foundation-owned files that are not contracts and change too: `LiveCue.Relay.csproj` (embeds
`asr/Start-ASR-Lab.ps1`, `asr/Stop-ASR-Lab.ps1` and `asr/LabCommon.ps1` as linked resources);
`Directory.Build.props` (`RestorePackagesWithLockFile`) and one committed `packages.lock.json` per project;
the workflow's restore step (`--locked-mode`).

### 7.2 What each package does differently

**WP-W00 Foundation**

- Makes every change of 7.1 and the three file changes listed under it.
- Pairing stub: implements `TryVerify`. The stub holds its hash and its revocation source as one object,
  swaps that object on reset and then cancels the old source, exactly as 4.7 says for the real service, so
  that the answer and speech packages still test a reset on their own branches.
- Edge minimum: P-4 calls `TryVerify` and uses the token it returns as the request's `PairingRevoked`. No
  code reads a "current revocation".
- Relay start: step 5 of 4.2 writes the three script copies through `LabScripts.WriteCopies` (a working
  method in the foundation, final in WP-W04) and creates `work\`.
- Every Codex stub and the process-runner stub use `EnvironmentPolicy.CodexAllowlist`.
- Hosting stubs: `CodexSignInCheck.ReadVersionAsync` returns null; `SettingsValidator.IsValidLedgerEntry`
  exists; `HostFileSystem` has the two new members; the in-memory settings store carries the two new
  settings; the supervisor has step 4a of 4.16 (with the stub mapping manager it never refuses); every
  `ConnectionInspection` it builds has an empty `Exposure`.
- Desktop stubs: the view models implement the new members with fixed values (no banner, three zeros, empty
  lists, the toggle true, "Unknown" as the version).
- Foundation tests (test plan 4.6): `MappingCommandTests.Apply_private_refuses_a_funnel_port` and
  `…Factories_throw_for_a_prefix_that_ends_in_a_line_feed`;
  `RouteTableTests.A_request_id_shape_is_a_whole_text_match`; `ProtocolConstantsTests` covers the two new
  constants; `AutomationIdTests` covers the six new ids.

**WP-W01 Edge and host**

- P-4 is `TryVerify` (4.4); nothing reads the revocation token separately from the verification.
- The three rejected-request counters, the W-13 timer, the `EdgeStatistics` event and its last publication at
  stop (4.2, 4.4). No allocation and no event on the 401 path.
- The listen socket is created with exclusive address use (4.3).
- Start step 5 in its final form: the script copies, and the purge of `work\` when `TempRoot` is null (4.2).
- K-9 (4.3): a request with both length headers is left to Kestrel; no framing is parsed by hand.
- `Date` is in the response-header allowlist (K-1, unchanged in substance).
- Adds: ST-13 (`BearerCheckTests`), ST-19, ST-20 (five of its seven cases), ST-10
  (`ListenerTests.The_listen_socket_is_exclusive`), ST-15 (`WorkFolderTests.A_start_empties_the_work_folder`).

**WP-W02 Pairing**

- `PairingService` holds one immutable `AcceptedPairing` (hash and revocation source); `TryVerify` reads it
  once; reset swaps the object and then cancels the old source (4.7). `Verify` and `CurrentRevocation` do
  not exist.
- The `tokenHash` check and the endpoint grammar are whole-text checks.
- Adds: ST-12 (two tests, one `Slow`), ST-13 (two tests).

**WP-W03 Processes, catalogue, answers**

- `ChildEnvironment`: the `CodexAllowlist` list of 4.13 replaces "everything except `LIVECUE_`".
  `CodexCatalogSource` and `CodexAnswerBackend` pass `CodexAllowlist`.
- `CodexAnswerBackend`: the working folder is `<TempRoot>\<32 hex characters>` with `TempRoot` under the data
  folder, an existing folder fails the run, and the argument list carries the containment arguments (4.9).
- **Fills the containment table of 4.9** from the help output of the Codex on the build PC, runs L-4 once and
  records its result there.
- `AnswerValidator` writes `memory.throughSegmentId` as `null` in every assist reply (X-01, P2-065).
- `OfferFilter`: an id longer than `ModelIdMaxLength` is not offered; the shape is a whole-text check.
- `RunRegistry` compares ids ordinally (A-13); `ModelsRoute` writes the shape of 4.6 (A-07).
- Changed test names in test plan 1.4 (P2-064); adds P2-065, ST-03 (two tests), ST-13 (two tests), ST-15
  (`WorkFolderTests`, except the start-time case).

**WP-W04 PC model control**

- The commands table has the M-4 row first (X-02); routes 4, 5 and 6 follow the order and the hand-written
  status JSON of 4.10 (A-07).
- `LabRuntime` starts `powershell.exe` from `Environment.SystemDirectory`, never from `%SystemRoot%`; the
  `-File` argument is the relay's own copy under `<DataDirectory>\lab\`; the start command carries
  `-LabFolder`; the launcher and `LabCommon.ps1` are compared with the embedded bytes before each run; a
  lab folder that is not a drive-letter path is "not configured" (4.10).
- `LabScripts.WriteCopies` in its final form (three files).
- Adds: ST-11 (the four relay-side tests).

**WP-W05 Speech bridge**

- `provider` in `loading` and `ready` is the model id from the header, and the two integers are written as
  integers (A-04); an offered extension or subprotocol is ignored (A-08, with its test in test plan 1.6); a
  frame that would pass C-21 has its transcript cut further (A-09, with its test); in `AwaitingLabReady`
  only a lab text message or an `Invalid` message is `MODEL_NOT_READY`, a close is `LAB_DISCONNECTED`
  (A-12).
- No change for SEC-07: `SpeechConnection.PairingRevoked` is still handed over by the edge, which now gets it
  from `TryVerify`.

**WP-W06 Hosting core and the console host**

- Settings (4.14): twelve members, the two new ones optional in a file; the private port may not be 443,
  8443 or 10000; the lab folder must be a drive-letter path on a fixed local volume; the version text has a
  shape; every pattern is a whole-text match; `IsValidLedgerEntry`; `Load` of an invalid file returns the
  defaults with the file's valid ledger entries; `Save` copies a failed file to `settings.json.invalid`
  first.
- `HostFileSystem`: `ProgramFilesDirectory` and `IsOnFixedLocalDrive`. `LabFolderResolver` checks the rule
  again at every resolve.
- `CodexSignInCheck`: both calls use `CodexAllowlist`; `ReadVersionAsync` (4.14). `PrerequisiteChecker` puts
  the version into the report.
- `PrerequisiteTexts`: the three additions of 3.7.
- `RelaySupervisor`: step 4a, and the inspection after a `PortInUse` fault (4.16).
- Console host (4.16): the interactive-console rule of `--show-pairing`, `--include-content`, exit code 2
  for `ExposedInPrivateMode`, the containment warning line.
- Owns the new file `LiveSmoke/CodexContainmentLiveTests.cs` (L-4).
- Changed rows in test plan 2.1, 2.2 and 2.4; adds ST-03 (hosting side), ST-08 (supervisor), ST-09, ST-10
  (supervisor), ST-11 (hosting side), ST-15 (console host), ST-16.

**WP-W07 Tailscale and the mapping manager**

- `TailscaleLocator` uses `IHostFileSystem.ProgramFilesDirectory`.
- `MappingRules.Exposure`, and `InspectAsync` fills `ConnectionInspection.Exposure` (4.15).
- `MappingTexts.Exposure`, `MappingTexts.ManualRemoval` and the "exposure unknown" text (4.15).
  `ManualRemoval` returns text only and never a `MappingCommand`.
- Nothing changes in the four command forms or in TS-1 to TS-10, except that `ApplyPrivate` now throws for a
  Funnel-capable port, which `PlanApply` reports as `InvalidSettings`.
- Adds: ST-08 (four tests; its serve-status documents live in the package's own test folder, not in the
  frozen `ServeStatusFixtures.cs`), ST-11 (`TailscaleLocatorTests`).

**WP-W08 Desktop core**

- `DesktopSession` (5.4): a start that returns `ExposedInPrivateMode` is the `Faulted` layout with the
  result's text and no fault code; the text of a `RelayFault` result replaces the text set from the
  `RelayFaulted` event; one containment warning message per changed version.
- `MainWindow` and `Interop/NativeMethods.cs`: the display affinity follows the saved setting (5.12).
- `HideFromCaptureToggle` is a `CheckBox` drawn with row styles that 5.3 already lists; no new style key.
- Adds: ST-06 (`CaptureExclusionTests`), ST-08 and ST-10 (the two `DesktopSessionTests`).

**WP-W09 Live and Activity**

- `ExposureBanner` and its three cases; the containment warning as the subtitle of the check 2 row (5.5).
- The three counters, their caption line, and no log entry for `EdgeStatistics` (5.7).
- Adds: ST-08 (`LiveViewModelTests.Exposure_banner_cases`), ST-19 (two `ActivityViewModelTests`).

**WP-W10 Pairing and Settings**

- Settings (5.8): the unowned exposure rows with their command text and no button that runs it; the
  "exposure unknown" caption; the field error for a Funnel-capable private port and for a lab folder off a
  local drive; the "Codex version" row with "Mark as checked"; the capture toggle as a form field.
- Nothing changes on the Pairing page: the QR keeps carrying the token (decisions 8.4).
- Adds: ST-06 (`SettingsViewModelTests.Hide_from_capture_is_a_form_field`), ST-08
  (`SettingsViewModelTests.Unowned_exposure_rows`).

**WP-W11 Self-test**

- The scripted hosting returns inspections with an empty `Exposure` and a report with a fixed version that
  equals the scripted `CodexContainmentCheckedVersion`, so that no warning appears in a scene. In self-test
  mode `SetWindowDisplayAffinity` is not called. Scenes and assertions are unchanged.

**WP-W12 Tools**

- `scripts/verify-pc-asr.ps1` always passes its own `--data-dir`, which is what allows it to read the
  payload of `--show-pairing` from a redirected output.

**WP-W13 Conformance and security sweep**

- S-13 loses its third case, which becomes ST-21; S-43, S-44 and S-45 as reworded in test plan 4.5.
- Adds: ST-20 (the `RELAY_PAUSED` and `ASSISTANT_BUSY` cases), ST-21. `TokenSweepTests` and
  `PrivateTextSweepTests` include `EdgeStatistics` events and the Codex version text.

**WP-W14 CI, audit, release**

- Test plan 5.5: every action by commit id, `--locked-mode`, the `use_cache` input and `release.yml` passing
  false, the release job failing when the tag exists, the provenance attestation and its two permissions,
  the notes template. Adds `scripts/ci/check_workflow_pins.py` and its tests (ST-17).

### 7.3 Changes outside `windows/` that these depend on

| # | Item | For |
|---|---|---|
| 1 | `asr/Start-ASR-Lab.ps1` gains `-LabFolder <folder>` (default: the script's own folder) and passes it on where it now passes its own folder as the lab root. `LabCommon.ps1` keeps being loaded from the folder of the script that runs. Without this the relay's copies cannot find the weights | The lab builder |
| 2 | `asr/server.py`: the `Host` rule on every route and the `Origin` and `Sec-Fetch-Site` rules on the three HTTP routes (protocol 11.1); test `test_http_routes_check_host_origin_and_fetch_site` (P2-162, ST-14) | The lab builder |
| 3 | `asr/Dockerfile` names its base image by digest; the requirement files carry hashes and are installed with `--require-hashes`; a text check in `asr/tests` (test plan 5.5) | The lab builder |
| 4 | `ios/Package.resolved` is committed and `scripts/ci/generate_project.sh` uses it and checks the XcodeGen version (test plan 5.2) | The iPhone side (iPhone design section 9) |

### 7.4 Hand-offs outside this document

| # | Item | For |
|---|---|---|
| 1 | Screens section 9 and the mockup do not have the six new elements yet (`ExposureBanner`, `ActivityRejectedCounts`, `UnownedExposureList`, `CodexVersionStatus`, `MarkContainmentCheckedButton`, `HideFromCaptureToggle`). Until they do, section 3.9 and sections 5.5, 5.7 and 5.8 of this file are their definition, and `AutomationIdTests` takes the six ids from `AutomationIds` | The screen designer |
| 2 | The security review proposed to reword the self-check success text ("Secure PC connection verified.") to say what was tested ("This PC reached its own address."). Copy belongs to the screen design; the text is unchanged here | The orchestrator and the screen designer |
| 3 | README: an "Uninstall" section (remove both mappings in Settings before deleting the folder, with the two removal commands); "unpack the zip under your own user profile"; how to run the containment check and what "Mark as checked" means; how to verify the release attestation | Whoever writes the README |
| 4 | The review's "Record this mapping as LiveCue's" was not adopted: it would let the application remove a handler it did not create (decision D-20, rule TS-8). A hand-made mapping is shown with its removal command instead | The orchestrator, in case the owner wants it |
