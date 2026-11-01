from __future__ import annotations

import base64
import copy
import json
import ssl
import time
import uuid
from urllib.parse import quote, urlencode

import httpx
from cryptography.hazmat.primitives.asymmetric.x25519 import X25519PrivateKey
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat

from .config import ConfigError, Panel, Server, secret


class PanelError(RuntimeError):
    """Only sanitized messages: panel responses may contain credentials."""


def object_value(value) -> dict:
    if isinstance(value, str):
        value = json.loads(value)
    if not isinstance(value, dict):
        raise PanelError("Некорректный JSON настроек inbound.")
    return value


def client_enabled(client: dict, stats: dict | None = None) -> bool:
    stats = stats or {}
    expiry = int(client.get("expiryTime", 0))
    total = int(client.get("totalGB", 0))
    return (
        bool(client.get("enable", True)) and bool(stats.get("enable", True))
        and (expiry == 0 or expiry > time.time() * 1000)
        and (total == 0 or int(stats.get("up", 0)) + int(stats.get("down", 0)) < total)
    )


class PanelClient:
    def __init__(self, panel: Panel, *, transport=None):
        self.panel = panel
        token = secret(panel.token_env, False)
        headers = {"Authorization": "Bearer " + token} if token else {}
        ca_file = secret(panel.ca_file_env, False)
        verify = ssl.create_default_context(cafile=ca_file) if ca_file else True
        self.client = httpx.AsyncClient(base_url=panel.base_url.rstrip("/") + "/", headers=headers, timeout=15, follow_redirects=False, transport=transport, verify=verify)
        self.token = bool(token)
        self.mode = panel.api_mode

    async def __aenter__(self):
        try:
            return await self._open()
        except BaseException:
            await self.client.aclose()
            raise

    async def _open(self):
        if not self.token:
            if not self.panel.username_env or not self.panel.password_env:
                await self.client.aclose()
                raise ConfigError(f"Задайте {self.panel.token_env or 'API-токен панели'} либо username_env/password_env для входа в 3x-ui.")
            response = await self.client.post("login", data={"username": secret(self.panel.username_env), "password": secret(self.panel.password_env)})
            try:
                valid = response.status_code == 200 and response.json().get("success") is True
            except (ValueError, AttributeError):
                valid = False
            if not valid:
                await self.client.aclose()
                raise PanelError("3x-ui: вход не выполнен. Проверьте URL, учётную запись или API-токен.")
        if self.mode == "auto":
            response = await self.client.get("panel/api/clients/list")
            if response.status_code == 404:
                self.mode = "legacy"
            elif response.status_code == 200:
                self._unwrap(response)
                self.mode = "modern"
            else:
                raise PanelError("3x-ui: не удалось определить API. Задайте api_mode явно.")
        return self

    async def __aexit__(self, *args):
        await self.client.aclose()

    @staticmethod
    def _unwrap(response: httpx.Response):
        if response.status_code != 200:
            raise PanelError(f"3x-ui: HTTP {response.status_code}.")
        try:
            data = response.json()
        except ValueError as exc:
            raise PanelError("3x-ui вернул HTML/неверный JSON. Проверьте base_url с webBasePath.") from exc
        if not isinstance(data, dict) or data.get("success") is not True:
            raise PanelError("3x-ui отклонил операцию; проверьте права и журнал панели.")
        return data.get("obj")

    async def request(self, method: str, path: str, **kwargs):
        try:
            # API-token authentication in 3.x bypasses session CSRF. Legacy 2.x uses its session cookie.
            return self._unwrap(await self.client.request(method, "panel/api/" + path, **kwargs))
        except httpx.HTTPError as exc:
            raise PanelError(f"3x-ui: ошибка соединения ({type(exc).__name__}).") from None

    async def inbound(self, inbound_id: int) -> dict:
        data = await self.request("GET", f"inbounds/get/{inbound_id}")
        if not isinstance(data, dict):
            raise PanelError("3x-ui: inbound не найден.")
        return data

    async def clients(self, inbound: dict) -> list[dict]:
        if self.mode == "legacy":
            return object_value(inbound["settings"]).get("clients", [])
        records = await self.request("GET", "clients/list")
        if not isinstance(records, list):
            raise PanelError("3x-ui: неверный список клиентов.")
        # 3.x records have a numeric database id and a separate Xray UUID.
        return [dict(row, id=row.get("uuid") or row.get("id")) for row in records]

    async def ensure_client(self, inbound: dict, email: str, flow: str) -> dict:
        clients = await self.clients(inbound)
        existing = next((row for row in clients if row.get("email") == email), None)
        if existing:
            if self.mode == "modern":
                detail = await self.request("GET", "clients/get/" + quote(email, safe=""))
                if inbound["id"] not in detail.get("inboundIds", []):
                    raise PanelError("Клиент с таким именем принадлежит другому inbound.")
            return existing
        client = {"id": str(uuid.uuid4()), "email": email, "flow": flow, "enable": True, "expiryTime": 0, "totalGB": 0, "limitIp": 0, "tgId": 0, "subId": uuid.uuid4().hex, "reset": 0}
        if self.mode == "modern":
            await self.request("POST", "clients/add", json={"client": client, "inboundIds": [inbound["id"]]})
        else:
            await self.request("POST", "inbounds/addClient", data={"id": inbound["id"], "settings": json.dumps({"clients": [client]})})
        return client


