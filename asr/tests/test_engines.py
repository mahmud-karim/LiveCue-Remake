"""The fake engine, and the two real adapters against stand-in model libraries.

The real adapters cannot run here: there is no GPU and the libraries are not
installed. These tests replace ``torch``, ``transformers`` and ``qwen_asr``
with small stand-ins that record how they are called. That proves the plumbing
(which calls are made, with which arguments, in which order, on which audio);
it proves nothing about the real libraries or about recognition quality.
"""

from __future__ import annotations

import sys
import types
from typing import List, Optional

import numpy as np
import pytest

import engines
import server
from engines import EngineUnavailable
from engines.base import SilenceSource
from engines.fake import FakeEngine
from support import Lab, collect, limits, open_relay, ramp, receive

# -- sources -----------------------------------------------------------------


class ListSource:
    """Audio from a list of frames; can be cancelled after a number of reads."""

    def __init__(self, frames: List[bytes], cancel_after: Optional[int] = None) -> None:
        self._frames = list(frames)
        self._cancel_after = cancel_after
        self.reads = 0
        self.cancelled = False

    def read(self) -> Optional[bytes]:
        self.reads += 1
        if self._cancel_after is not None and self.reads > self._cancel_after:
            self.cancelled = True
            return None
        return self._frames.pop(0) if self._frames else None


def frames_of(audio: bytes, frame_bytes: int) -> List[bytes]:
    return [audio[start : start + frame_bytes] for start in range(0, len(audio), frame_bytes)]


def as_float(audio: bytes) -> np.ndarray:
    return np.frombuffer(audio, dtype="<i2").astype(np.float32) / 32768


# -- the fake engine -----------------------------------------------------------


def test_fake_engine_reports_the_caption_per_chunk_and_at_the_end():
    engine = FakeEngine()
    partials = []
    result = engine.recognize(ListSource([bytes(4)] * 3), partials.append)
    assert partials == ["test caption"] * 3
    assert result.text == "test caption" and dict(result.metrics) == {}
    assert engine.recognize(ListSource([]), partials.append).text == "test caption"


def test_fake_engine_result_is_discarded_shape_when_cancelled():
    assert FakeEngine().recognize(ListSource([bytes(4)] * 3, cancel_after=1), lambda text: None).text == ""


def test_warm_up_feeds_two_seconds_of_silence():
    source = SilenceSource()
    heard = []
    while True:
        part = source.read()
        if part is None:
            break
        heard.append(part)
    assert len(heard) == 25 and all(part == bytes(2560) for part in heard)  # 25 x 80 ms
    assert not source.cancelled


# -- stand-in torch ------------------------------------------------------------


class Journal(list):
    def names(self):
        return [entry[0] for entry in self]


def make_torch(journal: Journal, cuda: bool = True) -> types.ModuleType:
    torch = types.ModuleType("torch")
    torch.float32 = "torch.float32"

    class Cuda:
        @staticmethod
        def is_available():
            journal.append(("cuda.is_available",))
            return cuda

    class InferenceMode:
        def __enter__(self):
            journal.append(("inference_mode.enter",))

        def __exit__(self, *exc):
            journal.append(("inference_mode.exit",))
            return False

    torch.cuda = Cuda
    torch.set_num_threads = lambda count: journal.append(("set_num_threads", count))
    torch.inference_mode = InferenceMode
    return torch


# -- stand-in transformers -----------------------------------------------------

N_FFT, HOP = 512, 160
FIRST_SAMPLES, FIRST_MEL = 5600, 33
CHUNK_SAMPLES, CHUNK_MEL = 5632, 32
MEL_BINS = 4


class Batch(dict):
    """Looks like the processor's output: a mapping with attribute access."""

    def to(self, device, dtype=None):
        self.moved = (device, dtype)
        return self

    @property
    def input_features(self):
        return self["input_features"]


