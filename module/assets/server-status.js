(() => {
  'use strict';
  const target = document.getElementById('server-monitor');
  if (!target) return;
  const list = document.getElementById('server-monitor-list'), note = document.getElementById('server-monitor-note'), fallback = document.getElementById('server-monitor-fallback') || document.getElementById('servers'), refresh = document.getElementById('server-monitor-refresh');
  let busy = false, latest = null;
  const active = () => !document.hidden && !target.parentElement.closest('[hidden]');
  const labels = { up: 'Доступен', down: 'Недоступен', unknown: 'Нет свежих данных', unsupported: 'Требуется настройка' };
  function el(tag, value, className) { const node = document.createElement(tag); if (value !== undefined) node.textContent = value; if (className) node.className = className; return node; }
  function fresh(value) { if (!value) return false; const age = Date.now() - Date.parse(value.replace(' ', 'T') + 'Z'); return Number.isFinite(age) && age >= -120000 && age <= 180000; }
  function card(value) {
    const state = fresh(value.checked_at) && ['up', 'down', 'unsupported'].includes(value.status) ? value.status : 'unknown';
    const node = el('article', undefined, 'service-card monitor-card'), head = el('div', undefined, 'monitor-card-head');
    head.append(el('h3', value.name), el('span', labels[state], 'monitor-state monitor-state-' + state));node.append(head);
    if (state === 'up' && Number.isInteger(value.latency_ms) && value.latency_ms > 0) node.append(el('p', value.latency_ms + ' мс', 'monitor-latency'));
    if (value.checked_at) { const date = new Date(value.checked_at.replace(' ', 'T') + 'Z'); if (!Number.isNaN(date.getTime())) node.append(el('p', 'Проверено: ' + date.toLocaleString('ru-RU'), 'monitor-checked')); }
    return node;
  }
  function render(data) {
    if (!data.enabled) { target.hidden = true; if (fallback) fallback.hidden = false; return; }
    target.hidden = false; if (fallback) fallback.hidden = true; list.replaceChildren();
    if (data.main) list.append(card(data.main));
    for (const node of data.nodes || []) list.append(card(node));
    const errors = { fetch_error: 'Не удалось обновить список серверов.', parse_error: 'Список серверов временно недоступен.', limit_exceeded: 'Список серверов временно недоступен.' };
    note.textContent = errors[data.source_status] || (!data.nodes?.length ? 'Ожидаем первую проверку серверов.' : !fresh(data.reported_at) ? 'Нет актуальных результатов проверки.' : '');
  }
  async function load() {
    if (busy || !active()) return; busy = true; refresh.disabled = true;
    const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 10000);
    try { const response = await fetch('api/server-status.php', { credentials: 'omit', cache: 'no-store', signal: controller.signal }); if (!response.ok) throw Error(); const data = await response.json(); if (typeof data.enabled !== 'boolean' || !Array.isArray(data.nodes)) throw Error(); latest = data; render(data); }
    catch { if (latest) render(latest); target.hidden = false; note.textContent = 'Не удалось обновить доступность серверов.'; }
    finally { clearTimeout(timeout); busy = false; refresh.disabled = false; }
  }
  function refreshView() { if (!active()) return; window.MKRServiceNotice?.refresh(); if (latest) render(latest); return load(); }
  window.MKRServerStatus = Object.freeze({ refresh: refreshView });
  refresh.onclick = refreshView;
  setInterval(() => { if (active()) load(); }, 60000);
  setInterval(() => { if (active() && latest) render(latest); }, 15000);
  document.addEventListener('visibilitychange', () => { if (active()) refreshView(); });
  refreshView();
})();
