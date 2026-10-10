"""The files around the service: images, pins, scripts, the page, the weights list.

These are text checks. They cannot prove that an image builds or that Docker
accepts a command; they keep the files from drifting away from the
architecture (docs/architecture/03-system-architecture.md, section 5).
"""

from __future__ import annotations

import json
import re
from pathlib import Path

import server

ASR = Path(server.__file__).resolve().parent
REPO = ASR.parent


def text(name: str) -> str:
    return (ASR / name).read_text(encoding="utf-8")


def requirements(name: str) -> list:
    lines = [line.strip() for line in text(f"requirements/{name}").splitlines()]
    return [line for line in lines if line and not line.startswith("#")]


# -- pinned dependencies ---------------------------------------------------------


def test_pins_are_those_of_the_spec():
    assert requirements("base.txt") == [
        "fastapi==0.135.1",
        "uvicorn==0.41.0",
        "websockets==16.0",
        "starlette==1.7.0",
    ]
    assert requirements("torch.txt") == [
        "--index-url https://download.pytorch.org/whl/cu128",
        "torch==2.9.1",
        "torchaudio==2.9.1",
    ]
    assert requirements("benchmark.txt") == ["soundfile==0.13.1", "scipy==1.17.1", "jiwer==4.0.0"]
    assert requirements("nemotron.txt") == ["transformers==5.17.0", "accelerate==1.12.0", "librosa==0.11.0"]
    assert requirements("qwen3.txt") == ["qwen-asr[vllm]==0.0.6"]


def test_every_requirement_is_pinned_and_the_unused_ones_are_gone():
    for path in sorted((ASR / "requirements").glob("*.txt")):
        for line in requirements(path.name):
            if line.startswith(("-r ", "--index-url ")):
                continue
            assert re.fullmatch(r"[A-Za-z0-9_.\[\]-]+==[0-9][0-9A-Za-z.]*", line), f"{path.name}: {line}"
    everything = "\n".join(text(f"requirements/{p.name}") for p in (ASR / "requirements").glob("*.txt"))
    assert "python-multipart" not in everything
    assert "curl" not in text("Dockerfile").replace("--no-install-recommends", "")


def test_tests_need_no_model_library():
    needed = requirements("test.txt")
    assert needed[0] == "-r base.txt"
    assert {line.split("==")[0] for line in needed[1:]} == {"pytest", "httpx", "numpy"}


# -- images ------------------------------------------------------------------------


def dockerfile_stages() -> dict:
    stages: dict = {}
    current = None
    joined = re.sub(r"\\\n\s*", " ", text("Dockerfile"))
    for line in joined.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        match = re.fullmatch(r"FROM (\S+) AS (\S+)", line)
        if match:
            current = match.group(2)
            stages[current] = {"from": match.group(1), "lines": []}
        else:
            stages[current]["lines"].append(line)
    return stages


def test_dockerfile_has_a_shared_base_and_one_target_per_model():
    stages = dockerfile_stages()
    assert list(stages) == ["base", "nemotron", "qwen3"]
    # By digest, not by the movable tag alone (architecture 5.1).
    assert re.fullmatch(r"python:3\.12-slim@sha256:[0-9a-f]{64}", stages["base"]["from"])
    assert len(re.findall(r"(?m)^FROM ", text("Dockerfile"))) == 3  # no other image comes in
    assert stages["nemotron"]["from"] == stages["qwen3"]["from"] == "base"
    for model in ("nemotron", "qwen3"):
        assert stages[model]["lines"][-1] == 'CMD ["python", "server.py"]'
        assert f"-r /tmp/requirements/{model}.txt" in " ".join(stages[model]["lines"])


def test_dockerfile_environment():
    stages = dockerfile_stages()
    base = " ".join(line for line in stages["base"]["lines"] if line.startswith("ENV "))
    for setting in (
        "PYTHONUNBUFFERED=1",
        "PIP_DISABLE_PIP_VERSION_CHECK=1",
        "HF_HUB_OFFLINE=1",
        "HF_HUB_DISABLE_TELEMETRY=1",
        "ASR_BIND_HOST=0.0.0.0",
    ):
        assert setting in base
    nemotron = " ".join(line for line in stages["nemotron"]["lines"] if line.startswith("ENV "))
    qwen3 = " ".join(line for line in stages["qwen3"]["lines"] if line.startswith("ENV "))
    assert "ASR_MODEL=nemotron" in nemotron and "VLLM_WORKER_MULTIPROC_METHOD" not in nemotron
    assert "ASR_MODEL=qwen3" in qwen3 and "VLLM_WORKER_MULTIPROC_METHOD=spawn" in qwen3
    assert "ASR_ENGINE" not in text("Dockerfile")  # no image selects the fake engine


