"""LiveCue ASR lab: loopback-only streaming speech recognition for the PC relay.

Run with ``python server.py``. Start-up order: load the weights, run one
warm-up recognition on synthetic silence, and only then open the HTTP port, so
a reachable ``/health`` always means a usable model.

This file is the whole service: configuration, logging, the audio queue and
the 30-second windowing, the session machinery, the two WebSocket protocols,
admission and the HTTP routes. It contains no model code (see ``engines/``)
and imports nothing heavier than FastAPI, so it runs and is tested on machines
with no GPU, no torch and no network.

Nothing happens at import time. vLLM starts its workers with the ``spawn``
method, which imports this module again in every child process.

Privacy rules that the code below keeps:

* audio and transcripts exist only in memory and are never written to disk;
* the log never contains audio, transcripts or exception text;
* the service makes no outbound network call.
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
import re
import sys
import threading
import time
import warnings
from collections import deque
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Deque, Dict, Mapping, Optional, Tuple

from fastapi import FastAPI, WebSocket
from fastapi.responses import FileResponse, JSONResponse

from engines import MODELS, Engine, EngineUnavailable, create_engine

# --------------------------------------------------------------------------
# Fixed protocol numbers
# --------------------------------------------------------------------------

#: Version of the native relay protocol spoken on ``/relay``.
RELAY_PROTOCOL = 1
#: PCM16 little-endian mono at this rate is the only accepted audio format.
SAMPLE_RATE = 16_000
BYTES_PER_SAMPLE = 2
#: Longest lab-page test, reported as ``maxSeconds`` by ``/health``.
MAX_TEST_SECONDS = 120
#: Port the service listens on inside its container.
CONTAINER_PORT = 8765
#: Loopback port of the PC that Docker maps to :data:`CONTAINER_PORT`.
DEFAULT_HOST_PORT = 47842
#: Largest WebSocket message the server reads before it drops the connection.
WS_MAX_MESSAGE_BYTES = 65_536
#: Close code for a refused handshake.
POLICY_VIOLATION = 1008

RELAY_BUSY = "Model is busy."
RELAY_LIMIT = "Local audio buffer or time limit exceeded."
RELAY_FAILURE = "Local inference failed."
STREAM_BUSY = "One test is already running."
STREAM_LIMIT = "Test timed out or exceeded its 120-second limit."
STREAM_FAILURE = "Local inference failed. Inspect the local model service log."

#: The folder that holds this file (``/app`` inside the container).
LAB_ROOT = Path(__file__).resolve().parent


# --------------------------------------------------------------------------
# Configuration
# --------------------------------------------------------------------------


@dataclass(frozen=True)
class Limits:
    """Buffers, caps and timers. The defaults are the protocol's real values.

    Tests shrink them instead of sleeping for real seconds or sending
    megabytes of audio.
    """

    # /relay: native relay protocol, version 1
    relay_max_frame_bytes: int = 16_384
    relay_max_total_bytes: int = SAMPLE_RATE * BYTES_PER_SAMPLE * 1800  # 30 minutes
    relay_window_samples: int = SAMPLE_RATE * 30  # one turn
    relay_inbound_frames: int = 128  # about 10 s of 80 ms frames
    relay_outbound_events: int = 256
    relay_finalize_seconds: float = 12.0

    # /stream: lab page protocol
    stream_max_frame_bytes: int = 64_000
    stream_max_total_bytes: int = SAMPLE_RATE * BYTES_PER_SAMPLE * MAX_TEST_SECONDS
    stream_inbound_frames: int = 1500
    stream_outbound_events: int = 2000
    stream_finalize_seconds: float = 60.0

    # both sockets
    idle_seconds: float = 20.0  # nothing received for this long ends the session
    closing_seconds: float = 2.0  # how long a final error frame may take to leave


@dataclass(frozen=True)
class Settings:
    """Where the service listens, what it answers to and which engine it runs."""

    model: str = "nemotron"
    #: ``model`` runs the real adapter for :attr:`model`; ``fake`` runs the test engine.
    engine: str = "model"
    bind_host: str = "127.0.0.1"
    listen_port: int = CONTAINER_PORT
    #: The port clients dial on the PC. ``Host`` and ``Origin`` are checked against it.
    host_port: int = DEFAULT_HOST_PORT
    model_dir: str = "/models"
    page_path: Path = LAB_ROOT / "static" / "index.html"
    sample_path: Path = LAB_ROOT / "private" / "sample.wav"
    limits: Limits = field(default_factory=Limits)

    @property
    def allowed_hosts(self) -> frozenset:
        return frozenset({f"127.0.0.1:{self.host_port}", f"localhost:{self.host_port}"})

    @property
    def allowed_origins(self) -> frozenset:
        return frozenset({f"http://127.0.0.1:{self.host_port}", f"http://localhost:{self.host_port}"})


class ConfigurationError(ValueError):
    """Bad configuration. ``reason`` is a fixed identifier that is safe to log."""

    def __init__(self, reason: str) -> None:
        super().__init__(reason)
        self.reason = reason


def _port(source: Mapping[str, str], name: str, default: int) -> int:
    try:
        value = int(source.get(name, str(default)))
    except ValueError:
        value = 0
    if not 0 < value < 65536:
        raise ConfigurationError("invalid_" + name.lower())
    return value


def settings_from_env(env: Optional[Mapping[str, str]] = None) -> Settings:
    """Build :class:`Settings` from environment variables.

    ``ASR_MODEL``        ``nemotron`` (default) or ``qwen3``. Set by the image.
    ``ASR_HOST_PORT``    the PC's loopback port for the lab, default 47842.
                         Set by the launcher; ``Host`` checks follow it.
    ``ASR_ENGINE``       ``model`` (default) or ``fake``. No image sets it.
    ``ASR_BIND_HOST``    listen address, default ``127.0.0.1``. The image sets
                         ``0.0.0.0`` because Docker publishes the port on the
                         PC's loopback interface only.
    ``ASR_LISTEN_PORT``  listen port, default 8765.
    ``ASR_MODEL_DIR``    weights folder, default ``/models``.
    """
    source = os.environ if env is None else env
    model = source.get("ASR_MODEL", "nemotron")
    if model not in MODELS:
        raise ConfigurationError("unknown_model")
    engine = source.get("ASR_ENGINE", "model")
    if engine not in ("model", "fake"):
        raise ConfigurationError("unknown_engine")
    return Settings(
        model=model,
        engine=engine,
        bind_host=source.get("ASR_BIND_HOST", "127.0.0.1"),
        listen_port=_port(source, "ASR_LISTEN_PORT", CONTAINER_PORT),
        host_port=_port(source, "ASR_HOST_PORT", DEFAULT_HOST_PORT),
        model_dir=source.get("ASR_MODEL_DIR", "/models"),
    )


# --------------------------------------------------------------------------
# Logging: status lines only
# --------------------------------------------------------------------------
#
# Audio and transcripts never reach the log, and neither does exception text:
# third-party exceptions can quote the audio or the words they were handling.
# A failure is recorded as its exception *type name* plus the code locations
# it passed through, which is enough to find the fault without leaking content.
#
# Every line is one JSON object whose values are numbers, booleans, null or
# short identifier-like tokens. Anything else is replaced by "[redacted]", so
# a careless future call cannot turn the log into a transcript.

_LOG_TOKEN = re.compile(r"[A-Za-z0-9_.:<>-]{1,96}")
_LOG_REDACTED = "[redacted]"
_LOG_MAX_ITEMS = 8
_log_lock = threading.Lock()


def _loggable(value: Any) -> Any:
    if value is None or isinstance(value, (bool, int)):
        return value
    if isinstance(value, float):
        return value if value == value and abs(value) != float("inf") else None
    if isinstance(value, str):
        return value if _LOG_TOKEN.fullmatch(value) else _LOG_REDACTED
    if isinstance(value, (list, tuple)):
        return [_loggable(item) if isinstance(item, str) else _LOG_REDACTED for item in list(value)[:_LOG_MAX_ITEMS]]
    return _LOG_REDACTED


def log_event(name: str, **fields: Any) -> None:
    """Write one status line, for example ``{"event":"warmup_complete","model":"qwen3"}``."""
    record = {"event": _loggable(name)}
    for key, value in fields.items():
        record[key] = _loggable(value)
    line = json.dumps(record, separators=(",", ":"))
    with _log_lock:
        try:
            sys.stdout.write(line + "\n")
            sys.stdout.flush()
        except Exception:  # noqa: BLE001 - a closed log must never end a session
            pass


def describe_exception(exc: BaseException) -> Dict[str, Any]:
    """What may be said about an exception: its type and where it travelled.

    The message, the arguments and the local variables are left out on
    purpose. File names are reduced to their base name.
    """
    places = []
    trace = exc.__traceback__
    while trace is not None:
        code = trace.tb_frame.f_code
        places.append(f"{os.path.basename(code.co_filename)}:{trace.tb_lineno}:{code.co_name}")
        trace = trace.tb_next
    return {"errorType": type(exc).__name__, "at": places[-_LOG_MAX_ITEMS:]}


def log_failure(name: str, exc: BaseException, **fields: Any) -> None:
    """Log a failure by type and location only."""
    log_event(name, **fields, **describe_exception(exc))


def loop_exception_handler(loop: Any, context: Dict[str, Any]) -> None:
    """asyncio's default handler prints the exception message; this one does not."""
    exc = context.get("exception")
    if isinstance(exc, BaseException):
        log_failure("loop_error", exc)
    else:
        log_event("loop_error")


