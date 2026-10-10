# LiveCue ASR lab images: one per model, over a shared base.
#
# The images hold the dependencies only. The service code is NOT copied in:
# Start-ASR-Lab.ps1 bind-mounts this folder read-only at /app, so a code change
# needs no rebuild and the code that runs is always the code in this folder.
# The weights are bind-mounted read-only at /models. Nothing is downloaded at
# run time (HF_HUB_OFFLINE=1).
#
# Build with Build-ASR-Images.ps1, or by hand from this folder:
#   docker build --target nemotron -t livecue-remake-asr-nemotron:1.0 .
#   docker build --target qwen3    -t livecue-remake-asr-qwen3:1.0 .

FROM python:3.12-slim AS base
ENV PYTHONUNBUFFERED=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    HF_HUB_OFFLINE=1 \
    HF_HUB_DISABLE_TELEMETRY=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    ASR_BIND_HOST=0.0.0.0
# ASR_BIND_HOST: outside a container the service listens on 127.0.0.1. Inside
# one it must listen on all interfaces, because Docker forwards the published
# port (127.0.0.1 on the PC only) to the container's own address.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libsndfile1 libgomp1 \
 && rm -rf /var/lib/apt/lists/*
COPY requirements/ /tmp/requirements/
RUN pip install --no-cache-dir -r /tmp/requirements/torch.txt
RUN pip install --no-cache-dir -r /tmp/requirements/base.txt -r /tmp/requirements/benchmark.txt
WORKDIR /app
EXPOSE 8765

FROM base AS nemotron
RUN pip install --no-cache-dir -r /tmp/requirements/nemotron.txt
ENV ASR_MODEL=nemotron
CMD ["python", "server.py"]

FROM base AS qwen3
RUN pip install --no-cache-dir -r /tmp/requirements/qwen3.txt
RUN apt-get update \
 && apt-get install -y --no-install-recommends build-essential \
 && rm -rf /var/lib/apt/lists/*
ENV ASR_MODEL=qwen3 \
    VLLM_WORKER_MULTIPROC_METHOD=spawn
CMD ["python", "server.py"]