def make_transformers(journal: Journal) -> types.ModuleType:
    transformers = types.ModuleType("transformers")

    class Processor:
        feature_extractor = types.SimpleNamespace(n_fft=N_FFT, hop_length=HOP)
        tokenizer = object()
        num_samples_first_audio_chunk = FIRST_SAMPLES
        num_mel_frames_first_audio_chunk = FIRST_MEL
        num_samples_per_audio_chunk = CHUNK_SAMPLES
        num_mel_frames_per_audio_chunk = CHUNK_MEL

        def set_num_lookahead_tokens(self, count):
            journal.append(("set_num_lookahead_tokens", count))

        def __call__(self, audio, **kwargs):
            journal.append(("processor", np.array(audio, copy=True), kwargs))
            frames = FIRST_MEL + 5 if kwargs["is_first_audio_chunk"] else CHUNK_MEL
            features = np.full((1, frames, MEL_BINS), float(len(journal)), dtype=np.float32)
            return Batch(input_features=features, prompt_ids="language prompt")

    class AutoProcessor:
        @staticmethod
        def from_pretrained(path, **kwargs):
            journal.append(("AutoProcessor.from_pretrained", path, kwargs))
            return Processor()

    class Model:
        device = "cuda:0"
        dtype = "torch.float32"
        words = ["Hello", " there", "", " world"]

        def to(self, device):
            journal.append(("model.to", device))
            return self

        def eval(self):
            journal.append(("model.eval",))
            return self

        def generate(self, **kwargs):
            journal.append(("generate", sorted(kwargs), kwargs["prompt_ids"]))
            streamer = kwargs["streamer"]
            words = list(self.words)
            for features in kwargs["input_features"]:
                journal.append(("features", features.shape, float(features.flat[0])))
                streamer.on_finalized_text(words.pop(0) if words else " more")
            streamer.on_finalized_text("", stream_end=True)

    class AutoModelForRNNT:
        @staticmethod
        def from_pretrained(path, **kwargs):
            journal.append(("AutoModelForRNNT.from_pretrained", path, kwargs))
            return Model()

    class TextStreamer:
        def __init__(self, tokenizer, skip_prompt=False, **decode_kwargs):
            journal.append(("TextStreamer", tokenizer is Processor.tokenizer, skip_prompt, decode_kwargs))

        def on_finalized_text(self, text, stream_end=False):
            raise AssertionError("the adapter must override this")

    transformers.AutoProcessor = AutoProcessor
    transformers.AutoModelForRNNT = AutoModelForRNNT
    transformers.TextStreamer = TextStreamer
    return transformers


@pytest.fixture
def weights(tmp_path):
    (tmp_path / "config.json").write_text("{}", encoding="utf-8")
    return str(tmp_path)


@pytest.fixture
def nemotron(monkeypatch, weights):
    journal = Journal()
    monkeypatch.setitem(sys.modules, "torch", make_torch(journal))
    monkeypatch.setitem(sys.modules, "transformers", make_transformers(journal))
    engine = engines.create_engine("nemotron", "model", weights)
    engine.load()
    return engine, journal, weights


def expected_chunks(signal: np.ndarray):
    """The slices the feature extractor must see, computed from the whole signal."""

    def padded(part, count):
        return np.pad(part, (0, count - len(part))) if len(part) < count else part

    total = len(signal)
    if total == 0:
        return []
    chunks = [padded(signal[:FIRST_SAMPLES], FIRST_SAMPLES)]
    exhausted = total < FIRST_SAMPLES
    index = FIRST_MEL
    while True:
        start = index * HOP - N_FFT // 2
        exhausted = exhausted or total < start + CHUNK_SAMPLES
        if start >= total:
            break
        chunks.append(padded(signal[start : start + CHUNK_SAMPLES], CHUNK_SAMPLES))
        index += CHUNK_MEL
        if exhausted:
            break
    return chunks


# -- Nemotron ------------------------------------------------------------------


