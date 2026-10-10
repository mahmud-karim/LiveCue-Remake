"""Shared helpers for the real adapters. Imports numpy, so only they import it."""

from __future__ import annotations

import os

import numpy as np

from .base import EngineUnavailable


def to_float32(pcm16: bytes) -> "np.ndarray":
    """PCM16 little-endian bytes to float32 samples in [-1, 1)."""
    return np.frombuffer(pcm16, dtype="<i2").astype(np.float32) / 32768


def require_weights(model_dir: str) -> None:
    """Refuse early, with a clear reason, when the weights folder is empty."""
    if not os.path.isfile(os.path.join(model_dir, "config.json")):
        raise EngineUnavailable("weights_missing")


def require_cuda(torch) -> None:
    """The lab never falls back to a CPU run by accident."""
    if not torch.cuda.is_available():
        raise EngineUnavailable("cuda_unavailable")
