"""Speech recognisers. The service picks exactly one from its configuration."""

from __future__ import annotations

from .base import AudioSource, Engine, EngineUnavailable, PartialCallback, Recognition

__all__ = [
    "AudioSource",
    "CHUNK_MS",
    "Engine",
    "EngineUnavailable",
    "MODELS",
    "PartialCallback",
    "Recognition",
    "create_engine",
]

#: The two fixed model identifiers. Nothing else can be selected.
MODELS = ("nemotron", "qwen3")
#: ``chunkMs`` reported by ``/health`` for each model.
CHUNK_MS = {"nemotron": 320, "qwen3": 1000}


def create_engine(model: str, kind: str = "model", model_dir: str = "/models") -> Engine:
    """Return the engine, not yet loaded, for a fixed model name.

    ``kind`` is ``model`` for the real adapter or ``fake`` for the test
    engine. The real adapters are imported only here and only on demand, so
    the service can be imported and tested without torch, transformers, vLLM
    or numpy.
    """
    if model not in MODELS:
        raise EngineUnavailable("unknown_model")
    if kind == "fake":
        from .fake import FakeEngine

        return FakeEngine(name=model)
    if kind != "model":
        raise EngineUnavailable("unknown_engine")
    if model == "nemotron":
        from .nemotron import NemotronEngine

        return NemotronEngine(model_dir)
    from .qwen3 import Qwen3Engine

    return Qwen3Engine(model_dir)
