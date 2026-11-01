import dataclasses
import uuid

import pytest

from vpn_bot.config import Config, Panel, Probe, Server
from vpn_bot.store import Store


@pytest.fixture
def cfg():
    return Config(
        panels=(Panel("panel", "https://panel.example/private/", token_env="TEST_PANEL_TOKEN"),),
        servers=(Server("one", "Сервер 1", "panel", 25, "203.0.113.1"), Server("two", "Сервер 2", "panel", 26, "203.0.113.2")),
        probes=(Probe("control", "Контроль", "outside"), Probe("ru", "Россия", "ru", "TEST_PROBE_TOKEN")),
        database=":memory:", issue_cooldown=0,
    )


@pytest.fixture
def store(cfg):
    db = Store(":memory:")
    db.init_checks(cfg)
    yield db
    db.close()


def sample(server="one", *, ok=True, kind="proxy", latency=40):
    return {"server_id": server, "ok": ok, "kind": kind, "latency_ms": latency if ok else None, "error": "" if ok else "vpn_request_failed"}


def report(store, cfg, *, probe="ru", now=None, results=None, batch=None):
    return store.ingest(probe, batch or str(uuid.uuid4()), results or [sample()], cfg, (999,), now)


def add_reserve(store, server="one", count=1):
    for _ in range(count):
        client = {"id": str(uuid.uuid4()), "email": "reserve-" + uuid.uuid4().hex}
        store.add_link(server, client, f"vless://{client['id']}@203.0.113.1:443")


def allow(store, uid):
    store.register(uid, str(uid))
    store.execute("UPDATE users SET status='active' WHERE id=?", (uid,))
