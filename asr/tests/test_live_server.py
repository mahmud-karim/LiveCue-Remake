"""The service behind a real uvicorn server on an ephemeral loopback port.

The in-memory tests cannot see what the HTTP server itself does with a
refused handshake or an over-sized message, and they do not run the real
entry point. These do. Still the fake engine: no GPU, no model.
"""

from __future__ import annotations

import base64
import json
import os
import socket
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

import pytest
from websockets.exceptions import ConnectionClosed, InvalidStatus
from websockets.sync.client import connect

import server
from engines.fake import FakeEngine
from support import RELAY_LIMIT_ERROR, free_port

ASR_ROOT = Path(server.__file__).resolve().parent


def get_json(port: int, path: str = "/health") -> dict:
    with urllib.request.urlopen(f"http://127.0.0.1:{port}{path}", timeout=5) as response:
        return json.loads(response.read())


def wait_until(condition, timeout: float = 10.0) -> None:
    deadline = time.monotonic() + timeout
    while not condition():
        if time.monotonic() > deadline:
            raise AssertionError("timed out")
        time.sleep(0.02)


def handshake_status(port: int, path: str, headers: dict) -> int:
    """Send a WebSocket upgrade request by hand and return the HTTP status."""
    lines = [f"GET {path} HTTP/1.1"]
    base = {
        "Upgrade": "websocket",
        "Connection": "Upgrade",
        "Sec-WebSocket-Key": base64.b64encode(os.urandom(16)).decode(),
        "Sec-WebSocket-Version": "13",
    }
    for name, value in {**base, **headers}.items():
        lines.append(f"{name}: {value}")
    with socket.create_connection(("127.0.0.1", port), timeout=5) as raw:
        raw.sendall(("\r\n".join(lines) + "\r\n\r\n").encode())
        reply = raw.recv(4096).decode("latin-1")
    return int(reply.split(" ", 2)[1])


@pytest.fixture
def port(live_lab):
    return live_lab.host_port


def test_server_options():
    engine = FakeEngine()
    config = server.build_server(engine, server.Settings()).config
    assert (config.host, config.port) == ("127.0.0.1", 8765)
    assert config.access_log is False
    assert config.ws_max_size == 65536
    assert config.log_config is None


def test_health_over_http(port):
    health = get_json(port)
    assert health["ready"] is True and health["relayProtocol"] == 1 and health["busy"] is False


def test_relay_session_over_a_real_socket(port):
    with connect(f"ws://127.0.0.1:{port}/relay") as ws:
        assert json.loads(ws.recv(timeout=5)) == {"type": "ready", "model": "nemotron", "sampleRate": 16000}
        assert json.loads(ws.recv(timeout=5))["type"] == "speechStart"
        for _ in range(3):
            ws.send(bytes(2560))
        assert json.loads(ws.recv(timeout=5))["transcript"] == "test caption"
        assert get_json(port)["busy"] is True
        ws.send('{"type":"endStream"}')
        assert json.loads(ws.recv(timeout=5))["type"] == "speechComplete"
        done = json.loads(ws.recv(timeout=5))
        assert done["type"] == "streamComplete" and done["audioProcessedMs"] == 240
        with pytest.raises(ConnectionClosed):
            ws.recv(timeout=5)
        assert ws.close_code == 1000
    assert get_json(port)["busy"] is False


def test_stream_session_from_the_lab_page_origin(port):
    with connect(f"ws://127.0.0.1:{port}/stream", origin=f"http://127.0.0.1:{port}") as ws:
        assert json.loads(ws.recv(timeout=5))["type"] == "ready"
        ws.send(bytes(2560))
        assert json.loads(ws.recv(timeout=5))["type"] == "transcript"
        ws.send('{"type":"stop"}')
        assert json.loads(ws.recv(timeout=5))["type"] == "result"


def test_localhost_name_is_admitted(port):
    assert handshake_status(port, "/relay", {"Host": f"localhost:{port}"}) == 101


def test_refused_handshakes_are_http_403(port):
    good_host = {"Host": f"127.0.0.1:{port}"}
    own_origin = {"Origin": f"http://127.0.0.1:{port}"}
    assert handshake_status(port, "/relay", {**good_host, **own_origin}) == 403
    assert handshake_status(port, "/relay", {"Host": f"evil.example:{port}"}) == 403
    assert handshake_status(port, "/relay", {"Host": "127.0.0.1:8765"}) == 403
    assert handshake_status(port, "/stream", {**good_host, "Origin": "https://evil.example"}) == 403
    assert handshake_status(port, "/stream", {"Host": f"evil.example:{port}", **own_origin}) == 403
    assert handshake_status(port, "/stream", {**good_host, **own_origin}) == 101
    with pytest.raises(InvalidStatus) as refused:
        connect(f"ws://127.0.0.1:{port}/relay", origin=f"http://127.0.0.1:{port}")
    assert refused.value.response.status_code == 403
    assert get_json(port)["busy"] is False


def test_unknown_socket_path_is_refused(port):
    assert handshake_status(port, "/v1/speech", {"Host": f"127.0.0.1:{port}"}) in (403, 404)


def test_a_message_over_65536_bytes_drops_the_connection(port):
    with connect(f"ws://127.0.0.1:{port}/relay", max_size=None) as ws:
        ws.recv(timeout=5), ws.recv(timeout=5)
        ws.send(bytes(65538))
        with pytest.raises(ConnectionClosed):
            while True:
                ws.recv(timeout=5)
        assert ws.close_code == 1009
    wait_until(lambda: get_json(port)["busy"] is False)


