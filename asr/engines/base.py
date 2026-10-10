"""The contract between the service and a speech recogniser.

This file is plain Python: the service and the fake engine import it on
machines that have no GPU and no model libraries.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Callable, List, Mapping, Optional, Protocol

#: Called with the cumulative text of the current recognition.
PartialCallback = Callable[[str], None]


class AudioSource(Protocol):
    """What a recogniser reads from."""

    def read(self) -> Optional[bytes]:
        """Block for the next non-empty chunk of PCM16 LE mono 16 kHz audio.

        Returns ``None`` when this recognition has no more audio: the input
        ended, the window is full, or the session was cancelled.
        """

    @property
    def cancelled(self) -> bool:
        """True when the session is gone and the result will be discarded."""


class EngineUnavailable(RuntimeError):
    """The engine cannot be used. ``reason`` is a fixed, loggable identifier."""

    def __init__(self, reason: str) -> None:
        super().__init__(reason)
        self.reason = reason


@dataclass(frozen=True)
class Recognition:
    """The outcome of one recognition."""

    text: str
    #: Optional numeric measurements. Only allow-listed names reach a client.
    metrics: Mapping[str, Optional[float]] = field(default_factory=dict)


class SilenceSource:
    """Two seconds of synthetic silence in 80 ms frames, for warming up."""

    cancelled = False

    def __init__(self, frames: int = 25, samples_per_frame: int = 1280) -> None:
        self._pending: List[bytes] = [bytes(samples_per_frame * 2)] * frames

    def read(self) -> Optional[bytes]:
        return self._pending.pop() if self._pending else None


class Engine(ABC):
    """One loaded speech model.

    ``recognize`` is blocking and runs on a worker thread, one call at a time.
    It reads audio until the source returns ``None``, reports cumulative text
    through ``on_partial`` and returns the final text. When the source reports
    ``cancelled`` it should return promptly; its result is then discarded.
    """

    #: Model identifier reported to clients: ``nemotron`` or ``qwen3``.
    name: str
    #: Latency step reported as ``chunkMs`` by ``/health``.
    chunk_ms: int
    #: Seconds spent in :meth:`load`.
    load_seconds: float = 0.0
    #: True once :meth:`load` has finished.
    loaded: bool = False

    def load(self) -> None:
        """Load the weights. Raises :class:`EngineUnavailable` or any library error."""
        self.loaded = True

    def warm_up(self) -> None:
        """Run one recognition over synthetic silence so first use is not slow."""
        self.recognize(SilenceSource(), lambda text: None)

    @abstractmethod
    def recognize(self, audio: AudioSource, on_partial: PartialCallback) -> Recognition:
        """Recognise one continuous piece of audio."""
