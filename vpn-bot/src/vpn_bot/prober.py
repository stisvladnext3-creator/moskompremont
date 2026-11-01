from __future__ import annotations

import asyncio
import copy
import json
import os
import socket
import tempfile
import time
from pathlib import Path

import httpx


async def tcp_probe(host: str, port: int, timeout: float) -> dict:
    started = time.monotonic()
    try:
        _, writer = await asyncio.wait_for(asyncio.open_connection(host, port), timeout)
        writer.close()
        await writer.wait_closed()
        return {"ok": True, "kind": "tcp", "latency_ms": (time.monotonic() - started) * 1000, "error": ""}
    except (OSError, TimeoutError):
        return {"ok": False, "kind": "tcp", "latency_ms": None, "error": "tcp_unreachable"}


async def proxy_probe(job: dict, timeout: float, xray_binary: str) -> dict:
    """Start a temporary real Xray client. No direct fallback is possible."""
    process = None
    config = copy.deepcopy(job["outbound"])
    config["tag"] = "vpn"
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    with tempfile.TemporaryDirectory(prefix="vpn-probe-") as directory:
        path = Path(directory) / "xray.json"
        body = {
            "log": {"loglevel": "none"},
            "inbounds": [{"listen": "127.0.0.1", "port": port, "protocol": "socks", "settings": {"auth": "noauth", "udp": False}}],
            "outbounds": [config],
        }
        path.write_text(json.dumps(body))
        os.chmod(path, 0o600)
        try:
            process = await asyncio.create_subprocess_exec(xray_binary, "run", "-config", str(path), stdout=asyncio.subprocess.DEVNULL, stderr=asyncio.subprocess.DEVNULL)
            ready = False
            async with asyncio.timeout(timeout):
                for _ in range(50):
                    if process.returncode is not None:
                        return {"ok": False, "kind": "proxy", "latency_ms": None, "error": "xray_config_rejected"}
                    try:
                        _, writer = await asyncio.open_connection("127.0.0.1", port)
                        writer.close()
                        await writer.wait_closed()
                        ready = True
                        break
                    except OSError:
                        await asyncio.sleep(0.05)
                if not ready:
                    raise TimeoutError
                # trust_env=False belongs to this generated client: measuring the VPN
                # must not silently use HTTP_PROXY or NO_PROXY on the probe host.
                async with httpx.AsyncClient(proxy=f"socks5://127.0.0.1:{port}", timeout=timeout, trust_env=False, follow_redirects=False) as client:
                    request_started = time.monotonic()
                    async with client.stream("GET", job["check_url"]) as response:
                        if response.status_code != job["expected_status"]:
                            return {"ok": False, "kind": "proxy", "latency_ms": None, "error": "unexpected_http_status"}
                return {"ok": True, "kind": "proxy", "latency_ms": (time.monotonic() - request_started) * 1000, "error": ""}
        except FileNotFoundError:
            return {"ok": False, "kind": "proxy", "latency_ms": None, "error": "xray_not_installed"}
        except (TimeoutError, OSError, httpx.HTTPError):
            return {"ok": False, "kind": "proxy", "latency_ms": None, "error": "vpn_request_failed"}
        finally:
            if process is not None and process.returncode is None:
                process.terminate()
                try:
                    await asyncio.wait_for(process.wait(), 2)
                except TimeoutError:
                    process.kill()
                    await process.wait()


async def run_checks(jobs: list[dict], timeout: float, xray_binary: str, concurrency: int = 4) -> list[dict]:
    semaphore = asyncio.Semaphore(concurrency)

    async def check(job):
        async with semaphore:
            if job.get("outbound"):
                result = await proxy_probe(job, timeout, xray_binary)
            else:
                result = await tcp_probe(job["host"], job["port"], timeout)
            return {"server_id": job["server_id"], "revision": job.get("revision", ""), **result}

    return list(await asyncio.gather(*(check(job) for job in jobs)))
