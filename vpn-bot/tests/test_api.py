import time
import uuid

import pytest
from aiohttp.test_utils import TestClient, TestServer

from vpn_bot.api import make_api
from vpn_bot.service import Service


@pytest.fixture
async def api(cfg, store, monkeypatch):
    monkeypatch.setenv("TEST_PROBE_TOKEN", "a" * 48)
    for sid in ("one", "two"):
        store.save_profile(sid, {"host": "203.0.113.1", "port": 443, "enabled": True, "outbound": {"protocol": "vless"}})
    client = TestClient(TestServer(make_api(Service(cfg, store, (999,)))))
    await client.start_server()
    yield client
    await client.close()


async def batch(api, *, ok=True):
    jobs = await (await api.get("/v1/probes/ru/jobs", headers={"Authorization": "Bearer " + "a" * 48})).json()
    return {"batch_id": str(uuid.uuid4()), "sent_at": time.time(), "results": [
        {"server_id": j["server_id"], "revision": j["revision"], "ok": ok, "kind": "proxy", "latency_ms": 50 if ok else None, "error": "" if ok else "vpn_request_failed"}
        for j in jobs["jobs"]]}


async def test_probe_needs_its_own_token(api):
    assert (await api.get("/v1/probes/ru/jobs")).status == 401
    assert (await api.get("/v1/probes/control/jobs", headers={"Authorization": "Bearer " + "a" * 48})).status == 401


async def test_replayed_reports_are_idempotent(api, store):
    data = await batch(api, ok=False)
    kwargs = {"json": data, "headers": {"Authorization": "Bearer " + "a" * 48}}
    assert await (await api.post("/v1/probes/ru/reports", **kwargs)).json() == {"accepted": True}
    assert await (await api.post("/v1/probes/ru/reports", **kwargs)).json() == {"accepted": False}
    assert store.rows("SELECT failures FROM checks WHERE probe_id='ru' AND server_id='one'")[0]["failures"] == 1


@pytest.mark.parametrize("bad", ["stale", "nan", "string_bool", "unknown_server", "old_revision", "partial", "duplicate"])
async def test_bad_reports_do_not_change_health(api, cfg, store, bad):
    data = await batch(api)
    if bad == "stale": data["sent_at"] -= 1000
    elif bad == "nan": data["results"][0]["latency_ms"] = float("nan")
    elif bad == "string_bool": data["results"][0]["ok"] = "true"
    elif bad == "unknown_server": data["results"][0]["server_id"] = "alien"
    elif bad == "old_revision": data["results"][0]["revision"] = "old"
    elif bad == "partial": data["results"].pop()
    elif bad == "duplicate": data["results"][1] = data["results"][0]
    response = await api.post("/v1/probes/ru/reports", json=data, headers={"Authorization": "Bearer " + "a" * 48})
    assert response.status == 400
    assert not store.healthy_servers(cfg)
