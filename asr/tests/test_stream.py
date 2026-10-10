"""``/stream`` in depth: the lab page protocol."""

from __future__ import annotations

import threading

import pytest

import server
from engines import Recognition
from engines.fake import FakeEngine
from support import (
    HOST,
    HOST_PORT,
    ORIGIN,
    STREAM_FAILURE_ERROR,
    STREAM_LIMIT_ERROR,
    CallbackEngine,
    DigestEngine,
    FakeClock,
    Lab,
    close_code,
    digest,
    drain,
    limits,
    ramp,
    receive,
    refused,
)

STOP = '{"type":"stop"}'


def open_stream(ws, model="nemotron"):
    assert receive(ws) == {"type": "ready", "model": model, "sampleRate": 16000}


# -- admission -----------------------------------------------------------------


@pytest.mark.parametrize("origin", [ORIGIN, f"http://localhost:{HOST_PORT}"])
def test_the_labs_own_origins_are_admitted(origin):
    with Lab().connect("/stream", origin=origin) as ws:
        open_stream(ws)


def test_no_origin_is_admitted():
    """The benchmark tool is not a browser and sends none."""
    with Lab().connect("/stream") as ws:
        open_stream(ws)


@pytest.mark.parametrize(
    "origin",
    [
        "https://evil.example",
        "http://127.0.0.1:8765",
        f"https://127.0.0.1:{HOST_PORT}",
        f"http://127.0.0.1:{HOST_PORT}/",
        f"http://127.0.0.1:{HOST_PORT}.evil.example",
        f"http://LOCALHOST:{HOST_PORT}",
        "http://127.0.0.1",
        "null",
        "",
    ],
)
def test_other_origins_are_refused(origin):
    lab = Lab()
    assert refused(lab, "/stream", origin=origin) == 1008
    assert not lab.busy


@pytest.mark.parametrize("host", ["evil.example:47842", "127.0.0.1:8765", "127.0.0.1", "example.test"])
def test_other_hosts_are_refused_even_with_a_good_origin(host):
    assert refused(Lab(), "/stream", host=host, origin=ORIGIN) == 1008


def test_origin_check_follows_the_configured_port():
    lab = Lab(host_port=50123)
    assert refused(lab, "/stream", host="127.0.0.1:50123", origin=ORIGIN) == 1008
    assert refused(lab, "/stream", host=HOST, origin=ORIGIN) == 1008
    with lab.connect("/stream", host="127.0.0.1:50123", origin="http://127.0.0.1:50123") as ws:
        open_stream(ws)


# -- frames and commands ---------------------------------------------------------


@pytest.mark.parametrize("size", [1, 3, 64001, 64002, 65536])
def test_bad_frames(size):
    lab = Lab()
    with lab.connect("/stream") as ws:
        open_stream(ws)
        ws.send_bytes(bytes(size))
        assert receive(ws) == STREAM_LIMIT_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()


def test_largest_frame_and_empty_frame_are_fine():
    engine = DigestEngine()
    audio = ramp(32000)
    with Lab(engine).connect("/stream") as ws:
        open_stream(ws)
        ws.send_bytes(b"")
        ws.send_bytes(audio)  # 64,000 bytes
        assert receive(ws)["text"] == "32000"
        ws.send_text(STOP)
        assert receive(ws)["text"] == digest(audio)
    assert engine.heard == [audio]


def test_caps_are_two_minutes_of_audio():
    defaults = server.Limits()
    assert defaults.stream_max_frame_bytes == 64_000
    assert defaults.stream_max_total_bytes == 3_840_000 == 16000 * 2 * 120
    assert defaults.stream_finalize_seconds == 60.0


def test_total_cap_allows_the_last_byte_and_refuses_the_next():
    lab = Lab(DigestEngine(), limits=limits(stream_max_total_bytes=6000))
    with lab.connect("/stream") as ws:
        open_stream(ws)
        ws.send_bytes(bytes(6000))
        assert receive(ws)["text"] == "3000"
        ws.send_bytes(bytes(2))
        assert receive(ws) == STREAM_LIMIT_ERROR
        assert close_code(ws) == 1000


