# LiveCue ASR lab

The PC speech-recognition service of LiveCue. It runs one of two fixed speech models on the PC's GPU,
inside a Docker container, and gives the LiveCue relay live transcription over a loopback WebSocket. A
paired iPhone that picks "Nemotron" or "Qwen3" as its speech model is served by this lab; its audio goes to
this PC and nowhere else.

Design references: `docs/architecture/03-system-architecture.md` section 5,
`docs/architecture/02-relay-protocol.md` section 11, and `docs/spec/03-relay.md` sections 5.4 to 5.6.

> **Not yet run on a GPU.** Everything here is tested with a stand-in engine and stand-ins for Docker.
> The two real model adapters, the Docker images and the container start have not been run in this
> remake. See [What is and is not verified](#what-is-and-is-not-verified) before relying on it.

## What you need

| | |
|---|---|
| PC | Windows 10 or 11 with an NVIDIA GPU. The previous version ran both models on a 10 GB card, one at a time. |
| Docker | Docker Desktop with NVIDIA GPU support (WSL 2 back end). |
| Disk | About 17 GB for both images and about 7.3 GB for both sets of weights. |
| PowerShell | Windows PowerShell 5.1, which is part of Windows. |

Nothing else is installed on the PC. Python and the model libraries live in the images.

## Layout

| Path | What it is |
|---|---|
| `server.py` | The whole service: `/health`, `/`, `/sample`, the `/relay` and `/stream` sockets, admission rules, the one-session lock, 30-second windows, queues and timers. No model code. |
| `engines/` | `base.py` is the small interface. `nemotron.py` and `qwen3.py` are the two real engines. `fake.py` is the test engine. |
| `static/index.html` | The lab test page, served at `/`. |
| `benchmark.py` | Manual tool that replays a recording through `/stream`. |
| `Dockerfile`, `requirements/` | One image per model over a shared base, with pinned versions. Dependencies only. |
| `Start-ASR-Lab.ps1`, `Stop-ASR-Lab.ps1` | Load and unload a model. The relay runs its own copies of these ([below](#how-livecue-runs-them)). |
| `Build-ASR-Images.ps1`, `Get-ASR-Weights.ps1` | Manual, one-time setup. Each asks before it does anything. |
| `LabCommon.ps1` | Functions shared by the four scripts. |
| `weights.json` | Where each model's weights come from: repository, pinned revision, sizes and checksums. |
| `tests/` | Python tests (`pytest`) and `Test-LabScripts.ps1` for the scripts. |
| `private/` | Git-ignored. `models/<model>/` holds the weights; `sample.wav` is the optional sample clip. |

## Setup, done once by hand

Nothing in LiveCue runs these steps for you: not the application, not a test, not CI.

1. **Install Docker Desktop** with NVIDIA GPU support, and start it.

2. **Build the images.**

   ```powershell
   .\Build-ASR-Images.ps1            # or: -Model nemotron, -Model qwen3
   .\Build-ASR-Images.ps1 -PlanOnly  # only print what would happen
   ```

   The script lists what it will download and from where, and builds only after you type `yes`. It
   never starts Docker itself.

3. **Download the weights** of the model or models you want.

   ```powershell
   .\Get-ASR-Weights.ps1 -Model nemotron
   .\Get-ASR-Weights.ps1 -Model qwen3
   .\Get-ASR-Weights.ps1 -Model qwen3 -PlanOnly   # only print what would happen
   ```

   The script shows the repository, the revision, every file with its size, and the destination, and
   downloads only after you type `yes`. Files are checked against their published size and checksum,
   an interrupted download resumes, and files already in place are not fetched again.

4. **Optional: add a sample clip** for the page's "Try sample phrase" button. Any mono, 16 kHz, 16-bit
   PCM WAV at `private\sample.wav` will do. Use a synthetic or public recording, never a private one.
   Windows can speak one for you with its built-in voice, without downloading anything:

   ```powershell
   Add-Type -AssemblyName System.Speech
   New-Item -ItemType Directory -Force private | Out-Null
   $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, 'Sixteen', 'Mono')
   $voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
   $voice.SetOutputToWaveFile("$PWD\private\sample.wav", $format)
   $voice.Speak('The quick brown fox jumps over the lazy dog. Please confirm the meeting for Thursday at ten.')
   $voice.Dispose()
   ```

5. **Tell LiveCue Desktop where this folder is** (Settings, speech lab folder), unless it is the `asr\`
   folder beside the executable. It must be a drive-letter path on a fixed local drive; a network share
   is treated as "not configured". Keep it where other Windows accounts cannot write.

### Where the weights come from

| | `nemotron` | `qwen3` |
|---|---|---|
| Model | Nemotron 3.5 ASR streaming 0.6B | Qwen3-ASR 1.7B |
| Repository | [`nvidia/nemotron-3.5-asr-streaming-0.6b`](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b) | [`Qwen/Qwen3-ASR-1.7B`](https://huggingface.co/Qwen/Qwen3-ASR-1.7B) |
| Pinned revision | `ea30d66debe3740a08b573244286791d423d6b3e` | `7278e1e70fe206f11671096ffdd38061171dd6e5` |
| Licence | OpenMDW-1.1 | Apache-2.0 |
| Download | 6 files, 2.55 GB | 10 files, 4.70 GB |
| Largest file | `model.safetensors`, 2.55 GB | `model-00001-of-00002.safetensors`, 4.22 GB |

Only the files the adapters load are fetched. For Nemotron that is the transformers-format checkpoint;
the repository's `.nemo` and `.gguf` copies (another 3.1 GB) are left out. The previous version did not
record which repositories it used. These were identified from its loading code (transformers
`AutoModelForRNNT` with streaming look-ahead; `qwen_asr` with the vLLM back end) and from the model
names on its lab page. Both repositories are public and need no account. To move to a newer revision,
update `weights.json`.

### What the image build downloads

`python:3.12-slim` from Docker Hub, two system libraries from the Debian mirrors, PyTorch 2.9.1 for
CUDA 12.8 from `download.pytorch.org` (about 4 GB), and the pinned Python packages from PyPI. Allow about
10 GB of disk for the Nemotron image and about 15 GB for the Qwen3 image; they share the PyTorch layer,
so both together need about 17 GB. These sizes are estimates and were not measured for this version.

The `Dockerfile` names the base image by digest, so a build always gets the same image. The requirement
files pin the version of each package they list. They do not carry package hashes yet, so `pip` is not
run with `--require-hashes` and the packages those depend on are chosen at build time.

## Start and stop

LiveCue does this for you when a paired iPhone asks for a PC speech model. By hand:

```powershell
.\Start-ASR-Lab.ps1 -Model nemotron            # loads the model and opens the lab page
.\Start-ASR-Lab.ps1 -Model qwen3 -NoBrowser    # switches model; replaces the container
.\Stop-ASR-Lab.ps1                             # unloads the model and frees the GPU
```

| Parameter | Script | Meaning |
|---|---|---|
| `-Model nemotron\|qwen3` | Start | Default `nemotron`. One model is loaded at a time. |
| `-Port <n>` | Start, Stop | The lab's port on this PC, default `47842`. |
| `-NoBrowser` | Start | Do not open the lab page. |
| `-EnsureDocker` | Start | If Docker's engine is down, start Docker Desktop first. Only the genuine program is started: its Authenticode signature must be valid and issued to Docker Inc. |
| `-LabFolder <folder>` | Start | The lab folder: the one with `private\models\<model>` and the service code. Default: the folder the script is in. |

A first load can take a few minutes. Start waits up to 2 minutes for Docker and up to 8 minutes for the
model. Both scripts exit with code 0 on success and non-zero on failure.

What the scripts refuse to do:

- replace or stop a container named `livecue-remake-asr` that does not carry the label
  `com.livecueremake.component=asr-lab`;
- switch or unload a model while a transcription session is running;
- start Docker in order to stop a container (Stop does nothing when Docker is not running);
- download or build anything.

Stop leaves the container in place, stopped. Images and weights are never touched.

### How LiveCue runs them

LiveCue does not run the script files in this folder. `Start-ASR-Lab.ps1`, `Stop-ASR-Lab.ps1` and
`LabCommon.ps1` are built into the relay, which writes its own copies to its data folder, checks them
against the built-in ones before every run, and starts those:

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File <data folder>\lab\Start-ASR-Lab.ps1 -LabFolder <this folder> -Model <model> -Port <lab port> -NoBrowser -EnsureDocker
powershell.exe -NoProfile -ExecutionPolicy Bypass -File <data folder>\lab\Stop-ASR-Lab.ps1 -Port <lab port>
```

So the scripts cannot assume that they lie in the lab folder. Start is told the folder with `-LabFolder`
and takes only data from it: the weights and the service code, both mounted read-only into the
container. Stop needs nothing from it. Both load `LabCommon.ps1` from the folder they themselves are in
and never from `-LabFolder`, and no script in this folder is run. Editing a script here therefore changes
what you run by hand, not what LiveCue runs.

### The container

| | |
|---|---|
| Name and label | `livecue-remake-asr`, `com.livecueremake.component=asr-lab` |
| Images | `livecue-remake-asr-nemotron:1.0`, `livecue-remake-asr-qwen3:1.0` |
| Port | `127.0.0.1:<lab port>:8765`. Published on the PC's loopback address only. |
| Hardening | `--cap-drop ALL`, `--security-opt no-new-privileges`, `--pids-limit 512`, `--shm-size 1g`, `--gpus all` |
| Mounts, both read-only | `<lab folder>\private\models\<model>` at `/models`; `<lab folder>` at `/app` |
| Environment | `VLLM_NO_USAGE_STATS=1`, `DO_NOT_TRACK=1`, `GRADIO_ANALYTICS_ENABLED=False`, `ASR_HOST_PORT=<lab port>` |

The image holds the dependencies and nothing else. The code that runs is this folder, mounted read-only,
so a code change needs no rebuild and an image can never run stale code. These names and the port differ
from the previous LiveCue's lab on purpose, so the two can sit on one PC without touching each other.

## Protocol summary

No authentication. The service is reachable only on the PC's loopback address, and the rules below keep
a web page in a browser from reaching it. Audio is always raw PCM, signed 16-bit little-endian, mono,
16,000 Hz.

**`Host`, on every route.** Every request, HTTP or WebSocket, must carry `Host: 127.0.0.1:<lab port>` or
`Host: localhost:<lab port>`. Anything else is refused before any handler runs, whatever the path: `403`
with an empty body over HTTP, close code 1008 on a socket (on the wire, HTTP 403). This is what stops a
page that points its own host name at `127.0.0.1` (DNS rebinding): its requests are same-origin and
carry no `Origin`, but they name the page's host.

**`Origin` and `Sec-Fetch-Site`, on HTTP.** `GET /health`, `GET /` and `GET /sample` also answer `403` to
an `Origin` other than `http://127.0.0.1:<lab port>` or `http://localhost:<lab port>`, and to
`Sec-Fetch-Site: cross-site`. A request with the lab's own `Host` and no `Origin` is served: that is the
relay's probe, the launcher's probe and the lab page. The two sockets have their own `Origin` rules,
below.

### `GET /health`

```json
{"model":"nemotron","ready":true,"loadSeconds":41.7,"sampleRate":16000,"maxSeconds":120,"chunkMs":320,"relayProtocol":1,"busy":false}
```

The HTTP port opens only after the model is loaded and warmed up, so a reachable `/health` means a usable
model. `busy` is true while any session runs. `chunkMs` is 320 for `nemotron` and 1000 for `qwen3`.

`GET /` is the lab page. `GET /sample` is `private/sample.wav`, or `404` when there is none.

### WebSocket `/relay` (native relay protocol, version 1)

Admission: any `Origin` header, or a `Host` other than `127.0.0.1:<lab port>` or `localhost:<lab port>`,
is refused before the socket is accepted (close code 1008; on the wire, HTTP 403).

| Direction | Message | Notes |
|---|---|---|
| in | binary | Not empty, even length, at most 16,384 bytes per frame and 57,600,000 bytes per session (30 minutes). |
| in | `{"type":"endStream"}` | Any other text is an error. |
| out | `{"type":"ready","model":"<m>","sampleRate":16000}` | First. |
| out | `{"type":"speechStart","turnId":n,"audioProcessedMs":ms}` | Start of each 30-second window. The first follows `ready` at once. |
| out | `{"type":"transcript","turnId":n,"transcript":"...","audioProcessedMs":ms}` | Each time the window's text changes. |
| out | `{"type":"speechComplete","turnId":n,"transcript":"...","audioProcessedMs":ms}` | End of each window. |
| out | `{"type":"streamComplete","audioProcessedMs":ms,"finalizationMs":ms}` | After the last window that follows `endStream`. Then a normal close. |
| out | `{"type":"error","message":"Model is busy."}` | Another session is running. |
| out | `{"type":"error","message":"Local audio buffer or time limit exceeded."}` | Bad frame, bad command, a full queue in either direction, 20 s with nothing received, or 12 s without finishing after `endStream`. |
| out | `{"type":"error","message":"Local inference failed."}` | Recognition raised. |

Windows are consecutive, independent and exactly 480,000 samples long. A frame that straddles a boundary
is cut; no sample is dropped or repeated. `turnId` starts at 1. `audioProcessedMs` is samples divided by
16. End of stream with no audio in the current window still produces a `speechComplete`. The model's
context resets at each boundary, so a word cut by one may be recognised less well.

Buffers: 128 inbound frames, 256 outbound events. After an `error` the service closes.

### WebSocket `/stream` (lab page only)

Same `Host` rule. `Origin`, if present, must be `http://127.0.0.1:<lab port>` or
`http://localhost:<lab port>`. Binary frames of at most 64,000 bytes, 3,840,000 bytes in total (120 s);
the text `{"type":"stop"}` ends the test. The service sends `ready`, then
`{"type":"transcript","text","elapsedMs","audioMs"}` whenever the text changes, then one
`{"type":"result","text","elapsedMs","audioMs","model","firstTextMs","finalizationMs","inputSeconds"}`
(`qwen3` adds `processingCallP95Ms`) and closes. Errors: `One test is already running.`,
`Test timed out or exceeded its 120-second limit.`,
`Local inference failed. Inspect the local model service log.` There is no windowing.

## Privacy

- Audio and transcripts exist only in memory. Nothing is written to disk.
- The service makes no outbound network call. The images set `HF_HUB_OFFLINE=1` and
  `HF_HUB_DISABLE_TELEMETRY=1`; the launcher adds `VLLM_NO_USAGE_STATS=1`, `DO_NOT_TRACK=1` and
  `GRADIO_ANALYTICS_ENABLED=False`. These switches are requests to the libraries, not a barrier: the
  container is on Docker's default network and is not cut off from the internet. That is an accepted risk
  for this version (`docs/architecture/03-system-architecture.md` section 6.5).
- The log holds status lines only: numbers, model names, exception type names and code locations. It never
  holds audio, transcript text, anything a client sent, or the message of an exception, because library
  exceptions can quote what they were processing. Once the model is warmed up, Python's `logging` module
  is switched off for the whole process, so the web server and the model libraries cannot log either.
  One limit: text that a library prints straight to the console, bypassing `logging`, cannot be stopped
  from inside the service.
- The port is published on `127.0.0.1` only. Do not publish it anywhere else.
- `benchmark.py --output` writes transcript text to the file you name. That is its purpose; keep the file
  somewhere private.

## Configuration

Environment variables read by `server.py`:

| Variable | Default | Set by | Meaning |
|---|---|---|---|
| `ASR_MODEL` | `nemotron` | the image | `nemotron` or `qwen3`. Anything else stops the start-up. |
| `ASR_HOST_PORT` | `47842` | the launcher | The lab's port on the PC. `Host` and `Origin` are checked against it. |
| `ASR_BIND_HOST` | `127.0.0.1` | the image (`0.0.0.0`) | Listen address. Inside the container it must be all interfaces, because Docker forwards the loopback-only published port to the container's own address. Outside a container the default keeps the service off the network. |
| `ASR_LISTEN_PORT` | `8765` | nobody | Listen port. |
| `ASR_MODEL_DIR` | `/models` | nobody | Weights folder. |
| `ASR_ENGINE` | `model` | nobody | `fake` runs the test engine. No image sets it. |

## Tests

```powershell
python -m venv .venv
.\.venv\Scripts\python -m pip install -r requirements\test.txt
.\.venv\Scripts\python -m pytest
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Test-LabScripts.ps1
```

`tests/test_protocol.py` has one test per lab statement of the conformance checklist (P2-150 to P2-162).
The other files go into each area: admission, frames, commands, sample-exact windows, turn numbers,
timing members, timers, queue overflow in both directions, the health document, configuration, the
privacy of the log, the benchmark tool, and the real entry point behind uvicorn on an ephemeral port.
Timers and limits are injected, so no test waits for real seconds. The tests need no GPU, no Docker, no
model library and no network. CI runs them on every change (`.github/workflows/asr.yml`).

To develop a relay against the service without a GPU, run it with the test engine. It answers exactly as
the real service does, with the caption `test caption`:

```powershell
$env:ASR_ENGINE = 'fake'; $env:ASR_LISTEN_PORT = '47842'; $env:ASR_HOST_PORT = '47842'
.\.venv\Scripts\python server.py
```

Dial it as `127.0.0.1:47842` or `localhost:47842`. Under any other name, or with a `Host` that names
another port, every route answers `403`.

## What is and is not verified

Verified by the automated tests, without a GPU:

- every wire detail of `/health`, `/relay`, `/stream` and `/sample`, against the fake engine, in memory and
  behind a real uvicorn server, including a client that stops reading;
- the lab page in a real browser against the fake engine: status, sample-missing message, and file replay
  at speaking speed;
- the logic of the four scripts under Windows PowerShell 5.1, with Docker replaced by stand-ins; and for
  real: downloading with resume and checksum verification (against a loopback server), reading a health
  document, the signature check (against an installed Docker Desktop), and the first two steps of Start
  and Stop with Docker not running;
- the two launchers started the way the relay starts them: copies in another folder, run by a separate
  `powershell.exe -File`, with `-LabFolder` pointing at a lab folder whose own scripts must stay unread;
- the plumbing of the two model adapters against stand-ins for `torch`, `transformers` and `qwen_asr`:
  which calls are made, with which arguments, in which order, on exactly which audio.

**Not verified, because it needs the GPU, Docker and the weights:**

- that `engines/nemotron.py` and `engines/qwen3.py` work with the real libraries. Their library calls
  are copied call for call from the previous version, which did run on real hardware;
- that the images build from this `Dockerfile` and its pins;
- that Docker accepts the `docker run` arguments, and that the container starts and becomes ready;
- that `Get-ASR-Weights.ps1` completes a multi-gigabyte download from the real repositories (its file
  list, sizes and checksums come from the repositories' published metadata);
- recognition quality, speed and memory use;
- the lab page's microphone capture.

### First run on real hardware

1. `.\Build-ASR-Images.ps1`, then `.\Get-ASR-Weights.ps1 -Model nemotron`.
2. `.\Start-ASR-Lab.ps1 -Model nemotron`. If it fails, `docker logs --tail 40 livecue-remake-asr` shows
   the model libraries' own load-time messages followed by one `startup_failed` line that names the
   exception type and the code locations.
3. On the lab page, replay the sample clip and then speak into the microphone.
4. `docker exec livecue-remake-asr python benchmark.py /app/private/sample.wav` for timings.
5. Repeat with `qwen3`.
6. Run the end-to-end check through the relay with a clip longer than 32 seconds
   (`scripts/verify-pc-asr`, conformance statements P2-M01 to P2-M03), which also proves the 30-second
   window rollover with a real model.

## Benchmark

```powershell
python benchmark.py <audio> [--reference "the words spoken"] [--output runs.jsonl] [--fast] [--port 47842]
```

Replays a recording through `/stream`: mixed to mono, resampled to 16 kHz, in 80 ms frames at speaking
speed (`--fast` sends without pacing and measures throughput, not latency). It prints the `result`
message plus `realtimePlayback`, `partialUpdates`, `fixture` and, with `--reference`, `normalizedWER`.
The images contain what it needs, so the simplest way to run it is inside the container, as in step 4
above. On the PC itself it needs `requirements/benchmark.txt` plus `numpy` and `websockets`.

## What changed from the previous lab

| | Before | Now |
|---|---|---|
| Outbound queue overflow | The session was cancelled silently; the client found out by timing out. | The client gets an `error` message, then the close. If it has stopped reading altogether, the session still ends within 2 seconds and frees the model. |
| Logging | `/stream` logged the full text of an inference exception. | Type name and code location only, on every path, including start-up. |
| Start script readiness | The last poll ignored `relayProtocol`. | Ready means the requested model **and** `relayProtocol` 1. |
| Code in the image | Copied into the image, then hidden by a bind mount. | Not copied. The bind mount is the only source. |
| Unused packages | `python-multipart`, `curl`. | Removed. |
| Host port | Fixed at 8765. | A `-Port` parameter, default 47842; the `Host` check follows it. |
| Setup | Not scripted. | `Build-ASR-Images.ps1` and `Get-ASR-Weights.ps1`, with the weight sources recorded here. |
| Lab page | Hard-coded GPU label. | Removed. |
| HTTP routes | Only the two sockets checked `Host`; `/health`, `/` and `/sample` answered anyone. | `Host` is checked on every route, and the HTTP routes also refuse a foreign `Origin` and a cross-site fetch. |
| Where the launchers lie | The scripts took their own folder to be the lab folder. | Start takes `-LabFolder`; the relay runs its own copies from its data folder and never a script from the lab folder. |
| Base image | Named by tag. | Named by digest. |