def _thread_excepthook(args: Any) -> None:
    exc = getattr(args, "exc_value", None)
    if isinstance(exc, BaseException):
        log_failure("thread_error", exc)
    else:
        log_event("thread_error")


def _unraisable_hook(unraisable: Any) -> None:
    exc = getattr(unraisable, "exc_value", None)
    if isinstance(exc, BaseException):
        log_failure("unraisable_error", exc)
    else:
        log_event("unraisable_error")


def guard_runtime_logging() -> None:
    """Silence every other writer before the first audio can arrive.

    Called once the model is loaded and warmed up. From then on the standard
    ``logging`` module is switched off process-wide (uvicorn, asyncio and the
    model libraries all log through it and may include exception text),
    warnings are not printed, and uncaught thread or finaliser errors are
    reduced to their type. Load-time output of the model libraries is left
    alone, because no audio exists yet.

    Text that a third-party library writes straight to stdout or stderr cannot
    be intercepted from here.
    """
    logging.disable(logging.CRITICAL)
    warnings.simplefilter("ignore")
    threading.excepthook = _thread_excepthook
    sys.unraisablehook = _unraisable_hook


# --------------------------------------------------------------------------
# Audio hand-off: the bounded queue and the sample-exact windows
# --------------------------------------------------------------------------
#
# Audio stays in memory as the PCM16 bytes that arrived on the socket. The
# event loop calls put/end/cancel; one recogniser thread calls get/read.


