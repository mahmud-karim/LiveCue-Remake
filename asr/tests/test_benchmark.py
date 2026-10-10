"""The benchmark tool, against the live fake-engine service.

Decoding and word error rate use soundfile, scipy and jiwer, which are not
test requirements. Those two functions are checked against stand-ins here and
against the real packages when they happen to be installed.
"""

from __future__ import annotations

import asyncio
import json
import sys
import time
import types
import wave

import numpy as np
import pytest

import benchmark
from support import STREAM_BUSY_ERROR

# -- pure parts --------------------------------------------------------------


def test_audio_is_cut_into_80_ms_frames():
    assert benchmark.FRAME_BYTES == 2560 and benchmark.SAMPLE_RATE == 16000
    audio = bytes(range(256)) * 25  # 6,400 bytes
    cut = benchmark.frames(audio)
    assert [len(frame) for frame in cut] == [2560, 2560, 1280]
    assert b"".join(cut) == audio
    assert benchmark.frames(b"") == []


def test_port_selection():
    assert benchmark.ports(None, {}) == (47842, 47842)
    assert benchmark.ports(50123, {}) == (50123, 50123)
    # Inside the container the launcher's variable is present: name the PC's
    # port in the URL, connect to the port the service listens on.
    assert benchmark.ports(None, {"ASR_HOST_PORT": "50123"}) == (50123, 8765)
    assert benchmark.ports(None, {"ASR_HOST_PORT": "50123", "ASR_LISTEN_PORT": "9000"}) == (50123, 9000)


def test_arguments():
    args = benchmark.parse(["clip.wav"])
    assert (args.audio, args.reference, args.output, args.fast, args.port) == ("clip.wav", None, None, False, None)
    args = benchmark.parse(["clip.wav", "--reference", "hello", "--output", "out.jsonl", "--fast", "--port", "5"])
    assert (args.reference, args.output, args.fast, args.port) == ("hello", "out.jsonl", True, 5)


# -- decoding, against stand-ins -------------------------------------------------


def fake_soundfile(monkeypatch, samples: np.ndarray, rate: int):
    calls = []
    module = types.ModuleType("soundfile")

    def read(path, dtype, always_2d):
        calls.append((path, dtype, always_2d))
        return samples, rate

    module.read = read
    monkeypatch.setitem(sys.modules, "soundfile", module)
    return calls


def test_decoding_mixes_to_mono_clips_and_scales(monkeypatch):
    stereo = np.array([[0.5, 0.5], [-1.0, -3.0], [2.0, 2.0], [0.25, -0.25]], dtype=np.float32)
    calls = fake_soundfile(monkeypatch, stereo, 16000)
    pcm = benchmark.load_pcm16("clip.flac")
    assert calls == [("clip.flac", "float32", True)]
    assert np.frombuffer(pcm, dtype="<i2").tolist() == [16383, -32767, 32767, 0]