def test_nemotron_loads_exactly_as_specified(nemotron):
    engine, journal, weights = nemotron
    assert (engine.name, engine.chunk_ms, engine.loaded) == ("nemotron", 320, True)
    assert engine.load_seconds >= 0
    assert list(journal) == [
        ("cuda.is_available",),
        ("set_num_threads", 4),
        ("AutoProcessor.from_pretrained", weights, {"local_files_only": True}),
        ("set_num_lookahead_tokens", 3),
        ("AutoModelForRNNT.from_pretrained", weights, {"local_files_only": True, "dtype": "torch.float32"}),
        ("model.to", "cuda"),
        ("model.eval",),
    ]


def test_nemotron_refuses_to_run_without_cuda(monkeypatch, weights):
    journal = Journal()
    monkeypatch.setitem(sys.modules, "torch", make_torch(journal, cuda=False))
    monkeypatch.setitem(sys.modules, "transformers", make_transformers(journal))
    engine = engines.create_engine("nemotron", "model", weights)
    with pytest.raises(EngineUnavailable) as error:
        engine.load()
    assert error.value.reason == "cuda_unavailable"
    assert journal.names() == ["cuda.is_available"] and not engine.loaded


CUTS = [
    (samples, frame_bytes)
    for samples in (1, 800, 5599, 5600, 5601, 10000, 10496, 10497, 16000, 48000)
    for frame_bytes in (2, 2560, 16384, 1_000_000)
    if frame_bytes > 2 or samples <= 10497  # one-sample frames only around the boundaries
]


@pytest.mark.parametrize("samples, frame_bytes", CUTS)
def test_nemotron_cuts_audio_into_the_right_feature_chunks(nemotron, samples, frame_bytes):
    engine, journal, _ = nemotron
    del journal[:]
    audio = ramp(samples)
    partials = []
    result = engine.recognize(ListSource(frames_of(audio, frame_bytes)), partials.append)

    calls = [entry for entry in journal if entry[0] == "processor"]
    expected = expected_chunks(as_float(audio))
    assert len(calls) == len(expected)
    for number, (call, chunk) in enumerate(zip(calls, expected, strict=True)):
        assert call[1].dtype == np.float32
        np.testing.assert_array_equal(call[1], chunk)
        assert call[2] == {
            "sampling_rate": 16000,
            "is_streaming": True,
            "is_first_audio_chunk": number == 0,
            "language": "en-US",
            "return_tensors": "pt",
        }

    # generate() got the processor's other inputs, the feature stream and our streamer.
    assert ("generate", ["input_features", "prompt_ids", "streamer"], "language prompt") in journal
    assert ("TextStreamer", True, False, {"skip_special_tokens": True}) in journal
    shapes = [entry[1] for entry in journal if entry[0] == "features"]
    assert shapes == [(1, FIRST_MEL, MEL_BINS)] + [(1, CHUNK_MEL, MEL_BINS)] * (len(expected) - 1)
    order = journal.names()
    assert order.index("inference_mode.enter") < order.index("generate") < order.index("inference_mode.exit")

    spoken = (["Hello", " there", "", " world"] + [" more"] * 50)[: len(expected)]
    assert result.text == "".join(spoken).strip()
    cumulative, texts = "", []
    for word in spoken:
        cumulative += word
        if word:
            texts.append(cumulative.strip())
    assert partials == texts


def test_nemotron_with_no_audio_returns_empty_text_without_touching_the_model(nemotron):
    engine, journal, _ = nemotron
    del journal[:]
    assert engine.recognize(ListSource([]), lambda text: None).text == ""
    assert list(journal) == []


def test_nemotron_stops_when_the_session_is_cancelled(nemotron):
    engine, journal, _ = nemotron
    del journal[:]
    source = ListSource(frames_of(ramp(48000), 2560), cancel_after=6)
    engine.recognize(source, lambda text: None)
    assert source.cancelled
    assert len([entry for entry in journal if entry[0] == "processor"]) <= 3
    assert "inference_mode.exit" in journal.names() or "generate" not in journal.names()


