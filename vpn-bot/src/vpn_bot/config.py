from __future__ import annotations

import os
import re
import tomllib
import math
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import urlparse


class ConfigError(ValueError):
    pass


def secret(name: str, required: bool = True) -> str:
    value = os.environ.get(name, "") if name else ""
    if required and not value:
        raise ConfigError(f"Задайте переменную окружения {name or '(не указана)' }.")
    return value


@dataclass(frozen=True)
class Panel:
    id: str
    base_url: str
    token_env: str = ""
    username_env: str = ""
    password_env: str = ""
    api_mode: str = "auto"
    ca_file_env: str = ""


@dataclass(frozen=True)
class Server:
    id: str
    name: str
    panel: str
    inbound_id: int
    host: str
    flow: str = ""
    sni: str = ""
    check_url: str = "https://www.google.com/generate_204"
    expected_status: int = 204


@dataclass(frozen=True)
class Probe:
    id: str
    name: str
    region: str
    token_env: str = ""


@dataclass(frozen=True)
class Config:
    panels: tuple[Panel, ...]
    servers: tuple[Server, ...]
    probes: tuple[Probe, ...]
    database: str = "data/bot.db"
    local_probe_id: str = "control"
    interval: int = 30
    timeout: int = 8
    stale_after: int = 120
    failures: int = 2
    recoveries: int = 2
    slow_ms: int = 1500
    pool_target: int = 15
    min_ru_successes: int = 1
    issue_cooldown: int = 600
    automatic_links: bool = True
    panel_refresh: int = 300
    listen_host: str = "127.0.0.1"
    listen_port: int = 8080
    xray_binary: str = "xray"
    concurrency: int = 4
    controller_url: str = ""
    controller_ca_env: str = ""
    bot_token_env: str = "TELEGRAM_BOT_TOKEN"
    admin_ids_env: str = "TELEGRAM_ADMIN_IDS"

    @classmethod
    def load(cls, path: str | Path) -> Config:
        with open(path, "rb") as stream:
            raw = tomllib.load(stream)
        allowed = set(cls.__dataclass_fields__) - {"panels", "servers", "probes"}
        options = raw.get("settings", {})
        unknown = set(options) - allowed
        if unknown:
            raise ConfigError("Неизвестные параметры: " + ", ".join(sorted(unknown)))
        cfg = cls(
            panels=tuple(Panel(**p) for p in raw.get("panels", [])),
            servers=tuple(Server(**s) for s in raw.get("servers", [])),
            probes=tuple(Probe(**p) for p in raw.get("probes", [])),
            **options,
        )
        cfg.validate()
        return cfg

    def validate(self) -> None:
        for collection in (self.panels, self.servers, self.probes):
            ids = [item.id for item in collection]
            if len(set(ids)) != len(ids) or any(not re.fullmatch(r"[a-z0-9_-]{1,48}", x) for x in ids):
                raise ConfigError("Идентификаторы должны быть уникальными: a-z, 0-9, _, -, до 48 символов.")
        if not self.servers or not self.probes:
            raise ConfigError("Нужны хотя бы один сервер и один проверяющий узел.")
        if self.local_probe_id and self.local_probe_id not in {p.id for p in self.probes}:
            raise ConfigError("local_probe_id отсутствует в probes.")
        if not 10 <= self.interval <= 3600 or not 1 <= self.timeout <= 60:
            raise ConfigError("interval: 10–3600 с, timeout: 1–60 с.")
        if not 1 <= self.pool_target <= 100 or not 1 <= self.concurrency <= 16:
            raise ConfigError("pool_target: 1–100, concurrency: 1–16.")
        maximum_cycle = math.ceil(len(self.servers) / self.concurrency) * self.timeout
        if self.stale_after < 2 * max(self.interval, maximum_cycle) + self.timeout:
            raise ConfigError("Увеличьте stale_after: он должен превышать два полных цикла проверки и тайм-аут.")
        if min(self.failures, self.recoveries, self.min_ru_successes, self.slow_ms, self.panel_refresh) < 1:
            raise ConfigError("Пороги и интервалы должны быть положительными.")
        if self.min_ru_successes > sum(p.region.lower() == "ru" for p in self.probes):
            raise ConfigError("Недостаточно проверяющих узлов с region = ru.")
        for panel in self.panels:
            if urlparse(panel.base_url).scheme not in {"http", "https"} or panel.api_mode not in {"auto", "legacy", "modern"}:
                raise ConfigError(f"Проверьте URL и api_mode панели {panel.id}.")
        for probe in self.probes:
            if probe.id != self.local_probe_id and not probe.token_env:
                raise ConfigError(f"Для узла {probe.id} нужен token_env.")
        for server in self.servers:
            if server.panel not in {p.id for p in self.panels} or server.inbound_id <= 0:
                raise ConfigError(f"Проверьте panel/inbound_id сервера {server.id}.")
            if not server.host or any(x in server.host for x in "/@?#\n\r "):
                raise ConfigError(f"Укажите публичный host без схемы и порта для {server.id}.")
            if urlparse(server.check_url).scheme != "https" or not 200 <= server.expected_status < 400:
                raise ConfigError(f"Для {server.id} нужен HTTPS check_url и ожидаемый HTTP-код.")

    def admins(self) -> tuple[int, ...]:
        try:
            ids = tuple(int(x.strip()) for x in secret(self.admin_ids_env).split(",") if x.strip())
        except ValueError as exc:
            raise ConfigError("TELEGRAM_ADMIN_IDS: числовые ID через запятую.") from exc
        if not ids or any(x <= 0 for x in ids):
            raise ConfigError("Укажите ID личных чатов администраторов.")
        return ids

    def probe(self, probe_id: str) -> Probe:
        return next(p for p in self.probes if p.id == probe_id)