class FrameQueue:
    """Bounded FIFO of audio frames with an end marker and a cancel switch."""

    def __init__(self, capacity: int) -> None:
        self._capacity = capacity
        self._frames: Deque[bytes] = deque()
        self._ended = False
        self._cancelled = False
        self._changed = threading.Condition()

    def put(self, frame: bytes) -> bool:
        """Queue one frame. Returns ``False`` when the queue is full."""
        with self._changed:
            if self._cancelled or self._ended:
                return True  # nobody will read it; that is not an overflow
            if len(self._frames) >= self._capacity:
                return False
            self._frames.append(frame)
            self._changed.notify()
            return True

    def end(self) -> None:
        """Mark the end of the input. Queued frames are still delivered first."""
        with self._changed:
            self._ended = True
            self._changed.notify_all()

    def cancel(self) -> None:
        """Drop everything and wake the reader."""
        with self._changed:
            self._cancelled = True
            self._frames.clear()
            self._changed.notify_all()

    def get(self) -> Optional[bytes]:
        """Block for the next frame; ``None`` at the end of input or on cancel."""
        with self._changed:
            while True:
                if self._cancelled:
                    return None
                if self._frames:
                    return self._frames.popleft()
                if self._ended:
                    return None
                self._changed.wait()

    @property
    def cancelled(self) -> bool:
        return self._cancelled


class QueueSource:
    """The whole session as one recognition (the lab page protocol)."""

    def __init__(self, frames: FrameQueue) -> None:
        self._frames = frames

    def read(self) -> Optional[bytes]:
        return self._frames.get()

    @property
    def cancelled(self) -> bool:
        return self._frames.cancelled


class AudioWindow:
    """One fixed-length turn cut out of a continuous stream, sample for sample.

    A frame that straddles the end of the window is split: the first part is
    delivered here and the rest is kept in :attr:`carry` for the next window.
    No sample is dropped and none is delivered twice.
    """

    def __init__(self, frames: FrameQueue, carry: bytes = b"", limit_samples: int = SAMPLE_RATE * 30) -> None:
        if limit_samples <= 0:
            raise ValueError("a window holds at least one sample")
        self._frames = frames
        self._limit = limit_samples
        #: Audio already taken from the queue that belongs to a later window.
        self.carry = carry
        #: Samples handed to the recogniser so far.
        self.samples = 0
        #: True once the end-of-input marker was read inside this window.
        self.ended = False

    def read(self) -> Optional[bytes]:
        if self.samples >= self._limit or self.ended or self._frames.cancelled:
            return None
        if self.carry:
            part, self.carry = self.carry, b""
        else:
            part = self._frames.get()
            if part is None:
                # A cancelled session is not the end of the audio.
                self.ended = not self._frames.cancelled
                return None
        room = (self._limit - self.samples) * BYTES_PER_SAMPLE
        if len(part) > room:
            part, self.carry = part[:room], part[room:]
        self.samples += len(part) // BYTES_PER_SAMPLE
        return part

    @property
    def cancelled(self) -> bool:
        return self._frames.cancelled