def test_nemotron_warm_up_runs_one_recognition_on_silence(nemotron):
    engine, journal, _ = nemotron
    del journal[:]
    engine.warm_up()
    calls = [entry for entry in journal if entry[0] == "processor"]
    assert len(calls) == len(expected_chunks(np.zeros(32000, dtype=np.float32)))
    assert all(not call[1].any() for call in calls)
    assert journal.names().count("generate") == 1


def test_nemotron_through_the_relay_socket(nemotron):
    """The adapter behind the real service: windows, turns and final texts."""
    engine, _, _ = nemotron
    lab = Lab(engine, limits=limits(relay_window_samples=16000, relay_inbound_frames=10_000))
    with lab.connect("/relay") as ws:
        open_relay(ws)
        for frame in frames_of(ramp(24000), 2560):
            ws.send_bytes(frame)
        ws.send_text('{"type":"endStream"}')
        messages = collect(ws, "streamComplete", timeout=30)
    completes = [m for m in messages if m["type"] == "speechComplete"]
    assert [(m["turnId"], m["audioProcessedMs"]) for m in completes] == [(1, 1000), (2, 1500)]
    assert completes[0]["transcript"] == "Hello there world"
    assert completes[1]["transcript"] == "Hello there"
    assert messages[-1] == {**messages[-1], "type": "streamComplete", "audioProcessedMs": 1500}
    assert lab.health()["model"] == "nemotron" and lab.health()["chunkMs"] == 320


# -- stand-in qwen_asr -----------------------------------------------------------


def make_qwen_asr(journal: Journal) -> types.ModuleType:
    module = types.ModuleType("qwen_asr")

    class State:
        def __init__(self):
            self.text = ""
            self.calls = 0

    class Model:
        def init_streaming_state(self, **kwargs):
            journal.append(("init_streaming_state", kwargs))
            return State()

        def streaming_transcribe(self, pcm, state):
            journal.append(("streaming_transcribe", np.array(pcm, copy=True)))
            state.calls += 1
            if state.calls % 2 == 0:  # the text changes on every second call
                state.text = f"heard {state.calls} chunks"

        def finish_streaming_transcribe(self, state):
            journal.append(("finish_streaming_transcribe",))
            state.text = (state.text + " done").strip()

    class Qwen3ASRModel:
        @staticmethod
        def LLM(**kwargs):
            journal.append(("Qwen3ASRModel.LLM", kwargs))
            return Model()

    module.Qwen3ASRModel = Qwen3ASRModel
    return module


@pytest.fixture
def qwen3(monkeypatch, weights):
    journal = Journal()
    monkeypatch.setitem(sys.modules, "torch", make_torch(journal))
    monkeypatch.setitem(sys.modules, "qwen_asr", make_qwen_asr(journal))
    engine = engines.create_engine("qwen3", "model", weights)
    engine.load()
    return engine, journal, weights


# -- Qwen3 -----------------------------------------------------------------------


def test_qwen3_loads_exactly_as_specified(qwen3):
    engine, journal, weights = qwen3
    assert (engine.name, engine.chunk_ms, engine.loaded) == ("qwen3", 1000, True)
    assert list(journal) == [
        ("cuda.is_available",),
        ("set_num_threads", 4),
        (
            "Qwen3ASRModel.LLM",
            {
                "model": weights,
                "gpu_memory_utilization": 0.55,
                "max_model_len": 4096,
                "max_num_seqs": 1,
                "max_num_batched_tokens": 1024,
                "limit_mm_per_prompt": {"audio": 1},
                "enforce_eager": True,
                "max_new_tokens": 128,
                "dtype": "float16",
            },
        ),
    ]


def test_qwen3_refuses_to_run_without_cuda(monkeypatch, weights):
    journal = Journal()
    monkeypatch.setitem(sys.modules, "torch", make_torch(journal, cuda=False))
    monkeypatch.setitem(sys.modules, "qwen_asr", make_qwen_asr(journal))
    with pytest.raises(EngineUnavailable) as error:
        engines.create_engine("qwen3", "model", weights).load()
    assert error.value.reason == "cuda_unavailable" and journal.names() == ["cuda.is_available"]


