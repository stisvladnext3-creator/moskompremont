(() => {
  'use strict';
  const targets = Array.from(document.querySelectorAll('[data-service-notice]'));
  if (!targets.length) return;
  const labels = { ok: 'Сервис работает без сбоев', warning: 'В работе сервиса имеются перебои', down: 'Сервис временно недоступен', unknown: 'Статус не получен' };
  const icons = { ok: '✓', warning: '!', down: '×', unknown: '?' };
  let busy = false, latest = null;
  const active = () => !document.hidden && targets.some(node => !node.parentElement.closest('[hidden]'));
  function render(data, failed = false) {
    const level = labels[data.level] ? data.level : 'warning';
    for (const node of targets) {
      const summary = node.hasAttribute('data-service-summary'), show = failed ? summary : data.enabled;
      node.dataset.level = failed ? 'unknown' : level;
      const title = node.querySelector('[data-service-notice-title]') || node.querySelector('strong');
      if (title) title.textContent = labels[failed ? 'unknown' : level];
      const icon = node.querySelector('.service-notice-icon'); if (icon) icon.textContent = icons[failed ? 'unknown' : level];
      node.querySelector('[data-service-notice-text]').textContent = show ? failed ? 'Откройте статус сервиса для повторной проверки.' : data.message : '';
      node.hidden = !show;
    }
  }
  async function refresh() {
    if (!active() || busy) return;
    if (latest) render(latest);
    busy = true;
    const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 8000);
    try {
      const response = await fetch('api/service-notice.php', { credentials: 'omit', cache: 'no-store', signal: controller.signal });
      if (!response.ok) throw Error(); const data = await response.json();
      if (typeof data.enabled !== 'boolean' || typeof data.message !== 'string') throw Error();
      latest = data; render(data);
    } catch { latest = null; render({ enabled: false, message: '' }, true); }
    finally { clearTimeout(timeout); busy = false; }
  }
  window.MKRServiceNotice = Object.freeze({ refresh });
  setInterval(refresh, 60000); document.addEventListener('visibilitychange', refresh); refresh();
})();
