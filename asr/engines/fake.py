"""The test engine. No GPU, no model, no third-party code.

It reports the partial text ``test caption`` for every audio chunk it reads
and returns ``test caption`` at the end of the input. The protocol tests run
the whole HTTP and WebSocket surface against it.

It is selected only by tests (or by hand with ``ASR_ENGINE=fake`` when
developing a relay against the service). No image sets that variable.
"""

from __future__ import annotations

from .base import AudioSource, Engine, PartialCallback, Recognition

CAPTION = "test caption"


class FakeEngine(Engine):
    def __init__(self, name: str = "nemotron") -> None:
        from . import CHUNK_MS

        self.name = name
        self.chunk_ms = CHUNK_MS.get(name, 320)

    def recognize(self, audio: AudioSource, on_partial: PartialCallback) -> Recognition:
        while audio.read() is not None:
            on_partial(CAPTION)
        if audio.cancelled:
            return Recognition("")
        return Recognition(CAPTION)
