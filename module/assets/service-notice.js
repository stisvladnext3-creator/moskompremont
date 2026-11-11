(() => {
  'use strict';
  const targets = Array.from(document.querySelectorAll('[data-service-notice]'));
  if (!targets.length) return;
  let busy = false, latest = null;
  // Ignore the banner's own hidden flag so a disabled notice can appear again.
  const active = () => !document.hidden && targets.some(node => !node.parentElement.closest('[hidden]'));
  function render(data) {
    const show = data.enabled && !!data.message.trim();
    for (const node of targets) {
      node.querySelector('[data-service-notice-text]').textContent = show ? data.message : '';
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
      if (!response.ok) throw Error();
      const data = await response.json();
      if (typeof data.enabled !== 'boolean' || typeof data.message !== 'string') throw Error();
      latest = data; render(data);
    } catch {
      // Avoid displaying an old incident message after a failed refresh.
      latest = null; render({ enabled: false, message: '' });
    } finally { clearTimeout(timeout); busy = false; }
  }
  window.MKRServiceNotice = Object.freeze({ refresh });
  setInterval(refresh, 60000);
  document.addEventListener('visibilitychange', refresh);
  refresh();
})();