# --------------------------------------------------------------------------
# Session machinery shared by both sockets
# --------------------------------------------------------------------------
#
# Three actors, each with one job:
#
# * the receive loop (event loop) validates frames and commands and feeds the
#   bounded FrameQueue;
# * the recogniser thread reads that queue, runs the engine and offers events
#   to the bounded Outbox;
# * the sender task (event loop) is the only writer to the socket. It drains
#   the outbox in order, sends the final frame and closes.
#
# Every way a session can end is one final event in the outbox, so the client
# is always told why (an "error" frame) before the socket closes.

Event = Dict[str, Any]
Clock = Callable[[], float]

_MAX_COMMAND_CHARS = 256


def error_event(message: str) -> Event:
    return {"type": "error", "message": message}


def encode(event: Event) -> str:
    return json.dumps(event, separators=(",", ":"), ensure_ascii=False)


def is_command(text: Any, name: str) -> bool:
    """True only for a text frame that is exactly ``{"type": "<name>"}``."""
    if not isinstance(text, str) or len(text) > _MAX_COMMAND_CHARS:
        return False
    try:
        value = json.loads(text)
    except (ValueError, RecursionError):
        return False
    return isinstance(value, dict) and value == {"type": name}


def _text(value: Any) -> str:
    return value if isinstance(value, str) else ""


class SessionSlot:
    """The one-session-at-a-time rule: a single model holds the GPU."""

    def __init__(self) -> None:
        self._lock = threading.Lock()

    def try_acquire(self) -> bool:
        return self._lock.acquire(blocking=False)

    def release(self) -> None:
        self._lock.release()

    @property
    def busy(self) -> bool:
        return self._lock.locked()


class Outbox:
    """Bounded, ordered queue of outbound events.

    ``offer`` may be called from any thread. When the queue is full the
    session is over: everything queued is dropped, the ``overflow`` event
    becomes the final one and ``on_overflow`` is called, so the client gets an
    explicit ``error`` frame instead of silence.
    """

    def __init__(
        self,
        loop: asyncio.AbstractEventLoop,
        capacity: int,
        overflow: Event,
        on_overflow: Optional[Callable[[], None]] = None,
    ) -> None:
        self._loop = loop
        self._capacity = capacity
        self._overflow = overflow
        self._on_overflow = on_overflow
        self._lock = threading.Lock()
        self._queue: Deque[Tuple[Event, bool]] = deque()
        self._sealed = False  # no further events are accepted
        self._wake = asyncio.Event()
        self.overflowed = False

    def offer(self, event: Event, final: bool = False) -> bool:
        """Queue an event. ``final`` marks the last frame of a normal session."""
        overflowed = False
        with self._lock:
            if self._sealed:
                return False
            if len(self._queue) >= self._capacity:
                self.overflowed = overflowed = True
                self._replace_with(self._overflow)
            else:
                self._queue.append((event, final))
                self._sealed = final
        self._notify()
        if overflowed and self._on_overflow is not None:
            self._on_overflow()
        return not overflowed

    def fail(self, event: Event) -> bool:
        """End the session with ``event`` now, ahead of anything still queued."""
        with self._lock:
            if self._sealed:
                return False
            self._replace_with(event)
        self._notify()
        return True

    def close(self) -> None:
        """Stop accepting events and forget the queued ones."""
        with self._lock:
            self._sealed = True
            self._queue.clear()

    def _replace_with(self, event: Event) -> None:
        self._queue.clear()
        self._queue.append((event, True))
        self._sealed = True

    def _notify(self) -> None:
        try:
            self._loop.call_soon_threadsafe(self._wake.set)
        except RuntimeError:
            pass  # the loop is gone, and so is the session

    async def next(self) -> Tuple[Event, bool]:
        """Wait for the next event. Returns ``(event, is_final)``."""
        while True:
            with self._lock:
                if self._queue:
                    return self._queue.popleft()
                self._wake.clear()
            await self._wake.wait()


@dataclass(frozen=True)
class Rules:
    """What differs between the two sockets."""

    route: str
    end_command: str
    allow_empty_frames: bool
    max_frame_bytes: int
    max_total_bytes: int
    inbound_frames: int
    outbound_events: int
    finalize_seconds: float
    idle_seconds: float
    closing_seconds: float
    limit_message: str
    failure_message: str


Script = Callable[["Session"], Optional[Event]]
Decorator = Callable[[Event], Event]


