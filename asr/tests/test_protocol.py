"""The lab statements of the conformance checklist, one test per statement.

``docs/architecture/02-relay-protocol.md`` section 15.8 (P2-150 to P2-161),
which carries over ``docs/spec/03-relay.md`` section 8.11 (AC-RELAY-100 to
107). Everything runs against the fake engine: no GPU, no model, no Docker.
The other test files go further into each area.
"""

from __future__ import annotations

import json

import pytest
from starlette.websockets import WebSocketDisconnect

import server
from engines.fake import FakeEngine
from support import (
    HOST_PORT,
    ORIGIN,
    RELAY_BUSY_ERROR,
    RELAY_FAILURE_ERROR,
    RELAY_LIMIT_ERROR,
    STREAM_FAILURE_ERROR,
    CallbackEngine,
    Lab,
    close_code,
    limits,
    pcm,
    receive,
    refused,
    unpack,
)


def test_p2_150_windows_preserve_every_sample():
    """AC-RELAY-100: [0..6], [7..10], end of input, read through windows of 5 samples."""
    frames = server.FrameQueue(capacity=8)
    assert frames.put(pcm(range(0, 7)))
    assert frames.put(pcm(range(7, 11)))
    frames.end()

    heard = []
    carry = b""
    while True:
        window = server.AudioWindow(frames, carry, limit_samples=5)
        while True:
            part = window.read()
            if part is None:
                break
            heard.extend(unpack(part))
        carry = window.carry
        if window.ended:
            break

    assert heard == list(range(11))


def test_p2_151_relay_session():
    """AC-RELAY-101."""
    lab = Lab()
    with lab.connect("/relay") as ws:
        assert receive(ws)["sampleRate"] == 16000
        assert receive(ws)["type"] == "speechStart"
        ws.send_bytes(bytes(2560))
        assert receive(ws)["transcript"] == "test caption"
        ws.send_text('{"type":"endStream"}')
        assert receive(ws)["type"] == "speechComplete"
        last = receive(ws)
        assert last["type"] == "streamComplete"
        assert last["audioProcessedMs"] == 80


def test_p2_152_relay_refuses_the_labs_own_origin():
    """AC-RELAY-102: a browser origin is never allowed on the native route."""
    lab = Lab()
    assert refused(lab, "/relay", origin=f"http://localhost:{HOST_PORT}") == 1008
    assert not lab.busy


def test_p2_153_stream_refuses_a_foreign_origin():
    """AC-RELAY-103."""
    assert refused(Lab(), "/stream", origin="https://evil.example") == 1008


@pytest.mark.parametrize("path", ["/stream", "/relay"])
def test_p2_154_host_must_be_the_configured_loopback_port(path):
    """AC-RELAY-104, plus the configurable host port."""
    lab = Lab(host_port=47842)
    assert refused(lab, path, host="evil.example:47842") == 1008
    assert refused(lab, path, host="127.0.0.1:8765") == 1008
    with lab.connect(path, host="127.0.0.1:47842") as ws:
        assert receive(ws)["type"] == "ready"


def test_p2_155_stream_session():
    """AC-RELAY-105."""
    lab = Lab()
    with lab.connect("/stream", origin=ORIGIN) as ws:
        assert receive(ws)["type"] == "ready"
        ws.send_bytes(bytes(2560))
        assert receive(ws)["type"] == "transcript"
        ws.send_text('{"type":"stop"}')
        result = receive(ws)
        assert result["type"] == "result"
        assert result["inputSeconds"] == pytest.approx(0.08)
        assert result["finalizationMs"] >= 0


def test_p2_156_stream_rejects_a_single_byte():
    """AC-RELAY-106."""
    lab = Lab()
    with lab.connect("/stream") as ws:
        assert receive(ws)["type"] == "ready"
        ws.send_bytes(b"x")
        assert receive(ws)["type"] == "error"


def test_p2_157_one_session_at_a_time():
    """AC-RELAY-107."""
    lab = Lab()
    with lab.connect("/stream") as first:
        assert receive(first)["type"] == "ready"
        with lab.connect("/stream") as second:
            assert receive(second)["type"] == "error"
        first.send_text('{"type":"stop"}')
        assert receive(first)["type"] == "result"


