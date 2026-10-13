# LiveCue Remake

A from-scratch remake of LiveCue: an iPhone conversation assistant with a private Windows relay, in a light liquid glass design on both.

The iPhone app listens to a conversation and shows a live transcript. Tap **Assist** and it shows an answer to the latest question, either from Codex running on your own PC or from a model you choose on OpenRouter.

> **Status: 1.0.0, not yet tried on a real iPhone.** Everything here is tested in simulators and on a Windows PC with automated tests. Real microphone capture, on-device speech models, the phone talking to a real PC, and the PC speech models on a GPU have not been exercised. Expect bugs on first use.

## What is in this repository

| Folder | What it is |
| --- | --- |
| `ios/` | The iPhone app (SwiftUI, iOS 18 or later; native Liquid Glass on iOS 26) |
| `windows/` | The Windows app: the relay and its desktop window in one self-contained .NET 10 application |
| `asr/` | Optional speech-recognition service for the PC's GPU (Docker), used by the "Nemotron" and "Qwen3" speech options |
| `docs/` | The functional spec, design system, screen designs and mockups, protocol, architecture and security review |

## How the pieces fit

- **Speech to text** is chosen on the phone: Meta Muse (cloud, straight from the phone), Nemotron or Qwen3 (on your PC's GPU, through the relay), or Voz or Parakeet (on the phone itself).
- **Answers** are chosen on the phone: Codex on your PC (through the relay; no API key) or OpenRouter (straight from the phone, with your key).
- **The relay** runs on your PC, listens only on the PC itself, and is reached from the phone through Tailscale. The phone pairs with it by scanning a QR code.

The PC is only needed when you use a PC speech model or Codex answers.

## Install on Windows

1. Download `LiveCueRemake-win-x64.zip` from [Releases](../../releases) and unzip it anywhere. Nothing else needs installing; .NET is included.
2. For Codex answers: install the Codex CLI and sign in to it once.
3. Install Tailscale and sign in.
4. Run `LiveCueRemake.exe`.
5. Open **Settings**, check the Connection section, and choose **Set up connection**. LiveCue shows the exact Tailscale command first and runs it only when you confirm. It never changes Tailscale at any other time.
6. On **Live**, choose **Start relay**. If something is missing, the page lists what and how to fix it.
7. On **Pairing**, choose **Show QR**.

Closing the window stops the relay. Settings and the pairing are kept in `%LOCALAPPDATA%\LiveCueRemake\`.

The folder also contains `LiveCueRelayHost.exe`, a console version of the relay. `LiveCueRelayHost.exe --show-pairing` prints the pairing text for the phone's paste option when the camera cannot be used.

## Install on iPhone

The app is built unsigned, for installing through LiveContainer.

1. Download `LiveCueRemake.ipa` from [Releases](../../releases) and install it in LiveContainer. It installs beside the original LiveCue and shares nothing with it.
2. If bars and sheets do not look like iOS 26 on an iOS 26 phone, turn on **Spoof SDK Version** for the app in LiveContainer.
3. Open the app. On Home, the readiness rows show what is missing:
   - **Pair Windows PC**: scan the QR from the Windows app, then **Verify and pair**.
   - **Transcription**: pick a speech option. Meta Muse needs your Meta API key; Voz and Parakeet need a one-time model download.
   - **Assistant models & timing**: pick Codex on your PC, or OpenRouter with your key and a model.
4. Tap **Start conversation**.

Microphone and camera permission are asked for by LiveContainer, not by the app, so the prompt shows LiveContainer's wording.

## PC speech models (optional)

`asr/README.md` explains the setup: Docker Desktop with NVIDIA GPU support, building two images, and downloading about 7 GB of model weights. Nothing downloads or builds until you run those scripts yourself. This part has only been tested with a stand-in engine.

## Privacy

- Audio is never written to disk by the phone, the relay or the speech service.
- With Meta Muse, audio goes from the phone to Meta. With OpenRouter, transcript text goes from the phone to OpenRouter. With Codex, transcript text goes to your PC and from there to OpenAI through the Codex CLI. With a PC speech model, audio goes to your PC only. With Voz or Parakeet, audio stays on the phone.
- API keys stay in the iPhone Keychain and are sent only to their own provider. The PC holds no API key.
- Conversation history is stored on the phone. The Windows app keeps recent activity in memory only.
- On-device speech uses Voz by Desert Ant Labs (desert-ant-core 3.1.0, Desert Ant Labs Source-Available License 1.0) and FluidAudio 0.15.6 (Apache License 2.0). The Voz package reports anonymous usage to its vendor, Desert Ant Labs. LiveCue cannot turn this off.

## Security notes

Read [docs/architecture/07-security-review.md](docs/architecture/07-security-review.md) before exposing the relay beyond your own devices.

- **One shared pairing token.** Anyone who scans or photographs the QR can use the relay until you choose **Reset pairing**. The QR is hidden until you ask for it and hides itself after two minutes.
- **Private mode (tailnet only) is the supported way to run.** The relay refuses requests that arrive through Tailscale Funnel while it is in Private mode.
- **Public mode (Tailscale Funnel) is implemented but not recommended yet.** It has not been tested behind a real Funnel mount, and one finding is open: a caller holding the token can tie up the relay's request slots by never reading replies (`windows/tests/LiveCue.Relay.Tests/Conformance/Coverage.md`, WP-W13-1).
- **Codex is not fully contained by switches.** The relay runs Codex with a read-only sandbox, an allowlisted environment, an empty working folder and every tool-disabling switch the CLI accepts, but Codex CLI 0.160.1 keeps one command tool and its plugin system on regardless. A live check (a prompt-injection transcript asking for two planted files) returned neither file's contents, which is evidence, not proof. What other people say in a conversation reaches Codex as text, so treat Codex answers as you would any model reading untrusted input.

## Building from source

Everything is built and tested by GitHub Actions on every push:

- `ios.yml`: core library tests on Linux, unit and UI tests on the iOS 26 and iOS 18 simulators, and the unsigned IPA.
- `windows.yml`: build, about 2,400 tests, publish, and the desktop self-test with screenshots of every page.
- `asr.yml`: speech service tests on Linux and Windows PowerShell.

On Windows with the .NET 10 SDK: `./scripts/publish-windows.ps1 -Output artifacts/publish -Zip`. The iPhone project is generated with XcodeGen from `ios/project.yml` and needs Xcode 26.3.

## Licence

MIT. See [LICENSE](LICENSE). Third-party packages keep their own licences.