@pytest.mark.parametrize("text", ['{"type":"endStream"}', '{"type":"stop","now":true}', "stop", "", "{", "[]"])
def test_any_other_text_is_an_error(text):
    with Lab().connect("/stream") as ws:
        open_stream(ws)
        ws.send_text(text)
        assert receive(ws) == STREAM_LIMIT_ERROR
        assert close_code(ws) == 1000


# -- the frames of a session, exactly ---------------------------------------------


def test_every_member_of_every_frame():
    clock = FakeClock(50.0)

    def recognise(audio, on_partial):
        audio.read()
        clock.advance(0.5)
        on_partial("hello")
        drain(audio)
        clock.advance(0.125)
        return "hello world"

    with Lab(CallbackEngine(recognise), clock=clock).connect("/stream") as ws:
        open_stream(ws)
        ws.send_bytes(bytes(2560))
        assert receive(ws) == {"type": "transcript", "text": "hello", "elapsedMs": 500, "audioMs": 80}
        ws.send_bytes(bytes(640))
        ws.send_text(STOP)
        assert receive(ws) == {
            "type": "result",
            "text": "hello world",
            "elapsedMs": 625,
            "audioMs": 100,
            "model": "nemotron",
            "firstTextMs": 500,
            "finalizationMs": 125,
            "inputSeconds": 0.1,
        }
        assert close_code(ws) == 1000


def test_no_text_means_no_first_text_time():
    def silent(audio, on_partial):
        drain(audio)
        return ""

    with Lab(CallbackEngine(silent)).connect("/stream") as ws:
        open_stream(ws)
        ws.send_text(STOP)
        result = receive(ws)
        assert result["text"] == "" and result["firstTextMs"] is None
        assert result["audioMs"] == 0 and result["inputSeconds"] == 0
        assert result["finalizationMs"] >= 0 and result["elapsedMs"] >= 0


def test_first_text_time_can_come_from_the_final_text():
    clock = FakeClock()

    def late(audio, on_partial):
        drain(audio)
        clock.advance(2)
        return "only at the end"

    with Lab(CallbackEngine(late), clock=clock).connect("/stream") as ws:
        open_stream(ws)
        ws.send_text(STOP)
        assert receive(ws)["firstTextMs"] == 2000


def test_partial_is_sent_only_when_the_text_changes():
    with Lab().connect("/stream") as ws:
        open_stream(ws)
        for _ in range(4):
            ws.send_bytes(bytes(2560))
        ws.send_text(STOP)
        assert receive(ws)["type"] == "transcript"
        assert receive(ws)["type"] == "result"


def test_the_whole_session_is_one_recognition():
    """No windowing on this socket, whatever the relay window is."""
    engine = DigestEngine()
    audio = ramp(5000)
    lab = Lab(engine, limits=limits(relay_window_samples=100))
    with lab.connect("/stream") as ws:
        open_stream(ws)
        for start in range(0, len(audio), 2000):
            ws.send_bytes(audio[start : start + 2000])
        ws.send_text(STOP)
        while True:
            message = receive(ws)
            if message["type"] == "result":
                break
    assert engine.heard == [audio]
    assert message["text"] == digest(audio)
    assert message["inputSeconds"] == len(audio) / 32000


def test_result_names_the_loaded_model():
    engine = FakeEngine("qwen3")
    engine.load()
    with Lab(engine).connect("/stream") as ws:
        open_stream(ws, "qwen3")
        ws.send_text(STOP)
        assert receive(ws)["model"] == "qwen3"


