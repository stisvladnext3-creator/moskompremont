import time
from types import SimpleNamespace
from unittest.mock import AsyncMock

from vpn_bot.main import deliver_outbox
from vpn_bot.service import Service
from vpn_bot.store import Store


async def test_notifications_survive_process_restart(tmp_path, cfg):
    path = str(tmp_path / "restart.db")
    first = Store(path)
    first.enqueue(999, "Сервер недоступен")
    first.close()
    second = Store(path)
    bot = SimpleNamespace(send_message=AsyncMock())
    await deliver_outbox(Service(cfg, second, (999,)), bot)
    assert bot.send_message.await_args.args[1] == "Сервер недоступен"
    assert not second.rows("SELECT * FROM outbox")
    second.close()
