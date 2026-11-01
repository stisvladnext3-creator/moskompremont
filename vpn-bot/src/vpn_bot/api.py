from __future__ import annotations

import hmac
import math
import time
import uuid

from aiohttp import web

from .config import secret
from .service import Service

ERRORS = {"", "tcp_unreachable", "xray_config_rejected", "unexpected_http_status", "xray_not_installed", "vpn_request_failed"}


def make_api(service: Service) -> web.Application:
    tokens = {}
    for probe in service.cfg.probes:
        if probe.id == service.cfg.local_probe_id:
            continue
        token = secret(probe.token_env)
        if len(token) < 32:
            raise ValueError(f"Для {probe.id} нужен токен минимум из 32 символов.")
        tokens[probe.id] = token
    if len(set(tokens.values())) != len(tokens):
        raise ValueError("Каждому удалённому узлу нужен отдельный уникальный токен.")

    def authorize(request: web.Request) -> str:
        pid = request.match_info["probe_id"]
        expected = tokens.get(pid)
        received = request.headers.get("Authorization", "")
        if not expected or not hmac.compare_digest(received, "Bearer " + expected):
            raise web.HTTPUnauthorized()
        return pid

    async def health(_):
        return web.json_response({"ok": True})

    async def jobs(request):
        authorize(request)
        return web.json_response({"jobs": service.jobs(), "interval": service.cfg.interval, "timeout": service.cfg.timeout}, headers={"Cache-Control": "no-store"})

    async def report(request):
        pid = authorize(request)
        try:
            data = await request.json()
            batch = str(uuid.UUID(data["batch_id"]))
            sent_at = float(data["sent_at"])
            if not math.isfinite(sent_at) or abs(time.time() - sent_at) > 120:
                raise ValueError("timestamp")
            results = data["results"]
            if not isinstance(results, list) or not 1 <= len(results) <= len(service.cfg.servers):
                raise ValueError("count")
            known = {s.id for s in service.cfg.servers}
            seen = set()
            expected_jobs = {job["server_id"]: job for job in service.jobs()}
            for result in results:
                sid = result["server_id"]
                if sid not in known or sid in seen or sid not in expected_jobs:
                    raise ValueError("server")
                seen.add(sid)
                if result.get("revision") != expected_jobs[sid]["revision"]:
                    raise ValueError("outdated_profile")
                kind = result["kind"]
                if kind not in {"tcp", "proxy"} or type(result["ok"]) is not bool:
                    raise ValueError("kind/ok")
                if kind == "proxy" and not expected_jobs[sid].get("outbound"):
                    raise ValueError("unconfigured_proxy")
                if result.get("error", "") not in ERRORS:
                    raise ValueError("error")
                latency = result["latency_ms"]
                if result["ok"]:
                    if type(latency) not in {int, float} or not math.isfinite(latency) or not 0 <= latency <= 600000:
                        raise ValueError("latency")
                elif latency is not None:
                    raise ValueError("failed_latency")
            if seen != set(expected_jobs):
                raise ValueError("incomplete_batch")
        except (ValueError, KeyError, TypeError, AttributeError, OverflowError):
            raise web.HTTPBadRequest(text="Invalid probe report") from None
        accepted = service.store.ingest(pid, batch, results, service.cfg, service.admins)
        return web.json_response({"accepted": accepted})

    app = web.Application(client_max_size=128 * 1024)
    app.router.add_get("/healthz", health)
    app.router.add_get("/v1/probes/{probe_id}/jobs", jobs)
    app.router.add_post("/v1/probes/{probe_id}/reports", report)
    return app
