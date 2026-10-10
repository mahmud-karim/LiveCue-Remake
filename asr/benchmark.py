"""Replay an audio file through the lab's ``/stream`` socket and report timings.

    python benchmark.py <audio> [--reference TEXT] [--output FILE] [--fast] [--port N]

The file is mixed down to mono, resampled to 16 kHz and sent in 80 ms frames
at speaking speed, so the timings are those a live speaker would see. With
``--fast`` the frames are sent as quickly as possible; that is a throughput
smoke test, not a latency measurement.

One JSON line is printed: the service's ``result`` frame plus
``realtimePlayback``, ``partialUpdates``, ``fixture`` (the file name) and,
with ``--reference``, ``normalizedWER``. ``--output`` appends the same line,
with every partial update, to a file of your choosing. That file then holds
transcript text: keep it somewhere private.

This is a manual tool. It needs ``requirements/benchmark.txt`` (plus numpy and
websockets), which both images already contain. The service never imports it.
Use a public or synthetic recording, never a private one.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import math
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

SAMPLE_RATE = 16_000
FRAME_BYTES = 2_560  # 80 ms of PCM16 mono
BYTES_PER_SECOND = SAMPLE_RATE * 2
MAX_SECONDS = 120  # the service ends a test at this length
DEFAULT_PORT = 47842
CONTAINER_PORT = 8765


class LabError(RuntimeError):
    """The lab refused or ended the test. The message is safe to show."""


def load_pcm16(path: str) -> bytes:
    """Decode a file to PCM16 LE mono 16 kHz."""
    import numpy as np
    import soundfile as sf

    pcm, rate = sf.read(path, dtype="float32", always_2d=True)
    pcm = pcm.mean(axis=1)
    if rate != SAMPLE_RATE:
        from scipy.signal import resample_poly

        divisor = math.gcd(rate, SAMPLE_RATE)
        pcm = resample_poly(pcm, SAMPLE_RATE // divisor, rate // divisor)
    return (np.clip(pcm, -1, 1) * 32767).astype("<i2").tobytes()


def normalized_wer(reference: str, hypothesis: str) -> float:
    """Word error rate after lower-casing and removing punctuation and extra spaces."""
    from jiwer import (
        Compose,
        ReduceToListOfListOfWords,
        RemoveMultipleSpaces,
        RemovePunctuation,
        Strip,
        ToLowerCase,
        wer,
    )

    normalize = Compose(
        [ToLowerCase(), RemovePunctuation(), RemoveMultipleSpaces(), Strip(), ReduceToListOfListOfWords()]
    )
    return wer(reference, hypothesis, reference_transform=normalize, hypothesis_transform=normalize)


def frames(pcm: bytes) -> List[bytes]:
    """Cut audio into 80 ms frames; the last one may be shorter."""
    return [pcm[start : start + FRAME_BYTES] for start in range(0, len(pcm), FRAME_BYTES)]


def ports(port: Optional[int], env: Optional[Dict[str, str]] = None) -> Tuple[int, int]:
    """Return ``(host_port, dial_port)``.

    The service checks the ``Host`` header against the lab's port on the PC.
    Inside the container the service is reached on another port (8765), so the
    two can differ: the URL carries the first, the TCP connection uses the second.
    """
    source = os.environ if env is None else env
    inside_container = "ASR_HOST_PORT" in source  # the launcher sets it for the container only
    host_port = port if port is not None else int(source.get("ASR_HOST_PORT", DEFAULT_PORT))
    dial_port = int(source.get("ASR_LISTEN_PORT", CONTAINER_PORT)) if inside_container else host_port
    return host_port, dial_port


async def replay(
    pcm: bytes, host_port: int, dial_port: Optional[int] = None, fast: bool = False
) -> Tuple[Dict[str, Any], List[Dict[str, Any]]]:
    """Run one test. Returns the ``result`` frame and the partial updates."""
    import websockets
    from websockets.exceptions import ConnectionClosed, InvalidStatus

    target = {"host": "127.0.0.1", "port": dial_port} if dial_port not in (None, host_port) else {}
    uri = f"ws://127.0.0.1:{host_port}/stream"
    try:
        return await _replay(websockets.connect(uri, max_size=1_000_000, proxy=None, **target), pcm, fast)
    except InvalidStatus as error:
        status = error.response.status_code
        raise LabError(f"The lab refused the connection (HTTP {status}). Check --port.") from None
    except ConnectionClosed:
        raise LabError("The lab closed the connection before sending a result.") from None


async def _replay(connection: Any, pcm: bytes, fast: bool) -> Tuple[Dict[str, Any], List[Dict[str, Any]]]:
    async with connection as ws:
        ready = json.loads(await ws.recv())
        if ready.get("type") != "ready":
            raise LabError(str(ready.get("message", "The lab did not become ready.")))
        started = time.perf_counter()

        async def send() -> None:
            sent = 0
            for frame in frames(pcm):
                sent += len(frame)
                if not fast:
                    # Audio cannot be sent before it would have been captured.
                    await asyncio.sleep(max(0.0, sent / BYTES_PER_SECOND - (time.perf_counter() - started)))
                await ws.send(frame)
            await ws.send(json.dumps({"type": "stop"}))

        sending = asyncio.ensure_future(send())
        updates: List[Dict[str, Any]] = []
        try:
            async for raw in ws:
                event = json.loads(raw)
                if event.get("type") == "error":
                    raise LabError(str(event.get("message", "The lab reported an error.")))
                if event.get("type") == "transcript":
                    updates.append({"elapsedMs": event["elapsedMs"], "text": event["text"]})
                if event.get("type") == "result":
                    await sending
                    return event, updates
        finally:
            sending.cancel()
    raise LabError("The lab closed the connection before sending a result.")


def run(args: argparse.Namespace) -> Dict[str, Any]:
    pcm = load_pcm16(args.audio)
    if len(pcm) > BYTES_PER_SECOND * MAX_SECONDS:
        raise LabError(f"The recording is longer than {MAX_SECONDS} seconds.")
    host_port, dial_port = ports(args.port)
    event, updates = asyncio.run(replay(pcm, host_port, dial_port, fast=args.fast))
    event["realtimePlayback"] = not args.fast
    event["partialUpdates"] = len(updates)
    event["fixture"] = Path(args.audio).name
    if args.reference:
        event["normalizedWER"] = normalized_wer(args.reference, event["text"])
    print(json.dumps(event), flush=True)
    if args.output:
        with open(args.output, "a", encoding="utf-8") as output:
            output.write(json.dumps({**event, "updates": updates}) + "\n")
    return event


def parse(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Replay an audio file through the LiveCue ASR lab.")
    parser.add_argument("audio", help="audio file; use a public or synthetic recording")
    parser.add_argument("--reference", help="the words spoken, to compute a normalized word error rate")
    parser.add_argument("--output", help="append the result and all partial updates to this file")
    parser.add_argument("--fast", action="store_true", help="unpaced throughput smoke test, NOT live latency")
    parser.add_argument("--port", type=int, help=f"the lab's port on this PC (default {DEFAULT_PORT})")
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = parse(argv)
    try:
        run(args)
    except LabError as error:
        print(f"benchmark: {error}", file=sys.stderr)
        return 1
    except OSError:
        print("benchmark: could not read the file or reach the lab. Is the model started?", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
