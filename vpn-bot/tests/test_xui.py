import base64
import copy
import dataclasses
import json
import os
import subprocess
import time
import uuid
from urllib.parse import parse_qs, urlparse

import httpx
import pytest
from cryptography.hazmat.primitives.asymmetric.x25519 import X25519PrivateKey
from cryptography.hazmat.primitives.serialization import Encoding, PrivateFormat, NoEncryption

from vpn_bot.config import ConfigError, Panel, Server
from vpn_bot.xui import PanelClient, PanelError, build_connection, client_enabled


@pytest.fixture
def inbound():
    key = X25519PrivateKey.generate().private_bytes(Encoding.Raw, PrivateFormat.Raw, NoEncryption())
    return {"id": 25, "port": 443, "protocol": "vless", "enable": True,
            "settings": json.dumps({"decryption": "none", "clients": []}),
            "streamSettings": json.dumps({"network": "grpc", "security": "reality", "grpcSettings": {"serviceName": "vpn-test", "multiMode": False}, "realitySettings": {"privateKey": base64.urlsafe_b64encode(key).decode().rstrip("="), "serverNames": ["www.example.com"], "shortIds": ["abcd"]}})}


@pytest.mark.parametrize("mode", ["legacy", "modern"])
async def test_3xui_api_creates_unlimited_clients_without_rewriting_inbound(monkeypatch, inbound, mode):
    monkeypatch.setenv("TEST_TOKEN", "dummy-token")
    calls = []

    def handler(request):
        assert request.headers["authorization"] == "Bearer dummy-token"
        calls.append(request.url.path)
        path = request.url.path
        if path.endswith("clients/list"):
            return httpx.Response(404) if mode == "legacy" else httpx.Response(200, json={"success": True, "obj": []})
        if path.endswith("inbounds/get/25"):
            return httpx.Response(200, json={"success": True, "obj": inbound})
        if path.endswith("clients/add"):
            body = json.loads(request.content)
            assert body["inboundIds"] == [25]
            client = body["client"]
        elif path.endswith("inbounds/addClient"):
            body = parse_qs(request.content.decode())
            assert body["id"] == ["25"]
            client = json.loads(body["settings"][0])["clients"][0]
        else:
            raise AssertionError(path)
        assert client["enable"] is True and client["expiryTime"] == client["totalGB"] == 0
        return httpx.Response(200, json={"success": True, "obj": None})

    panel = Panel("p", "https://panel.example/hidden/", token_env="TEST_TOKEN")
    async with PanelClient(panel, transport=httpx.MockTransport(handler)) as client:
        item = await client.inbound(25)
        new = await client.ensure_client(item, "vpn-watch-one-r0001", "")
        assert uuid.UUID(new["id"])
    assert not any("update" in path or "restart" in path or "del" in path for path in calls)
    assert all(path.startswith("/hidden/panel/api/") for path in calls)


async def test_modern_record_uses_uuid_not_numeric_row_id(monkeypatch, inbound):
    monkeypatch.setenv("TEST_TOKEN", "dummy-token")
    uid = str(uuid.uuid4())
    def handler(request):
        return httpx.Response(200, json={"success": True, "obj": [{"id": 42, "uuid": uid, "email": "vpn-test"}]})
    async with PanelClient(Panel("p", "https://panel.example/", token_env="TEST_TOKEN", api_mode="modern"), transport=httpx.MockTransport(handler)) as client:
        assert (await client.clients(inbound))[0]["id"] == uid


async def test_legacy_session_login(monkeypatch, inbound):
    monkeypatch.setenv("XUI_USER", "test-user")
    monkeypatch.setenv("XUI_PASSWORD", "test-password")
    def handler(request):
        if request.url.path.endswith("/login"):
            assert parse_qs(request.content.decode())["username"] == ["test-user"]
            return httpx.Response(200, json={"success": True}, headers={"set-cookie": "session=dummy; Path=/"})
        assert "session=dummy" in request.headers.get("cookie", "")
        return httpx.Response(200, json={"success": True, "obj": inbound})
    panel = Panel("p", "https://panel.example/base/", username_env="XUI_USER", password_env="XUI_PASSWORD", api_mode="legacy")
    async with PanelClient(panel, transport=httpx.MockTransport(handler)) as client:
        assert (await client.inbound(25))["id"] == 25


def test_grpc_reality_outbound_and_uri_do_not_expose_server_private_key(inbound):
    private = json.loads(inbound["streamSettings"])["realitySettings"]["privateKey"]
    uid = str(uuid.uuid4())
    server = Server("one", "Тестовый сервер", "p", 25, "203.0.113.1")
    outbound, uri = build_connection(server, inbound, {"id": uid, "email": "test"})
    assert private not in json.dumps(outbound) and private not in uri
    assert "privateKey" not in json.dumps(outbound)
    assert outbound["streamSettings"]["grpcSettings"]["serviceName"] == "vpn-test"
    query = parse_qs(urlparse(uri).query)
    assert query["type"] == ["grpc"] and query["serviceName"] == ["vpn-test"]
    assert "flow" not in query and query["pbk"]


@pytest.mark.parametrize("network,security", [("grpc", "reality"), ("tcp", "reality"), ("raw", "reality"), ("ws", "tls"), ("xhttp", "tls"), ("httpupgrade", "tls")])
def test_generated_profiles_accepted_by_real_xray(inbound, tmp_path, network, security):
    binary = os.getenv("XRAY_TEST_BINARY")
    if not binary:
        pytest.skip("Set XRAY_TEST_BINARY for real engine validation")
    stream = json.loads(inbound["streamSettings"])
    stream.update(network=network, security=security)
    inbound["streamSettings"] = json.dumps(stream)
    outbound, _ = build_connection(Server("one", "VPN", "p", 25, "203.0.113.1"), inbound, {"id": str(uuid.uuid4())})
    config = tmp_path / "xray.json"
    config.write_text(json.dumps({"outbounds": [outbound]}))
    result = subprocess.run([binary, "run", "-test", "-config", str(config)], capture_output=True, timeout=10)
    assert result.returncode == 0, result.stdout.decode() + result.stderr.decode()


def test_quota_expiry_and_disabled_flags():
    assert client_enabled({"enable": True, "expiryTime": 0, "totalGB": 0})
    assert not client_enabled({"enable": False})
    assert not client_enabled({"expiryTime": int(time.time() * 1000) - 1})
    assert not client_enabled({"totalGB": 10}, {"up": 9, "down": 1})


def test_unsupported_protocol_is_explicit(inbound):
    inbound["protocol"] = "vmess"
    with pytest.raises(ConfigError):
        build_connection(Server("one", "VPN", "p", 25, "203.0.113.1"), inbound, {"id": str(uuid.uuid4())})