def test_decoding_resamples_other_rates_to_16k(monkeypatch):
    fake_soundfile(monkeypatch, np.zeros((48, 1), dtype=np.float32), 48000)
    resampled = []
    signal = types.ModuleType("scipy.signal")

    def resample_poly(pcm, up, down):
        resampled.append((len(pcm), up, down))
        return np.full(len(pcm) * up // down, 0.5, dtype=np.float32)

    signal.resample_poly = resample_poly
    scipy = types.ModuleType("scipy")
    scipy.signal = signal
    monkeypatch.setitem(sys.modules, "scipy", scipy)
    monkeypatch.setitem(sys.modules, "scipy.signal", signal)

    pcm = benchmark.load_pcm16("clip.wav")
    assert resampled == [(48, 1, 3)]
    assert np.frombuffer(pcm, dtype="<i2").tolist() == [16383] * 16


def test_word_error_rate_is_normalized(monkeypatch):
    seen = {}
    jiwer = types.ModuleType("jiwer")
    for name in ("ToLowerCase", "RemovePunctuation", "RemoveMultipleSpaces", "Strip", "ReduceToListOfListOfWords"):
        setattr(jiwer, name, lambda name=name: name)
    jiwer.Compose = lambda steps: tuple(steps)

    def wer(reference, hypothesis, reference_transform, hypothesis_transform):
        seen.update(reference=reference, hypothesis=hypothesis, transforms=(reference_transform, hypothesis_transform))
        return 0.25

    jiwer.wer = wer
    monkeypatch.setitem(sys.modules, "jiwer", jiwer)

    assert benchmark.normalized_wer("Hello, world.", "hello word") == 0.25
    steps = ("ToLowerCase", "RemovePunctuation", "RemoveMultipleSpaces", "Strip", "ReduceToListOfListOfWords")
    assert seen == {"reference": "Hello, world.", "hypothesis": "hello word", "transforms": (steps, steps)}


# -- the same two functions with the real packages, when present -------------------


def test_decoding_with_the_real_packages(tmp_path):
    pytest.importorskip("soundfile")
    pytest.importorskip("scipy")
    clip = tmp_path / "tone.wav"
    tone = (np.sin(np.arange(8000) * 2 * np.pi * 440 / 8000) * 12000).astype("<i2")
    with wave.open(str(clip), "wb") as out:
        out.setnchannels(2)
        out.setsampwidth(2)
        out.setframerate(8000)
        out.writeframes(np.repeat(tone, 2).tobytes())
    pcm = np.frombuffer(benchmark.load_pcm16(str(clip)), dtype="<i2")
    assert len(pcm) == 16000  # one second, now at 16 kHz mono
    assert 11000 < int(np.abs(pcm[2000:14000]).max()) < 13000


def test_word_error_rate_with_the_real_package():
    pytest.importorskip("jiwer")
    assert benchmark.normalized_wer("Hello, world.", "hello   WORLD") == 0
    assert benchmark.normalized_wer("one two three four", "one two three") == 0.25


# -- replay against the running service ---------------------------------------------


def test_fast_replay_returns_the_result_and_the_partials(live_lab):
    audio = bytes(2560 * 5)  # 0.4 s
    result, updates = asyncio.run(benchmark.replay(audio, live_lab.host_port, fast=True))
    assert result["type"] == "result" and result["text"] == "test caption" and result["model"] == "nemotron"
    assert result["inputSeconds"] == 0.4 and result["audioMs"] == 400
    assert [update["text"] for update in updates] == ["test caption"]
    assert set(updates[0]) == {"elapsedMs", "text"}


def test_paced_replay_takes_as_long_as_the_audio(live_lab):
    audio = bytes(2560 * 6)  # 0.48 s
    started = time.perf_counter()
    result, _ = asyncio.run(benchmark.replay(audio, live_lab.host_port))
    assert time.perf_counter() - started >= 0.45
    assert result["elapsedMs"] >= 450 and result["finalizationMs"] >= 0


def test_replay_inside_the_container_names_one_port_and_dials_another(published_lab):
    audio = bytes(2560)
    result, _ = asyncio.run(
        benchmark.replay(audio, published_lab.host_port, dial_port=published_lab.listen_port, fast=True)
    )
    assert result["type"] == "result"
    with pytest.raises(benchmark.LabError, match="HTTP 403"):
        asyncio.run(benchmark.replay(audio, published_lab.listen_port, fast=True))


def test_replay_reports_the_labs_own_error(live_lab):
    from websockets.sync.client import connect

    with connect(f"ws://127.0.0.1:{live_lab.host_port}/stream") as holder:
        holder.recv(timeout=5)
        with pytest.raises(benchmark.LabError) as error:
            asyncio.run(benchmark.replay(bytes(2560), live_lab.host_port, fast=True))
    assert str(error.value) == STREAM_BUSY_ERROR["message"]


# -- the command ---------------------------------------------------------------------


def wait_free(lab):
    deadline = time.monotonic() + 5
    while lab.busy and time.monotonic() < deadline:
        time.sleep(0.01)


def test_command_prints_one_line_and_appends_the_log(live_lab, monkeypatch, tmp_path, capsys):
    wait_free(live_lab)
    monkeypatch.setattr(benchmark, "load_pcm16", lambda path: bytes(2560 * 3))
    monkeypatch.setattr(benchmark, "normalized_wer", lambda reference, text: {"ref": reference, "hyp": text})
    log = tmp_path / "runs.jsonl"
    arguments = ["some/folder/clip.wav", "--fast", "--port", str(live_lab.host_port), "--output", str(log)]

    assert benchmark.main(arguments + ["--reference", "Test caption."]) == 0
    assert benchmark.main(arguments) == 0

    printed = [json.loads(line) for line in capsys.readouterr().out.splitlines()]
    assert len(printed) == 2
    first = printed[0]
    assert first["type"] == "result" and first["text"] == "test caption"
    assert first["realtimePlayback"] is False and first["partialUpdates"] == 1 and first["fixture"] == "clip.wav"
    assert first["normalizedWER"] == {"ref": "Test caption.", "hyp": "test caption"}
    assert "normalizedWER" not in printed[1] and "updates" not in printed[1]

    logged = [json.loads(line) for line in log.read_text(encoding="utf-8").splitlines()]
    assert len(logged) == 2
    assert [update["text"] for update in logged[0]["updates"]] == ["test caption"]
    assert {key: value for key, value in logged[1].items() if key != "updates"} == printed[1]


def test_command_refuses_a_recording_over_two_minutes(monkeypatch, capsys):
    monkeypatch.setattr(benchmark, "load_pcm16", lambda path: bytes(32000 * 120 + 2))
    assert benchmark.main(["long.wav", "--fast"]) == 1
    captured = capsys.readouterr()
    assert captured.out == "" and "longer than 120 seconds" in captured.err


def test_command_explains_an_unreachable_lab(monkeypatch, capsys):
    from support import free_port

    monkeypatch.setattr(benchmark, "load_pcm16", lambda path: bytes(2560))
    assert benchmark.main(["clip.wav", "--fast", "--port", str(free_port())]) == 1
    assert "Is the model started?" in capsys.readouterr().err