class Session:
    """One admitted WebSocket session that holds the model."""

    def __init__(self, ws: Any, engine: Engine, slot: SessionSlot, rules: Rules, clock: Clock) -> None:
        self.ws = ws
        self.engine = engine
        self.rules = rules
        self.clock = clock
        self._slot = slot
        self._loop = asyncio.get_running_loop()
        self._aborted = asyncio.Event()
        self.frames = FrameQueue(rules.inbound_frames)
        self.outbox = Outbox(
            self._loop, rules.outbound_events, error_event(rules.limit_message), self._on_outbound_overflow
        )
        #: Bytes of audio received so far.
        self.audio_bytes = 0
        #: Clock reading when the session was admitted.
        self.started_at = clock()
        #: Clock reading when the end command arrived, or ``None``.
        self.ended_at: Optional[float] = None

    # -- recogniser thread ---------------------------------------------------

    def emit(self, event: Event) -> None:
        self.outbox.offer(event)

    def _on_outbound_overflow(self) -> None:
        # The client is not reading. Stop recognising and let the receive loop
        # give the error frame a bounded time to leave before it closes.
        self.frames.cancel()
        log_event("outbound_overflow", route=self.rules.route)
        try:
            self._loop.call_soon_threadsafe(self._aborted.set)
        except RuntimeError:
            pass

    def _work(self, script: Script) -> None:
        final: Optional[Event] = None
        failed = False
        try:
            final = script(self)
        except BaseException as exc:  # noqa: BLE001 - this thread must never die loudly
            failed = True
            log_failure("inference_failed", exc, route=self.rules.route)
        finally:
            # Free the model before the last frame can reach the client, so a
            # client that reconnects at once is never told the model is busy.
            self._slot.release()
        if failed:
            # In order, after what was already recognised: earlier turns stay valid.
            self.outbox.offer(error_event(self.rules.failure_message), final=True)
        elif final is not None and not self.frames.cancelled:
            self.outbox.offer(final, final=True)

    # -- sender task ---------------------------------------------------------

    async def _send(self, decorate: Optional[Decorator]) -> None:
        try:
            while True:
                event, final = await self.outbox.next()
                if decorate is not None and event.get("type") != "error":
                    event = decorate(event)
                await self.ws.send_text(encode(event))
                if final:
                    await self.ws.close()
                    return
        except asyncio.CancelledError:
            raise
        except Exception:  # noqa: BLE001 - the peer went away mid-send
            return

    # -- receive loop --------------------------------------------------------

    async def run(self, ready: Event, script: Script, decorate: Optional[Decorator] = None) -> None:
        """Run the session to its end. The caller already holds the slot."""
        worker_started = False
        sender: Optional["asyncio.Future[None]"] = None
        try:
            await self.ws.send_text(encode(ready))
            worker = threading.Thread(target=self._work, args=(script,), name="asr-" + self.rules.route, daemon=True)
            worker.start()
            worker_started = True
            sender = asyncio.ensure_future(self._send(decorate))
            await self._receive(sender)
        except asyncio.CancelledError:
            raise
        except Exception as exc:  # noqa: BLE001 - never hand exception text to the server's logger
            log_failure("session_error", exc, route=self.rules.route)
        finally:
            self.frames.cancel()
            self.outbox.close()
            if sender is not None and not sender.done():
                sender.cancel()
            if not worker_started:
                self._slot.release()

    async def _receive(self, sender: "asyncio.Future[None]") -> None:
        rules = self.rules
        loop = self._loop
        aborted = asyncio.ensure_future(self._aborted.wait())
        pending: Optional["asyncio.Future[Any]"] = None
        deadline: Optional[float] = None  # set once the end command has arrived
        try:
            while True:
                if pending is None:
                    pending = asyncio.ensure_future(self.ws.receive())
                timeout = rules.idle_seconds if deadline is None else deadline - loop.time()
                done: set = set()
                if timeout > 0:
                    done, _ = await asyncio.wait(
                        {pending, sender, aborted}, timeout=timeout, return_when=asyncio.FIRST_COMPLETED
                    )
                if sender in done:
                    return  # the final frame has been sent, or the peer is gone
                if aborted in done:
                    break  # outbound overflow: the error frame is already queued
                if pending not in done:
                    break  # nothing received for too long, or finalization ran out of time
                message, pending = _received(pending), None
                if message is None or message.get("type") != "websocket.receive":
                    return  # disconnected: nobody is left to tell
                if deadline is not None:
                    continue  # after the end command everything else is ignored
                data = message.get("bytes")
                if data is not None:
                    if not self._accept_audio(data):
                        break
                    continue
                if not is_command(message.get("text"), rules.end_command):
                    break
                self.ended_at = self.clock()
                self.frames.end()
                deadline = loop.time() + rules.finalize_seconds
            # A violation, a timeout or an overflow: tell the client, then close.
            self.frames.cancel()
            self.outbox.fail(error_event(rules.limit_message))
            await asyncio.wait({sender}, timeout=rules.closing_seconds)
        finally:
            for future in (pending, aborted):
                if future is not None and not future.done():
                    future.cancel()

    def _accept_audio(self, data: bytes) -> bool:
        rules = self.rules
        size = len(data)
        self.audio_bytes += size
        if size % 2 or size > rules.max_frame_bytes or self.audio_bytes > rules.max_total_bytes:
            return False
        if size == 0:
            return rules.allow_empty_frames
        return self.frames.put(bytes(data))


