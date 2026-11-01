from __future__ import annotations

import argparse
import asyncio
import contextlib
import logging
import os
import signal
import ssl
import time
import uuid
from datetime import timedelta
from urllib.parse import urlparse

import httpx
from aiohttp import web
from telegram import BotCommand, LinkPreviewOptions
from telegram.error import RetryAfter

from .api import make_api
from .config import Config, ConfigError, secret
from .prober import run_checks
from .service import Service
from .store import Store
from .telegram_bot import build_application

LOG = logging.getLogger("vpn_bot")


async def deliver_outbox(service: Service, bot):
    """At-least-once delivery; failed Telegram requests survive a restart."""
    now = time.time()
    for row in service.store.rows("SELECT * FROM outbox WHERE next_try<=? ORDER BY id LIMIT 20", (now,)):
        try:
            body = row["body"]
            # Telegram limits messages to 4096 UTF-16 units; conservative chunks also fit emoji.
            for start in range(0, len(body), 1800):
                await bot.send_message(row["chat_id"], body[start:start + 1800], link_preview_options=LinkPreviewOptions(is_disabled=True))
            service.store.execute("DELETE FROM outbox WHERE id=?", (row["id"],))
        except Exception as exc:
            delay = min(300, 2 ** min(row["attempts"] + 1, 9))
            if isinstance(exc, RetryAfter):
                retry = exc.retry_after
                delay = max(delay, retry.total_seconds() if isinstance(retry, timedelta) else retry)
            service.store.execute("UPDATE outbox SET attempts=attempts+1,next_try=? WHERE id=?", (now + delay, row["id"]))
    for row in service.store.rows("SELECT * FROM forwards WHERE next_try<=? ORDER BY id LIMIT 20", (now,)):
        try:
            header_id = row["header_id"]
            if not header_id:
                message = await bot.send_message(row["admin_id"], f"Обращение #{row['ticket_id']} · ID {row['user_id']}\n/reply {row['ticket_id']} текст")
                header_id = message.message_id
                service.store.execute("UPDATE forwards SET header_id=? WHERE id=?", (header_id, row["id"]))
                service.store.execute("INSERT OR REPLACE INTO replies VALUES(?,?,?)", (row["admin_id"], header_id, row["ticket_id"]))
            copied = await bot.copy_message(row["admin_id"], row["user_id"], row["message_id"])
            with service.store.transaction():
                service.store.execute("INSERT OR REPLACE INTO replies VALUES(?,?,?)", (row["admin_id"], copied.message_id, row["ticket_id"]))
                service.store.execute("DELETE FROM forwards WHERE id=?", (row["id"],))
        except Exception:
            service.store.execute("UPDATE forwards SET attempts=attempts+1,next_try=? WHERE id=?", (now + min(300, 2 ** min(row["attempts"] + 1, 9)), row["id"]))


async def controller(cfg: Config):
    admins = cfg.admins()
    store = Store(cfg.database)
    store.init_checks(cfg)
    service = Service(cfg, store, admins)
    api = web.AppRunner(make_api(service), access_log=None)
    app = build_application(service)
    stop = asyncio.Event()
    for sig in (signal.SIGINT, signal.SIGTERM):
        with contextlib.suppress(NotImplementedError):
            asyncio.get_running_loop().add_signal_handler(sig, stop.set)

    async def panel_loop():
        while True:
            await service.sync_panels()
            await asyncio.sleep(cfg.panel_refresh)

    async def local_loop():
        while True:
            started = time.monotonic()
            try:
                jobs = service.jobs()
                if jobs:
                    results = await run_checks(jobs, cfg.timeout, cfg.xray_binary, cfg.concurrency)
                    store.ingest(cfg.local_probe_id, str(uuid.uuid4()), results, cfg, admins)
            except Exception as exc:
                LOG.error("Local checks failed: %s", type(exc).__name__)
            try:
                await asyncio.wait_for(service.force_check.wait(), max(1, cfg.interval - (time.monotonic() - started)))
                service.force_check.clear()
            except TimeoutError:
                pass

    async def delivery_loop():
        while True:
            store.mark_stale(cfg, admins)
            await deliver_outbox(service, app.bot)
            await asyncio.sleep(1)

    tasks = []
    try:
        await api.setup()
        await web.TCPSite(api, cfg.listen_host, cfg.listen_port).start()
        await app.initialize()
        await app.start()
        await app.updater.start_polling(drop_pending_updates=False, allowed_updates=["message"])
        await app.bot.set_my_commands([BotCommand("status", "Состояние VPN"), BotCommand("link", "Резервная ссылка"), BotCommand("help", "Помощь"), BotCommand("myid", "Мой Telegram ID")])
        tasks = [asyncio.create_task(panel_loop()), asyncio.create_task(delivery_loop())]
        if cfg.local_probe_id:
            tasks.append(asyncio.create_task(local_loop()))
        store.notify(admins, "Бот запущен. /status — состояние серверов, /pool — резерв, /help — управление.")
        # Stop the container if a critical loop terminates; restart policy restores service.
        waiter = asyncio.create_task(stop.wait())
        done, _ = await asyncio.wait([waiter, *tasks], return_when=asyncio.FIRST_COMPLETED)
        for task in done:
            if task is not waiter:
                task.result()
        waiter.cancel()
    finally:
        for task in tasks:
            task.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)
        if app.updater.running:
            await app.updater.stop()
        if app.running:
            await app.stop()
        await app.shutdown()
        await api.cleanup()
        store.close()


