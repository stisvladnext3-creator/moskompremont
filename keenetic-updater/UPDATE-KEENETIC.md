# Исправление Keenetic/XKeen 0.1.0 → 0.1.1

В Entware встречается jq, собранный без Oniguruma. В 0.1.0 это вызывало сообщение `jq was compiled without ONIGURUMA regex library`, а затем ошибку адреса API ещё до ввода кода. Версия 0.1.1 проверяет адрес, код устройства и SHA256 без регулярных выражений jq. Менять jq на роутере не требуется.

## На сайте

Распакуйте **moskom-keenetic-repair-0.1.1.zip** в папку сайта, где находится `account.html`, с объединением папки `downloads`. В архиве только:

```text
downloads/keenetic/install.sh
downloads/keenetic/0.1.1/mkr-xkeen-updater_0.1.1-1_all.ipk
downloads/keenetic/0.1.1/SHA256SUMS.txt
UPDATE-KEENETIC.md
```

Миграция базы и замена файлов кабинета для этого исправления не нужны. Установщик и IPK из нового архива нужно загрузить вместе: установщик проверяет SHA256 именно нового IPK.

## На роутере

В SSH Entware выполните:

```sh
curl --proto '=https' --proto-redir '=https' -fL --connect-timeout 15 --max-time 90 'https://moskompremont.ru/downloads/keenetic/install.sh?v=0.1.1' -o /tmp/mkr-keenetic-install.sh && sh /tmp/mkr-keenetic-install.sh 'https://moskompremont.ru/api/router-agent.php'
```

При размещении сайта в подпапке используйте её в обоих адресах. Параметр `?v=0.1.1` относится только к загрузке установщика; адрес API передаётся без параметров.

Дождитесь строки **«Код устройства (ввод скрыт):»**, вставьте код из вкладки Keenetic в кабинете и нажмите Enter. Во время ввода символы кода не отображаются. Код вводится именно в этом запросе установщика; отдельной команды с кодом выполнять не требуется.

После установки:

```sh
mkr-xkeen version
mkr-xkeen status
```

Версия должна быть **0.1.1**. При повторном запуске установщика пакет 0.1.0 обновляется штатным `opkg install`; принудительная переустановка и отключение проверок не используются. Настройки выбора файла/блока, режим обновления и резервные копии сохраняются.
