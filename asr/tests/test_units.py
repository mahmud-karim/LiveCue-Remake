"""The building blocks on their own: queue, window, outbox, command parsing.

The last tests drive a whole session against a socket whose sends can be held
back, which is the only way to make "the client stopped reading" exact.
"""

from __future__ import annotations

import asyncio
import json
import threading
import time

import pytest

import server
from support import RELAY_LIMIT_ERROR, CallbackEngine, drain, pcm, unpack

# -- FrameQueue --------------------------------------------------------------


def test_queue_is_bounded_and_ordered():
    frames = server.FrameQueue(capacity=2)
    assert frames.put(b"\x01\x00") and frames.put(b"\x02\x00")
    assert not frames.put(b"\x03\x00")  # full
    assert frames.get() == b"\x01\x00"
    assert frames.put(b"\x04\x00")  # room again
    assert [frames.get(), frames.get()] == [b"\x02\x00", b"\x04\x00"]


def test_queue_delivers_what_is_queued_before_the_end():
    frames = server.FrameQueue(capacity=4)
    frames.put(b"ab")
    frames.end()
    assert frames.get() == b"ab"
    assert frames.get() is None and frames.get() is None
    assert not frames.cancelled


def test_cancel_wakes_a_waiting_reader_and_drops_the_audio():
    frames = server.FrameQueue(capacity=4)
    got = []
    reader = threading.Thread(target=lambda: got.append(frames.get()))
    reader.start()
    time.sleep(0.02)
    frames.cancel()
    reader.join(2)
    assert not reader.is_alive() and got == [None]
    assert frames.put(b"ab")  # ignored, and not reported as an overflow
    assert frames.get() is None and frames.cancelled


def test_end_wakes_a_waiting_reader():
    frames = server.FrameQueue(capacity=4)
    got = []
    reader = threading.Thread(target=lambda: got.append(frames.get()))
    reader.start()
    time.sleep(0.02)
    frames.end()
    reader.join(2)
    assert got == [None] and not frames.cancelled


# -- AudioWindow -------------------------------------------------------------


def read_all(frames: server.FrameQueue, limit: int):
    """Read through consecutive windows; return the samples each one got."""
    windows = []
    carry = b""
    while True:
        window = server.AudioWindow(frames, carry, limit_samples=limit)
        heard = []
        while True:
            part = window.read()
            if part is None:
                break
            heard.extend(unpack(part))
        windows.append(heard)
        assert window.samples == len(heard)
        carry = window.carry
        if window.ended:
            return windows


def test_one_frame_can_span_many_windows():
    frames = server.FrameQueue(capacity=4)
    frames.put(pcm(range(10)))
    frames.end()
    assert read_all(frames, 3) == [[0, 1, 2], [3, 4, 5], [6, 7, 8], [9]]


def test_a_frame_ending_on_the_boundary_leaves_no_carry():
    frames = server.FrameQueue(capacity=4)
    frames.put(pcm(range(4)))
    frames.put(pcm(range(4, 8)))
    frames.end()
    assert read_all(frames, 4) == [[0, 1, 2, 3], [4, 5, 6, 7], []]


def test_a_full_window_stops_reading_and_is_not_the_end():
    frames = server.FrameQueue(capacity=4)
    frames.put(pcm(range(6)))
    window = server.AudioWindow(frames, limit_samples=4)
    assert unpack(window.read()) == [0, 1, 2, 3]
    assert window.read() is None and window.read() is None
    assert window.samples == 4 and not window.ended
    assert unpack(window.carry) == [4, 5]


def test_a_cancelled_window_is_not_an_ended_one():
    frames = server.FrameQueue(capacity=4)
    frames.put(pcm(range(6)))
    window = server.AudioWindow(frames, carry=pcm([9]), limit_samples=4)
    frames.cancel()
    assert window.read() is None
    assert window.cancelled and not window.ended and window.samples == 0


def test_a_window_needs_room_for_a_sample():
    with pytest.raises(ValueError):
        server.AudioWindow(server.FrameQueue(1), limit_samples=0)


# -- commands ----------------------------------------------------------------


def test_command_parsing():
    assert server.is_command('{"type":"endStream"}', "endStream")
    assert server.is_command('{"type": "stop"}', "stop")
    assert not server.is_command('{"type":"stop"}', "endStream")
    assert not server.is_command('{"type":"endStream","a":1}', "endStream")
    assert not server.is_command(None, "endStream")
    assert not server.is_command(b'{"type":"endStream"}', "endStream")
    assert not server.is_command("[" * 100_000, "endStream")


# -- Outbox ------------------------------------------------------------------

OVERFLOW = {"type": "error", "message": "overflow"}


def run(coroutine):
    return asyncio.run(coroutine)


