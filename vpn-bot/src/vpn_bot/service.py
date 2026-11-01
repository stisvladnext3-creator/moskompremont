from __future__ import annotations

import asyncio
import hashlib
import json
import time

from .config import Config
from .store import Store
from .xui import PanelClient, build_connection, client_enabled


class Service:
    def __init__(self, cfg: Config, store: Store, admins: tuple[int, ...]):
        self.cfg, self.store, self.admins = cfg, store, admins
        self.panel_lock = asyncio.Lock()
        self.panel_errors: set[str] = set()
        self.force_check = asyncio.Event()

    async def sync_panels(self, *, seed: bool = False) -> int:
        """Read existing state. Only explicit seed/pool-fill creates credentials."""
        added = 0
        async with self.panel_lock:
            for panel in self.cfg.panels:
                servers = [s for s in self.cfg.servers if s.panel == panel.id]
                try:
                    async with PanelClient(panel) as client:
                        for server in servers:
                            inbound = await client.inbound(server.inbound_id)
                            clients = await client.clients(inbound)
                            probe_email = f"vpn-watch-{server.id}-probe"
                            # Validate protocol/transport before creating anything in the panel.
                            if seed:
                                build_connection(server, inbound, {"id": "00000000-0000-4000-8000-000000000001"})
                            monitor = next((c for c in clients if c.get("email") == probe_email), None)
                            if seed and monitor is None:
                                monitor = await client.ensure_client(inbound, probe_email, server.flow)
                            if monitor is not None:
                                outbound, _ = build_connection(server, inbound, monitor)
                                self.store.save_profile(server.id, {
                                    "outbound": outbound, "host": server.host, "port": int(inbound["port"]),
                                    "enabled": bool(inbound.get("enable", True)) and client_enabled(monitor),
                                })
                            if seed:
                                # Divide the requested total across servers (15 total, not 15 per server).
                                index = self.cfg.servers.index(server)
                                target = self.cfg.pool_target // len(self.cfg.servers) + (index < self.cfg.pool_target % len(self.cfg.servers))
                                free = len(self.store.rows("SELECT id FROM links WHERE server_id=? AND assigned_to IS NULL AND enabled=1", (server.id,)))
                                slot = 0
                                while free < target:
                                    slot += 1
                                    if slot > 10000:
                                        raise RuntimeError("reserve_slot_limit")
                                    email = f"vpn-watch-{server.id}-r{slot:04}"
                                    if self.store.rows("SELECT id FROM links WHERE server_id=? AND email=?", (server.id, email)):
                                        continue
                                    reserve = await client.ensure_client(inbound, email, server.flow)
                                    if not client_enabled(reserve) or reserve.get("tgId"):
                                        continue
                                    _, uri = build_connection(server, inbound, reserve)
                                    self.store.add_link(server.id, reserve, uri)
                                    free += 1
                                    added += 1
                            # Refresh all owned clients without changing expiry/quotas set in the panel.
                            inbound = await client.inbound(server.inbound_id)
                            clients = await client.clients(inbound)
                            by_email = {c.get("email"): c for c in clients}
                            stats = {c.get("email"): c for c in inbound.get("clientStats", [])}
                            for link in self.store.rows("SELECT * FROM links WHERE server_id=?", (server.id,)):
                                remote = by_email.get(link["email"])
                                enabled = bool(remote) and bool(inbound.get("enable", True))
                                if remote:
                                    traffic = stats.get(link["email"], {})
                                    if int(remote.get("totalGB", 0)) > 0 and not traffic:
                                        endpoint = "clients/traffic/" if client.mode == "modern" else "inbounds/getClientTraffics/"
                                        traffic = await client.request("GET", endpoint + link["email"])
                                    enabled = enabled and client_enabled(remote, traffic)
                                self.store.execute("UPDATE links SET enabled=? WHERE id=?", (int(enabled), link["id"]))
                                if link["enabled"] and not enabled:
                                    self.store.notify(self.admins, f"⚠️ Резерв {link['email']} на {server.name} отключён, истёк или исчерпал лимит в 3x-ui.")
                    if panel.id in self.panel_errors:
                        self.panel_errors.remove(panel.id)
                        self.store.notify(self.admins, f"✅ Связь с панелью {panel.id} восстановлена.")
                except Exception as exc:
                    if seed:
                        raise
                    if panel.id not in self.panel_errors:
                        self.panel_errors.add(panel.id)
                        self.store.notify(self.admins, f"⚠️ Панель {panel.id} недоступна ({type(exc).__name__}). Проверки трафика продолжаются с последним профилем.")
        return added

    def jobs(self) -> list[dict]:
        profiles = self.store.profiles()
        jobs = []
        for server in self.cfg.servers:
            profile = profiles.get(server.id)
            # A TCP result is explicitly unverified and cannot unlock reserve delivery.
            if not profile:
                continue
            job = {
                "server_id": server.id, "host": server.host, "port": profile["port"],
                "outbound": profile["outbound"] if profile.get("enabled") else None,
                "check_url": server.check_url, "expected_status": server.expected_status,
            }
            job["revision"] = hashlib.sha256(json.dumps(job, sort_keys=True).encode()).hexdigest()
            jobs.append(job)
        return jobs

    def status(self, *, admin: bool = False) -> str:
        now = time.time()
        result = ["Состояние VPN:"]
        labels = {"up": "✅", "slow": "🟡", "down": "🔴", "stale": "⚪", "unknown": "⚪"}
        rows = self.store.rows("SELECT * FROM checks")
        for server in self.cfg.servers:
            result.append("\n" + server.name)
            for probe in self.cfg.probes:
                row = next(r for r in rows if r["server_id"] == server.id and r["probe_id"] == probe.id)
                state = row["state"] if row["last_seen"] >= now - self.cfg.stale_after else "stale"
                if state == "stale" and row["last_seen"] >= now - self.cfg.stale_after:
                    state = "unknown"  # Fresh samples are arriving; recovery is not confirmed yet.
                kind = "VPN-запрос" if row["kind"] == "proxy" else "порт, VPN не подтверждён"
                if not row["last_seen"]:
                    text = "ожидается первая проверка"
                elif state == "stale":
                    text = "нет свежих данных"
                elif row["ok"]:
                    text = f"{row['latency']:.0f} мс · {kind}"
                else:
                    text = "ошибка проверки" + (f" ({row['error']})" if admin else "")
                icon = "🟡" if state in {"up", "slow"} and not row["ok"] else labels[state]
                result.append(f"{icon} {probe.name}: {text}")
        return "\n".join(result)
