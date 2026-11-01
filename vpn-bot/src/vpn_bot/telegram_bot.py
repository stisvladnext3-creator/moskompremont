from __future__ import annotations

import logging
import time
from collections import defaultdict, deque

from telegram import LinkPreviewOptions, Update
from telegram.ext import Application, CommandHandler, ContextTypes, MessageHandler, filters

from .config import secret
from .service import Service

USER_HELP = "Команды:\n/status — состояние VPN\n/link — получить рабочую резервную ссылку\n/myid — мой Telegram ID\n\nДля помощи просто напишите сообщение или пришлите скриншот. Его получит владелец VPN. Доступ к ссылкам открывает владелец."
ADMIN_HELP = "Управление:\n/status — состояние всех серверов\n/check — запустить внеочередную локальную проверку\n/users — заявки на доступ\n/allow ID — разрешить выдачу ссылок\n/deny ID — закрыть доступ\n/pool — остаток резервов\n/pool fill — довести свободный резерв до 15 (или pool_target)\n/issue ID — повторно выдать проверенный резерв клиенту\n/tickets — открытые обращения\n/reply НОМЕР текст — ответить\n/close НОМЕР — закрыть обращение\n\nНа скопированное сообщение клиента также можно ответить обычной кнопкой «Ответить», включая фото и документы."