def test_outbox_keeps_order_and_marks_the_final_event():
    async def scenario():
        outbox = server.Outbox(asyncio.get_running_loop(), 8, OVERFLOW)
        assert outbox.offer({"n": 1}) and outbox.offer({"n": 2}) and outbox.offer({"n": 3}, final=True)
        assert not outbox.offer({"n": 4})  # nothing follows the final event
        return [await outbox.next() for _ in range(3)]

    assert run(scenario()) == [({"n": 1}, False), ({"n": 2}, False), ({"n": 3}, True)]


def test_outbox_overflow_replaces_everything_with_one_final_error():
    async def scenario():
        calls = []
        outbox = server.Outbox(asyncio.get_running_loop(), 3, OVERFLOW, lambda: calls.append(1))
        assert all(outbox.offer({"n": n}) for n in range(3))
        assert not outbox.offer({"n": 3})  # the fourth does not fit
        assert not outbox.offer({"n": 4})  # and nothing is accepted afterwards
        assert not outbox.fail({"type": "error", "message": "other"})
        return outbox.overflowed, calls, await outbox.next()

    assert run(scenario()) == (True, [1], (OVERFLOW, True))


def test_outbox_failure_jumps_the_queue_but_never_a_final_event():
    async def scenario():
        failure = {"type": "error", "message": "failed"}
        first = server.Outbox(asyncio.get_running_loop(), 8, OVERFLOW)
        first.offer({"n": 1})
        assert first.fail(failure)
        second = server.Outbox(asyncio.get_running_loop(), 8, OVERFLOW)
        second.offer({"n": 1}, final=True)
        assert not second.fail(failure)
        return await first.next(), await second.next()

    assert run(scenario()) == (({"type": "error", "message": "failed"}, True), ({"n": 1}, True))


def test_outbox_wakes_a_waiting_sender_from_another_thread():
    async def scenario():
        outbox = server.Outbox(asyncio.get_running_loop(), 8, OVERFLOW)
        threading.Timer(0.02, lambda: outbox.offer({"n": 1})).start()
        return await asyncio.wait_for(outbox.next(), 2)

    assert run(scenario()) == ({"n": 1}, False)


# -- a session against a socket that can stop accepting sends -------------------


class GatedSocket:
    """Lets the first ``free_sends`` frames through, then holds every send."""

    def __init__(self, free_sends: int) -> None:
        self.inbox: asyncio.Queue = asyncio.Queue()
        self.gate = asyncio.Event()
        self.free_sends = free_sends
        self.sent = []
        self.closed = False

    async def receive(self):
        return await self.inbox.get()

    async def send_text(self, text: str) -> None:
        if len(self.sent) >= self.free_sends:
            await self.gate.wait()
        self.sent.append(json.loads(text))

    async def close(self, code: int = 1000) -> None:
        self.closed = True


def chatty(audio, on_partial):
    for number in range(400):  # more than the 256 events the queue holds
        on_partial(f"word {number}")
    drain(audio)
    return ""


async def start_session(closing_seconds: float):
    slot = server.SessionSlot()
    assert slot.try_acquire()
    socket = GatedSocket(free_sends=1)  # "ready" leaves, then the client stalls
    rules = server.relay_rules(server.Limits(closing_seconds=closing_seconds))
    session = server.Session(socket, CallbackEngine(chatty), slot, rules, time.perf_counter)
    script = server.relay_script(server.Limits().relay_window_samples)
    task = asyncio.ensure_future(session.run(server.ready_event("nemotron"), script))
    while not session.outbox.overflowed:
        await asyncio.sleep(0.005)
    return slot, socket, session, task


def test_overflow_with_the_default_queue_size_when_the_client_stops_reading(capfd):
    async def scenario():
        slot, socket, session, task = await start_session(closing_seconds=0.05)
        await asyncio.wait_for(task, 5)  # bounded: the session does not linger
        return slot, socket

    slot, socket = run(scenario())
    assert socket.sent == [{"type": "ready", "model": "nemotron", "sampleRate": 16000}]
    deadline = time.monotonic() + 5
    while slot.busy and time.monotonic() < deadline:
        time.sleep(0.005)
    assert not slot.busy
    assert '"event":"outbound_overflow","route":"relay"' in capfd.readouterr().out


def test_overflow_error_is_the_next_frame_once_the_client_reads_again():
    async def scenario():
        slot, socket, session, task = await start_session(closing_seconds=5)
        socket.gate.set()  # the client catches up within the time allowed
        await asyncio.wait_for(task, 5)
        return socket

    socket = run(scenario())
    assert socket.sent == [
        {"type": "ready", "model": "nemotron", "sampleRate": 16000},
        {"type": "speechStart", "turnId": 1, "audioProcessedMs": 0},  # was already on its way
        RELAY_LIMIT_ERROR,
    ]
    assert socket.closed
