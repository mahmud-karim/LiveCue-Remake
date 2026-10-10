"""Fixtures shared by the lab tests."""

from __future__ import annotations

import threading
import time
from dataclasses import dataclass
from typing import Iterator

import pytest

import server
from engines.fake import FakeEngine
from support import free_port


@dataclass(frozen=True)
class LiveLab:
    """A running service. ``host_port`` is what clients must put in ``Host``."""

    listen_port: int
    host_port: int
    app: object

    @property
    def busy(self) -> bool:
        return self.app.state.slot.busy


def _serve(listen_port: int, host_port: int) -> Iterator[LiveLab]:
    engine = FakeEngine()
    engine.load()
    settings = server.Settings(model="nemotron", engine="fake", listen_port=listen_port, host_port=host_port)
    live = server.build_server(engine, settings)
    thread = threading.Thread(target=live.run, daemon=True)
    thread.start()
    deadline = time.monotonic() + 15
    while not live.started:
        if time.monotonic() > deadline:
            raise AssertionError("the test server did not start")
        time.sleep(0.01)
    yield LiveLab(listen_port, host_port, live.config.app)
    live.should_exit = True
    thread.join(15)
    assert not thread.is_alive()


@pytest.fixture(scope="session")
def live_lab() -> Iterator[LiveLab]:
    """The service on an ephemeral loopback port, dialled directly."""
    port = free_port()
    yield from _serve(port, port)


@pytest.fixture(scope="session")
def published_lab() -> Iterator[LiveLab]:
    """The service as it runs in its container: it listens on one port while
    clients know it by another (the one Docker publishes on the PC)."""
    yield from _serve(free_port(), free_port())
