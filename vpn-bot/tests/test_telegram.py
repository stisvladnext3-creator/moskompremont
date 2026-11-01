import time
from types import SimpleNamespace
from unittest.mock import AsyncMock

from vpn_bot.main import deliver_outbox
from vpn_bot.service import Service
from vpn_bot.telegram_bot import TelegramHandlers
from conftest import add_reserve, report


def update(uid, text, *, private=True, mid=1):
    message = SimpleNamespace(text=text, message_id=mid, reply_text=AsyncMock(), reply_to_message=None)
    return SimpleNamespace(effective_user=SimpleNamespace(id=uid, full_name="Test"), effective_chat=SimpleNamespace(type="private" if private else "group"), effective_message=message)


async def test_group_and_non_admin_cannot_approve_customers(cfg, store):
    handlers = TelegramHandlers(Service(cfg, store, (999,)))
    store.register(2, "customer")
    for message in [update(999, "/allow 2", private=False), update(1, "/allow 2")]:
        await handlers.command(message, SimpleNamespace(args=["2"]))
    assert store.rows("SELECT status FROM users WHERE id=2")[0]["status"] == "pending"


async def test_unapproved_customer_cannot_get_reserve(cfg, store):
    handlers = TelegramHandlers(Service(cfg, store, (999,)))
    add_reserve(store)
    report(store, cfg)
    message = update(1, "/link")
    await handlers.command(message, SimpleNamespace(args=[]))
    assert not store.current_link(1)


async def test_support_saved_before_telegram_delivery_and_retry(cfg, store):
    service = Service(cfg, store, (999,))
    handlers = TelegramHandlers(service)
    message = update(1, "Помогите подключиться", mid=42)
    await handlers.message(message, SimpleNamespace(bot=AsyncMock()))
    assert len(store.rows("SELECT * FROM forwards")) == 1
    bot = SimpleNamespace(send_message=AsyncMock(return_value=SimpleNamespace(message_id=100)), copy_message=AsyncMock(side_effect=RuntimeError("offline")))
    await deliver_outbox(service, bot)
    pending = store.rows("SELECT * FROM forwards")[0]
    assert pending["header_id"] == 100 and pending["attempts"] == 1
    store.execute("UPDATE forwards SET next_try=0")
    bot.copy_message = AsyncMock(return_value=SimpleNamespace(message_id=101))
    await deliver_outbox(service, bot)
    assert not store.rows("SELECT * FROM forwards")
    assert len(store.rows("SELECT * FROM replies WHERE ticket_id=1")) == 2


async def test_alerts_remain_queued_on_telegram_outage(cfg, store):
    service = Service(cfg, store, (999,))
    store.enqueue(999, "VPN недоступен")
    bot = SimpleNamespace(send_message=AsyncMock(side_effect=RuntimeError("offline")))
    await deliver_outbox(service, bot)
    assert store.rows("SELECT * FROM outbox")[0]["attempts"] == 1
    store.execute("UPDATE outbox SET next_try=0")
    bot.send_message = AsyncMock()
    await deliver_outbox(service, bot)
    assert not store.rows("SELECT * FROM outbox")