def test_dockerfile_copies_no_service_code():
    """The code is bind-mounted at run time; the image is dependencies only."""
    copies = [
        line for line in re.sub(r"\\\n\s*", " ", text("Dockerfile")).splitlines() if line.startswith(("COPY", "ADD"))
    ]
    assert copies == ["COPY requirements/ /tmp/requirements/"]
    ignore = [line.strip() for line in text(".dockerignore").splitlines() if line.strip() and not line.startswith("#")]
    assert ignore == ["*", "!requirements"]  # nothing else, above all not private/, reaches the engine


def test_dockerfile_system_packages():
    stages = dockerfile_stages()
    base = " ".join(stages["base"]["lines"])
    assert "libsndfile1 libgomp1" in base and "build-essential" not in base
    assert "build-essential" in " ".join(stages["qwen3"]["lines"])
    assert "build-essential" not in " ".join(stages["nemotron"]["lines"])


def test_private_folder_is_ignored_by_git():
    ignored = [line.strip() for line in text(".gitignore").splitlines()]
    assert "private/" in ignored


# -- scripts -----------------------------------------------------------------------

SCRIPTS = ["LabCommon.ps1", "Start-ASR-Lab.ps1", "Stop-ASR-Lab.ps1", "Build-ASR-Images.ps1", "Get-ASR-Weights.ps1"]


def test_scripts_are_plain_ascii_for_windows_powershell_5():
    for name in SCRIPTS + ["tests/Test-LabScripts.ps1"]:
        raw = (ASR / name).read_bytes()
        assert raw.isascii(), name
        assert not raw.startswith(b"\xef\xbb\xbf"), name


def test_scripts_carry_the_exact_messages_and_names():
    common = text("LabCommon.ps1")
    for message in (
        "Docker Desktop is required.",
        "Start Docker Desktop first, then run this launcher again.",
        "Model weights have not been downloaded.",
        "An ASR session is active. Stop it before switching models.",
        "Container name belongs to another workload; refusing to replace it.",
        "Container is not owned by LiveCue. Refusing to stop it.",
        "An ASR session is active. Stop it before unloading the model.",
    ):
        assert f"'{message}'" in common, message
    for fixed in (
        "$LabContainerName = 'livecue-remake-asr'",
        "$LabLabelName = 'com.livecueremake.component'",
        "$LabLabelValue = 'asr-lab'",
        "$LabImageTag = '1.0'",
        "$LabDefaultPort = 47842",
        '"livecue-remake-asr-${Model}:$LabImageTag"',
        "'--cap-drop', 'ALL'",
        "'--security-opt', 'no-new-privileges'",
        "'--pids-limit', '512'",
        "'--shm-size', '1g'",
        "'--gpus', 'all'",
        '"127.0.0.1:${Port}:$LabContainerPort"',
    ):
        assert fixed in common, fixed
    assert "-p', \"0.0.0.0" not in common and "--privileged" not in common and "--network" not in common


def test_launchers_take_the_documented_parameters():
    start = text("Start-ASR-Lab.ps1")
    assert "[ValidateSet('nemotron', 'qwen3')][string]$Model = 'nemotron'" in start
    assert "[int]$Port = 47842" in start and "[switch]$NoBrowser" in start and "[switch]$EnsureDocker" in start
    assert "[string]$LabFolder" in start and "-LabRoot (Resolve-LabFolder $LabFolder)" in start
    stop = text("Stop-ASR-Lab.ps1")
    assert "[int]$Port = 47842" in stop and "$Model" not in stop and "$LabFolder" not in stop
    for name in ("Build-ASR-Images.ps1", "Get-ASR-Weights.ps1"):
        assert "[switch]$PlanOnly" in text(name)
        assert "asks before doing anything" in text(name)