class TelegramHandlers:
    def __init__(self, service: Service):
        self.service = service
        self.store = service.store
        self.admins = service.admins
        self.rates = defaultdict(deque)

    def limited(self, key, count: int = 5, seconds: int = 60) -> bool:
        queue = self.rates[key]
        now = time.monotonic()
        while queue and queue[0] <= now - seconds:
            queue.popleft()
        if len(queue) >= count:
            return True
        queue.append(now)
        return False

    def admin(self, update: Update) -> bool:
        return update.effective_user.id in self.admins and update.effective_chat.type == "private"

    async def command(self, update: Update, context: ContextTypes.DEFAULT_TYPE):
        message = update.effective_message
        user = update.effective_user
        if not message or not user or update.effective_chat.type != "private":
            return
        command = message.text.split()[0].split("@")[0][1:]
        if command == "myid":
            await message.reply_text(f"Ваш Telegram ID: {user.id}")
            return
        is_admin = self.admin(update)
        if not is_admin and self.limited((user.id, "commands"), 10):
            return
        status, fresh = self.store.register(user.id, user.full_name)
        if is_admin:
            self.store.execute("UPDATE users SET status='active' WHERE id=?", (user.id,))
            status = "active"
        if fresh and not is_admin:
            self.store.notify(self.admins, f"Новая заявка: {user.full_name[:100]}\nID: {user.id}\nРазрешить: /allow {user.id}")
        if command in {"start", "help"}:
            await message.reply_text(ADMIN_HELP if is_admin else USER_HELP)
            return
        if command == "status":
            await message.reply_text(self.service.status(admin=is_admin) if status == "active" else "Доступ ещё не открыт владельцем. Для помощи напишите сообщение.")
            return
        if command == "link":
            if status != "active":
                await message.reply_text("Выдача ссылок доступна после подтверждения владельцем. Ваша заявка зарегистрирована.")
                return
            if not self.service.cfg.automatic_links:
                if not self.limited((user.id, "link_request"), 1, 600):
                    self.store.notify(self.admins, f"Клиент {user.id} запросил резервную ссылку. Выдать: /issue {user.id}")
                await message.reply_text("Запрос на резервную ссылку отправлен владельцу.")
                return
            link, reason = self.store.issue(user.id, self.service.cfg)
            if link:
                await message.reply_text("Ваша ссылка:\n" + link["url"], link_preview_options=LinkPreviewOptions(is_disabled=True))
            else:
                texts = {"cooldown": "Резерв уже выдавался недавно. Напишите владельцу, если подключение не работает.", "no_verified_reserve": "Сейчас нет свободной ссылки с подтверждённой проверкой из РФ. Запрос передан владельцу."}
                await message.reply_text(texts.get(reason, "Доступ к выдаче ссылок закрыт."))
                if not self.limited((user.id, "link_request"), 1, 600):
                    self.store.notify(self.admins, f"Не удалось выдать резерв клиенту {user.id}: {reason}.")
            return
        if not is_admin:
            await message.reply_text("Команда недоступна. Для помощи напишите сообщение.")
            return
        try:
            if command == "users":
                rows = self.store.rows("SELECT * FROM users WHERE status='pending' LIMIT 30")
                await message.reply_text("Заявки:\n" + ("\n".join(f"{r['id']} · {r['name']}" for r in rows) or "Нет новых заявок."))
            elif command in {"allow", "deny"}:
                uid = int(context.args[0])
                if not self.store.rows("SELECT id FROM users WHERE id=?", (uid,)):
                    await message.reply_text("Клиент сначала должен открыть бота и отправить /start.")
                    return
                active = command == "allow"
                self.store.execute("UPDATE users SET status=? WHERE id=?", ("active" if active else "blocked", uid))
                self.store.enqueue(uid, "Доступ открыт. /link — резервная ссылка, /status — состояние VPN." if active else "Доступ к выдаче ссылок закрыт владельцем.")
                await message.reply_text("Готово.")
            elif command == "check":
                self.service.force_check.set()
                await message.reply_text("Локальная проверка запрошена. Узел в РФ пришлёт результат в своём ближайшем цикле; интервал — " + str(self.service.cfg.interval) + " с.")
            elif command == "pool":
                if context.args == ["fill"]:
                    await message.reply_text("Создаю недостающие резервные учётные записи в 3x-ui…")
                    added = await self.service.sync_panels(seed=True)
                    await message.reply_text(f"Добавлено резервов: {added}.")
                rows = self.store.rows("SELECT server_id,SUM(assigned_to IS NULL AND enabled=1) AS free,SUM(assigned_to IS NOT NULL) AS issued FROM links GROUP BY server_id")
                await message.reply_text("Резерв:\n" + ("\n".join(f"{r['server_id']}: свободно {r['free']}, выдано {r['issued']}" for r in rows) or "Пусто. Выполните /pool fill после настройки 3x-ui."))
            elif command == "issue":
                uid = int(context.args[0])
                link, reason = self.store.issue(uid, self.service.cfg, renew=True)
                if link:
                    self.store.enqueue(uid, "Владелец выдал вам резервную ссылку:\n" + link["url"])
                    await message.reply_text(f"Ссылка закреплена за {uid} и поставлена в очередь доставки.")
                else:
                    await message.reply_text("Выдача не выполнена: " + reason + ". Нужен одобренный клиент и свежий успешный VPN-тест из РФ.")
            elif command == "tickets":
                rows = self.store.rows("SELECT * FROM tickets WHERE open=1 ORDER BY id DESC LIMIT 30")
                await message.reply_text("Обращения:\n" + ("\n".join(f"#{r['id']} · ID {r['user_id']}" for r in rows) or "Нет открытых обращений."))
            elif command in {"reply", "close"}:
                ticket_id = int(context.args[0].lstrip("#"))
                rows = self.store.rows("SELECT * FROM tickets WHERE id=?", (ticket_id,))
                if not rows:
                    await message.reply_text("Обращение не найдено.")
                    return
                if command == "close":
                    self.store.execute("UPDATE tickets SET open=0 WHERE id=?", (ticket_id,))
                else:
                    body = message.text.split(maxsplit=2)[2]
                    self.store.enqueue(rows[0]["user_id"], "Ответ поддержки:\n" + body)
                await message.reply_text("Готово.")
        except (IndexError, ValueError):
            await message.reply_text("Проверьте аргументы команды. /help — примеры.")
        except Exception as exc:
            # Never include exception text: request URLs and panel data can contain secrets.
            await message.reply_text(f"Операция не завершена ({type(exc).__name__}). Проверьте настройки панели и доступность сервера.")

    async def message(self, update: Update, context: ContextTypes.DEFAULT_TYPE):
        message, user = update.effective_message, update.effective_user
        if not message or not user or update.effective_chat.type != "private":
            return
        if self.admin(update):
            parent = message.reply_to_message
            rows = self.store.rows("SELECT t.* FROM replies r JOIN tickets t ON t.id=r.ticket_id WHERE r.chat_id=? AND r.message_id=?", (user.id, parent.message_id)) if parent else []
            if rows:
                await context.bot.copy_message(rows[0]["user_id"], user.id, message.message_id)
                await message.reply_text("Ответ отправлен.")
            else:
                await message.reply_text("Ответьте на сообщение клиента или используйте /reply НОМЕР текст.")
            return
        if self.limited((user.id, "support")):
            return
        status, fresh = self.store.register(user.id, user.full_name)
        if status == "blocked":
            await message.reply_text("Доступ закрыт владельцем.")
            return
        if fresh:
            self.store.notify(self.admins, f"Новая заявка: {user.full_name[:100]} · ID {user.id}\n/allow {user.id}")
        ticket = self.store.ticket(user.id)
        self.store.queue_support(user.id, message.message_id, ticket, self.admins)
        await message.reply_text(f"Обращение #{ticket} зарегистрировано. Поддержка ответит здесь.")


def build_application(service: Service) -> Application:
    handlers = TelegramHandlers(service)
    app = Application.builder().token(secret(service.cfg.bot_token_env)).concurrent_updates(8).build()
    commands = ["start", "help", "myid", "status", "link", "users", "allow", "deny", "check", "pool", "issue", "tickets", "reply", "close"]
    app.add_handler(CommandHandler(commands, handlers.command, filters=filters.ChatType.PRIVATE))
    app.add_handler(MessageHandler(filters.ChatType.PRIVATE & ~filters.COMMAND, handlers.message))

    async def on_error(update, context):
        logging.getLogger(__name__).error("Telegram operation failed: %s", type(context.error).__name__)

    app.add_error_handler(on_error)
    return app
