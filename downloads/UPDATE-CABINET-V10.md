# Обновление v10 — скриншоты V2RayN и V2rayTun

Все 8 изображений из `instructions.rar` добавлены к трём руководствам на главной странице и во вкладке «Инструкции» личного кабинета:

- **V2RayN — импорт подписки из буфера обмена:** 4 скриншота — Configuration, Import Share Links from clipboard, import_sub / Subscription Group, Update current subscription without proxy.
- **V2rayTun — добавление ссылки:** 2 скриншота — «+» и «Импорт из буфера обмена».
- **V2rayTun — удаление подписки из приложения:** 2 скриншота — название группы для долгого нажатия и пункт «Удалить».

Изображения расположены по порядку действий. Под каждым есть подпись с номером шага; нажатие открывает оригинал в полном размере в новой вкладке. Файлы из архива сохранены без изменения изображения, включая ваши отметки. Скриншоты V2RayN занимают ширину инструкции; мобильные изображения адаптируются к экрану.

В стандартном каталоге по-прежнему 8 руководств, теперь с 17 скриншотами. Прежние инструкции и изображения сохранены. При пользовательском тексте с тем же ID/заголовком новые скриншоты подставляются автоматически, если собственный массив `images` не задан. Явный `images: []` сохраняет отсутствие изображений по выбору владельца.

## Установка поверх v8 или v9

1. Сделайте копию файлов сайта.
2. Распакуйте `moskompremont-cabinet-v10-update.zip` и загрузите содержимое в папку с `account.html` и `internet.html`, с заменой одноимённых файлов.
3. Обязательно загрузите папку **images/instructions** вместе с **assets/instructions.js**. Только замена HTML не добавит изображения.
4. Свои `private/config.php`, `assets/config.js` и QR-код оставьте на сервере: архив обновления их не содержит.
5. Обновите страницу через Ctrl+F5 и откройте инструкции V2RayN/V2rayTun. Убедитесь, что изображения видны и открываются по нажатию.

**Для установленной v8 или v9 импорт SQL и переустановка службы Ubuntu не требуются.** Серверный код и схема базы не менялись. Мониторинг, клиенты, удаление ключей, очередь писем, оплаты и поддержка используют существующие настройки.

Если установлена v5–v7, импортируйте `private/migrations/005-monitoring.sql`. Для более ранней версии или новой установки используйте полный `private/schema.sql`. Установка службы и настройка почты — в `MONITORING-SETUP.md`; служба остаётся версии v8.

## Файлы изображений

- `images/instructions/v2rayn-import-01.jpg` — Configuration.
- `images/instructions/v2rayn-import-02.jpg` — Import Share Links from clipboard.
- `images/instructions/v2rayn-import-03.jpg` — import_sub и Subscription Group.
- `images/instructions/v2rayn-import-04.jpg` — Update current subscription without proxy.
- `images/instructions/v2raytun-import-01.jpg` — кнопка «+».
- `images/instructions/v2raytun-import-02.jpg` — импорт из буфера обмена.
- `images/instructions/v2raytun-remove-01.jpg` — название группы подписки.
- `images/instructions/v2raytun-remove-02.jpg` — удаление через контекстное меню.

Общий каталог обеих страниц — `assets/instructions.js`; `INSTRUCTIONS.json` — копия каталога для просмотра. При изменении содержания используйте общий JS-каталог либо `SITE_CONFIG.instructions` в своём `assets/config.js`.
