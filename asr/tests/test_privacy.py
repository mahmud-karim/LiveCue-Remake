"""Nothing sensitive reaches the log, on any path.

The log may hold status lines with numbers, model names, exception type names
and code locations. It may never hold audio, transcript text, text the client
sent, or the message of an exception.
"""

from __future__ import annotations

import asyncio
import json
import logging
import sys
import threading
import warnings

import pytest
from starlette.websockets import WebSocketDisconnect

import server
from engines import Engine, Recognition
from support import CallbackEngine, Lab, drain, limits, receive

SECRET = "SECRET-7f3a"


def captured(capfd, caplog) -> str:
    """Everything the process wrote, after checking the ``logging`` module.

    The only records allowed there are the event loop's own debug lines about
    which selector it uses (they come from the test client starting a loop).
    """
    assert SECRET not in caplog.text
    assert [r for r in caplog.records if (r.name, r.levelno) != ("asyncio", logging.DEBUG)] == []
    out, err = capfd.readouterr()
    return out + err


def until_closed(ws):
    messages = []
    with pytest.raises(WebSocketDisconnect):
        while True:
            messages.append(receive(ws))
    return messages


def speaking(audio, on_partial):
    """An engine whose every text contains the secret."""
    while audio.read() is not None:
        on_partial(f"{SECRET} partial")
    return f"{SECRET} final"


@pytest.mark.parametrize("path, end", [("/relay", "endStream"), ("/stream", "stop")])
def test_a_normal_session_logs_nothing_at_all(path, end, capfd, caplog):
    lab = Lab(CallbackEngine(speaking))
    with caplog.at_level(0):
        with lab.connect(path) as ws:
            receive(ws)
            ws.send_bytes(SECRET.encode().ljust(64, b"\x00"))
            ws.send_text(json.dumps({"type": end}))
            sent_to_client = json.dumps(until_closed(ws))
        lab.wait_idle()
    assert SECRET in sent_to_client  # the client did get the words
    assert captured(capfd, caplog) == ""


@pytest.mark.parametrize("path", ["/relay", "/stream"])
@pytest.mark.parametrize(
    "exception",
    [
        RuntimeError(f"{SECRET} could not decode 'what the speaker said'"),
        KeyError(SECRET),
        UnicodeDecodeError("utf-8", SECRET.encode(), 0, 1, SECRET),
        type("CustomModelError", (Exception,), {})(SECRET),
    ],
    ids=lambda exc: type(exc).__name__,
)
def test_inference_failure_logs_type_and_location_only(path, exception, capfd, caplog):
    def explode(audio, on_partial):
        on_partial(f"{SECRET} heard before the failure")
        raise exception

    lab = Lab(CallbackEngine(explode))
    with caplog.at_level(0):
        with lab.connect(path) as ws:
            until_closed(ws)
        lab.wait_idle()

    text = captured(capfd, caplog)
    assert SECRET not in text and "speaker" not in text
    (line,) = [json.loads(row) for row in text.splitlines()]
    assert line["event"] == "inference_failed"
    assert line["route"] == path.strip("/")
    assert line["errorType"] == type(exception).__name__
    assert line["at"][-1].startswith("test_privacy.py:") and line["at"][-1].endswith(":explode")
    assert set(line) == {"event", "route", "errorType", "at"}


@pytest.mark.parametrize("path", ["/relay", "/stream"])
def test_what_a_client_sends_is_never_logged(path, capfd, caplog):
    lab = Lab()
    with caplog.at_level(0):
        with lab.connect(path) as ws:
            receive(ws)
            ws.send_text(json.dumps({"type": SECRET, "note": SECRET}))
            until_closed(ws)
        with lab.connect(path) as ws:
            receive(ws)
            ws.send_bytes((SECRET * 3).encode()[:33])  # an odd number of bytes
            until_closed(ws)
        lab.wait_idle()
    assert captured(capfd, caplog) == ""


def test_timeouts_and_overflows_log_no_content(capfd, caplog):
    lab = Lab(CallbackEngine(speaking), limits=limits(idle_seconds=1.0, relay_outbound_events=0))
    with caplog.at_level(0):
        with lab.connect("/stream") as ws:  # some words, then the idle limit
            receive(ws)
            ws.send_bytes(bytes(64))
            assert SECRET in receive(ws)["text"]
            until_closed(ws)
        lab.wait_idle()
        with lab.connect("/relay") as ws:  # outbound overflow
            until_closed(ws)
        lab.wait_idle()
    text = captured(capfd, caplog)
    assert SECRET not in text
    assert [json.loads(row) for row in text.splitlines()] == [{"event": "outbound_overflow", "route": "relay"}]


