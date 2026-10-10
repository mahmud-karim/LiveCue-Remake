"""HTTP routes, configuration and engine selection."""

from __future__ import annotations

import json
import subprocess
import sys
import wave
from pathlib import Path

import pytest

import engines
import server
from engines.fake import FakeEngine
from support import HOST, HOST_PORT, ORIGIN, DigestEngine, Lab, receive, refused

ASR_ROOT = Path(server.__file__).resolve().parent

# -- /health -----------------------------------------------------------------


def test_health_for_each_model_identity():
    for model, chunk_ms in (("nemotron", 320), ("qwen3", 1000)):
        engine = FakeEngine(model)
        engine.load()
        health = Lab(engine).health()
        assert health["model"] == model and health["chunkMs"] == chunk_ms
        assert list(health) == [
            "model",
            "ready",
            "loadSeconds",
            "sampleRate",
            "maxSeconds",
            "chunkMs",
            "relayProtocol",
            "busy",
        ]


def test_health_reports_load_time_and_follows_the_session():
    lab = Lab(DigestEngine())  # the test engines report 1.5 s
    assert lab.health()["loadSeconds"] == 1.5
    assert lab.health()["busy"] is False
    with lab.connect("/stream") as ws:
        receive(ws)
        assert lab.health()["busy"] is True
        ws.send_text('{"type":"stop"}')
        receive(ws)
        assert lab.health()["busy"] is False


def test_health_is_not_cached():
    response = Lab().client.get("/health")
    assert response.status_code == 200
    assert response.headers["cache-control"] == "no-store"
    assert response.headers["content-type"] == "application/json"


def test_no_cors_headers_anywhere():
    lab = Lab()
    for origin in ("https://evil.example", ORIGIN):
        for path in ("/health", "/", "/sample", "/missing"):
            response = lab.client.get(path, headers={"origin": origin})
            assert not [name for name in response.headers if name.lower().startswith("access-control-")]
    # No preflight is ever answered with permission: refused for a foreign
    # origin, and not a method of the route for the lab's own.
    assert lab.client.options("/health", headers={"origin": "https://evil.example"}).status_code == 403
    assert lab.client.options("/health", headers={"origin": ORIGIN}).status_code == 405


# -- admission of HTTP requests ------------------------------------------------
#
# The lab's Host on every route; on HTTP also no foreign Origin and no
# cross-site fetch (protocol 11.1). The sockets' own rules are in
# test_relay.py and test_stream.py.

HTTP_ROUTES = ("/health", "/", "/sample")


@pytest.fixture
def lab_with_sample(tmp_path):
    clip = tmp_path / "sample.wav"
    clip.write_bytes(b"RIFF" + bytes(40))
    return Lab(sample_path=clip)


@pytest.mark.parametrize(
    "host",
    [
        "evil.example:47842",
        "127.0.0.1:8765",
        "127.0.0.1",
        "localhost",
        "127.0.0.1:47843",
        "LOCALHOST:47842",
        "[::1]:47842",
        "0.0.0.0:47842",
        "127.0.0.1:47842.evil.example",
        "",
    ],
)
def test_http_under_another_host_is_refused(lab_with_sample, host):
    for path in HTTP_ROUTES:
        response = lab_with_sample.client.get(path, headers={"host": host})
        assert response.status_code == 403, path
        assert response.content == b"" and response.headers["content-length"] == "0"
        assert response.headers["cache-control"] == "no-store"
        assert "content-type" not in response.headers


@pytest.mark.parametrize("host", [HOST, f"localhost:{HOST_PORT}"])
def test_http_under_both_loopback_names_is_served(lab_with_sample, host):
    for path in HTTP_ROUTES:
        assert lab_with_sample.client.get(path, headers={"host": host}).status_code == 200, path


def test_http_with_two_host_headers_is_refused(lab_with_sample):
    twice = [("host", HOST), ("host", "evil.example:47842")]
    assert lab_with_sample.client.get("/health", headers=twice).status_code == 403
    assert lab_with_sample.client.get("/health", headers=[("host", HOST), ("host", HOST)]).status_code == 403


def test_http_host_check_follows_the_configured_port():
    lab = Lab(host_port=50123)
    assert lab.client.get("/health", headers={"host": HOST}).status_code == 403
    assert lab.client.get("/health", headers={"host": "127.0.0.1:8765"}).status_code == 403
    assert lab.client.get("/health", headers={"host": "127.0.0.1:50123"}).status_code == 200
    assert lab.client.get("/health", headers={"origin": ORIGIN}).status_code == 403
    assert lab.client.get("/health", headers={"origin": "http://localhost:50123"}).status_code == 200


@pytest.mark.parametrize(
    "origin",
    [
        "https://evil.example",
        "http://127.0.0.1:8765",
        f"https://127.0.0.1:{HOST_PORT}",
        f"http://127.0.0.1:{HOST_PORT}/",
        f"http://127.0.0.1:{HOST_PORT}.evil.example",
        f"http://LOCALHOST:{HOST_PORT}",
        "http://127.0.0.1",
        "null",
        "",
    ],
)
def test_http_with_a_foreign_origin_is_refused(lab_with_sample, origin):
    for path in HTTP_ROUTES:
        response = lab_with_sample.client.get(path, headers={"origin": origin})
        assert response.status_code == 403 and response.content == b"", path


