# 07. Security review of the design

Status: advisory. A review of the design before anything is built. Nothing here is binding until the
orchestrator rules on it; every fix names the text it would change. Companion files are cited as "protocol"
(`02-relay-protocol.md`), "architecture" (`03-system-architecture.md`), "iPhone design"
(`04-ios-architecture.md`), "W-design" (`05-windows-architecture.md`), "test plan"
(`06-test-and-ci-plan.md`) and "decisions" (`01-decisions.md`).

Read for this review: decisions sections 1 to 3 and 7; the whole protocol; architecture sections 3 to 7; the
iPhone design's pairing storage, Keychain, relay client and QR parts; W-design sections 3.2 to 3.6 and 4, and
the pairing, activity and settings pages of section 5; test plan sections 3 to 5; `spec/03-relay.md` sections
3, 6 and 10.

Method: the design was read five times, each time as a different attacker: a caller on the internet with no
token; someone who briefly sees the QR or a copy of the pairing data; a web page open in a browser on the PC
or the phone; another program on the PC; and a hostile speaker in a transcribed conversation. **Nothing was
run**: no scan, no Tailscale, Docker or Codex command, no server. Where a finding depends on how Codex,
Kestrel, Tailscale or a browser behaves, that behaviour is stated from the reviewer's knowledge and marked
"to confirm".

Contents