def test_startup_failure_logs_type_and_location_only(monkeypatch, capfd, caplog):
    class Broken(Engine):
        name = "nemotron"
        chunk_ms = 320

        def load(self):
            raise OSError(f"{SECRET}: cannot read C:\\Users\\someone\\weights")

        def recognize(self, audio, on_partial):
            return Recognition("")

    monkeypatch.setattr(server, "create_engine", lambda *args: Broken())
    with caplog.at_level(0):
        assert server.main({}) == 1
    text = captured(capfd, caplog)
    assert SECRET not in text and "someone" not in text
    (line,) = [json.loads(row) for row in text.splitlines()]
    assert (line["event"], line["stage"], line["errorType"]) == ("startup_failed", "load", "OSError")
    assert line["at"][-1].endswith(":load")


def test_warm_up_failure_is_reported_the_same_way(monkeypatch, capfd):
    def explode(audio, on_partial):
        drain(audio)
        raise ValueError(SECRET)

    monkeypatch.setattr(server, "create_engine", lambda *args: CallbackEngine(explode))
    assert server.main({}) == 1
    out = capfd.readouterr().out
    assert SECRET not in out
    lines = [json.loads(row) for row in out.splitlines()]
    assert lines[0] == {"event": "loaded", "model": "nemotron", "loadSeconds": 1.5}
    assert (lines[1]["event"], lines[1]["stage"], lines[1]["errorType"]) == ("startup_failed", "warmup", "ValueError")


# -- the log writer itself -----------------------------------------------------


def test_status_lines_accept_identifiers_and_numbers_only(capfd):
    server.log_event(
        "loaded",
        model="qwen3",
        loadSeconds=41.7,
        ready=True,
        nothing=None,
        sentence="two words",
        quoted='he said "hi"',
        long="x" * 97,
        raw=b"bytes",
        nested={"a": 1},
        nan=float("nan"),
        items=["ok", "not ok", 3],
    )
    assert json.loads(capfd.readouterr().out) == {
        "event": "loaded",
        "model": "qwen3",
        "loadSeconds": 41.7,
        "ready": True,
        "nothing": None,
        "sentence": "[redacted]",
        "quoted": "[redacted]",
        "long": "[redacted]",
        "raw": "[redacted]",
        "nested": "[redacted]",
        "nan": None,
        "items": ["ok", "[redacted]", "[redacted]"],
    }


def test_the_two_documented_status_lines_are_exact(capfd):
    server.log_event("loaded", model="nemotron", loadSeconds=12.25)
    server.log_event("warmup_complete", model="nemotron")
    assert capfd.readouterr().out.splitlines() == [
        '{"event":"loaded","model":"nemotron","loadSeconds":12.25}',
        '{"event":"warmup_complete","model":"nemotron"}',
    ]


def test_exception_description_has_no_message_and_no_paths():
    def inner():
        raise ValueError(f"{SECRET} in C:\\private\\path")

    try:
        inner()
    except ValueError as exc:
        described = server.describe_exception(exc)
    assert described["errorType"] == "ValueError"
    assert SECRET not in json.dumps(described)
    assert all("\\" not in place and "/" not in place for place in described["at"])
    assert described["at"][-1].endswith(":inner") and len(described["at"]) <= 8


# -- the run-time guard ----------------------------------------------------------


@pytest.fixture
def restore_process_state():
    saved = (logging.root.manager.disable, threading.excepthook, sys.unraisablehook, warnings.filters[:])
    yield
    logging.disable(saved[0])
    threading.excepthook, sys.unraisablehook = saved[1], saved[2]
    warnings.filters[:] = saved[3]


def test_guard_silences_logging_warnings_and_uncaught_thread_errors(restore_process_state, capfd):
    server.guard_runtime_logging()

    handler = logging.StreamHandler(sys.stderr)
    logger = logging.getLogger("uvicorn.error")
    logger.addHandler(handler)
    try:
        try:
            raise RuntimeError(SECRET)
        except RuntimeError:
            logger.exception("Exception in ASGI application %s", SECRET)
        logging.getLogger("vllm").critical("decoded: %s", SECRET)
        logging.getLogger().warning(SECRET)
    finally:
        logger.removeHandler(handler)

    warnings.warn(f"{SECRET} deprecated", UserWarning, stacklevel=1)

    def die():
        raise RuntimeError(SECRET)

    thread = threading.Thread(target=die)
    thread.start()
    thread.join()

    out, err = capfd.readouterr()
    assert SECRET not in out + err and err == ""
    (line,) = [json.loads(row) for row in out.splitlines()]
    assert (line["event"], line["errorType"]) == ("thread_error", "RuntimeError")


def test_loop_errors_are_reduced_to_their_type(capfd):
    async def scenario():
        loop = asyncio.get_running_loop()
        loop.set_exception_handler(server.loop_exception_handler)
        loop.call_exception_handler({"message": SECRET, "exception": KeyError(SECRET)})
        loop.call_exception_handler({"message": SECRET})

    asyncio.run(scenario())
    out, err = capfd.readouterr()
    assert SECRET not in out + err
    assert [json.loads(row)["event"] for row in out.splitlines()] == ["loop_error", "loop_error"]
    assert json.loads(out.splitlines()[0])["errorType"] == "KeyError"