def test_relay_copies_of_the_launchers_load_only_what_lies_beside_them():
    """The relay runs its own copies of three scripts from its data folder
    (protocol 11.5): the lab folder is a parameter and supplies data only."""
    load = ". (Join-Path $PSScriptRoot 'LabCommon.ps1')"
    for name in ("Start-ASR-Lab.ps1", "Stop-ASR-Lab.ps1"):
        launcher = text(name)
        assert launcher.count(load) == 1, name
        assert len(re.findall(r"(?m)^\s*\. ", launcher)) == 1, name  # nothing else is dot-sourced
    # The shared script never works out where it is (the lab folder is handed
    # to it) and loads no other code.
    common = text("LabCommon.ps1")
    code = "\n".join(line for line in common.splitlines() if not line.lstrip().startswith("#"))
    assert "$PSScriptRoot" not in code and "$PSCommandPath" not in code and "$MyInvocation" not in code
    assert not re.search(r"(?m)^\s*\. ", code) and "Import-Module" not in code and "Invoke-Expression" not in code


# -- the weights list ----------------------------------------------------------------


def test_weights_list_matches_what_the_adapters_load():
    manifest = json.loads(text("weights.json"))
    assert set(manifest) == set(server.MODELS)
    assert manifest["nemotron"]["repository"] == "nvidia/nemotron-3.5-asr-streaming-0.6b"
    assert manifest["qwen3"]["repository"] == "Qwen/Qwen3-ASR-1.7B"
    for entry in manifest.values():
        assert re.fullmatch(r"[0-9a-f]{40}", entry["revision"])
        assert entry["license"] and entry["licenseUrl"].startswith("https://")
        paths = [item["path"] for item in entry["files"]]
        assert "config.json" in paths  # the start script and the adapters look for it
        assert any(path.endswith(".safetensors") for path in paths)
        assert not any(path.endswith((".bin", ".pt", ".pkl", ".nemo", ".gguf", ".py")) for path in paths)
        assert entry["totalBytes"] == sum(item["size"] for item in entry["files"])
        for item in entry["files"]:
            assert re.fullmatch(r"[A-Za-z0-9._-]+", item["path"])
            checksum = item.get("sha256") or item.get("gitSha1")
            assert re.fullmatch(r"[0-9a-f]{64}|[0-9a-f]{40}", checksum)
    assert 2.5e9 < manifest["nemotron"]["totalBytes"] < 2.6e9
    assert 4.6e9 < manifest["qwen3"]["totalBytes"] < 4.8e9


# -- the lab page --------------------------------------------------------------------


def test_page_is_self_contained():
    page = text("static/index.html")
    assert not re.search(r"""(?:src|href)\s*=\s*["'](?:https?:)?//""", page)
    assert "@import" not in page and "<iframe" not in page
    external = set(re.findall(r"https?://[^\s\"'<>)]+", page))
    assert external == set(), external
    assert "default-src 'none'" in page


def test_page_keeps_its_limits_and_has_no_hardware_label():
    page = text("static/index.html")
    for constant in (
        "const SAMPLE_RATE = 16000;",
        "const FRAME_SAMPLES = 1280;",
        "const AUTO_STOP_MS = 119000;",
        "const MAX_FILE_BYTES = 20 * 1024 * 1024;",
        "const MAX_FILE_SECONDS = 120;",
        "const MAX_QUEUED_BYTES = 320000;",
    ):
        assert constant in page
    assert '"/stream"' in page and '{type: "stop"}' in page and 'fetch("/sample"' in page
    assert "English test" in page
    assert "RTX" not in page and "3080" not in page
    assert "innerHTML" not in page  # text from the service is never parsed as markup


# -- nothing personal in the folder ----------------------------------------------------


def test_no_local_paths_or_addresses_in_committed_files():
    suspicious = re.compile(
        r"[A-Za-z]:\\Users\\|/home/[a-z]|\b(?:10|192\.168|100)\.\d{1,3}\.\d{1,3}\.\d{1,3}\b|\.ts\.net"
    )
    allowed = {"test_privacy.py", "test_packaging.py", "Test-LabScripts.ps1"}  # they contain made-up examples
    for path in ASR.rglob("*"):
        if not path.is_file() or path.name in allowed:
            continue
        if any(part in {".venv", "private", "__pycache__", ".pytest_cache", ".ruff_cache"} for part in path.parts):
            continue
        if path.suffix not in {".py", ".ps1", ".txt", ".md", ".html", ".json", ".ini", ".toml", ""}:
            continue
        found = suspicious.search(path.read_text(encoding="utf-8", errors="replace"))
        assert not found, f"{path.name}: {found.group(0)}"