@pytest.mark.parametrize("origin", [ORIGIN, f"http://localhost:{HOST_PORT}"])
def test_http_with_the_labs_own_origin_is_served(lab_with_sample, origin):
    for path in HTTP_ROUTES:
        assert lab_with_sample.client.get(path, headers={"origin": origin}).status_code == 200, path
    twice = [("origin", origin), ("origin", origin)]
    assert lab_with_sample.client.get("/health", headers=twice).status_code == 403


def test_http_cross_site_fetch_is_refused_and_the_pages_own_fetch_is_served(lab_with_sample):
    client = lab_with_sample.client
    for path in HTTP_ROUTES:
        assert client.get(path, headers={"sec-fetch-site": "cross-site"}).status_code == 403, path
        # A good Origin does not excuse it, and neither does a second value.
        both = {"sec-fetch-site": "cross-site", "origin": ORIGIN}
        assert client.get(path, headers=both).status_code == 403, path
        twice = [("sec-fetch-site", "same-origin"), ("sec-fetch-site", "cross-site")]
        assert client.get(path, headers=twice).status_code == 403, path
        # same-origin: the lab page's own fetch. none: the address typed into the browser.
        for site in ("same-origin", "none", "same-site"):
            assert client.get(path, headers={"sec-fetch-site": site}).status_code == 200, (path, site)


def test_refusal_comes_before_routing_whatever_the_path_or_method():
    lab = Lab()
    evil = {"host": "evil.example:47842"}
    assert lab.client.get("/missing", headers=evil).status_code == 403
    assert lab.client.post("/health", headers=evil).status_code == 403
    assert lab.client.get("/relay", headers=evil).status_code == 403
    assert lab.client.get("/missing", headers={"origin": "https://evil.example"}).status_code == 403
    assert lab.client.post("/health", headers={"sec-fetch-site": "cross-site"}).status_code == 403
    # A socket that no route would take is refused for its Host, like the two that exist.
    assert refused(lab, "/missing", host="evil.example:47842") == 1008
    assert refused(lab, "/", host="evil.example:47842") == 1008
    # With the lab's own Host the same requests reach the router.
    assert lab.client.get("/missing").status_code == 404
    assert lab.client.post("/health").status_code == 405


def test_a_refused_request_runs_no_handler():
    """The health handler reads the engine; a refused request must not get that far."""
    reads = []

    class Watched(FakeEngine):
        @property
        def load_seconds(self):
            reads.append("health")
            return 0.0

        @load_seconds.setter
        def load_seconds(self, value):
            pass

    engine = Watched()
    engine.load()
    lab = Lab(engine)
    for headers in (
        {"host": "evil.example:47842"},
        {"origin": "https://evil.example"},
        {"sec-fetch-site": "cross-site"},
    ):
        assert lab.client.get("/health", headers=headers).status_code == 403
    assert reads == []
    assert lab.client.get("/health").status_code == 200
    assert reads == ["health"]


# -- / and /sample -----------------------------------------------------------


def test_page_is_served_as_html():
    response = Lab().client.get("/")
    assert response.status_code == 200
    assert response.headers["content-type"].startswith("text/html")
    assert "<title>" in response.text


def test_page_missing_is_a_plain_404(tmp_path):
    assert Lab(page_path=tmp_path / "nothing.html").client.get("/").status_code == 404


def test_sample_is_absent_until_the_owner_adds_one(tmp_path):
    assert server.Settings().sample_path == ASR_ROOT / "private" / "sample.wav"
    response = Lab(sample_path=tmp_path / "sample.wav").client.get("/sample")
    assert response.status_code == 404