def _received(future: "asyncio.Future[Any]") -> Optional[dict]:
    """The received message, or ``None`` when receiving failed (peer gone)."""
    try:
        message = future.result()
    except Exception:  # noqa: BLE001
        return None
    return message if isinstance(message, dict) else None


# --------------------------------------------------------------------------
# /relay: native relay protocol, version 1
# --------------------------------------------------------------------------
#
# Client to service
#   binary  PCM16 LE mono 16 kHz; non-empty, even length, at most 16,384 bytes
#           per frame and 57,600,000 bytes per session (30 minutes)
#   text    exactly {"type":"endStream"}
#
# Service to client (text JSON, in this order)
#   ready           once, first
#   speechStart     at the start of every 30 s window (the first one straight
#                   after ready, before any audio)
#   transcript      whenever the text of the current window changes
#   speechComplete  at the end of every window
#   streamComplete  after the last window that follows endStream; then close
#   error           at most once, last; then close


def relay_rules(limits: Limits) -> Rules:
    return Rules(
        route="relay",
        end_command="endStream",
        allow_empty_frames=False,
        max_frame_bytes=limits.relay_max_frame_bytes,
        max_total_bytes=limits.relay_max_total_bytes,
        inbound_frames=limits.relay_inbound_frames,
        outbound_events=limits.relay_outbound_events,
        finalize_seconds=limits.relay_finalize_seconds,
        idle_seconds=limits.idle_seconds,
        closing_seconds=limits.closing_seconds,
        limit_message=RELAY_LIMIT,
        failure_message=RELAY_FAILURE,
    )


def ready_event(model: str) -> Event:
    return {"type": "ready", "model": model, "sampleRate": SAMPLE_RATE}


