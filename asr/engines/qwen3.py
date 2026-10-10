"""Qwen3-ASR 1.7B through the ``qwen_asr`` package with its vLLM backend.

NOT VERIFIED ON A GPU IN THIS REMAKE. The calls into ``qwen_asr`` (how the
model is created, the engine limits, the streaming state and the three
streaming calls) are carried over call for call from the previous
implementation, which was verified on real hardware. Only the plumbing around
them is new. Change the third-party calls only together with a run on real
hardware.

Model facts: float16 weights, 1.0 s streaming chunks with two unfixed chunks
and five unfixed tokens, one sequence at a time, at most 55% of GPU memory,
no language pinned, four CPU threads, local files only (no network).

vLLM starts worker processes with the ``spawn`` method, which imports the
main module again. ``server.py`` therefore does nothing at import time.
"""

from __future__ import annotations

import time

import numpy as np

from ._pcm import require_cuda, require_weights, to_float32
from .base import AudioSource, Engine, PartialCallback, Recognition

CPU_THREADS = 4


class Qwen3Engine(Engine):
    name = "qwen3"
    chunk_ms = 1000

    def __init__(self, model_dir: str = "/models") -> None:
        self._model_dir = str(model_dir)
        self._model = None

    def load(self) -> None:
        started = time.perf_counter()
        require_weights(self._model_dir)
        import torch

        require_cuda(torch)
        torch.set_num_threads(CPU_THREADS)
        from qwen_asr import Qwen3ASRModel

        self._model = Qwen3ASRModel.LLM(
            model=self._model_dir,
            gpu_memory_utilization=0.55,
            max_model_len=4096,
            max_num_seqs=1,
            max_num_batched_tokens=1024,
            limit_mm_per_prompt={"audio": 1},
            enforce_eager=True,
            max_new_tokens=128,
            dtype="float16",
        )
        self.load_seconds = time.perf_counter() - started
        self.loaded = True

    def recognize(self, audio: AudioSource, on_partial: PartialCallback) -> Recognition:
        model = self._model
        state = model.init_streaming_state(chunk_size_sec=1.0, unfixed_chunk_num=2, unfixed_token_num=5)
        text = ""
        calls = []
        while (part := audio.read()) is not None:
            start = time.perf_counter()
            model.streaming_transcribe(to_float32(part), state)
            calls.append((time.perf_counter() - start) * 1000)
            if state.text != text:
                text = state.text
                on_partial(text)
        if audio.cancelled:
            return Recognition("")
        model.finish_streaming_transcribe(state)
        on_partial(state.text)
        return Recognition(
            state.text,
            {"processingCallP95Ms": float(np.percentile(calls, 95)) if calls else None},
        )