def test_qwen3_feeds_every_frame_and_reports_changes(qwen3):
    engine, journal, _ = qwen3
    del journal[:]
    audio = ramp(5 * 1280)
    partials = []
    result = engine.recognize(ListSource(frames_of(audio, 2560)), partials.append)

    settings = {"chunk_size_sec": 1.0, "unfixed_chunk_num": 2, "unfixed_token_num": 5}
    assert journal[0] == ("init_streaming_state", settings)
    fed = [entry[1] for entry in journal if entry[0] == "streaming_transcribe"]
    assert len(fed) == 5 and all(part.dtype == np.float32 for part in fed)
    np.testing.assert_array_equal(np.concatenate(fed), as_float(audio))
    assert journal.names()[-1] == "finish_streaming_transcribe"
    assert journal.names().count("finish_streaming_transcribe") == 1

    assert partials == ["heard 2 chunks", "heard 4 chunks", "heard 4 chunks done"]
    assert result.text == "heard 4 chunks done"
    assert isinstance(result.metrics["processingCallP95Ms"], float) and result.metrics["processingCallP95Ms"] >= 0


def test_qwen3_with_no_audio_still_finishes_and_has_no_timing(qwen3):
    engine, journal, _ = qwen3
    del journal[:]
    result = engine.recognize(ListSource([]), lambda text: None)
    assert journal.names() == ["init_streaming_state", "finish_streaming_transcribe"]
    assert result.text == "done" and result.metrics == {"processingCallP95Ms": None}


def test_qwen3_does_not_finish_a_cancelled_session(qwen3):
    engine, journal, _ = qwen3
    del journal[:]
    result = engine.recognize(ListSource(frames_of(ramp(6400), 2560), cancel_after=2), lambda text: None)
    assert journal.names() == ["init_streaming_state", "streaming_transcribe", "streaming_transcribe"]
    assert result.text == ""


def test_qwen3_through_both_sockets(qwen3):
    engine, _, _ = qwen3
    lab = Lab(engine, limits=limits(relay_window_samples=6400, relay_inbound_frames=10_000))
    with lab.connect("/stream") as ws:
        assert receive(ws)["model"] == "qwen3"
        for frame in frames_of(ramp(5120), 2560):
            ws.send_bytes(frame)
        ws.send_text('{"type":"stop"}')
        result = collect(ws, "result")[-1]
    assert result["text"] == "heard 4 chunks done" and result["model"] == "qwen3"
    assert result["processingCallP95Ms"] >= 0 and result["inputSeconds"] == 0.32
    lab.wait_idle()

    with lab.connect("/relay") as ws:
        open_relay(ws, "qwen3")
        for frame in frames_of(ramp(9600), 2560):
            ws.send_bytes(frame)
        ws.send_text('{"type":"endStream"}')
        messages = collect(ws, "streamComplete")
    done = [m for m in messages if m["type"] == "speechComplete"]
    completes = [(m["turnId"], m["transcript"], m["audioProcessedMs"]) for m in done]
    # Window 1 holds 6,400 samples (frames of 1,280: 5 calls); window 2 the remaining 3,200.
    assert completes == [(1, "heard 4 chunks done", 400), (2, "heard 2 chunks done", 600)]
    assert all("processingCallP95Ms" not in m for m in messages)
    assert lab.health()["chunkMs"] == 1000


def test_selecting_a_real_engine_needs_no_model_library(tmp_path):
    """Creating the adapter is free; only ``load`` touches torch."""
    assert "torch" not in sys.modules and "transformers" not in sys.modules and "qwen_asr" not in sys.modules
    engine = engines.create_engine("nemotron", "model", str(tmp_path))
    assert not engine.loaded
    assert "torch" not in sys.modules
    assert server.Settings().model_dir == "/models"