@pytest.mark.parametrize(
    "send",
    [
        pytest.param(lambda ws: ws.send_bytes(bytes(16385 + 1)), id="frame over 16,384 bytes"),
        pytest.param(lambda ws: ws.send_bytes(bytes(16385)), id="16,385-byte frame"),
        pytest.param(lambda ws: ws.send_bytes(bytes(3)), id="odd length"),
        pytest.param(lambda ws: ws.send_text('{"type":"stop"}'), id="text other than endStream"),
    ],
)
def test_p2_158_relay_violations_give_the_fixed_error(send):
    """UT-23."""
    lab = Lab()
    with lab.connect("/relay") as ws:
        receive(ws), receive(ws)
        send(ws)
        assert receive(ws) == RELAY_LIMIT_ERROR
        assert close_code(ws) == 1000


def test_p2_158_relay_idle_limit():
    """UT-23: 20 s without input. The timer is shortened; its default is checked."""
    assert server.Limits().idle_seconds == 20.0
    lab = Lab(limits=limits(idle_seconds=0.05))
    with lab.connect("/relay") as ws:
        receive(ws), receive(ws)
        assert receive(ws) == RELAY_LIMIT_ERROR
        assert close_code(ws) == 1000


def test_p2_158_second_relay_session_is_told_the_model_is_busy():
    """UT-23."""
    lab = Lab()
    with lab.connect("/relay") as first:
        receive(first), receive(first)
        with lab.connect("/relay") as second:
            assert receive(second) == RELAY_BUSY_ERROR
            assert close_code(second) == 1000
        first.send_text('{"type":"endStream"}')
        assert receive(first)["type"] == "speechComplete"
        assert receive(first)["type"] == "streamComplete"


def test_p2_159_outbound_overflow_sends_an_error_before_closing():
    """New in the remake (old defect B18): overflow is never silent."""
    lab = Lab(limits=limits(relay_outbound_events=0))  # the first event overflows
    with lab.connect("/relay") as ws:
        assert receive(ws)["type"] == "ready"
        assert receive(ws) == RELAY_LIMIT_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()


def test_p2_160_health_document():
    """UT-24: every member, with its documented type."""
    engine = FakeEngine("qwen3")
    engine.load()
    body = Lab(engine).client.get("/health")
    assert body.status_code == 200
    health = body.json()
    assert health == {
        "model": "qwen3",
        "ready": True,
        "loadSeconds": 0.0,
        "sampleRate": 16000,
        "maxSeconds": 120,
        "chunkMs": 1000,
        "relayProtocol": 1,
        "busy": False,
    }
    raw = json.loads(body.text)
    assert type(raw["ready"]) is bool and type(raw["busy"]) is bool
    assert type(raw["relayProtocol"]) is int and raw["relayProtocol"] == 1
    assert type(raw["sampleRate"]) is int and type(raw["maxSeconds"]) is int and type(raw["chunkMs"]) is int
    assert isinstance(raw["loadSeconds"], (int, float)) and isinstance(raw["model"], str)


@pytest.mark.parametrize(
    "path, end, error",
    [("/relay", "endStream", RELAY_FAILURE_ERROR), ("/stream", "stop", STREAM_FAILURE_ERROR)],
)
def test_p2_161_inference_failure_logs_the_type_and_never_the_text(path, end, error, capfd, caplog):
    """New in the remake (old defect B19), on both routes."""

    def explode(audio, on_partial):
        audio.read()
        raise LookupError("SECRET words the speaker said")

    lab = Lab(CallbackEngine(explode))
    with caplog.at_level(0):
        with lab.connect(path) as ws:
            assert receive(ws)["type"] == "ready"
            ws.send_bytes(bytes(2560))
            messages = []
            with pytest.raises(WebSocketDisconnect):
                while True:
                    messages.append(receive(ws))
            assert messages[-1] == error
        lab.wait_idle()

    out, err = capfd.readouterr()
    logged = out + err + caplog.text
    assert "SECRET" not in logged and "speaker" not in logged
    line = next(json.loads(text) for text in out.splitlines() if "inference_failed" in text)
    assert line["errorType"] == "LookupError"
    assert line["route"] == path.strip("/")
