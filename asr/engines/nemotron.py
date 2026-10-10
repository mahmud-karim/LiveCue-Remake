"""Nemotron 3.5 ASR streaming 0.6B (RNN-T) through Hugging Face transformers.

NOT VERIFIED ON A GPU IN THIS REMAKE. The calls into torch and transformers
(how the processor and model are loaded and configured, how audio is cut into
feature chunks, how text is streamed out of ``generate``) are carried over
call for call from the previous implementation, which was verified on real
hardware. Only the plumbing around them is new. Change the third-party calls
only together with a run on real hardware.

Model facts: float32 weights, three look-ahead tokens (320 ms), language
pinned to ``en-US``, four CPU threads, local files only (no network).
"""

from __future__ import annotations

import time

import numpy as np

from ._pcm import require_cuda, require_weights, to_float32
from .base import AudioSource, Engine, PartialCallback, Recognition

SAMPLE_RATE = 16_000
LANGUAGE = "en-US"
LOOKAHEAD_TOKENS = 3  # 320 ms; a latency setting, not an end-to-end promise
CPU_THREADS = 4


def _caption_streamer_class(text_streamer_base):
    """Build the streamer subclass once transformers has been imported."""

    class CaptionStreamer(text_streamer_base):
        def __init__(self, tokenizer, on_partial):
            super().__init__(tokenizer, skip_special_tokens=True)
            self.text = ""
            self._on_partial = on_partial

        def on_finalized_text(self, text, stream_end=False):
            self.text += text
            if text:
                self._on_partial(self.text.strip())

    return CaptionStreamer


class NemotronEngine(Engine):
    name = "nemotron"
    chunk_ms = 320

    def __init__(self, model_dir: str = "/models") -> None:
        self._model_dir = str(model_dir)
        self._torch = None
        self._model = None
        self._processor = None
        self._streamer_class = None

    def load(self) -> None:
        started = time.perf_counter()
        require_weights(self._model_dir)
        import torch

        require_cuda(torch)
        torch.set_num_threads(CPU_THREADS)
        from transformers import AutoModelForRNNT, AutoProcessor, TextStreamer

        processor = AutoProcessor.from_pretrained(self._model_dir, local_files_only=True)
        processor.set_num_lookahead_tokens(LOOKAHEAD_TOKENS)
        model = (
            AutoModelForRNNT.from_pretrained(self._model_dir, local_files_only=True, dtype=torch.float32)
            .to("cuda")
            .eval()
        )
        self._torch = torch
        self._processor = processor
        self._model = model
        self._streamer_class = _caption_streamer_class(TextStreamer)
        self.load_seconds = time.perf_counter() - started
        self.loaded = True

    def recognize(self, audio: AudioSource, on_partial: PartialCallback) -> Recognition:
        torch = self._torch
        model = self._model
        processor = self._processor
        extractor = processor.feature_extractor

        # Keep only the short stretch of audio the feature extractor still
        # needs, never the whole recording.
        pcm = np.empty(0, dtype=np.float32)
        base = 0  # absolute sample index of pcm[0]
        exhausted = False

        def read_window(start, count):
            nonlocal pcm, base, exhausted
            while base + len(pcm) < start + count and not exhausted:
                part = audio.read()
                if part is None:
                    exhausted = True
                else:
                    pcm = np.concatenate((pcm, to_float32(part)))
            available_end = base + len(pcm)
            if audio.cancelled or start >= available_end:
                return None
            result = pcm[max(0, start - base) : start - base + count]
            if len(result) < count:
                result = np.pad(result, (0, count - len(result)))
            # Retain the overlap the next mel window needs.
            keep = max(base, start - extractor.n_fft)
            pcm = pcm[keep - base :]
            base = keep
            return result

        first = read_window(0, processor.num_samples_first_audio_chunk)
        if first is None:
            return Recognition("")
        inputs = processor(
            first,
            sampling_rate=SAMPLE_RATE,
            is_streaming=True,
            is_first_audio_chunk=True,
            language=LANGUAGE,
            return_tensors="pt",
        )
        inputs = inputs.to(model.device, dtype=model.dtype)

        def features():
            yield inputs.input_features[:, : processor.num_mel_frames_first_audio_chunk, :]
            index = processor.num_mel_frames_first_audio_chunk
            while not audio.cancelled:
                start = index * extractor.hop_length - extractor.n_fft // 2
                chunk = read_window(start, processor.num_samples_per_audio_chunk)
                if chunk is None:
                    break
                values = processor(
                    chunk,
                    sampling_rate=SAMPLE_RATE,
                    is_streaming=True,
                    is_first_audio_chunk=False,
                    language=LANGUAGE,
                    return_tensors="pt",
                )
                yield values.to(model.device, dtype=model.dtype).input_features
                index += processor.num_mel_frames_per_audio_chunk
                if exhausted:
                    break

        streamer = self._streamer_class(processor.tokenizer, on_partial)
        with torch.inference_mode():
            model.generate(**{**inputs, "input_features": features(), "streamer": streamer})
        return Recognition(streamer.text.strip())