def _samples_to_ms(samples: int) -> float:
    """``audioProcessedMs`` = samples / 16. Exact: the divisor is a power of two."""
    return samples / (SAMPLE_RATE // 1000)


def relay_script(window_samples: int) -> Script:
    """Build the recogniser-thread routine for one ``/relay`` session."""

    def script(session: Session) -> Optional[Event]:
        offset = 0  # samples consumed by completed windows
        carry = b""  # audio already received that belongs to the next window
        turn = 0
        while True:
            window = AudioWindow(session.frames, carry, window_samples)
            turn += 1
            session.emit({"type": "speechStart", "turnId": turn, "audioProcessedMs": _samples_to_ms(offset)})
            shown = [""]

            def on_partial(
                text: Any, turn: int = turn, window: AudioWindow = window, offset: int = offset, shown: list = shown
            ) -> None:
                text = _text(text)
                if text == shown[0]:
                    return
                shown[0] = text
                session.emit(
                    {
                        "type": "transcript",
                        "turnId": turn,
                        "transcript": text,
                        "audioProcessedMs": _samples_to_ms(offset + window.samples),
                    }
                )

            recognition = session.engine.recognize(window, on_partial)
            if window.cancelled:
                return None
            if window.samples == 0 and not window.ended:
                # A recogniser that returns without reading would spin forever.
                raise RuntimeError("recogniser made no progress")
            offset += window.samples
            carry = window.carry
            session.emit(
                {
                    "type": "speechComplete",
                    "turnId": turn,
                    "transcript": _text(recognition.text),
                    "audioProcessedMs": _samples_to_ms(offset),
                }
            )
            if window.ended:
                ended_at = session.ended_at
                return {
                    "type": "streamComplete",
                    "audioProcessedMs": _samples_to_ms(offset),
                    "finalizationMs": (session.clock() - ended_at) * 1000 if ended_at is not None else 0,
                }

    return script


# --------------------------------------------------------------------------
# /stream: lab page protocol (never used by the relay)
# --------------------------------------------------------------------------
#
# Client to service
#   binary  PCM16 LE mono 16 kHz; even length, at most 64,000 bytes per frame
#           and 3,840,000 bytes in total (120 seconds)
#   text    exactly {"type":"stop"}
#
# Service to client (text JSON)
#   ready       once, first
#   transcript  whenever the cumulative text changes
#   result      once, after stop; then close
#   error       at most once, last; then close
#
# There is no windowing: the whole session is one recognition.

#: Engine measurements that may be added to the ``result`` frame.
_RESULT_METRICS = ("processingCallP95Ms",)


def stream_rules(limits: Limits) -> Rules:
    return Rules(
        route="stream",
        end_command="stop",
        allow_empty_frames=True,
        max_frame_bytes=limits.stream_max_frame_bytes,
        max_total_bytes=limits.stream_max_total_bytes,
        inbound_frames=limits.stream_inbound_frames,
        outbound_events=limits.stream_outbound_events,
        finalize_seconds=limits.stream_finalize_seconds,
        idle_seconds=limits.idle_seconds,
        closing_seconds=limits.closing_seconds,
        limit_message=STREAM_LIMIT,
        failure_message=STREAM_FAILURE,
    )


def stream_script(session: Session) -> Optional[Event]:
    """The recogniser-thread routine: one recognition over the whole session."""
    source = QueueSource(session.frames)
    shown = [""]

    def on_partial(text: Any) -> None:
        text = _text(text)
        if text != shown[0]:
            shown[0] = text
            session.emit({"type": "transcript", "text": text})

    recognition = session.engine.recognize(source, on_partial)
    if source.cancelled:
        return None
    result: Event = {"type": "result", "text": _text(recognition.text)}
    for name in _RESULT_METRICS:
        if name in recognition.metrics:
            value = recognition.metrics[name]
            is_number = isinstance(value, (int, float)) and not isinstance(value, bool)
            result[name] = float(value) if is_number else None
    return result


def stream_decorator(session: Session) -> Decorator:
    """Add the timing members at the moment a frame is sent."""
    first_text_ms: list = [None]
    bytes_per_ms = SAMPLE_RATE * BYTES_PER_SAMPLE // 1000  # 32
    bytes_per_second = SAMPLE_RATE * BYTES_PER_SAMPLE  # 32,000

    def decorate(event: Event) -> Event:
        now = session.clock()
        elapsed = (now - session.started_at) * 1000
        if event.get("text") and first_text_ms[0] is None:
            first_text_ms[0] = elapsed
        out: Event = {
            "type": event["type"],
            "text": event.get("text", ""),
            "elapsedMs": elapsed,
            "audioMs": session.audio_bytes / bytes_per_ms,
        }
        if event["type"] == "result":
            ended_at = session.ended_at
            out["model"] = session.engine.name
            out["firstTextMs"] = first_text_ms[0]
            out["finalizationMs"] = (now - ended_at) * 1000 if ended_at is not None else None
            out["inputSeconds"] = session.audio_bytes / bytes_per_second
            for name in _RESULT_METRICS:
                if name in event:
                    out[name] = event[name]
        return out

    return decorate


# --------------------------------------------------------------------------
# Admission
# --------------------------------------------------------------------------
#
# There is no authentication: the service is reachable on the PC's loopback
# interface only. These checks stop a web page in the user's browser from
# reaching it anyway, directly (Origin) or through DNS rebinding (Host).


def _host_allowed(headers: Any, settings: Settings) -> bool:
    hosts = headers.getlist("host")
    return len(hosts) == 1 and hosts[0] in settings.allowed_hosts


def relay_admitted(headers: Any, settings: Settings) -> bool:
    """``/relay`` is for the native relay only: no ``Origin`` header at all."""
    return _host_allowed(headers, settings) and not headers.getlist("origin")


def stream_admitted(headers: Any, settings: Settings) -> bool:
    """``/stream`` also serves the lab page, so the lab's own origin is allowed."""
    if not _host_allowed(headers, settings):
        return False
    origins = headers.getlist("origin")
    return not origins or (len(origins) == 1 and origins[0] in settings.allowed_origins)


# --------------------------------------------------------------------------
# The application
# --------------------------------------------------------------------------

_NO_STORE = {"Cache-Control": "no-store", "X-Content-Type-Options": "nosniff"}


def create_app(
    engine: Engine,
    settings: Optional[Settings] = None,
    *,
    clock: Clock = time.perf_counter,
) -> FastAPI:
    """Build the service around an already loaded engine.

    Tests pass the fake engine, shrunken :class:`Limits` and a fake clock.
    """
    settings = settings or Settings(model=engine.name)
    limits = settings.limits
    slot = SessionSlot()

    @asynccontextmanager
    async def lifespan(app: FastAPI):  # type: ignore[no-untyped-def]
        asyncio.get_running_loop().set_exception_handler(loop_exception_handler)
        yield

    app = FastAPI(title="LiveCue ASR lab", docs_url=None, redoc_url=None, openapi_url=None, lifespan=lifespan)
    app.state.engine = engine
    app.state.settings = settings
    app.state.slot = slot

    def not_found() -> JSONResponse:
        return JSONResponse({"detail": "Not Found"}, status_code=404, headers=_NO_STORE)

    @app.get("/")
    def page() -> Any:
        if not settings.page_path.is_file():
            return not_found()
        return FileResponse(settings.page_path, media_type="text/html", headers=_NO_STORE)

    @app.get("/health")
    def health() -> Any:
        return JSONResponse(
            {
                "model": engine.name,
                "ready": bool(engine.loaded),
                "loadSeconds": engine.load_seconds,
                "sampleRate": SAMPLE_RATE,
                "maxSeconds": MAX_TEST_SECONDS,
                "chunkMs": engine.chunk_ms,
                "relayProtocol": RELAY_PROTOCOL,
                "busy": slot.busy,
            },
            headers=_NO_STORE,
        )

    @app.get("/sample")
    def sample() -> Any:
        # One optional clip at one fixed path. There is no parameter, so no
        # request can name a file.
        if not settings.sample_path.is_file():
            return not_found()
        return FileResponse(settings.sample_path, media_type="audio/wav", headers=_NO_STORE)

    async def serve(ws: WebSocket, admitted: bool, busy_message: str, start: Callable[[], Any]) -> None:
        """Admission, the one-session rule, then the session itself.

        Nothing is allowed to escape from here: an exception that reached the
        server would be logged with its message.
        """
        holding = False
        try:
            if not admitted:
                # Closing before accepting refuses the handshake.
                await ws.close(code=POLICY_VIOLATION)
                return
            await ws.accept()
            if not slot.try_acquire():
                await ws.send_text(encode(error_event(busy_message)))
                await ws.close()
                return
            holding = True
            run = start()
            holding = False  # from here on the session releases the slot
            await run()
        except asyncio.CancelledError:
            raise
        except Exception as exc:  # noqa: BLE001
            log_failure("socket_error", exc)
        finally:
            if holding:
                slot.release()

    @app.websocket("/relay")
    async def relay_socket(ws: WebSocket) -> None:
        def start() -> Any:
            session = Session(ws, engine, slot, relay_rules(limits), clock)
            script = relay_script(limits.relay_window_samples)
            return lambda: session.run(ready_event(engine.name), script)

        await serve(ws, relay_admitted(ws.headers, settings), RELAY_BUSY, start)

    @app.websocket("/stream")
    async def stream_socket(ws: WebSocket) -> None:
        def start() -> Any:
            session = Session(ws, engine, slot, stream_rules(limits), clock)
            return lambda: session.run(ready_event(engine.name), stream_script, stream_decorator(session))

        await serve(ws, stream_admitted(ws.headers, settings), STREAM_BUSY, start)

    return app


# --------------------------------------------------------------------------
# Entry point
# --------------------------------------------------------------------------


def build_server(engine: Engine, settings: Settings) -> Any:
    """Create the uvicorn server for an already loaded engine."""
    import uvicorn

    config = uvicorn.Config(
        create_app(engine, settings),
        host=settings.bind_host,
        port=settings.listen_port,
        access_log=False,
        ws_max_size=WS_MAX_MESSAGE_BYTES,
        log_config=None,
        server_header=False,
        timeout_graceful_shutdown=5,
    )
    return uvicorn.Server(config)


def main(env: Optional[Mapping[str, str]] = None) -> int:
    stage = "configure"
    try:
        settings = settings_from_env(env)
        engine = create_engine(settings.model, settings.engine, settings.model_dir)
        stage = "load"
        engine.load()
        log_event("loaded", model=engine.name, loadSeconds=engine.load_seconds)
        stage = "warmup"
        engine.warm_up()
        log_event("warmup_complete", model=engine.name)
    except (ConfigurationError, EngineUnavailable) as exc:
        log_event("startup_failed", stage=stage, reason=exc.reason)
        return 1
    except Exception as exc:  # noqa: BLE001 - type and location only, never the message
        log_failure("startup_failed", exc, stage=stage)
        return 1

    server = build_server(engine, settings)
    # From here on real audio can arrive: nothing but our own status lines.
    guard_runtime_logging()
    log_event("listening", model=engine.name, port=settings.listen_port, hostPort=settings.host_port)
    try:
        server.run()
    except SystemExit as exc:  # uvicorn exits this way when it cannot bind
        log_event("server_failed", code=exc.code if isinstance(exc.code, int) else 1)
        return 1
    except Exception as exc:  # noqa: BLE001
        log_failure("server_failed", exc)
        return 1
    log_event("server_stopped")
    return 0


if __name__ == "__main__":
    sys.exit(main())
