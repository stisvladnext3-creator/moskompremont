import os

import pytest
from aiohttp import web
from aiohttp.test_utils import TestServer

from vpn_bot.prober import proxy_probe


@pytest.mark.parametrize("protocol,success", [("freedom", True), ("blackhole", False)])
async def test_real_xray_traffic_or_failure_without_direct_fallback(protocol, success):
    binary = os.getenv("XRAY_TEST_BINARY")
    if not binary:
        pytest.skip("Set XRAY_TEST_BINARY for real engine traffic tests")
    hits = []
    async def response(request):
        hits.append(True)
        return web.Response(status=204)
    app = web.Application()
    app.router.add_get("/probe", response)
    async with TestServer(app) as server:
        result = await proxy_probe({"outbound": {"protocol": protocol, "settings": {}}, "check_url": str(server.make_url("/probe")), "expected_status": 204}, 3, binary)
        assert result["ok"] is success and result["kind"] == "proxy"
        assert bool(hits) is success


async def test_missing_xray_cannot_report_working_vpn():
    result = await proxy_probe({"outbound": {"protocol": "freedom"}, "check_url": "https://example.com/", "expected_status": 200}, 1, "/missing/xray-binary")
    assert result["ok"] is False and result["error"] == "xray_not_installed"
