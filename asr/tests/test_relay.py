"""``/relay`` in depth: admission, frames, commands, windows, numbers, timers, queues."""

from __future__ import annotations

import threading

import pytest

import server
from engines import Recognition
from support import (
    HOST,
    HOST_PORT,
    ORIGIN,
    RELAY_BUSY_ERROR,
    RELAY_FAILURE_ERROR,
    RELAY_LIMIT_ERROR,
    STREAM_BUSY_ERROR,
    CallbackEngine,
    DigestEngine,
    FakeClock,
    Lab,
    close_code,
    collect,
    digest,
    drain,
    limits,
    open_relay,
    ramp,
    receive,
    refused,
)

END = '{"type":"endStream"}'


# -- admission -----------------------------------------------------------------


@pytest.mark.parametrize(
    "origin",
    [
        ORIGIN,
        f"http://localhost:{HOST_PORT}",
        "https://evil.example",
        "null",
        "",
    ],
)
def test_any_origin_header_is_refused(origin):
    lab = Lab()
    assert refused(lab, "/relay", origin=origin) == 1008
    assert not lab.busy


@pytest.mark.parametrize(
    "host",
    [
        "evil.example:47842",
        "127.0.0.1:8765",
        "127.0.0.1",
        "localhost",
        "127.0.0.1:47843",
        "LOCALHOST:47842",
        "[::1]:47842",
        "0.0.0.0:47842",
        "127.0.0.1:47842.evil.example",
    ],
)
def test_other_hosts_are_refused(host):
    assert refused(Lab(), "/relay", host=host) == 1008


@pytest.mark.parametrize("host", [HOST, f"localhost:{HOST_PORT}"])
def test_both_loopback_names_are_admitted(host):
    with Lab().connect("/relay", host=host) as ws:
        open_relay(ws)


def test_host_check_follows_the_configured_port():
    lab = Lab(host_port=50123)
    assert refused(lab, "/relay", host=HOST) == 1008
    with lab.connect("/relay", host="127.0.0.1:50123") as ws:
        open_relay(ws)


def test_a_refused_handshake_does_not_take_the_model():
    lab = Lab()
    refused(lab, "/relay", origin=ORIGIN)
    with lab.connect("/relay") as ws:
        open_relay(ws)


# -- the one-session rule ------------------------------------------------------


def test_busy_across_both_sockets_and_health():
    lab = Lab()
    assert lab.health()["busy"] is False
    with lab.connect("/relay") as first:
        open_relay(first)
        assert lab.health()["busy"] is True
        with lab.connect("/stream") as page:
            assert receive(page) == STREAM_BUSY_ERROR
            assert close_code(page) == 1000
        with lab.connect("/relay") as second:
            assert receive(second) == RELAY_BUSY_ERROR
            assert close_code(second) == 1000
        # The running session is untouched by the refusals.
        first.send_bytes(bytes(2560))
        assert receive(first)["type"] == "transcript"
        first.send_text(END)
        assert [m["type"] for m in collect(first, "streamComplete")] == ["speechComplete", "streamComplete"]
    lab.wait_idle()
    assert lab.health()["busy"] is False


def test_relay_is_told_busy_while_a_lab_page_test_runs():
    lab = Lab()
    with lab.connect("/stream") as page:
        assert receive(page)["type"] == "ready"
        with lab.connect("/relay") as relay:
            assert receive(relay) == RELAY_BUSY_ERROR


def test_model_is_free_the_moment_the_client_sees_the_last_frame():
    lab = Lab()
    for _ in range(25):
        with lab.connect("/relay") as ws:
            open_relay(ws)
            ws.send_text(END)
            collect(ws, "streamComplete")
            assert not lab.busy


def test_a_client_that_vanishes_frees_the_model():
    lab = Lab()
    with lab.connect("/relay") as ws:
        open_relay(ws)
        ws.send_bytes(bytes(2560))
        assert receive(ws)["type"] == "transcript"
    lab.wait_idle()
    with lab.connect("/relay") as ws:
        open_relay(ws)