async def probe_agent(cfg: Config, probe_id: str):
    try:
        probe = cfg.probe(probe_id)
    except StopIteration as exc:
        raise ConfigError("Неизвестный идентификатор проверяющего узла.") from exc
    if probe_id == cfg.local_probe_id:
        raise ConfigError("Для удалённого агента выберите отдельный probe_id.")
    token = secret(probe.token_env)
    if len(token) < 32:
        raise ConfigError("Токен проверяющего узла должен содержать минимум 32 символа.")
    target = cfg.controller_url.rstrip("/")
    parsed = urlparse(target)
    if parsed.scheme != "https" and not (parsed.scheme == "http" and parsed.hostname in {"127.0.0.1", "localhost", "::1"}):
        raise ConfigError("Для удалённого controller_url нужен HTTPS.")
    base = target + f"/v1/probes/{probe_id}"
    cached = []
    ca_file = secret(cfg.controller_ca_env, False)
    verify = ssl.create_default_context(cafile=ca_file) if ca_file else True
    async with httpx.AsyncClient(headers={"Authorization": "Bearer " + token}, timeout=20, follow_redirects=False, verify=verify) as client:
        while True:
            started = time.monotonic()
            try:
                response = await client.get(base + "/jobs")
                response.raise_for_status()
                cached = response.json()["jobs"]
            except (httpx.HTTPError, ValueError, KeyError):
                LOG.warning("Cannot refresh probe jobs; retrying with last known profiles.")
            if cached:
                results = await run_checks(cached, cfg.timeout, cfg.xray_binary, cfg.concurrency)
                report = {"batch_id": str(uuid.uuid4()), "sent_at": time.time(), "results": results}
                # Retry this same idempotent batch, never turn retries into extra failures.
                for attempt in range(3):
                    try:
                        response = await client.post(base + "/reports", json=report)
                        response.raise_for_status()
                        break
                    except httpx.HTTPError:
                        if attempt == 2:
                            LOG.warning("Cannot deliver probe report; controller will mark it stale.")
                        else:
                            await asyncio.sleep(1 + attempt)
            await asyncio.sleep(max(1, cfg.interval - (time.monotonic() - started)))


async def seed(cfg: Config):
    store = Store(cfg.database)
    try:
        count = await Service(cfg, store, ()).sync_panels(seed=True)
        print(f"Создано резервных ссылок: {count}. Секреты сохранены в базе данных; /pool показывает остаток.")
    finally:
        store.close()


def main():
    parser = argparse.ArgumentParser(description="Telegram VPN support and monitoring")
    parser.add_argument("--config", default="config.toml")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("run", help="Telegram bot and probe controller")
    sub.add_parser("seed", help="Create missing reserve clients and monitoring clients in 3x-ui")
    sub.add_parser("validate", help="Validate configuration without contacting Telegram or 3x-ui")
    probe = sub.add_parser("probe", help="Russian or external probe agent")
    probe.add_argument("--id", required=True)
    args = parser.parse_args()
    os.umask(0o077)
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
    # HTTP debug logging contains Telegram tokens in URLs and must remain disabled.
    for name in ("httpx", "httpcore", "telegram", "aiohttp.access"):
        logging.getLogger(name).setLevel(logging.WARNING)
    try:
        cfg = Config.load(args.config)
        if args.command == "validate":
            print(f"Конфигурация корректна: серверов {len(cfg.servers)}, узлов {len(cfg.probes)}, резерв {cfg.pool_target}.")
            return
        asyncio.run(controller(cfg) if args.command == "run" else seed(cfg) if args.command == "seed" else probe_agent(cfg, args.id))
    except KeyboardInterrupt:
        pass
    except (ConfigError, FileNotFoundError) as exc:
        parser.exit(2, str(exc) + "\n")
    except Exception as exc:
        parser.exit(1, f"Ошибка запуска/операции: {type(exc).__name__}. Проверьте конфигурацию и соединение.\n")


if __name__ == "__main__":
    main()