def test_engine_measurement_is_passed_through_and_nothing_else():
    def measured(audio, on_partial):
        drain(audio)
        return Recognition("text", {"processingCallP95Ms": 12.5, "secretInternal": 1, "elapsedMs": -1})

    with Lab(CallbackEngine(measured, name="qwen3")).connect("/stream") as ws:
        open_stream(ws, "qwen3")
        ws.send_text(STOP)
        result = receive(ws)
    assert result["processingCallP95Ms"] == 12.5
    assert "secretInternal" not in result and result["elapsedMs"] >= 0
    assert set(result) == {
        "type",
        "text",
        "elapsedMs",
        "audioMs",
        "model",
        "firstTextMs",
        "finalizationMs",
        "inputSeconds",
        "processingCallP95Ms",
    }


def test_engine_measurement_may_be_null_and_is_absent_without_one():
    def no_calls(audio, on_partial):
        drain(audio)
        return Recognition("", {"processingCallP95Ms": None})

    with Lab(CallbackEngine(no_calls)).connect("/stream") as ws:
        open_stream(ws)
        ws.send_text(STOP)
        assert receive(ws)["processingCallP95Ms"] is None
    with Lab().connect("/stream") as ws:
        open_stream(ws)
        ws.send_text(STOP)
        assert "processingCallP95Ms" not in receive(ws)


# -- timers and queues -------------------------------------------------------------


def test_idle_limit():
    lab = Lab(limits=limits(idle_seconds=0.05))
    with lab.connect("/stream") as ws:
        open_stream(ws)
        assert receive(ws) == STREAM_LIMIT_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()


def test_finalization_that_takes_too_long():
    release = threading.Event()

    def stuck(audio, on_partial):
        drain(audio)
        release.wait(10)
        return "too late"

    lab = Lab(CallbackEngine(stuck), limits=limits(stream_finalize_seconds=0.05))
    try:
        with lab.connect("/stream") as ws:
            open_stream(ws)
            ws.send_text(STOP)
            assert receive(ws) == STREAM_LIMIT_ERROR
            assert close_code(ws) == 1000
    finally:
        release.set()
    lab.wait_idle()


def test_inbound_queue_overflow():
    release = threading.Event()

    def not_listening(audio, on_partial):
        release.wait(10)
        drain(audio)
        return ""

    lab = Lab(CallbackEngine(not_listening), limits=limits(stream_inbound_frames=3))
    try:
        with lab.connect("/stream") as ws:
            open_stream(ws)
            for _ in range(3):
                ws.send_bytes(bytes(320))
            ws.send_bytes(bytes(320))
            assert receive(ws) == STREAM_LIMIT_ERROR
            assert close_code(ws) == 1000
    finally:
        release.set()
    lab.wait_idle()


def test_outbound_queue_overflow():
    lab = Lab(limits=limits(stream_outbound_events=0))
    with lab.connect("/stream") as ws:
        open_stream(ws)
        ws.send_bytes(bytes(320))  # the first partial does not fit
        assert receive(ws) == STREAM_LIMIT_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()


# -- failures ------------------------------------------------------------------------


def test_inference_failure_message_and_no_timing_members():
    def explode(audio, on_partial):
        raise RuntimeError("nope")

    lab = Lab(CallbackEngine(explode))
    with lab.connect("/stream") as ws:
        open_stream(ws)
        assert receive(ws) == STREAM_FAILURE_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()


def test_words_recognised_before_a_failure_still_arrive_first():
    def fails_late(audio, on_partial):
        audio.read()
        on_partial("so far so good")
        raise RuntimeError("nope")

    lab = Lab(CallbackEngine(fails_late))
    with lab.connect("/stream") as ws:
        open_stream(ws)
        ws.send_bytes(bytes(320))
        assert receive(ws)["text"] == "so far so good"
        assert receive(ws) == STREAM_FAILURE_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()


def test_a_client_that_vanishes_frees_the_model():
    lab = Lab()
    with lab.connect("/stream") as ws:
        open_stream(ws)
        ws.send_bytes(bytes(2560))
        assert receive(ws)["type"] == "transcript"
    lab.wait_idle()
    assert lab.health()["busy"] is False
