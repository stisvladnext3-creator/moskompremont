from __future__ import annotations

import json
import os
import sqlite3
import threading
import time
from contextlib import contextmanager
from pathlib import Path

from .config import Config


class Store:
    def __init__(self, path: str):
        if path != ":memory:":
            Path(path).parent.mkdir(parents=True, exist_ok=True)
            fd = os.open(path, os.O_CREAT | os.O_RDWR, 0o600)
            os.close(fd)
            os.chmod(path, 0o600)
        self.lock = threading.RLock()
        self.db = sqlite3.connect(path, check_same_thread=False, isolation_level=None)
        self.db.row_factory = sqlite3.Row
        self.db.execute("PRAGMA journal_mode=WAL")
        self.db.execute("PRAGMA busy_timeout=5000")
        self.db.executescript("""
          CREATE TABLE IF NOT EXISTS users (
            id INTEGER PRIMARY KEY, name TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'pending');
          CREATE TABLE IF NOT EXISTS profiles (
            server_id TEXT PRIMARY KEY, data TEXT NOT NULL, updated REAL NOT NULL);
          CREATE TABLE IF NOT EXISTS links (
            id INTEGER PRIMARY KEY, server_id TEXT NOT NULL, client_id TEXT NOT NULL,
            email TEXT NOT NULL, url TEXT NOT NULL UNIQUE, enabled INTEGER NOT NULL DEFAULT 1,
            assigned_to INTEGER, issued_at REAL, created REAL NOT NULL,
            UNIQUE(server_id, client_id));
          CREATE TABLE IF NOT EXISTS checks (
            probe_id TEXT NOT NULL, server_id TEXT NOT NULL, state TEXT NOT NULL DEFAULT 'unknown',
            failures INTEGER NOT NULL DEFAULT 0, successes INTEGER NOT NULL DEFAULT 0,
            ok INTEGER NOT NULL DEFAULT 0, kind TEXT NOT NULL DEFAULT 'none', latency REAL,
            error TEXT NOT NULL DEFAULT '', last_seen REAL NOT NULL DEFAULT 0, created REAL NOT NULL,
            PRIMARY KEY(probe_id,server_id));
          CREATE TABLE IF NOT EXISTS batches (
            probe_id TEXT NOT NULL, batch_id TEXT NOT NULL, seen REAL NOT NULL,
            PRIMARY KEY(probe_id,batch_id));
          CREATE TABLE IF NOT EXISTS outbox (
            id INTEGER PRIMARY KEY, chat_id INTEGER NOT NULL, body TEXT NOT NULL,
            attempts INTEGER NOT NULL DEFAULT 0, next_try REAL NOT NULL DEFAULT 0);
          CREATE TABLE IF NOT EXISTS tickets (
            id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL, open INTEGER NOT NULL DEFAULT 1,
            created REAL NOT NULL);
          CREATE TABLE IF NOT EXISTS replies (
            chat_id INTEGER NOT NULL, message_id INTEGER NOT NULL, ticket_id INTEGER NOT NULL,
            PRIMARY KEY(chat_id,message_id));
          CREATE TABLE IF NOT EXISTS forwards (
            id INTEGER PRIMARY KEY, admin_id INTEGER NOT NULL, user_id INTEGER NOT NULL,
            message_id INTEGER NOT NULL, ticket_id INTEGER NOT NULL, header_id INTEGER,
            attempts INTEGER NOT NULL DEFAULT 0, next_try REAL NOT NULL DEFAULT 0,
            UNIQUE(admin_id,user_id,message_id));
        """)

    @contextmanager
    def transaction(self):
        with self.lock:
            self.db.execute("BEGIN IMMEDIATE")
            try:
                yield
                self.db.execute("COMMIT")
            except BaseException:
                self.db.execute("ROLLBACK")
                raise

    def rows(self, sql: str, args=()) -> list[dict]:
        with self.lock:
            return [dict(r) for r in self.db.execute(sql, args).fetchall()]

    def execute(self, sql: str, args=()):
        with self.lock:
            return self.db.execute(sql, args)

    def init_checks(self, cfg: Config, now: float | None = None):
        now = time.time() if now is None else now
        with self.transaction():
            probe_ids = tuple(p.id for p in cfg.probes)
            server_ids = tuple(s.id for s in cfg.servers)
            self.execute("DELETE FROM checks WHERE probe_id NOT IN (" + ",".join("?" for _ in probe_ids) + ") OR server_id NOT IN (" + ",".join("?" for _ in server_ids) + ")", probe_ids + server_ids)
            for probe in cfg.probes:
                for server in cfg.servers:
                    self.execute("INSERT OR IGNORE INTO checks(probe_id,server_id,created) VALUES(?,?,?)", (probe.id, server.id, now))

    def register(self, user_id: int, name: str) -> tuple[str, bool]:
        with self.transaction():
            old = self.rows("SELECT * FROM users WHERE id=?", (user_id,))
            self.execute("INSERT INTO users(id,name) VALUES(?,?) ON CONFLICT(id) DO UPDATE SET name=excluded.name", (user_id, name[:200]))
            return (old[0]["status"] if old else "pending", not old)

    def enqueue(self, chat_id: int, body: str):
        self.execute("INSERT INTO outbox(chat_id,body) VALUES(?,?)", (chat_id, body))

    def notify(self, admins: tuple[int, ...], body: str):
        for user_id in admins:
            self.enqueue(user_id, body)

    def save_profile(self, server_id: str, data: dict):
        encoded = json.dumps(data, sort_keys=True)
        with self.transaction():
            existing = self.rows("SELECT data FROM profiles WHERE server_id=?", (server_id,))
            if existing and json.loads(existing[0]["data"]) != data:
                self.execute("UPDATE checks SET state='unknown',ok=0,kind='none',failures=0,successes=0,last_seen=0,created=? WHERE server_id=?", (time.time(), server_id))
            self.execute("INSERT INTO profiles VALUES(?,?,?) ON CONFLICT(server_id) DO UPDATE SET data=excluded.data,updated=excluded.updated", (server_id, encoded, time.time()))

    def profiles(self) -> dict[str, dict]:
        return {r["server_id"]: json.loads(r["data"]) for r in self.rows("SELECT * FROM profiles")}

    def add_link(self, server_id: str, client: dict, url: str):
        self.execute("INSERT OR IGNORE INTO links(server_id,client_id,email,url,created) VALUES(?,?,?,?,?)", (server_id, client["id"], client["email"], url, time.time()))

    def current_link(self, user_id: int) -> dict | None:
        rows = self.rows("SELECT * FROM links WHERE assigned_to=? ORDER BY issued_at DESC,id DESC LIMIT 1", (user_id,))
        return rows[0] if rows else None

    def healthy_servers(self, cfg: Config, now: float | None = None) -> set[str]:
        now = time.time() if now is None else now
        ru = {p.id for p in cfg.probes if p.region.lower() == "ru"}
        configured = {s.id for s in cfg.servers}
        found: dict[str, set[str]] = {}
        for row in self.rows("SELECT * FROM checks WHERE ok=1 AND kind='proxy' AND state IN ('up','slow') AND last_seen>=?", (now - cfg.stale_after,)):
            if row["probe_id"] in ru and row["server_id"] in configured:
                found.setdefault(row["server_id"], set()).add(row["probe_id"])
        return {s for s, probes in found.items() if len(probes) >= cfg.min_ru_successes}

    def issue(self, user_id: int, cfg: Config, *, renew: bool = False, server_id: str = "") -> tuple[dict | None, str]:
        """A single transaction reserves one personal link; never hand it to another user."""
        with self.transaction():
            user = self.rows("SELECT status FROM users WHERE id=?", (user_id,))
            if not user or user[0]["status"] != "active":
                return None, "not_allowed"
            current = self.current_link(user_id)
            healthy = self.healthy_servers(cfg)
            if server_id:
                healthy &= {server_id}
            if current and current["enabled"] and current["server_id"] in healthy and not renew:
                return current, "existing"
            if current and time.time() - current["issued_at"] < cfg.issue_cooldown and not renew:
                return None, "cooldown"
            choices = self.rows("SELECT * FROM links WHERE assigned_to IS NULL AND enabled=1 ORDER BY id")
            # Prefer another host after failure; a new UUID on a blocked IP cannot help.
            hosts = {s.id: s.host for s in cfg.servers}
            previous_host = hosts.get(current["server_id"]) if current else None
            choices.sort(key=lambda row: hosts.get(row["server_id"]) == previous_host)
            link = next((row for row in choices if row["server_id"] in healthy), None)
            if link is None:
                return None, "no_verified_reserve"
            self.execute("UPDATE links SET assigned_to=?,issued_at=? WHERE id=? AND assigned_to IS NULL", (user_id, time.time(), link["id"]))
            link = self.rows("SELECT * FROM links WHERE id=?", (link["id"],))[0]
            return link, "issued"

    def ingest(self, probe_id: str, batch_id: str, results: list[dict], cfg: Config, admins: tuple[int, ...], now: float | None = None) -> bool:
        now = time.time() if now is None else now
        server_names = {s.id: s.name for s in cfg.servers}
        probe_name = cfg.probe(probe_id).name
        with self.transaction():
            if self.rows("SELECT 1 FROM batches WHERE probe_id=? AND batch_id=?", (probe_id, batch_id)):
                return False
            self.execute("INSERT INTO batches VALUES(?,?,?)", (probe_id, batch_id, now))
            self.execute("DELETE FROM batches WHERE seen<?", (now - 86400,))
            for result in results:
                sid = result["server_id"]
                old = self.rows("SELECT * FROM checks WHERE probe_id=? AND server_id=?", (probe_id, sid))[0]
                ok = result["ok"]
                failures = 0 if ok else old["failures"] + 1
                target = "slow" if ok and result["latency_ms"] > cfg.slow_ms else "up"
                previous_target = "slow" if old["ok"] and old["latency"] is not None and old["latency"] > cfg.slow_ms else "up"
                successes = (old["successes"] + 1 if old["ok"] and previous_target == target else 1) if ok else 0
                state = old["state"]
                if not ok and failures >= cfg.failures:
                    state = "down"
                elif ok and (old["state"] == "unknown" or successes >= cfg.recoveries):
                    state = target
                self.execute("UPDATE checks SET state=?,failures=?,successes=?,ok=?,kind=?,latency=?,error=?,last_seen=? WHERE probe_id=? AND server_id=?", (state, failures, successes, int(ok), result["kind"], result["latency_ms"], result.get("error", ""), now, probe_id, sid))
                if state != old["state"] and not (old["state"] == "unknown" and state == "up"):
                    labels = {"up": "✅ Восстановлен", "slow": "⚠️ Высокая задержка", "down": "🔴 Недоступен", "unknown": "Нет подтверждения", "stale": "Нет данных"}
                    kind = "запрос через VPN" if result["kind"] == "proxy" else "только порт (VPN не подтверждён)"
                    latency = f"{result['latency_ms']:.0f} мс" if ok else result.get("error", "ошибка")
                    self.notify(admins, f"{labels[state]}: {server_names[sid]}\nУзел: {probe_name}\nПроверка: {kind}\n{latency}")
            return True

    def mark_stale(self, cfg: Config, admins: tuple[int, ...], now: float | None = None):
        now = time.time() if now is None else now
        with self.transaction():
            rows = self.rows("SELECT * FROM checks WHERE state!='stale' AND MAX(last_seen,created)<?", (now - cfg.stale_after,))
            for row in rows:
                self.execute("UPDATE checks SET state='stale',ok=0,successes=0,failures=0 WHERE probe_id=? AND server_id=?", (row["probe_id"], row["server_id"]))
            for pid in {row["probe_id"] for row in rows}:
                self.notify(admins, f"⚪ Нет свежих данных от узла {cfg.probe(pid).name}. Доступность из этой точки неизвестна; автоматическая выдача её результаты не использует.")

    def ticket(self, user_id: int) -> int:
        with self.transaction():
            rows = self.rows("SELECT id FROM tickets WHERE user_id=? AND open=1 ORDER BY id DESC LIMIT 1", (user_id,))
            if rows:
                return rows[0]["id"]
            return self.execute("INSERT INTO tickets(user_id,created) VALUES(?,?)", (user_id, time.time())).lastrowid

    def queue_support(self, user_id: int, message_id: int, ticket_id: int, admins: tuple[int, ...]):
        with self.transaction():
            for admin in admins:
                self.execute("INSERT OR IGNORE INTO forwards(admin_id,user_id,message_id,ticket_id) VALUES(?,?,?,?)", (admin, user_id, message_id, ticket_id))

    def close(self):
        self.db.close()
