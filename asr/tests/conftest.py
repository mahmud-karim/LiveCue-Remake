"""Fixtures shared by the lab tests."""

from __future__ import annotations

import threading
import time
from contextlib import contextmanager
from dataclasses import dataclass
from typing import Iterator, Optional

import pytest

import server
from engines import Engine
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


@contextmanager
def serve(
    listen_port: int,
    host_port: int,
    engine: Optional[Engine] = None,
    limits: Optional[server.Limits] = None,
) -> Iterator[LiveLab]:
    """Run the real uvicorn server on a loopback port for the length of a ``with``."""
    if engine is None:
        engine = FakeEngine()
        engine.load()
    settings = server.Settings(
        model=engine.name,
        engine="fake",
        listen_port=listen_port,
        host_port=host_port,
        limits=limits or server.Limits(),
    )
    live = server.build_server(engine, settings)
    thread = threading.Thread(target=live.run, daemon=True)
    thread.start()
    deadline = time.monotonic() + 15
    while not live.started:
        if time.monotonic() > deadline:
            raise AssertionError("the test server did not start")
        time.sleep(0.01)
    try:
        yield LiveLab(listen_port, host_port, live.config.app)
    finally:
        live.should_exit = True
        thread.join(15)
        assert not thread.is_alive()


@pytest.fixture(scope="session")
def live_lab() -> Iterator[LiveLab]:
    """The service on an ephemeral loopback port, dialled directly."""
    port = free_port()
    with serve(port, port) as lab:
        yield lab


@pytest.fixture(scope="session")
def published_lab() -> Iterator[LiveLab]:
    """The service as it runs in its container: it listens on one port while
    clients know it by another (the one Docker publishes on the PC)."""
    with serve(free_port(), free_port()) as lab:
        yield lab


@pytest.fixture
def serve_lab():
    """The context manager itself, for tests that need their own engine or limits."""
    return serve
