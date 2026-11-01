import json
import uuid

import pytest

from vpn_bot.service import Service


async def test_seed_creates_exactly_15_reserves_and_is_idempotent(cfg, store, monkeypatch):
    clients = {25: [], 26: []}
    originals = {25: {"id": str(uuid.uuid4()), "email": "original", "enable": True, "expiryTime": 123, "totalGB": 100}, 26: {"id": str(uuid.uuid4()), "email": "original2", "enable": True}}
    clients[25].append(originals[25].copy())
    clients[26].append(originals[26].copy())

    class FakePanel:
        mode = "legacy"
        def __init__(self, panel): pass
        async def __aenter__(self): return self
        async def __aexit__(self, *args): pass
        async def inbound(self, iid):
            return {"id": iid, "port": 443, "enable": True, "protocol": "vless", "settings": json.dumps({"decryption": "none", "clients": clients[iid]}), "streamSettings": json.dumps({"network": "grpc", "security": "tls", "tlsSettings": {"serverName": "vpn.example.com"}, "grpcSettings": {"serviceName": "vpn"}})}
        async def clients(self, inbound): return clients[inbound["id"]]
        async def ensure_client(self, inbound, email, flow):
            rows = clients[inbound["id"]]
            existing = next((r for r in rows if r["email"] == email), None)
            if existing: return existing
            item = {"id": str(uuid.uuid4()), "email": email, "enable": True, "flow": flow, "expiryTime": 0, "totalGB": 0}
            rows.append(item)
            return item

    monkeypatch.setattr("vpn_bot.service.PanelClient", FakePanel)
    service = Service(cfg, store, (999,))
    assert await service.sync_panels(seed=True) == 15
    assert await service.sync_panels(seed=True) == 0
    assert len(store.rows("SELECT * FROM links")) == 15
    assert len(clients[25]) + len(clients[26]) == 19  # 15 reserves + 2 monitors + 2 original clients
    assert clients[25][0] == originals[25] and clients[26][0] == originals[26]
    for group in clients.values():
        for client in group[1:]:
            assert client["expiryTime"] == 0 and client["totalGB"] == 0
    # Disable one reserve externally: refresh retires it; refill uses a new slot.
    victim = next(c for c in clients[25] if "-r" in c["email"])
    victim["enable"] = False
    await service.sync_panels()
    assert store.rows("SELECT enabled FROM links WHERE email=?", (victim["email"],))[0]["enabled"] == 0
    assert await service.sync_panels(seed=True) == 1
    assert len(store.rows("SELECT * FROM links WHERE enabled=1 AND assigned_to IS NULL")) == 15


def test_status_labels_port_check_as_unverified(cfg, store):
    import time
    store.execute("UPDATE checks SET state='up',ok=1,kind='tcp',latency=3,last_seen=?", (time.time(),))
    text = Service(cfg, store, ()).status()
    assert "VPN не подтверждён" in text
