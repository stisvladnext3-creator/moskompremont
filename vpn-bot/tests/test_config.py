import dataclasses
from pathlib import Path

import pytest

from vpn_bot.config import Config, ConfigError
from vpn_bot.telegram_bot import build_application
from vpn_bot.service import Service


def test_example_is_valid():
    cfg = Config.load(Path(__file__).parents[1] / "config.example.toml")
    assert cfg.pool_target == 15 and cfg.timeout == 8


def test_config_without_russian_probe_cannot_enable_delivery(cfg):
    with pytest.raises(ConfigError):
        dataclasses.replace(cfg, probes=cfg.probes[:1]).validate()


def test_admin_ids_are_explicit_positive_private_ids(cfg, monkeypatch):
    monkeypatch.setenv("TELEGRAM_ADMIN_IDS", "11,22")
    assert cfg.admins() == (11, 22)
    monkeypatch.setenv("TELEGRAM_ADMIN_IDS", "-100123")
    with pytest.raises(ConfigError): cfg.admins()


def test_real_telegram_application_builds_without_network(cfg, store, monkeypatch):
    monkeypatch.setenv("TELEGRAM_BOT_TOKEN", "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk")
    app = build_application(Service(cfg, store, (999,)))
    assert len(app.handlers[0]) == 2
    assert app.concurrent_updates == 8
