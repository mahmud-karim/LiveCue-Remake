"""Shared helpers for the lab tests: a service under test, test engines, PCM."""

from __future__ import annotations

import socket
import struct
import threading
import time
import zlib
from typing import Any, Callable, Dict, Iterable, List, Optional

from fastapi.testclient import TestClient
from starlette.websockets import WebSocketDisconnect

import server
from engines import Engine, Recognition
from engines.fake import FakeEngine

HOST_PORT = server.DEFAULT_HOST_PORT  # 47842
HOST = f"127.0.0.1:{HOST_PORT}"
ORIGIN = f"http://127.0.0.1:{HOST_PORT}"

RELAY_LIMIT_ERROR = {"type": "error", "message": "Local audio buffer or time limit exceeded."}
RELAY_FAILURE_ERROR = {"type": "error", "message": "Local inference failed."}
RELAY_BUSY_ERROR = {"type": "error", "message": "Model is busy."}
STREAM_LIMIT_ERROR = {"type": "error", "message": "Test timed out or exceeded its 120-second limit."}
STREAM_FAILURE_ERROR = {
    "type": "error",
    "message": "Local inference failed. Inspect the local model service log.",
}
STREAM_BUSY_ERROR = {"type": "error", "message": "One test is already running."}

#: Timers small enough that no test waits for real seconds.
FAST = dict(idle_seconds=5.0, closing_seconds=1.0)


def free_port() -> int:
    """A loopback port that is free right now."""
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


# -- PCM ---------------------------------------------------------------------


def pcm(samples: Iterable[int]) -> bytes:
    """Encode integers as PCM16 little-endian."""
    values = list(samples)
    return struct.pack(f"<{len(values)}h", *values)


def unpack(data: bytes) -> List[int]:
    return list(struct.unpack(f"<{len(data) // 2}h", data))


def ramp(count: int, start: int = 0) -> bytes:
    """``count`` samples whose values never repeat within 60,000 samples."""
    return pcm(((start + i) % 60000) - 30000 for i in range(count))


def digest(data: bytes) -> str:
    """What :class:`DigestEngine` returns for exactly these bytes."""
    return f"{len(data) // 2}:{zlib.crc32(data):08x}"


# -- clock -------------------------------------------------------------------


class FakeClock:
    """A clock that moves only when a test moves it."""

    def __init__(self, now: float = 1000.0) -> None:
        self.now = now

    def __call__(self) -> float:
        return self.now

    def advance(self, seconds: float) -> None:
        self.now += seconds


# -- engines -----------------------------------------------------------------


class TestEngine(Engine):
    """Base for the engines defined by tests: already loaded, model ``nemotron``."""

    __test__ = False  # not a pytest class
    name = "nemotron"
    chunk_ms = 320
    loaded = True
    load_seconds = 1.5


class DigestEngine(TestEngine):
    """Proves which samples reached each recognition.

    Partial text: the number of samples read so far. Final text:
    ``"<samples>:<crc32 of the bytes read>"``. Every recognition's bytes are
    also kept in :attr:`heard`.
    """

    def __init__(self) -> None:
        self.heard: List[bytes] = []

    def recognize(self, audio: Any, on_partial: Callable[[str], None]) -> Recognition:
        got = bytearray()
        while True:
            part = audio.read()
            if part is None:
                break
            got += part
            on_partial(str(len(got) // 2))
        self.heard.append(bytes(got))
        return Recognition(digest(bytes(got)))


class CallbackEngine(TestEngine):
    """Runs a function as the recogniser."""

    def __init__(self, function: Callable[[Any, Callable[[str], None]], Any], name: str = "nemotron") -> None:
        self._function = function
        self.name = name
        self.chunk_ms = 320 if name == "nemotron" else 1000

    def recognize(self, audio: Any, on_partial: Callable[[str], None]) -> Recognition:
        result = self._function(audio, on_partial)
        return result if isinstance(result, Recognition) else Recognition(result)


def drain(audio: Any) -> bytes:
    """Read a source to its end."""
    got = bytearray()
    while True:
        part = audio.read()
        if part is None:
            return bytes(got)
        got += part


# -- the service under test ----------------------------------------------------


class Lab:
    """One service instance and a client that talks to it in memory."""

    def __init__(
        self,
        engine: Optional[Engine] = None,
        limits: Optional[server.Limits] = None,
        clock: Optional[Callable[[], float]] = None,
        **settings: Any,
    ) -> None:
        if engine is None:
            engine = FakeEngine()
            engine.load()
        self.engine = engine
        self.settings = server.Settings(model=engine.name, limits=limits or server.Limits(**FAST), **settings)
        self.app = server.create_app(engine, self.settings, clock=clock or time.perf_counter)
        host = f"127.0.0.1:{self.settings.host_port}"
        self.client = TestClient(self.app, base_url=f"http://{host}", headers={"host": host})

    def connect(self, path: str, **headers: str) -> Any:
        return self.client.websocket_connect(path, headers=headers)

    @property
    def busy(self) -> bool:
        return self.app.state.slot.busy

    def health(self) -> Dict[str, Any]:
        return self.client.get("/health").json()

    def wait_idle(self, timeout: float = 5.0) -> None:
        """Wait until the recogniser thread of the last session has finished."""
        deadline = time.monotonic() + timeout
        while self.busy:
            if time.monotonic() > deadline:
                raise AssertionError("the model stayed busy")
            time.sleep(0.005)


def limits(**overrides: Any) -> server.Limits:
    return server.Limits(**{**FAST, **overrides})


# -- socket helpers ------------------------------------------------------------


def receive(ws: Any, timeout: float = 5.0) -> Dict[str, Any]:
    """Next JSON message, failing the test instead of hanging when none comes.

    Raises ``WebSocketDisconnect`` when the service closes the socket.
    """
    box: Dict[str, Any] = {}

    def target() -> None:
        try:
            box["value"] = ws.receive_json()
        except BaseException as exc:  # noqa: BLE001
            box["error"] = exc

    thread = threading.Thread(target=target, daemon=True)
    thread.start()
    thread.join(timeout)
    if thread.is_alive():
        raise AssertionError("the service sent nothing")
    if "error" in box:
        raise box["error"]
    return box["value"]


def close_code(ws: Any, timeout: float = 5.0) -> int:
    """Expect the service to close next; return its close code."""
    try:
        message = receive(ws, timeout)
    except WebSocketDisconnect as exc:
        return exc.code
    raise AssertionError(f"expected a close, got {message.get('type')!r}")


def refused(lab: Lab, path: str, **headers: str) -> int:
    """Expect the handshake to be refused; return the close code."""
    try:
        with lab.connect(path, **headers):
            raise AssertionError("the handshake was accepted")
    except WebSocketDisconnect as exc:
        return exc.code


def open_relay(ws: Any, model: str = "nemotron") -> None:
    """Consume ``ready`` and the first ``speechStart``."""
    assert receive(ws) == {"type": "ready", "model": model, "sampleRate": 16000}
    assert receive(ws) == {"type": "speechStart", "turnId": 1, "audioProcessedMs": 0}


def collect(ws: Any, until: str, timeout: float = 5.0) -> List[Dict[str, Any]]:
    """Messages up to and including the first one of type ``until``."""
    messages = []
    while True:
        message = receive(ws, timeout)
        messages.append(message)
        if message["type"] == until:
            return messages
