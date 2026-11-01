# syntax=docker/dockerfile:1
FROM python:3.12-slim-bookworm
ARG TARGETARCH
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /app
COPY requirements.txt ./
RUN --mount=type=secret,id=proxy_ca,required=false \
    if [ -f /run/secrets/proxy_ca ]; then export PIP_CERT=/run/secrets/proxy_ca; fi; \
    pip install --no-cache-dir -r requirements.txt
COPY deploy/install_xray.py /tmp/install_xray.py
RUN --mount=type=secret,id=proxy_ca,required=false \
    if [ -f /run/secrets/proxy_ca ]; then export SSL_CERT_FILE=/run/secrets/proxy_ca; fi; \
    python /tmp/install_xray.py && rm /tmp/install_xray.py
COPY pyproject.toml ./
COPY src ./src
RUN --mount=type=secret,id=proxy_ca,required=false \
    if [ -f /run/secrets/proxy_ca ]; then export PIP_CERT=/run/secrets/proxy_ca; fi; \
    pip install --no-cache-dir setuptools==80.9.0 wheel==0.45.1 \
    && pip install --no-cache-dir --no-deps --no-build-isolation . \
    && groupadd --gid 10001 vpn-bot \
    && useradd --uid 10001 --gid 10001 --no-create-home vpn-bot \
    && mkdir /data && chown 10001:10001 /data
USER 10001:10001
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8080/healthz',timeout=2)"
ENTRYPOINT ["vpn-bot", "--config", "/app/config.toml"]
CMD ["run"]