def build_connection(server: Server, inbound: dict, client: dict) -> tuple[dict, str]:
    """Build a client outbound and URI without copying server private keys/certificates."""
    if inbound.get("protocol") != "vless":
        raise ConfigError(f"{server.id}: автоматический выпуск поддерживает VLESS; протокол inbound отличается.")
    settings = object_value(inbound["settings"])
    if settings.get("decryption", "none") != "none":
        raise ConfigError(f"{server.id}: этот режим VLESS Encryption требует отдельного клиентского профиля.")
    original = object_value(inbound["streamSettings"])
    network = original.get("network", "tcp")
    if network not in {"tcp", "raw", "ws", "grpc", "httpupgrade", "xhttp"}:
        raise ConfigError(f"{server.id}: транспорт {network} пока не поддержан.")
    if server.flow and network not in {"tcp", "raw"}:
        raise ConfigError(f"{server.id}: flow Vision применим к TCP/raw, уберите flow для {network}.")
    stream = {"network": network, "security": original.get("security", "none")}
    query = {"encryption": "none", "type": network, "security": stream["security"]}
    if stream["security"] == "reality":
        reality = original.get("realitySettings", {})
        public = reality.get("settings", {}).get("publicKey") or reality.get("publicKey")
        if not public:
            private = reality.get("privateKey", "")
            try:
                key = X25519PrivateKey.from_private_bytes(base64.urlsafe_b64decode(private + "=" * (-len(private) % 4)))
                public = base64.urlsafe_b64encode(key.public_key().public_bytes(Encoding.Raw, PublicFormat.Raw)).decode().rstrip("=")
            except (ValueError, TypeError) as exc:
                raise ConfigError(f"{server.id}: не удалось получить публичный ключ Reality.") from exc
        names = reality.get("serverNames", [])
        name = server.sni or (names[0] if names else "")
        if not name:
            raise ConfigError(f"{server.id}: для Reality нужен SNI.")
        fingerprint = reality.get("settings", {}).get("fingerprint") or "chrome"
        short = (reality.get("shortIds") or [""])[0]
        # publicKey remains accepted by Xray 26.x; never send privateKey to agents.
        stream["realitySettings"] = {"serverName": name, "fingerprint": fingerprint, "publicKey": public, "shortId": short, "spiderX": "/"}
        query.update(sni=name, fp=fingerprint, pbk=public, sid=short, spx="/")
    elif stream["security"] == "tls":
        tls = original.get("tlsSettings", {})
        name = server.sni or tls.get("serverName") or server.host
        stream["tlsSettings"] = {"serverName": name, "fingerprint": "chrome", "allowInsecure": False}
        query.update(sni=name, fp="chrome")
        if tls.get("alpn"):
            stream["tlsSettings"]["alpn"] = tls["alpn"]
            query["alpn"] = ",".join(tls["alpn"])
    elif stream["security"] != "none":
        raise ConfigError(f"{server.id}: неизвестный режим безопасности.")
    if network in {"tcp", "raw"}:
        key = "tcpSettings" if network == "tcp" else "rawSettings"
        header = original.get(key, {}).get("header", {})
        if header.get("type", "none") != "none":
            raise ConfigError(f"{server.id}: TCP HTTP-header требует отдельного профиля; используйте обычный TCP/raw.")
    elif network == "grpc":
        source = original.get("grpcSettings", {})
        stream["grpcSettings"] = {k: copy.deepcopy(source[k]) for k in ("serviceName", "authority", "multiMode") if k in source}
        query.update(serviceName=source.get("serviceName", ""), mode="multi" if source.get("multiMode") else "gun")
        if source.get("authority"):
            query["authority"] = source["authority"]
    else:
        key = {"ws": "wsSettings", "httpupgrade": "httpupgradeSettings", "xhttp": "xhttpSettings"}[network]
        source = original.get(key, {})
        transport = {"path": source.get("path", "/")}
        host = source.get("host") or source.get("headers", {}).get("Host", "")
        if network == "ws" and host:
            transport["headers"] = {"Host": host}
        elif host:
            transport["host"] = host
        if network == "xhttp":
            transport["mode"] = source.get("mode", "auto")
            if source.get("extra"):
                raise ConfigError(f"{server.id}: XHTTP extra требует отдельного проверенного профиля.")
            query["mode"] = transport["mode"]
        stream[key] = transport
        query.update(path=transport["path"], host=host)
    flow = client.get("flow") or server.flow
    user = {"id": str(client["id"]), "encryption": "none"}
    if flow:
        user["flow"] = flow
        query["flow"] = flow
    port = int(inbound["port"])
    outbound = {"tag": "vpn", "protocol": "vless", "settings": {"vnext": [{"address": server.host, "port": port, "users": [user]}]}, "streamSettings": stream}
    host = f"[{server.host}]" if ":" in server.host else server.host
    uri = f"vless://{client['id']}@{host}:{port}?{urlencode(query)}#{quote(server.name)}"
    return outbound, uri