1. [Summary](#1-summary)
2. [Findings](#2-findings)
3. [Checked and found sound](#3-checked-and-found-sound)
4. [Security tests to add](#4-security-tests-to-add)
5. [Questions for the owner](#5-questions-for-the-owner)

---

## 1. Summary

### 1.1 Verdict

**Private mode: fit, once the low-severity fixes are in. Public mode: not yet.**

The part that faces the internet is the strongest part of the design. Before the token is accepted the relay
parses headers, compares a hash and writes one pre-built answer; it reads no body, touches no file, starts no
process and tells the desktop nothing (protocol 5.2). Routes are an exact allowlist on the raw request line.
There is no cookie, no unauthenticated route and no control route. In the design I found no way for a caller
without the token to pass step E-3, to make the relay do work, or to learn more than "a relay is running
here".

The weaknesses are on either side of that gate:

- **The one credential is treated less carefully than the gate that checks it.** It never expires, and it is
  drawn on the PC screen as a QR (SEC-02), kept on the phone in a file that other apps in the same container
  can read and replace (SEC-03), and printed by the console host (SEC-11).
- **What the credential unlocks is more than the design says.** Behind it is a coding agent that runs as the
  owner. The text says "no tools"; the command line switches off one tool, the "read-only sandbox" does not
  restrict reading, the agent inherits the owner's environment, and the agent binary updates itself (SEC-01).
- **What is exposed can drift from what the owner believes.** The warning for a leftover public mapping
  depends on a ledger that the documented first-time Funnel flow does not write (SEC-04).

No finding is critical: none gives a caller without the token anything. Two are high because, chained, a
glimpse of the QR becomes read access to files in the owner's Windows profile from anywhere on the internet.

### 1.2 The three changes that matter most

| # | Change | Findings |
|---|---|---|
| 1 | **Make Codex containment a checked property instead of a flag.** Switch off every tool explicitly, give the agent an allowlisted environment, verify the tool list of the installed Codex without a model call, tie the result to the hash of `codex.exe`, and refuse answers in Public mode until that check has passed for the binary in use. | SEC-01 |
| 2 | **Stop handing out the permanent token.** The QR (and the console host) carry a single-use enrolment code that lives for the 120 s the QR is shown; the phone exchanges it for the token over TLS. On the phone the Keychain is the authority for the pairing and the file is a fallback only. | SEC-02, SEC-03, SEC-11 |
| 3 | **Make "who can reach the relay" true and fail-safe.** Derive the exposure warning from Tailscale's live configuration, not from the ledger; do not run in Private mode while a Funnel handler points at the relay; keep tailnet-only listeners off Funnel-capable ports. | SEC-04, SEC-05 |

### 1.3 Findings at a glance

| Id | Severity | Finding |
|---|---|---|
| SEC-01 | high | Codex is contained by a deny-list of one tool; nothing verifies it and the binary updates itself |
| SEC-02 | high (Public), medium (Private) | The QR shows the permanent bearer token |
| SEC-03 | medium | On the phone the pairing file is the authority; other apps in the container can read and replace it |
| SEC-04 | medium | A public mapping can outlive the owner's intent without any warning |
| SEC-05 | low (medium on a shared PC) | While LiveCue is closed, any local program can take the relay port and collect the token |
| SEC-06 | low | Launcher scripts are run from a configurable folder with the execution policy bypassed |
| SEC-07 | low | "Reset pairing" has a race: verify and revocation are read separately |
| SEC-08 | low | Shape checks written `^…$` accept a trailing line feed, including in code marked complete |
| SEC-09 | low | The lab's plain HTTP routes have no `Host` rule; the container has open outbound network |
| SEC-10 | low | Conversation text reaches disk in places the "never written to disk" statements do not cover |
| SEC-11 | low | `--show-pairing` prints the permanent token to standard output |
| SEC-12 | low | Release inputs are not pinned and a release can be replaced under the same tag |
| SEC-13 | low | Pairing to a different host needs no confirmation (hostile QR) |
| SEC-14 | note | The owner cannot see that the public address is being probed |
| SEC-15 | note | One framing test expects behaviour Kestrel may not have; the property to protect is stated here |

Counts: critical 0, high 2, medium 2, low 9, note 2.

### 1.4 Recommendation on Public mode

Keep Private as the default (it already is) and **hold Public back** until all of these are true:

1. SEC-01: the tool-list check (test ST-01) and the canary run (ST-02) pass against the Codex installed on
   the owner's PC, and the relay enforces the check per `codex.exe` hash.
2. SEC-02: the enrolment code is in, or the owner has accepted the interim measures and their residual risk
   in writing (question 2).
3. SEC-04: the exposure warning is driven by live inspection, and Private mode refuses to run beside a public
   mapping.
4. Test plan section 4 and the tests of section 4 below pass on the published build, and the manual probe
   through a real Funnel mount (ST-27) has been run once from outside the tailnet.

SEC-03 should land before Public mode too, because the shipped iPhone build is meant for an app container
that does not isolate its guests, and a token read there is usable from the internet in Public mode.

---

## 2. Findings

Severity scale, by what the attacker needs and what they get:

| Level | Meaning |
|---|---|
| critical | A caller with no token and no access to the PC or phone gets data or code execution |
| high | A realistic single step by an outsider leads to the owner's files, credentials or conversations |
| medium | Needs a foothold the owner is likely to grant without thinking of it as trust, or silently defeats a protection the owner relies on |
| low | Needs local access the design already mostly trusts, or has small impact |
| note | Not a weakness by itself; a gap in evidence or visibility |

### SEC-01. Codex is contained by a deny-list of one tool

**Severity: high.** Anyone who holds the token (and, less reliably, anyone whose speech is transcribed)
controls up to 128 KiB of input to an agent that runs as the owner. If any tool other than the shell is
available to that agent, the result is disclosure of files in the owner's profile, including the Codex
sign-in. The design does not show that none is available, and nothing would notice if one appeared.

**Where:** protocol 12 (common process rules), 12.2, 12.4; architecture 3.7, 6.1 (row "Relay to Codex"), 6.4
(row "Prompt injection in a transcript"); W-design 3.2 (`EnvironmentPolicy.WithoutLiveCueVariables`), 4.9,
4.14 (`CodexLocator`); decisions section 7 risk 5; test plan L-1.

What the text relies on, and why each is weaker than it reads:

| Control in the text | Weakness |
|---|---|
| `--disable shell_tool` | A deny-list of one. Codex releases have carried other tools: viewing an image by local path, applying patches, web search, configured tool servers, and file-reading tools offered to some models (to confirm for the installed version). Protocol 12.4 and architecture 6.4 say "no tools"; the arguments say "no shell". Also to confirm: whether an unknown feature name is an error or only a warning. If it is a warning, a renamed feature silently brings the shell back, and Codex runs commands it classes as harmless readers without asking. |
| `--sandbox read-only` | A read-only sandbox allows reading every file the user can read. It limits writing and network access for commands the agent spawns; tools that run inside the Codex process are not inside it. On Windows its enforcement depends on user configuration that `--ignore-user-config` discards (to confirm). It gives no confidentiality at all. |
| Empty working folder, `-C` | Tools take absolute paths. The folder limits nothing. |
| Environment "minus `LIVECUE_`" | The agent's process holds every secret the owner keeps in environment variables. The lab scripts get an allowlist of thirteen names; the agent, which faces hostile text, gets everything. |
| A fixed executable | Architecture 3.7 rule 3 picks the most recently modified `codex.exe`. Codex updates itself. The tool set can change between two requests with no change in LiveCue. |
| "Untrusted data" framing in the prompt | Advice to the model. Worth keeping; not a control. |

**Scenario A (token holder).** An attacker obtains the token through SEC-02 or SEC-03 and, in Public mode,
posts `/v1/assist` from anywhere. The whole body is theirs, so `transcript` is a page of instructions: view
the image at a given path, read a given file, search the web for a given string. They may retry without limit,
one request at a time. Whatever the agent reads comes back in `answer` or `details` (the relay forwards up to
1 MiB). First targets: the sign-in file in the Codex home folder, private keys, saved screenshots.

**Scenario B (hostile speaker).** A participant in a meeting says the instructions aloud; they arrive as
`transcript`. The output goes to the owner's phone, not to the speaker, so this needs a tool with network
reach to pay off. It needs no credential.

**Fix.**

1. Protocol 12.2, arguments: replace the single `--disable shell_tool` by the rule "every tool the installed
   Codex can offer is switched off by an explicit argument", list those arguments (shell, patch application,
   image viewing, web search, tool servers, project instruction files), and add: "an argument the installed
   Codex does not recognise must fail the run; if Codex only warns, the relay treats that Codex as
   unverified".
2. New protocol 12.6, "Tool list check". Before a `codex.exe` is used for answers, the relay runs the exact
   command line of 12.2 once with the model service replaced by a listener on `127.0.0.1` (a provider
   override passed with `-c`; to confirm that the installed Codex accepts one), reads the tool list in the
   request Codex sends, and requires it to be empty or equal to a fixed allowlist. No model is called. The
   result is stored in `settings.json` as `{sha256 of codex.exe, version text, time}`. If the override is not
   possible, the fallback is the canary run of test ST-02 on the owner's explicit "Check assistant".
3. New rule in protocol 8.2 between R-5 and R-6: in Public mode, when the resolved `codex.exe` has no passed
   check for its current hash, answer `503 ASSISTANT_NOT_VERIFIED` (a new code in 14.1). In Private mode the
   desktop shows a standing warning instead. The Settings row "Codex path" shows the version and whether it
   is verified.
4. Protocol 12, common process rules, and W-design 3.2: replace `WithoutLiveCueVariables` for Codex by an
   allowlist: the thirteen names of protocol 11.5 plus `HOMEDRIVE`, `HOMEPATH`, `CODEX_HOME`, and the proxy
   variables when set. Nothing else reaches the agent.
5. Protocol 12.4 and architecture 6.1 and 6.4: replace "tools disabled" and "no tools" by what is checked:
   "the tool list of the installed Codex is verified to be empty (12.6); the sandbox flag is not relied on for
   confidentiality".
6. Protocol C-06: add a length limit (64) to the model id shape. It ends up on a command line.

OS-level containment of the Codex process (a restricted token that cannot read the profile) would be the
stronger answer, but Codex must read its own sign-in, so it is not a small change. It is question 3.

### SEC-02. The QR shows the permanent bearer token

**Severity: high in Public mode, medium in Private.** Whoever sees the screen for a moment (a shared screen
in a call, a recording, a photo, a capture tool, operating-system screen history) gets a credential that
never expires and, in Public mode, works from anywhere. In Private mode they also need a device in the
tailnet. What it unlocks is everything in protocol section 6, and through SEC-01 possibly more.

**Where:** protocol 4.1 (lifetime unlimited), 4.2, 4.4 (first start and reset show the QR without being
asked), 4.5; architecture 3.12 (QR machine, 120 s), 6.2, 6.5; W-design 5.6; decisions D-15, A-16.

**Scenario.** The owner starts LiveCue for the first time while sharing their screen; the QR opens by itself
and stays for 120 s. A viewer decodes the frame: endpoint and token. From then on the viewer's requests are
indistinguishable from the phone's: `PhoneSeen` carries no identity, route 2 "records nothing", there is no
device list. The owner sees "iPhone connected" and has no reason to reset. The same holds each time "Show QR"
is used, because it redisplays the same token by design. The paste path has the same problem in slower
motion: the pairing text must travel from the PC to the phone through some channel the owner improvises.

**Fix (protocol change).** The secret on screen becomes a single-use enrolment code.

| Text | Change |
|---|---|
| Protocol 4.2 | The payload is `{"endpoint":…,"code":<43 characters, shape C-02>}`. The code is 32 random bytes, held in memory only, valid until its first successful use, until the QR is hidden, until 120 s have passed, or until the relay stops, whichever comes first. One code at a time. |
| Protocol 4.1, E-3 | "Accepted tokens: exactly one; and, while a code is live, that code, for the one request `POST /v1/pair/verify`." A request that presents the code with any other method or target fails E-3 with the same 401. |
| Protocol 6.3 | A code-authenticated call answers `200 {"ok":true,"token":"<token>"}` and consumes the code before the response is written. A token-authenticated call keeps `{"ok":true}`. |
| Protocol 4.4, 13 | The phone saves `{endpoint, token}` from the response; it never stores the code. V-4 applies to `code` in the payload and to `token` in the response. |
| Architecture 3.4, 3.12 | A new event `PairingCodeUsed`; the desktop hides the QR at once and writes "An iPhone paired just now." If the owner's own scan then fails with 401, someone else used the code: the hint says to reset pairing. |
| Architecture 3.14 | `--show-pairing` prints the code, not the token (this also closes SEC-11). |

This keeps what D-15 wanted: phones already paired stay paired, there is still one shared token and no device
list. It gives up one sentence of protocol 4.2 ("the QR, the pasted payload and the saved pairing are the same
JSON object") and changes P2-142 and P2-144. The DPAPI copy stays, because the relay must hand the token to a
new phone.

**If the owner declines** (question 2), the minimum is: the desktop window is excluded from screen capture
while the QR is shown (`SetWindowDisplayAffinity`, exclude-from-capture); the QR is never shown without a
click, including on the first start and after a reset; the QR hides when the window loses focus; and the
pairing hint says "If anyone else could have seen or captured this code, reset pairing." The risk of a photo
remains and should be added to architecture 6.5.

### SEC-03. On the phone the pairing file is the authority

**Severity: medium.** The attacker is another app in the same LiveContainer, which the owner installed but
would not knowingly trust with their conversations. It can read the token (usable from the internet in Public
mode) and, worse, redirect the app to its own server without any prompt.

**Where:** architecture 4.10 ("Pairing load order: file, then (refreshing the Keychain copy from it) the
Keychain copy"; Files table), 6.1 (row "LiveContainer guests"), 6.5 (fourth bullet); iPhone design 3.2.2
(`PairingStoring`, `FilePairingStore`) and its save-order table ("File (must succeed), then the Keychain
copy"); platform research section 5 (guest containers are not isolated from each other).

**Scenario.** A second guest app walks the sibling data folders and finds `pairedPC.json`.

- Read: it now has endpoint and token. Architecture 6.5 accepts this. In Public mode it makes the guest's
  author an internet token holder (see SEC-01).
- Write, which 6.5 does not consider: it replaces the file with a payload naming its own HTTPS host and any
  43-character token. At the next launch LiveCue loads the file, passes it through the grammar (which accepts
  any host), **overwrites the good Keychain copy with it**, and starts polling health on the new host, which
  answers 200. From then on every assist (transcript window, memory, instruction), every summary (the whole
  transcript) and, with a PC speech provider, the live microphone stream go to that host, which also writes
  the answers the owner reads. The only visible sign is the address in the Saved PC panel.

**Fix (architecture 4.10 and iPhone design 3.2.2).**

| Level | Rule |
|---|---|
| Minimum (integrity) | Load order: Keychain first. The file is read only when the Keychain has no item or the read fails. The Keychain copy is never refreshed from the file. When both exist and differ, the Keychain wins and the file is rewritten from it. |
| Full (confidentiality too) | Save order: Keychain first. The file is written **only when the Keychain save fails**; when it succeeds, an existing file is deleted. A pairing that was loaded from the file is marked as such, and the Saved PC panel says: "Pairing read from app storage because the Keychain is unavailable. Other apps in the same container can read and change it." |

The full level costs what the architects wanted to avoid: if the Keychain works on the day of pairing and
fails later, the phone is unpaired until the QR is scanned again. That trade is question 5. Architecture 6.5
should also say what is true at either level: inside LiveContainer every other guest can read the session
history (the SwiftData store) and the preferences.

### SEC-04. A public mapping can outlive the owner's intent without any warning

**Severity: medium.** The attacker is anyone on the internet, and what they get is small: the relay's parser
and its token check, answering 401. The finding is that the owner can believe the relay is tailnet-only while
it is not, and that the one technical guard for that case passes when the thing it tests for is missing.

**Where:** architecture 3.5 (`privateHttpsPort` rule; "runs on defaults until the user saves settings"), 3.9
(TS-8, TS-9, TS-10; the row "Our mapping for the other mode still exists"), 3.11, 6.3, 6.4 (row "Mode
confusion"); W-design 4.15 (`StaleOwned`; "If the process dies between steps 5 and 8"), 5.8 ("Mapping status
for the selected mode"), 5.12; protocol 4.5, E-1; decisions section 1 item 6, A-10, section 7 risk 3.

**Scenarios.**

1. First Public set-up. Funnel must be approved in the Tailscale admin console, so the CLI waits and the
   write times out. TS-10 then tells the owner to run the shown command in a terminal. They do. The mapping
   exists but is not in the ledger (`PresentNotOwned`). Later the owner switches to Private. The "stale
   mapping" warning is computed from ledger entries only (`StaleOwned`), and the Settings page shows the
   status of the selected mode only. No warning, no "Remove". The Funnel mount stays for good.
2. The same state follows a process death between steps 5 and 8 of `ExecuteAsync`, and an unreadable or
   invalid `settings.json` followed by a save (the ledger is replaced by an empty list).
3. `privateHttpsPort` may be 443, 8443 or 10000 as long as it differs from the public port. A tailnet-only
   root handler then sits on a port where any other tool can later switch Funnel on, publishing it.
4. The application is removed by deleting its folder. The mount keeps publishing a loopback port that is now
   free (SEC-05).
5. In all of these the only guard is E-1: Private mode refuses requests that carry
   `Tailscale-Funnel-Request`. Decisions risk 3 records that this header was read from source, not
   exercised. If a Tailscale release renames or drops it, the guard admits everything and nothing fails.
6. The self-check cannot settle it either way. In Public mode the PC's request to its own name probably takes
   the tailnet path (to confirm), so "verified" does not show the public path works; in Private mode no
   check from the PC can show that a public path is absent.

**Fix.**

| Text | Change |
|---|---|
| Architecture 3.9, new "Exposure report" | From every serve-status read, list every handler, on any port and mount and in any structure, whose `Proxy` is `http://127.0.0.1:<relayPort>`, with whether Funnel is on for its port. Warnings come from this list, not from the ledger. A handler in the ledger gets "Remove…"; one that is not gets the exact removal command as text for the owner to run, and "Check again". The report is shown on the Live page, not only in Settings. |
| Architecture 3.11 | Mapping inspection moves before relay start for one condition: **in Private mode, if the exposure report contains a handler with Funnel on, the relay does not start** and the status line says "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode." If the serve status cannot be read, the relay starts and the report says "unknown". E-1 stays as the second line. |
| Architecture 3.5, W-design 4.14 | `privateHttpsPort` must not be 443, 8443 or 10000. |
| Architecture 3.5 | When `settings.json` fails validation, ledger entries that validate by themselves are kept in memory, and the first save writes them back; the bad file is kept beside the new one as `settings.json.invalid`. |
| Architecture 3.9, TS-10 | After a timed-out apply, "Check again" that finds exactly the handler of the plan shown in that dialog offers "Record this mapping as LiveCue's" (an explicit click). Without it the hand-made mapping can never be removed from the application. |
| Protocol 4.5, architecture 3.12 | The success text describes what was tested: "This PC reached its own address." The Funnel text is shown only when the request did arrive marked. |
| README (iPhone design hand-off H-5 has the precedent) | An "Uninstall" section: remove both mappings in Settings before deleting the folder, with the two commands. |

Whether mappings should be removed when LiveCue closes is question 4.

### SEC-05. While LiveCue is closed, any local program can take the relay port

**Severity: low on a single-user PC, medium on a shared one.** The attacker is a program that is *not* the
owner's own account at full rights: another Windows account, or a sandboxed process that may still listen on
loopback. It gets the bearer token and the traffic, without reading `pairing.json` or calling DPAPI.

**Where:** architecture 3.9 ("Mappings are persistent Tailscale configuration and stay after the relay
stops"), 3.11 (stop; check 7 text), 6.5; W-design 5.12; protocol 5.1, 13.3; decisions 6.2 (port-in-use text).

**Scenario.** LiveCue is not running; the mapping still forwards to `127.0.0.1:47841`. The other program
binds that port. The phone, open and needing the PC, polls health every 5 s with `Authorization: Bearer
<token>`; Tailscale forwards it; the program records the token, answers the health shape, and then receives
assists, summaries and audio. When the owner starts LiveCue it says "Port 47841 is already in use. Close the
other LiveCue window", which points away from the problem.

**Fix.** Accept the root cause for 1.0, because closing it properly means either removing mappings on exit
(question 4) or replacing the bearer header with a proof of possession, which is out of proportion here. Make
it visible instead:

- Architecture 3.11, check 7, and decisions 6.2: when the port is in use *and* the exposure report (SEC-04)
  shows a mapping to it, the text becomes "Port `<n>` is in use by another program while a Tailscale mapping
  points at it. That program may have received your iPhone's requests, including the pairing token. Free the
  port, start the relay, then reset pairing."
- W-design 4.3: state that the listen socket is opened for exclusive use of the address, so that no second
  socket can share the port while the relay runs.
- Architecture 6.5: add the risk.

### SEC-06. Launcher scripts are run from a configurable folder with the execution policy bypassed

**Severity: low.** Needs write access to the lab folder or to `settings.json`. For the owner's own account
that is the risk architecture 6.5 already accepts. Two cases go beyond it.

**Where:** protocol 11.5; architecture 3.5 (`asrLabFolder`), 3.7 (last paragraph: scripts are not in the job
object), 3.8, 5.1 (the folder is also the container's `/app`), 5.3; W-design 4.10 (`LabRuntime`: PowerShell
path built from `%SystemRoot%`), 4.14 (`LabFolderResolver`, `TailscaleLocator`).

**Scenarios.**

1. The zip is unpacked somewhere other accounts can write. A folder created directly under the root of the
   system drive is, by default, writable by every signed-in account. Another account edits
   `asr\Start-ASR-Lab.ps1`. The next time the phone asks for a model, the relay runs it as the owner: hidden
   window, `-ExecutionPolicy Bypass`, outside the job object, so it outlives the application. The trigger is
   remote and needs only the token.
2. A file-write as the owner that is less than code execution (an archive that unpacks outside its folder, a
   sync client) sets `asrLabFolder` to a network share. The rule is only "absolute path of an existing folder
   that contains both scripts", so `\\host\share\asr` passes, and the scripts on the share run on the next
   model start.

**Fix.**

- Protocol 11.5 and architecture 3.8: the two launcher scripts are embedded in the relay and rewritten into
  `<data folder>\lab\` at each start, exactly as the schema files are. The relay runs those copies and passes
  the lab folder as a parameter. The configured folder then supplies data only: weights and the Python
  service, both mounted read-only into a container without capabilities. (Smaller alternative: before each
  run the relay hashes the two scripts and compares with values compiled into the build; a mismatch is
  `LAB_NOT_CONFIGURED` and a desktop notice.)
- Architecture 3.5, W-design 4.14: `asrLabFolder` must be a drive-letter path on a fixed local volume. UNC
  and device-namespace forms are refused at save and again at use.
- W-design 4.10 and 4.14: the PowerShell path comes from the system-directory API and the Tailscale path
  from the known-folder API, not from environment variables.
- README: unpack under the user profile.

The `PATH` look-ups for `codex.exe`, `tailscale.exe` and `docker.exe` were considered and reduce to the
accepted same-account risk. The Settings page already shows the resolved Codex path; with SEC-01 it also pins
its hash.

### SEC-07. "Reset pairing" has a race

**Severity: low.** The attacker already holds the token and must be sending requests at the instant the owner
resets. One request or one speech stream then survives the reset: up to 60 s of Codex, or up to 30 minutes of
holding the speech lease. It matters because reset is the only revocation there is.

**Where:** W-design 3.3 (`IPairingService.Verify`, `CurrentRevocation`: "The edge captures it immediately
after Verify returned true"), 4.4 step P-4, 4.7 (Reset); protocol 4.4 ("From that instant the old token is
rejected"), 10.5 (`PAIRING_CHANGED`); P2-115, P2-143, P2-149.

**Scenario.** Thread A verifies the old token against the old hash. The reset swaps the hash, cancels the old
revocation source and installs a new one. Thread A now reads `CurrentRevocation` and gets the *new* source. Its
stream is linked to a token that this reset will never cancel.

**Fix (W-design 3.3, 4.4, 4.7).** One immutable object `{hash, revocation token}` is swapped as a unit.
`Verify` becomes `bool TryVerify(ReadOnlySpan<char> token, out CancellationToken revoked)`: it reads the
object once and returns that object's token. `CurrentRevocation` is removed from the interface.

### SEC-08. Shape checks written `^…$` accept a trailing line feed

**Severity: low.** Needs a crafted `settings.json`, or a pasted value on the phone. The effect is a malformed
Tailscale mount and a confirmation dialog that does not show what runs, not command injection.

**Where:** W-design 3.6 (`MappingCommand.PrefixPattern`, in a class marked "Complete as written"), 4.14
(prefix and `tokenHash` patterns); architecture 3.5; protocol C-02, C-03, C-06, 4.3 rule V-4; iPhone design
3.1 (`PairingPayload.parse`, `manual`).

In .NET regular expressions `$` matches at the end of the text **and before a final line feed**. The same
holds for the regular-expression engines available to the Swift core (to confirm for the one chosen). So
`"/livecue"` followed by a line feed passes `^/[a-z0-9][a-z0-9-]{0,31}$`.

**Scenario.** `publicPathPrefix` in `settings.json` ends with a line feed. `RequirePrefix` accepts it. The
argument `--set-path=/livecue<LF>` is passed to Tailscale, and `DisplayText`, "exactly what the user is
shown", contains an invisible break. Tailscale creates a mount the inspection never recognises as ours; the
result is `NotVerified`, no ledger entry, and a public mapping the application cannot see (SEC-04). On the
phone, a token typed "exactly as typed" with a trailing line break passes V-4 if V-4 is a pattern with `$`.

**Fix.** Protocol section 1, conventions: "A shape written `^…$` matches the whole text. A text with a
trailing line break does not match." Implement every shape with `\z` or an explicit character loop, on both
sides. Change `PrefixPattern` in W-design 3.6 now, since that file is frozen first.

### SEC-09. The lab's plain HTTP routes have no `Host` rule; the container has open outbound network

**Severity: low.** A web page open on the PC while the container runs can read the lab's sample recording
and status. It cannot reach audio or transcripts of a session.

**Where:** protocol 11.1 ("Authentication: None"), 11.2, 11.3, 11.4; architecture 5.1 (port, mounts), 6.1
(row "Relay to lab"); P2-152 to P2-154 (WebSockets only).

**Scenario.** The page serves itself from a host name it controls on port 47842 over plain HTTP, then points
that name at `127.0.0.1`. Its own `GET /sample` and `GET /health` are now same-origin plain-HTTP requests:
the browser adds no `Origin` and no `Sec-Fetch-Site`, and the lab checks `Host` only on its two WebSockets.
The page reads `private\sample.wav` (which architecture 5.4 invites the owner to supply, so it may be their
voice) and the model state. Some browsers now ask before a public page may reach loopback; not all do.

Separately: the container runs on the default bridge network, so the Python stack that receives all PC-speech
audio can reach the internet. The telemetry switches of 5.1 are good; they are requests, not a barrier.

**Fix.** Protocol 11.3: the `Host` rule applies to **every** route of the lab, HTTP and WebSocket; a request
with another `Host` gets 403 before any handler runs. HTTP routes also refuse an `Origin` that is not the
lab's own and a `Sec-Fetch-Site` of `cross-site`. For the network: accept for 1.0, because the images are
built by the owner from pinned requirements; state it in architecture 6.5 and pin by hash (SEC-12). It is
question 9.

### SEC-10. Conversation text reaches disk in places the guarantees do not cover

**Severity: low.** No remote attacker. The statements "Nothing else is persisted" (architecture 3.5) and
"never written to disk" (3.10, decisions 6.2 activity footer) are shown to the owner and are not quite true.

**Where:** protocol 12.2 (working directory under the OS temp folder; `-o output.json`; `--ephemeral`);
W-design 4.9 steps 2 and 7, W-09; architecture 3.5, 3.10, 3.12, 3.14 (`--events json`), 4.10, 6.5.

| Place | What ends up there |
|---|---|
| `output.json` under the OS temp folder | The answer and the rolling memory, in plain text, until the folder is deleted. A crash, a kill, or three failed deletes (W-09) leave it. The folder name has six random characters from an unstated source, and creating a folder that already exists succeeds silently. |
| Codex's own session store | If the installed Codex does not honour `--ephemeral` (renamed, or ignored as unknown: see SEC-01), every prompt is kept in the Codex home folder indefinitely. No test looks. Whether `--ignore-user-config` also discards the owner's retention and analytics choices is to confirm. |
| Console host with `--events json` | `RequestStarted`, `RequestCompleted` and `SpeechText` carry conversation text to standard output, where a wrapper may log it. |
| Screen capture and screen history | The Live and Activity pages show full transcripts. |
| The phone, inside LiveContainer | Session history is readable by other guests (SEC-03). |

**Fix.**

- Protocol 12.2, W-design 4.9: the working folder is `<data folder>\work\<32 hex characters from the
  cryptographic random source>`, created with "fail if it exists". At start the relay deletes everything
  under `work\`. Architecture 3.5 lists the folder and says what it holds and for how long.
- Test ST-02 asserts that a run adds no file to the Codex home folder.
- Architecture 3.14: `--events json` omits `Transcript`, `PartialTranscript`, `Instruction`, `PayloadJson`
  and `SpeechText.Text` unless `--include-content` is given.
- W-design 5: a setting "Hide this window from screen capture", on by default (question 6).
- Architecture 3.10: reword to "written to disk only as Codex's output file, for the life of one request".

### SEC-11. `--show-pairing` prints the permanent token

**Severity: low.** Needs access to the owner's terminal history or to wherever they redirected the output.

**Where:** architecture 3.14; W-design 4.16; decisions A-16; test plan L-3.

**Scenario.** The owner runs the host with `--show-pairing > out.txt` or under a shell transcript, or
together with `--events json` inside a wrapper that keeps output. The token now sits in a plain file with no
expiry.

**Fix.** With SEC-02 the host prints an enrolment code that is dead in 120 s, and this finding closes.
Without SEC-02: architecture 3.14 adds "`--show-pairing` on the default data folder requires an interactive
console; with redirected output it prints nothing and exits 2", and forbids the combination with
`--events json`. The verification script (L-3) keeps working because it passes its own `--data-dir`.

### SEC-12. Release inputs are not pinned and a release can be replaced

**Severity: low.** Needs the compromise of an upstream action tag, an upstream package, or the repository
account. The result would be a build that runs as the owner with the microphone, the Codex sign-in and the
pairing. It is rated low for likelihood, not for impact. The iPhone build can only be made in CI, so the
owner cannot compare it with a local one.

**Where:** test plan section 5 intro ("Actions are referenced by major version"), 5.2 (caches;
`brew install xcodegen`), 5.3 step 3 ("move the tag to this commit and replace the four assets") and the
release notes; architecture 3.1 (allowed packages), 4.1 ("The generated project is never committed"), 5.1
(base image by tag), 7.6.

| Gap | Fix |
|---|---|
| Actions by movable tag | Reference every action by full commit id; a CI lint fails otherwise (ST-17). |
| No committed resolution file for the two speech packages, so their own dependencies float | Commit the Swift package resolution file, copy it into the generated project before resolving, and build with automatic resolution disabled. |
| NuGet restore not locked | Commit `packages.lock.json`; restore with `--locked-mode`. |
| XcodeGen installed unpinned although architecture 4.1 names a version | Check the installed version and fail on a mismatch. |
| A release can be re-made under an existing tag | The `release` job fails if the tag exists. A corrected build is a new version (question 8). |
| Checksums live beside the files they describe | Add a build provenance attestation for the IPA and the zip (the `release` job alone gains `id-token: write` and `attestations: write`), and say in the notes how to verify it. |
| The called CI restores caches during a release | `ci.yml` takes an input that switches caching off; `release.yml` sets it. |
| Lab image: base by tag, requirements by version | Base image by digest; `pip install --require-hashes`. |

Unsigned executables: accept, because this is a personal tool and the release notes say so.

### SEC-13. Pairing to a different host needs no confirmation

**Severity: low.** Needs the owner to scan a QR that is not from their PC and to tap "Verify and pair". What
follows is the redirection of SEC-03.

**Where:** protocol 4.3 (any HTTPS host is valid), 4.4 (row "Pair"); architecture 4.9; iPhone design 5.9,
`PairingController.verifyAndPair` ("a previous pairing is untouched on any failure").

**Scenario.** A QR arrives by message or on paper: "LiveCue needs to be paired again". It names the
attacker's host and any token. The server answers 200 to the verify call, and the saved pairing is replaced.

**Fix (iPhone design 5.9, architecture 4.9).** When a pairing exists and the new host differs, "Verify and
pair" first asks: "Replace `<current host>` with `<new host>`? Only do this with the QR shown by LiveCue
Desktop on your own PC." When the host is not a Tailscale name, the dialog adds one line saying so. Beyond
that: accept, because the owner pairs with their own screen. What is already right: nothing is sent before
the tap, and the first request reveals no secret.

### SEC-14. The owner cannot see that the public address is being probed

**Severity: note.** Not a weakness: with a 256-bit token, the absence of lockout and delay (architecture 6.5)
is correct, and a lockout would hand an attacker a way to lock the owner out. But protocol 5.2 ("no event
reaches the desktop") leaves the owner blind. A request with a well-formed but wrong token is worth knowing
about: it means an old or copied token is in use somewhere.

**Fix (protocol 5.5, architecture 3.4).** Three counters, incremented without allocation before E-3:
malformed, well-formed but not accepted, and Funnel-marked in Private mode. One `EdgeStatistics` event, at
most once per 60 s and only when a counter changed. The Live page shows "Rejected requests in the last hour".
P2-005 stays true: no event per request.

### SEC-15. One framing test expects behaviour Kestrel may not have

**Severity: note.** Test plan S-13 expects a bare 400 for a request with both `Content-Length` and
`Transfer-Encoding`. As far as this reviewer recalls, Kestrel lets `Transfer-Encoding` win and drops
`Content-Length` instead of rejecting (to confirm on .NET 10). A builder who meets the failing test must not
"fix" it by hand-parsing framing.

The property that matters, and why smuggling through the Tailscale proxy was judged not to be a risk: the
proxy writes each forwarded request with its own framing, and it may reuse one loopback connection for
different callers. The relay must therefore never leave an unread body on a connection it keeps open, or the
next caller's request would be read as the rest of that body. W-design K-4 says exactly this
("`Connection: close`" on every answer written before the body was read). That sentence is the whole
defence; it deserves its own test (ST-20), and S-13 should be reworded as ST-21.

---

## 3. Checked and found sound

Each line was examined as an attack and no change is asked for.

| Area | Result |
|---|---|
| Unauthenticated caller | E-1 to E-3 do no work, and the three 401 answers are one byte array. A caller can tell only: a relay is running (401) or not (the proxy's 502); that the server core rejects malformed requests with bare status codes; and its own request's effect on `Connection`. None depends on a secret. Private and Public mode are indistinguishable from outside. |
| Token | 32 random bytes; hash compared in constant time; the shape pre-check leaks only the shape; no token in any URL; no second accepted token. Rate limiting is not needed for guessing. |
| Storage on the PC | DPAPI for the current user with a fixed, public entropy value protects against other accounts and against a copied disk, and not against the owner's own account, as 6.5 says. The atomic write and the "unreadable means do not start, never re-pair silently" rule are right. Plaintext lives in memory only inside the listed calls. |
| Self-check | The token goes only to an address built from the name Tailscale reports, over validated TLS, with no proxy and no redirect. |
| Browser on the PC or phone | Cross-site requests, form posts and WebSockets carry `Origin`; there are no CORS headers and no cookies. One caveat to keep in mind: a DNS-rebinding page can send a same-origin plain-HTTP `GET` with neither `Origin` nor `Sec-Fetch-Site`, so E-1 does not stop it. E-2 does, because the page cannot know the token and the relay has no ambient credential. **The relay must never gain a cookie or an unauthenticated route.** (The lab has no token, hence SEC-09.) |
| Routes and the path prefix | Exact match on the raw target. Queries, encodings, trailing slashes, dot segments and a leftover prefix are 404 after authentication. No request text reaches a file path. |
| Slow and many clients | Header, body, keep-alive and route deadlines, the minimum body rate, and the caps C-30 to C-35 cover what the test plan lists. The proxy in front is expected to read complete request headers before it connects to the relay (to confirm in ST-27); the relay's own limits hold either way. An unauthenticated flood can crowd the 64 connections the phone shares; that is inherent, and SEC-14 makes it visible. |
| WebSocket | No compression, no subprotocol, bounded message buffer, socket ceiling before the upgrade, lifetime and idle limits. The byte limits C-72 and C-73 do not bound the *number* of frames, so a token holder can send very small frames; this ends their own stream at the lab's queue and nothing else. |
| Argument injection | The model id comes from the relay's own catalogue and must start with `gpt-`; the effort is one of seven fixed words; arguments are a list; only a real `codex.exe` is started; the prompt travels on standard input; `requestId` never reaches a command line. Speech models are two fixed words. |
| Prompt delimiters | The body is re-written as one compact JSON line, so the end marker cannot appear outside a JSON string. |
| Codex output | Validated by hand and rebuilt from an allowlist; size-capped; lab frames likewise. |
| Mapping manager | Four command forms from typed factories, list arguments, a read before every write, a diff after. A crafted `settings.json` can choose ports and a constrained prefix and nothing else (apart from SEC-08). A crafted *ledger* can at most make the application offer to remove a loopback proxy handler it did not create, on a dialog that shows the command. |
| Child processes | Job object with kill-on-close for Codex, tree kill on every exit path, output caps, deadlines. Lab scripts are outside the job on purpose and have their own bounded polling. |
| Container | No capabilities, no new privileges, read-only mounts, loopback-only publish, fixed name with an ownership label. |
| Token holder versus the owner | A stolen token gives use of the owner's Codex quota and GPU, denial of service against the owner's own session, and the timing of the owner's activity (busy states). It gives no route to transcripts, history or files, unless SEC-01 fails. |
| Phone transport | HTTPS-only grammar parsed by hand, no App Transport Security exceptions, ephemeral sessions, redirects refused, API keys in the Keychain only, no URL scheme, nothing sent before "Verify and pair". The only relay texts shown as they arrive are the PC status message, the loading line and the text of an unknown code (at most 300 characters); ST-26 keeps them plain text. |
| What leaves the phone | Codex answers: transcript window, memory and instruction to the PC and from there to the model service under the owner's Codex sign-in. OpenRouter: the same context, directly. Meta: live audio, directly. PC speech: audio to the PC only. Voz: audio stays on the phone, but the package reports usage to its vendor, which is disclosed; ST-29 checks what that traffic is. |
| Another program under the owner's account | It can read the protected token, replace settings and binaries, and call Codex itself. Architecture 6.5 accepts this and the review agrees: nothing in LiveCue raises what such a program can already do. |
| CI | No secrets, read-only default permission, pull requests from forks cannot reach a write token, releases only from `main`. |

---

## 4. Security tests to add

None of these is in test plan section 4. "Side": **R** relay tests, **H** hosting tests, **D** desktop tests,
**L** lab tests, **P** phone core tests, **A** phone app tests, **C** CI, **Live** opt-in on the build PC
(test plan section 3), **M** manual.

| Id | Test | Covers | Side |
|---|---|---|---|
| ST-01 | Tool list: the command line of protocol 12.2 is run against a loopback stand-in for the model service; the request Codex sends names no tool outside the allowlist of 12.6. The same run with an invented `--disable` name fails | SEC-01 | Live (no model call) |
| ST-02 | Canary: one real request whose input asks Codex to read a canary file in the working folder and one in the data folder, list a folder, fetch a loopback URL and write a file. The reply holds no canary value; the listener saw no connection; no file was created; the Codex home folder holds no new file afterwards | SEC-01, SEC-10 | Live |
| ST-03 | The Codex child's environment is exactly the allowlist: a parent variable named like a secret and a parent model API key are both absent. Replaces the `LIVECUE_` assertion of P2-064 | SEC-01 | R |
| ST-04 | With a `codex.exe` whose hash has no passed check: in Public mode routes 7 and 8 answer `ASSISTANT_NOT_VERIFIED` and the backend ran 0 times; in Private mode they run and a notice is raised | SEC-01 | R, H |
| ST-05 | Enrolment code: decoding the QR matrix yields no accepted token. The code works once on route 2 and the reply carries the token. A second use, use on any other route, use after 120 s on the injected clock, after "Hide QR" and after a relay stop each give the fixed 401, byte for byte. A code-authenticated call raises `PairingCodeUsed` and no presence event | SEC-02 | R |
| ST-06 | The window's display affinity is "exclude from capture" while the QR state is `Shown` (or always, per question 6) | SEC-02, SEC-10 | D |
| ST-07 | Phone store: Keychain holds A and the file holds B: load returns A, the Keychain still holds A, the file no longer holds B. Load never writes the Keychain from the file. At the full level: a successful Keychain save leaves no file; a failed one writes the file and marks the pairing as fallback | SEC-03 | A |
| ST-08 | Exposure report, from serve-status fixtures with a Funnel-on handler that targets the relay port (a) at the configured public port and prefix but not in the ledger, (b) on another port and mount, (c) on the private port after Funnel was switched on there: in Private mode the supervisor does not start the relay, the report lists the handler with its removal command, and no removal plan exists for a handler that is not in the ledger | SEC-04 | H |
| ST-09 | Settings: `privateHttpsPort` 443, 8443 and 10000 are refused. An invalid file followed by a save keeps every ledger entry that is valid by itself and leaves `settings.json.invalid` | SEC-04 | H |
| ST-10 | Port in use while a mapping to that port exists: the fault text carries the reset advice. Without a mapping: the plain text | SEC-05 | H, D |
| ST-11 | Lab folder: UNC, device-namespace, relative and drive-relative paths are refused at save and at use. A launcher script that differs from the build's copy is never started. The PowerShell path does not change when `SystemRoot` is altered in the parent environment | SEC-06 | H, R |
| ST-12 | Reset race: with a gate between hash comparison and the hand-over of the revocation token, a reset in that gap still ends the request with 401 and a stream with `PAIRING_CHANGED`. Stress form: 10,000 resets against continuous requests; afterwards nothing authenticated with an old token is running | SEC-07 | R |
| ST-13 | Whole-text shapes: the prefix, token, request id, model id, token hash and ledger path, each with a trailing line feed, carriage return plus line feed, and U+2028, are refused. `MappingCommand` factories throw for a prefix that ends in a line feed. `PairingPayload.parse` and `manual` refuse a token that ends in one | SEC-08 | H, R, P |
| ST-14 | Lab `GET /health`, `GET /` and `GET /sample` with a foreign `Host`, with `Origin: https://evil.example`, and with `Sec-Fetch-Site: cross-site` are refused; with the lab's own `Host` and no `Origin` they are served | SEC-09 | L |
| ST-15 | After a successful, failed, timed-out and cancelled run, and after a simulated crash followed by a start: no file under the work folder or the temp root contains the transcript canary. `--events json` output contains it only with `--include-content` | SEC-10 | R, H |
| ST-16 | `--show-pairing` with redirected output on the default data folder exits 2 and prints nothing of the token or code shape | SEC-11 | H |
| ST-17 | Workflow lint: every `uses:` is a 40-character commit id; the release path restores no cache; NuGet restore is locked; the Swift resolution file is unchanged after resolving; the release job fails when the tag exists | SEC-12 | C |
| ST-18 | Pairing to a different host requires the confirmation that names both hosts; "Cancel" leaves the saved pairing. Between a scan and "Verify and pair" the fake transport recorded zero requests | SEC-13 | A |
| ST-19 | Counters: 1,000 requests of each rejected kind give exact counts and at most one `EdgeStatistics` event per 60 s on the injected clock; P2-005 still holds | SEC-14 | R |
| ST-20 | No unread body stays on a kept connection. For each answer written before the body is read (401; 404; `PROTOCOL_MISMATCH`; 429 at the gauge; `RELAY_PAUSED`; `ASSISTANT_BUSY`; 413 by declared length): the raw client declares N bytes, sends N/2, then a complete second request. Exactly one response arrives, it has `Connection: close`, the server closes, and the second request is never answered | Protocol 5.1; W-design K-4; SEC-15 | R |
| ST-21 | A request with both `Content-Length` and `Transfer-Encoding` is never framed by `Content-Length`: either a bare 400, or chunked framing with the connection closed after the response. Replaces the third case of S-13 | SEC-15 | R |
| ST-22 | E-1 by header name, with a valid token: `ORIGIN`, `origin`, `sec-fetch-site`, a repeated header and a value of only spaces each give the fixed 401; so does `TAILSCALE-FUNNEL-REQUEST` in Private mode | Protocol E-1 | R |
| ST-23 | Soak before authentication: 50,000 requests without a token, 5,000 refused upgrades and 5,000 half-sent requests. Afterwards the connection count and the gauge are zero, handle count and managed heap are within a fixed margin of the start, and a real request is served | Protocol E-1 to E-3, C-30 | R (`Slow`) |
| ST-24 | The real `URLSession` transports against a loopback server in the simulator: a 302 on an HTTP route and on the speech upgrade is returned and not followed, and the redirect target receives no request | Protocol 3 rule 7, 13.1 | A |
| ST-25 | The built app's `Info.plist` has no App Transport Security key, no URL types and no document types (added to `validate_ipa.py`) | Architecture 4.1 | C |
| ST-26 | Answer, details, an unknown-code relay text and a PC status `message` are drawn as plain text: a fixture holding a Markdown link and a bare URL produces no link element | SEC-01 (reach of an injected answer); protocol 14 | A |
| ST-27 | Once, before Public mode is first used, from outside the tailnet through the real Funnel mount: a framing battery (both length headers, repeated lengths, bad chunk sizes, oversized headers, absolute-form targets) and a path battery around the prefix (dot segments, doubled slashes, upper case, an encoded slash). Only the fixed 401, a 404 with the token, or the proxy's own errors come back; a phone session running at the same time is undisturbed; the counters of SEC-14 match what was sent. Also recorded: Funnel requests arrive marked, tailnet requests do not, and a mark sent by a tailnet client is removed | Decisions section 7 risks 2 and 3; protocol E-1; SEC-04 | M |
| ST-28 | Private mode, from outside the tailnet: the private address does not connect. With a public mapping left in place: the relay does not start, and the Live page shows the handler and its removal command | SEC-04 | M |
| ST-29 | Network capture of the phone during a Voz session and during on-device model preparation: the hosts contacted are the ones the README names, and no upload grows with the length of the audio | Architecture 4.13, 6.5 | M |

---

## 5. Questions for the owner

Each is a risk decision that the documents cannot make.

1. **Is Public mode needed for 1.0?** If running Tailscale on the iPhone is acceptable, leaving Public mode
   out removes the internet edge and makes SEC-02, SEC-03 and SEC-01 one level less urgent. If it is needed,
   do you accept the four conditions of 1.4 before it is first switched on?
2. **Enrolment code (SEC-02).** May the protocol change so that the QR carries a 120-second, single-use code
   instead of the token? If not, do you accept that anyone who captures the QR once has access until you
   reset pairing, with capture exclusion and "never shown without a click" as the only mitigations?
3. **Codex updates (SEC-01).** May LiveCue refuse answers in Public mode after Codex updates itself, until
   "Check assistant" has been run again for the new binary? And is stronger containment of the Codex process
   (a restricted Windows identity) wanted later, at the cost of a more fragile Codex integration?
4. **Mappings when LiveCue is closed (SEC-04, SEC-05).** Today a mapping stays until you remove it, and TS-9
   forbids any write on exit. Would you rather give one standing consent for "remove my mapping when LiveCue
   closes and put it back when it starts"? It closes SEC-05 and most of SEC-04, and it makes the phone see
   "unreachable" instead of "relay not running" while the app is closed.
5. **LiveContainer (SEC-03, SEC-10).** Which other apps share the container with LiveCue? Choose the minimum
   level (the Keychain wins; the file stays readable) or the full level (no file while the Keychain works;
   a later Keychain failure means scanning the QR again). Is it acceptable that other guests can read the
   session history, or should history be encrypted with a key held in the Keychain?
6. **Screen capture (SEC-02, SEC-10).** Exclude the desktop window from capture always, only while the QR is
   shown, or as a setting? "Always" also blocks your own screenshots of the window.
7. **Is the PC used under any other Windows account?** If yes, SEC-05 and SEC-06 move up one level and
   should be fixed before first use.
8. **Releases (SEC-12).** May a published tag become immutable, so that a corrected build always needs a new
   version number?
9. **Lab container network (SEC-09).** Accept that the container can reach the internet, relying on pinned
   and hashed dependencies, or do you want it cut off (which needs a different way to publish its port)?