def test_the_largest_message_the_server_reads_is_judged_by_the_protocol(port):
    with connect(f"ws://127.0.0.1:{port}/relay", max_size=None) as ws:
        ws.recv(timeout=5), ws.recv(timeout=5)
        ws.send(bytes(65536))  # fits the server, far too big for a relay frame
        assert json.loads(ws.recv(timeout=5)) == RELAY_LIMIT_ERROR
        with pytest.raises(ConnectionClosed):
            ws.recv(timeout=5)
        assert ws.close_code == 1000
    wait_until(lambda: get_json(port)["busy"] is False)


def test_a_client_that_drops_the_tcp_connection_frees_the_model(port):
    ws = connect(f"ws://127.0.0.1:{port}/relay")
    ws.recv(timeout=5)
    assert get_json(port)["busy"] is True
    ws.socket.close()  # no closing handshake at all
    wait_until(lambda: get_json(port)["busy"] is False)


# -- behind a published port, as in the container ---------------------------------


def test_host_is_the_published_port_not_the_port_listened_on(published_lab):
    listening, published = published_lab.listen_port, published_lab.host_port
    assert listening != published
    # Reached directly, the service is addressed by a name it does not answer to.
    assert handshake_status(listening, "/relay", {"Host": f"127.0.0.1:{listening}"}) == 403
    assert handshake_status(listening, "/relay", {"Host": f"127.0.0.1:{published}"}) == 101
    assert handshake_status(listening, "/stream", {"Host": f"localhost:{published}"}) == 101
    origin = {"Origin": f"http://127.0.0.1:{published}"}
    assert handshake_status(listening, "/stream", {"Host": f"127.0.0.1:{published}", **origin}) == 101
    wrong = {"Origin": f"http://127.0.0.1:{listening}"}
    assert handshake_status(listening, "/stream", {"Host": f"127.0.0.1:{published}", **wrong}) == 403


def test_relay_session_through_a_published_port(published_lab):
    """What the relay does: it dials the published port and names it in Host."""
    link = socket.create_connection(("127.0.0.1", published_lab.listen_port), timeout=5)
    with connect(f"ws://127.0.0.1:{published_lab.host_port}/relay", sock=link) as ws:
        assert json.loads(ws.recv(timeout=5))["type"] == "ready"
        assert json.loads(ws.recv(timeout=5))["type"] == "speechStart"
        ws.send(bytes(2560))
        assert json.loads(ws.recv(timeout=5))["transcript"] == "test caption"
        ws.send('{"type":"endStream"}')
        assert json.loads(ws.recv(timeout=5))["type"] == "speechComplete"
        assert json.loads(ws.recv(timeout=5))["audioProcessedMs"] == 80


# -- the real entry point ------------------------------------------------------


def start_lab(extra_env: dict) -> subprocess.Popen:
    env = {**os.environ, "ASR_ENGINE": "fake", **extra_env}
    return subprocess.Popen(
        [sys.executable, "server.py"],
        cwd=str(ASR_ROOT),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )


def test_python_server_py_loads_warms_up_then_listens():
    number = free_port()
    lab = start_lab({"ASR_MODEL": "qwen3", "ASR_LISTEN_PORT": str(number), "ASR_HOST_PORT": str(number)})
    try:
        lines = [json.loads(lab.stdout.readline()) for _ in range(3)]
        assert lines == [
            {"event": "loaded", "model": "qwen3", "loadSeconds": 0.0},
            {"event": "warmup_complete", "model": "qwen3"},
            {"event": "listening", "model": "qwen3", "port": number, "hostPort": number},
        ]
        wait_until(lambda: _reachable(number))
        assert get_json(number)["model"] == "qwen3" and get_json(number)["chunkMs"] == 1000
        with connect(f"ws://127.0.0.1:{number}/relay") as ws:
            assert json.loads(ws.recv(timeout=5))["model"] == "qwen3"
            ws.send("not a command, and SECRET")
            assert json.loads(ws.recv(timeout=5))["type"] == "speechStart"
            assert json.loads(ws.recv(timeout=5)) == RELAY_LIMIT_ERROR
    finally:
        lab.terminate()
        out, err = lab.communicate(timeout=20)
    # After "listening" the process says nothing: no access log, no server chatter.
    assert err == "" and "SECRET" not in out
    assert [json.loads(line)["event"] for line in out.splitlines()] in ([], ["server_stopped"])


def _reachable(number: int) -> bool:
    try:
        get_json(number)
        return True
    except OSError:
        return False


def test_python_server_py_refuses_an_unknown_model():
    lab = start_lab({"ASR_MODEL": "whisper"})
    out, err = lab.communicate(timeout=30)
    assert lab.returncode == 1 and err == ""
    assert [json.loads(line) for line in out.splitlines()] == [
        {"event": "startup_failed", "stage": "configure", "reason": "unknown_model"}
    ]


def test_python_server_py_reports_a_port_it_cannot_bind():
    with socket.socket() as taken:
        taken.bind(("127.0.0.1", 0))
        taken.listen(1)
        number = taken.getsockname()[1]
        lab = start_lab({"ASR_LISTEN_PORT": str(number), "ASR_HOST_PORT": str(number)})
        out, err = lab.communicate(timeout=30)
    assert lab.returncode == 1 and err == ""
    assert json.loads(out.splitlines()[-1]) == {"event": "server_failed", "code": 1}
