// Shared connection guides for the public page and the personal cabinet.
(() => {
  'use strict';
  const defaults = [
  {
    "title": "OneXray — первое подключение",
    "text": "1. Скачайте и установите приложение OneXray.\n2. При первом запуске выберите «RU».\n3. На этой странице нажмите «Срочная ссылка», выберите вариант и скопируйте ссылку.\n4. В OneXray откройте раздел «Серверы» в нижнем меню.\n5. Нажмите «+» в правом верхнем углу.\n6. Выберите «Добавить подписку».\n7. Укажите любое удобное название подписки, а в поле «Ссылка подписки» вставьте скопированную ссылку.\n8. Нажмите «Добавить подписку» внизу экрана.",
    "images": [
      {
        "src": "images/instructions/onexray-add-01.jpg",
        "alt": "В разделе «Серверы» OneXray выделена кнопка «+» в правом верхнем углу.",
        "caption": "Шаг 5. В разделе «Серверы» нажмите «+»."
      },
      {
        "src": "images/instructions/onexray-add-02.jpg",
        "alt": "В меню добавления серверов OneXray выделен пункт «Добавить подписку».",
        "caption": "Шаг 6. Выберите «Добавить подписку»."
      },
      {
        "src": "images/instructions/onexray-add-03.jpg",
        "alt": "Форма добавления подписки: произвольное название и поле для ссылки подписки.",
        "caption": "Шаги 7–8. Название — любое удобное. Вставьте ссылку в поле «Ссылка подписки» и нажмите «Добавить подписку»."
      }
    ],
    "cabinetText": "1. Скачайте и установите приложение OneXray.\n2. При первом запуске выберите «RU».\n3. В личном кабинете откройте «Мои подписки», выберите подписку и нажмите «Копировать».\n4. В OneXray откройте раздел «Серверы» в нижнем меню.\n5. Нажмите «+» в правом верхнем углу.\n6. Выберите «Добавить подписку».\n7. Укажите любое удобное название подписки, а в поле «Ссылка подписки» вставьте скопированную ссылку.\n8. Нажмите «Добавить подписку» внизу экрана."
  },
  {
    "title": "OneXray — обновления и источники",
    "text": "1. Перед настройкой отключите активные подключения в других сетевых приложениях на телефоне.\n2. Откройте OneXray.\n3. В разделе «Серверы» нажмите «Обновления и источники» в нижней части экрана, под списком подписок."
  },
  {
    "title": "Happ, V2raytun и Incy — подключение",
    "text": "1. Скопируйте ссылку на этой странице.\n2. Откройте приложение Happ, V2raytun или Incy.\n3. Нажмите «+» или «Импорт из буфера».\n4. Подключитесь к новому серверу.",
    "cabinetText": "1. Откройте «Мои подписки» в личном кабинете и скопируйте нужную ссылку.\n2. Откройте приложение Happ, V2raytun или Incy.\n3. Нажмите «+» или «Импорт из буфера».\n4. Подключитесь к новому серверу."
  },
  {
    "id": "v2raytun-import-clipboard",
    "includeWithCustom": true,
    "title": "V2rayTun — добавление ссылки",
    "text": "1. Скопируйте выданную ссылку.\n2. В V2rayTun нажмите «+» в правом верхнем углу.\n3. Выберите «Импорт из буфера обмена».",
    "cabinetText": "1. Откройте «Мои подписки» в личном кабинете и скопируйте выданную ссылку.\n2. В V2rayTun нажмите «+» в правом верхнем углу.\n3. Выберите «Импорт из буфера обмена»."
  },
  {
    "id": "v2raytun-remove-subscription",
    "includeWithCustom": true,
    "title": "V2rayTun — удаление подписки из приложения",
    "text": "1. В V2rayTun зажмите пальцем название группы подписки.\n2. В появившемся меню нажмите «Удалить»."
  },
  {
    "title": "Invisible Man — настройка подписки",
    "text": "1. Скопируйте ссылку на этой странице.\n2. Откройте Invisible Man и нажмите «Manage server configuration».\n3. Перейдите на вкладку «Subscription».\n4. Нажмите «+» в правом нижнем углу.\n5. В поле Remarks укажите любое удобное название подписки.\n6. В поле Subscription link вставьте скопированную ссылку.\n7. Нажмите IMPORT. Если подписок несколько, выберите добавленную в списке.\n8. Для обновления подписки нажмите кнопку, обведённую красным.\n9. Для проверки доступности сервера нажмите значок, обведённый зелёным.\n10. После выбора сервера вернитесь на главный экран и нажмите RUN для подключения.",
    "images": [
      {
        "src": "images/instructions/invisible-man-01.png",
        "alt": "Главный экран Invisible Man: стрелка указывает на Manage server configuration.",
        "caption": "Откройте Manage server configuration."
      },
      {
        "src": "images/instructions/invisible-man-02.jpg",
        "alt": "В окне Server config выделена вкладка Subscription.",
        "caption": "Перейдите на вкладку Subscription."
      },
      {
        "src": "images/instructions/invisible-man-03.jpg",
        "alt": "На вкладке Subscription стрелка указывает на кнопку добавления «+».",
        "caption": "Нажмите «+», чтобы добавить подписку."
      },
      {
        "src": "images/instructions/invisible-man-04.jpg",
        "alt": "Форма с полями Remarks и Subscription link и кнопкой IMPORT.",
        "caption": "Remarks — любое название подписки. В Subscription link вставьте ссылку, затем нажмите IMPORT."
      },
      {
        "src": "images/instructions/invisible-man-05.png",
        "alt": "Красная отметка у кнопки обновления подписки, зелёная — у проверки доступности сервера.",
        "caption": "Красным отмечено обновление подписки. Зелёным — проверка доступности сервера."
      }
    ],
    "cabinetText": "1. Откройте «Мои подписки» в личном кабинете и скопируйте нужную ссылку.\n2. Откройте Invisible Man и нажмите «Manage server configuration».\n3. Перейдите на вкладку «Subscription».\n4. Нажмите «+» в правом нижнем углу.\n5. В поле Remarks укажите любое удобное название подписки.\n6. В поле Subscription link вставьте скопированную ссылку.\n7. Нажмите IMPORT. Если подписок несколько, выберите добавленную в списке.\n8. Для обновления подписки нажмите кнопку, обведённую красным.\n9. Для проверки доступности сервера нажмите значок, обведённый зелёным.\n10. После выбора сервера вернитесь на главный экран и нажмите RUN для подключения."
  },
  {
    "id": "v2rayn-import-clipboard",
    "includeWithCustom": true,
    "title": "V2RayN — импорт подписки из буфера обмена",
    "text": "1. Скопируйте выданную ссылку-подписку.\n2. В V2RayN откройте меню «Configuration» в левом верхнем углу.\n3. Выберите «Import Share Links from clipboard» (Ctrl+V).\n4. Выберите добавленную группу «import_sub».\n5. Откройте меню «Subscription Group».\n6. Выберите «Update current subscription without proxy».",
    "cabinetText": "1. Откройте «Мои подписки» в личном кабинете и скопируйте нужную ссылку.\n2. В V2RayN откройте меню «Configuration» в левом верхнем углу.\n3. Выберите «Import Share Links from clipboard» (Ctrl+V).\n4. Выберите добавленную группу «import_sub».\n5. Откройте меню «Subscription Group».\n6. Выберите «Update current subscription without proxy»."
  },
  {
    "title": "V2RayN — Windows и macOS",
    "text": "1. Для обновления списка серверов откройте Subscription Group, отмеченное зелёным, и выберите «update current subscription without proxy».\n2. Для проверки доступности серверов нажмите кнопку, отмеченную синим.\n3. Для выбора профиля маршрутизации откройте список, отмеченный красным.\n\nПрофили маршрутизации:\n• Для отдельных сервисов — через выбранный сервер направляются только нужные сервисы. Рекомендуется использовать этот профиль.\n• Весь трафик через сервер — отдельные российские сервисы, например налоговые, почта и Госуслуги, могут работать некорректно.\n• Иностранный трафик через сервер — российские сервисы открываются напрямую, остальной трафик идёт через выбранный сервер. Скорость может быть ниже.\n\nРасположение элементов и названия профилей могут отличаться в зависимости от версии приложения и системы. Работа сайтов и скорость зависят от выбранного сервера и профиля.",
    "images": [
      {
        "src": "images/instructions/v2rayn-01.png",
        "alt": "V2RayN: зелёным выделено меню подписки, синим — проверка серверов, красным — профиль маршрутизации.",
        "caption": "Зелёная отметка — обновление подписки; синяя — проверка доступности серверов; красная — настройка маршрутизации.",
        "wide": true
      }
    ]
  }
];
  function element(tag, text, className) {
    const node = document.createElement(tag);
    if (text !== undefined && text !== null) node.textContent = String(text);
    if (className) node.className = className;
    return node;
  }
  function chevron() {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'icon instruction-chevron');
    svg.setAttribute('width', '20'); svg.setAttribute('height', '20'); svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('fill', 'none'); svg.setAttribute('stroke', 'currentColor'); svg.setAttribute('stroke-width', '2'); svg.setAttribute('aria-hidden', 'true');
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', 'm6 9 6 6 6-6'); svg.append(path); return svg;
  }
  function render(container, configured, options = {}) {
    if (!container) return;
    const items = Array.isArray(configured) && configured.length
      ? configured.concat(defaults.filter(item => item.includeWithCustom && !configured.some(value => value && (value.id === item.id || value.title === item.title))))
      : defaults;
    const nodes = [];
    for (const item of items) {
      if (!item || typeof item.title !== 'string') continue;
      const details = element('details', undefined, 'accordion-item instruction-details');
      if (typeof item.id === 'string') details.dataset.guideId = item.id;
      const summary = element('summary', undefined, 'accordion-header instruction-summary');
      summary.append(element('span', item.title), chevron());
      const body = element('div', undefined, 'instruction-body');
      const text = options.cabinet && typeof item.cabinetText === 'string' ? item.cabinetText : item.text;
      if (text) body.append(element('p', text));
      if (Array.isArray(item.images)) {
        const gallery = element('div', undefined, 'instruction-gallery');
        item.images.forEach((value, index) => {
          const picture = typeof value === 'string' ? { src: value } : value;
          if (!picture || typeof picture.src !== 'string' || !picture.src.trim()) return;
          let url;
          try {
            url = new URL(picture.src, location.href);
            if (url.origin !== location.origin || !['http:', 'https:'].includes(url.protocol) || url.username || url.password) return;
          } catch { return; }
          const figure = element('figure', undefined, 'instruction-figure' + (picture.wide ? ' instruction-figure-wide' : ''));
          const link = element('a', undefined, 'instruction-image-link');
          link.href = url.href; link.target = '_blank'; link.rel = 'noopener noreferrer';
          link.setAttribute('aria-label', 'Открыть скриншот ' + (index + 1) + ' к инструкции «' + item.title + '» в полном размере');
          const image = element('img', undefined, picture.wide ? 'instruction-image instruction-image-wide' : 'instruction-image');
          image.alt = picture.alt || 'Скриншот к инструкции «' + item.title + '», ' + (index + 1);
          image.loading = 'lazy'; image.decoding = 'async';
          const unavailable = element('p', 'Изображение пока недоступно. Ориентируйтесь на шаги выше.', 'instruction-image-error');
          unavailable.hidden = true;
          image.addEventListener('error', () => { link.hidden = true; unavailable.hidden = false; });
          link.append(image); figure.append(link, unavailable);
          if (picture.caption) figure.append(element('figcaption', picture.caption, 'instruction-caption'));
          gallery.append(figure); image.src = url.href;
        });
        if (gallery.children.length) body.append(gallery);
      }
      details.append(summary, body); nodes.push(details);
    }
    if (nodes.length) container.replaceChildren(...nodes);
  }
  window.MKRInstructions = Object.freeze({ render });
})();