# -- frames --------------------------------------------------------------------


@pytest.mark.parametrize("size", [0, 1, 3, 2559, 16385, 16386, 65536])
def test_bad_frames(size):
    lab = Lab()
    with lab.connect("/relay") as ws:
        open_relay(ws)
        ws.send_bytes(bytes(size))
        assert receive(ws) == RELAY_LIMIT_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()


@pytest.mark.parametrize("size", [2, 2560, 16384])
def test_good_frames(size):
    engine = DigestEngine()
    with Lab(engine).connect("/relay") as ws:
        open_relay(ws)
        ws.send_bytes(ramp(size // 2))
        assert receive(ws) == {
            "type": "transcript",
            "turnId": 1,
            "transcript": str(size // 2),
            "audioProcessedMs": size / 32,
        }


def test_total_cap_is_thirty_minutes_of_audio():
    assert server.Limits().relay_max_total_bytes == 57_600_000 == 16000 * 2 * 60 * 30
    assert server.Limits().relay_max_frame_bytes == 16_384


def test_total_cap_allows_the_last_byte_and_refuses_the_next():
    lab = Lab(DigestEngine(), limits=limits(relay_max_total_bytes=6000))
    with lab.connect("/relay") as ws:
        open_relay(ws)
        ws.send_bytes(bytes(4000))
        assert receive(ws)["transcript"] == "2000"
        ws.send_bytes(bytes(2000))  # exactly at the cap
        assert receive(ws)["transcript"] == "3000"
        ws.send_bytes(bytes(2))  # one sample too many
        assert receive(ws) == RELAY_LIMIT_ERROR
        assert close_code(ws) == 1000


# -- commands ------------------------------------------------------------------


@pytest.mark.parametrize(
    "text",
    [
        '{"type":"stop"}',
        '{"type":"endstream"}',
        '{"type":"endStream","extra":1}',
        '{"type":["endStream"]}',
        '{"Type":"endStream"}',
        '"endStream"',
        "endStream",
        "[]",
        "null",
        "{}",
        "",
        "{",
        '{"type":"endStream"}{"type":"endStream"}',
        "[" * 5000,
        '{"type":"endStream"' + " " * 400 + "}",
    ],
)
def test_any_other_text_is_an_error(text):
    lab = Lab()
    with lab.connect("/relay") as ws:
        open_relay(ws)
        ws.send_text(text)
        assert receive(ws) == RELAY_LIMIT_ERROR
        assert close_code(ws) == 1000


@pytest.mark.parametrize("text", ['{"type":"endStream"}', ' { "type" : "endStream" } ', '{"type":"endStream"}\n'])
def test_end_stream_is_recognised_as_json(text):
    with Lab().connect("/relay") as ws:
        open_relay(ws)
        ws.send_text(text)
        assert collect(ws, "streamComplete")[-1]["type"] == "streamComplete"


def test_frames_after_end_stream_are_ignored():
    engine = DigestEngine()
    with Lab(engine).connect("/relay") as ws:
        open_relay(ws)
        ws.send_bytes(ramp(100))
        ws.send_text(END)
        ws.send_bytes(ramp(100))
        ws.send_text("anything")
        last = collect(ws, "streamComplete")[-1]
        assert last["audioProcessedMs"] == 100 / 16
        assert close_code(ws) == 1000
    assert engine.heard == [ramp(100)]


# -- the frames of a session, exactly ------------------------------------------


def test_every_frame_of_a_short_session():
    clock = FakeClock()
    with Lab(clock=clock).connect("/relay") as ws:
        assert receive(ws) == {"type": "ready", "model": "nemotron", "sampleRate": 16000}
        assert receive(ws) == {"type": "speechStart", "turnId": 1, "audioProcessedMs": 0}
        ws.send_bytes(bytes(2560))
        assert receive(ws) == {
            "type": "transcript",
            "turnId": 1,
            "transcript": "test caption",
            "audioProcessedMs": 80,
        }
        ws.send_text(END)
        assert receive(ws) == {
            "type": "speechComplete",
            "turnId": 1,
            "transcript": "test caption",
            "audioProcessedMs": 80,
        }
        assert receive(ws) == {"type": "streamComplete", "audioProcessedMs": 80, "finalizationMs": 0}
        assert close_code(ws) == 1000


def test_transcript_is_sent_only_when_the_text_changes():
    """The fake engine reports the same caption for every chunk."""
    with Lab().connect("/relay") as ws:
        open_relay(ws)
        for _ in range(5):
            ws.send_bytes(bytes(2560))
        ws.send_text(END)
        kinds = [m["type"] for m in collect(ws, "streamComplete")]
        assert kinds == ["transcript", "speechComplete", "streamComplete"]


def test_end_of_stream_with_no_audio_still_completes_a_turn():
    with Lab(DigestEngine()).connect("/relay") as ws:
        open_relay(ws)
        ws.send_text(END)
        assert receive(ws) == {
            "type": "speechComplete",
            "turnId": 1,
            "transcript": digest(b""),
            "audioProcessedMs": 0,
        }
        done = receive(ws)
        assert done["type"] == "streamComplete" and done["audioProcessedMs"] == 0
        assert done["finalizationMs"] >= 0


def test_empty_final_text_is_allowed():
    def silent(audio, on_partial):
        drain(audio)
        return ""

    with Lab(CallbackEngine(silent)).connect("/relay") as ws:
        open_relay(ws)
        ws.send_text(END)
        assert receive(ws)["transcript"] == ""


def test_text_that_is_not_a_string_becomes_empty():
    def odd(audio, on_partial):
        drain(audio)
        on_partial(None)
        return Recognition(None)

    with Lab(CallbackEngine(odd)).connect("/relay") as ws:
        open_relay(ws)
        ws.send_text(END)
        assert receive(ws) == {"type": "speechComplete", "turnId": 1, "transcript": "", "audioProcessedMs": 0}


# -- windows -------------------------------------------------------------------


def run_windows(audio: bytes, frame_bytes: int, window_samples: int):
    """Send ``audio`` in frames, end the stream, return (messages, engine)."""
    engine = DigestEngine()
    lab = Lab(engine, limits=limits(relay_window_samples=window_samples, relay_inbound_frames=100_000))
    with lab.connect("/relay") as ws:
        assert receive(ws)["type"] == "ready"
        for start in range(0, len(audio), frame_bytes):
            ws.send_bytes(audio[start : start + frame_bytes])
        ws.send_text(END)
        messages = collect(ws, "streamComplete", timeout=30)
        assert close_code(ws) == 1000
    return messages, engine


def test_a_frame_that_straddles_a_boundary_is_cut_exactly():
    # 3 frames of 700 samples against windows of 1,000 samples.
    audio = ramp(2100)
    messages, engine = run_windows(audio, frame_bytes=1400, window_samples=1000)

    assert engine.heard == [audio[0:2000], audio[2000:4000], audio[4000:4200]]
    assert b"".join(engine.heard) == audio  # nothing dropped, nothing repeated

    starts = [m for m in messages if m["type"] == "speechStart"]
    completes = [m for m in messages if m["type"] == "speechComplete"]
    assert starts == [
        {"type": "speechStart", "turnId": 1, "audioProcessedMs": 0},
        {"type": "speechStart", "turnId": 2, "audioProcessedMs": 62.5},
        {"type": "speechStart", "turnId": 3, "audioProcessedMs": 125},
    ]
    assert completes == [
        {"type": "speechComplete", "turnId": 1, "transcript": digest(audio[0:2000]), "audioProcessedMs": 62.5},
        {"type": "speechComplete", "turnId": 2, "transcript": digest(audio[2000:4000]), "audioProcessedMs": 125},
        {"type": "speechComplete", "turnId": 3, "transcript": digest(audio[4000:4200]), "audioProcessedMs": 131.25},
    ]
    assert messages[-1]["type"] == "streamComplete"
    assert messages[-1]["audioProcessedMs"] == 131.25 == len(audio) / 32


def test_messages_arrive_in_turn_order():
    messages, _ = run_windows(ramp(2100), frame_bytes=1400, window_samples=1000)
    order = [(m["type"], m.get("turnId")) for m in messages if m["type"] != "transcript"]
    assert order == [
        ("speechStart", 1),
        ("speechComplete", 1),
        ("speechStart", 2),
        ("speechComplete", 2),
        ("speechStart", 3),
        ("speechComplete", 3),
        ("streamComplete", None),
    ]
    # Every transcript belongs to the turn that is open when it is sent.
    open_turn = None
    for message in messages:
        if message["type"] == "speechStart":
            open_turn = message["turnId"]
        elif message["type"] == "transcript":
            assert message["turnId"] == open_turn
        elif message["type"] == "speechComplete":
            assert message["turnId"] == open_turn
            open_turn = None


def test_transcript_progress_counts_samples_consumed_so_far():
    messages, _ = run_windows(ramp(2100), frame_bytes=1400, window_samples=1000)
    partials = [(m["turnId"], m["transcript"], m["audioProcessedMs"]) for m in messages if m["type"] == "transcript"]
    # (turn, samples read in this window, total samples consumed / 16)
    assert partials == [
        (1, "700", 700 / 16),
        (1, "1000", 1000 / 16),
        (2, "400", 1400 / 16),
        (2, "1000", 2000 / 16),
        (3, "100", 2100 / 16),
    ]


def test_audio_that_ends_exactly_on_a_boundary_gets_an_empty_last_turn():
    audio = ramp(2000)
    messages, engine = run_windows(audio, frame_bytes=1000, window_samples=1000)
    assert engine.heard == [audio[0:2000], audio[2000:4000], b""]
    completes = [m for m in messages if m["type"] == "speechComplete"]
    assert [m["turnId"] for m in completes] == [1, 2, 3]
    assert completes[-1] == {"type": "speechComplete", "turnId": 3, "transcript": digest(b""), "audioProcessedMs": 125}
    assert messages[-1]["audioProcessedMs"] == 125


def test_one_sample_is_a_sixteenth_of_a_millisecond():
    messages, _ = run_windows(ramp(1), frame_bytes=2, window_samples=1000)
    assert messages[-1]["audioProcessedMs"] == 0.0625


@pytest.mark.parametrize("seed", range(12))
def test_random_frame_sizes_never_lose_or_repeat_a_sample(seed):
    import random

    rng = random.Random(seed)
    window = rng.randrange(1, 900)
    total = rng.randrange(0, 6000)
    audio = ramp(total, start=seed * 131)
    engine = DigestEngine()
    lab = Lab(engine, limits=limits(relay_window_samples=window, relay_inbound_frames=100_000))
    with lab.connect("/relay") as ws:
        assert receive(ws)["type"] == "ready"
        position = 0
        while position < len(audio):
            size = 2 * rng.randrange(1, 700)
            ws.send_bytes(audio[position : position + size])
            position += size
        ws.send_text(END)
        messages = collect(ws, "streamComplete", timeout=30)

    assert b"".join(engine.heard) == audio
    assert [len(part) // 2 for part in engine.heard[:-1]] == [window] * (total // window)
    assert len(engine.heard[-1]) // 2 == total % window
    turns = [m["turnId"] for m in messages if m["type"] == "speechComplete"]
    assert turns == list(range(1, total // window + 2))
    assert messages[-1]["audioProcessedMs"] == total / 16


def test_the_real_thirty_second_boundary():
    """Default window: 480,000 samples. 59 full-size frames cross it once."""
    assert server.Limits().relay_window_samples == 480_000
    audio = ramp(59 * 8192)
    engine = DigestEngine()
    lab = Lab(engine, limits=limits(relay_inbound_frames=100_000))
    with lab.connect("/relay") as ws:
        assert receive(ws)["type"] == "ready"
        for start in range(0, len(audio), 16384):
            ws.send_bytes(audio[start : start + 16384])
        ws.send_text(END)
        messages = collect(ws, "streamComplete", timeout=60)

    boundary = 480_000 * 2
    assert engine.heard == [audio[:boundary], audio[boundary:]]
    marks = [(m["type"], m["turnId"], m["audioProcessedMs"]) for m in messages if m["type"].startswith("speech")]
    assert marks == [
        ("speechStart", 1, 0),
        ("speechComplete", 1, 30_000),
        ("speechStart", 2, 30_000),
        ("speechComplete", 2, 59 * 8192 / 16),
    ]
    assert messages[-1]["audioProcessedMs"] == len(audio) / 32


def test_a_recogniser_that_stops_early_loses_no_audio():
    """The next window starts where the recogniser stopped reading."""

    def impatient(audio, on_partial):
        return digest(audio.read() or b"")

    audio = ramp(300)
    engine = CallbackEngine(impatient)
    lab = Lab(engine, limits=limits(relay_window_samples=1000))
    with lab.connect("/relay") as ws:
        open_relay(ws)
        for start in range(0, 600, 200):
            ws.send_bytes(audio[start : start + 200])
        ws.send_text(END)
        messages = collect(ws, "streamComplete")
    completes = [m for m in messages if m["type"] == "speechComplete"]
    assert [m["transcript"] for m in completes] == [
        digest(audio[0:200]),
        digest(audio[200:400]),
        digest(audio[400:600]),
        digest(b""),
    ]
    assert [m["audioProcessedMs"] for m in completes] == [6.25, 12.5, 18.75, 18.75]


def test_a_recogniser_that_reads_nothing_is_a_failure_not_a_loop():
    lab = Lab(CallbackEngine(lambda audio, on_partial: "never read"))
    with lab.connect("/relay") as ws:
        open_relay(ws)
        assert receive(ws) == RELAY_FAILURE_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()


# -- streamComplete and finalizationMs -------------------------------------------


def test_finalization_time_is_measured_from_end_stream():
    clock = FakeClock(100.0)

    def slow_to_finish(audio, on_partial):
        drain(audio)
        clock.advance(0.25)  # finishing takes a quarter of a second
        return "done"

    with Lab(CallbackEngine(slow_to_finish), clock=clock).connect("/relay") as ws:
        open_relay(ws)
        ws.send_bytes(bytes(3200))
        ws.send_text(END)
        assert receive(ws)["type"] == "speechComplete"
        assert receive(ws) == {"type": "streamComplete", "audioProcessedMs": 100, "finalizationMs": 250}


def test_stream_complete_is_followed_by_a_normal_close_and_nothing_else():
    with Lab().connect("/relay") as ws:
        open_relay(ws)
        ws.send_text(END)
        collect(ws, "streamComplete")
        assert close_code(ws) == 1000


# -- timers ----------------------------------------------------------------------


def test_timer_defaults():
    defaults = server.Limits()
    assert defaults.idle_seconds == 20.0
    assert defaults.relay_finalize_seconds == 12.0
    assert defaults.closing_seconds == 2.0


def test_idle_limit_counts_from_the_last_frame():
    lab = Lab(limits=limits(idle_seconds=1.0))
    with lab.connect("/relay") as ws:
        open_relay(ws)
        ws.send_bytes(bytes(2560))
        assert receive(ws)["type"] == "transcript"
        assert receive(ws) == RELAY_LIMIT_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()


def test_finalization_that_takes_too_long_ends_with_the_error():
    release = threading.Event()

    def stuck(audio, on_partial):
        drain(audio)
        release.wait(10)
        return "too late"

    lab = Lab(CallbackEngine(stuck), limits=limits(relay_finalize_seconds=0.05))
    try:
        with lab.connect("/relay") as ws:
            open_relay(ws)
            ws.send_text(END)
            assert receive(ws) == RELAY_LIMIT_ERROR
            assert close_code(ws) == 1000
            assert lab.busy  # the recogniser has not let go yet
    finally:
        release.set()
    lab.wait_idle()


def test_the_idle_limit_does_not_apply_after_end_stream():
    release = threading.Event()

    def slow(audio, on_partial):
        drain(audio)
        release.wait(1.5)  # three idle limits
        return "worth waiting for"

    lab = Lab(CallbackEngine(slow), limits=limits(idle_seconds=0.5, relay_finalize_seconds=10))
    with lab.connect("/relay") as ws:
        open_relay(ws)
        ws.send_text(END)
        assert receive(ws)["transcript"] == "worth waiting for"
        assert receive(ws)["type"] == "streamComplete"


# -- queues ------------------------------------------------------------------------


def test_queue_defaults():
    defaults = server.Limits()
    assert defaults.relay_inbound_frames == 128
    assert defaults.relay_outbound_events == 256


def test_inbound_queue_overflow():
    release = threading.Event()

    def not_listening(audio, on_partial):
        release.wait(10)
        drain(audio)
        return ""

    lab = Lab(CallbackEngine(not_listening), limits=limits(relay_inbound_frames=4))
    try:
        with lab.connect("/relay") as ws:
            open_relay(ws)
            for _ in range(4):
                ws.send_bytes(bytes(320))  # fills the queue
            ws.send_bytes(bytes(320))  # the fifth does not fit
            assert receive(ws) == RELAY_LIMIT_ERROR
            assert close_code(ws) == 1000
    finally:
        release.set()
    lab.wait_idle()


def test_end_stream_fits_even_when_the_queue_is_full():
    release = threading.Event()

    def late(audio, on_partial):
        release.wait(10)
        return digest(drain(audio))

    lab = Lab(CallbackEngine(late), limits=limits(relay_inbound_frames=4))
    with lab.connect("/relay") as ws:
        open_relay(ws)
        for _ in range(4):
            ws.send_bytes(bytes(320))
        ws.send_text(END)
        release.set()
        assert receive(ws)["transcript"] == digest(bytes(1280))
        assert receive(ws)["type"] == "streamComplete"


def test_outbound_queue_overflow_ends_the_session_with_the_error():
    """Deterministic here with a queue of no size; test_units.py and
    test_live_server.py cover a client that really stops reading."""
    lab = Lab(limits=limits(relay_outbound_events=0))
    with lab.connect("/relay") as ws:
        assert receive(ws)["type"] == "ready"
        assert receive(ws) == RELAY_LIMIT_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()
    with lab.connect("/relay") as ws:  # and the model is free again
        assert receive(ws)["type"] == "ready"


# -- failures ------------------------------------------------------------------------


def test_failure_in_a_later_turn():
    turns = []

    def second_turn_fails(audio, on_partial):
        turns.append(1)
        if len(turns) == 2:
            raise ValueError("boom")
        return digest(drain(audio))

    lab = Lab(CallbackEngine(second_turn_fails), limits=limits(relay_window_samples=100))
    with lab.connect("/relay") as ws:
        open_relay(ws)
        ws.send_bytes(ramp(150))
        assert receive(ws)["type"] == "speechComplete"
        assert receive(ws) == {"type": "speechStart", "turnId": 2, "audioProcessedMs": 6.25}
        assert receive(ws) == RELAY_FAILURE_ERROR
        assert close_code(ws) == 1000
    lab.wait_idle()


def test_base_exceptions_are_contained_too():
    def exits(audio, on_partial):
        raise SystemExit(3)

    lab = Lab(CallbackEngine(exits))
    with lab.connect("/relay") as ws:
        open_relay(ws)
        assert receive(ws) == RELAY_FAILURE_ERROR
    lab.wait_idle()