def test_sample_is_one_fixed_file_with_no_way_to_name_another(tmp_path):
    clip = tmp_path / "sample.wav"
    with wave.open(str(clip), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(16000)
        out.writeframes(bytes(3200))
    (tmp_path / "other.wav").write_bytes(b"not this one")
    lab = Lab(sample_path=clip)

    response = lab.client.get("/sample")
    assert response.status_code == 200
    assert response.headers["content-type"] == "audio/wav"
    assert response.content == clip.read_bytes()

    assert lab.client.get("/sample?path=other.wav").content == clip.read_bytes()
    for path in ("/sample/other.wav", "/sample/..%2Fother.wav", "/other.wav", "/private/sample.wav"):
        assert lab.client.get(path).status_code == 404


def test_only_the_documented_routes_exist():
    lab = Lab()
    for path in ("/docs", "/redoc", "/openapi.json", "/server.py", "/static/index.html", "/relay", "/stream"):
        assert lab.client.get(path).status_code == 404
    assert lab.client.post("/health").status_code == 405


# -- configuration -----------------------------------------------------------


def test_defaults():
    settings = server.settings_from_env({})
    assert settings.model == "nemotron" and settings.engine == "model"
    assert settings.bind_host == "127.0.0.1"  # never a routable address by default
    assert settings.listen_port == 8765 and settings.host_port == 47842
    assert settings.model_dir == "/models"
    assert settings.allowed_hosts == {"127.0.0.1:47842", "localhost:47842"}
    assert settings.allowed_origins == {"http://127.0.0.1:47842", "http://localhost:47842"}
    assert settings.page_path == ASR_ROOT / "static" / "index.html"


def test_environment_is_read():
    settings = server.settings_from_env(
        {
            "ASR_MODEL": "qwen3",
            "ASR_ENGINE": "fake",
            "ASR_BIND_HOST": "0.0.0.0",
            "ASR_LISTEN_PORT": "9000",
            "ASR_HOST_PORT": "50123",
            "ASR_MODEL_DIR": "/somewhere",
        }
    )
    assert (settings.model, settings.engine, settings.bind_host) == ("qwen3", "fake", "0.0.0.0")
    assert (settings.listen_port, settings.host_port, settings.model_dir) == (9000, 50123, "/somewhere")
    assert settings.allowed_hosts == {"127.0.0.1:50123", "localhost:50123"}


@pytest.mark.parametrize(
    "env, reason",
    [
        ({"ASR_MODEL": "whisper"}, "unknown_model"),
        ({"ASR_MODEL": "../nemotron"}, "unknown_model"),
        ({"ASR_MODEL": ""}, "unknown_model"),
        ({"ASR_ENGINE": "cpu"}, "unknown_engine"),
        ({"ASR_HOST_PORT": "0"}, "invalid_asr_host_port"),
        ({"ASR_HOST_PORT": "65536"}, "invalid_asr_host_port"),
        ({"ASR_HOST_PORT": "http"}, "invalid_asr_host_port"),
        ({"ASR_LISTEN_PORT": "-1"}, "invalid_asr_listen_port"),
    ],
)
def test_bad_configuration_is_refused_with_a_fixed_reason(env, reason):
    with pytest.raises(server.ConfigurationError) as error:
        server.settings_from_env(env)
    assert error.value.reason == reason


def test_the_two_models_and_their_chunk_sizes():
    assert engines.MODELS == ("nemotron", "qwen3")
    assert engines.CHUNK_MS == {"nemotron": 320, "qwen3": 1000}
    assert server.RELAY_PROTOCOL == 1 and server.SAMPLE_RATE == 16000 and server.MAX_TEST_SECONDS == 120
    assert server.WS_MAX_MESSAGE_BYTES == 65536 and server.CONTAINER_PORT == 8765


def test_fake_engine_is_selected_by_configuration_and_keeps_the_model_name():
    engine = engines.create_engine("qwen3", "fake")
    assert isinstance(engine, FakeEngine)
    assert (engine.name, engine.chunk_ms, engine.loaded) == ("qwen3", 1000, False)
    engine.load()
    engine.warm_up()
    assert engine.loaded


def test_unknown_selections_have_no_engine():
    for model, kind in (("whisper", "model"), ("whisper", "fake"), ("nemotron", "cpu")):
        with pytest.raises(engines.EngineUnavailable):
            engines.create_engine(model, kind)


def test_real_engines_refuse_to_load_without_weights(tmp_path):
    """Checked before any model library is imported, so this runs anywhere."""
    for model in engines.MODELS:
        engine = engines.create_engine(model, "model", str(tmp_path))
        assert engine.name == model and engine.chunk_ms == engines.CHUNK_MS[model]
        with pytest.raises(engines.EngineUnavailable) as error:
            engine.load()
        assert error.value.reason == "weights_missing"
    assert "torch" not in sys.modules and "transformers" not in sys.modules and "vllm" not in sys.modules


# -- start-up ----------------------------------------------------------------


def test_startup_failures_are_one_status_line_and_exit_code_1(tmp_path, capfd):
    assert server.main({"ASR_MODEL": "whisper"}) == 1
    assert server.main({"ASR_MODEL": "qwen3", "ASR_MODEL_DIR": str(tmp_path)}) == 1
    lines = [json.loads(line) for line in capfd.readouterr().out.splitlines()]
    assert lines == [
        {"event": "startup_failed", "stage": "configure", "reason": "unknown_model"},
        {"event": "startup_failed", "stage": "load", "reason": "weights_missing"},
    ]


def test_importing_the_service_loads_no_model_library():
    """A fresh interpreter: the service, the engine registry and the fake engine."""
    script = (
        "import sys, json\n"
        "import server, engines, engines.fake\n"
        "app = server.create_app(engines.create_engine('nemotron', 'fake'))\n"
        "heavy = ['torch', 'torchaudio', 'transformers', 'vllm', 'qwen_asr', 'numpy', 'scipy', 'soundfile', 'jiwer']\n"
        "print(json.dumps([name for name in heavy if name in sys.modules]))\n"
    )
    done = subprocess.run(
        [sys.executable, "-c", script], cwd=str(ASR_ROOT), capture_output=True, text=True, timeout=120
    )
    assert done.returncode == 0, done.stderr
    assert json.loads(done.stdout.strip().splitlines()[-1]) == []
