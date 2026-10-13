# 04. iPhone architecture

Status: binding for builders of `ios/`. It refines `03-system-architecture.md` section 4 down to files,
declarations and work packages. Builders compile only on GitHub Actions, so every name, signature and
isolation rule here is meant to be copied, not interpreted.

Companion files, cited by short name: `01-decisions.md` ("decisions"), `02-relay-protocol.md` ("protocol"),
`03-system-architecture.md` ("architecture"), `00-platform-research.md` ("research"), `spec/01-ios-app.md`
("spec 01"), `spec/02-ios-core.md` ("spec 02"), `design/01-design-system.md` ("DS"),
`design/02-ios-screens.md` ("screens").

Contents

1. [Reconciliation](#1-reconciliation)
2. [File map](#2-file-map)
3. [Contracts](#3-contracts)
4. [Behavioural design](#4-behavioural-design)
5. [Screen wiring](#5-screen-wiring)
6. [Test plan](#6-test-plan)
7. [Work packages](#7-work-packages)
8. [Compile-risk checklist](#8-compile-risk-checklist)
9. [Changes after review](#9-changes-after-review)

Conventions

- "WP-nn" is a work package of section 7. "R-nn" is a ruling of section 1.
- Swift in section 3 is a declaration: a signature without a body is written by WP-01 with a stub body and
  filled in by the owning package. A builder never changes a signature printed here. If one does not compile
  as written, the builder makes the smallest change that compiles, marks it `// CONTRACT-CHANGE:` and reports
  it, so this file can be corrected for everyone else.
- Example hosts are reserved test names. Example tokens are the synthetic token of protocol 4.2.

---

## 1. Reconciliation

Rule applied: the screens document governs navigation, layout, copy and which element carries which
accessibility identifier; the decisions and the protocol govern behaviour and which features exist.

"Edits" names what was changed in place: **D** = `01-decisions.md`, **S** = `02-ios-screens.md`.

### 1.1 Rulings

| Id | Disagreement | Ruling | Edits |
|---|---|---|---|
| R-01 | Decisions IOS-A4 keeps "no delete confirmation"; screens 13 asks for one. | Orchestrator: deleting a session asks for confirmation. Everything else in IOS-A4 stays absent. | D: IOS-A4, 5.1, 6.1 |
| R-02 | Decisions IOS-A5 keeps two routes to Settings; screens 1 has one, a Transcription screen, a PC speech model screen and the answer service picked in place. | Orchestrator: navigation follows screens 1. | D: IOS-A5, 5.1 |
| R-03 | Architecture 4.4 and protocol 14.3 say "alert" for in-session problems; screens 4.3 uses a persistent banner. | Orchestrator: the banner. Table 1.2 says which surface each error uses. While the live screen is up, nothing raises the global alert. | D: IOS-B4, 5.1 |
| R-04 | Live state label: spec AC-IOS-020 and 029 and architecture 4.4 say "Updating…"; screens 4.2 says "Connecting…", "Pausing…", "Stopping…". | Screens (copy). | D: 5.1, 6.1 |
| R-05 | Screens 14 invents four texts for refused PC speech connections, shows "the relay's own text" for speech errors, and adds a fallback text for Codex transit failures. Protocol 14 gives the phone's text for every code and every `L-` condition, and P2-206 tests it. | Protocol. The code-to-text table is behaviour with a conformance test; the screens' rows described an upgrade-refusal model that protocol 2 no longer has. | S: 14 |
| R-06 | Voz 30-minute text: screens 14 says "…Tap Assist to transcribe it, then resume."; decisions 6.1 says "…and was transcribed. Resume to keep recording." | Decisions. Under IOS-B4 the pending audio is transcribed as part of the pause, so the screens' text would tell the user to do something already done. | S: 14 |
| R-07 | On-device hand-off queue overflow: architecture 4.6 raises an alert and drops the buffer without pausing; screens 4.3 and 14 pause the session with a notice. Decisions IOS-B4 and A-18 say a failure of any provider leads to Paused. | Pause, with the notice and the screens' text. It follows IOS-B4 and A-18, keeps one failure path for every provider, loses no recognised text (the on-device finalisation runs first) and needs no extra event. The on-device overflow clause of architecture 4.6 is superseded (7.4, H-4). Confirmed by the orchestrator. | D: IOS-B4, 6.1 |
| R-08 | On-device live processing failure: spec 8.1 shows the library's error text; screens 14 shows "Transcription stopped on this iPhone. Resume to continue." | Screens (copy). No raw library text appears during a session. Model preparation failures keep the library's text in the alert (screens 11). | D: 6.1 |
| R-09 | OpenRouter catalogue empty and its fetch fails during Assist: screens 14 adds a new text; decisions IOS-B9 and architecture 4.5 say "that failure's text". | Decisions. The card shows the `OpenRouterError` text of the failed fetch. | S: 14 |
| R-10 | "PC connected" under the manual pairing fields: decisions IOS-B17 keeps it with a condition; screens 10 removes it. | Screens (layout). The Saved PC panel carries the state. The last sentence of architecture 4.9 is moot. | D: IOS-B17 |
| R-11 | Quick-pairing caption: screens 10 refers to "Copy pairing text" in LiveCue Desktop; decisions A-16 says the desktop never holds the payload text, and decisions 6.1 has its own caption. | Decisions. The caption is the text of decisions 6.1, and the address placeholder is the one given there. | S: 10, 16 |
| R-12 | Settings About block and the Voz usage sentence in Model Library (architecture 4.13, D-05) are missing from screens 6 and 11. | Decisions: both exist. "Display" keeps "Text size"; a new "About" panel holds Appearance, Version, Speech packages and Usage reporting. | S: 6, 11 |
| R-13 | A session cut off by an app kill: screens 13 says it keeps the title "New conversation"; decisions 5.1 says it is titled by the normal rule. | Decisions. | S: 13 |
| R-14 | Architecture 4.7 requires the identifier `transcriber-status`; screens 15 does not list it. | It exists, on line C of the status panel. | S: 4.2, 15 |
| R-15 | PC loading status line: screens 4.2 gives "Loading Nemotron 3.5 on PC · 12 s"; architecture 4.7 gives "Loading `<provider name>` on PC · `<N>` s. Microphone starts when ready." | Architecture text, composed on the phone from the provider's short name (3.2.5) and the frame's `elapsedSeconds`. | S: 4.2 |
| R-16 | Six old messages name screens that no longer exist ("Transcription settings", "Assistant settings"); screens 14 rewords them; decisions 6 says an old string changes only when listed. | Screens (copy). The six final strings are in 1.3. | D: 6.1 |
| R-17 | Smaller copy and layout changes in screens that decisions did not list: Home readiness panel merged with the setup rows ("✓ Ready" becomes a "Ready" chip, "Transcription & appearance" becomes "Transcription", "Cloud, PC or iPhone speech" and the row label "Windows relay" are gone); Settings "Text size" value; Speech benchmark title and purpose text; "RECOMMENDED" becomes "Recommended". | Screens. | D: 5.1, 6.1 |
| R-18 | New elements and strings marked **(new)** in screens (first-run banner, "Earlier answers", "Jump to live", the "Copied" toast, choice-row subtitles, lock captions, "In use" chip, "Not loaded", "Pair Windows PC" in Assistant Lab, notice titles, delete dialog). | Adopted as written. They are the screen design. | D: 6.1 |
| R-19 | Expanded transcript: spec AC-IOS-028 always scrolls to the newest text; screens 4.4 stays pinned only while the reader is at the bottom. | Screens. | D: 5.1 |
| R-20 | Choosing a provider: spec AC-IOS-093 closes the picker; screens 7 stays on Transcription. | Screens. | D: 5.1 |
| R-21 | DS 7.2 describes a "Haptics" setting; screens 6 has none. | No setting. The haptic events of screens 4 exist and follow the system setting. | none |
| R-22 | Camera denied: DS 4.15 shows an empty state with a Settings button; screens 10 closes the sheet and raises the alert. | Screens, with the alert text of decisions 6.1. | none |
| R-23 | Glass wrapper: research 1.6 sketches `LCGlass`, `lcGlass(_:in:)` and a button modifier; the CI spike (branch `spike/ios-ci`) compiled and ran `daylightChrome(in:interactive:)`, `LCGlassContainer`, `LCGlassButton`, `lcBottomBar`, `lcGlassID`, `lcScrollEdge`, `lcTabBarMinimizesOnScroll`, `LCToolbarFixedSpacer`. | The spike. It is proven on both simulator legs and matches architecture 4.12. Section 3.3.2 prints it and marks the three additions. | none |
| R-24 | Architecture 4.7 says every transcriber event carries its generation, but the printed enum has no such field, and nothing lets the controller know that the events of a finished call have been handled. | `TranscriberEvent` is a struct of `generation` and `kind`; `kind` has exactly the seven cases of architecture 4.7. The protocol gains `emittedEventCount` (3.2.5). This completes the interface; it changes no behaviour. | none |
| R-25 | Start with a PC-local provider whose model cannot start: spec 2.3 raises an alert; screens 3 disables the button with a caption. | Both. The button is disabled; the check stays in preflight for the race where the model changes between a poll and the tap. | D: 5.1 |
| R-26 | Screens 6 and 9 drop "session active" states because the screens cannot be open during a session; decisions keep AC-IOS-104 and 113. | Both. No UI state; the logic still refuses (3.2.8). | D: 5.1 |
| R-27 | Failed finalisation: architecture 4.4 says alert. | On Pause: notice titled "Paused". On Stop: global alert, shown on Home after the return (screens 4.3, 14). | D: 5.1 |
| R-28 | Failed Resume: architecture 4.4 says alert. | Notice titled "Recording paused" with "Transcription could not start. " + the reason. | S: 4.3, 14 |
| R-29 | "Pair your PC first." (spec 8.2, screens 5 and 8) and `L-NOT-PAIRED` "Pair this iPhone with the Windows relay first." (protocol 14.2). | Both. The short text is the inline state of the catalogue and PC model lines when nothing is paired (no request is attempted). `L-NOT-PAIRED` is the start alert and the Assist error. | none |
| R-30 | Project definition and CI: research 6.2 to 6.5 give a sample `project.yml`, sample commands and an explanation of simulator signing; the CI spike has a `project.yml`, a workflow, a lint script (`scripts/ci/lint_swift.sh`) and an entitlements file that ran green. | The spike wherever they differ. WP-01 starts from that branch and changes `project.yml` only as 7.2 says. The lint is a text search, so a glass symbol in a comment fails it too (8.1). | none |
| R-31 | Architecture 4.1 lists two test bundles; session controller and view-model tests need a third. | Target `LiveCueRemakeTests` (hosted unit tests, sources `ios/AppTests`) is added to the one scheme. | none |
| R-32 | DS 3.2 requires a UI test that samples the prominent glass control's fill. | Adopted as one UI test on the iOS 26 leg (6.3). | none |
| R-33 | Architecture 4.10 keeps sessions in the default SwiftData store, one row per session. | Orchestrator: the session store is file based. `FileSessionStore` keeps one JSON file per session in a folder `Sessions` under Application Support (3.2.2). The `SessionStoring` protocol is unchanged, and nothing in the app imports SwiftData. | none |
| R-34 | Architecture 4.11 lets the test seam test the Meta key (old test T3, AC-IOS-110) and forbids any socket in fixture mode, but names no fixture for the key test. | `FixtureMetaKeyTester` (3.2.10): it succeeds at once and opens nothing. It is the fixture wiring's `metaKeyTester`. | none |

### 1.2 Where each error appears

Surfaces: **Alert** = the global "LiveCue" alert; **Notice** = the banner `session-notice`; **Card** = the
inline Assist error card `assist-error`; **Line** = an inline message line on the screen that started the
action. Messages are those of spec 01 section 8 and protocol 14 unless 1.3 lists a change.

| # | Condition | Session phase when it happens | Surface | Title and text |
|---|---|---|---|---|
| E-01 | A start check fails (no Meta key, PC model not ready, not paired, relay offline, microphone denied) | `preflight` | Alert | The check's message (4.1.2) |
| E-02 | The transcriber fails to start; the session is discarded | `connecting` | Alert | "Transcription could not start. " + reason |
| E-03 | Resume fails | `resuming` | Notice | "Recording paused" / "Transcription could not start. " + reason |
| E-04 | A stream fails after it started: relay `error` frame, relay drop, Meta error event, Meta socket failure, send failure, upload queue overflow | `listening` | Notice | "Recording paused" / the message |
| E-05 | Audio interruption began, or the input device went away (every provider) | `listening` | Notice | "Recording paused" / the message |
| E-06 | On-device failure: Voz 30-minute cap, hand-off queue overflow, Parakeet processing error | `listening` | Notice | "Recording paused" / the message |
| E-07 | Finalisation fails on Pause | `pausing` | Notice | "Paused" / "Could not finalize the transcript. Some final words may be missing." |
| E-08 | Finalisation fails on Stop | `ending` | Alert, on Home | Same text |
| E-09 | Any Assist failure, including the two preconditions (not paired, no OpenRouter key) and "No speech was recognized yet." | any, with a session | Card | "Assist could not finish" / the message |
| E-10 | Session summary fails | after `ending` | none | Silent, final |
| E-11 | Pairing payload invalid; "Verify and pair" fails; camera denied, unavailable or scanner failure | no session, or Assistant Lab reached from a session | Alert | The message |
| E-12 | On-device model preparation fails | no session | Alert, and status "Preparation failed" | The library's error text |
| E-13 | Key save, test, verify, remove; catalogue load; PC model status, start, stop; health check of the saved PC | any | Line | The message |

Rules: a newer alert replaces an older one. A notice stays until Resume succeeds or the session ends. A
failure event that arrives in any phase other than `listening` is ignored (the running call reports its own
outcome). Cancellation is never shown.

### 1.3 Strings changed by these rulings

Every other string is that of spec 01 section 8, as amended by decisions 6.1 and protocol 14.

| Where | Final text |
|---|---|
| `MetaSpeechError.missingKey` | Add your Meta API key in Transcription. It stays in this iPhone's Keychain. |
| `MetaSpeechError.authentication` | Meta did not accept the connection. Check the API key, account access, and billing in Transcription. |
| `OpenRouterError.missingKey` | Add your OpenRouter API key in Assistant models & timing first. |
| `OpenRouterError.invalidKey` | OpenRouter rejected this API key. Replace it in Assistant models & timing. |
| `OpenRouterError.noModel` | Choose an OpenRouter model in Assistant models & timing first. |
| Start check, PC model not ready | PC speech model: `<home label>`. Open Transcription to start it or check its status. |
| `MetaSpeechError.malformedKey`, `OpenRouterError.malformedKey` | This key contains spaces or line breaks. Paste it again exactly as issued. |
| Notice, on-device overflow | Transcription could not keep up. Recording paused; resume to continue. |
| Notice, on-device processing failure | Transcription stopped on this iPhone. Resume to continue. |
| Notice, Voz cap | Pending recording reached 30 minutes and was transcribed. Resume to keep recording. |
| Microphone denied | Microphone permission is required for live transcription. If you use LiveContainer, allow it for LiveContainer in Settings. |
| Camera denied | Camera access is required. Enable it in Settings, or paste the pairing payload instead. If you use LiveContainer, allow it for LiveContainer in Settings. |

---

## 2. File map

One line per source file. "Owner" is the package that implements the file. WP-01 creates every file listed
here (with the final declarations and a stub body); a file owned by WP-01 is complete when WP-01 lands and
is not edited again. No file has two owners. File names are unique inside each target, because the Swift
compiler refuses two files of the same name in one module.

### 2.1 `ios/` root

| Path | Responsibility | Owner |
|---|---|---|
| `ios/project.yml` | XcodeGen definition: app, `LiveCueRemakeTests`, `LiveCueRemakeUITests`, one scheme, Info.plist keys of architecture 4.1 | WP-01 |
| `ios/Support/Simulator.entitlements` | Simulator-only entitlements so Keychain calls work (research 6.5) | WP-01 |
| `ios/LiveCueCore/Package.swift` | Package manifest: tools 6.0, Swift 6 mode, iOS 18 and macOS 15, no dependencies | WP-01 |

### 2.2 `ios/LiveCueCore/Sources/LiveCueCore/`

| Path | Responsibility | Owner |
|---|---|---|
| `Domain/TranscriptSegment.swift` | Final piece of transcript | WP-01 |
| `Domain/SessionMemory.swift` | Rolling memory and marker | WP-01 |
| `Domain/SessionNotes.swift` | End-of-session notes | WP-01 |
| `Domain/AssistantTurn.swift` | One saved answer | WP-01 |
| `Domain/Session.swift` | The unit of history | WP-01 |
| `Domain/Session+Lifecycle.swift` | Final title rule, closing an interrupted session | WP-02 |
| `Domain/AssistantProvider.swift` | The two answer services | WP-01 |
| `Domain/AssistantConfiguration.swift` | Selection, defaults, legacy decoding | WP-02 |
| `Domain/AssistantModelOption.swift` | Catalogue entry, `supports(_:)` | WP-02 |
| `Domain/RelayExecution.swift` | `RelayExecution`, `AssistantUsage` | WP-01 |
| `Domain/AssistPerformance.swift` | Timing record, transport estimate | WP-01 |
| `Domain/AssistRequest.swift` | `AssistRequest`, `SummaryRequest` | WP-01 |
| `Domain/AssistResponse.swift` | `AssistResponse`, `SummaryResponse` | WP-01 |
| `Domain/TranscriptionModel.swift` | On-device catalogue entries | WP-01 |
| `Domain/SpeechProvider.swift` | The five speech providers and their flags | WP-01 |
| `Domain/PCRequirement.swift` | "Is the PC needed" for a pair of selections | WP-01 |
| `Domain/PCModelStatus.swift` | Status object, readiness, home label | WP-02 |
| `Domain/PairingPayload.swift` | Payload parsing (protocol 4.3), manual entry, stored text | WP-01 |
| `Domain/APIKeyShape.swift` | Empty and whitespace checks for API keys | WP-02 |
| `Usage/SpeechStreamUsage.swift` | One stream's usage record | WP-01 |
| `Usage/UsageUpdate.swift` | Usage events sent by transcribers | WP-01 |
| `Usage/TranscriptionUsage.swift` | Usage operations and derived values | WP-02 |
| `Coding/LiveCueJSON.swift` | The one encoder and decoder configuration | WP-02 |
| `Coding/ISO8601Milliseconds.swift` | Date text without a formatter object | WP-02 |
| `Coding/JSONValue.swift` | Strict dynamic JSON value | WP-01 |
| `Context/ContextBuilder.swift` | Assist and summary requests, same-text retry | WP-02 |
| `Context/WireBudget.swift` | Byte budgets and the fitting step (protocol 13.4) | WP-02 |
| `WER/WordErrorRate.swift` | Word error rate | WP-02 |
| `Relay/RelayConstants.swift` | Protocol constants used by the phone | WP-01 |
| `Relay/RelayRoute.swift` | Route paths, methods, phone timeouts | WP-01 |
| `Relay/RelayHealth.swift` | Health reply | WP-01 |
| `Relay/RelayFailure.swift` | `RelayFailure`, `RelayWireCode`, `RelayLocalCondition` | WP-01 |
| `Relay/RelayEndpoint.swift` | Endpoint grammar, canonical form, URL building | WP-01 |
| `Relay/RelayOutcome.swift` | Internal: classification of an HTTP outcome (protocol 13.6) | WP-03 |
| `Relay/RelayMessages.swift` | Phone texts for codes and conditions (protocol 14) | WP-03 |
| `Relay/RelayClient.swift` | The nine HTTP routes, timeouts, echo check, cancel | WP-03 |
| `Relay/RequestDeadline.swift` | Internal: the total time limit of one request, raced against `CoreTiming.sleep`; used by both HTTP clients | WP-03 |
| `Relay/RelaySpeechFrame.swift` | Parsing of relay speech frames | WP-04 |
| `Relay/RelaySpeechNegotiation.swift` | Protocol 13.7 as a pure state machine | WP-04 |
| `OpenRouter/OpenRouterError.swift` | Error cases and their texts | WP-01 |
| `OpenRouter/OpenRouterCatalogue.swift` | Internal: catalogue decoding, filter, sort | WP-03 |
| `OpenRouter/OpenRouterStreamParser.swift` | Internal: SSE parser and whole-body form | WP-03 |
| `OpenRouter/OpenRouterClient.swift` | Key check, catalogue, streamed assist | WP-03 |
| `Meta/MetaSpeechError.swift` | Error cases and their texts | WP-01 |
| `Meta/MetaEvent.swift` | Sanitised Meta event | WP-01 |
| `Meta/MetaRealtimeAPI.swift` | Endpoint, handshake, acknowledgement, sanitiser | WP-04 |
| `Meta/MetaConnectionTest.swift` | The key test without audio | WP-04 |
| `Transcript/StreamTranscriptEvent.swift` | Input of the caption state machine | WP-01 |
| `Transcript/StreamTranscript.swift` | Caption merge state machine | WP-04 |
| `Transcript/PCMFramer.swift` | 80 ms framing with a final remainder | WP-04 |
| `Transcript/PendingAudioBuffer.swift` | Voz pending audio: cap, overlap, cursor | WP-04 |
| `Transcript/SpeechStreamTypes.swift` | Dialect, status, event, failure, ending, timing | WP-01 |
| `Transcript/SpeechStreamSession.swift` | One streaming speech connection, Meta or relay | WP-04 |
| `Transport/HTTPTransport.swift` | `HTTPRequest`, `HTTPResponseHead`, `HTTPTransport` | WP-01 |
| `Transport/WebSocketTransport.swift` | `WebSocketMessage`, `WebSocketConnection`, `WebSocketTransport` | WP-01 |
| `Transport/TransportFailure.swift` | `TransportFailure`, `WebSocketUpgradeRejected` | WP-01 |

### 2.3 `ios/LiveCueCore/Tests/LiveCueCoreTests/`

| Path | Responsibility | Owner |
|---|---|---|
| `Support/FakeHTTPTransport.swift` | Scripted HTTP transport that records requests | WP-01 |
| `Support/FakeWebSocketTransport.swift` | Scripted socket that records frames | WP-01 |
| `Support/CoreFixtures.swift` | Synthetic pairing, tokens, sessions, replies | WP-01 |
| `Support/ManualTime.swift` | Controllable sleep and uptime for timeouts | WP-01 |
| `ContractSmokeTests.swift` | Constants of protocol section 2 have their values | WP-01 |
| `PairingPayloadTests.swift`, `RelayEndpointTests.swift`, `JSONValueTests.swift` | Section 6.1 | WP-01 |
| `DomainCodingTests.swift`, `AssistantConfigurationTests.swift`, `LiveCueJSONTests.swift`, `SessionLifecycleTests.swift`, `ContextBuilderTests.swift`, `WireBudgetTests.swift`, `WordErrorRateTests.swift`, `TranscriptionUsageTests.swift`, `PCModelStatusTests.swift`, `SpeechProviderTests.swift` | Section 6.1 | WP-02 |
| `RelayClientRoutesTests.swift`, `RelayClientClassificationTests.swift`, `RelayClientAssistTests.swift`, `RelayMessagesTests.swift`, `OpenRouterClientTests.swift`, `OpenRouterCatalogueTests.swift`, `OpenRouterStreamParserTests.swift` | Section 6.1 | WP-03 |
| `StreamTranscriptTests.swift`, `MetaRealtimeAPITests.swift`, `MetaConnectionTestTests.swift`, `PCMFramerTests.swift`, `PendingAudioBufferTests.swift`, `RelaySpeechFrameTests.swift`, `RelaySpeechNegotiationTests.swift`, `SpeechStreamSessionRelayTests.swift`, `SpeechStreamSessionMetaTests.swift` | Section 6.1 | WP-04 |

### 2.4 `ios/App/`

| Path | Responsibility | Owner |
|---|---|---|
| `AppShell/LiveCueApp.swift` | App entry; builds the environment once; forces light appearance | WP-01 |
| `AppShell/RootView.swift` | Swaps tab bar and live screen; global alert; toast overlay; scene-phase hook | WP-01 |
| `AppShell/RouteDestination.swift` | Maps a `Route` to its screen | WP-01 |
| `AppShell/AppRoutes.swift` | `AppTab`, `Route` | WP-01 |
| `AppShell/AppModel.swift` | Navigation state and the global alert | WP-01 |
| `AppShell/SessionActivity.swift` | Shared flags "a session exists" and "assisting" | WP-01 |
| `AppShell/AppDependencies.swift` | The protocol-typed dependencies | WP-01 |
| `AppShell/AppEnvironment.swift` | The container: dependencies, state owners, view models, bootstrap | WP-01 |
| `AppShell/AppEnvironment+Live.swift` | Live wiring | WP-01 |
| `AppShell/AppEnvironment+Fixture.swift` | Fixture wiring | WP-01 |
| `AppShell/EnvironmentInjection.swift` | `appEnvironment(_:)` view modifier | WP-01 |
| `AppShell/SystemReadiness.swift` | needsPC, speechReady, systemReady, provider lock reason | WP-09 |
| `Session/SessionTypes.swift` | `SessionPhase`, `AssistStage`, `SessionNotice` | WP-01 |
| `Session/SessionController.swift` | State machine, tick, event handling | WP-08 |
| `Session/SessionController+Assist.swift` | Assist pipeline and retry | WP-08 |
| `Session/SessionController+Summary.swift` | End-of-session summary and its cancellation | WP-08 |
| `Session/SessionPersistence.swift` | Latest-wins save queue | WP-08 |
| `Session/TranscriberEventPump.swift` | Consumes transcriber events in order; the "processed through" barrier | WP-08 |
| `Session/SessionTexts.swift` | Messages the controller composes | WP-08 |
| `Speech/SpeechTranscriber.swift` | `SpeechTranscriber`, `TranscriberStart`, `TranscriberEvent`, `TranscriberFailure` | WP-01 |
| `Speech/TranscriberEventChannel.swift` | Stream plus emitted-event counter used by every transcriber | WP-01 |
| `Speech/SpeechTranscriberFactory.swift` | Factory protocol | WP-01 |
| `Speech/MetaKeyTesting.swift` | Key-test protocol and its live implementation over `MetaConnectionTest` | WP-01 |
| `Speech/SpeechProvider+Presentation.swift` | Short names, picker titles, Home lines, placeholders | WP-01 |
| `Speech/OnDeviceEngines.swift` | `BatchRecognizing`, `StreamingRecognizing`, `OnDeviceEngineLoading`, progress type | WP-01 |
| `Speech/LiveTranscriberFactory.swift` | Builds the real transcriber for a provider | WP-07 |
| `Speech/StreamingTranscriberEngine.swift` | Shared by Meta and relay: capture to `SpeechStreamSession`, event mapping | WP-07 |
| `Speech/MetaTranscriber.swift` | Provider `meta` | WP-07 |
| `Speech/RelayTranscriber.swift` | Providers `nemotron`, `qwen3` | WP-07 |
| `Speech/VozTranscriber.swift` | Provider `voz` | WP-07 |
| `Speech/ParakeetTranscriber.swift` | Provider `parakeet` | WP-07 |
| `Speech/TranscriberStatusText.swift` | Status line texts of architecture 4.7 | WP-07 |
| `Speech/VozEngine.swift` | The only file that imports `Voz` | WP-07 |
| `Speech/ParakeetEngine.swift` | The only file that imports `FluidAudio` | WP-07 |
| `Speech/LiveOnDeviceEngineLoader.swift` | Loads either engine; backup exclusion | WP-07 |
| `Speech/OnDeviceModelManager.swift` | Preparation, progress, reload, unload | WP-07 |
| `Speech/SpeechSettings.swift` | Selected provider, Meta key actions | WP-09 |
| `Audio/AudioCapturing.swift` | Capture protocol and its value types | WP-01 |
| `Audio/AudioCapture.swift` | `AVAudioEngine` capture, interruptions, route loss | WP-07 |
| `Audio/AudioTapConverter.swift` | The tap block and format conversion, off the main actor | WP-07 |
| `Audio/AudioSessionPolicy.swift` | Category, mode, options, activation rules | WP-07 |
| `Answers/AnswerService.swift` | `AnswerService`, `AnswerServiceProviding`, `OpenRouterKeyVerifying` | WP-01 |
| `Answers/CodexRelayAnswerService.swift` | Service `codex` | WP-08 |
| `Answers/OpenRouterAnswerService.swift` | Service `openrouter` | WP-08 |
| `Answers/LiveAnswerServices.swift` | Returns the service for a provider | WP-08 |
| `Answers/AnswerErrorText.swift` | Any thrown error to the text of the Assist card | WP-08 |
| `Answers/AssistantSettings.swift` | Answer selection, catalogue, OpenRouter key, instruction | WP-09 |
| `Connectivity/RelayGateway.swift` | Protocol over `RelayClient` | WP-01 |
| `Connectivity/ConnectivityTypes.swift` | `SavedPCState`, `InlineMessage` | WP-01 |
| `Connectivity/RelayMonitor.swift` | Health and PC model polling, PC model commands | WP-09 |
| `Connectivity/PairingController.swift` | Reading a payload, verify and pair | WP-09 |
| `Persistence/SessionStoring.swift` | Session store protocol | WP-01 |
| `Persistence/InMemorySessionStore.swift` | In-memory store for fixture mode and tests | WP-01 |
| `Persistence/PreferencesStoring.swift` | Preferences protocol | WP-01 |
| `Persistence/SecretStoring.swift` | Secret store protocol, `SecretKind`, `SecretStoreFailure` | WP-01 |
| `Persistence/PairingStoring.swift` | Pairing store protocol | WP-01 |
| `Persistence/StorageNames.swift` | Keys, accounts and file names, live and test | WP-01 |
| `Persistence/FileSessionStore.swift` | Sessions on disk: one JSON file per session under Application Support (R-33) | WP-06 |
| `Persistence/UserDefaultsPreferencesStore.swift` | Preferences in `UserDefaults.standard` | WP-06 |
| `Persistence/KeychainSecretStore.swift` | API keys in the Keychain | WP-06 |
| `Persistence/FilePairingStore.swift` | Pairing: the Keychain item (the authority) and the fallback file (3.2.2) | WP-06 |
| `Persistence/HistoryModel.swift` | Stored sessions in memory, delete | WP-09 |
| `Platform/AppClock.swift` | Clock protocol and `SystemClock` | WP-01 |
| `Platform/PermissionProviding.swift` | Microphone and camera permission protocol | WP-01 |
| `Platform/ClipboardWriting.swift` | Clipboard protocol and `SystemClipboard` | WP-01 |
| `Platform/QRScanFailure.swift` | Scanner failures and their texts | WP-01 |
| `Platform/AppVersion.swift` | Marketing version or "Unknown" | WP-01 |
| `Platform/URLSessionHTTPTransport.swift` | `HTTPTransport` over `URLSession`, including `bytes(for:)` | WP-06 |
| `Platform/URLSessionWebSocketTransport.swift` | `WebSocketTransport` over `URLSessionWebSocketTask` | WP-06 |
| `Platform/URLErrorMapping.swift` | The one mapping from system errors to `TransportFailure` | WP-06 |
| `Platform/KeychainStore.swift` | Generic-password calls | WP-01 |
| `Platform/SystemPermissions.swift` | Real permission requests | WP-06 |
| `Platform/QRScannerView.swift` | Camera preview and QR metadata output | WP-06 |
| `Platform/BackupExclusion.swift` | Marks a folder excluded from backup | WP-06 |
| `DesignSystem/LiquidGlass.swift` | Every iOS 26 symbol, with the fallback | WP-05 |
| `DesignSystem/Theme.swift` | Token structure and the environment value | WP-05 |
| `DesignSystem/Theme+Daylight.swift` | Daylight values | WP-05 |
| `DesignSystem/Theme+Resolved.swift` | Reduce Transparency and Increase Contrast variants | WP-05 |
| `DesignSystem/DesignTypes.swift` | `TextRole`, `StatusTone`, `ChipMark`, `BannerTone`, button enums | WP-05 |
| `DesignSystem/GroundView.swift` | The ground | WP-05 |
| `DesignSystem/ScreenScaffold.swift` | Ground, scroll container, insets | WP-05 |
| `DesignSystem/Panel.swift`, `Card.swift`, `SectionTitle.swift` | Surfaces (DS 4.9) | WP-05 |
| `DesignSystem/LCButton.swift`, `LCButtonStyle.swift`, `IconButton.swift` | Buttons (DS 4.1 to 4.3) | WP-05 |
| `DesignSystem/LCNavigationRow.swift`, `ChoiceRow.swift`, `DetailRow.swift` | Rows (DS 4.8) | WP-05 |
| `DesignSystem/StatusChip.swift`, `Banner.swift`, `ToastCenter.swift`, `Spinner.swift`, `LCProgressBar.swift` | Status elements (DS 4.11 to 4.13) | WP-05 |
| `DesignSystem/LCTextField.swift`, `LCSecureField.swift` | Fields (DS 4.6, 4.7) | WP-05 |
| `DesignSystem/EmptyStateView.swift`, `DisclosureRow.swift` | DS 4.14, 4.24 | WP-05 |
| `DesignSystem/LevelMeter.swift`, `TranscriptLine.swift`, `LiveCaret.swift` | DS 4.18, 4.21 | WP-05 |
| `DesignSystem/SessionStartControl.swift`, `SessionControlCluster.swift` | DS 4.17 | WP-05 |
| `DesignSystem/BrandMark.swift` | In-app mark and two-tone name (DS 8) | WP-05 |
| `DesignSystem/Haptics.swift` | The events of DS 7.2 | WP-05 |
| `DesignSystem/Surfaces.swift`, `Glyph.swift`, `Pill.swift`, `Scaling.swift`, `Pulse.swift`, `RowSupport.swift`, `BrandMarkArtwork.swift` | Private helpers of the components: the content surfaces, icon sizes, the pill shape, lengths that follow the text size, the live pulse, the row layout inside a panel or card, the geometry of the mark | WP-05 |
| `DesignSystem/DesignGallery.swift` | Debug builds only: a gallery of the components, shown in place of every screen when the app is launched with `-design-gallery` | WP-05 |
| `Features/Home/HomeScreen.swift`, `HomeViewModel.swift`, `HomeReadinessPanel.swift` | Screens 3 | WP-10 |
| `Features/Home/ContainerHeightReader.swift` | Private helper of Home and the live session: the measured container height that the compact rules are written against | WP-10 |
| `Features/LiveSession/LiveSessionScreen.swift`, `LiveSessionViewModel.swift`, `LiveStatusPanel.swift`, `SessionNoticeBanner.swift`, `LiveTranscriptPanel.swift`, `AnswerFeed.swift`, `AssistErrorCard.swift`, `InProgressCard.swift`, `AnswerCard.swift`, `TimingView.swift`, `TimingViewContent.swift` | Screens 4 | WP-10 |
| `Features/LiveSession/AnswerFormat.swift`, `CueCardHeader.swift`, `LiveLayout.swift` | Private helpers of the live screen: the numbers of an answer as text, the header of a cue card, the layout values of screens 4.1 | WP-10 |
| `Features/AssistantLab/AssistantLabScreen.swift`, `AssistantLabViewModel.swift`, `AnswerServicePanel.swift`, `AssistantModelPanel.swift`, `OpenRouterKeyPanel.swift`, `RecentComparisonsPanel.swift`, `OpenRouterModelsScreen.swift`, `ReasoningScreen.swift`, `RunDetailsScreen.swift` | Screens 5 | WP-10 |
| `Features/AssistantLab/AssistantMessageLine.swift` | Private helper of Assistant Lab: the result line of a key action | WP-10 |
| `Features/Settings/SettingsScreen.swift`, `SettingsViewModel.swift`, `ProviderSetupRows.swift` | Screens 6; the provider rows are shared with Transcription | WP-11 |
| `Features/Transcription/TranscriptionScreen.swift`, `TranscriptionViewModel.swift` | Screens 7 | WP-11 |
| `Features/PCSpeechModel/PCSpeechModelScreen.swift`, `PCSpeechModelViewModel.swift` | Screens 8 | WP-11 |
| `Features/MetaKey/MetaKeyScreen.swift`, `MetaKeyViewModel.swift` | Screens 9 | WP-11 |
| `Features/Pairing/PairingScreen.swift`, `PairingViewModel.swift`, `SavedPCPanel.swift`, `QRScanSheet.swift` | Screens 10 | WP-11 |
| `Features/ModelLibrary/ModelLibraryScreen.swift`, `ModelLibraryViewModel.swift` | Screens 11 | WP-11 |
| `Features/Benchmark/BenchmarkScreen.swift`, `BenchmarkViewModel.swift` | Screens 12 | WP-11 |
| `Features/History/HistoryScreen.swift`, `HistoryViewModel.swift`, `HistoryRow.swift` | Screens 13 | WP-11 |
| `Features/SessionDetail/SessionDetailScreen.swift`, `SessionDetailContent.swift` | Screens 13 | WP-11 |
| `TestSeam/LaunchConfiguration.swift` | Launch-argument parsing; unit-test host detection | WP-01 |
| `TestSeam/TestMode.swift` | The thirteen modifiers | WP-01 |
| `TestSeam/FixtureValues.swift` | Fixture texts, catalogues, synthetic pairing | WP-01 |
| `TestSeam/FixturePairingStore.swift`, `FixturePermissions.swift` | "Always paired"; permissions always granted, and the Meta key test that opens no socket (R-34) | WP-01 |
| `TestSeam/TestSeamLaunchActions.swift` | Key resets, pairing fixture, dropped Keychain copy | WP-06 |
| `TestSeam/FixtureTranscriber.swift`, `FixtureTranscriberFactory.swift`, `FixtureEngineLoader.swift` | Speech fixtures | WP-07 |
| `TestSeam/FixtureAnswerServices.swift` | Answer fixtures | WP-08 |
| `TestSeam/FixtureRelayGateway.swift` | Relay fixture: health, catalogue, PC model | WP-09 |
| `Resources/Assets.xcassets` | App icon (committed, from the icon tool) | WP-01 |

### 2.5 `ios/AppTests/` (target `LiveCueRemakeTests`)

| Path | Responsibility | Owner |
|---|---|---|
| `Support/ManualClock.swift`, `FakeTranscriber.swift`, `FakeAnswerService.swift`, `FakeRelayGateway.swift`, `FakeStores.swift`, `FakeAudioCapture.swift`, `FakeEngines.swift`, `TestOwners.swift` | Fakes and a builder that wires state owners to them (3.2.11) | WP-01 |
| `LaunchConfigurationTests.swift`, `AppEnvironmentSmokeTests.swift`, `KeychainStoreTests.swift` | Section 6.2 | WP-01 |
| `KeychainSecretStoreTests.swift`, `FilePairingStoreTests.swift`, `UserDefaultsPreferencesStoreTests.swift`, `FileSessionStoreTests.swift`, `URLErrorMappingTests.swift`, `TestSeamLaunchActionsTests.swift` | Section 6.2 | WP-06 |
| `StreamingTranscriberEngineTests.swift`, `VozTranscriberTests.swift`, `ParakeetTranscriberTests.swift`, `OnDeviceModelManagerTests.swift`, `FixtureTranscriberTests.swift` | Section 6.2 | WP-07 |
| `SessionControllerStateTests.swift`, `SessionControllerAssistTests.swift`, `SessionControllerSummaryTests.swift`, `SessionPersistenceTests.swift`, `AnswerServicesTests.swift` | Section 6.2 | WP-08 |
| `RelayMonitorTests.swift`, `PairingControllerTests.swift`, `AssistantSettingsTests.swift`, `SpeechSettingsTests.swift`, `HistoryModelTests.swift`, `SystemReadinessTests.swift` | Section 6.2 | WP-09 |
| `HomeViewModelTests.swift`, `LiveSessionViewModelTests.swift`, `AssistantLabViewModelTests.swift`, `TimingViewContentTests.swift`, `AnswerPlainTextTests.swift` | Section 6.2 | WP-10 |
| `SettingsViewModelTests.swift`, `TranscriptionViewModelTests.swift`, `PCSpeechModelViewModelTests.swift`, `MetaKeyViewModelTests.swift`, `PairingViewModelTests.swift`, `ModelLibraryViewModelTests.swift`, `BenchmarkViewModelTests.swift`, `HistoryViewModelTests.swift`, `SessionDetailContentTests.swift` | Section 6.2 | WP-11 |

### 2.6 `ios/UITests/` (target `LiveCueRemakeUITests`)

| Path | Responsibility | Owner |
|---|---|---|
| `UITestSupport.swift` | Launch helper, lookup by identifier, wait helpers, screenshot attachment | WP-01 |
| `ShellUITests.swift` | The app launches into three tabs | WP-01 |
| `ConversationUITests.swift` | T1, T2, T16 | WP-12 |
| `CloudOnlyUITests.swift` | T3, T4 | WP-12 |
| `PCModelUITests.swift` | T5, T6, T8, T9 | WP-12 |
| `AssistantUITests.swift` | T10, T11 | WP-12 |
| `OnDeviceUITests.swift` | T12 | WP-12 |
| `PairingUITests.swift` | T7, T13 | WP-12 |
| `SettingsUITests.swift` | T14, T15 | WP-12 |
| `HistoryUITests.swift` | Delete confirmation | WP-12 |
| `GlassContrastUITests.swift` | DS 3.2 gate | WP-12 |
| `UITestWalk.swift` | Steps, lookups and waits the tests of 6.3 share on top of `UITestSupport`: starting, assisting and stopping a session, the PC model walk, typing a key, alerts, and waits that look once before they wait | WP-12 |

As built (WP-12): the screenshot-only files of the screen builders, `ScreensAScreenshotUITests.swift` (WP-10) and `ScreensBScreenshotUITests.swift` (WP-11), are removed. The tests of 6.3 walk the same screens and attach pictures of them.

WP-01 starts from the CI spike (branch `spike/ios-ci`), whose layout already matches this map. Spike files
that WP-01 removes because this map replaces them: `ios/App/AppShell/PlaceholderRootView.swift`,
`ios/App/Speech/SpeechPackages.swift`, `ios/App/TestSeam/TestSeam.swift`,
`ios/App/TestSeam/KeychainProbe.swift`, `ios/UITests/PlaceholderUITests.swift`,
`ios/LiveCueCore/Sources/LiveCueCore/SemanticVersion.swift` and its test. The spike's proof that the
Keychain works in a signed simulator build moves into `KeychainStoreTests`. Spike files that stay as they
are and are extended by WP-05: `DesignSystem/LiquidGlass.swift`, `Theme.swift`, `GroundView.swift`.

---
## 3. Contracts

### 3.0 Rules that apply to every contract

| Area | Rule |
|---|---|
| Core package | Swift 6 language mode (tools 6.0). Every public type is an explicit `Sendable` value type, a `Sendable` protocol, or an actor. Every file imports `Foundation` only. Every public struct declares its public initialiser. No static stored property of a non-`Sendable` type (no formatter, encoder, decoder or `Regex` kept in a `static let`). |
| App target | Swift 5 language mode with `SWIFT_STRICT_CONCURRENCY: complete` (research 6.6). `SWIFT_DEFAULT_ACTOR_ISOLATION` unset. Never treat warnings as errors. |
| State owners and view models | `@MainActor @Observable final class`. Observation only: no `ObservableObject`, `@Published`, `@StateObject`, `@ObservedObject`, `@EnvironmentObject`. |
| Dependencies that touch I/O | `Sendable` protocols with `async` methods, implemented by `Sendable` structs or actors. The two exceptions are `SpeechTranscriber` and `AudioCapturing`, which are `@MainActor` protocols (architecture 4.7). |
| Errors | Each contract names the error types it throws. `CancellationError` always means "the caller went away" and is never shown. |
| Time | The core takes time through `CoreTiming`; the app through `AppClock`. Nothing calls `Date()`, `Task.sleep` or `ProcessInfo.systemUptime` directly outside those two types and their system implementations. |

### 3.1 Core package `LiveCueCore`

#### 3.1.1 Transport (architecture 4.2; fixed)

```swift
public struct HTTPRequest: Sendable {
    public var method: String
    public var url: URL
    public var headers: [String: String]
    public var body: Data?
    /// Idle timeout in seconds. Total time limits are enforced by the client that sends the request.
    public var timeout: TimeInterval
    public init(method: String, url: URL, headers: [String: String] = [:], body: Data? = nil, timeout: TimeInterval)
}

public struct HTTPResponseHead: Sendable {
    public var status: Int
    /// Header names are lower-cased by the transport.
    public var headers: [String: String]
    public init(status: Int, headers: [String: String] = [:])
}

/// Contract for every implementation: ephemeral session, no cookies, no cache, redirects never followed
/// (a 3xx is returned as a response). Throws only `TransportFailure`. Task cancellation surfaces as
/// `TransportFailure.cancelled`.
public protocol HTTPTransport: Sendable {
    func send(_ request: HTTPRequest) async throws -> (HTTPResponseHead, Data)
    /// The stream yields body chunks of any size, in order, and finishes at end of body.
    func stream(_ request: HTTPRequest) async throws -> (HTTPResponseHead, AsyncThrowingStream<Data, Error>)
}

public enum TransportFailure: Error, Equatable, Sendable { case cancelled, timedOut, tls, unreachable, other }

public struct WebSocketUpgradeRejected: Error, Equatable, Sendable {
    public var status: Int
    public init(status: Int)
}

public enum WebSocketMessage: Equatable, Sendable {
    case text(String)
    case data(Data)
    /// The peer closed, or the close handshake finished. Delivered once; `receive()` throws afterwards.
    case closed(code: Int, reason: String)
}

public protocol WebSocketConnection: Sendable {
    func send(text: String) async throws          // TransportFailure
    func send(data: Data) async throws            // TransportFailure
    func receive() async throws -> WebSocketMessage   // TransportFailure when the connection dropped without a close frame
    func close(code: Int, reason: String) async   // idempotent; never throws
}

public protocol WebSocketTransport: Sendable {
    /// Returns once the upgrade succeeded. Throws `WebSocketUpgradeRejected` for an HTTP answer other than
    /// 101, `TransportFailure` otherwise. No `Origin`, no subprotocol, no compression.
    func open(url: URL, headers: [String: String], maximumMessageSize: Int) async throws -> any WebSocketConnection
}

/// Monotonic time and sleeping, replaceable in tests.
public struct CoreTiming: Sendable {
    public var uptime: @Sendable () -> TimeInterval
    public var sleep: @Sendable (TimeInterval) async throws -> Void
    public init(uptime: @escaping @Sendable () -> TimeInterval, sleep: @escaping @Sendable (TimeInterval) async throws -> Void)
    /// `ProcessInfo.processInfo.systemUptime` and `Task.sleep(nanoseconds:)`.
    public static let system: CoreTiming
}
```

`CoreTiming` lives in `Transport/HTTPTransport.swift`.

#### 3.1.2 Domain model

Coding rules for every type below (spec 02 sections 0 and 1, as changed by the register):

- Synthesised `Codable` unless an initialiser is printed. Unknown keys are ignored. An absent optional is
  omitted on encode (never `null`) and accepts absent or `null` on decode.
- UUIDs are written upper case (Foundation's `uuidString`) and accepted in either case.
- Dates follow the encoder in use. `LiveCueJSON` writes ISO 8601 UTC with exactly three fractional digits.
  Initialisers that take a `Date` round it to whole milliseconds, so a value round-trips exactly through
  `LiveCueJSON` (CORE-A12).
- Computed properties are never encoded.

```swift
public struct TranscriptSegment: Codable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public var text: String
    public var startSeconds: Double
    public var endSeconds: Double
    public init(id: UUID = UUID(), text: String, startSeconds: Double, endSeconds: Double)
}   // No `isFinal` member: every segment is final (CORE-C2). A stored `isFinal` key is ignored.

public struct SessionMemory: Codable, Equatable, Sendable {
    public var summary: String
    public var facts: [String]
    public var openQuestions: [String]
    public var throughSegmentId: UUID?   // a string that is not a UUID fails decoding of the whole value; a relay reply is therefore never decoded directly (3.1.5)
    public init(summary: String = "", facts: [String] = [], openQuestions: [String] = [], throughSegmentId: UUID? = nil)
}

public struct SessionNotes: Codable, Equatable, Sendable {
    public var summary: String
    public var keyPoints: [String]
    public var actionItems: [String]
    public init(summary: String = "", keyPoints: [String] = [], actionItems: [String] = [])
}

public enum AssistantProvider: String, Codable, CaseIterable, Sendable {
    case codex, openrouter
    public var displayName: String { get }   // "PC · Codex", "OpenRouter · direct" (U+00B7 with a space each side)
}

public struct AssistantConfiguration: Codable, Equatable, Sendable {
    public var model: String
    public var reasoningEffort: String
    public var provider: AssistantProvider
    public init(model: String = "gpt-5.6-sol", reasoningEffort: String = "low", provider: AssistantProvider = .codex)
    public init(from decoder: any Decoder) throws     // `provider` absent means `.codex`; it is always written
    public static func `default`(for provider: AssistantProvider) -> AssistantConfiguration
    // codex: gpt-5.6-sol / low.  openrouter: "" / default.
}

public struct AssistantModelOption: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var name: String
    public var reasoningEfforts: [String]
    public var structuredOutputs: Bool?
    public var promptPrice: String?
    public var completionPrice: String?
    public init(id: String, name: String, reasoningEfforts: [String], structuredOutputs: Bool? = nil,
                promptPrice: String? = nil, completionPrice: String? = nil)
    /// The effort to use after selecting this option: `current` when listed, else the first listed, else "low".
    public func effort(keeping current: String) -> String
}

extension Array where Element == AssistantModelOption {
    /// True when an option has this id and lists this effort. The model check of every Assist.
    public func supports(_ configuration: AssistantConfiguration) -> Bool
}

public struct AssistantUsage: Codable, Equatable, Sendable {
    public var generationId: String?
    public var promptTokens: Int?
    public var completionTokens: Int?
    public var costCredits: Double?
    public init(generationId: String? = nil, promptTokens: Int? = nil, completionTokens: Int? = nil, costCredits: Double? = nil)
}

public struct RelayExecution: Codable, Equatable, Sendable {
    public var model: String
    public var reasoningEffort: String
    public var codexMs: Double
    public var relayTotalMs: Double
    public var requestReadMs: Double
    public var relayOverheadMs: Double
    public var usage: AssistantUsage?
    public init(model: String, reasoningEffort: String, codexMs: Double, relayTotalMs: Double,
                requestReadMs: Double, relayOverheadMs: Double, usage: AssistantUsage? = nil)
}

public struct AssistPerformance: Codable, Equatable, Sendable {
    public var configuration: AssistantConfiguration
    public var speechModel: String
    public var reusedText: Bool
    public var transcriptionMs: Double
    public var contextMs: Double
    public var roundTripMs: Double
    public var totalMs: Double
    public var lastLiveChunkMs: Double?
    public var transcriptCharacters: Int
    public var execution: RelayExecution?
    public init(configuration: AssistantConfiguration, speechModel: String, reusedText: Bool, transcriptionMs: Double,
                contextMs: Double, roundTripMs: Double, totalMs: Double, lastLiveChunkMs: Double? = nil,
                transcriptCharacters: Int, execution: RelayExecution? = nil)
    /// max(0, roundTripMs - execution.relayTotalMs); nil without an execution record.
    public var transportEstimateMs: Double? { get }
}

public struct AssistantTurn: Codable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public var createdAt: Date
    public var request: String?
    public var detectedQuestion: String
    public var answer: String
    public var details: String
    public var performance: AssistPerformance?
    /// The detected question of every OpenRouter answer. The UI hides it (CORE-B5).
    public static let latestConversation = "Latest conversation"
    public init(id: UUID = UUID(), createdAt: Date = Date(), request: String? = nil, detectedQuestion: String,
                answer: String, details: String, performance: AssistPerformance? = nil)
}

public struct Session: Codable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public var title: String
    public var startedAt: Date
    public var endedAt: Date?
    public var segments: [TranscriptSegment]
    public var assistantTurns: [AssistantTurn]
    public var memory: SessionMemory
    public var notes: SessionNotes
    public var transcriptionUsage: TranscriptionUsage?
    public static let defaultTitle = "New conversation"
    public init(id: UUID = UUID(), title: String = Session.defaultTitle, startedAt: Date = Date(), endedAt: Date? = nil,
                segments: [TranscriptSegment] = [], assistantTurns: [AssistantTurn] = [],
                memory: SessionMemory = SessionMemory(), notes: SessionNotes = SessionNotes())
}

extension Session {   // Domain/Session+Lifecycle.swift
    /// The first 48 characters of the first segment's text, or "Conversation" without segments.
    public func finalTitle() -> String
    /// Launch recovery: end time = `lastSaved`, title by `finalTitle()`, every stream still "streaming"
    /// becomes "interrupted". A session that already has an end time is returned unchanged.
    public func closedAfterInterruption(at lastSaved: Date) -> Session
    /// Every segment's text joined by single spaces (Speech benchmark).
    public var spaceJoinedTranscript: String { get }
}

public struct AssistRequest: Codable, Equatable, Sendable {
    public var assistant: AssistantConfiguration?
    public var requestId: UUID
    public var sessionId: UUID
    public var instruction: String?
    public var memory: SessionMemory
    public var transcript: String
    public var partialTranscript: String?
    public init(requestId: UUID = UUID(), sessionId: UUID, instruction: String? = nil, memory: SessionMemory,
                transcript: String, partialTranscript: String? = nil)
}

public struct SummaryRequest: Codable, Equatable, Sendable {
    public var requestId: UUID
    public var sessionId: UUID
    public var assistant: AssistantConfiguration
    public var memory: SessionMemory
    public var transcript: String
    /// `true` when the transcript was cut, otherwise absent. Never `false`.
    public var transcriptTruncated: Bool?
    public init(requestId: UUID = UUID(), sessionId: UUID, assistant: AssistantConfiguration, memory: SessionMemory,
                transcript: String, transcriptTruncated: Bool? = nil)
}

public struct AssistResponse: Codable, Equatable, Sendable {
    public var execution: RelayExecution?
    public var detectedQuestion: String
    public var answer: String
    public var details: String
    public var memory: SessionMemory
    public init(detectedQuestion: String, answer: String, details: String, memory: SessionMemory)
}

public struct SummaryResponse: Codable, Equatable, Sendable {
    public var title: String
    public var summary: String
    public var keyPoints: [String]
    public var actionItems: [String]
    public init(title: String, summary: String, keyPoints: [String], actionItems: [String])
}

public enum SpeechProvider: String, Codable, CaseIterable, Sendable {
    case meta, nemotron, qwen3, voz, parakeet            // catalogue order
    public var displayName: String { get }               // spec 02 section 7.1
    public var isCloud: Bool { get }                     // meta
    public var isPCLocal: Bool { get }                   // nemotron, qwen3
    public var isOnDevice: Bool { get }                  // voz, parakeet
    public var streamsAudio: Bool { get }                // isCloud || isPCLocal
    public var sampleRate: Int { get }                   // 24000 for meta, otherwise 16000
    /// `.meta` for nil or any unknown raw value.
    public static func stored(_ rawValue: String?) -> SpeechProvider
}

public enum PCRequirement {
    /// True when the speech provider is PC-local or the answer service is Codex.
    public static func needsPC(speech: SpeechProvider, answers: AssistantProvider) -> Bool
}

public struct TranscriptionModel: Codable, Equatable, Sendable, Identifiable {
    public var variant: String
    public var displayName: String
    public var approximateMegabytes: Int
    public var quality: String
    public var recommended: Bool
    public var id: String { get }                        // == variant
    public init(variant: String, displayName: String, approximateMegabytes: Int, quality: String, recommended: Bool)
    public static let voz: TranscriptionModel            // "voz", "Voz on Assist", 467, "Transcribes when you tap Assist", true
    public static let parakeet: TranscriptionModel       // "parakeet", "Live Parakeet", 230, "English live captions · 320 ms mode", true
    public static func onDevice(for provider: SpeechProvider) -> TranscriptionModel?
}

public struct PCModelStatus: Codable, Equatable, Sendable {
    public var state: String                             // stopped, starting, ready, stopping, error
    public var model: String?
    public var busy: Bool                                // required on decode
    public var elapsedSeconds: Int                       // required on decode; must be an integer
    public var message: String                           // required on decode
    public init(state: String, model: String? = nil, busy: Bool = false, elapsedSeconds: Int = 0, message: String)
    public var isChanging: Bool { get }
    public func isReady(for provider: SpeechProvider) -> Bool      // state "ready" and model == provider id; `busy` not considered
    public func homeLabel(for provider: SpeechProvider) -> String  // the seven rows of spec 02 section 8.2
    /// The loaded model's id when it is not `provider` and the state is not "stopped".
    public func otherLoadedModel(than provider: SpeechProvider) -> String?
}

public enum APIKeyShape: Equatable, Sendable {
    case valid, empty, containsWhitespace
    /// `empty` for "", `containsWhitespace` when any character is whitespace or a newline.
    public static func check(_ key: String) -> APIKeyShape
}

/// Always valid: it can only be made by `parse` or `manual`.
public struct PairingPayload: Equatable, Sendable {
    public let endpoint: String                          // canonical form (protocol 4.3)
    public let token: String
    /// Rules V-1 to V-4. Throws `PairingPayloadError.invalid`. V-4 is a whole-text check done by a
    /// character loop (exactly 43 characters of the token alphabet), never a pattern ending in `$`: a
    /// token followed by a line feed, a carriage return or any other character is invalid.
    public static func parse(_ text: String) throws -> PairingPayload
    /// Manual entry of protocol 4.3: trims the address, lower-cases a leading "https://" written in any
    /// case, applies V-3 and V-4. The token is used exactly as typed and is NOT trimmed: one that ends in
    /// a line break or a space fails V-4.
    public static func manual(address: String, token: String) throws -> PairingPayload
    /// `{"endpoint":"…","token":"…"}`: members in this order, no whitespace. The stored and QR form.
    public var storedText: String { get }
    public let relayEndpoint: RelayEndpoint              // parsed once; its `canonical` equals `endpoint`
}

public enum PairingPayloadError: LocalizedError, Equatable, Sendable {
    case invalid
    public var errorDescription: String? { get }         // PAIRING_PAYLOAD_INVALID text of protocol 14.2
}
```

Usage (spec 02 section 9, unchanged rules):

```swift
public struct SpeechStreamUsage: Codable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public var sentMs: Double
    public var processedMs: Double?
    public var hasTranscript: Bool
    public var state: String
    public static let streaming = "streaming"
    public static let completed = "completed"
    public static let interrupted = "interrupted"
    public init(id: UUID, sentMs: Double = 0, processedMs: Double? = nil, hasTranscript: Bool = false,
                state: String = SpeechStreamUsage.streaming)
}

public enum UsageUpdate: Equatable, Sendable {
    case begin(id: UUID)
    case update(id: UUID, sentMs: Double?, processedMs: Double?, hasTranscript: Bool)
    case finish(id: UUID, completed: Bool)
    case firstText(milliseconds: Double)        // applied once per session
    case finalization(milliseconds: Double)     // replaces the previous value
}

public struct TranscriptionUsage: Codable, Equatable, Sendable {
    public var provider: String
    public var hourlyRateUSD: Double
    public var pricingDate: String
    public var firstTextMs: Double?
    public var finalizationMs: Double?
    public private(set) var streams: [SpeechStreamUsage]
    /// Rate 0.18 for "meta", otherwise 0; pricing date "2026-09-28".
    public init(provider: String = "meta")
    public mutating func begin(_ id: UUID)
    public mutating func update(_ id: UUID, sentMs: Double? = nil, processedMs: Double? = nil, hasTranscript: Bool = false)
    public mutating func finish(_ id: UUID, completed: Bool)
    public mutating func apply(_ update: UsageUpdate)
    public mutating func interruptOpenStreams()
    public var estimatedSeconds: Int { get }
    public var estimatedUSD: Double { get }
    public var reportedSeconds: Int { get }
    public var isIncomplete: Bool { get }
    public var formattedCost: String { get }             // "$0.00155": five decimals, "." in every locale
}
```

As built by WP-02: `apply(_:)` ignores a `firstText` or `finalization` time that is negative, NaN or
infinite, as `update` ignores such a position (JSON has no such number, so the session could not be saved).
A stream whose stored `state` is none of the three is costed like a finished one. The derived seconds
saturate at `Int.max` instead of trapping on a stored value out of range.

#### 3.1.3 Coding

```swift
public enum LiveCueJSON {
    /// Dates as "2026-10-01T14:03:22.123Z" (always three fractional digits, always "Z").
    /// Output formatting: sorted keys, slashes not escaped, compact.
    public static func makeEncoder() -> JSONEncoder
    /// Dates with zero to nine fractional digits and "Z". Anything else fails decoding.
    public static func makeDecoder() -> JSONDecoder
    public static func encode<T: Encodable>(_ value: T) throws -> Data
    public static func decode<T: Decodable>(_ type: T.Type, from data: Data) throws -> T
    public static func string(from date: Date) -> String
    public static func date(from string: String) -> Date?
    public static func roundedToMilliseconds(_ date: Date) -> Date
}

/// Strict dynamic JSON. A JSON boolean is never a number and a number is never a boolean (CORE-A17).
public enum JSONValue: Codable, Equatable, Sendable {
    case null
    case bool(Bool)
    case number(Double)
    case string(String)
    case array([JSONValue])
    case object([String: JSONValue])
    public static func parse(_ data: Data) -> JSONValue?     // nil when the bytes are not JSON (see below)
    public static func parse(_ text: String) -> JSONValue?
    public subscript(key: String) -> JSONValue? { get }      // nil unless this is an object with that key
    public var stringValue: String? { get }
    public var numberValue: Double? { get }                  // finite numbers only
    public var intValue: Int? { get }                        // finite numbers with no fraction that fit in Int
    public var boolValue: Bool? { get }
    public var arrayValue: [JSONValue]? { get }
    public var objectValue: [String: JSONValue]? { get }
}
```

`JSONValue.parse` uses Foundation's parser, which is lenient in one way that was seen on CI: it accepts a
comma after the last member of an object or an array. Nothing relies on such text being rejected.

`ISO8601Milliseconds.swift` holds the two pure functions behind `string(from:)` and `date(from:)`. They use
integer arithmetic on the days-since-epoch count (the civil-date algorithm), not a formatter, so they behave
the same on Linux and need no shared mutable object. Encoders and decoders are created per call.

What the declarations leave open, as built by WP-02 and held by `LiveCueJSONTests`:

- `date(from:)` accepts exactly `YYYY-MM-DDTHH:MM:SS`, then nothing or a `.` with one to nine digits, then
  `Z` (capital `T` and `Z`), and only for a date that exists: no 30 February, no hour 24, no second 60.
- Digits beyond the third are rounded to the nearest millisecond, so every date that is read is written
  back unchanged.
- The text has a four-digit year. A date outside the years 0000 to 9999 is written, and rounded by
  `roundedToMilliseconds`, as the nearest end of that range.
- `Session.closedAfterInterruption(at:)` rounds its date as the initialisers do.

#### 3.1.4 Context builder and wire budget (protocol 13.4)

```swift
public enum ContextBuilder {
    /// Step 1 of protocol 13.4. `assistant` is left unset and `requestId` is fresh.
    /// Transcript: segments from two before the first one not covered by the memory marker, joined by "\n",
    /// tail of C-12. Partial: trimmed, tail of C-14, nil when empty. Instruction: trimmed, head of 4,000
    /// characters then head of 8,000 bytes, nil when empty. Memory: summary tail of C-16; each list keeps
    /// its last 16 items, each cut to its head of 256 bytes; the marker is passed through.
    public static func assistRequest(session: Session, partial: String?, instruction: String?) -> AssistRequest
    /// False when the transcript is empty and there is no partial ("No speech was recognized yet.").
    public static func hasContent(_ request: AssistRequest) -> Bool
    /// Every segment joined by "\n", tail of C-13, `transcriptTruncated` true exactly when cut; memory cut
    /// as above. Returns nil for a session without segments.
    public static func summaryRequest(session: Session, assistant: AssistantConfiguration) -> SummaryRequest?
    /// Same text, new `requestId`, the given assistant. The caller fits it again.
    public static func retry(_ previous: AssistRequest, assistant: AssistantConfiguration) -> AssistRequest
}

public enum WireBudget {
    /// Longest prefix of whole characters within the limit.
    public static func head(_ text: String, maxCharacters: Int) -> String
    public static func head(_ text: String, maxBytes: Int) -> String
    /// Longest suffix of whole characters within the limit.
    public static func tail(_ text: String, maxBytes: Int) -> String
    /// The body exactly as the relay client sends it.
    public static func encodedBody(_ request: AssistRequest) throws -> Data
    public static func encodedBody(_ request: SummaryRequest) throws -> Data
    /// Steps 2 and 3: returns the request unchanged when its encoded body is at most C-11 bytes; otherwise
    /// shrinks one member at a time in the order transcript, memory.summary, partialTranscript,
    /// memory.facts and memory.openQuestions (oldest items first), instruction. Sets `transcriptTruncated`
    /// on a summary whose transcript it cut. The result always fits.
    public static func fitted(_ request: AssistRequest) throws -> AssistRequest
    public static func fitted(_ request: SummaryRequest) throws -> SummaryRequest
}
```

"Character" is a Swift `Character`. Sizes are UTF-8 bytes of the raw string. `fitted` finds each cut by
binary search over the kept length, encoding the candidate body each time.

What the declarations leave open, as built by WP-02 and held by `WireBudgetTests`:

- A member that step 3 empties stays in the body as `""` or `[]`, except `partialTranscript` and
  `instruction`, which become absent as step 1 leaves them.
- The two lists are shrunk one after the other: the oldest facts go first, and an open question is dropped
  only when no fact is left. Items are dropped whole.
- `fitted` returns only a request that fits. It throws what the encoder throws, and
  `EncodingError.invalidValue` for a body that is over C-11 with all of those members empty. That needs an
  assistant selection of more than 100,000 bytes; a relay model id is at most 64 characters (C-06).

#### 3.1.5 Relay: constants, endpoint, failures, messages, client

```swift
public enum RelayConstants {
    public static let protocolVersion = 2                    // C-01
    public static let tokenLength = 43                       // C-02: [A-Za-z0-9_-]
    public static let maximumEncodedBodyBytes = 126_976      // C-11
    public static let assistTranscriptBytes = 80_000         // C-12
    public static let summaryTranscriptBytes = 100_000       // C-13
    public static let partialTranscriptBytes = 16_000        // C-14
    public static let instructionCharacters = 4_000          // C-15
    public static let instructionBytes = 8_000               // C-15
    public static let memorySummaryBytes = 24_000            // C-16
    public static let memoryListItems = 16                   // C-17
    public static let memoryListItemBytes = 256              // C-17
    public static let pairingPayloadBytes = 8_192            // C-18
    public static let maximumInboundSpeechMessageBytes = 131_072   // C-21
    public static let speechConnectSeconds: TimeInterval = 180     // C-59
    public static let speechFinalizeSeconds: TimeInterval = 15     // C-60
    public static let healthPollSeconds: TimeInterval = 5          // C-61
    public static let pcModelPollSeconds: TimeInterval = 2         // C-61
    public static let speechSampleRate = 16_000              // C-70
    public static let speechFrameBytes = 2_560               // C-71
    public static let unknownCodeTextMaximumCharacters = 300 // protocol 14
    public static let speechModels: [String] = ["nemotron", "qwen3"]   // C-04
}

public enum RelayRoute: Equatable, Sendable {
    case health, pairVerify, models, localModel, localModelStart, localModelStop
    case assist, sessionSummary, cancel(requestId: String), speech
    public var method: String { get }                    // "GET", "POST", "DELETE"
    public var path: String { get }                      // with a leading slash, exactly as protocol 6.1
    public var hasBody: Bool { get }
    public var phoneTimeout: TimeInterval { get }        // protocol 6.1: 8, 15, 45, 8, 15, 15, 70, 70, 8; speech: C-59
}

public struct RelayEndpoint: Equatable, Sendable {
    /// "https://" + lower-case host + optional ":port" + path ending in exactly one "/".
    public let canonical: String
    /// The lower-case host of `canonical`, without scheme, port or path. Added by the review for the
    /// "replace this PC?" confirmation (5.9).
    public var host: String { get }
    /// The grammar of protocol 4.3, parsed by hand. No trimming and no case change of the scheme.
    public init?(parsing text: String)
    /// `canonical` + the route path without its leading slash. Text concatenation only.
    public func url(for route: RelayRoute) -> URL?
    /// The speech URL: as above with the scheme "wss".
    public func speechURL() -> URL?
}

public struct RelayHealth: Equatable, Sendable {
    public var relayVersion: String
    public init(relayVersion: String)
}

public enum RelayWireCode: String, CaseIterable, Sendable {
    case unauthorized = "UNAUTHORIZED", notFound = "NOT_FOUND", protocolMismatch = "PROTOCOL_MISMATCH"
    case invalidRequest = "INVALID_REQUEST", invalidJSON = "INVALID_JSON", invalidRequestID = "INVALID_REQUEST_ID"
    case invalidAssistant = "INVALID_ASSISTANT", payloadTooLarge = "PAYLOAD_TOO_LARGE", requestTimeout = "REQUEST_TIMEOUT"
    case tooManyRequests = "TOO_MANY_REQUESTS", deadlineExceeded = "DEADLINE_EXCEEDED", internalError = "INTERNAL_ERROR"
    case relayPaused = "RELAY_PAUSED", assistantBusy = "ASSISTANT_BUSY"
    case assistantCatalogUnavailable = "ASSISTANT_CATALOG_UNAVAILABLE"
    case assistantSelectionUnavailable = "ASSISTANT_SELECTION_UNAVAILABLE"
    case assistantFailed = "ASSISTANT_FAILED", assistantTimeout = "ASSISTANT_TIMEOUT"
    case requestCancelled = "REQUEST_CANCELLED", requestNotFound = "REQUEST_NOT_FOUND"
    case invalidModel = "INVALID_MODEL", labNotConfigured = "LAB_NOT_CONFIGURED"
    case modelOperationInProgress = "MODEL_OPERATION_IN_PROGRESS", modelBusy = "MODEL_BUSY", modelMismatch = "MODEL_MISMATCH"
    case invalidSpeechModel = "INVALID_SPEECH_MODEL", speechBusy = "SPEECH_BUSY"
    case modelLoadFailed = "MODEL_LOAD_FAILED", modelLoadTimeout = "MODEL_LOAD_TIMEOUT", modelNotReady = "MODEL_NOT_READY"
    case labConnectionFailed = "LAB_CONNECTION_FAILED", labInferenceFailed = "LAB_INFERENCE_FAILED"
    case labProtocolError = "LAB_PROTOCOL_ERROR", labDisconnected = "LAB_DISCONNECTED"
    case audioOutOfSequence = "AUDIO_OUT_OF_SEQUENCE", audioLimit = "AUDIO_LIMIT", frameTooLarge = "FRAME_TOO_LARGE"
    case unsupportedCommand = "UNSUPPORTED_COMMAND", audioIdleTimeout = "AUDIO_IDLE_TIMEOUT"
    case finalizeTimeout = "FINALIZE_TIMEOUT", sessionLimit = "SESSION_LIMIT"
    case pairingChanged = "PAIRING_CHANGED", relayStopping = "RELAY_STOPPING"
}

public enum RelayLocalCondition: String, CaseIterable, Sendable {
    case tls = "L-TLS", unreachable = "L-UNREACHABLE", network = "L-NETWORK", timeout = "L-TIMEOUT"
    case noRelay = "L-NO-RELAY", http = "L-HTTP", unreadable = "L-UNREADABLE", badEndpoint = "L-BAD-ENDPOINT"
    case notPaired = "L-NOT-PAIRED", notConfirmed = "L-NOT-CONFIRMED"
    case speechNotConfirmed = "L-SPEECH-NOT-CONFIRMED", unexpectedFrame = "L-UNEXPECTED-FRAME"
    case streamDropped = "L-STREAM-DROPPED"
}

/// The one error type of every relay call and of the relay speech stream.
public struct RelayFailure: Error, Equatable, Sendable {
    public enum Kind: Equatable, Sendable {
        case wire(String)                    // a code exactly as received; it may be one this build does not know
        case local(RelayLocalCondition)
    }
    public enum Origin: Equatable, Sendable { case http, stream }
    public var kind: Kind
    public var origin: Origin
    public var httpStatus: Int?              // set for HTTP outcomes; shown by L-HTTP
    public var relayText: String?            // the relay's `error` or `message`; used only for an unknown code
    public init(kind: Kind, origin: Origin, httpStatus: Int? = nil, relayText: String? = nil)
    public static func local(_ condition: RelayLocalCondition, origin: Origin = .http, httpStatus: Int? = nil) -> RelayFailure
    public static func wire(_ code: RelayWireCode, origin: Origin = .http) -> RelayFailure
    public var wireCode: RelayWireCode? { get }          // nil for local conditions and unknown codes
    public func isCode(_ code: RelayWireCode) -> Bool
    public func isLocal(_ condition: RelayLocalCondition) -> Bool
}

public enum RelayMessages {
    /// Protocol 14.1 and 14.2. A known code or condition gives its phone text. An unknown code gives
    /// `relayText` when it is 1 to 300 characters, otherwise the L-HTTP text (origin http) or the
    /// L-STREAM-DROPPED text (origin stream). No result ever contains a token.
    public static func text(for failure: RelayFailure) -> String
    public static func text(for code: RelayWireCode) -> String
    public static func text(for condition: RelayLocalCondition, httpStatus: Int? = nil) -> String
    // Protocol 14.3 compositions.
    public static func startFailure(_ message: String) -> String              // "Transcription could not start. " + message
    /// "Your pairing is saved. " + text for L-TLS, L-UNREACHABLE, L-NETWORK, L-TIMEOUT, L-NO-RELAY; the bare text otherwise.
    public static func savedPairingHealthFailure(_ failure: RelayFailure) -> String
    public static func catalogueFailure(_ failure: RelayFailure) -> String    // "Could not load models. Restart the updated PC app and check the connection. " + text
    public static func pcModelCheckFailure(_ failure: RelayFailure) -> String // "Could not check the PC model. Keep the updated LiveCue Desktop running. " + text
}

/// Protocol 13. A value type with no state: safe to copy and to call from any task.
/// Every method throws `RelayFailure`, or `CancellationError` when the calling task was cancelled
/// (protocol 13.6 row 1). No retries. Every request carries the headers of protocol 13.2.
public struct RelayClient: Sendable {
    public init(transport: any HTTPTransport, timing: CoreTiming = .system)
    /// Returns only when the relay is online: 200, `ok` true, `protocol` 2. A 200 with another or no
    /// `protocol` throws PROTOCOL_MISMATCH; `ok` not true throws L-UNREADABLE.
    public func health(_ pairing: PairingPayload) async throws -> RelayHealth
    public func verifyPairing(_ pairing: PairingPayload) async throws                    // body "{}"
    public func models(_ pairing: PairingPayload) async throws -> [AssistantModelOption]
    public func localModelStatus(_ pairing: PairingPayload) async throws -> PCModelStatus
    /// `provider` must be PC-local. Any 2xx is success.
    public func startLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus
    public func stopLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus
    /// Requires `request.assistant`. Fits the body (3.1.4) before sending. The reply's `execution` must be
    /// present and echo the selection, else L-NOT-CONFIRMED. The returned `memory.throughSegmentId` is
    /// always nil: the reply's value is never read (protocol 13.5).
    public func assist(_ request: AssistRequest, pairing: PairingPayload) async throws -> AssistResponse
    public func summarize(_ request: SummaryRequest, pairing: PairingPayload) async throws -> SummaryResponse
    /// Route 9, best effort. Never throws; both answers count as done. The id in the path is
    /// `requestId.uuidString`, the same upper-case text the encoder wrote in the body: the relay compares
    /// ids byte for byte.
    public func cancel(requestId: UUID, pairing: PairingPayload) async
}
```

Rules inside `RelayClient` that callers rely on:

- The route's phone timeout is a total time, raced against `timing.sleep`. When it wins, the outcome is
  `L-TIMEOUT` (row 3), also if the transport then reports cancellation.
- Every relay request is built with `HTTPRequest.timeout` (the transport's idle timeout) equal to the
  route's phone timeout plus 10 s, so the transport's own timer can never end a request before the total
  timer does. A slow route always ends as `L-TIMEOUT`, never as a transport time-out (row 5).
- The assist reply is never decoded with the synthesised decoder of `AssistResponse` directly: its `memory`
  is a `SessionMemory`, whose marker must be a UUID. The relay always sends `memory.throughSegmentId` as
  `null` (protocol 8.3), but the phone does not rely on that: it tolerates and ignores any string there.
  `RelayClient` parses the reply with `JSONValue.parse`, removes the member `memory.throughSegmentId` when
  `memory` is an object, encodes that value again and decodes `AssistResponse` from the result. A reply
  that fails this is `L-UNREADABLE` (protocol 13.6 row 8).
- For `assist` and `summarize`: when the phone timeout wins, or when the calling task is cancelled while the
  request is in flight, the client starts **one** unstructured task that sends `cancel(requestId:)` and does
  not wait for it. Nothing else in the app sends route 9. Deleting a session whose summary is in flight
  cancels the summary's task, which produces that one `DELETE` (P2-212).
- `RelayOutcome.swift` (internal) implements protocol 13.6 as one function from
  `(route, Result<(HTTPResponseHead, Data), TransportFailure>)` to success bytes or a `RelayFailure`:
  `RelayOutcome.classify(route:result:) -> Result<Data, RelayFailure>`.

#### 3.1.6 Relay speech frames and negotiation (protocol 10.3, 13.7)

```swift
public enum RelaySpeechFrame: Equatable, Sendable {
    case loading(provider: String, elapsedSeconds: Int, message: String)
    case ready(provider: String, sampleRate: Int, handshakeMs: Double)
    case speechStart(turnId: Int?, audioProcessedMs: Double?)
    case transcript(turnId: Int?, audioProcessedMs: Double?, transcript: String?)
    case speechComplete(turnId: Int?, audioProcessedMs: Double?, transcript: String?)
    case streamComplete(audioProcessedMs: Double?, finalizationMs: Double?)
    case error(code: String, message: String)
    case unknown                                         // a JSON object of another type: ignored
    /// nil when the text is not a JSON object with a string `type`. Members of the wrong JSON type are
    /// read as absent. `ready` and `error` with a missing member parse as `.unknown`. `loading` with a
    /// missing member is still `loading`, with 0 or "" in its place.
    public static func parse(_ text: String) -> RelaySpeechFrame?
}

/// Steps 1, 2, 4, 6 and 7 of protocol 13.7 without any I/O.
public struct RelaySpeechNegotiation: Sendable {
    public enum Step: Equatable, Sendable {
        case loading(elapsedSeconds: Int)
        case ready(handshakeMs: Double)                  // open the microphone now
        case event(StreamTranscriptEvent, audioProcessedMs: Double?, hasText: Bool)
        case streamComplete(finalizationMs: Double?)
        case ignore
    }
    public enum Ending: Equatable, Sendable {
        case completed                                   // endStream sent, close code 1000, inside C-60
        case interrupted(RelayFailure)
    }
    public init(model: SpeechProvider)
    public private(set) var isReady: Bool
    public private(set) var endStreamSent: Bool
    /// Throws `RelayFailure`: an `error` frame's code; L-SPEECH-NOT-CONFIRMED for a `ready` naming another
    /// provider or sample rate; L-UNEXPECTED-FRAME for `speechStart`, `transcript`, `speechComplete` or
    /// `streamComplete` before `ready`, and for text that is not a frame. `.unknown` is `Step.ignore` in
    /// every state.
    public mutating func receive(text: String) throws -> Step
    public mutating func markEndStreamSent()
    /// How the stream ended, given the close. `insideWindow` is false when C-60 passed first.
    public func ending(closeCode: Int, closeReason: String, insideWindow: Bool) -> Ending
    /// The ending when the socket dropped with no close frame.
    public func endingAfterDrop() -> Ending
    public static func failure(forUpgradeStatus status: Int) -> RelayFailure   // 401, 404, 429, 502 to 504, other
    public static func failure(for transport: TransportFailure) -> RelayFailure // rows 4 to 6 of protocol 13.6
}
```

As built by WP-04 and held by `RelaySpeechNegotiationTests`: text that is not a frame throws
`L-UNEXPECTED-FRAME` in every state, as the comment of `receive(text:)` reads. After `ready`, a `loading`
and a second `ready` are `Step.ignore`. An `error` frame's `message` is kept as `relayText` only for a code
this build does not know, so a known code equals `RelayFailure.wire(_:origin: .stream)`. `ending` with
`insideWindow` false is `L-STREAM-DROPPED` whatever the close code and reason. A refused upgrade carries
`httpStatus` only when it is `L-HTTP`. `failure(for: .cancelled)` is `L-NETWORK`; the session never asks,
because it turns a cancelled transport into `CancellationError` first (protocol 13.6 row 1).

#### 3.1.7 Caption merge, framing, pending audio

```swift
public enum StreamTranscriptEvent: Equatable, Sendable {
    case speechStart(turn: Int?, processedMs: Double)
    case transcript(turn: Int?, text: String?, processedMs: Double)
    case speechComplete(turn: Int?, text: String?, processedMs: Double)
    case other                                           // speechEnd, audioProgress, streamComplete, unknown
}

/// Spec 02 section 6 with CORE-A7 and CORE-A8. A fresh value is made for every stream.
public struct StreamTranscript: Sendable {
    public init()
    /// Non-empty partials of unfinished turns, ascending turn number, joined by one space.
    public var partialText: String { get }
    /// `transcript` updates the turn its `turn` names when that turn is open (started and not completed),
    /// otherwise the active turn when it is open, otherwise nothing. `speechComplete` for a turn not yet
    /// completed marks it completed and returns a segment when the trimmed text is not empty
    /// (start = the turn's recorded start or 0, end = processedMs / 1000).
    public mutating func apply(_ event: StreamTranscriptEvent) -> TranscriptSegment?
}

/// Cuts a byte stream into fixed frames; the remainder is flushed once at the end.
public struct PCMFramer: Sendable {
    public init(frameBytes: Int)                         // 3,840 for Meta, 2,560 for PC speech
    public mutating func append(_ bytes: Data) -> [Data] // zero or more full frames
    /// The remainder, or nil when empty. An odd trailing byte is dropped, so the frame is even and not empty.
    public mutating func flush() -> Data?
}

/// Voz pending audio (spec 01 section 3.3): 16 kHz mono samples, 30-minute cap, 2-second overlap.
public struct PendingAudioBuffer: Sendable {
    public static let sampleRate = 16_000
    public static let capacitySamples = 28_800_000       // 30 minutes
    public static let overlapSamples = 32_000            // 2 seconds
    public struct Snapshot: Sendable {
        public let samples: [Float]
        public let cursorSeconds: Double                 // words ending at or before this are not new
    }
    public struct TimedWord: Equatable, Sendable {
        public var text: String
        public var end: Double
        public init(text: String, end: Double)
    }
    public init()
    public var sampleCount: Int { get }
    public var hasUntranscribedAudio: Bool { get }
    /// False, appending nothing, when the chunk would pass the cap.
    public mutating func append(_ chunk: [Float]) -> Bool
    public func snapshot() -> Snapshot?                  // nil when there is nothing new
    /// Keeps the words whose end is after the snapshot's cursor, joins them with spaces and trims; then
    /// trims the buffer to the snapshot's last 2 seconds (keeping samples appended since) and moves the cursor.
    public mutating func complete(_ snapshot: Snapshot, words: [TimedWord]) -> String
    public mutating func reset()
}
```

As built by WP-04: a `speechStart` for a turn records its start again, and never reopens a completed turn.
`PendingAudioBuffer.sampleCount` and the cap count the kept overlap too. `complete` returns "" and changes
nothing for a snapshot taken before a `reset()` or already completed once. A snapshot shorter than the
overlap is kept whole, and the cursor is then its end.

#### 3.1.8 Meta realtime (spec 02 section 5; contract unchanged)

```swift
public enum MetaSpeechError: LocalizedError, Equatable, Sendable {
    case missingKey, malformedKey, authentication, provider, connection
    public var errorDescription: String? { get }         // spec 02 section 5.6 with the changes of 1.3
}

public struct MetaEvent: Equatable, Sendable {
    public var type: String                              // "" for an event to ignore
    public var turnId: Int?
    public var audioProcessedMs: Double?
    public var isFinal: Bool?                            // the wire member `final`
    public var transcript: String?
    public init(type: String, turnId: Int? = nil, audioProcessedMs: Double? = nil, isFinal: Bool? = nil, transcript: String? = nil)
    public var isIgnored: Bool { get }
    public var transcriptEvent: StreamTranscriptEvent { get }   // processedMs 0 when absent
}

public enum MetaRealtimeAPI {
    public static let endpoint: URL                      // wss://api.meta.ai/v1/asr/realtime
    public static let sampleRate = 24_000
    public static let frameBytes = 3_840
    public static let connectSeconds: TimeInterval = 30
    public static let testSeconds: TimeInterval = 15
    public static let finalizeSeconds: TimeInterval = 15
    public static let maximumMessageBytes = 131_072
    public static let endStreamMessage = "{\"type\":\"endStream\"}"
    /// The six-member first message. Throws `.missingKey` for "" and `.malformedKey` for whitespace.
    public static func handshake(key: String) throws -> String
    /// Accepts only a JSON object with no `type` member and a non-empty string `sessionId`; else `.authentication`.
    public static func acknowledge(_ firstMessage: String) throws
    /// The sanitiser: throws `.provider` for type "error"; returns an ignored event for any type outside
    /// the five allowed; copies only `turnId` and `audioProcessedMs` (numbers, never booleans), `final`
    /// (boolean) and `transcript` (key replaced by "[redacted]", then the first 32,000 characters).
    public static func event(_ message: String, key: String) throws -> MetaEvent
}

public enum MetaConnectionTest {
    /// Connect, handshake, acknowledgement, endStream, close; no audio; bounded by `testSeconds`.
    /// Throws `MetaSpeechError`, or `CancellationError`.
    public static func run(key: String, transport: any WebSocketTransport, timing: CoreTiming = .system) async throws
}
```

As built by WP-04: `MetaConnectionTest.run` does not wait for Meta's close after `endStream`; it closes the
socket itself. A refused upgrade, a send failure, and a close or a drop before the acknowledgement are
`connection`; a first message that is not text is `authentication`. The same holds for the start of a Meta
`SpeechStreamSession`.

#### 3.1.9 Streaming speech session (Meta and relay)

One actor carries a whole stream, so the microphone rule, framing, usage and the completed rule are tested
on Linux with a fake socket (P2-213 to P2-218, AC-IOS-020, AC-IOS-047).

```swift
public enum SpeechStreamDialect: Equatable, Sendable {
    case meta(key: String)
    case relay(pairing: PairingPayload, model: SpeechProvider)
}

public enum SpeechStreamStatus: Equatable, Sendable {
    case connecting
    case loading(elapsedSeconds: Int)                    // relay only
    case connected(setupSeconds: Double)
    case firstWords(seconds: Double)                     // from stream start
    case finalized(seconds: Double)
}

public struct SpeechStreamFailure: Error, Equatable, Sendable {
    public enum Source: Equatable, Sendable {
        case relay(RelayFailure)
        case meta(MetaSpeechError)
        case sendFailed                                  // "Audio connection failed. Resume to reconnect."
    }
    public var source: Source
    public init(_ source: Source)
    public var message: String { get }                   // the user text for the source
}

public enum SpeechStreamEvent: Equatable, Sendable {
    case status(SpeechStreamStatus)
    case partial(String)
    case segment(TranscriptSegment)                      // times relative to this stream
    case usage(UsageUpdate)
    case failed(SpeechStreamFailure)                     // only after a successful start; the socket is already closed
}

public enum SpeechStreamEnding: Equatable, Sendable { case completed, interrupted }

public actor SpeechStreamSession {
    /// Finishes when the stream has ended, after the last event.
    public nonisolated let events: AsyncStream<SpeechStreamEvent>
    public init(dialect: SpeechStreamDialect, transport: any WebSocketTransport, timing: CoreTiming = .system)
    /// Opens the socket and returns when the provider is ready for audio: the Meta acknowledgement
    /// (limit 30 s) or the relay's valid `ready` (limit C-59). Emits `.status` while waiting, then
    /// `.status(.connected)` and `.usage(.begin)`. For the relay dialect, when C-59 passes first the socket
    /// is closed and the failure is `.relay(.local(.timeout, origin: .stream))`. Throws `SpeechStreamFailure` or `CancellationError`;
    /// after a throw the socket is closed and `events` has finished.
    public func start() async throws
    /// PCM16 mono at the dialect's rate, any length. Frames it and sends it. Ignored before `start`
    /// returned and after `finish` began. A send error ends the stream with `.failed(.sendFailed)`.
    public func send(audio: Data) async
    /// Flushes the remainder as one shorter frame, sends `endStream` once, then waits up to 15 s for the
    /// close. Returns after `events` has finished. `completed` only for close code 1000 inside the limit.
    public func finish() async -> SpeechStreamEnding
    /// Closes now. Idempotent. Marks the usage stream interrupted if it was open.
    public func abort() async
}
```

Usage emitted by the session: `begin` when ready; `update(sentMs:)` after each frame sent
(`bytes / (sampleRate × 2 / 1000)`); `update(processedMs:hasTranscript:)` for each inbound event;
`firstText` at the first non-empty partial; `finalization` at the relay's `streamComplete` or at Meta's
normal close; `finish(completed:)` exactly once.

What the declarations leave open, as built by WP-04 and held by the two `SpeechStreamSession…Tests` files:

- A stream that ends interrupted delivers `.failed` as its last event, also when that happens inside
  `finish()`: a close code other than 1000, a drop, or the 15 s passing (`L-STREAM-DROPPED` for the relay,
  `MetaSpeechError.connection` for Meta). Only `abort()` ends a stream without `.failed`. The order at the
  end is: socket closed, `usage(.finish)`, `.failed`, end of `events`.
- `start()` throws `CancellationError` when its task is cancelled and when `abort()` or `finish()` is called
  while it waits. A second `start()` ends the stream and throws.
- The 15 s of `finish()` count from the call, so they bound the flush and `endStream` as well as the wait
  for the close (AC-IOS-047).
- `connected(setupSeconds:)` is the relay's `handshakeMs`, in seconds, for the relay dialect and the phone's
  own time from the connection attempt to the acknowledgement for Meta. `firstWords` and `firstText` count from the
  moment the provider was ready, and fire at the first non-empty partial or, when a turn completes without
  one, at its segment. `finalization` is the relay's `finalizationMs` when the frame has one, otherwise the
  time since `finish()` was called; for Meta it is always the latter.
- The `update` for an inbound event is sent also when the event has neither a position nor text. `.partial`
  is sent only when the text changed, and a segment before the partial that no longer holds it.
- A binary inbound message is ignored, except as Meta's first message, where it fails the acknowledgement.
- The phone closes with code 1000 when the peer ended the stream (a close frame, or a relay `error` frame)
  and with 1001 when it cuts the stream itself (a limit, a local failure, `abort()`).
- The session ends every wait by closing the socket. A transport whose `receive()` ends when `close` is
  called, and whose `open` ends when its task is cancelled, leaves no task behind; nothing else depends on
  either.

#### 3.1.10 OpenRouter (spec 02 section 4; contract unchanged)

```swift
public enum OpenRouterError: LocalizedError, Equatable, Sendable {
    case missingKey, malformedKey, invalidKey, credits, rateLimited, unavailable, invalidResponse, truncated, noModel
    public var errorDescription: String? { get }         // spec 02 section 4.7 with the changes of 1.3
}

/// Every method throws `OpenRouterError`, or `CancellationError` when the calling task was cancelled
/// (CORE-A11). One request per call, no retry, no fallback.
public struct OpenRouterClient: Sendable {
    public static let baseURL = "https://openrouter.ai/api/v1/"
    public static let keyCheckSeconds: TimeInterval = 20
    public static let catalogueSeconds: TimeInterval = 20
    public static let assistIdleSeconds: TimeInterval = 65
    public static let assistTotalSeconds: TimeInterval = 120
    public init(transport: any HTTPTransport, timing: CoreTiming = .system)
    public func verifyKey(_ key: String) async throws
    /// No `Authorization` header. A malformed entry is skipped (CORE-A5); only a missing or non-array
    /// `data` fails. Sorted by lower-cased name, then by id, without locale rules (CORE-B4).
    public func models() async throws -> [AssistantModelOption]
    /// Check order: empty `option.id` gives `.noModel`; the body is built; the key is checked; the request
    /// is sent. The embedded context JSON has no `instruction` member (CORE-A4). `onUpdate` receives the
    /// whole answer so far and is awaited.
    public func assist(_ request: AssistRequest, option: AssistantModelOption, key: String,
                       throughSegmentId: UUID?,
                       onUpdate: (@Sendable (String) async -> Void)? = nil) async throws -> AssistResponse
}

// Internal; reached by tests with `@testable import LiveCueCore`.
struct OpenRouterStreamResult: Equatable, Sendable {
    var id: String?
    var model: String?
    var text: String
    var usage: AssistantUsage?
}

struct OpenRouterStreamParser: Sendable {
    init()
    private(set) var text: String                        // readable after a failure, for the preview
    private(set) var done: Bool
    /// One line without its terminator. True when the answer text grew. Throws `OpenRouterError`.
    mutating func consumeLine(_ line: String) throws -> Bool
    /// Succeeds only when done is set, the finish reason is "stop" and the trimmed text is not empty.
    func result() throws -> OpenRouterStreamResult
    /// The whole-body form.
    static func completeJSON(_ data: Data) throws -> OpenRouterStreamResult
}

enum OpenRouterCatalogue {
    static func options(from body: Data) throws -> [AssistantModelOption]
}
```

#### 3.1.11 Word error rate

```swift
public enum WordErrorRate {
    /// Spec 02 section 3.4. Can exceed 1.
    public static func compute(reference: String, hypothesis: String) -> Double
}
```

#### 3.1.12 Core test fixtures (WP-01)

```swift
actor FakeHTTPTransport: HTTPTransport {
    enum Reply: Sendable {
        case response(status: Int, headers: [String: String], body: Data)
        case failure(TransportFailure)
        case never                                       // suspends until the task is cancelled, then throws .cancelled
    }
    enum StreamEnding: Sendable {
        case finish                                      // the body ends normally
        case fail(TransportFailure)                      // the body breaks off with this failure after the last chunk
        case stall                                       // the body never ends: the reader waits until its task is cancelled
    }
    init(replies: [Reply] = [])
    func enqueue(_ reply: Reply)
    func setDefault(_ reply: Reply)                      // used when the queue is empty; without one a request fails with .other
    var requests: [HTTPRequest] { get }
    func waitForRequests(count: Int) async               // returns when at least `count` were received
    // HTTPTransport: `stream` delivers a plain reply's body as one chunk, or the chunks given to
    // `enqueueChunks`; `send` returns those chunks joined.
    func enqueueChunks(status: Int, headers: [String: String], chunks: [Data], ending: StreamEnding = .finish)
}

/// One transport stands for one connection. After a `.closed` message was received, and after the client
/// called `close`, `receive()` throws TransportFailure.other, and so do sends. A `receive()` whose task is
/// cancelled throws `.cancelled`.
actor FakeWebSocketTransport: WebSocketTransport {
    enum OpenResult: Sendable { case connection, rejected(status: Int), failure(TransportFailure), never }
    init(open: OpenResult = .connection)
    var openedURL: URL? { get }
    var openedHeaders: [String: String] { get }
    var openedMaximumMessageSize: Int? { get }
    var sentTexts: [String] { get }
    var sentFrames: [Data] { get }
    var closeCalls: [Int] { get }                        // the code of every `close`, in order
    func deliver(_ message: WebSocketMessage)            // queued; the next `receive()` returns it
    func drop()                                          // the next `receive()` throws TransportFailure.other
    func failSends()                                     // later sends throw TransportFailure.other
    func waitForOpen() async
    func waitForSentTexts(count: Int) async
    func waitForSentFrames(count: Int) async
    func waitForCloseCalls(count: Int = 1) async
    func waitForReceive() async                          // a `receive()` is waiting with nothing left in the queue
}

struct ManualTime: Sendable {                            // Support/ManualTime.swift
    init()
    var timing: CoreTiming { get }                       // a sleep of zero or less returns at once; a cancelled sleep throws CancellationError
    var now: TimeInterval { get }                        // seconds advanced so far
    var sleeperCount: Int { get }
    func advance(by seconds: TimeInterval) async         // wakes sleepers whose time has come, in order
    func waitForSleepers(count: Int = 1) async           // call it before `advance` when the sleep starts on another task
}

enum CoreFixtures {
    static let token: String                             // the synthetic token of protocol 4.2
    static func pairing(_ endpoint: String = "https://pc.example.test:11443/") -> PairingPayload
    static func session(segments: [String]) -> Session   // segment i spans i to i+1 seconds
    static let assistReplyJSON: String                   // protocol 8.3 with model "gpt-test", effort "low"
    static let summaryReplyJSON: String
    static let catalogueJSON: String                     // {"models":[{"id":"gpt-test","name":"GPT Test","reasoningEfforts":["low"]}]}
}
```

A core test never waits by sleeping or by yielding in a loop. It waits with one of the `waitFor…` helpers,
which is what keeps the tests steady on the Linux thread pool. `ContractSmokeTests` exercises every helper
above.

### 3.2 App target

Everything in 3.2 is `internal` (no access modifier). Files import `LiveCueCore` where they use its types.

#### 3.2.1 Platform protocols and adapters

```swift
// Platform/AppClock.swift
protocol AppClock: Sendable {
    func now() -> Date                                   // rounded to whole milliseconds
    func uptime() -> TimeInterval                        // monotonic seconds
    func sleep(seconds: TimeInterval) async throws       // CancellationError
}
struct SystemClock: AppClock { init() }
extension AppClock { var coreTiming: CoreTiming { get } }   // the same clock for core clients

// Platform/PermissionProviding.swift
protocol PermissionProviding: Sendable {
    func requestMicrophone() async -> Bool               // prompts on first use; false when denied
    func requestCamera() async -> Bool
}

// Platform/ClipboardWriting.swift
protocol ClipboardWriting: Sendable { @MainActor func copy(_ text: String) }
struct SystemClipboard: ClipboardWriting { init() }      // UIPasteboard.general

// Platform/QRScanFailure.swift
enum QRScanFailure: Error, Equatable, Sendable {
    case cameraDenied, cameraUnavailable, scannerUnavailable
    var message: String { get }                          // spec 01 section 8.1, with the denied text of 1.3
}

// Platform/AppVersion.swift
enum AppVersion { static var marketing: String { get } } // CFBundleShortVersionString, or "Unknown"
```

Adapters (WP-06, except `KeychainStore`, which WP-01 completes so the spike's Keychain proof keeps running).
Each is the only place its Apple API is called.

```swift
// Platform/URLSessionHTTPTransport.swift
// Ephemeral configuration, no cookie storage, no cache, a task delegate that answers every redirect with nil.
// `stream` uses `URLSession.bytes(for:)` (research 7.1: absent on Linux, which is why it lives here) and
// yields a chunk at every line feed and at every 4,096 bytes.
struct URLSessionHTTPTransport: HTTPTransport {
    init(resourceTimeout: TimeInterval? = nil)
    func send(_ request: HTTPRequest) async throws -> (HTTPResponseHead, Data)
    func stream(_ request: HTTPRequest) async throws -> (HTTPResponseHead, AsyncThrowingStream<Data, Error>)
}

// Platform/URLSessionWebSocketTransport.swift
// One ephemeral URLSession per connection, invalidated on close. The upgrade request keeps an idle timeout
// of 60 s or more (protocol 13.1). `open` resumes the task and waits for
// `urlSession(_:webSocketTask:didOpenWithProtocol:)`; if the task completes first, an HTTP response on
// the task means `WebSocketUpgradeRejected(status:)`, anything else a `TransportFailure`.
// `receive()` returns `.closed(code:reason:)` once when the task reports a close code; a failure without
// one is thrown as `TransportFailure`.
struct URLSessionWebSocketTransport: WebSocketTransport {
    init()
    func open(url: URL, headers: [String: String], maximumMessageSize: Int) async throws -> any WebSocketConnection
}

// Platform/URLErrorMapping.swift
enum URLErrorMapping {
    /// cancelled and CancellationError: .cancelled.  timedOut: .timedOut.
    /// secureConnectionFailed and every serverCertificate and clientCertificate code: .tls.
    /// notConnectedToInternet, networkConnectionLost, cannotConnectToHost, cannotFindHost, dnsLookupFailed: .unreachable.
    /// Everything else: .other.
    static func transportFailure(for error: any Error) -> TransportFailure
}

// Platform/KeychainStore.swift
// Generic password, accessible after first unlock, this device only, no access group, no synchronisation
// (research 5.3). `set` updates an existing item and adds it when absent.
struct KeychainFailure: Error, Equatable, Sendable { let status: Int32 }
struct KeychainStore: Sendable {
    let service: String
    init(service: String = StorageNames.keychainService)
    func set(_ value: String, account: String) throws    // KeychainFailure
    func get(account: String) -> String?
    func delete(account: String) throws                  // KeychainFailure; "not found" is success
}

// Platform/SystemPermissions.swift
// `AVAudioApplication.requestRecordPermission()` and `AVCaptureDevice.requestAccess(for: .video)`.
struct SystemPermissions: PermissionProviding { init() }

// Platform/QRScannerView.swift
// `AVCaptureSession` with one `AVCaptureMetadataOutput` limited to `.qr`, delegate queue `.main`, an
// aspect-fill preview layer on a black view; `startRunning` and `stopRunning` on a private serial queue.
// The first decoded string ends the scan. (Not covered by research; these are the calls of the old app,
// which built with this SDK.)
struct QRScannerView: UIViewControllerRepresentable {
    init(permissions: any PermissionProviding, onResult: @escaping @MainActor (Result<String, QRScanFailure>) -> Void)
}

// Platform/BackupExclusion.swift
enum BackupExclusion { static func exclude(_ url: URL) } // sets isExcludedFromBackup; failures are ignored
```

What the declarations leave open, as built by WP-06. No declaration changes; the members named here are
internal additions that the unit tests use.

- `URLSessionHTTPTransport` keeps one session for its lifetime, so requests to one host share a connection.
  The session's own idle timeout is 300 s, so that only the timeout a request brings can end it. When a
  body breaks off, the bytes that arrived are yielded before the failure. When the reader's task is
  cancelled the body stream ends without an error, as `AsyncThrowingStream` does, and the request is
  cancelled; `send` and the start of `stream` throw `.cancelled`. A second initialiser,
  `init(resourceTimeout:protocolClasses:)`, lets a test answer requests with a URL protocol.
- `URLSessionWebSocketTransport`: the upgrade request's idle timeout is 60 s. A `receive()` whose task is
  cancelled throws `.cancelled` at once and loses nothing: the message it was waiting for goes to the next
  `receive()`. One `receive()` waits at a time; a second one throws `.other`. A close frame of the peer is
  delivered once as `.closed`; the system's code 1006 ("no close frame") is not a close and the failure is
  thrown instead. After that message, and after `close`, `send` and `receive` throw `.other`. A reason
  longer than 123 bytes is cut. An address whose scheme is not `wss` or `ws` throws `.other`.
- `URLErrorMapping` returns a `TransportFailure` unchanged.
- `QRScannerView`: no camera, or a camera that cannot be an input, is `cameraUnavailable`; an output that
  cannot be added or does not offer QR codes is `scannerUnavailable`. Nothing is reported after the view
  went away.
- Tests beyond the plan of 6.2, in `URLErrorMappingTests.swift`: the class `URLSessionTransportTests`
  proves the private session, the request, the response head, the chunk rule and the refused redirect,
  and runs whole requests against a URL protocol inside the test. No test opens a socket.

#### 3.2.2 Stores (architecture 4.10)

```swift
// Persistence/StorageNames.swift
enum StorageNames {
    static let keychainService = "com.mahmudkarim.LiveCueRemake"
    struct Names: Equatable, Sendable {
        let pairingAccount: String
        let metaKeyAccount: String
        let openRouterKeyAccount: String
        let pairingFileName: String
    }
    static let live: Names   // relayPairing, meta-api-key, openrouter-api-key, pairedPC.json
    static let test: Names   // relayPairing-tests, meta-api-key-ui-test, openrouter-api-key-ui-test, pairedPC-tests.json
    static let instructionKey = "assistantInstruction"
    static let speechProviderKey = "speechProvider"
    static let configurationKey = "assistantConfiguration"   // per provider: + ".codex", + ".openrouter"
}

// Persistence/SessionStoring.swift
protocol SessionStoring: Sendable {
    /// Newest `startedAt` first. Sessions that fail to decode are skipped.
    func loadAll() async -> [Session]
    /// Inserts or replaces the session with this id and records `savedAt` as its last-saved time.
    func save(_ session: Session, savedAt: Date) async throws
    func delete(id: UUID) async throws
    func contains(id: UUID) async -> Bool
    /// Launch recovery: every stored session without an end time is replaced by
    /// `closedAfterInterruption(at:)` with its last-saved time. Returns how many were closed.
    @discardableResult func closeInterruptedSessions() async -> Int
}

// Persistence/InMemorySessionStore.swift
actor InMemorySessionStore: SessionStoring { init() }

// Persistence/FileSessionStore.swift (R-33)
// One JSON file per session, `<session id>.json` (the id as `uuidString`), in the folder `Sessions` under
// Application Support. Each file holds one session, encoded with LiveCueJSON, together with its
// last-saved time, and is written atomically. The folder is created by `open()` and protected "complete
// until first user authentication". A file that does not decode is skipped silently on load. No file of
// the app imports SwiftData.
actor FileSessionStore: SessionStoring {
    /// `directory` nil means `Sessions` under Application Support; tests pass a temporary folder.
    static func open(directory: URL? = nil) throws -> FileSessionStore
}

// Persistence/PreferencesStoring.swift
@MainActor protocol PreferencesStoring: AnyObject {
    var assistantInstruction: String { get set }
    var speechProvider: SpeechProvider { get set }               // unknown stored values read as .meta
    var assistantConfiguration: AssistantConfiguration { get set }   // the current selection; default codex / gpt-5.6-sol / low
    func rememberedConfiguration(for provider: AssistantProvider) -> AssistantConfiguration?
    func remember(_ configuration: AssistantConfiguration)       // writes the key of its own provider
    func resetAssistantConfiguration()                           // removes the three configuration keys
}

// Persistence/UserDefaultsPreferencesStore.swift
@MainActor final class UserDefaultsPreferencesStore: PreferencesStoring { init(defaults: UserDefaults = .standard) }

// Persistence/SecretStoring.swift
enum SecretKind: CaseIterable, Sendable { case metaAPIKey, openRouterAPIKey }
struct SecretStoreFailure: Error, Equatable, Sendable { let status: Int32 }
protocol SecretStoring: Sendable {
    func read(_ kind: SecretKind) -> String?
    func save(_ value: String, for kind: SecretKind) throws      // SecretStoreFailure; nothing is stored elsewhere on failure
    func remove(_ kind: SecretKind) throws                       // SecretStoreFailure; absent is success
}

// Persistence/KeychainSecretStore.swift
struct KeychainSecretStore: SecretStoring { init(keychain: KeychainStore = KeychainStore(), names: StorageNames.Names) }

// Persistence/PairingStoring.swift
protocol PairingStoring: Sendable {
    /// The Keychain item is the authority; the file is a fallback (architecture 4.10). Keychain item
    /// first: when it parses, that is the pairing, and a file that is missing or holds anything else is
    /// rewritten from it. The file is read only when the Keychain has no item, cannot be read, or holds
    /// text that does not parse. The Keychain is never written from the file. Text that does not pass
    /// `PairingPayload.parse` is treated as absent.
    func load() -> PairingPayload?
    /// Keychain item first. When that write succeeds, the file is rewritten best effort (atomically,
    /// protected until first user authentication, excluded from backup). When it fails, the file must
    /// be written, and an older Keychain item is then deleted so that it cannot win at the next load;
    /// if such an item can be neither replaced nor deleted, the file is put back to what the Keychain
    /// holds and the call throws. After a `save` that returned, `load()` returns that pairing.
    func save(_ pairing: PairingPayload) throws
    func removeKeychainCopy()                                    // test seam only
}

// Persistence/FilePairingStore.swift
struct FilePairingStore: PairingStoring {
    init(keychain: KeychainStore = KeychainStore(), names: StorageNames.Names, directory: URL? = nil)   // nil = Application Support
}
```

What the declarations leave open, as built by WP-06. No declaration changes.

- `FileSessionStore`: a file is the JSON object `{"savedAt": …, "session": …}`. A file whose name is not
  `<id of the session inside it>.json` is skipped like one that does not decode, so no session can appear
  twice. `contains(id:)` is true when the session's file exists. Deleting an absent session succeeds.
  `closeInterruptedSessions` keeps each file's last-saved time.
- `UserDefaultsPreferencesStore` stores each configuration as JSON data written with `LiveCueJSON`, and
  reads every value from the defaults each time, so two stores over the same defaults agree.
- `FilePairingStore`: a file larger than C-18 is treated as absent without being read. After a refused
  Keychain write the store deletes the item and then looks at what the Keychain holds: only an older
  pairing that is still readable there makes `save` throw, and it throws the refusal of the write. A
  Keychain that refuses every call (LiveContainer without its groups) therefore leaves the pairing in the
  file and `save` succeeds. The Keychain calls go through the internal `PairingKeychainAccess`, which a
  test replaces to make single calls fail.
- Protection classes cannot be read back in a simulator, which keeps none. `FilePairingStoreTests` and
  `FileSessionStoreTests` assert the writing options and check the class only where the system reports
  one; backup exclusion is read back from the file.

#### 3.2.3 Connectivity types and the relay gateway

```swift
// Connectivity/ConnectivityTypes.swift
enum SavedPCState: Equatable, Sendable { case connected, reconnecting, needsAttention }
struct InlineMessage: Equatable, Sendable {
    let text: String
    let isError: Bool
    init(_ text: String, isError: Bool = false)
}

// Connectivity/RelayGateway.swift
/// The app's view of the relay. `RelayClient` already has these methods, so the conformance is empty.
protocol RelayGateway: Sendable {
    func health(_ pairing: PairingPayload) async throws -> RelayHealth
    func verifyPairing(_ pairing: PairingPayload) async throws
    func models(_ pairing: PairingPayload) async throws -> [AssistantModelOption]
    func localModelStatus(_ pairing: PairingPayload) async throws -> PCModelStatus
    func startLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus
    func stopLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus
    func assist(_ request: AssistRequest, pairing: PairingPayload) async throws -> AssistResponse
    func summarize(_ request: SummaryRequest, pairing: PairingPayload) async throws -> SummaryResponse
    func cancel(requestId: UUID, pairing: PairingPayload) async
}
extension RelayClient: RelayGateway {}
```

#### 3.2.4 Audio capture (architecture 4.6)

```swift
// Audio/AudioCapturing.swift
enum AudioTarget: Equatable, Sendable {
    case pcm16(sampleRate: Double)                       // little-endian Int16 mono bytes: Meta 24,000, PC speech 16,000
    case float32(sampleRate: Double)                     // mono Float samples: on-device 16,000
}
enum AudioChunk: Sendable {
    case pcm16(Data)
    case float32([Float])
}
struct AudioCaptureRequest: Equatable, Sendable {
    let target: AudioTarget
    let queueLimit: Int                                  // 64 streaming, 128 on-device
}
enum AudioCaptureEvent: Equatable, Sendable {
    case level(Float)                                    // RMS 0...1 of a converted buffer, at most 24 per second
    case overflow                                        // a converted buffer did not fit in the queue; sent once
    case interrupted                                     // AVAudioSession interruption began
    case routeLost                                       // route change with reason "old device unavailable"
}
enum AudioCaptureFailure: Error, Equatable, Sendable {
    case noUsableMicrophone                              // zero sample rate or zero channels: "No usable microphone."
    case couldNotStart                                   // audio session or engine refused to start: same text
}
struct AudioCaptureStreams: Sendable {
    let chunks: AsyncStream<AudioChunk>                  // bounded by `queueLimit`, oldest kept; finishes after `stop`
    let events: AsyncStream<AudioCaptureEvent>           // finishes after `stop`
}

@MainActor protocol AudioCapturing: AnyObject {
    var isRunning: Bool { get }
    /// Configures and activates the audio session (play-and-record, measurement, default to speaker,
    /// Bluetooth hands-free, haptics allowed while recording), taps the input node in its native format
    /// with a 2,048-frame buffer and starts the engine. Throws `AudioCaptureFailure`.
    func start(_ request: AudioCaptureRequest) throws -> AudioCaptureStreams
    /// Removes the tap, stops the engine and finishes both streams; buffers already queued stay readable.
    /// `releaseSession` deactivates the audio session, notifying other apps.
    func stop(releaseSession: Bool)
}
```

`AudioCapture` (WP-07) is the implementation: `@MainActor final class AudioCapture: AudioCapturing { init() }`.
`AudioTapConverter` is a `final class … @unchecked Sendable` that owns the `AVAudioConverter` and the two
continuations; its `static func makeTapBlock(...) -> AVAudioNodeTapBlock` is `nonisolated`, so the block
that runs on the audio thread is not main-actor isolated. (The stub declares `makeTapBlock(for:)`; WP-07
is free to change its parameters, which nothing else calls.) Calls are those of research 3.3 step 5 and of the
old app (`AVAudioEngine.inputNode.installTap(onBus:bufferSize:format:block:)`,
`AVAudioConverter.convert(to:error:withInputFrom:)`).

What the declarations leave open, as built by WP-07. No declaration changes.

- `AudioCapture.init` touches neither the engine nor the audio session. Every `start` makes a new
  `AVAudioEngine`, so each start takes the hardware format as it is then. A second `start` while running
  ends the first capture.
- `AudioTapConverter` is `init?(inputFormat:request:)` and `makeTapBlock(for:)`; it is nil for a format
  without a sample rate or a channel. One converter is kept for the whole capture, so the resampler runs
  on across buffers; it is replaced when a buffer arrives in another format. With more than one input
  channel the first is taken.
- The chunk queue is `bufferingOldest(queueLimit)`; the event stream is unbounded. The level is thinned
  by audio time, not by a clock: one level per `sampleRate / 24` converted frames, the first buffer at
  once.
- `stop(releaseSession: true)` deactivates only a session this capture activated, also when no capture
  is running (the Stop after an on-device Pause). A start that fails after activation deactivates it.
- The two notification observers are built in `AudioSessionPolicy`, outside the main actor, and report
  straight into the event stream. Only "interruption began" and "old device unavailable" are reported. A
  new input device attached while listening is not, by design (spec 01 section 3.2); see 6.5 item 4.

#### 3.2.5 Speech providers (architecture 4.7)

```swift
// Speech/SpeechTranscriber.swift
struct TranscriberStart: Equatable, Sendable {
    let generation: UUID
    let streamOffsetSeconds: Double
}

struct TranscriberFailure: Error, Equatable, Sendable {
    enum Reason: Equatable, Sendable {
        case stream                                      // relay, Meta or send failure
        case uploadOverflow, microphoneInterrupted, routeChanged, noMicrophone
        case notPrepared                                 // "Prepare Voz first." / "Prepare Parakeet first."
        case pendingAudioLimit, processingOverflow, processingFailed
    }
    let reason: Reason
    let message: String                                  // the text to show; never raw system or library text
    init(_ reason: Reason, message: String)
}

struct TranscriberEvent: Sendable {
    enum Kind: Sendable {                                // exactly the cases of architecture 4.7
        case status(String)
        case partial(String)
        case segment(TranscriptSegment)                  // times already include the stream offset
        case level(Float)
        case usage(UsageUpdate)
        case lastChunk(milliseconds: Double)
        case failed(TranscriberFailure)                  // after a successful start only
    }
    let generation: UUID
    let kind: Kind
}

@MainActor protocol SpeechTranscriber: AnyObject {
    var provider: SpeechProvider { get }
    /// One stream for the life of the object, unbounded. Events are emitted on the main actor.
    var events: AsyncStream<TranscriberEvent> { get }
    /// How many events have been emitted so far. When `start`, `finalize` or `transcribePending` returns
    /// or throws, every event that call caused has been emitted, so a consumer that has handled this many
    /// events has seen them all.
    var emittedEventCount: Int { get }
    /// Returns once audio is flowing. Streaming providers open the microphone only after the provider
    /// handshake. Throws `TranscriberFailure`; after a throw nothing is running.
    func start(_ start: TranscriberStart) async throws
    /// The provider's Pause and Stop rule (spec 01 section 3.1). Throws `TranscriberFailure` when the end
    /// was not clean; capture and connection are down either way. Streaming providers release the audio
    /// session; on-device providers keep it active.
    func finalize() async throws
    /// Voz: transcribe pending audio now and return the seconds it took. Others return 0 at once.
    /// Calls are serialised with `finalize` by the transcriber.
    func transcribePending() async throws -> TimeInterval
    /// Release everything and deactivate the audio session. Idempotent. Emits nothing.
    func shutdown() async
}

// Speech/TranscriberEventChannel.swift
@MainActor final class TranscriberEventChannel {
    let stream: AsyncStream<TranscriberEvent>
    private(set) var emittedCount: Int
    init()
    func emit(_ kind: TranscriberEvent.Kind, generation: UUID)   // after `finish()`: dropped and not counted
    func finish()
}

// Speech/SpeechTranscriberFactory.swift
@MainActor protocol SpeechTranscriberFactory: AnyObject {
    /// A new transcriber for one session. It lives across that session's pauses and resumes.
    func makeTranscriber(for provider: SpeechProvider) -> any SpeechTranscriber
}

// Speech/MetaKeyTesting.swift
protocol MetaKeyTesting: Sendable { func test(key: String) async throws }   // MetaSpeechError
struct LiveMetaKeyTester: MetaKeyTesting { init(transport: any WebSocketTransport, clock: any AppClock) }
```

On-device engines sit behind two protocols so the transcribers are unit-tested with fakes, and so each
package is imported by exactly one file (both packages export a type named `DownloadProgress`).

```swift
// Speech/OnDeviceEngines.swift
struct RecognizedWord: Equatable, Sendable {
    let text: String
    let start: TimeInterval
    let end: TimeInterval
}
struct BatchRecognition: Equatable, Sendable {
    let words: [RecognizedWord]
    let duration: TimeInterval
}
protocol BatchRecognizing: Sendable {                    // Voz
    func transcribe(samples: [Float]) async throws -> BatchRecognition   // 16 kHz mono
}
protocol StreamingRecognizing: Sendable {                // Parakeet
    func reset() async
    func setPartialHandler(_ handler: @escaping @Sendable (String) -> Void) async   // called off the main actor
    func process(samples: [Float]) async throws          // 16 kHz mono; text arrives through the handler
    func finish() async throws -> String                 // flushes and returns the final text
    func cleanup() async
}
struct ModelPreparationProgress: Equatable, Sendable {
    enum Phase: Equatable, Sendable { case checking, downloading, preparing }
    enum Counter: Equatable, Sendable {
        case none
        case bytes(completed: Int64, total: Int64)
        case files(completed: Int, total: Int)
    }
    let fraction: Double                                 // clamped to 0...1
    let phase: Phase
    let counter: Counter
    init(fraction: Double, phase: Phase, counter: Counter)   // clamps the fraction
}
protocol OnDeviceEngineLoading: Sendable {
    /// Downloads when needed, then loads. `progress` is called off the main actor.
    func loadVoz(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any BatchRecognizing
    func loadParakeet(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing
}

// Speech/VozEngine.swift: the only file that imports Voz (research 4.2)
// load: `let directory = try await Voz.download(progress:)` (its String result is the model folder, which
// is then excluded from backup), then `try await Voz(progress:)`. Progress maps `fraction`,
// `completedBytes`, `totalBytes`; phase is `.downloading` below 1 and `.preparing` at 1.
// transcribe: `try await voz.transcribe(samples:)`, mapping `result.words` (`text`, `start`, `end`) and
// `result.duration`.
struct VozEngine: BatchRecognizing {
    static func load(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> VozEngine
    func transcribe(samples: [Float]) async throws -> BatchRecognition
}

// Speech/ParakeetEngine.swift: the only file that imports FluidAudio (research 3.3)
// load: `MLModelConfiguration` with `.cpuAndNeuralEngine`;
// `StreamingEouAsrManager(configuration:chunkSize: .ms320, eouDebounceMs: 640)`;
// `try await manager.loadModels(to: nil, configuration: nil, progressHandler:)`. Progress maps
// `fractionCompleted` and `phase`: `.listing` is `.checking`, `.downloading(completedFiles:totalFiles:)`
// is `.downloading` with a file counter, `.compiling` is `.preparing`. Afterwards
// `Application Support/FluidAudio` is excluded from backup.
// process: builds a Float32 16 kHz mono `AVAudioPCMBuffer` from the samples and calls
// `try await manager.process(audioBuffer:)`, discarding its String result.
struct ParakeetEngine: StreamingRecognizing {
    static func load(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> ParakeetEngine
}

// Speech/LiveOnDeviceEngineLoader.swift
struct LiveOnDeviceEngineLoader: OnDeviceEngineLoading { init() }
```

```swift
// Speech/OnDeviceModelManager.swift
struct ModelPreparation: Equatable, Sendable {
    let variant: String                                  // the model this block belongs to
    var fraction: Double
    var startedAt: Date
    var finishedAt: Date?                                // elapsed time freezes here
    var counterText: String?                             // "212 MB / 467 MB" or "3 / 7 files"
    var failed: Bool
}

@MainActor @Observable final class OnDeviceModelManager {
    private(set) var loadedVariant: String?              // never restored at launch (IOS-B1)
    private(set) var preparation: ModelPreparation?      // nil once its model is ready (IOS-B13)
    private(set) var statusLine: String                  // starts as "Prepare a model"
    var isPreparing: Bool { get }
    var vozEngine: (any BatchRecognizing)? { get }
    var parakeetEngine: (any StreamingRecognizing)? { get }
    init(loader: any OnDeviceEngineLoading, activity: SessionActivity, clock: any AppClock)
    func isLoaded(for provider: SpeechProvider) -> Bool
    func loadedModel(for provider: SpeechProvider) -> TranscriptionModel?
    /// "Download & use" and "Reload". Does nothing during a session or while a preparation runs.
    /// Rethrows the loader's error after setting the status "Preparation failed".
    func prepare(_ model: TranscriptionModel) async throws
    func unload()
    func providerChanged(to provider: SpeechProvider)    // releases the loaded model
    func reportStatus(_ line: String)                    // transcribers mirror their status line here
    /// Fixture wiring only (`-on-device-ready`).
    func installPreloaded(variant: String, voz: (any BatchRecognizing)?, parakeet: (any StreamingRecognizing)?)
}
```

```swift
// Speech/SpeechProvider+Presentation.swift
extension SpeechProvider {
    var shortName: String { get }            // "Meta Muse", "Nemotron 3.5", "Qwen3 1.7B", "Voz", "Parakeet"
    var pickerTitle: String { get }          // screens 7: "Meta Muse · live cloud", …
    var homeCaption: String { get }          // screens 3, description line 1
    var listeningPlaceholder: String { get } // voz: "Recording locally. Tap Assist to transcribe."; others: "Listening for speech…"
    var connectingPlaceholder: String { get }// PC-local: "Listening starts when the PC model is ready."; others: "Listening starts when the connection is ready."
    var packageLabel: String? { get }        // voz: "Desert Ant 3.1.0"; parakeet: "FluidAudio 0.15.6"
    func explanation(answers: AssistantProvider) -> String   // S-01, S-02, S-03
}
```

Implementations (WP-07), all `@MainActor final class … : SpeechTranscriber`:

```swift
final class MetaTranscriber     { init(audio: any AudioCapturing, transport: any WebSocketTransport, secrets: any SecretStoring, clock: any AppClock) }
final class RelayTranscriber    { init(model: SpeechProvider, audio: any AudioCapturing, transport: any WebSocketTransport, pairingStore: any PairingStoring, clock: any AppClock) }
final class VozTranscriber      { init(engine: (any BatchRecognizing)?, audio: any AudioCapturing, clock: any AppClock, models: OnDeviceModelManager) }
final class ParakeetTranscriber { init(engine: (any StreamingRecognizing)?, audio: any AudioCapturing, clock: any AppClock, models: OnDeviceModelManager) }

/// Shared by Meta and relay: owns one `SpeechStreamSession` per start, pumps capture into it and maps
/// its events to `TranscriberEvent`s (adding the stream offset to segment times).
@MainActor final class StreamingTranscriberEngine {
    init(provider: SpeechProvider, audio: any AudioCapturing, transport: any WebSocketTransport,
         clock: any AppClock, channel: TranscriberEventChannel)
    func start(_ start: TranscriberStart, dialect: SpeechStreamDialect) async throws
    func finalize() async throws
    func shutdown() async
}

@MainActor final class LiveTranscriberFactory: SpeechTranscriberFactory {
    init(audio: any AudioCapturing, transport: any WebSocketTransport, secrets: any SecretStoring,
         pairingStore: any PairingStoring, clock: any AppClock, models: OnDeviceModelManager)
}

enum TranscriberStatusText {                             // Speech/TranscriberStatusText.swift
    static func connecting(_ provider: SpeechProvider) -> String        // "Connecting to <display name>…"
    static func loading(_ provider: SpeechProvider, seconds: Int) -> String   // "Loading <short name> on PC · <N> s. Microphone starts when ready."
    static func connected(_ provider: SpeechProvider, setupSeconds: Double) -> String   // "Meta direct · 0.00 s setup" / "Connected · 0.00 s setup"
    static func firstWords(seconds: Double) -> String    // "First words · 0.00 s from stream start"
    static func appendingFinal(_ line: String, seconds: Double) -> String   // line + " · Final 0.00 s"
    static func lastBuffer(milliseconds: Double) -> String   // "Last buffer processing: N ms"
    static func batch(audioSeconds: Double, transcriptionSeconds: Double) -> String   // "0.0 s audio → 0.00 s transcription"
    static let recordingOnDevice = "Recording on this iPhone"
    static let disconnected = "Disconnected · resume to retry"
    static let finishing = "Finishing the transcript…"
}
```

Numbers are formatted with `String(format:)` and the POSIX locale, so the decimal separator is always ".".

What the declarations leave open, as built by WP-07. No declaration changes; the members named here are
internal additions.

- No transcriber ever finishes its event stream, also not in `shutdown`: a consumer ends its own loop.
- Streaming transcribers. A stream that ends between the provider being ready and `start` returning makes
  `start` throw with that failure's text and emits no `failed`; the usage stream it opened is closed as
  interrupted before the throw. After `start` has returned, `failed` can arrive at once, so the consumer
  must not drop a `failed` of the current generation that arrives before it has changed its phase.
  `finalize` emits no `failed`: an unclean end is the thrown finalise text. The fifteen seconds bound the
  whole finalisation, the hand-over of queued buffers included. `finalize` with nothing running returns
  and emits nothing. A start without a pairing throws before anything is emitted.
- On-device transcribers. After `failed` the run stays open for the `finalize` that T-10 calls: its
  segment and its statuses carry the same generation, and the capture is not stopped twice. Buffers
  queued at the failure are still appended (Voz) or processed without events (Parakeet), except after a
  Parakeet processing error. Parakeet emits `lastChunk` and its status only while listening. Voz says
  "Recording on this iPhone" again after a batch that failed while recording. Every Voz start empties the
  pending buffer.
- `OnDeviceModelManager`: `prepare` returns for a variant that is not on-device; `unload()` does nothing
  during a session; `unload()` and `providerChanged(to:)` abandon a preparation in flight, whose engine is
  released when it arrives. `reportStatus` is unconditional; the transcribers report only when they hold
  an engine.
- `LiveOnDeviceEngineLoader` rethrows a package error that has only a `description` as
  `EngineLoadFailure`, so that `localizedDescription` (E-12) is the package's text.
- `TranscriberStatusText` also holds "Listening", "Transcribing…", "Paused" and "Ready";
  `TranscriberFailureText`, in the same file, holds every failure text of 4.2.
- `FixtureTranscriber` commits the fixture segment at every start, Resume included (4.9).
  `FixtureEngineLoader` reports its half with a byte count (Voz) or a file count (Parakeet).

#### 3.2.6 Answer services (architecture 4.8; the protocol is fixed)

```swift
// Answers/AnswerService.swift
protocol AnswerService: Sendable {
    var provider: AssistantProvider { get }
    var supportsSummary: Bool { get }                    // true for Codex only
    func loadCatalogue() async throws -> [AssistantModelOption]
    /// `request.assistant` is set and the request is already fitted. `onProgress` receives the whole text so far.
    func answer(_ request: AssistRequest,
                onProgress: @escaping @Sendable (String) async -> Void) async throws -> AssistResponse
    func summarize(_ request: SummaryRequest) async throws -> SummaryResponse
    /// Cancels the task that is running the request with this id, if any. Sends nothing itself.
    func cancel(requestId: UUID) async
}

protocol AnswerServiceProviding: Sendable {
    func service(for provider: AssistantProvider) -> any AnswerService
}

protocol OpenRouterKeyVerifying: Sendable { func verify(key: String) async throws }   // OpenRouterError
```

```swift
// Answers/CodexRelayAnswerService.swift
// Reads the pairing at the moment of use; without one every call throws RelayFailure.local(.notPaired).
// Each answer and summary runs in a task the actor keeps by request id until it ends; `cancel` cancels
// that task, and the relay client then sends the one DELETE (3.1.5).
actor CodexRelayAnswerService: AnswerService {
    init(gateway: any RelayGateway, pairingStore: any PairingStoring)
}

// Answers/OpenRouterAnswerService.swift
// Reads the key at the moment of use. `answer` passes an option made from `request.assistant.model` and a
// nil marker (the controller owns the marker). `summarize` is never called and throws `.unavailable`.
actor OpenRouterAnswerService: AnswerService, OpenRouterKeyVerifying {
    init(client: OpenRouterClient, secrets: any SecretStoring)
}

// Answers/LiveAnswerServices.swift
struct LiveAnswerServices: AnswerServiceProviding {
    init(codex: CodexRelayAnswerService, openRouter: OpenRouterAnswerService)
}

// Answers/AnswerErrorText.swift
enum AnswerErrorText {
    /// RelayFailure: RelayMessages.text.  OpenRouterError, MetaSpeechError: errorDescription.
    /// TranscriberFailure (Voz transcription inside an Assist): its message.
    /// CancellationError: nil (nothing is shown).  Anything else: the L-NETWORK text for Codex, the
    /// `.unavailable` text for OpenRouter.
    static func message(for error: any Error, provider: AssistantProvider) -> String?
}
```

An actor that conforms to `AnswerService` declares `nonisolated let provider` and
`nonisolated var supportsSummary`.

#### 3.2.7 Session controller

```swift
// Session/SessionTypes.swift
enum SessionPhase: Equatable, Sendable { case idle, preflight, connecting, listening, pausing, paused, resuming, ending }

enum AssistStage: Equatable, Sendable {
    case transcribing, thinking, checkingModel, replying
    var label: String { get }                            // "Transcribing…", "Thinking…", "Checking model…", "Replying…"
}

struct SessionNotice: Equatable, Sendable {
    enum Kind: Equatable, Sendable { case recordingPaused, finalizeFailed }
    let kind: Kind
    let message: String
    var title: String { get }                            // "Recording paused" / "Paused"
}

// AppShell/SessionActivity.swift
/// Written only by SessionController. Read by the owners that must lock while a session runs.
@MainActor @Observable final class SessionActivity {
    var isActive: Bool                                   // a session exists (phase is not idle or preflight)
    var isAssisting: Bool
    init()
}
```

```swift
// Session/SessionController.swift
@MainActor struct SessionDependencies {
    let transcribers: any SpeechTranscriberFactory
    let answers: any AnswerServiceProviding
    let sessions: any SessionStoring
    let secrets: any SecretStoring
    let pairingStore: any PairingStoring
    let permissions: any PermissionProviding
    let clock: any AppClock
    let app: AppModel
    let activity: SessionActivity
    let speech: SpeechSettings
    let assistant: AssistantSettings
    let relay: RelayMonitor
    let models: OnDeviceModelManager
    let history: HistoryModel
}

@MainActor @Observable final class SessionController {
    // Observable state. Views read it; only this class writes it.
    private(set) var phase: SessionPhase
    private(set) var session: Session?                   // nil in idle and preflight
    private(set) var provider: SpeechProvider?           // the provider this session started with
    private(set) var partialText: String
    private(set) var level: Float                        // 0 while not listening
    private(set) var elapsedSeconds: Int
    private(set) var transcriberStatus: String
    private(set) var notice: SessionNotice?
    private(set) var pausedAutomatically: Bool
    private(set) var isAssisting: Bool
    private(set) var assistStage: AssistStage?
    private(set) var streamingAnswer: String             // kept after a failure until the next Assist starts
    private(set) var assistError: String?
    private(set) var latestTurnID: UUID?                 // the turn marked "Latest" in this session
    private(set) var canRetry: Bool                      // a previous request is remembered
    private(set) var listeningStartedCount: Int          // increments on every entry into `listening` (haptic trigger)
    private(set) var answerArrivedCount: Int             // increments at the first streamed text or saved answer of an Assist
    private(set) var assistFailedCount: Int

    var isTransitioning: Bool { get }                    // preflight, connecting, pausing, resuming, ending
    var showsLiveScreen: Bool { get }                    // session != nil
    var controlsEnabled: Bool { get }                    // a session exists, not transitioning, not assisting
    var estimatedCost: String? { get }                   // "$0.00000 USD" for Meta sessions, else nil

    init(_ dependencies: SessionDependencies)

    // Intents. Each is single-flight: it returns at once when its guard fails.
    func start() async
    func togglePause() async                             // Pause when listening, Resume when paused
    func assist() async
    func retrySameText() async
    func stop() async
    /// Cancels the summary request of a stored session, if one is in flight. Called before a delete.
    func cancelSummary(forSession id: UUID) async
}
```

`assist()` and `retrySameText()` are declared in `SessionController+Assist.swift`, and
`cancelSummary(forSession:)` in `SessionController+Summary.swift`, as extensions. Swift lets only the file
that declares a `private(set)` property assign it, so code in the two extension files cannot write the
observable state directly: it calls methods that `SessionController.swift` declares for that purpose
(8.3).

Helper types owned by WP-08, used only inside `Session/`:

```swift
/// Consumes one transcriber's events in order on the main actor.
@MainActor final class TranscriberEventPump {
    init(transcriber: any SpeechTranscriber, handler: @escaping @MainActor (TranscriberEvent) -> Void)
    func waitUntilCaughtUp() async                       // returns when every event emitted so far was handled
    func stop()
}

/// Latest value wins: a save requested while one is running replaces any queued one.
@MainActor final class SessionPersistence {
    init(store: any SessionStoring, clock: any AppClock)
    func schedule(_ session: Session)
    func flush() async                                   // returns when nothing is queued or running
}
```

What the declarations leave open, as built by WP-08. No declaration changes; the members named here are
internal additions.

- `SessionController.dependencies` is internal, so that the two extension files can read it. The state
  changes of the Assist pipeline are methods of `SessionController.swift` (`clearAssistCard`,
  `showAssistPrecondition`, `beginAssist`, `setAssistStage`, `transcribePendingAudio`, `rememberAssist`,
  `showStreamedAnswer`, `saveAnswer`, `failAssist`, `endAssist`); nothing outside `Session/` calls them.
  `waitUntilSettled()` returns when every transcriber event emitted so far was applied and no save is
  queued or running; the app never needs it, tests wait with it.
- The usage of the session in progress is observable state of its own and is copied into `session` at each
  save, so a usage event redraws the cost and nothing else. `estimatedCost` reads it.
- A `failed` of the current generation that arrives while the phase is still `connecting` or `resuming` is
  kept (T-20). When the start call throws it is forgotten; when the start call returned it is applied
  right after T-04 or T-12, as T-09 or T-10.
- `level` goes to 0 as soon as the phase leaves `listening` (at T-06, T-10 and T-14), not only at T-07, so
  that it is 0 whenever the session is not listening.
- Ending a session, and a failed start, put the observable state back to that of no session (partial,
  level, elapsed time, status, notice, streamed text, Assist error, latest turn, retry memory). The three
  counters only ever rise.
- A precondition of step 2 sets `assistError` and leaves `assistFailedCount` alone: the Assist did not
  start. A cancellation changes neither. `showStreamedAnswer` and `saveAnswer` drop what names another
  session, or arrives when no Assist is running.
- `SessionPersistence` stamps a save with `clock.now()` when it hands it to the store. A save the store
  refuses is dropped; the next one carries the whole session again.
- The summary keeps the session as it was saved at Stop and writes that copy back with the reply's title
  and notes. `cancelSummary` forgets the summary first, so a reply that still arrives is dropped.
- Both live services also cancel their request when the calling task is cancelled.
- `FixtureAnswerService` throws its one failure of `assistFailsOnce` after the 0.4 s wait, so the partial
  text is on screen first. Its `cancel` does nothing: it keeps nothing in flight.

#### 3.2.8 State owners

```swift
// AppShell/AppRoutes.swift
enum AppTab: Hashable, Sendable { case live, history, settings }
enum Route: Hashable, Sendable {
    case transcription, pairing, assistantLab, modelLibrary, benchmark, metaKey, pcSpeechModel
    case openRouterModels, reasoning
    case runDetails(turnID: UUID)
    case sessionDetail(sessionID: UUID)
}

// AppShell/AppModel.swift
@MainActor @Observable final class AppModel {
    var selectedTab: AppTab
    var livePath: [Route]
    var historyPath: [Route]
    var settingsPath: [Route]
    var sessionPath: [Route]                             // the stack of the live session screen
    private(set) var alertMessage: String?
    var isAlertPresented: Bool { get set }               // setting false clears the message
    init()
    func presentAlert(_ message: String)                 // a newer message replaces an older one
    func sessionDidStart()                               // empties all four paths
    func sessionDidEnd()                                 // empties all four paths and selects .live
}
```

```swift
// Connectivity/RelayMonitor.swift (protocol 13.3; spec 01 section 3.7)
@MainActor @Observable final class RelayMonitor {
    private(set) var pairing: PairingPayload?
    private(set) var isOnline: Bool
    private(set) var savedPCState: SavedPCState          // follows the most recent check only (IOS-B11)
    private(set) var connectionMessage: String?          // nil until the first check ends
    private(set) var isCheckingHealth: Bool
    private(set) var pcModelStatus: PCModelStatus?       // nil = unknown
    private(set) var pcModelMessage: String              // starts as "Checking PC model…"
    private(set) var isPCModelCommandInFlight: Bool

    var isPaired: Bool { get }
    var needsPC: Bool { get }                            // PCRequirement.needsPC for the current selections
    var isPCModelLocked: Bool { get }                    // command in flight, PC changing, session active, PC busy, or not paired

    init(gateway: any RelayGateway, pairingStore: any PairingStoring, clock: any AppClock,
         speech: SpeechSettings, assistant: AssistantSettings, activity: SessionActivity)

    func start()                                         // loads the pairing and starts the two poll loops; idempotent
    func appBecameActive()                               // one health check now, when needed
    func selectionChanged()                              // provider or answer service changed: re-evaluate polling at once
    @discardableResult func checkHealthNow() async -> Bool   // "Reconnect" and the start check; joins a running check
    func pairingSaved(_ pairing: PairingPayload)         // sets connected and "Connected. Your pairing is saved on this iPhone."
    func reloadPairing()                                 // reads the store again
    func beginPCModelObservation()                       // a screen that shows the PC model appeared
    func endPCModelObservation()
    func refreshPCModel() async
    func startPCModel() async                            // refused locally while locked
    func stopPCModel() async
    func isReady(for provider: SpeechProvider) -> Bool
    func canStartSession(for provider: SpeechProvider) -> Bool   // online, ready for it, not busy, no command in flight
    func homeLabel(for provider: SpeechProvider) -> String       // spec 01 section 3.7, ten rows
}

// Connectivity/PairingController.swift (architecture 4.9)
@MainActor @Observable final class PairingController {
    private(set) var isVerifying: Bool
    init(gateway: any RelayGateway, store: any PairingStoring, monitor: RelayMonitor)
    /// Parse only. Nothing is saved or sent. Throws `PairingPayloadError`.
    func readPayload(_ text: String) throws -> PairingPayload
    /// Canonicalises, calls route 2, and on 200 saves and tells the monitor. Throws `PairingPayloadError`
    /// or `RelayFailure`; a previous pairing is untouched on any failure.
    func verifyAndPair(address: String, token: String) async throws
    /// Added by the review. Pure: nothing is sent or saved. Non-nil when a pairing is saved and the host
    /// of `address` (after the manual-entry rules) differs from the saved host; nil when nothing is
    /// saved, when the hosts are equal, and when `address` does not parse (the verify call reports that).
    func replacement(forAddress address: String) -> PairingReplacement?
}

// Connectivity/ConnectivityTypes.swift (added by the review)
struct PairingReplacement: Equatable, Sendable {
    let currentHost: String
    let newHost: String
    var newHostIsTailscaleName: Bool { get }             // newHost ends in ".ts.net"
}
```

```swift
// Answers/AssistantSettings.swift (spec 01 section 3.8; spec 02 section 2.2)
@MainActor @Observable final class AssistantSettings {
    private(set) var configuration: AssistantConfiguration
    var instruction: String                              // saved on every change
    private(set) var catalogue: [AssistantModelOption]   // memory only
    private(set) var catalogueMessage: InlineMessage?
    private(set) var isLoadingCatalogue: Bool
    private(set) var hasOpenRouterKey: Bool
    private(set) var openRouterKeyMessage: InlineMessage?
    private(set) var isVerifyingOpenRouterKey: Bool
    @ObservationIgnored var onProviderChanged: (@MainActor () -> Void)?

    init(preferences: any PreferencesStoring, secrets: any SecretStoring, pairingStore: any PairingStoring,
         answers: any AnswerServiceProviding, keyVerifier: any OpenRouterKeyVerifying, activity: SessionActivity)

    func switchProvider(_ provider: AssistantProvider)   // refused while assisting; same provider does nothing
    func selectCodexModel(_ option: AssistantModelOption)   // keeps the effort when the option lists it
    func selectReasoningEffort(_ effort: String)
    func selectOpenRouterModel(_ option: AssistantModelOption)   // effort "default"
    func loadCatalogue() async                           // tagged by provider; a stale result is dropped
    func adoptCatalogue(_ options: [AssistantModelOption])   // the Assist pipeline stores what it fetched
    @discardableResult func saveOpenRouterKey(_ text: String) -> Bool
    func verifyOpenRouterKey() async
    func removeOpenRouterKey()
}

// Speech/SpeechSettings.swift (spec 01 sections 2.6 and 4.1)
@MainActor @Observable final class SpeechSettings {
    private(set) var provider: SpeechProvider
    private(set) var hasMetaKey: Bool
    private(set) var metaKeyMessage: InlineMessage?
    private(set) var isTestingMetaKey: Bool
    @ObservationIgnored var onProviderChanged: (@MainActor (SpeechProvider) -> Void)?
    @ObservationIgnored var changeIsBlocked: (@MainActor () -> Bool)?     // installed by AppEnvironment from SystemReadiness

    init(preferences: any PreferencesStoring, secrets: any SecretStoring, keyTester: any MetaKeyTesting,
         activity: SessionActivity)

    func select(_ provider: SpeechProvider)              // refused during a session or while blocked
    @discardableResult func saveMetaKey(_ text: String) -> Bool   // the three key actions are refused during a session
    func testMetaKey() async
    func removeMetaKey()
}

// Persistence/HistoryModel.swift
@MainActor @Observable final class HistoryModel {
    private(set) var sessions: [Session]                 // newest start first
    @ObservationIgnored var willDelete: (@MainActor (UUID) async -> Void)?   // SessionController.cancelSummary
    init(store: any SessionStoring)
    func reload() async
    func delete(id: UUID) async                          // calls `willDelete`, deletes, reloads
    func session(id: UUID) -> Session?
}

// AppShell/SystemReadiness.swift (spec 01 section 0.2)
enum ProviderLockReason: Equatable, Sendable {
    case preparingModel                                  // "Available when the model has finished preparing."
    case pcModelChanging                                 // "Available when the PC model has finished changing."
    var caption: String { get }
}
@MainActor final class SystemReadiness {
    init(speech: SpeechSettings, assistant: AssistantSettings, relay: RelayMonitor, models: OnDeviceModelManager)
    var needsPC: Bool { get }
    var speechReady: Bool { get }
    var answersReady: Bool { get }
    var systemReady: Bool { get }
    var providerLockReason: ProviderLockReason? { get }
}
```

`SystemReadiness` stores nothing; reading its properties inside a view body is tracked through the owners
it reads.

What the declarations leave open, as built by WP-09. No declaration changes.

- `RelayMonitor` reads the saved pairing when it is built, so `isPaired` is right in the first frame, and
  again in `start()`. Nothing is polled before `start()`: until then `appBecameActive()`,
  `selectionChanged()` and the two observation calls send nothing. `checkHealthNow()` asks whenever a
  pairing is saved, also when the PC is not needed: only "Reconnect" and start check 3 call it, and both
  are asked for by the user. Without a pairing it returns false and sends nothing. It returns after the PC
  model check that follows a successful check, so its caller finds the PC model status fresh. A check
  that is in flight when the pairing changes is dropped, and what was known about the PC model is
  forgotten with the old pairing.
- `refreshPCModel()` called while a status check is in flight sends nothing and returns when that check
  ends, so its caller (start check 2, for one) reads a status that arrived after the call. A PC model
  command first ends a status check that is in flight (the check is cancelled and its answer dropped), so
  the two never overlap (protocol 13.3). A status check that returns after the provider stopped being
  PC-local says nothing.
- `PairingController.verifyAndPair` reports invalid input before it looks at anything else. Besides the
  two errors of its declaration it can throw `CancellationError` (the caller's task was cancelled, or a
  verification is already running) and, when the relay accepted the pairing and the store then refused
  it, the store's own error; there is no phone text for that last case. `replacement(forAddress:)`
  compares with the pairing the store holds.
- `AssistantSettings`: "Pair your PC first." is a plain message, not an error. A failed load keeps the
  rows that were loaded before. `adoptCatalogue` leaves the message alone. Choosing a model or an effort
  of the service that is not selected does nothing. An empty OpenRouter key gets "Enter a valid API key.",
  and one with a space or a line break anywhere gets the malformed-key text; nothing is trimmed from it.
- `SpeechSettings.saveMetaKey` trims the text first (spec 01 section 2.6). What is left gets the
  missing-key text when it is empty and the malformed-key text when it holds a space or a line break.
  Choosing the provider that is already selected does nothing.
- `SystemReadiness.providerLockReason` has no case for a running session, because the screens that show
  the caption cannot be open then; `SpeechSettings.select` refuses that change by itself.
- `HistoryModel` sorts what the store returns. A reload that asked the store before a newer reload did
  does not replace the newer answer. A session the store could not delete stays listed.
- `FixtureRelayGateway`: with `pcOffline` every route gives `L-UNREACHABLE`, not only health. The PC
  model follows protocol 9.3: starting the loaded model answers `ready` at once, a second operation is
  `MODEL_OPERATION_IN_PROGRESS`, stopping while the other model is loaded is `MODEL_MISMATCH`, a model
  that is not PC-local is `INVALID_MODEL`, and the error of `pcModelStartError` is forgotten after C-48
  or at the next start. An operation ends when a later call finds that its time has passed; nothing runs
  in the background. `models` waits 8 s with `pcCatalogSlow`. `assist` and `summarize`, which the fixture
  wiring never calls, give a fixed reply with the selection echoed, or `INVALID_ASSISTANT` and
  `ASSISTANT_SELECTION_UNAVAILABLE`.
- Tests beyond the plan of 6.2, in `RelayMonitorTests.swift`: the class `FixtureRelayGatewayTests`. Two
  tests of `SystemReadinessTests` (an on-device model that is loaded, and the lock while one is prepared)
  skip themselves while `OnDeviceModelManager` is still the stub of WP-01.

#### 3.2.9 Environment and dependency container (architecture 4.3)

```swift
// AppShell/AppDependencies.swift
@MainActor struct AppDependencies {
    var clock: any AppClock
    var permissions: any PermissionProviding
    var clipboard: any ClipboardWriting
    var preferences: any PreferencesStoring
    var secrets: any SecretStoring
    var pairingStore: any PairingStoring
    var sessions: any SessionStoring
    var relayGateway: any RelayGateway
    var answers: any AnswerServiceProviding
    var openRouterKeyVerifier: any OpenRouterKeyVerifying
    var metaKeyTester: any MetaKeyTesting
    var engineLoader: any OnDeviceEngineLoading
    /// Built after the model manager exists, because on-device transcribers take their engine from it.
    var makeTranscriberFactory: @MainActor (OnDeviceModelManager) -> any SpeechTranscriberFactory
    static func live() -> AppDependencies                // AppEnvironment+Live.swift
    static func fixture(_ mode: TestMode) -> AppDependencies   // AppEnvironment+Fixture.swift
}

// AppShell/AppEnvironment.swift
@MainActor @Observable final class AppEnvironment {
    let mode: LaunchMode
    let dependencies: AppDependencies
    // State owners, built in this order.
    let app: AppModel
    let activity: SessionActivity
    let toasts: ToastCenter
    let speech: SpeechSettings
    let assistant: AssistantSettings
    let models: OnDeviceModelManager
    let relay: RelayMonitor
    let pairing: PairingController
    let history: HistoryModel
    let readiness: SystemReadiness
    let session: SessionController
    // View models (section 5), built last.
    let home: HomeViewModel
    let live: LiveSessionViewModel
    let assistantLab: AssistantLabViewModel
    let settings: SettingsViewModel
    let transcription: TranscriptionViewModel
    let pcSpeechModel: PCSpeechModelViewModel
    let metaKey: MetaKeyViewModel
    let pairingScreen: PairingViewModel
    let modelLibrary: ModelLibraryViewModel
    let benchmark: BenchmarkViewModel
    let historyScreen: HistoryViewModel

    /// The only way the app makes one. Applies the test-seam launch actions before any owner reads a store.
    static func make(_ launch: LaunchConfiguration) -> AppEnvironment
    init(mode: LaunchMode, dependencies: AppDependencies)
    /// Once, from the root view's task: close interrupted sessions, load History, start the relay monitor.
    func bootstrap() async
    func sceneBecameActive()
}

// AppShell/EnvironmentInjection.swift
extension View {
    /// Puts the environment and every state owner and view model into the SwiftUI environment, so a view
    /// reads what it needs with `@Environment(Type.self)`.
    func appEnvironment(_ environment: AppEnvironment) -> some View
}
```

Wiring done in `AppEnvironment.init` after the owners exist:
`speech.onProviderChanged = { models.providerChanged(to: $0); relay.selectionChanged() }`,
`speech.changeIsBlocked = { readiness.providerLockReason != nil }`,
`assistant.onProviderChanged = { relay.selectionChanged() }`,
`history.willDelete = { await session.cancelSummary(forSession: $0) }`.

Live wiring (`AppDependencies.live()`): `SystemClock`; `SystemPermissions`; `SystemClipboard`;
`UserDefaultsPreferencesStore`; `KeychainSecretStore(names: StorageNames.live)`;
`FilePairingStore(names: StorageNames.live)`;
`FileSessionStore.open()`, falling back to `InMemorySessionStore` if it throws;
`RelayClient(transport: URLSessionHTTPTransport(), timing: clock.coreTiming)`;
`OpenRouterClient(transport: URLSessionHTTPTransport(resourceTimeout: 120), timing: clock.coreTiming)`;
the two answer services; `LiveMetaKeyTester`; `LiveOnDeviceEngineLoader`; a `LiveTranscriberFactory` with
one `AudioCapture` and one `URLSessionWebSocketTransport`.

#### 3.2.10 Test seam (architecture 4.11; fixed arguments)

```swift
// TestSeam/TestMode.swift
struct TestMode: Equatable, Sendable {
    var onDeviceReady = false                // -on-device-ready
    var metaNoKey = false                    // -meta-no-key
    var metaKeyPersistence = false           // -meta-key-persistence
    var openRouterUIReset = false            // -openrouter-ui-reset
    var openRouterPersistenceTest = false    // -openrouter-persistence-test
    var pcOffline = false                    // -pc-offline
    var pcCatalogSlow = false                // -pc-catalog-slow
    var pcModelSlowStart = false             // -pc-model-slow-start
    var pcModelStartError = false            // -pc-model-start-error
    var assistFailsOnce = false              // -assist-fails-once
    var pairingPersistenceTest = false       // -pairing-persistence-test
    var savePairingFixture = false           // -save-pairing-fixture
    var dropTestKeychain = false             // -drop-test-keychain
    init()
    init(arguments: [String])
}

// TestSeam/LaunchConfiguration.swift
enum LaunchMode: Equatable, Sendable { case live, fixture(TestMode) }
struct LaunchConfiguration: Equatable, Sendable {
    let mode: LaunchMode
    /// Fixture mode when the arguments contain "-ui-testing", or when the environment has
    /// "XCTestConfigurationFilePath" (the app is hosting unit tests; base test mode).
    init(arguments: [String], environment: [String: String])
    static var current: LaunchConfiguration { get }      // from ProcessInfo
    static let uiTestingArgument = "-ui-testing"
    static let unitTestHostKey = "XCTestConfigurationFilePath"
}

// TestSeam/FixtureValues.swift
enum FixtureValues {
    static let segmentText = "What is the main advantage of local transcription?"
    static let partialAnswer = "Your audio stays on the iPhone,"
    static let answer = "Your audio stays on the iPhone, so nothing you say leaves the device to be transcribed."
    static let details = "Local transcription also keeps working without a network connection."
    static let metaKey = "fixture-meta-key"
    static let codexCatalogue: [AssistantModelOption]    // gpt-6-astra, gpt-6-luna, gpt-5.6-sol, gpt-5.6-luna, gpt-5.3-codex-spark
    static let openRouterCatalogue: [AssistantModelOption]   // test/direct, ["default"]
    static let pairingEndpoint = "https://pc.example.test:11443/"
    static let pairingToken: String                      // the synthetic token of protocol 4.2
    static var pairing: PairingPayload { get }           // `pairingEndpoint` and `pairingToken`
    static let execution: RelayExecution                 // codexMs 600, relayTotalMs 620, requestReadMs 2, relayOverheadMs 20
    static let openRouterUsage: AssistantUsage           // 120 prompt tokens, 40 completion tokens, cost 0.0001
    static let assistPartialSeconds: TimeInterval = 0.4
    static let pcModelChangeSeconds: TimeInterval = 1
    static let pcModelSlowStartSeconds: TimeInterval = 6
    static let catalogueSlowSeconds: TimeInterval = 8
    static let preparationHoldSeconds: TimeInterval = 2
}

// TestSeam/FixturePairingStore.swift, FixturePermissions.swift
struct FixturePairingStore: PairingStoring { init() }    // always the fixture pairing; save and remove do nothing
struct FixturePermissions: PermissionProviding { init() }   // both true, no prompt
struct FixtureMetaKeyTester: MetaKeyTesting { init() }   // in FixturePermissions.swift; succeeds at once, opens no socket (R-34)

// TestSeam/TestSeamLaunchActions.swift
@MainActor enum TestSeamLaunchActions {
    /// In this order: provider = meta (parakeet with onDeviceReady); reset the answer configuration unless
    /// openRouterPersistenceTest; save the placeholder Meta key unless metaKeyPersistence; remove it when
    /// metaNoKey; remove the OpenRouter key when openRouterUIReset; save the fixture pairing when
    /// savePairingFixture; remove the Keychain copy when dropTestKeychain.
    static func apply(_ mode: TestMode, preferences: any PreferencesStoring, secrets: any SecretStoring,
                      pairingStore: any PairingStoring)
}

// TestSeam/FixtureTranscriber.swift, FixtureTranscriberFactory.swift, FixtureEngineLoader.swift
@MainActor final class FixtureTranscriber: SpeechTranscriber { init(provider: SpeechProvider, clock: any AppClock) }
@MainActor final class FixtureTranscriberFactory: SpeechTranscriberFactory { init(clock: any AppClock) }
struct FixtureEngineLoader: OnDeviceEngineLoading { init(clock: any AppClock) }   // reports 0.5, waits 2 s, returns a fixture engine
struct FixtureBatchEngine: BatchRecognizing { init() }
struct FixtureStreamingEngine: StreamingRecognizing { init() }

// TestSeam/FixtureAnswerServices.swift
actor FixtureAnswerService: AnswerService, OpenRouterKeyVerifying {
    init(provider: AssistantProvider, mode: TestMode, clock: any AppClock)
}
struct FixtureAnswerServices: AnswerServiceProviding { init(mode: TestMode, clock: any AppClock) }

// TestSeam/FixtureRelayGateway.swift
actor FixtureRelayGateway: RelayGateway { init(mode: TestMode, clock: any AppClock) }
```

Fixture wiring (`AppDependencies.fixture(_:)`): `SystemClock`; `FixturePermissions`; `SystemClipboard`;
`UserDefaultsPreferencesStore`; `KeychainSecretStore(names: StorageNames.test)`;
`FilePairingStore(names: StorageNames.test)` when `pairingPersistenceTest`, else `FixturePairingStore`;
`InMemorySessionStore`; `FixtureRelayGateway`; `FixtureAnswerServices`; a second
`FixtureAnswerService(provider: .openrouter, …)` as the OpenRouter key verifier; `FixtureMetaKeyTester`;
`FixtureEngineLoader`; `FixtureTranscriberFactory`. With `onDeviceReady`, `AppEnvironment.init` calls
`models.installPreloaded(variant: "parakeet", …)`. Nothing in fixture mode opens a socket, a URL session,
the microphone or the camera. Fixture behaviour is specified in 4.9.

#### 3.2.11 Fakes for app unit tests (`ios/AppTests/Support`, WP-01)

```swift
final class ManualClock: AppClock, @unchecked Sendable {      // lock-protected
    init(now: Date = Date(timeIntervalSince1970: 1_790_000_000))
    func advance(by seconds: TimeInterval) async         // moves now and uptime; wakes due sleepers in order, yielding between them
    func waitForSleepers(count: Int = 1) async           // returns when that many sleeps are waiting
    var sleeperCount: Int { get }
}

@MainActor final class FakeTranscriber: SpeechTranscriber {
    init(provider: SpeechProvider)
    var startResult: Result<Void, TranscriberFailure>    // default success
    var finalizeResult: Result<Void, TranscriberFailure>
    var transcribePendingResult: Result<Void, TranscriberFailure>
    var pendingSeconds: TimeInterval
    var segmentsOnFinalize: [String]                     // emitted as segments before finalize returns, then cleared
    var segmentsOnTranscribePending: [String]            // the same for transcribePending
    private(set) var calls: [String]                     // "start", "finalize", "transcribePending", "shutdown"
    private(set) var starts: [TranscriberStart]
    func emit(_ kind: TranscriberEvent.Kind)             // with the generation of the last start
    func emit(_ kind: TranscriberEvent.Kind, generation: UUID)
    var holdStart: Bool                                  // true: start suspends until `releaseStart()`
    func releaseStart()
}
@MainActor final class FakeTranscriberFactory: SpeechTranscriberFactory {
    init()
    private(set) var made: [FakeTranscriber]
    var configure: ((FakeTranscriber) -> Void)?
}
actor FakeMetaKeyTester: MetaKeyTesting {                // in FakeTranscriber.swift
    init()
    func setResult(_ result: Result<Void, MetaSpeechError>)   // default success
    func setHold(_ hold: Bool)                           // true: test(key:) suspends until `release()`
    func release()
    var testedKeys: [String] { get }
}

actor FakeAnswerService: AnswerService, OpenRouterKeyVerifying {
    init(provider: AssistantProvider)
    func setCatalogue(_ result: Result<[AssistantModelOption], any Error>)   // default: empty
    /// Default, when never set: fixed texts and an `execution` that echoes the request's selection.
    func setAnswer(_ result: Result<AssistResponse, any Error>, progress: [String])
    func setSummary(_ result: Result<SummaryResponse, any Error>)            // default: a fixed summary
    func setVerify(_ result: Result<Void, any Error>)                        // default success
    func setHoldAnswer(_ hold: Bool)                     // answer suspends, after its progress calls, until released or cancelled
    func releaseAnswer()
    func setHoldSummary(_ hold: Bool)
    func releaseSummary()
    func waitForCatalogueCalls(count: Int) async
    func waitForAnswerRequests(count: Int) async
    func waitForSummaryRequests(count: Int) async
    var catalogueCalls: Int { get }
    var answerRequests: [AssistRequest] { get }
    var summaryRequests: [SummaryRequest] { get }
    var cancelledIDs: [UUID] { get }                     // `cancel(requestId:)` also ends the held request of that id with CancellationError
    var verifiedKeys: [String] { get }
}
struct FakeAnswerServices: AnswerServiceProviding { init(codex: FakeAnswerService, openRouter: FakeAnswerService) }

/// Defaults: health, verify, models (empty) and the three PC model calls succeed; assist and summarize
/// fail with L-NETWORK. Names, here and in `calls`, are the method names: "health", "verifyPairing",
/// "models", "localModelStatus", "startLocalModel", "stopLocalModel", "assist", "summarize", "cancel".
actor FakeRelayGateway: RelayGateway {
    init()
    func setHealth(_ result: Result<RelayHealth, RelayFailure>)
    func setVerify(_ result: Result<Void, RelayFailure>)
    func setModels(_ result: Result<[AssistantModelOption], RelayFailure>)
    func setStatus(_ result: Result<PCModelStatus, RelayFailure>)
    func setStart(_ result: Result<PCModelStatus, RelayFailure>)
    func setStop(_ result: Result<PCModelStatus, RelayFailure>)
    func setAssist(_ result: Result<AssistResponse, RelayFailure>)
    func setSummary(_ result: Result<SummaryResponse, RelayFailure>)
    func setHold(_ hold: Bool, for name: String)         // calls of that name suspend until `release` or cancellation
    func release(_ name: String)
    func waitForCalls(count: Int) async
    var calls: [String] { get }                          // method names in call order
    var pairings: [PairingPayload] { get }
    var modelRequests: [SpeechProvider] { get }          // the provider of every start and stop
    var assistRequests: [AssistRequest] { get }
    var summaryRequests: [SummaryRequest] { get }
    var cancelledIDs: [UUID] { get }
}

@MainActor final class FakePreferences: PreferencesStoring { init(); private(set) var remembered: [AssistantProvider: AssistantConfiguration]; private(set) var resetCount: Int }
final class FakeSecrets: SecretStoring, @unchecked Sendable { init(_ values: [SecretKind: String] = [:]); var failStatus: Int32? }
final class FakePairingStore: PairingStoring, @unchecked Sendable {
    init(_ pairing: PairingPayload? = nil)
    var saveFails: Bool
    var saved: [PairingPayload] { get }
    var removeKeychainCopyCalls: Int { get }
}
struct FakePermissions: PermissionProviding { init(microphone: Bool = true, camera: Bool = true) }
@MainActor final class FakeClipboard: ClipboardWriting { init(); private(set) var copies: [String] }

@MainActor final class FakeAudioCapture: AudioCapturing {    // unbounded streams: a test sends `.overflow` itself
    init()
    var startFailure: AudioCaptureFailure?
    func feed(_ chunk: AudioChunk)
    func send(_ event: AudioCaptureEvent)
    private(set) var requests: [AudioCaptureRequest]
    private(set) var stops: [Bool]                       // the `releaseSession` argument of each stop
}

struct FakeEngineFailure: Error, Equatable {}
actor FakeGate { init(); func wait() async; func open() }    // `wait()` suspends until `open()` was called
actor FakeBatchEngine: BatchRecognizing {
    init()
    func setResult(_ result: Result<BatchRecognition, any Error>)   // default: no words, duration of the samples at 16 kHz
    func setGate(_ gate: FakeGate?)                      // transcribe records the call, then waits for the gate
    var calls: [Int] { get }                             // the sample count of every transcribe
}
actor FakeStreamingEngine: StreamingRecognizing {
    init()
    func emitPartial(_ text: String)
    func setFinal(_ text: String)
    func setProcessError(_ fails: Bool)                  // process throws FakeEngineFailure
    func setFinishError(_ fails: Bool)
    var processed: Int { get }                           // calls of process
    var processedSamples: Int { get }
    var resetCount: Int { get }
    var finishCount: Int { get }
    var cleanupCount: Int { get }
}
/// Reports `steps`, waits for `gate` when there is one, then throws `failure` or returns the engine.
struct FakeEngineLoader: OnDeviceEngineLoading {
    init(voz: FakeBatchEngine, parakeet: FakeStreamingEngine, steps: [ModelPreparationProgress] = [],
         failure: (any Error)? = nil, gate: FakeGate? = nil)
}

/// Builds every state owner on fakes, with the same wiring as `AppEnvironment.init`.
@MainActor final class TestOwners {
    let clock: ManualClock
    let preferences: FakePreferences
    let secrets: FakeSecrets
    let pairingStore: FakePairingStore
    let sessions: InMemorySessionStore
    let gateway: FakeRelayGateway
    let codex: FakeAnswerService
    let openRouter: FakeAnswerService
    let keyTester: FakeMetaKeyTester
    let transcribers: FakeTranscriberFactory
    let vozEngine: FakeBatchEngine
    let parakeetEngine: FakeStreamingEngine
    let clipboard: FakeClipboard
    let app: AppModel
    let activity: SessionActivity
    let toasts: ToastCenter
    let speech: SpeechSettings
    let assistant: AssistantSettings
    let models: OnDeviceModelManager
    let relay: RelayMonitor
    let pairing: PairingController
    let history: HistoryModel
    let readiness: SystemReadiness
    let session: SessionController
    init(paired: Bool = true, provider: SpeechProvider = .meta, answers: AssistantProvider = .codex,
         metaKey: String? = "fixture-meta-key", microphoneGranted: Bool = true)
}
```

`Error` is `Sendable`, so a `Result<…, any Error>` crosses into the fake actors without complaint. The
fakes offer waits (`waitFor…`) and holds (`setHold…`, `FakeGate`) so that a test never has to sleep or
guess at timing: it waits for the call it expects, then releases it. A test that needs a state owner built
differently from `TestOwners` builds that one owner itself from the same fakes.

### 3.3 Design-system surface (WP-05)

Views contain no literal colour, size, radius or font. They read `@Environment(\.theme)` and use the
components below. Glass is used for chrome only (DS 3.1): the tab bar, navigation bars, the Start control
and the live control cluster.

#### 3.3.1 Theme tokens (DS section 2)

```swift
struct Theme: Sendable {
    var ground: Ground
    var surface: Surface
    var stroke: Stroke
    var sheen: Sheen
    var ink: Ink
    var accent: Accent
    var status: Status
    var track: Track
    var radius: Radius
    var space: Space
    var size: Size
    var shadow: Shadows
    var motion: Motion
    var opacity: Opacity

    struct Bloom: Sendable { var color: Color; var center: UnitPoint; var radius: CGFloat }
    struct Ground: Sendable { var base: Color; var blooms: [Bloom]; var bloomFadeEnd: CGFloat }
    struct Surface: Sendable {
        var panel: Color, inset: Color, raised: Color, raisedPressed: Color
        var floating: Color, floatingSolid: Color, overlay: Color, inverse: Color, scrim: Color, qr: Color
    }
    struct Stroke: Sendable {
        var edgeTop: Color, edgeBottom: Color, raised: Color, inset: Color, onAccent: Color
        var hairline: Color, control: Color, danger: Color, focus: Color
    }
    struct Sheen: Sendable { var top: Color; var fadeEnd: CGFloat }
    struct Ink: Sendable { var primary: Color, secondary: Color, disabled: Color, onAccent: Color, onInverse: Color }
    struct Accent: Sendable { var fill: Color, pressed: Color, strong: Color, tint: Color }
    struct Status: Sendable {
        var live: Color, success: Color, warning: Color, danger: Color, offline: Color
        var liveTint: Color, successTint: Color, warningTint: Color, dangerTint: Color, neutralTint: Color
        func color(for tone: StatusTone) -> Color
        func tint(for tone: StatusTone) -> Color
    }
    struct Track: Sendable { var progress: Color }
    struct Radius: Sendable {
        var panel: CGFloat, compactPanel: CGFloat, sheet: CGFloat, card: CGFloat, field: CGFloat, tile: CGFloat, small: CGFloat
    }   // 30, 26, 32, 22, 16, 12, 8
    struct Space: Sendable {
        var half: CGFloat, s1: CGFloat, s2: CGFloat, s3: CGFloat, s4: CGFloat, s5: CGFloat, s6: CGFloat, s8: CGFloat
        var screenInset: CGFloat, panelInset: CGFloat, cardInset: CGFloat, panelGap: CGFloat, groupGap: CGFloat
        var inlineGap: CGFloat, controlGap: CGFloat
    }   // 2, 4, 8, 12, 16, 20, 24, 32; 16, 16, 16, 12, 12, 8, 12
    struct Size: Sendable {
        var minimumTarget: CGFloat, largeButton: CGFloat, regularButton: CGFloat, sessionControl: CGFloat
        var clusterCircle: CGFloat, row: CGFloat, twoLineRow: CGFloat, field: CGFloat, chip: CGFloat, iconTile: CGFloat
        var compactHeightThreshold: CGFloat
    }   // 44, 52, 44, 64, 52, 52, 64, 48, 28, 36; 740
    struct Shadow: Sendable { var color: Color; var radius: CGFloat; var y: CGFloat }
    struct Shadows: Sendable { var panel: Shadow, raised: Shadow, floating: Shadow, accent: Shadow }
    struct Motion: Sendable {
        var press: Animation, fast: Animation, base: Animation, emphasis: Animation, stream: Animation
        var pulseSeconds: Double, pressedScale: CGFloat
    }
    struct Opacity: Sendable { var disabled: Double, caretLow: Double }

    static let daylight: Theme                           // Theme+Daylight.swift: the values of DS section 2
    /// DS 6.3 and 6.4: solid twins, no sheen, the strong colour set, visible outlines.
    func resolved(reduceTransparency: Bool, increaseContrast: Bool) -> Theme
}

extension EnvironmentValues { @Entry var theme: Theme = .daylight }
```

The root view reads `accessibilityReduceTransparency` and `colorSchemeContrast` and injects
`Theme.daylight.resolved(...)`. Members already present in the CI spike's `Theme.swift` keep their names
and values; the wrapper's fallback drawing reads them.

```swift
// DesignSystem/DesignTypes.swift
enum TextRole: CaseIterable, Sendable {
    case display, title, headline, cue, transcript, body, label, caption, micro, mono
    var font: Font { get }                               // DS 2.10: Dynamic Type text styles only (IOS-B14)
}
enum StatusTone: Equatable, Sendable { case live, success, warning, danger, neutral }
enum ChipMark: Equatable, Sendable { case dot, pulsingDot, check, pause, alert, ring, spinner, none }
enum BannerTone: Equatable, Sendable { case info, success, warning, danger }
enum LCButtonKind: Equatable, Sendable { case primary, secondary, destructive, quiet }
enum LCButtonSize: Equatable, Sendable { case large, regular }
enum LevelMeterState: Equatable, Sendable { case listening, idle, paused }
extension View {
    /// Font of the role plus a colour (default `ink.primary`); tabular digits when `tabular` is true.
    func lcText(_ role: TextRole, color: Color? = nil, tabular: Bool = false) -> some View
}
```

#### 3.3.2 Glass wrapper (research 1.6; one file)

`DesignSystem/LiquidGlass.swift` is the file of the CI spike, which compiled and ran on the iOS 26.2 and
iOS 18.6 simulators. Its declarations are kept exactly; the three marked "added" are new and follow the
same pattern (one `if #available(iOS 26.0, *)` with the fallback in the other branch).

```swift
enum LiquidGlass { static var isNative: Bool { get } }

extension View {
    /// Chrome-layer glass behind this view. `interactive` only for surfaces that respond to touch.
    func daylightChrome<S: InsettableShape>(in shape: S, interactive: Bool = false) -> some View
    func lcGlassID(_ id: some Hashable & Sendable, in namespace: Namespace.ID) -> some View
    /// Pins the bar above the bottom safe area and insets it 16 from the screen edges: pass the controls
    /// without padding of their own.
    func lcBottomBar<Bar: View>(@ViewBuilder _ bar: () -> Bar) -> some View
    func lcScrollEdge(hard: Bool = false, for edges: Edge.Set) -> some View
    func lcTabBarMinimizesOnScroll() -> some View
    /// Added. iOS 18 to 25: a visible `.ultraThinMaterial` toolbar background for the tab bar. iOS 26: nothing.
    func lcTabBarChrome() -> some View
    /// Added. `.presentationBackground(theme.surface.overlay)`; before iOS 26 also corner radius 32 and a
    /// visible grabber (DS 3.3).
    func lcSheetChrome() -> some View
}

struct LCGlassContainer<Content: View>: View {
    init(spacing: CGFloat? = nil, @ViewBuilder content: () -> Content)
}

enum LCGlassButtonShape: Equatable, Sendable { case capsule, circle }          // added

/// The label colour is set by the button on both paths: pass an unstyled label.
struct LCGlassButton<Label: View>: View {
    /// `shape` is added, with a default that keeps the existing call sites compiling.
    init(prominent: Bool = false, shape: LCGlassButtonShape = .capsule,
         action: @escaping @MainActor () -> Void, @ViewBuilder label: () -> Label)
}

struct LCToolbarFixedSpacer: ToolbarContent {
    var placement: ToolbarItemPlacement              // default .automatic
}
```

The lint `scripts/ci/lint_swift.sh` fails the build if any other Swift file under `ios/` contains
`glassEffect`, `GlassEffectContainer`, `glassEffectID`, `safeAreaBar`, `scrollEdgeEffectStyle`,
`tabBarMinimizeBehavior`, `ToolbarSpacer`, or `.glass` / `.glassProminent` as a whole member name. It is a
text search: a comment or a string counts.

#### 3.3.3 Components

A component that is a single control takes its accessibility identifier from the caller's
`.accessibilityIdentifier(_:)`. A component with inner structure takes an `identifier` parameter and puts it
on the right inner element, because an identifier set on a container spreads to every child.

```swift
// Scaffolding and surfaces
struct GroundView: View { init() }
/// The ground behind everything, then one column 16 from the edges with 12 between children; a
/// `ScrollView` unless `scrolls` is false. System backgrounds are hidden.
struct ScreenScaffold<Content: View>: View {
    init(scrolls: Bool = true, @ViewBuilder content: () -> Content)
}
struct StatusChipModel: Equatable, Sendable {
    let text: String
    let tone: StatusTone
    let mark: ChipMark
    init(_ text: String, tone: StatusTone, mark: ChipMark = .dot)
}
struct Panel<Content: View>: View {                      // DS 4.9; optional header title and trailing chip
    init(_ title: String? = nil, chip: StatusChipModel? = nil, compact: Bool = false, @ViewBuilder content: () -> Content)
}
struct Card<Content: View>: View {
    init(prominent: Bool = false, @ViewBuilder content: () -> Content)
}
struct SectionTitle: View { init(_ text: String) }       // on the ground, above a panel
struct CaptionText: View { init(_ text: String, tone: StatusTone? = nil) }   // `type.caption`; ink.secondary unless a tone is given

// Buttons (DS 4.1 to 4.3), content placement
struct LCButton: View {
    /// Busy: a spinner replaces the icon, the title becomes `busyTitle` when given, input is ignored, width is kept.
    init(_ title: String, systemImage: String? = nil, kind: LCButtonKind = .primary, size: LCButtonSize = .regular,
         fullWidth: Bool = false, isBusy: Bool = false, busyTitle: String? = nil,
         action: @escaping @MainActor () -> Void)
}
struct LCButtonStyle: ButtonStyle {                      // for `NavigationLink` and `Link` labels
    init(kind: LCButtonKind, size: LCButtonSize = .regular, fullWidth: Bool = false)
}
struct IconButton: View {
    enum Style: Equatable, Sendable { case raised, quiet, chrome }
    init(systemImage: String, label: String, style: Style = .quiet, isBusy: Bool = false,
         action: @escaping @MainActor () -> Void)
}

// Rows (DS 4.8)
/// A row that pushes `route`. Title and value form the link's accessibility label and value. The subtitle
/// is laid over the link without hit testing and stays its own accessibility element, so an identifier
/// set inside it is reachable by UI tests. `identifier` goes on the link.
struct LCNavigationRow<Subtitle: View>: View {
    init(route: Route, title: String, value: String? = nil, valueTone: StatusTone? = nil, systemImage: String? = nil,
         identifier: String? = nil, @ViewBuilder subtitle: () -> Subtitle)
}
extension LCNavigationRow where Subtitle == EmptyView {
    init(route: Route, title: String, value: String? = nil, valueTone: StatusTone? = nil, systemImage: String? = nil,
         identifier: String? = nil)
}
struct ChoiceRow: View {                                 // one Button; selected trait and a trailing check
    init(title: String, subtitle: String? = nil, isSelected: Bool, action: @escaping @MainActor () -> Void)
}
struct DetailRow: View {                                 // key left, value right, tabular
    init(_ key: String, value: String, valueTone: StatusTone? = nil)
}

// Status (DS 4.11 to 4.13)
struct StatusChip: View {                                // one accessibility element whose label is the text
    init(_ model: StatusChipModel, raised: Bool = false)
    init(_ text: String, tone: StatusTone, mark: ChipMark = .dot, raised: Bool = false)
}
struct Banner: View {                                    // one accessibility element: title, then message
    init(tone: BannerTone, title: String, message: String)
}
@MainActor @Observable final class ToastCenter {
    private(set) var message: String?
    init(clock: any AppClock)
    func show(_ text: String)                            // visible for 4 s; a new toast replaces the current one
}
struct ToastOverlay: View { init() }                     // reads `ToastCenter` from the environment; placed by RootView
struct Spinner: View { init(size: CGFloat = 20) }
struct LCProgressBar: View { init(value: Double, failed: Bool = false) }   // value 0...1

// Fields (DS 4.6, 4.7); `identifier` goes on the text field itself
struct LCTextField: View {
    init(_ label: String, text: Binding<String>, placeholder: String = "", lines: ClosedRange<Int>? = nil,
         monospaced: Bool = false, isURL: Bool = false, identifier: String? = nil)
}
struct LCSecureField: View {                             // reveal button; never autocapitalised or autocorrected
    init(_ label: String, text: Binding<String>, placeholder: String, identifier: String? = nil)
}

// Other
struct EmptyStateView: View { init(systemImage: String, title: String, message: String) }
/// `identifier` and the two labels go on the header button.
struct DisclosureRow<Content: View>: View {
    init(_ title: String, isExpanded: Binding<Bool>, identifier: String? = nil,
         collapsedLabel: String? = nil, expandedLabel: String? = nil, @ViewBuilder content: () -> Content)
}
struct LevelMeter: View { init(level: Float, state: LevelMeterState) }    // size S of DS 4.21; hidden from accessibility
struct TranscriptLine: View { init(_ text: String, isInterim: Bool = false, showsCaret: Bool = false) }
struct LiveCaret: View { init(paused: Bool = false) }
struct BrandMark: View { init(height: CGFloat) }         // 68, or 48 in compact height
struct BrandName: View { init(role: TextRole = .display) }   // "Live" in ink, "Cue" in the accent, one Text

// Chrome controls (DS 4.17); these two use the glass wrapper
struct SessionStartControl: View {                       // 64 high accent capsule, floating above the tab bar
    init(title: String, systemImage: String, isBusy: Bool = false, action: @escaping @MainActor () -> Void)
}
/// The live control cluster. It carries its own identifiers and labels: `pause-session` ("Pause" /
/// "Resume"), `assist-button` ("Assist"; busy "Replying…"), `end-session` ("Stop conversation").
/// All three are disabled while `isAssisting` or `isTransitioning`; Stop shows a spinner while transitioning.
struct SessionControlCluster: View {
    init(isPaused: Bool, isAssisting: Bool, isTransitioning: Bool,
         onPauseResume: @escaping @MainActor () -> Void,
         onAssist: @escaping @MainActor () -> Void,
         onStop: @escaping @MainActor () -> Void)
}

// Haptics (DS 7.2)
enum Haptic: Equatable, Sendable {
    case listeningStarted, listeningStopped, pauseToggled, answerArrived, failure, warning, success
}
extension View {
    /// `sensoryFeedback` for the event whenever `trigger` changes.
    func haptic<T: Equatable>(_ haptic: Haptic, trigger: T) -> some View
}
```

What the components do by themselves, so that a screen does not do it again (WP-05):

- `Panel`, `Card` and an open `DisclosureRow` lay out their own children. Adjacent `LCNavigationRow`s and
  `ChoiceRow`s touch and get a hairline between them; adjacent `DetailRow`s touch; a `DisclosureRow` gets a
  hairline above it unless it comes first; everything else sits 12 apart. List the children and add no
  dividers or spacing. Children wrapped in a stack of the screen's own count as one child.
- `ScreenScaffold` sets `ink.primary` as the default text colour, hides system backgrounds, applies
  `lcTabBarChrome()` (a bar's background is read from the tab's content, so the call on the tab view in
  `RootView` is not enough alone) and, on iOS 18, fades its content out above an `lcBottomBar`.
- `Spinner` and every icon take the colour of the text around them (`foregroundStyle`).
- A busy `LCButton`, `IconButton` or `SessionStartControl` ignores input and keeps its full appearance;
  `disabled` is for controls that are unavailable.
- An `LCTextField` or `LCSecureField` with an empty label draws no label and reads its placeholder to
  assistive technology. A secure field's accessibility value is "Hidden" or "Shown", never its text.
- Component sizes that a screen passes in are tokens too: `theme.component.brandMark` (68) and
  `theme.component.compactBrandMark` (48).

### 3.4 Concurrency rule of each contract, in one place

| Contract | Kind | Isolation rule |
|---|---|---|
| Domain model, `UsageUpdate`, `JSONValue`, `RelayFailure`, error enums, `MetaEvent`, frames, statuses | Value types | `Sendable`; no isolation |
| `LiveCueJSON`, `ContextBuilder`, `WireBudget`, `RelayMessages`, `MetaRealtimeAPI`, `WordErrorRate`, `RelayConstants` | Caseless enums of static functions | Pure; callable from anywhere |
| `StreamTranscript`, `PCMFramer`, `PendingAudioBuffer`, `RelaySpeechNegotiation`, `OpenRouterStreamParser` | Mutating value types | `Sendable`; owned by one actor or task at a time |
| `HTTPTransport`, `WebSocketTransport`, `WebSocketConnection` | Protocols | `Sendable`; `async` methods |
| `RelayClient`, `OpenRouterClient`, `CoreTiming` | Structs | `Sendable`; stateless |
| `SpeechStreamSession` | Actor | `events` is `nonisolated` |
| `URLSessionHTTPTransport`, `URLSessionWebSocketTransport`, `KeychainStore`, `KeychainSecretStore`, `FilePairingStore`, `SystemPermissions`, `SystemClock`, engines, loaders | Structs | `Sendable` |
| `SessionStoring`, `SecretStoring`, `PairingStoring`, `RelayGateway`, `AnswerService`, `AnswerServiceProviding`, `PermissionProviding`, `AppClock`, `BatchRecognizing`, `StreamingRecognizing`, `OnDeviceEngineLoading`, `MetaKeyTesting`, `OpenRouterKeyVerifying` | Protocols | `Sendable` |
| `InMemorySessionStore`, `FileSessionStore`, `CodexRelayAnswerService`, `OpenRouterAnswerService`, fixture services and gateway | Actors | Synchronous protocol requirements are `nonisolated` |
| `PreferencesStoring`, `SpeechTranscriber`, `SpeechTranscriberFactory`, `AudioCapturing` | Protocols | `@MainActor`, class-bound |
| `TranscriberEventChannel`, `TranscriberEventPump`, `SessionPersistence`, the four transcribers, `AudioCapture`, `SystemReadiness`, `LiveTranscriberFactory` | Classes | `@MainActor`, not observable |
| `AppEnvironment`, `AppModel`, `SessionActivity`, `SessionController`, `RelayMonitor`, `PairingController`, `AssistantSettings`, `SpeechSettings`, `HistoryModel`, `OnDeviceModelManager`, `ToastCenter`, every view model | Classes | `@MainActor @Observable final` |
| `AppDependencies`, `SessionDependencies` | Structs | `@MainActor` |
| `AudioTapConverter`, URL session delegate classes | Classes | `@unchecked Sendable`; the only ones in the app |
| Views, components, `Theme` | Structs | Views are main-actor by `View`; `Theme` is `Sendable` |

---

## 4. Behavioural design

### 4.1 Session state machine

#### 4.1.1 Transition table

`g` is the generation of the current transcriber start. "Barrier" means
`await pump.waitUntilCaughtUp()`: every event the finished call emitted has been applied. "Save" means
`persistence.schedule(session)` after copying the usage into the session, and happens only once the first
save has been made. Guards are checked synchronously on entry, and the phase is changed before the first
`await`, which is what makes every intent single-flight.

| Id | State | Event | Guard | Action | Next |
|---|---|---|---|---|---|
| T-01 | `idle` | `start()` | none | Run the start checks (4.1.2). | `preflight` |
| T-02 | `preflight` | a check fails | | `app.presentAlert(message)`. Nothing was created. | `idle` |
| T-03 | `preflight` | all checks pass | | Create the session in memory (`startedAt = clock.now()`, title "New conversation"); new usage for the provider; clear partial, level, feed error, streamed text, retry memory, notice; elapsed 0; `activity.isActive = true`; `app.sessionDidStart()`; make the transcriber and its pump; start the tick; status "Connecting to `<display name>`…"; new `g`; `transcriber.start(g, offset 0)`. | `connecting` |
| T-04 | `connecting` | start returns | | Barrier. **First save.** `listeningStartedCount += 1`. | `listening` |
| T-05 | `connecting` | start throws `f` | | `transcriber.shutdown()`; stop pump and tick; discard the session (nothing was saved); `activity.isActive = false`; `app.sessionDidEnd()`; alert "Transcription could not start. " + `f.message`. | `idle` |
| T-06 | `listening` | `togglePause()` | not assisting | Status "Finishing the transcript…"; `try transcriber.finalize()`; barrier. | `pausing`, then T-07 or T-08 |
| T-07 | `pausing` | finalize returned | | Retire `g`; level 0; save. | `paused` |
| T-08 | `pausing` | finalize threw | | As T-07, plus notice (kind `finalizeFailed`, "Could not finalize the transcript. Some final words may be missing."). | `paused` |
| T-09 | `listening` | event `failed(f)` from a streaming provider | event generation is `g` | Retire `g` (the transcriber has already torn itself down and marked its usage stream interrupted); level 0; `pausedAutomatically = true`; notice (kind `recordingPaused`, `f.message`); status "Disconnected · resume to retry"; save. A running Assist continues. | `paused` |
| T-10 | `listening` | event `failed(f)` from an on-device provider | event generation is `g` | Phase `pausing`; `try? transcriber.finalize()` so recognised text is kept; barrier; then exactly as T-09. | `paused` |
| T-11 | `paused` | `togglePause()` | not assisting | Clear the partial text; offset = `elapsedSeconds`; new `g`; status "Connecting to `<display name>`…"; `transcriber.start(g, offset)`. | `resuming`, then T-12 or T-13 |
| T-12 | `resuming` | start returns | | Barrier; notice nil; `pausedAutomatically = false`; `listeningStartedCount += 1`; save. | `listening` |
| T-13 | `resuming` | start throws `f` | | Retire `g`; notice (kind `recordingPaused`, "Transcription could not start. " + `f.message`); `pausedAutomatically = true`; status "Disconnected · resume to retry"; save. | `paused` |
| T-14 | `listening` | `stop()` | not assisting | Status "Finishing the transcript…"; `transcriber.finalize()`, remembering an error; barrier. | `ending`, then T-16 |
| T-15 | `paused` | `stop()` | not assisting | **No finalisation** (IOS-B7). | `ending`, then T-16 |
| T-16 | `ending` | work done | | `transcriber.shutdown()`; stop pump and tick; `endedAt = clock.now()`; `title = finalTitle()`; copy usage; schedule the save and `await persistence.flush()`; `history.reload()`; clear the session; `activity.isActive = false`; `app.sessionDidEnd()`; if finalisation failed, alert with the finalise text; start the summary (4.4). | `idle` |
| T-17 | any with a session | tick, every 1 s | | `elapsedSeconds = floor(clock.now() − startedAt)`; save. | same |
| T-18 | any phase in which `g` is current (`connecting`, `listening`, `pausing`, `resuming`, `ending`) | event `status`, `partial`, `segment`, `usage`, `lastChunk`, `level` with generation `g` | `level` only in `listening` | Apply (4.1.3). | same |
| T-19 | any | event whose generation is not `g`, or any event after `g` was retired | | Dropped. | same |
| T-20 | any except `listening` | event `failed` | | Ignored; the call in progress reports its own outcome. One case is kept instead of ignored: a `failed` of generation `g` in `connecting` or `resuming` can belong to a start that has already returned (3.2.5, as built). If the start throws, it is forgotten; if the start returned, it is applied right after T-04 or T-12, as T-09 or T-10. | same |
| T-21 | any with a session | `assist()`, `retrySameText()` | not assisting, not transitioning | 4.3. `assisting` is an overlay; the phase does not change. | same |
| T-22 | any | the app goes to the background or the phone locks | | Nothing. With an active audio session the process keeps running (background mode `audio`); the tick, capture and polling continue. | same |
| T-23 | any | the app becomes active | | `relay.appBecameActive()`. The next tick recomputes the elapsed time from the wall clock. | same |
| T-24 | launch | `bootstrap()` | | `sessions.closeInterruptedSessions()`, then `history.reload()`: a session cut off by a kill is closed at its last save, titled by the normal rule, with its open usage streams interrupted. | `idle` |

Interruptions and route changes are not separate rows: `AudioCapture` reports them to the transcriber, which
turns them into `failed` (T-09 or T-10), for every provider (IOS-B5). While `paused`, capture is stopped, so
neither can arrive. Start, Assist and summaries cannot be cancelled by the user.

Chip and control state by phase (screens 4.2):

| Phase | Chip | Level meter | Cluster |
|---|---|---|---|
| `connecting`, `resuming` | live, spinner, "Connecting…" | idle | all disabled; Stop shows a spinner |
| `listening` | live, pulsing dot, "Listening" | listening | enabled unless assisting |
| `pausing` | warning, spinner, "Pausing…" | paused | all disabled; Stop shows a spinner |
| `paused` | warning, pause glyph, "Paused" | paused | leading control is Resume; enabled unless assisting |
| `ending` | neutral, spinner, "Stopping…" | paused | all disabled; Stop shows a spinner |

#### 4.1.2 Start checks, in order

| # | Applies to | Check | Message on failure |
|---|---|---|---|
| 1 | Meta | `secrets.read(.metaAPIKey)` is not nil | `MetaSpeechError.missingKey` text |
| 2 | PC-local | `await relay.refreshPCModel()`, then the status is ready for the provider, not busy, and no command is in flight | "PC speech model: `<relay.homeLabel(for:)>`. Open Transcription to start it or check its status." |
| 3 | PC-local | A pairing exists; the relay is online, after one `relay.checkHealthNow()` if it was not | `L-NOT-PAIRED` text; else `relay.connectionMessage`, or the `L-UNREACHABLE` text when there is none |
| 4 | All | `await permissions.requestMicrophone()` | The microphone-denied text of 1.3 |

There is no check for a missing on-device model (IOS-D10): the Home control is the download link in that
case, and the transcriber's own "Prepare Voz first." or "Prepare Parakeet first." surfaces through T-05.
Start is not gated on readiness (IOS-A3): a missing answer-service prerequisite is reported by the first
Assist.

#### 4.1.3 Applying events

| Event | Effect |
|---|---|
| `status(s)` | `transcriberStatus = s` |
| `partial(t)` | `partialText = t` |
| `segment(s)` | Append to `session.segments`; for a streaming provider, sort stably by `startSeconds`; save |
| `level(v)` | `level = v`, only in `listening` |
| `usage(u)` | `usage.apply(u)` |
| `lastChunk(ms)` | Remembered for the next Assist's timing record |
| `failed(f)` | T-09, T-10 or T-20 |

### 4.2 Transcribers

Common rules: one object per session; every start gets the generation it is given and tags its events with
it; `failed` is the last event of its generation; nothing reconnects or falls back; audio is never written
to disk.

| | `MetaTranscriber` | `RelayTranscriber` | `VozTranscriber` | `ParakeetTranscriber` |
|---|---|---|---|---|
| Needs | Meta key from `SecretStoring`, read in `start` | Pairing from `PairingStoring`, read in `start` | A loaded `BatchRecognizing` | A loaded `StreamingRecognizing` |
| `start` | Status "Connecting to …"; `SpeechStreamSession(.meta(key:))`; `session.start()` (30 s); then `audio.start(pcm16 24,000, queue 64)`; status "Meta direct · 0.00 s setup" | Same with `.relay(pairing:model:)` (limit C-59); `loading` gives "Loading `<short name>` on PC · `N` s. Microphone starts when ready."; then `audio.start(pcm16 16,000, queue 64)`; status "Connected · 0.00 s setup" | `audio.start(float32 16,000, queue 128)` at once; reset the pending buffer; status "Recording on this iPhone" | `engine.reset()`; set the partial handler; `audio.start(float32 16,000, queue 128)`; status "Listening" |
| Start failures (`TranscriberFailure.message`) | Missing or malformed key, not accepted, could not connect (`MetaSpeechError` texts); "No usable microphone." | `RelayMessages.text` of the failure (`L-NOT-PAIRED` without a pairing); "No usable microphone." | "Prepare Voz first."; "No usable microphone." | "Prepare Parakeet first."; "No usable microphone." |
| While running | Capture chunks go to `session.send(audio:)` in order. Session events map one to one: status texts, `partial`, `segment` (plus the stream offset), `usage`. First text gives "First words · 0.00 s from stream start". | Same | Each chunk is appended to `PendingAudioBuffer` | Each chunk goes to `engine.process(samples:)`; its duration gives `lastChunk` and "Last buffer processing: `N` ms"; the handler's text gives `partial` |
| Failures after start | Session `failed` (its message); capture `overflow`: "Network cannot keep up. Recording paused; resume to retry."; `interrupted` and `routeLost`: the two microphone texts. On any of them: stop capture releasing the audio session, `session.abort()`, wait for the session's events to finish, status "Disconnected · resume to retry", then emit `failed`. | Same | Buffer full: the Voz cap text of 1.3. Capture `overflow`: the on-device overflow text. `interrupted`, `routeLost`: the microphone texts. On any of them: stop capture keeping the audio session, emit `failed` once. | Processing error: the on-device processing text. Capture events as for Voz. Same handling. |
| `finalize` | Stop capture releasing the audio session; wait for queued chunks to be sent; `session.finish()`; wait for its events to finish; when the ending is `interrupted`, throw with the finalise text. A known finalisation time appends " · Final 0.00 s" to the status. | Same | Stop capture keeping the audio session; wait for queued chunks; `transcribePending()`; status "Paused" | Stop capture keeping the audio session; wait for queued chunks; `engine.finish()`; a non-empty trimmed text becomes one segment (start 0, end 0); partial ""; status "Paused" |
| `transcribePending` | 0 | 0 | Snapshot the buffer; nothing new returns 0. Status "Transcribing…"; `engine.transcribe`; a result for an older generation is discarded; `buffer.complete` gives the new text, emitted as one segment (start 0, end = duration); status "`0.0` s audio → `0.00` s transcription". An engine error throws with the on-device processing text. | 0 |
| `shutdown` | Stop capture releasing the audio session; `session.abort()` | Same | Stop capture releasing the audio session; reset the buffer | Stop capture releasing the audio session |

On-device transcribers also call `models.reportStatus(_:)` with each status line, and "Ready" from
`shutdown`, so Model Library shows it.

### 4.3 Assist pipeline

Triggers: the Assist button (fresh), "Retry same text" and "Retry same text with selected model" (retry).
A retry with nothing remembered runs as fresh. All clocks are `clock.uptime()`.

| Step | Fresh request | Codex | OpenRouter |
|---|---|---|---|
| 1 | Clear `assistError` and `streamingAnswer`. | | |
| 2 | Precondition, reported on the card; the Assist does not start. | No pairing: `L-NOT-PAIRED` text | No key: `OpenRouterError.missingKey` text |
| 3 | `isAssisting = true`, `activity.isAssisting = true`. `t0` = now. Snapshot `assistant.configuration`, the session id and `assistant.instruction`. | | |
| 4 | Stage "Transcribing…" for Voz, else "Thinking…". Voz: `transcriptionMs = transcriber.transcribePending() × 1000`, then barrier. Skipped on a retry. | | |
| 5 | `request = ContextBuilder.assistRequest(session:partial:instruction:)`; when `!hasContent(request)`, fail with "No speech was recognized yet." `marker = session.segments.last?.id`. On a retry: `request = ContextBuilder.retry(remembered, assistant: snapshot)`; the marker is not touched. | | |
| 6 | Stage "Checking model…". | `catalogue = service.loadCatalogue()`, every time | `catalogue = assistant.catalogue`; when empty, `service.loadCatalogue()` and `assistant.adoptCatalogue(_:)`; a failed fetch fails the Assist with that failure's text (IOS-B9) |
| 7 | `catalogue.supports(snapshot)`, else fail. The selection is never changed. | "`<model>` / `<effort>` is not in this PC's current catalog. Open Assistant models & timing and choose an available combination. Your selection has not changed." | `OpenRouterError.noModel` text |
| 8 | `contextMs` = now − `t0` − `transcriptionMs`. Stage "Thinking…". `request.assistant = snapshot`; `request = WireBudget.fitted(request)`; remember `request` for retries and, on a fresh request, remember `marker`. | | |
| 9 | `response = service.answer(request, onProgress:)`. `roundTripMs` = its duration. | One reply. The relay client waits 70 s, checks the echo and sends the cancel itself (3.1.5). | Each progress call, if the session is still the same: `streamingAnswer = text`, stage "Replying…", and `answerArrivedCount += 1` on the first one. |
| 10 | Success, and the session is still the one of step 3: build the turn (`request` = the instruction sent, detected question, answer, details, performance); append it; `memory = response.memory` with `throughSegmentId` = the remembered marker (both services); `latestTurnID = turn.id`; clear `streamingAnswer`; `canRetry = true`; save. | | |
| 11 | Success for a session that ended or changed: dropped silently. | | |
| 12 | Failure at any step after 3: `assistError = AnswerErrorText.message(for:provider:)`; `assistFailedCount += 1`; streamed text stays; nothing is saved; memory and marker are unchanged. A cancellation shows nothing. | | |
| 13 | Always: `isAssisting = false`, `activity.isAssisting = false`, stage nil. | | |

Performance record: `configuration` = the snapshot; `speechModel` = the session provider's raw value;
`reusedText` = retry; `transcriptionMs` (0 unless Voz and fresh); `contextMs`; `roundTripMs`;
`totalMs` = now − `t0`; `lastLiveChunkMs` for Parakeet only; `transcriptCharacters` = characters of the
transcript plus the partial; `execution` = the response's.

Marker ownership (CORE-A1): the marker is the id of the session's last segment at step 5 of a fresh
request. It is applied at step 10 for both services. A reply never supplies it.

`canRetry` is true once step 8 has remembered a request in this session, also when that request failed.

### 4.4 End-of-session summary

Started at the end of T-16, in a task the controller keeps by session id, when all of these hold: the answer
service selected at Stop is Codex; a pairing exists; the session has at least one segment
(`ContextBuilder.summaryRequest` returned a request). The request is fitted and sent through
`service.summarize`. On success, if `sessions.contains(id:)`: the stored session's title becomes the reply's
title, its notes become the reply's summary, key points and action items; save; `history.reload()`. Any
failure is silent and final. `cancelSummary(forSession:)` calls `service.cancel(requestId:)` and forgets the
task; the relay client then sends the one `DELETE`.

### 4.5 Audio pipeline per provider

| | Meta | PC speech | Voz, Parakeet |
|---|---|---|---|
| Target format | PCM16 mono 24,000 Hz | PCM16 mono 16,000 Hz | Float32 mono 16,000 Hz |
| Tap | Hardware format, 2,048 frames requested | same | same |
| Hand-off queue | 64 converted buffers | 64 | 128 |
| Queue overflow | Stream failure, session pauses | same | Failure, session pauses after finalising (R-07) |
| Framing | `PCMFramer(3,840)` inside the session; remainder as one shorter frame | `PCMFramer(2,560)`; same | none |
| Microphone opens | After the acknowledgement | After a valid `ready` | At once |
| On Pause | Audio session released | released | kept active |
| On Stop | Audio session released, notifying others | same | same |
| Level | RMS of each converted buffer, at most 24 per second | same | same |
| Interruption began, input device unavailable | Failure with the microphone text | same | same |

### 4.6 On-device model preparation

`OnDeviceModelManager.prepare(_:)`:

1. Return at once during a session or while a preparation runs.
2. If this variant is loaded (the button reads "Reload"): release its engine (`cleanup()` for Parakeet) and
   clear `loadedVariant`. The files stay cached, so the next step loads from the cache (IOS-B13).
3. Release the other provider's engine: one recogniser in memory at a time.
4. `preparation = ModelPreparation(variant:, fraction: 0, startedAt: clock.now())`; status
   "Downloading and preparing `<display name>`…".
5. Call the loader. Its progress closure hops to the main actor and is ignored unless it belongs to this
   preparation. Status and counter by progress:

| Model | Progress | Status | Counter |
|---|---|---|---|
| Voz | fraction below 1 | "Downloading Voz…" | "`<done>` / `<total>`" with `ByteCountFormatter`, file style |
| Voz | fraction 1 | "Download complete · preparing Neural Engine…" | same |
| Parakeet | checking | "Checking model files…" | none |
| Parakeet | downloading | "Downloading Parakeet…" | "`n` / `m` files" |
| Parakeet | preparing | "Download complete · preparing model…" | last value |

6. Success: keep the engine; `loadedVariant = variant`; status "Ready"; `preparation = nil`.
7. Failure: `preparation.failed = true`, `finishedAt = clock.now()`; status "Preparation failed"; rethrow.
   The Model Library view model raises the alert with the error's `localizedDescription` (E-12).

`unload()` releases both engines and clears `loadedVariant`; status "Prepare a model". It deletes no file.
`providerChanged(to:)` does the same. Nothing is restored at launch (IOS-B1). The engines mark their model
folders excluded from backup after a successful load (3.2.5).

### 4.7 Relay monitoring and PC model polling (protocol 13.3)

Health loop, one task for the life of the app, started by `start()`:

```
repeat:
    if needsPC and a pairing exists: run one health check
    sleep C-61 (5 s)                      # counted from the end of the check
```

Extra checks, each joining a check already in flight instead of starting a second: at `start()`, on
`appBecameActive()`, on `selectionChanged()` when the PC has just become needed, on "Reconnect" and in start
check 3 (`checkHealthNow()`). When the PC is not needed or nothing is paired, the loop sends nothing
(P2-220) and the stored state is left as it was.

| Health result | `isOnline` | `savedPCState` | `connectionMessage` |
|---|---|---|---|
| Success | true | `connected` | "Connected. Your pairing is saved on this iPhone." |
| `UNAUTHORIZED` | false | `needsAttention` | Its text, no prefix |
| Any other failure | false | `reconnecting` | `RelayMessages.savedPairingHealthFailure` |

After every successful check with a PC-local provider selected, one `refreshPCModel()` follows.

PC model loop: runs while the observation count is above zero (Home, Transcription, Settings and PC speech
model call `beginPCModelObservation()` when they appear and `endPCModelObservation()` when they disappear)
and a PC-local provider is selected: `refreshPCModel()`, then sleep C-61 (2 s).

| `refreshPCModel()` | Result |
|---|---|
| Provider is not PC-local, or a command or another refresh is in flight | Nothing |
| Nothing paired | Status nil; message "Pair your PC first." |
| Success | Status = the reply; message = its `message` |
| Failure | Status nil; message = `RelayMessages.pcModelCheckFailure` |

`startPCModel()` and `stopPCModel()`: refused while `isPCModelLocked`; set `isPCModelCommandInFlight`; on
success the reply replaces the status and its `message` the message; on failure only the message changes
(`RelayMessages.text`). The poll then follows the operation.

`homeLabel(for:)`, first match: not paired "Pair PC first"; offline "PC offline"; status nil "Checking…";
otherwise `PCModelStatus.homeLabel(for:)`.

### 4.8 Persistence and launch

| Moment | Write |
|---|---|
| T-04, the transcriber is connected | First save of the session (IOS-B2) |
| A segment is committed; an Assist succeeds; every tick; Pause; Resume; Stop | Save, latest value wins |
| Stop | The save is flushed before History reloads |
| A summary arrives | Save of the stored session, then History reloads |
| Provider, answer selection, instruction change | Preferences, at once |
| Pairing verified | The Keychain item, then the file (3.2.2: the Keychain item is the authority, the file the fallback) |
| API key saved or removed | Keychain only |

Launch order: `LaunchConfiguration.current`; `AppEnvironment.make` (in fixture mode,
`TestSeamLaunchActions.apply` first); the root view appears; its task calls `bootstrap()`:
`closeInterruptedSessions()`, `history.reload()`, `relay.start()`. No permission prompt appears at launch.

### 4.9 Fixture behaviour (architecture 4.11)

| Fixture | Behaviour |
|---|---|
| `FixtureTranscriber.start` | Status "Listening"; for a streaming provider `usage(.begin)`; emits the fixture segment (start = the stream offset, end = offset + 3); starts a 1 s loop that, while started, emits `usage(.update)` adding 1,000 ms sent and processed with `hasTranscript` true, and `level(0.4)`. On-device providers emit no usage. |
| `FixtureTranscriber.finalize` | Stops the loop; streaming: `usage(.finish(completed: true))`; status "Paused". `transcribePending` returns 0. |
| `FixtureAnswerService.answer` | `onProgress(partialAnswer)`; sleep 0.4 s; return the fixed answer with detected question = the fixture segment (Codex) or the sentinel (OpenRouter), the details, a memory, and `execution` echoing the request's selection with the fixture timings; OpenRouter adds the fixture usage. With `assistFailsOnce`, the first call for each session id throws `OpenRouterError.unavailable` after the progress call and the 0.4 s wait. |
| `FixtureAnswerService.loadCatalogue` | The Codex or OpenRouter fixture catalogue; Codex waits 8 s first with `pcCatalogSlow`. `verify(key:)` returns at once. `summarize` returns a fixed summary. |
| `FixtureRelayGateway.health` | Success; with `pcOffline`, `RelayFailure.local(.unreachable)`, which is what a PC that is switched off gives (protocol 13.6 row 5) |
| `FixtureRelayGateway` PC model | Starts `stopped` (MSG-3; MSG-1 is never sent on the wire). Start: `starting` (MSG-6) for 1 s, or 6 s with `pcModelSlowStart`, then `ready` (MSG-4); with `pcModelStartError`, `error` (MSG-8) instead. Stop: `stopping` (MSG-7) for 1 s, then `stopped` (MSG-3). `elapsedSeconds` counts whole seconds of the running operation. |
| `FixtureEngineLoader` | Reports fraction 0.5, sleeps 2 s, returns a fixture engine. |

### 4.10 Timers, timeouts and limits

| What | Value | Source |
|---|---|---|
| Session tick | 1 s | spec 01 section 3.6 |
| Health poll / PC model poll | 5 s / 2 s after the previous check ends | C-61 |
| Relay phone timeouts: health, status, cancel / verify, start, stop / models / assist, summary | 8 s / 15 s / 45 s / 70 s | protocol 6.1, A-21 |
| PC speech connect plus model loading | 180 s | C-59 |
| Finalisation wait after `endStream` (relay and Meta) | 15 s | C-60, IOS-A11 |
| Meta connect plus handshake / key test | 30 s / 15 s | spec 01 section 3.6 |
| OpenRouter key check and catalogue / chat idle / chat total | 20 s / 65 s / 120 s | spec 02 section 4.7 |
| OpenRouter limits: response / SSE line or event / answer text | 8 MiB / 128 KiB / 1 MiB | spec 02 section 4.7 |
| OpenRouter `max_tokens` / instruction in the preference message / memory tail | 4,096 / 4,000 characters / 6,000 characters | spec 02 sections 2.4, 4.4 |
| Largest inbound WebSocket message (relay and Meta) | 131,072 bytes | C-21 |
| Audio tap buffer / frame length | 2,048 frames / 80 ms (3,840 and 2,560 bytes) | architecture 4.6, C-71 |
| Hand-off queue | 64 buffers streaming, 128 on-device | architecture 4.6 |
| Level updates | at most 24 per second | architecture 4.6 |
| Voz pending cap / overlap | 28,800,000 samples (30 min) / 32,000 samples (2 s) | spec 01 section 3.3 |
| Parakeet chunk / end-of-utterance debounce | 320 ms / 640 ms | spec 01 section 4.5 |
| Request body / assist transcript / summary transcript / partial | 126,976 / 80,000 / 100,000 / 16,000 bytes | C-11 to C-14 |
| Instruction / memory summary / memory lists | 4,000 characters and 8,000 bytes / 24,000 bytes / 16 items of 256 bytes | C-15 to C-17 |
| Pairing payload / token | 8,192 bytes / 43 characters | C-18, C-02 |
| Unknown-code relay text shown | 1 to 300 characters | protocol 14 |
| Sanitised Meta transcript | 32,000 characters | spec 02 section 5.4 |
| Session title | first 48 characters | spec 01 section 2.5 |
| Recent comparisons | 50 | spec 01 section 1.6 |
| Toast | 4 s | DS 4.11 |
| Expanded transcript height | min(180, 24% of the screen height) | screens 4.1 |
| Compact layout threshold | usable height under 740 | screens 2 |
| Status line shown in place of the provider line, compact height | 4 s | screens 4.8 |
| Model Library elapsed display | 1 s | spec 01 section 3.6 |
| Fixture: Assist partial / PC model change / slow start / slow catalogue / preparation hold | 0.4 s / 1 s / 6 s / 8 s / 2 s | architecture 4.11 |

---
## 5. Screen wiring

Rules for every screen:

- A screen is a `View` struct with no initialiser arguments (except the two detail screens, which take an
  id). It reads its view model with `@Environment(<ViewModel>.self)` and other owners the same way.
  Two-way fields use `@Bindable var model = model` inside `body`.
- A view model is a `@MainActor @Observable final class` created once by `AppEnvironment`. Its initialiser
  has no side effects. It stores only UI state (drafts, expansion flags); everything else is a computed
  property over the state owners, so Observation tracks through it.
- Layout, copy and states are those of the screens document; this section adds only the wiring. Each
  screen's body is `ScreenScaffold { … }` unless stated.
- Navigation is by value: `NavigationLink(value: Route)` or `LCNavigationRow(route:)`. Each tab's stack and
  the session stack attach one `.navigationDestination(for: Route.self) { RouteDestination(route: $0) }`.
- Picker screens return with `@Environment(\.dismiss)`.

### 5.1 Root (`AppShell/RootView.swift`)

`if session.showsLiveScreen` a `NavigationStack(path: $app.sessionPath)` around `LiveSessionScreen`;
otherwise a `TabView(selection: $app.selectedTab)` with three `Tab`s ("Live" `waveform`, "History" `clock`,
"Settings" `gearshape`), each a `NavigationStack` over its path. On top: the alert
`.alert("LiveCue", isPresented: $app.isAlertPresented) { Button("OK") {} } message: { Text(app.alertMessage ?? "") }`
and `ToastOverlay()`. `.task { await environment.bootstrap() }`;
`.onChange(of: scenePhase)` calls `environment.sceneBecameActive()` on `.active`. The root applies
`.preferredColorScheme(.light)`, the resolved theme, `.tint(theme.accent.strong)` on the tab view and
`.tint(theme.accent.fill)` inside each tab (DS 3.2), `lcTabBarChrome()`.

Contracts: `AppModel`, `SessionController`, `AppEnvironment`, `ToastCenter`. Identifiers: none.

### 5.2 Home (screens 3)

```swift
enum HomeTranscriptionDetail: Equatable {
    case meta(state: String, isReady: Bool)                          // "Key saved · direct" / "Add API key"
    case pcModel(label: String, canStart: Bool, isChanging: Bool)    // the home label of spec 01 section 3.7
    case onDevice(text: String)                                      // the model description, or "Download a model to start"
}
enum HomeStartControl: Equatable {
    case start(isBusy: Bool, isEnabled: Bool, caption: String?)      // caption when a PC model cannot start
    case download                                                    // links to Model Library
}

@MainActor @Observable final class HomeViewModel {
    init(session: SessionController, speech: SpeechSettings, assistant: AssistantSettings, relay: RelayMonitor,
         models: OnDeviceModelManager, readiness: SystemReadiness, history: HistoryModel)
    var captionLine1: String { get }
    var captionLine2: String { get }
    var statusChip: StatusChipModel { get }              // the five rows of screens 3
    var showsFirstRunBanner: Bool { get }                // system not ready and History empty
    var readinessChip: StatusChipModel { get }           // "Ready" (success, check) / "Setup" (warning)
    var transcriptionValue: String { get }
    var transcriptionValueNeedsAttention: Bool { get }   // "Not configured"
    var transcriptionDetail: HomeTranscriptionDetail { get }
    var pairingTitle: String { get }                     // "PC connection" / "Pair Windows PC"
    var pairingValue: String { get }                     // "Not required" / "Paired" / "Pairing required"
    var pairingSubtitle: String { get }
    var pairingNeedsAttention: Bool { get }
    var assistantSubtitle: String { get }                // the model id, or "Choose an OpenRouter model"
    var assistantNeedsAttention: Bool { get }
    var startControl: HomeStartControl { get }
    func start() async                                   // session.start()
    func appeared()                                      // relay.beginPCModelObservation()
    func disappeared()
}
```

"Start is busy" means `session.phase == .preflight`. Layout does not scroll at normal sizes
(`ScreenScaffold(scrolls: false)`), and switches to the compact rules under 740 and to scrolling at
accessibility sizes (`dynamicTypeSize.isAccessibilitySize`). The Start control is placed with
`lcBottomBar`.

Contracts: `SessionController`, `SpeechSettings`, `AssistantSettings`, `RelayMonitor`,
`OnDeviceModelManager`, `SystemReadiness`, `HistoryModel`; `LCNavigationRow`, `Panel`, `StatusChip`,
`Banner`, `SessionStartControl`, `BrandMark`, `BrandName`.

Identifiers: `transcription-settings`, `pair-pc`, `assistant-lab` (the three rows, on the link);
`home-meta-state`, `home-pc-model-state` (the state text inside the Transcription row's subtitle);
`start-session` (the control in every `start` state); `download-model`.

### 5.3 Live session (screens 4)

```swift
enum TranscriptPreview: Equatable {
    case interim(String)                                 // partial text, with the caret
    case committed(String)                               // last committed segment
    case placeholder(String)
}
struct InProgressState: Equatable {
    let label: String                                    // the stage label, or "Incomplete answer"
    let text: String
    let showsSpinner: Bool
    let isIncomplete: Bool                               // an error card is above it
}

@MainActor @Observable final class LiveSessionViewModel {
    init(session: SessionController, assistant: AssistantSettings, clipboard: any ClipboardWriting, toasts: ToastCenter)
    var isTranscriptExpanded: Bool                       // false when a session starts
    var stateChip: StatusChipModel { get }               // 4.1.1, by phase
    var meterState: LevelMeterState { get }
    var level: Float { get }
    var elapsedText: String { get }                      // "MM:SS"
    var elapsedSpokenText: String { get }                // "4 minutes 12 seconds"
    var providerLine: String { get }                     // display name; Meta adds " · est. transcription"
    var costText: String? { get }                        // Meta only
    var statusLine: String { get }                       // session.transcriberStatus
    var assistantModelText: String { get }               // text after the last "/", or "Choose model"
    var notice: SessionNotice? { get }
    var preview: TranscriptPreview { get }
    var segments: [TranscriptSegment] { get }
    var partialText: String { get }
    var errorText: String? { get }
    var inProgress: InProgressState? { get }             // while assisting, or while streamed text is left over
    var latestTurn: AssistantTurn? { get }               // the turn whose id is session.latestTurnID
    var earlierTurns: [AssistantTurn] { get }            // newest first
    var showsEmptyState: Bool { get }
    var canRetry: Bool { get }                           // session.canRetry and not assisting
    var isPaused: Bool { get }
    var isAssisting: Bool { get }
    var isTransitioning: Bool { get }
    var feedScrollTrigger: Int { get }                   // changes when an answer lands, an Assist starts or an error appears
    func togglePause() async
    func assist() async
    func retry() async
    func stop() async
    func copy(_ turn: AssistantTurn)                     // clipboard, then toast "Copied"
}

// Features/LiveSession/TimingViewContent.swift: pure, unit-tested
struct TimingRow: Equatable, Identifiable {
    let id: String
    let key: String
    let value: String
}
struct TimingViewContent: Equatable {
    let caption: String                                  // "<model used> · <effort used>"
    let rows: [TimingRow]                                // screens 4.6, in order
    let usageCaption: String?                            // "N input · M output tokens"
    let warning: String?                                 // "PC timing unavailable. Restart the updated desktop relay."
    let footerCaption: String                            // "<speech id> · <N> transcript characters" (+ " · same-text retry")
    let footnote: String                                 // S-20 or S-21
    init?(turn: AssistantTurn)                           // nil without a performance record
}

struct TimingView: View { init(turn: AssistantTurn) }    // identifier `response-timing`
struct AnswerCard: View {
    init(turn: AssistantTurn, isLatest: Bool, canRetry: Bool,
         onRetry: @escaping @MainActor () -> Void, onCopy: @escaping @MainActor () -> Void)
}
```

Seconds are shown as `0.000 s` in the timing view and `0.00 s` on cards; cost as `$0.000000`; all with a
"." separator.

As built (WP-10). The views of this screen that the feed and the tests construct take what they show, so
that they can be hosted without the environment: `InProgressCard(state: InProgressState)`,
`AssistErrorCard(message:canRetry:onRetry:)`, `AnswerDetailsText(details:)` (the details of an `AnswerCard`),
`AnswerFeed(scrolls:)` (false at the accessibility text sizes, where the whole screen scrolls),
`LiveStatusPanel(isCompact:)` and `LiveTranscriptPanel(isCompact:expandedHeight:)`. `SessionNoticeBanner()`
reads the view model. The view model also has the pure texts its properties are made of, for the tests:
`static func chip(for: SessionPhase)`, `meterState(for:)`, `elapsedText(seconds:)` and
`spokenElapsed(seconds:)`. `TimingViewContent.lastChunkRowID` names the Parakeet row, which the view draws
after the usage caption and the warning. `isTranscriptExpanded` is computed over the id of the session it
was expanded for, which is what makes it false when a session starts. The number formats are in
`AnswerFormat`. `ReasoningScreen` reads the selected effort from `AssistantSettings`.

Contracts: `SessionController`, `AssistantSettings`, `ClipboardWriting`, `ToastCenter`;
`SessionControlCluster`, `StatusChip`, `LevelMeter`, `Banner`, `DisclosureRow`, `TranscriptLine`, `Card`,
`IconButton`, `LCButton`, `EmptyStateView`, `haptic`.

Identifiers: `assistant-lab` (sliders button); `live-transcription-cost`; `transcriber-status` (status
line); `session-notice` (the banner); `expand-transcript` (disclosure header, labels "Expand live
transcript" / "Collapse live transcript"); `transcript-preview`; `live-transcript` (the scroll view);
`assistant-feed` (the scroll view); `assist-error` (the error text), `retry-failed-assist`;
`streaming-answer`; `assistant-answer`, `previous-assistant-answer`; `timing-disclosure`,
`previous-timing`; `response-timing`; `retry-same-text`; `pause-session`, `assist-button`, `end-session`
(set by the cluster). No element has the identifier `dismiss-answer`.

Haptics on the screen: `listeningStarted` on `session.listeningStartedCount`; `pauseToggled` on
`isPaused`; `answerArrived` on `session.answerArrivedCount`; `failure` on `session.assistFailedCount`;
`warning` on the notice appearing. `listeningStopped` fires from the root when the live screen goes away.

### 5.4 Assistant Lab and its sub-screens (screens 5)

```swift
struct ComparisonRow: Equatable, Identifiable {
    let id: UUID                                         // the turn's id
    let model: String
    let detail: String                                   // "<speech id> · <effort> · same text" or "… · fresh transcript"
    let timing: String                                   // "0.00 s total · <service name>"
    let question: String
}

@MainActor @Observable final class AssistantLabViewModel {
    init(assistant: AssistantSettings, relay: RelayMonitor, activity: SessionActivity,
         history: HistoryModel, session: SessionController)
    var keyDraft: String
    var searchText: String
    var provider: AssistantProvider { get }
    var isLocked: Bool { get }                           // an Assist is running: panels 1 to 3 disabled
    var selectedModelID: String { get }
    var reasoningCaption: String? { get }                // "Reasoning: <effort>", Codex only
    var codexModels: [AssistantModelOption] { get }
    var showsReasoningRow: Bool { get }                  // the selected model is in the catalogue
    var reasoningValue: String { get }                   // capitalised
    var reasoningEfforts: [String] { get }               // the selected model's, in catalogue order
    var showsPairFirst: Bool { get }                     // Codex and not paired
    var openRouterModelRowTitle: String { get }          // "Choose a model" / "Change model"
    var keyPanelFirst: Bool { get }                      // OpenRouter with no key saved
    var hasKey: Bool { get }
    var keyStateChip: StatusChipModel { get }            // "Key saved" (success) / "No key saved" (neutral)
    var keyMessage: InlineMessage? { get }
    var isVerifyingKey: Bool { get }
    var refreshTitle: String { get }                     // "Refresh PC model list" / "Refresh OpenRouter models"
    var isLoadingCatalogue: Bool { get }
    var catalogueMessage: InlineMessage? { get }
    var explanation: String { get }                      // S-11 / S-12
    var comparisons: [ComparisonRow] { get }             // 50 newest timed turns of the active and stored sessions
    var filteredOpenRouterModels: [AssistantModelOption] { get }   // name or id contains searchText, case-insensitive
    func appeared() async                                // assistant.loadCatalogue()
    func selectProvider(_ provider: AssistantProvider)   // switch, then reload the catalogue
    func selectCodexModel(_ option: AssistantModelOption)
    func selectEffort(_ effort: String)
    func selectOpenRouterModel(_ option: AssistantModelOption)
    func saveKey()                                       // clears the draft on success
    func verifyKey() async
    func removeKey()
    func refresh() async
    func turn(id: UUID) -> AssistantTurn?                // Run details
    static func effortLabel(_ effort: String) -> String  // "None (no reasoning)" for "none", else capitalised
    static func priceText(prompt: String?, completion: String?) -> String   // "Input $0.000 / 1M · output $0.000 / 1M"; "Unknown" per missing price
}
```

Sub-screens: `OpenRouterModelsScreen` (`.searchable(text:prompt: "Search models")`), `ReasoningScreen`,
`RunDetailsScreen(turnID: UUID)`. Each reads the same view model.

Contracts: `AssistantSettings`, `RelayMonitor`, `SessionActivity`, `HistoryModel`, `SessionController`;
`Panel`, `ChoiceRow`, `LCNavigationRow`, `LCSecureField`, `LCButton`, `StatusChip`, `Banner`, `TimingView`.

Identifiers: `assistant-provider` (the "Answer service" panel as a container whose accessibility value is
the selected service's display name); `assistant-provider-codex`, `assistant-provider-openrouter`;
`selected-assistant-model`; `choose-<model id>` (Codex rows and OpenRouter picker rows); `reasoning-picker`;
`effort-<effort>`; `openrouter-model-picker`; `openrouter-key`, `save-openrouter-key`,
`openrouter-key-state`, `verify-openrouter-key`, `remove-openrouter-key`; `timing-disclosure` is not used
here: Run details shows `response-timing` open.

### 5.5 Settings (screens 6, with the About panel of R-12)

```swift
// Features/Settings/ProviderSetupRows.swift: shared by Settings and Transcription
struct ProviderSetupState: Equatable {
    enum Row: Equatable {
        case metaKey(value: String, needsAttention: Bool)     // "Saved on iPhone" / "Required"
        case pcSpeechModel(value: String)                     // the home label
        case modelLibrary(value: String)                      // "Ready" / "Not loaded"
        case benchmark
    }
    let rows: [Row]
    init(provider: SpeechProvider, hasMetaKey: Bool, pcModelLabel: String, isModelLoaded: Bool)
}
struct ProviderSetupRows: View { init(state: ProviderSetupState) }

@MainActor @Observable final class SettingsViewModel {
    init(speech: SpeechSettings, assistant: AssistantSettings, relay: RelayMonitor,
         models: OnDeviceModelManager, readiness: SystemReadiness)
    var providerName: String { get }
    var providerLockCaption: String? { get }             // readiness.providerLockReason?.caption
    var setup: ProviderSetupState { get }
    var explanation: String { get }                      // S-01 / S-02 / S-03
    var assistantSubtitle: String { get }
    var instruction: String { get set }                  // assistant.instruction
    var pairingTitle: String { get }
    var pairingSubtitle: String? { get }                 // "Saved · connected" / "Saved · reconnecting"
    var versionText: String { get }                      // AppVersion.marketing
    func appeared()                                      // relay.beginPCModelObservation()
    func disappeared()
}
```

Fixed rows: "Text size" → "Follows your iPhone's text size"; "Appearance" → "Daylight"; "Version";
"Speech packages" and "Usage reporting" with the two texts of architecture 4.13; S-05; S-06 as amended by
decisions 6.1; links to the Meta pricing page.

Contracts: the owners above; `Panel`, `LCNavigationRow`, `LCTextField`, `DetailRow`, `CaptionText`.

Identifiers: `speech-provider` ("Provider" row); `meta-key-settings` ("Meta API key" row);
`assistant-lab`; `assistant-instruction`; `finish-instruction` (keyboard toolbar "Done"); `pair-pc`.

### 5.6 Transcription (screens 7)

```swift
@MainActor @Observable final class TranscriptionViewModel {
    init(speech: SpeechSettings, assistant: AssistantSettings, relay: RelayMonitor,
         models: OnDeviceModelManager, readiness: SystemReadiness)
    var providers: [SpeechProvider] { get }              // catalogue order
    var selected: SpeechProvider { get }
    var lockCaption: String? { get }
    var selectedTitle: String { get }                    // the display name: panel 2's title
    var setup: ProviderSetupState { get }
    var explanation: String { get }
    func select(_ provider: SpeechProvider)              // stays on this screen
    func appeared()
    func disappeared()
}
```

Contracts: `SpeechSettings`, `RelayMonitor`, `OnDeviceModelManager`, `SystemReadiness`; `ChoiceRow`,
`ProviderSetupRows`. Identifiers: `provider-meta`, `provider-nemotron`, `provider-qwen3`, `provider-voz`,
`provider-parakeet`; `meta-key-settings`.

### 5.7 PC speech model (screens 8)

```swift
@MainActor @Observable final class PCSpeechModelViewModel {
    init(speech: SpeechSettings, relay: RelayMonitor)
    var title: String { get }                            // the provider's display name
    var stateText: String { get }                        // "Stopped", "Starting", "Ready", "Stopping", "Error", "Unavailable"
    var isChanging: Bool { get }
    var isError: Bool { get }
    var elapsedText: String? { get }                     // "N s" while changing
    var message: String { get }
    var otherLoadedModel: String? { get }                // "Currently loaded: <id>"
    var isInUse: Bool { get }                            // ready and busy: chip "In use"
    var canStart: Bool { get }                           // not locked and not already ready for this provider
    var canStop: Bool { get }                            // not locked, status known, not stopped, loaded model is this provider
    func start() async
    func stop() async
    func refresh() async
    func appeared()
    func disappeared()
}
```

Contracts: `RelayMonitor`, `SpeechSettings`. Identifiers: `pc-model-state`, `pc-model-message`,
`start-pc-model`, `stop-pc-model`, `refresh-pc-model`.

### 5.8 Meta API key (screens 9)

```swift
@MainActor @Observable final class MetaKeyViewModel {
    init(speech: SpeechSettings)
    var keyDraft: String
    var hasKey: Bool { get }
    var stateChip: StatusChipModel { get }
    var message: InlineMessage? { get }
    var isTesting: Bool { get }
    var canSave: Bool { get }                            // draft not empty and no test running
    func appeared()                                      // clears the draft
    func save()                                          // clears the draft on success
    func test() async
    func remove()
}
```

Contracts: `SpeechSettings`. Identifiers: `meta-key`, `save-meta-key`, `meta-key-state`, `verify-meta-key`,
`remove-meta-key`, `meta-key-message`. Texts S-07 (as amended), S-08, S-09.

### 5.9 Pairing and the QR sheet (screens 10)

```swift
@MainActor @Observable final class PairingViewModel {
    init(pairing: PairingController, relay: RelayMonitor, app: AppModel)
    var pasteText: String
    var address: String
    var token: String
    var showsOptions: Bool                               // the disclosure, when paired
    var isScannerPresented: Bool
    var title: String { get }                            // "PC connection" / "Pair Windows PC"
    var isPaired: Bool { get }
    var savedStateChip: StatusChipModel { get }          // Connected / Reconnecting / Pairing needs attention
    var savedAddress: String { get }
    var savedMessage: String { get }                     // relay.connectionMessage, or the default text
    var isChecking: Bool { get }
    var optionsVisible: Bool { get }                     // not paired, or showsOptions
    var canReadPayload: Bool { get }
    var canVerify: Bool { get }                          // both fields not empty and not verifying
    var isVerifying: Bool { get }
    func appeared()                                      // address = the saved endpoint; token and paste box emptied
    func reconnect() async
    func readPayload()                                   // fills the two fields and clears the box, or alerts
    func scannerFinished(_ result: Result<String, QRScanFailure>)   // closes the sheet; success is handled as pasted text
    func verifyAndPair() async                           // on failure: alert with the message; fields keep their text
    // Added by the review (replacing a saved PC by another host needs a confirmation):
    var replacementPrompt: PairingReplacement? { get }   // non-nil while the confirmation is shown
    func confirmReplacement() async                      // clears the prompt, then verifies and pairs
    func cancelReplacement()                             // clears the prompt; nothing was sent, the saved pairing stays
}
```

**Replacing a saved PC** (architecture 4.9). `verifyAndPair()` first asks
`pairing.replacement(forAddress: address)`. When it is nil the call goes on as before. When it is not nil,
nothing is sent: the view model sets `replacementPrompt`, and `PairingScreen` shows a local `.alert` bound
to it (not the global alert, which has one button):

| Element | Text |
|---|---|
| Title | "Replace this PC?" |
| Message | "Replace `<current host>` with `<new host>`? Only do this with the QR shown by LiveCue Desktop on your own PC." |
| Extra line, when `newHostIsTailscaleName` is false | "`<new host>` is not a Tailscale address." |
| Buttons | "Replace" (destructive role) calls `confirmReplacement()`; "Cancel" (cancel role) calls `cancelReplacement()` |

Scanning or pasting a payload still only fills the two fields. No request is made before "Verify and pair",
and none before "Replace" when the prompt is shown.

As built (WP-11): `appeared()` also closes the pairing options and drops a pending replacement prompt,
because the view model outlives the screen. `PairingScreen` keeps what the scanner delivered until the
sheet is gone and calls `scannerFinished(_:)` from the sheet's `onDismiss`, so that the alert of a failed
scan is not raised under a sheet that is still closing. A pairing that the PC accepted and the phone could
not store raises the alert "This iPhone could not save the pairing. Try again."

`QRScanSheet` presents `QRScannerView` under the viewfinder overlay of DS 4.15, with the title
"Scan PC QR", the button "Cancel" and the caption "Point at the QR in LiveCue Desktop"; it uses
`lcSheetChrome()`. A failed "Verify and pair" shows `RelayMessages.text(for:)` with no "Your pairing is
saved." prefix (IOS-B10).

Contracts: `PairingController`, `RelayMonitor`, `AppModel`, `PermissionProviding` (through the
environment's dependencies), `QRScannerView`, `haptic(.success)`.

Identifiers: `saved-pc-state`, `saved-pc-address`, `reconnect-pc`, `show-pairing-options`,
`scan-pairing-qr`.

### 5.10 Model Library (screens 11)

```swift
struct PreparationBlock: Equatable {
    let fraction: Double
    let percentText: String                              // "N% setup"
    let elapsedText: String                              // "MM:SS elapsed"
    let statusLine: String
    let counterText: String?
    let failed: Bool
}

@MainActor @Observable final class ModelLibraryViewModel {
    init(speech: SpeechSettings, models: OnDeviceModelManager, app: AppModel, clock: any AppClock)
    var model: TranscriptionModel? { get }               // nil for a streaming provider
    var descriptionText: String { get }                  // "~467 MB · Transcribes when you tap Assist"
    var isLoaded: Bool { get }
    var mainButtonTitle: String { get }                  // "Download & use" / "Reload"
    var isPreparing: Bool { get }
    var block: PreparationBlock? { get }                 // only for this model, while preparing or after a failure
    var statusLine: String { get }
    var packageLabel: String { get }
    var showsVozUsageSentence: Bool { get }
    func prepare() async                                 // on failure: app.presentAlert(error.localizedDescription)
    func unload()
    func appeared()                                      // starts the 1 s elapsed refresh
    func disappeared()
}
```

Contracts: `OnDeviceModelManager`, `SpeechSettings`, `AppModel`, `AppClock`; `LCProgressBar`.
Identifier: `model-download-progress` (the bar).

### 5.11 Speech benchmark (screens 12)

```swift
@MainActor @Observable final class BenchmarkViewModel {
    init(session: SessionController, history: HistoryModel)
    var reference: String                                // default "The quick brown fox jumps over the lazy dog"
    var hypothesis: String
    var rateText: String { get }                         // one decimal and "%", or "–" while the reference is empty
    var canUseLatest: Bool { get }
    var latestCaption: String { get }
    func useLatestTranscript()                           // the active session, else the newest stored one (IOS-D6)
}
```

Contracts: `WordErrorRate`, `SessionController`, `HistoryModel`. Identifiers: none.

### 5.12 History and Session detail (screens 13)

```swift
enum HistoryUsageLabel: Equatable {
    case meta(String)                                    // identifier `history-transcription-cost`
    case local(String)                                   // identifier `history-local-transcription`
}
struct HistoryRowContent: Equatable, Identifiable {
    let id: UUID
    let title: String
    let dateText: String                                 // abbreviated date, short time, device locale
    let countsText: String                               // "N transcript segments · M assists"
    let usage: HistoryUsageLabel?                        // nil when the session recorded no usage
    init(session: Session)
}

@MainActor @Observable final class HistoryViewModel {
    init(history: HistoryModel)
    var pendingDeleteID: UUID?                           // the row whose confirmation is showing
    var rows: [HistoryRowContent] { get }
    var isEmpty: Bool { get }
    func requestDelete(_ id: UUID)
    func confirmDelete() async
    func cancelDelete()
}

// Features/SessionDetail/SessionDetailContent.swift: pure, unit-tested
struct SessionDetailContent: Equatable {
    struct Fact: Equatable, Identifiable {
        let id: String
        let key: String
        let value: String
    }
    let title: String
    let usageFacts: [Fact]                               // empty when no usage was recorded
    let usageCaption: String?                            // S-14 or S-15, Meta only
    let summary: String?
    let keyPoints: [String]
    let actionItems: [String]
    let segments: [TranscriptSegment]
    let turns: [AssistantTurn]
    init(session: Session)
}
struct SessionDetailScreen: View { init(sessionID: UUID) }
```

History is a `List` with clear row backgrounds (DS 4.8), because it needs `.swipeActions`; the "Delete"
action calls `requestDelete`, and the confirmation is an `.alert` titled "Delete this conversation?" with
the message and the buttons "Delete" (destructive) and "Cancel" of screens 13.

As built (WP-11): the list draws the ground itself instead of using `ScreenScaffold`, and each row draws
its slice of one panel as its row background, so the rows read as a panel. A row is a `Button` that appends
`Route.sessionDetail` to `AppModel.historyPath`, not a `NavigationLink`: a link in a `List` brings the
system's own disclosure indicator. The swipe action has no destructive role, because that role removes the
row before the confirmation. `HistoryRow` lays itself out twice, as `LCNavigationRow` does, so that the
usage label and its identifier stay outside the button.

Contracts: `HistoryModel`. Identifiers: `history-transcription-cost`, `history-local-transcription`.

---
## 6. Test plan

Three bundles in one scheme: `LiveCueCoreTests` (package tests), `LiveCueRemakeTests` (hosted unit tests),
`LiveCueRemakeUITests`. XCTest everywhere. No test contacts a provider, a relay, a real key, the microphone
or the camera.

### 6.1 Core unit tests (`LiveCueCoreTests`)

Fixtures are those of 3.1.12. "AC" ids are `AC-CORE` of spec 02 section 10, read with the changes of
decisions 5.2; "P2" ids are protocol 15.9.

| File (owner) | Proves | Fixtures |
|---|---|---|
| `DomainCodingTests` (WP-02) | AC-001, 002, 003, 038, 083, 104, 105, 106; every coded type round-trips under the default encoder and under `LiveCueJSON` | `CoreFixtures.session` |
| `AssistantConfigurationTests` (WP-02) | AC-036, 037; `effort(keeping:)` keeps, falls to the first, falls to "low"; `supports(_:)` | none |
| `LiveCueJSONTests` (WP-02) | CORE-A12, IOS-B18: exactly three fractional digits written; zero to nine accepted; a non-UTC or malformed date fails; UUIDs written upper case and read in either case; absent optionals omitted; a stored `isFinal` key is ignored (CORE-C2) | literals |
| `SessionLifecycleTests` (WP-02) | AC-IOS-076 title rule (48 characters, "Conversation"); IOS-B2 recovery: end time, title and interrupted streams after `closedAfterInterruption(at:)`; a closed session is unchanged | `CoreFixtures.session` |
| `ContextBuilderTests` (WP-02) | AC-009, 010, S01, S02, S04, S05; P2-208 (request id present, `transcriptTruncated` exactly when cut, nil without segments); retry keeps the text and changes only id and assistant | `CoreFixtures.session` |
| `WireBudgetTests` (WP-02) | AC-011; P2-207 for the four inputs, assist and summary: encoded size at most C-11, cuts on character boundaries, the end kept; shrink order of step 3, including a plain ASCII request whose members are all at their step 1 budgets (136,192 bytes of text before fitting); `head` and `tail` on multi-byte text | generated strings |
| `WordErrorRateTests` (WP-02) | AC-012, 013, S20 | literals |
| `TranscriptionUsageTests` (WP-02) | AC-033 to 035, 082, 090 to 103, 107, S17; `apply(_:)` equals the direct calls; `firstText` applied once; `interruptOpenStreams` | none |
| `PCModelStatusTests` (WP-02) | AC-070 to 080, S18; P2-219 | literals |
| `SpeechProviderTests` (WP-02) | AC-024, 081, 084; `stored(_:)` default; `PCRequirement.needsPC` for all ten pairs | none |
| `PairingPayloadTests` (WP-01) | AC-004 to 008 with a 43-character token; P2-202; P2-203 manual entry; `storedText` member order and round trip through `parse`; ST-13 (test plan 4.6): a token followed by a line feed, by a carriage return and line feed, or by U+2028 is refused by `parse` and by `manual` | `CoreFixtures.token` |
| `RelayEndpointTests` (WP-01) | P2-200 for both endpoints and all ten routes; P2-203 canonical forms; every rejection of the grammar in protocol 4.3, including an endpoint followed by a line feed; `host` for an endpoint with and without a port and a path | literals |
| `JSONValueTests` (WP-01) | A boolean is never a number and a number never a boolean; non-finite and fractional numbers have no `intValue`; text that is not JSON parses to nil | literals |
| `RelayClientRoutesTests` (WP-03) | P2-201 (headers on every route, none named `Origin` or `Cookie`); P2-204; P2-211 (one request per failing call); P2-221; verify sends `{}`; start and stop send the model and accept 200 and 202; a health 200 with `protocol` 2 and `ok` false is `L-UNREADABLE`; every request's `timeout` is its route's phone timeout plus 10 s; the cancel path ends in `requestId.uuidString` | `FakeHTTPTransport`, `CoreFixtures` |
| `RelayClientClassificationTests` (WP-03) | P2-205, one test per row of protocol 13.6, including row 3 with `ManualTime` | `FakeHTTPTransport`, `ManualTime` |
| `RelayClientAssistTests` (WP-03) | P2-210; the reply decodes for `throughSegmentId` null, a UUID and "not-a-uuid", and the returned marker is nil (core half of P2-209); P2-212 (one `DELETE` on timeout, one on task cancellation, a 404 answer is not an error); the body sent is the fitted body | `FakeHTTPTransport`, `ManualTime`, `CoreFixtures.assistReplyJSON` |
| `RelayMessagesTests` (WP-03) | P2-206 for every case of `RelayWireCode` and `RelayLocalCondition`; unknown code with and without usable relay text, for both origins; no text contains the token; the four compositions of protocol 14.3 and the prefix rule (IOS-B10) | none |
| `OpenRouterClientTests` (WP-03) | AC-039, 042 to 054, S10 (the whitespace key now gives `malformedKey`), S11 (and no `instruction` in the context JSON, CORE-A4), S12, S13; cancellation surfaces as `CancellationError` from all three calls (CORE-A11); the 120 s total limit gives `unavailable` | `FakeHTTPTransport`, `ManualTime` |
| `OpenRouterCatalogueTests` (WP-03) | AC-040, 041, S09 as changed (order by lower-cased name then id; a malformed entry skipped; a non-string price read as absent; missing `data` fails) | literals |
| `OpenRouterStreamParserTests` (WP-03) | AC-055 to 069, S14, S15 | literals |
| `StreamTranscriptTests` (WP-04) | AC-014 to 020, 031, 032, 085 to 089, S16 as changed; CORE-A7 (no stray spaces); CORE-A8 (a partial goes to the turn it names when open) | none |
| `MetaRealtimeAPITests` (WP-04) | AC-021 to 023, 025 (empty is `missingKey`, whitespace is `malformedKey`), 026 to 030, S19; a boolean `turnId` or `audioProcessedMs` is dropped (CORE-A17) | literals |
| `MetaConnectionTestTests` (WP-04) | The test sends the handshake, requires the acknowledgement, sends `endStream`, closes, and sends no binary frame (AC-IOS-110); a rejected acknowledgement is `authentication`; expiry of 15 s is `connection` | `FakeWebSocketTransport`, `ManualTime` |
| `PCMFramerTests` (WP-04) | Full frames of the given size; one final shorter, non-empty, even frame (P2-216 framing) | none |
| `PendingAudioBufferTests` (WP-04) | 30-minute cap refuses the chunk; only words ending after the cursor are kept; the buffer is trimmed to 2 s and keeps audio appended during transcription | none |
| `RelaySpeechFrameTests` (WP-04) | Each frame type of protocol 10.3; wrong member types read as absent; a `loading` frame with a missing member is still `loading`; unknown types (P2-218) | literals |
| `RelaySpeechNegotiationTests` (WP-04) | P2-213 (mismatched `ready`), P2-214, P2-215, P2-217, step 7 of protocol 13.7 (close reason is a known code); before `ready` an unknown type is ignored and each of the four lab types is `L-UNEXPECTED-FRAME` | none |
| `SpeechStreamSessionRelayTests` (WP-04) | The speech URL and headers (P2-200, P2-201); no audio before `ready` and `loading` keeps waiting up to C-59, after which the socket is closed and the start fails with `L-TIMEOUT` (P2-213, AC-IOS-020); P2-214 end to end; P2-215 (1000 inside C-60 is completed; 1011, a drop and no close are interrupted, the last with `L-STREAM-DROPPED`; AC-IOS-047); P2-216 (2,560-byte frames, remainder, exactly one `endStream`); P2-218; usage events in order (AC-IOS-041) | `FakeWebSocketTransport`, `ManualTime`, `CoreFixtures.pairing` |
| `SpeechStreamSessionMetaTests` (WP-04) | The fixed endpoint; handshake first; acknowledgement required; 3,840-byte frames; an `error` event ends the stream with the provider text; completed only after `endStream` and close 1000 (IOS-A11); 30 s connect limit | `FakeWebSocketTransport`, `ManualTime` |
| `ContractSmokeTests` (WP-01) | The constants of 3.1.5 have the values of protocol section 2 | none |

AC-CORE-S03 is retired (decisions 5.2). AC-CORE-S06 to S08 are replaced by P2-202 and P2-203.

### 6.2 App unit tests (`LiveCueRemakeTests`)

They build owners with `TestOwners` (3.2.11) and drive time with `ManualClock`. Every test method is
`@MainActor` and `async`.

| File (owner) | Proves |
|---|---|
| `LaunchConfigurationTests` (WP-01) | Each of the thirteen arguments sets its flag; `-ui-testing` and the unit-test host select fixture mode; anything else is live |
| `AppEnvironmentSmokeTests` (WP-01) | `AppEnvironment` builds in fixture mode; `bootstrap()` is idempotent |
| `KeychainStoreTests` (WP-01) | Set, read, overwrite and delete an item under a test service; deleting an absent item succeeds. This is also the check that the test build was signed: an unsigned simulator build fails here with -34018 |
| `SessionControllerStateTests` (WP-08) | One test per row T-01 to T-21 of 4.1.1. Among them: nothing saved before T-04 and nothing left after T-05 (IOS-B2, AC-IOS-154); T-09 and T-10 for every provider with the failure of each kind (AC-IOS-043 to 046, IOS-B4, IOS-B5); no second finalisation on Stop from Paused (IOS-B7, AC-IOS-077); stale generations dropped; elapsed keeps counting while paused (AC-IOS-030); the stream offset on resume (AC-IOS-041); start checks in order with their texts (AC-IOS-016, 017, 022); controls disabled while transitioning or assisting (AC-IOS-042); title on Stop (AC-IOS-076); finalise failure on Pause is a notice and on Stop an alert (AC-IOS-047) |
| `SessionControllerAssistTests` (WP-08) | The thirteen steps of 4.3 for both services: preconditions inline (AC-IOS-055, 056); "No speech was recognized yet." (AC-IOS-054); Codex catalogue fetched every time and a missing selection named, selection unchanged (AC-IOS-062); OpenRouter catalogue fetched only when empty and its failure shown (IOS-B9); marker recorded at build and applied for both services, unchanged by a retry (P2-209, AC-CORE-046); retry has a new id, the current selection and `reusedText` (AC-IOS-060); failure saves nothing and keeps streamed text (AC-IOS-057, 059); success after failure clears it (AC-IOS-058); a result for an ended session is dropped; Voz transcribes first and the segment is in the request (AC-IOS-065); Assist while paused (AC-IOS-067); the performance record's fields |
| `SessionControllerSummaryTests` (WP-08) | Summary only for Codex, a pairing and at least one segment (AC-IOS-078, 079, P2-208, P2-220); success replaces title and notes; failure is silent; `cancelSummary` cancels the service's request |
| `SessionPersistenceTests` (WP-08) | Latest value wins; `flush` waits; saves are in order |
| `AnswerServicesTests` (WP-08) | Codex service without a pairing throws `L-NOT-PAIRED`; `cancel(requestId:)` cancels the running task; OpenRouter service without a key throws `missingKey`; `AnswerErrorText` for each error type |
| `RelayMonitorTests` (WP-09) | No request when the PC is not needed or nothing is paired (P2-220, AC-IOS-143); a check at start, on activation and every 5 s after completion, never overlapping (AC-IOS-142); state follows the most recent check only (IOS-B11, AC-IOS-136, 137); PC model polling only while observed and PC-local, every 2 s (AC-IOS-106); commands refused while locked; failure changes only the message; the ten home labels; `canStartSession` (AC-IOS-011) |
| `PairingControllerTests` (WP-09) | Reading a payload saves and sends nothing (AC-IOS-128); success saves and updates the monitor (AC-IOS-130); failure leaves the old pairing (AC-IOS-131); invalid input throws the payload error (AC-IOS-129) |
| `AssistantSettingsTests` (WP-09) | Switching saves and restores per-service selections (AC-IOS-124) and is refused while assisting; model and effort rules (AC-IOS-117); a stale catalogue result is dropped (AC-IOS-123); the catalogue messages; key save, verify, remove and their messages, including the malformed-key text |
| `SpeechSettingsTests` (WP-09) | Unknown stored provider reads as Meta; `select` refused during a session and while blocked (AC-IOS-094); key actions refused during a session (AC-IOS-113); empty and whitespace keys give their two messages (AC-IOS-109 as changed); test and remove messages |
| `HistoryModelTests`, `SystemReadinessTests` (WP-09) | Order newest first (AC-IOS-085); delete calls `willDelete` first; readiness for every provider and service (AC-IOS-008); lock reasons |
| `KeychainSecretStoreTests`, `FilePairingStoreTests`, `UserDefaultsPreferencesStoreTests`, `FileSessionStoreTests`, `URLErrorMappingTests`, `TestSeamLaunchActionsTests` (WP-06) | Keys under the test accounts round-trip and remove (AC-IOS-152); pairing load order file then Keychain, restore without the Keychain copy, file excluded from backup and protected (AC-IOS-133, 141); preference keys and defaults of architecture 4.10; session files upsert, sort, are written atomically into a protected folder, a file that does not decode is skipped, and `closeInterruptedSessions` uses the file's last-saved time (the store is given a temporary folder); each system error code maps as 3.2.1 says; each launch action |
| `StreamingTranscriberEngineTests`, `VozTranscriberTests`, `ParakeetTranscriberTests`, `OnDeviceModelManagerTests`, `FixtureTranscriberTests` (WP-07) | The columns of 4.2 with `FakeAudioCapture` and fake engines: microphone after the handshake, audio session released or kept on Pause (IOS-B6), each failure kind and its text, `failed` last, events emitted before a call returns; the steps of 4.6 including Reload and the per-model progress block (IOS-B13, AC-IOS-145 to 149); fixture usage gains 1 s per second |
| `HomeViewModelTests`, `LiveSessionViewModelTests`, `AssistantLabViewModelTests`, `TimingViewContentTests` (WP-10) | Home chip and rows for every combination (AC-IOS-004 to 014); live chip, preview and feed states (AC-IOS-027, 029, 038, 050, 072); comparisons limit and order (AC-IOS-126); price and effort labels; timing rows for Codex, OpenRouter, Parakeet, retry and missing PC timings (AC-IOS-069) |
| `SettingsViewModelTests`, `TranscriptionViewModelTests`, `PCSpeechModelViewModelTests`, `MetaKeyViewModelTests`, `PairingViewModelTests`, `ModelLibraryViewModelTests`, `BenchmarkViewModelTests`, `HistoryViewModelTests`, `SessionDetailContentTests` (WP-11) | Provider rows by provider (AC-IOS-095 to 097); PC model button rules (AC-IOS-099 to 105); pairing fields after a read, alert without prefix on a failed verify; progress block only for its model; "Use latest transcript" source (IOS-D6); usage labels and sections only with content (AC-IOS-081, 084, 086, 088) |
| `FilePairingStoreTests` (WP-06), as changed by the review | ST-07 (test plan 4.6). This replaces "pairing load order file then Keychain" in the WP-06 row above: with A in the Keychain and B in the file, `load` returns A, the Keychain still holds A and the file now holds A; with a Keychain item and no file, the file is written from it; with no Keychain item, `load` returns the file's pairing and **does not write the Keychain**; a Keychain text that does not parse falls back to the file; `save` writes the Keychain first and then the file; when the Keychain write fails the file is written and an older Keychain item is deleted; when that item can be neither replaced nor deleted, `save` throws and the file again holds the Keychain's pairing; after every `save` that returned, `load` returns the saved pairing. Still holds: restore without the Keychain copy, the file excluded from backup and protected (AC-IOS-133, 141) |
| `PairingControllerTests` (WP-09), added by the review | ST-18: `replacement(forAddress:)` is nil with nothing saved, nil for the saved host (also with another port or path, and in another letter case), non-nil with both hosts for another host, and nil for an address that does not parse; `newHostIsTailscaleName`; calling it sends and saves nothing |
| `PairingViewModelTests` (WP-11), added by the review | ST-18: with a saved pairing and another host, `verifyAndPair()` sets `replacementPrompt` and the fake gateway recorded zero requests; `cancelReplacement()` clears it, sends nothing and leaves the saved pairing; `confirmReplacement()` verifies and pairs; with the same host, and with nothing saved, there is no prompt; between `scannerFinished` and `verifyAndPair()` the gateway recorded zero requests |
| `AnswerPlainTextTests` (WP-10), added by the review | ST-26: `AnswerCard`, `InProgressCard` and `AssistErrorCard`, each hosted in a `UIHostingController` with the text `[label](https://pc.example.test/x) https://pc.example.test/y` as answer, details, streamed text and error message, expose no accessibility element with the link trait, and the text is shown verbatim, brackets included |
| `RelayTextPlainTextTests` (WP-11), added by the review | ST-26: the same check for the PC status `message` line of the PC speech model screen, the Saved PC message line, and a line that shows an unknown-code relay text |

### 6.3 UI tests (`LiveCueRemakeUITests`)

Every test launches with `-ui-testing` plus the arguments shown, finds elements by identifier across all
element types (`UITestSupport.element(_:)`), and attaches a screenshot at each named step. The old tests
T1 to T16 are ported to the new navigation.

| Test | Old | Arguments | Proves (`AC-IOS`) |
|---|---|---|---|
| `ShellUITests.testLaunchShowsThreeTabs` (WP-01) | | none | 001 |
| `ConversationUITests.testInlineAnswersTranscriptAndPinnedControls` | T1 | none | 019, 025, 026, 035, 036, 049, 050, 051, 053, 074; no `dismiss-answer` |
| `ConversationUITests.testFailedAssistIsInlineAndRetryRecovers` | T2 | `-assist-fails-once` | 057, 058, 061 |
| `ConversationUITests.testCostPauseAndHistory` | T16 | none | 001, 002, 003, 029, 031, 032, 033, 034, 035, 039, 080 |
| `CloudOnlyUITests.testMetaKeyAndCloudOnlyConversation` | T3 | `-meta-no-key -pc-offline -openrouter-ui-reset`, then relaunch with `-meta-key-persistence -openrouter-persistence-test -pc-offline` | 005, 007, 010, 016, 096, 107, 108, 110, 111, 112, 053 |
| `CloudOnlyUITests.testOpenRouterKeyModelAndThreeAssists` | T4 | `-openrouter-ui-reset -pc-offline -pc-catalog-slow`, then relaunch with `-openrouter-persistence-test -pc-offline` | 005, 056, 118 to 123, 053, 079 |
| `PCModelUITests.testStartAndStopOnPCSpeechModelScreen` | T5 | none | 093 as changed, 097, 099, 100, 101 |
| `PCModelUITests.testStartErrorAllowsRetry` | T6 | `-pc-model-start-error` | 014, 102 |
| `PCModelUITests.testPCSpeechHidesCostInSessionAndHistory` | T8 | none | 031, 039, 077, 081 |
| `PCModelUITests.testHomeFollowsLoadingReadyAndStopped` | T9 | `-pc-model-slow-start` | 011, 012, 013 |
| `AssistantUITests.testAnotherCodexModelAtLowEffortCanAssist` | T10 | none | 114, 117 |
| `AssistantUITests.testSelectionReasoningAndTiming` | T11 | none | 060, 068, 069, 115, 116, 125 |
| `OnDeviceUITests.testConversationThenModelDownload` | T12 | none, then relaunch with `-on-device-ready` | 009, 026, 075, 145, 150 |
| `PairingUITests.testPairingSurvivesRelaunchAndMissingKeychainCopy` | T7 | `-pairing-persistence-test -save-pairing-fixture`, then relaunch with `-pairing-persistence-test -drop-test-keychain` | 132, 133, 134, 135 |
| `PairingUITests.testPairingOffersQRScanner` | T13 | none | 127 |
| `SettingsUITests.testAppearanceAndCloudDisclosure` | T14 | none | 089, 090 as changed, 098 |
| `SettingsUITests.testInstructionPersists` | T15 | none, then relaunch | 034, 091, 092 |
| `HistoryUITests.testDeleteAsksForConfirmation` | | none | 083 as changed (Cancel keeps the row; Delete removes it), 075 |
| `GlassContrastUITests.testProminentControlFillIsDarkEnough` | | none; iOS 26 leg only, skipped elsewhere | DS 3.2: the Start control's fill, sampled away from its label in a screenshot, has relative luminance of 0.183 or less |

Not covered by a UI test because the fixed test seam has no argument for it: stream failures and the
session notice, an unpaired first run, the QR camera. They are covered in 6.2.

As built (WP-12). Nineteen tests; all pass on both legs, and the glass gate, which skips itself before
iOS 26, measured a relative luminance of 0.110 for the fill. Each test attaches pictures named after its
old test (`t1-…` to `t16-…`, `history-…`, `session-detail`, `glass-…`). Where a test proves a statement
in another form than the statement's own words, this is the form:

| Statement | How the test proves it |
|---|---|
| 019, 051 "tappable" | The control exists, is enabled and lies on screen; the walk then taps it. `isHittable` is not used: on iOS 26 it is not reliable for every control |
| 002 | The pairing row, the assistant row and Start lie on screen, the rows above Start and Start above the tab buttons; both rows are then tapped where they are |
| 029, 039 | The state element has no identifier. It is found by its label, the chip text, among the elements that are not `transcriber-status`; its value is the elapsed time |
| 033 | The cost is read when "Paused" appears and again after the elapsed time changed twice |
| 080 | The session is paused before Stop, so that the last cost can be read; the History row must contain that text |
| 061 | T2: no retry control exists before the first Assist; "Retry same text" is enabled after the failure and the same-text retry after the answer. T4: after an Assist that sent nothing (no OpenRouter key) "Retry same text" is shown disabled. That both are disabled while an Assist runs, 0.4 s in the fixture, is left to the unit tests of 6.2 |
| 012 | "Loading" and the disabled Start are read from one snapshot of the app, so that they belong to the same moment |
| 102 | The retry is taken: the start runs again and ends in the same error |
| 120 | The fixture model has no price, so its row reads "Input Unknown · output Unknown". The search is tried with a part of the id and with a text that matches nothing; the fixture's name and id cannot be told apart by a search without a space |
| 123 | The PC's catalogue takes 8 s. The test stays on OpenRouter's list until 10 s after Assistant Lab appeared and requires that no Codex model shows up |
| 079 | Seen from outside: the conversation keeps the title its first segment gave it. The fixture summary would rename it to "Local transcription", as `HistoryUITests` sees with Codex answers |
| 093, 096 | The value of a row ("Qwen3 1.7B · PC", "Required", "Saved on iPhone") is its accessibility value |
| 098 | The Version row must show a dotted number and not "Unknown"; the number itself is not fixed in the test |
| 132 to 135 | The pairing is saved by `-save-pairing-fixture`, never typed. A launch before it only keeps pictures of the unpaired first run, while the simulator has no test pairing yet |
| 127 | After "Scan PC QR code" the simulator, which has no camera, raises "Camera is unavailable."; that is kept as a picture and not asserted |
| 092 | After the keyboard's "Done" neither a keyboard nor the "Done" button is left |
| 145, 150 | Voz is prepared in the first launch, from Home's download control; the Parakeet session runs in the launch with `-on-device-ready` |

The two tests that launch with `-pc-offline` expect "PC reconnecting" on Home and nowhere a text
"PC offline." (9.2). `testInstructionPersists` records the runtime warning "Invalid frame dimension
(negative or non-finite)" on both legs while the instruction field has the keyboard; no statement is
affected, and its origin was not looked for.

`UITestSupport` (WP-01) is what the UI tests share:

```swift
enum UITestSupport {
    static let launchTimeout: TimeInterval = 120         // the first element after a launch; a fresh runner is slow
    static let timeout: TimeInterval = 15
    @MainActor @discardableResult static func launch(_ arguments: [String] = []) -> XCUIApplication   // adds "-ui-testing"; also the relaunch
    @MainActor static func element(_ identifier: String) -> XCUIElement       // any element type
    @MainActor @discardableResult static func waitFor(_ identifier: String, timeout: TimeInterval = 15,
                                                      file: StaticString = #filePath, line: UInt = #line) -> XCUIElement
    @MainActor static func waitForLabel(_ identifier: String, equals label: String, timeout: TimeInterval = 15) -> Bool
    @MainActor static func waitForLabel(_ identifier: String, contains text: String, timeout: TimeInterval = 15) -> Bool
    @MainActor static func waitForDisappearance(of identifier: String, timeout: TimeInterval = 15) -> Bool
    @MainActor static func waitUntilSelected(_ element: XCUIElement, timeout: TimeInterval = 15) -> Bool
    @MainActor static func tabButton(_ title: String) -> XCUIElement
    @MainActor static func attachScreenshot(named name: String, to testCase: XCTestCase)
}
```

The first wait after a launch uses `launchTimeout`. CI gives a test 180 s by default and at most 600 s
(`executionTimeAllowance`), so a test with several relaunches or the 8 s catalogue raises its own allowance.

### 6.4 Where each test runs

| Run | What | Tests |
|---|---|---|
| Linux, `swift:6.2-noble` | `swift test --package-path ios/LiveCueCore`; the glass and import lint | All of 6.1 |
| Simulator, iOS 26.2 on `macos-26` | The scheme: all three bundles | 6.1, 6.2, 6.3 including the glass gate |
| Simulator, iOS 18.6 on `macos-15` | The scheme: all three bundles | 6.1, 6.2, 6.3 except the glass gate |

The workflow `.github/workflows/ios.yml` runs in two sizes, because a public repository gets only a few
macOS jobs at a time:

| Size | When | Jobs |
|---|---|---|
| quick | A push to a branch named `wp/**` | Linux core job; helper-script tests; one macOS job that builds the app and runs `LiveCueCoreTests` and `LiveCueRemakeTests` on the iOS 26.2 simulator. No UI tests, no iOS 18 leg, no device build |
| full | A push to `main`; a pull request into `main`; a manual run | Linux core job; helper-script tests; both simulator legs with all three bundles and exported screenshots; the unsigned device build with IPA validation |

A full run on a branch is started by hand: `gh workflow run ios.yml --ref <branch> -f scope=full`
(`scope=quick` asks for the small run). A newer run for the same branch and trigger cancels the older one.
The workflow starts only when a file under `ios/`, `scripts/ci/` or the workflow itself changed.

### 6.5 What only a device can show (for the owner)

1. The IPA installs and launches in LiveContainer beside the old app; bars and sheets show Liquid Glass with
   "Spoof SDK Version" on.
2. Microphone prompt and the denied path inside LiveContainer; camera prompt and a scan of the real desktop
   QR.
3. Capture continues with the app in the background and the phone locked, for one streaming provider and
   one on-device provider; the tick and elapsed time after a long lock.
4. A phone call during listening pauses with the interruption notice, for Meta, PC speech, Voz and Parakeet;
   unplugging or switching off a headset pauses with the route notice. Plugging a headset in while
   listening is not reported by design: check whether capture goes on or the level meter falls flat.
5. Parakeet: whether the partial text is cumulative for the whole stretch or resets after an utterance
   (IOS-A1), and that Pause commits one segment.
6. Meta closes with code 1000 after `endStream`, so a Pause is recorded completed (IOS-A11).
7. Voz: first load time, that "Reload" does not download again (4.6, `Voz.download` is assumed idempotent),
   and one Assist with real audio.
8. Model folders are excluded from backup after preparation.
9. Keychain inside LiveContainer: keys save and survive a relaunch; with a failing Keychain the pairing is
   still restored from the file.
10. A PC speech stream of several minutes through Private mode and through Public mode on a mobile network,
    including at least one full minute of silence: the relay's keep-alive is an unsolicited Pong every 15 s,
    and the stream must stay up through it.
11. One real pairing in each mode, one real Codex answer and one summary; "Reset pairing" on the PC shows
    "Pairing needs attention" on the phone.
12. Haptics are felt during a session and are not audible in the transcript.
13. Home at the largest accessibility text size scrolls and the Start control stays reachable.

---

## 7. Work packages

### 7.1 Rules for every package

- Work from this file. Own only the files listed for the package; create new files only inside a folder
  the package owns and only for private helpers; file names stay unique in the target.
- Replace every stub body marked `// STUB(WP-nn)`. Do not change a printed signature (see Conventions).
  Some marked bodies are already right because the real thing was the cheapest valid stub (a switch, a
  one-line getter); the owner checks them and removes the marker.
- A package is done when its tests are added, the whole CI run is green on its branch, no `STUB(WP-nn)` of
  its own remains, and its definition of done holds. "The whole CI run" is the full run of 6.4, which a
  builder starts by hand on the package's `wp/**` branch; pushes to that branch get the quick run.
- Sizes are lines of Swift, sources plus tests, rounded.

### 7.2 Packages

**WP-01 Foundation** (first, alone)

| | |
|---|---|
| Goal | A repository in which every later package compiles against real declarations, and an app that launches into an empty shell on both simulator legs. |
| Starts from | Branch `spike/ios-ci`: its `ios/project.yml`, workflow, CI scripts, lint, entitlements file, `LiquidGlass.swift`, `Theme.swift` and `GroundView.swift` are known to build and run. They are kept; what the research document says differently is ignored (R-30). |
| Files | Everything marked WP-01 in section 2; and, as stubs, every other file of section 2. The removals listed at the end of section 2. `ios/project.yml` gains only: the target `LiveCueRemakeTests` (`bundle.unit-test`, sources `AppTests`, dependency on the app target, `GENERATE_INFOPLIST_FILE: "YES"`), and that target in the scheme's test list. The workflow's bundle check gains `--require-bundle LiveCueRemakeTests`, and the workflow gains the quick and full sizes of 6.4. |
| Provides | Every declaration of section 3 as compiling code. Complete: the domain model of 3.1.2 without the behaviours listed for WP-02; `PairingPayload`, `RelayEndpoint` and `JSONValue` (the fixture pairing needs them at launch); transport types and `CoreTiming`; `RelayConstants`, `RelayRoute`, `RelayFailure`; the error enums with their texts; `StreamTranscriptEvent`, `SpeechStreamTypes`, `MetaEvent`, `UsageUpdate`; the app protocols of 3.2; `KeychainStore`; `AppModel`, `SessionActivity`, `AppEnvironment` and both wirings; `LaunchConfiguration`, `TestMode`, `FixtureValues`; `InMemorySessionStore`; `TranscriberEventChannel`; `SystemClock`, `SystemClipboard`, `AppVersion`, `LiveMetaKeyTester`; `RootView`, `RouteDestination`; all test fakes of 3.1.12 and 3.2.11. Stubs: state owners and view models whose computed properties return fixed placeholder values and whose intents do nothing; screens that show their title in a `ScreenScaffold`; components that render their text plainly; core behaviours that return the cheapest valid value or throw the type's generic failure. No stub traps: the shell must launch. |
| Tests | `ContractSmokeTests`, `PairingPayloadTests`, `RelayEndpointTests`, `JSONValueTests`, `LaunchConfigurationTests`, `AppEnvironmentSmokeTests`, `KeychainStoreTests`, `ShellUITests`. |
| Done when | CI is green with all three bundles reported on both legs; the device build still produces an arm64 app with the bundle identifier of D-03; the lint passes; a list of every `STUB(WP-nn)` marker per package is posted. |
| Size | 5,500 |

**WP-02 Core model**

| | |
|---|---|
| Goal | The pure value logic of the core. |
| Files | Marked WP-02 in 2.2 and 2.3. |
| Consumes | Domain types, `RelayConstants`, `RelayRoute`. |
| Provides | 3.1.2 behaviours (`AssistantConfiguration` decoding and defaults, `supports`, `effort(keeping:)`, `PCModelStatus`, `APIKeyShape`, `Session+Lifecycle`, `TranscriptionUsage`), `LiveCueJSON`, 3.1.4, 3.1.11. |
| Tests | The WP-02 rows of 6.1. |
| Done when | P2-207, 208, 219 and the listed `AC-CORE` statements pass on Linux. |
| Size | 1,800 |

**WP-03 Core HTTP clients**

| | |
|---|---|
| Goal | The relay client and the OpenRouter client over `HTTPTransport`. |
| Files | Marked WP-03 in 2.2 and 2.3. |
| Consumes | Transport, domain, `RelayFailure`, `CoreTiming`, `JSONValue`, `RelayEndpoint`, `PairingPayload`; from WP-02 `WireBudget.fitted` and `encodedBody` and `LiveCueJSON`. |
| Provides | 3.1.5 `RelayMessages` and `RelayClient`; 3.1.10. |
| Tests | The WP-03 rows of 6.1. |
| Done when | P2-201, 204 to 206, 209 (core half), 210 to 212, 221 and the OpenRouter statements pass on Linux after WP-02 is merged. |
| Size | 2,200 |

**WP-04 Core streams**

| | |
|---|---|
| Goal | Caption merging and both streaming speech connections over `WebSocketTransport`. |
| Files | Marked WP-04 in 2.2 and 2.3. |
| Consumes | Transport, domain, `RelayFailure`, `MetaSpeechError`, `CoreTiming`, `JSONValue`, `RelayEndpoint`; from WP-02 `TranscriptionUsage`; from WP-03 `RelayMessages`. |
| Provides | 3.1.6 to 3.1.9. |
| Tests | The WP-04 rows of 6.1. |
| Done when | P2-213 to 218 and the listed statements pass on Linux after WP-02 and WP-03 are merged. |
| Size | 2,000 |

**WP-05 Design system**

| | |
|---|---|
| Goal | Daylight tokens, the glass wrapper additions and every component of 3.3, at every Dynamic Type size, with Reduce Transparency and Increase Contrast. |
| Files | `ios/App/DesignSystem/**`. |
| Consumes | `Route`, `AppClock`. |
| Provides | 3.3. |
| Tests | None of its own: a component cannot be asserted without a screen. Its evidence is the screenshots that the UI tests attach on both legs. |
| Done when | Both legs build; the lint of architecture 7.3 passes; `ShellUITests` screenshots show the ground, tab bar chrome and Start control on both legs; no view in the folder contains a literal colour, font size or radius. |
| Size | 2,200 |

**WP-06 Platform and persistence**

| | |
|---|---|
| Goal | Every adapter over an Apple API, and the stores. |
| Files | Marked WP-06 in 2.4 and 2.5. |
| Consumes | Transport protocols, the store protocols, `StorageNames`, `PermissionProviding`, `QRScanFailure`, `TestMode`, `FixtureValues`. |
| Provides | 3.2.1 adapters, 3.2.2 implementations, `TestSeamLaunchActions`. |
| Tests | The WP-06 row of 6.2. |
| Done when | Its tests pass on both legs; no file outside `Platform/` and `Persistence/` imports `Security`; no file imports `SwiftData` (R-33); no access group appears in any Keychain query. |
| Size | 1,700 |

**WP-07 Audio and speech**

| | |
|---|---|
| Goal | Capture, the four transcribers, the two engine adapters, model preparation and the speech fixtures. |
| Files | Marked WP-07 in 2.4 and 2.5. |
| Consumes | `AudioCapturing`, `SpeechTranscriber` and its types, `OnDeviceEngines`, `SpeechStreamSession`, `PendingAudioBuffer`, `SecretStoring`, `PairingStoring`, `AppClock`, `SessionActivity`, `BackupExclusion`. |
| Provides | 4.2, 4.5, 4.6; the speech rows of 4.9. |
| Tests | The WP-07 row of 6.2. |
| Done when | Its tests pass on both legs; `Voz` is imported only by `VozEngine.swift` and `FluidAudio` only by `ParakeetEngine.swift`; nothing writes audio to disk. |
| Size | 2,300 |

**WP-08 Session and answers**

| | |
|---|---|
| Goal | The session controller, the Assist pipeline, the summary, the two answer services and their fixture. |
| Files | Marked WP-08 in 2.4 and 2.5. |
| Consumes | `SpeechTranscriberFactory`, `AnswerService`, `RelayGateway`, the stores, `AppClock`, `AppModel`, `SessionActivity`, `SpeechSettings`, `AssistantSettings`, `RelayMonitor`, `OnDeviceModelManager`, `HistoryModel`, `ContextBuilder`, `WireBudget`, `OpenRouterClient`. |
| Provides | 3.2.6 implementations, 3.2.7, 4.1, 4.3, 4.4; the answer row of 4.9. |
| Tests | The WP-08 rows of 6.2. |
| Done when | Every row of 4.1.1 and every step of 4.3 has a passing test on both legs. |
| Size | 2,400 |

**WP-09 Connectivity and settings state**

| | |
|---|---|
| Goal | The remaining state owners and the relay fixture. |
| Files | Marked WP-09 in 2.4 and 2.5. |
| Consumes | `RelayGateway`, the stores, `AnswerServiceProviding`, `OpenRouterKeyVerifying`, `MetaKeyTesting`, `AppClock`, `SessionActivity`, `OnDeviceModelManager`, `RelayMessages`, `PairingPayload`. |
| Provides | 3.2.8 (`RelayMonitor`, `PairingController`, `AssistantSettings`, `SpeechSettings`, `HistoryModel`, `SystemReadiness`), 4.7; the relay rows of 4.9. |
| Tests | The WP-09 rows of 6.2. |
| Done when | Its tests pass on both legs, including "no relay request when the PC is not needed". |
| Size | 2,000 |

**WP-10 Screens A: Home, Live session, Assistant Lab**

| | |
|---|---|
| Goal | The three screens of the conversation path, exactly as screens 3, 4 and 5. |
| Files | `Features/Home/**`, `Features/LiveSession/**`, `Features/AssistantLab/**`; the WP-10 row of 2.5. |
| Consumes | 3.3; `SessionController`, `SpeechSettings`, `AssistantSettings`, `RelayMonitor`, `OnDeviceModelManager`, `SystemReadiness`, `HistoryModel`, `ToastCenter`, `ClipboardWriting`. |
| Provides | 5.2 to 5.4. |
| Tests | The WP-10 row of 6.2. |
| Done when | Every identifier of 5.2 to 5.4 is on its element; its tests pass on both legs; the feed keeps more than half the screen height with three answers at the default text size. |
| Size | 2,400 |

**WP-11 Screens B: Settings, Transcription, PC speech model, Meta key, Pairing, Model Library, Benchmark, History, Session detail**

| | |
|---|---|
| Goal | The remaining screens, exactly as screens 6 to 13 with the edits of R-11 to R-13. |
| Files | The other nine folders of `Features/`; the WP-11 row of 2.5. |
| Consumes | 3.3; `SpeechSettings`, `AssistantSettings`, `RelayMonitor`, `PairingController`, `OnDeviceModelManager`, `SystemReadiness`, `HistoryModel`, `SessionController`, `AppModel`, `QRScannerView`, `WordErrorRate`. |
| Provides | 5.5 to 5.12. |
| Tests | The WP-11 row of 6.2. |
| Done when | Every identifier of 5.5 to 5.12 is on its element; its tests pass on both legs. |
| Size | 2,600 |

**WP-12 UI tests** (last to merge)

| | |
|---|---|
| Goal | The ported UI tests and the glass gate. |
| Files | Marked WP-12 in 2.6. |
| Consumes | The identifiers of section 5, the arguments of 3.2.10, the fixture behaviour of 4.9, `UITestSupport`. |
| Provides | 6.3. |
| Done when | Every test of 6.3 passes on both legs (the glass gate on the iOS 26 leg), and each attaches the screenshots named for it. |
| Size | 1,600 |

### 7.3 Order and concurrency

After WP-01 lands, **WP-02 to WP-11 are built at the same time by ten builders.** They own disjoint files
and all compile against the stubs, so nobody waits to start and every branch gets compile feedback from CI
at once. What is ordered is merging, because a package's tests exercise the real behaviour of the packages
beneath it:

| Merge layer | Packages | Needs merged first | Why |
|---|---|---|---|
| 1 | WP-02, WP-05 | WP-01 | Nothing below them is a stub |
| 2 | WP-03, WP-04, WP-06 | WP-02 (and WP-03 before WP-04) | Fitting, usage and message texts; the session store encodes with `LiveCueJSON` and closes sessions with `closedAfterInterruption` |
| 3 | WP-07, then WP-09 | WP-04 for WP-07; WP-02, WP-03 and WP-07 for WP-09 | Stream session; home labels, relay texts, and readiness reads the model manager |
| 4 | WP-08 | WP-02, WP-03, WP-07, WP-09 | Its tests build the real state owners (`TestOwners`) |
| 5 | WP-10, WP-11 | WP-05, WP-07, WP-08, WP-09 | View-model tests read the real owners; screens use the real components |
| 6 | WP-12 | all | UI tests need every screen and fixture |

- A package in a later layer is finished when its code is written and its tests fail only where a stub of
  an earlier layer is still in place. It then rebases on `main` as its layer opens and must be green before
  it merges. The core packages are small and get their feedback from the one-minute Linux job, so layers
  1 and 2 close early.
- Because no file has two owners, a rebase can only conflict in a file the package does not own, which
  means a rule of 7.1 was broken.
- WP-12 is written during the same period by whoever finishes first (a core builder), against the
  identifiers of section 5 and the fixture behaviour of 4.9, and merges last.
- `ShellUITests` and the smoke tests of WP-01 stay green after every merge. That is the check that a
  half-merged tree still launches.

### 7.4 Hand-offs outside `ios/` and open items

| # | Item | For |
|---|---|---|
| H-1 | The scheme runs three bundles. The CI step that checks which bundles ran must require `LiveCueCoreTests`, `LiveCueRemakeTests` and `LiveCueRemakeUITests`. Done by WP-01 in `.github/workflows/ios.yml` (the quick run requires the first two); the CI plan carries it into `ci.yml`. | CI plan |
| H-2 | `GlassContrastUITests` runs only on the leg named `glass` (it skips itself when the system is older than iOS 26), and reads the screenshot it takes itself; it needs nothing from the workflow. | CI plan |
| H-3 | The synthetic token used by `CoreFixtures` and `FixtureValues` is the one of protocol 4.2 and must be on the audit allowlist (AUD-2). | CI plan |
| H-4 | Architecture 4.6, row "Hand-off queue": the on-device overflow clause is superseded by R-07. Architecture 4.9, last sentence, is moot (R-10). Architecture 4.4 and protocol 14.3 say "alert" where R-03, R-27 and R-28 now say notice. Architecture 4.7's `TranscriberEvent` is completed by R-24. Architecture 4.10's SwiftData store is replaced by R-33. Noted in `03-system-architecture.md` sections 4.4, 4.6, 4.7, 4.9 and 4.10 by WP-01. | Done |
| H-5 | The README must carry the two Voz statements of architecture 4.13 and the LiveContainer notes of 6.5 item 1. | Whoever writes the README |

---

## 8. Compile-risk checklist

Builders cannot compile locally. Each line is a mistake that would cost a CI cycle here, and the rule that
avoids it.

### 8.1 Availability

| Risk | Rule |
|---|---|
| An iOS 26 symbol outside the wrapper | Only `DesignSystem/LiquidGlass.swift` names `glassEffect`, `Glass`, `GlassEffectContainer`, `glassEffectID`, `.glass`, `.glassProminent`, `safeAreaBar`, `scrollEdgeEffectStyle`, `tabBarMinimizeBehavior`, `ToolbarSpacer`. Everywhere else call the wrapper (3.3.2). |
| A glass symbol in a comment or a string | The lint is a text search over every Swift file under `ios/`, tests included. Outside the wrapper, do not write those names even in a comment, a doc comment or a test name; say "the glass wrapper". Member paths such as `theme.sheen` are fine; a property literally named `glass` followed by a space or a parenthesis is not. |
| An iOS 26 type in a signature | Never. The wrapper's public signatures use only `Bool`, shapes and view builders. Inside the wrapper, name iOS 26 types only inside `if #available(iOS 26.0, *)`. |
| `?:` across the two availability branches | Their types differ. Use `if`/`else` inside a `@ViewBuilder` or a `ViewModifier` body. |
| An API newer than iOS 18.0 used ungated | The deployment target is 18.0. Allowed without a check: `Tab`, `@Entry`, Observation, `onScrollGeometryChange`, `ScrollPosition`, `sensoryFeedback`, `AVAudioApplication`, `onChange(of:) { old, new in }`, `TextField(axis:)`, `navigationDestination(for:)`. If the documentation says 18.1 or later, do not use it. |
| `Text + Text` | Deprecated in this SDK. Compose with interpolation: `Text("\(a)\(b)")`. |
| `@ContentBuilder`, the `@State` macro, `toolbarMinimizationBehavior` | Xcode 27 only. Write `@ViewBuilder`; use `@State` as a property wrapper. |

### 8.2 Observation

| Risk | Rule |
|---|---|
| Mixing the two observation systems | Only `@Observable`. No `ObservableObject`, `@Published`, `@StateObject`, `@ObservedObject`, `@EnvironmentObject`. Read with `@Environment(Type.self)`; bind with `@Bindable var model = model` inside `body`. |
| `@Observable` members the macro rejects | No `lazy var` and no property wrapper on a stored property of an `@Observable` class. Mark stored closures, tasks and dependencies that must not be tracked `@ObservationIgnored`; `let` properties need nothing. |
| A view model built inside a view | View models come from the environment. A screen never writes `@State private var model = SomeViewModel(...)`. |
| An `@Observable` class without an actor | Every one is `@MainActor`. |
| `.environment(_:)` with a non-observable object | That overload needs `AnyObject & Observable`. `AppEnvironment` is `@Observable` for this reason only. |

### 8.3 Main-actor isolation and `Sendable`

| Risk | Rule |
|---|---|
| Calling a `@MainActor` method from a nonisolated synchronous context | It is an error in Swift 5 mode too. Callbacks from AVFoundation, `NotificationCenter`, the speech packages and `URLSession` delegates are not on the main actor: copy the `Sendable` values you need, then `Task { @MainActor in … }`. |
| A closure created in a `@MainActor` method and run on another thread | Build the audio tap block in the `nonisolated static` function of `AudioTapConverter`, not inline in `AudioCapture`. |
| Delegate methods on a `@MainActor` class | Mark the delegate method `nonisolated`, extract `String` or other `Sendable` values there, and hop. (`QRScannerController.metadataOutput`.) |
| An actor conforming to a protocol with synchronous requirements | `nonisolated let provider`, `nonisolated var supportsSummary`. |
| Missing `await` | Every call into an actor (`Voz`, `StreamingEouAsrManager`, the stores, the services, `SpeechStreamSession`) and every `async` function needs `await`; a throwing one needs `try await` in that order. |
| Core package, Swift 6 mode: non-`Sendable` statics | No `static let` formatter, encoder, decoder, `Regex`, `NSRegularExpression`, `CharacterSet` built with a mutable object, or key path table. Make them per call, or write the logic by hand as 3.1.3 does for dates. |
| Core package: a public struct that is not `Sendable` | Public types do not get `Sendable` inferred. Write it. A struct that stores a closure is `Sendable` only if the closure type is `@Sendable`. |
| Core package: missing public initialiser | A public struct has no public memberwise initialiser. Declare every one printed in 3.1. |
| `@unchecked Sendable` | Allowed only for `AudioTapConverter`, the URL session delegate classes of WP-06, and the test fakes named in 3.2.11. Nowhere else, and never `nonisolated(unsafe)`. |
| `Task { }` versus `Task.detached` | `Task { }` inside a `@MainActor` type runs on the main actor; that is what the state owners want. Do not use `Task.detached`, `DispatchQueue.main.async` or `Timer`; sleep with `clock.sleep(seconds:)`. |
| A continuation resumed twice or never | Adapters that bridge a delegate to `async` (`URLSessionWebSocketTransport.open`) guard the continuation with a "resumed" flag under a lock and resume on every path, including cancellation. |
| `private(set)` state written from an extension in another file | Only the declaring file can assign a `private(set)` property. `SessionController+Assist.swift` and `SessionController+Summary.swift` are extensions in their own files: they change state through methods that `SessionController.swift` declares, never by assignment. The same holds for every state owner. |
| Capturing a local `var` in a `Task` | A `Task { }` closure may not mutate a captured local `var`. Keep the value in a property of the `@MainActor` owner, or in a small `@MainActor` class in a test. |
| `await` inside `XCTAssert…` | The arguments are autoclosures that cannot suspend. Read the value into a `let` first, then assert. |
| Warnings as errors | The app target is Swift 5 mode so that isolation findings are warnings. Never set `SWIFT_TREAT_WARNINGS_AS_ERRORS` or `-warnings-as-errors`. Deprecation warnings (for example `.allowBluetooth`) are accepted. |

### 8.4 SwiftUI

| Risk | Rule |
|---|---|
| "Unable to type-check this expression in reasonable time" | Keep each `body` under about forty lines. Move sections into separate `View` structs or `@ViewBuilder private var` properties. No arithmetic, string building or ternary chains inside a view expression: compute them in the view model. |
| `some View` with two return types | A function that returns different views on different paths is marked `@ViewBuilder` and uses `if`/`switch`, with no `return`. No `AnyView`. |
| Closure parameter types | Give explicit parameter types to closures passed to generic initialisers when the compiler has more than one candidate (`ForEach`, `.onChange`, `.task(id:)`). |
| Generic component initialisers | Call them exactly as printed in 3.3.3. `LCNavigationRow` without a subtitle uses the `EmptyView` overload; do not pass `{ EmptyView() }`. |
| `ForEach` identity | Iterate `Identifiable` values or give `id: \.self` for strings; never iterate indices of a mutable array. |
| An identifier on a container | `.accessibilityIdentifier` on a stack spreads to every child. Put it on the leaf control, pass it to the component's `identifier` parameter, or make the container an element with `.accessibilityElement(children: .contain)` (used once, for `assistant-provider`). |
| An identifier inside a `Button` label | It is not exposed. That is why `LCNavigationRow` keeps its subtitle outside the link. |
| Modal state | One `.alert` at the root for the global alert, one on History for the delete confirmation, one on `PairingScreen` for the "Replace this PC?" confirmation (5.9), one `.sheet` on Pairing. Do not attach a second alert to the same view. |
| Forced light appearance | Only the root sets `.preferredColorScheme(.light)`. No view reads `colorScheme`. |
| Fixed sizes that hold text | Use the theme's sizes as minimum heights (`frame(minHeight:)`), never as fixed heights, so Dynamic Type can grow them. |

### 8.5 Packages and frameworks

| Risk | Rule |
|---|---|
| `DownloadProgress` is ambiguous | `Voz` and `FluidAudio` both export it. Never import both in one file; each has exactly one importing file. Do not qualify names with the module `Voz`: the module and its main type share the name. |
| FluidAudio calls | `StreamingEouAsrManager` is an actor: `await manager.reset()`, `await manager.setPartialCallback { … }`, `_ = try await manager.process(audioBuffer:)`, `try await manager.finish()`, `await manager.cleanup()`. `MLModelConfiguration` needs `import CoreML`; `AVAudioPCMBuffer` needs `import AVFoundation`. Read signatures from research 3.3, not from the package README. |
| Voz calls | `try await Voz(progress:)`; `try await voz.transcribe(samples:)` (it is `throws` on an actor, so `try await`). The result's `words` have `text`, `start`, `end`. |
| Progress handlers | Both packages call them off the main actor and they are `@Sendable`: capture only `Sendable` values and hop. |
| Session files | No `import SwiftData` anywhere (R-33). `FileSessionStore` is an actor; `FileManager` and `Data.write(to:options:)` are called inside it with `.atomic` and `.completeFileProtectionUntilFirstUserAuthentication`. Build each file name from `id.uuidString`, never from text that came out of a file. A file that does not decode is skipped, not deleted. |
| Keychain | Build queries as `[String: Any]` and cast with `as CFDictionary`. No `kSecAttrAccessGroup`. Treat `errSecItemNotFound` on delete as success. |
| `URLSession` | Create sessions from `URLSessionConfiguration.ephemeral` with `urlCache = nil`, `httpCookieStorage = nil`, `httpShouldSetCookies = false`. The redirect delegate calls its completion handler with `nil`. `URLSession.bytes(for:)` appears only in `URLSessionHTTPTransport.swift`. |
| The core on Linux | `import Foundation` only: no `FoundationNetworking`, `URLSession`, `os`, `OSLog`, `CryptoKit`, `Combine`, `Dispatch` queues or `autoreleasepool`. No `Bundle.module` resources in tests: fixtures are string literals. No locale-dependent formatting or comparison (`localizedCaseInsensitiveCompare`, `Date.formatted`). |
| XCTest on Linux | `async throws` test methods are fine. Do not use `XCTestExpectation`, `@MainActor` test methods or `measure` in the core tests; wait with the fakes' `async` helpers. |
| Duplicate file names | Two files with the same name in one target fail the build. Keep to the names of section 2. |
| XcodeGen | Quote `"YES"` and `"NO"`. Sources are picked up by folder, so a new file needs no project edit; a new folder outside `ios/App`, `ios/AppTests`, `ios/UITests` is not compiled. |
| UI tests | Mark each test method `@MainActor`. Set `continueAfterFailure = false`. Wait with `waitForExistence(timeout:)`, never `sleep`. Look elements up with `UITestSupport.element(_:)`, because a chip or banner is not always a `staticText`. |

### 8.6 Text

| Risk | Rule |
|---|---|
| A string that differs by one character from the test's expectation | Copy strings from the spec, protocol 14 and 1.3; do not retype them. The ellipsis is the single character "…", the separator is "·" (U+00B7), the arrow in the timing view is "→". |
| Locale in numbers | Costs, seconds and percentages use `String(format:)` with `Locale(identifier: "en_US_POSIX")`, or the core's own formatters. |
| Byte budgets counted in characters | Budgets of protocol 13.4 are UTF-8 bytes (`utf8.count`), cut on `Character` boundaries. Only the four limits that the specs give in characters (title 48, instruction 4,000, OpenRouter memory 6,000, Meta transcript 32,000) use `count`. |
| Text that came from a model or from the relay turning into a link | Answer, details, detected question, summary, key points, streamed answer text, the PC status `message`, and a relay text shown for an unknown code are drawn with `Text(verbatim:)`. Never build such text through a string-literal `Text("\(value)")` (that is a `LocalizedStringKey`, which parses Markdown), `AttributedString(markdown:)`, `Link` or `openURL`. `scripts/ci/lint_swift.sh` fails when `AttributedString(markdown` appears in a Swift file under `ios/App`; two unit-test files prove the rendering (6.2, ST-26). A prompt-injected answer must not be able to put a tappable link on the phone. |

---

## 9. Changes after review

Two reviews changed this document after WP-01 had started from its earlier text: the cross-check of the
protocol against both designs (items `X-nn` and `A-nn`) and the security review `07-security-review.md`
(findings `SEC-nn`, tests `ST-nn`). The rulings are in decisions section 8. The sections above already hold
the new text. This section says, per work package, what its builder must do **differently from the text as
it stood before**. 9.1 lists every change to a contract declaration, with its file; WP-01 creates those
files, so WP-01 makes those changes even where another package owns the behaviour.

### 9.1 Contract declarations that change

Core paths are under `ios/LiveCueCore/Sources/LiveCueCore/`, app paths under `ios/App/`. "Comment" means the
signature is unchanged and only its documentation, and with it the required behaviour, changed.

| # | File | Change | From |
|---|---|---|---|
| 1 | `Relay/RelayEndpoint.swift` | New member `public var host: String { get }`: the lower-case host of `canonical` | SEC-13 |
| 2 | `Domain/PairingPayload.swift` | Comment, and behaviour of a type WP-01 completes: V-4 is a whole-text check by a character loop; `manual` does not trim the token; a token followed by a line break is invalid | SEC-08 |
| 3 | `Domain/SessionMemory.swift` | Comment of `throughSegmentId`: a relay reply is never decoded directly | X-01 |
| 4 | `Relay/RelayClient.swift` | Comment of `cancel(requestId:pairing:)`: the path carries `requestId.uuidString`. No signature changes; the new rules of 3.1.5 are behaviour (WP-03) | A-13, A-01, X-01 |
| 5 | `Relay/RelaySpeechFrame.swift` | Comment of `parse`: a `loading` frame with a missing member is still `loading` | A-14 |
| 6 | `Relay/RelaySpeechNegotiation.swift` | Comment of `receive(text:)`: `.unknown` is ignored in every state; the four lab types before `ready` are `L-UNEXPECTED-FRAME` | A-03 |
| 7 | `Transcript/SpeechStreamSession.swift` | Comment of `start()`: when C-59 passes, the socket is closed and the failure is `.relay(.local(.timeout, origin: .stream))` | A-02 |
| 8 | `Persistence/PairingStoring.swift` | Comments of `load()` and `save(_:)`: the order is reversed. The Keychain item is the authority and the file the fallback; the Keychain is never written from the file | SEC-03 |
| 9 | `Connectivity/ConnectivityTypes.swift` | New struct `PairingReplacement { let currentHost: String; let newHost: String; var newHostIsTailscaleName: Bool { get } }`, `Equatable`, `Sendable` | SEC-13 |
| 10 | `Connectivity/PairingController.swift` | New method `func replacement(forAddress address: String) -> PairingReplacement?` | SEC-13 |
| 11 | `Features/Pairing/PairingViewModel.swift` | New members `var replacementPrompt: PairingReplacement? { get }`, `func confirmReplacement() async`, `func cancelReplacement()` | SEC-13 |
| 12 | `Platform/URLSessionWebSocketTransport.swift` | Comment: the upgrade request keeps an idle timeout of 60 s or more | A-01 |

No declaration of the transport (3.1.1), of the domain requests and replies, of `RelayFailure` and its two
code enums, of the store protocols' signatures, of `RelayGateway` or of any state owner other than the two
above changed. No wire code and no `L-` condition was added: `L-TIMEOUT` and `L-STREAM-DROPPED` gained one
more use each (protocol 14.2, 13.7).

Files that are not declarations and change too: a new committed file `ios/Package.resolved`;
`scripts/ci/generate_project.sh` (checks the XcodeGen version, copies the resolution file into the generated
project, resolves with automatic resolution disabled, fails when the file changed);
`scripts/ci/lint_swift.sh` (fails on `AttributedString(markdown` under `ios/App`).

### 9.2 What each package does differently

**WP-01 Foundation**

- Makes every change of 9.1, as declarations and stubs: `RelayEndpoint.host` and `PairingReplacement`
  complete; `PairingController.replacement(forAddress:)` returning nil; the three `PairingViewModel` members
  doing nothing.
- `PairingPayload`: the token check is a loop over exactly 43 characters of the alphabet; nothing is trimmed
  from the token, in `parse` or in `manual`.
- Tests it owns gain cases (6.1): `PairingPayloadTests` and `RelayEndpointTests` (ST-13, a trailing line
  feed, carriage return and line feed, U+2028; `host`).
- Commits `ios/Package.resolved` and changes the two scripts named above.

**WP-02 Core model**

- `WireBudgetTests` gains the plain ASCII case in which every member is at its step 1 budget (X-13). No
  source change: `fitted` already runs step 3 whenever step 2 does not fit.

**WP-03 Core HTTP clients**

- Every relay request is built with `HTTPRequest.timeout` = the route's phone timeout plus 10 s (A-01).
- The assist reply is parsed with `JSONValue`, `memory.throughSegmentId` is removed, and `AssistResponse` is
  decoded from the rest; the synthesised decoder never sees the relay's marker (X-01). The relay sends
  `null` there, and the client must still accept any string.
- The cancel path is built from `requestId.uuidString` (A-13).
- A summary reply's `execution` is not read; only assist replies are checked for the echo (A-06). A
  `409 REQUEST_CANCELLED` that is read is an ordinary wire code (A-10). A health 200 with `protocol` 2 and
  `ok` not true is `L-UNREADABLE` (A-05; the declaration already said so).
- Added cases in `RelayClientRoutesTests` (6.1).

**WP-04 Core streams**

- `RelaySpeechFrame.parse`: `loading` with a missing or wrongly typed member is `.loading` with 0 or ""
  (A-14).
- `RelaySpeechNegotiation.receive`: `.unknown` gives `Step.ignore` before and after `ready`; `speechStart`,
  `transcript`, `speechComplete` and `streamComplete` before `ready`, and text that is not a frame, throw
  `L-UNEXPECTED-FRAME` (A-03).
- `SpeechStreamSession`: when C-59 passes during `start()` for the relay dialect, close the socket and throw
  `.relay(.local(.timeout, origin: .stream))`; when C-60 passes in `finish()` with no close, the ending is
  interrupted with `L-STREAM-DROPPED` (A-02). The Meta dialect's limits are untouched.
- Added cases in `RelaySpeechFrameTests`, `RelaySpeechNegotiationTests` and `SpeechStreamSessionRelayTests`
  (6.1).

**WP-05 Design system**

- Nothing changes.

**WP-06 Platform and persistence**

- `FilePairingStore`: load and save in the new order of 3.2.2. It no longer copies the file's pairing into
  the Keychain at load; it rewrites the file from the Keychain instead. `FilePairingStoreTests` is rewritten
  to the row "as changed by the review" of 6.2 (ST-07).
- `URLSessionWebSocketTransport`: the upgrade request's idle timeout is 60 s or more (A-01).
  `URLSessionHTTPTransport` keeps using `HTTPRequest.timeout` as the request's idle timeout.

**WP-07 Audio and speech**

- Nothing changes in its files. A relay stream that times out while loading now fails the start with the
  `L-TIMEOUT` text through the existing failure path.

**WP-08 Session and answers**

- Nothing changes. The marker rule of 4.3 step 10 already ignored the reply's marker; a Codex reply now
  always arrives with a nil marker.

**WP-09 Connectivity and settings state**

- `PairingController.replacement(forAddress:)` (3.2.8): pure, from the saved pairing's
  `relayEndpoint.host` and the host of the address after the manual-entry rules.
- `FixtureRelayGateway`: with `pcOffline`, health throws `RelayFailure.local(.unreachable)` (X-09), so the
  saved-PC message becomes the "Your pairing is saved. " composition of the `L-UNREACHABLE` text; the PC
  model fixture starts `stopped` with MSG-3 (X-10).
- Adds the `PairingControllerTests` row of 6.2 (ST-18).

**WP-10 Screens A**

- Answer, details, detected question, streamed answer text and the Assist error message are drawn with
  `Text(verbatim:)` (8.6). Adds `AnswerPlainTextTests` (ST-26).

**WP-11 Screens B**

- `PairingViewModel` and `PairingScreen`: the "Replace this PC?" confirmation of 5.9, as a local alert on
  the Pairing screen. Adds the `PairingViewModelTests` row of 6.2 (ST-18).
- The PC status `message`, the Saved PC message and every line that can show a relay text for an unknown
  code are drawn with `Text(verbatim:)` (8.6). Adds `RelayTextPlainTextTests` (ST-26).

**WP-12 UI tests**

- No new UI test. The two tests that launch with `-pc-offline` must not expect the text "PC offline." with a
  full stop anywhere: that fixture text no longer exists (X-09). The Home label "PC offline", without a full
  stop, is a different string and is unchanged.

### 9.3 Hand-offs outside `ios/`

| # | Item | For |
|---|---|---|
| R-1 | Device check 10 of 6.5 now includes a full minute of silence on a PC speech stream, because the relay's keep-alive frame is an unsolicited Pong (X-04) | The owner |
| R-2 | The "Replace this PC?" dialog is new and is not in `02-ios-screens.md` yet; its strings are in 5.9 and in decisions 6.1 | The screen designer |
| R-3 | README: inside LiveContainer, other guest apps can read the fallback pairing file, the stored sessions and the preferences (architecture 6.5) | Whoever writes the README |
| R-4 | The CI plan carries the XcodeGen version check, the committed `ios/Package.resolved` and the Markdown lint into `ci.yml` (test plan 5.2, 5.5) | CI plan |
