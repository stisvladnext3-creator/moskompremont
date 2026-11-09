(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  if (!$('auth-section')) return;
  let csrf = '', user = null, payment = { available: false }, mode = 'client';
  let clientTab = 'subscriptions', adminTab = 'support', conversation = null;
  let assignmentUsersLoaded = false; const assignmentUsers = new Map();
  const pages = { users: 0, payments: 0, subscriptions: 0 };
  const orderStatus = { created: 'Ожидает оплаты', submitted: 'На ручной проверке', paid: 'Оплата подтверждена', rejected: 'Оплата не подтверждена' };
  const claimStatus = { pending: 'Ожидает ссылки или проверки', approved: 'Подписка добавлена', rejected: 'Запрос отклонён', revoked: 'Привязка снята администратором' };
  const money = n => new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB', maximumFractionDigits: 0 }).format(Number(n));
  const date = value => value ? new Date(value.replace(' ', 'T') + 'Z').toLocaleString('ru-RU') : '';
  const el = (tag, text, className) => { const node = document.createElement(tag); if (text !== undefined && text !== null) node.textContent = text; if (className) node.className = className; return node; };
  function message(text, error = false) { $('account-message').textContent = text; $('account-message').dataset.error = String(error); }
  function button(text, fn, className = 'account-secondary') { const b = el('button', text, className); b.type = 'button'; b.addEventListener('click', () => run(b, fn)); return b; }
  async function run(b, fn) { message(''); if (b) b.disabled = true; try { await fn(); } catch (e) { message(e.message, true); $('account-message').scrollIntoView({ behavior: 'smooth', block: 'nearest' }); } finally { if (b && b.isConnected) b.disabled = false; } }
  async function api(action, data, params = {}) {
    const multipart = data instanceof FormData;
    const response = await fetch('api/account.php?' + new URLSearchParams({ action, ...params }), { method: data === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store', headers: data === undefined ? {} : { ...(multipart ? {} : { 'Content-Type': 'application/json' }), 'X-CSRF-Token': csrf }, body: data === undefined ? undefined : multipart ? data : JSON.stringify(data) });
    let result; try { result = await response.json(); } catch { throw Error(user?.is_admin ? 'Сервер вернул некорректный ответ (HTTP ' + response.status + '). Проверьте журнал ошибок PHP на хостинге.' : 'Не удалось обработать ответ сервера. Обновите страницу или напишите в поддержку.'); }
    if (!response.ok) { if (response.status === 401 && user) signedOut(); const e = Error(result.error || 'Ошибка запроса'); e.status = response.status; throw e; }
    if (result.csrf) csrf = result.csrf; return result;
  }
  function authTab(register) { $('login-form').hidden = register; $('register-form').hidden = !register; $('login-tab').setAttribute('aria-pressed', String(!register)); $('register-tab').setAttribute('aria-pressed', String(register)); message(''); }
  $('login-tab').onclick = () => authTab(false); $('register-tab').onclick = () => authTab(true);
  $('has-subscriptions').onchange = e => { $('registration-subscriptions').hidden = !e.target.checked; $('existing-subscriptions').disabled = !e.target.checked; $('existing-subscriptions').required = e.target.checked; };
  for (const action of ['login', 'register']) $(action + '-form').onsubmit = event => {
    event.preventDefault(); run(event.submitter, async () => {
      const data = Object.fromEntries(new FormData(event.target)); if (action === 'register') data.has_existing_subscriptions = $('has-subscriptions').checked;
      const result = await api(action, data); user = result.user; event.target.reset(); $('registration-subscriptions').hidden = true; $('existing-subscriptions').disabled = true; $('existing-subscriptions').required = false;
      await showCabinet(); message(action === 'register' ? 'Аккаунт создан. Найденные подписки уже в разделе «Мои подписки».' : 'Вы вошли в кабинет.');
    });
  };
  function signedOut() {
    user = null; conversation = null; $('auth-section').hidden = false; $('cabinet-section').hidden = true; $('conversation').hidden = true;
    for (const id of ['subscriptions-list', 'claims-list', 'orders-list', 'tickets-list', 'admin-tickets', 'admin-users', 'admin-orders', 'admin-subscriptions', 'admin-claims', 'conversation-messages']) $(id).replaceChildren();
    document.querySelectorAll('#cabinet-section form').forEach(form => form.reset()); $('reply-form').reset(); assignmentUsers.clear(); assignmentUsersLoaded = false; $('assignment-user').replaceChildren(new Option('Выберите клиента', '')); resetDirectRows();
  }
  $('logout-button').onclick = e => run(e.target, async () => { await api('logout', {}); signedOut(); message('Вы вышли из кабинета.'); });
  async function showCabinet() {
    $('auth-section').hidden = !!user; $('cabinet-section').hidden = !user; if (!user) return;
    $('profile-name').textContent = user.first_name + ' ' + user.last_name; $('profile-contact').textContent = user.email + ' · ' + user.telegram;
    $('client-mode').hidden = !user.is_admin; $('admin-mode').hidden = !user.is_admin;
    $('payment-unavailable').hidden = payment.available; $('order-form').querySelector('button').disabled = !payment.available;
    mode = user.is_admin ? 'admin' : 'client'; if (location.hash === '#payment') { mode = 'client'; clientTab = 'payments'; }
    await setMode(mode); await updateSummary();
  }
  async function setMode(value) { mode = value; $('client-shell').hidden = value !== 'client'; $('admin-shell').hidden = value !== 'admin'; closeConversation(); if (value === 'admin') await selectAdmin(adminTab); else await selectClient(clientTab); }
  $('client-mode').onclick = e => run(e.target, () => setMode('client')); $('admin-mode').onclick = e => run(e.target, () => setMode('admin'));
  async function selectClient(value) { clientTab = value; closeConversation(); document.querySelectorAll('[data-client-tab]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.clientTab === value))); document.querySelectorAll('[data-client-panel]').forEach(p => p.hidden = p.dataset.clientPanel !== value); if (value === 'subscriptions') await loadSubscriptions(); if (value === 'payments') await loadOrders(); if (value === 'support') await loadTickets(false); }
  document.querySelectorAll('[data-client-tab]').forEach(b => b.onclick = () => run(b, () => selectClient(b.dataset.clientTab)));
  async function selectAdmin(value) { adminTab = value; closeConversation(); document.querySelectorAll('[data-admin-tab]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.adminTab === value))); document.querySelectorAll('[data-admin-panel]').forEach(p => p.hidden = p.dataset.adminPanel !== value); await loadAdmin(value); }
  document.querySelectorAll('[data-admin-tab]').forEach(b => b.onclick = () => run(b, () => selectAdmin(b.dataset.adminTab)));
  document.querySelectorAll('[data-admin-refresh]').forEach(b => b.onclick = () => run(b, () => loadAdmin(b.dataset.adminRefresh)));
  function linkRow(url, name = 'подписка') {
    const row = el('div', null, 'link-row'), input = el('input'); input.value = url; input.readOnly = true; input.setAttribute('aria-label', 'Ссылка ' + name); row.append(input);
    const copy = button('Копировать', async () => { try { await navigator.clipboard.writeText(url); copy.textContent = 'Скопировано ✓'; setTimeout(() => copy.textContent = 'Копировать', 2000); } catch { input.focus(); input.select(); message('Скопируйте выделенную ссылку.'); } }); row.append(copy);
    if (/^(https?|vless|vmess|trojan|ss|hysteria2):\/\//i.test(url)) { const a = el('a', 'Открыть'); a.href = url; a.target = '_blank'; a.rel = 'noopener noreferrer'; row.append(a); } return row;
  }
  function subscriptionCard(sub) { const card = el('article', null, 'subscription-card'); card.append(el('h3', sub.name), linkRow(sub.url, sub.name)); return card; }
  async function loadSubscriptions() {
    const data = await api('subscriptions'); $('subscriptions-list').replaceChildren(); $('claims-list').replaceChildren();
    if (!data.subscriptions.length) $('subscriptions-list').append(el('p', 'У вас пока нет выданных подписок. Укажите имеющиеся названия ниже или выберите тариф.', 'account-muted'));
    data.subscriptions.forEach(s => $('subscriptions-list').append(subscriptionCard(s)));
    for (const claim of data.claims.filter(c => c.status !== 'approved')) { const box = el('div', null, 'record'); box.append(el('h3', claim.name), el('span', claimStatus[claim.status] || claim.status, 'status-badge')); $('claims-list').append(box); }
  }
  $('refresh-subscriptions').onclick = e => run(e.target, loadSubscriptions);
  $('claim-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => { await api('claim_subscriptions', { names: e.target.elements.names.value }); e.target.reset(); await loadSubscriptions(); message('Запрос обработан. Найденные подписки добавлены в кабинет.'); }); };
  function total() { const form = $('order-form'), count = Number(form.elements.devices.value), monthly = form.elements.plan.value === 'monthly', rates = monthly ? [250, 225, 200, 175] : [3000, 2500, 2000, 1650]; $('order-total').textContent = Number.isInteger(count) && count >= 1 && count <= 20 ? money(rates[Math.min(count, 4) - 1] * count) + (monthly ? ' / месяц' : ' разово') : 'Укажите от 1 до 20 устройств'; }
  $('order-form').oninput = total; total();
  $('order-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => { await api('create_order', { plan: e.target.elements.plan.value, devices: Number(e.target.elements.devices.value) }); await loadOrders(); message('Заявка создана. Оплатите по QR-коду ниже.'); $('orders-list').scrollIntoView({ behavior: 'smooth' }); }); };
  function orderCard(order) { const box = el('article', null, 'account-order'); box.append(el('h3', (order.plan === 'monthly' ? 'Ежемесячно' : 'Навсегда') + ' · ' + order.devices + ' устр. · ' + money(order.amount)), el('p', 'Заявка ' + order.id, 'account-muted'), el('span', orderStatus[order.status] || order.status, 'status-badge status-' + order.status)); return box; }
  async function loadOrders() {
    const data = await api('orders'); $('orders-list').replaceChildren(); if (!data.orders.length) $('orders-list').append(el('p', 'У вас пока нет заявок.', 'account-muted'));
    for (const order of data.orders) {
      const box = orderCard(order);
      if (order.status === 'created' && payment.available) {
        box.append(el('p', 'К оплате: ' + money(order.amount))); const image = el('img'); image.src = payment.qr_image; image.alt = 'QR-код оплаты в Озон Банке'; image.className = 'payment-qr'; box.append(image);
        const a = el('a', 'Открыть QR-код в отдельной вкладке'); a.href = payment.qr_image; a.target = '_blank'; a.rel = 'noopener'; box.append(a, el('p', 'Получатель: ' + payment.recipient), el('p', 'Проверьте получателя и введите точную сумму. ' + payment.note, 'account-note'));
        const form = el('form'), label = el('label', 'Данные перевода для сверки'), input = el('input'); input.name = 'reference'; input.required = true; input.minLength = 4; input.maxLength = 500; input.placeholder = 'Например: 15:20, телефон 3015'; label.append(input);
        const submit = el('button', 'Я оплатил — отправить на проверку', 'account-primary'); submit.type = 'submit'; form.append(label, submit); form.onsubmit = e => { e.preventDefault(); run(submit, async () => { await api('submit_payment', { id: order.id, reference: input.value }); await loadOrders(); message('Вы сообщили об оплате. Администратору создано уведомление; после проверки он выдаст подписку.'); }); }; box.append(form);
      }
      if (order.status === 'submitted') box.append(el('p', 'Ожидайте проверки поступления. Повторно платить не нужно.', 'account-muted'));
      if (order.status === 'paid' && !order.subscriptions.length) box.append(el('p', order.revoked_subscriptions?.length ? 'Привязка подписки снята администратором. История оплаты сохранена.' : 'Оплата подтверждена. Ожидайте выдачи подписки администратором.', 'account-note'));
      for (const sub of order.subscriptions) box.append(subscriptionCard(sub));
      if (order.status === 'rejected') box.append(el('p', 'Свяжитесь с поддержкой для сверки перевода перед повторной оплатой.', 'account-muted'));
      $('orders-list').append(box);
    }
  }
  $('refresh-orders').onclick = e => run(e.target, loadOrders);
  $('ticket-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => { const data = await api('create_ticket', new FormData(e.target)); e.target.reset(); await loadTickets(false); await openConversation(data.id, false); message('Обращение отправлено. Администратору создано уведомление и поставлено письмо в очередь.'); }); };
  async function loadTickets(asAdmin) {
    const data = await api(asAdmin ? 'admin_tickets' : 'tickets'), target = $(asAdmin ? 'admin-tickets' : 'tickets-list'); target.replaceChildren();
    if (!data.tickets.length) target.append(el('p', 'Обращений пока нет.', 'account-muted'));
    for (const ticket of data.tickets) { const box = el('article', null, 'record' + (Number(ticket.unread) ? ' record-new' : '')); box.append(el('h3', '#' + ticket.id + ' · ' + ticket.subject), el('p', (ticket.status === 'closed' ? 'Решено' : 'Открыто') + ' · ' + date(ticket.updated_at), 'account-muted')); if (asAdmin) box.append(el('p', ticket.first_name + ' ' + ticket.last_name + ' · ' + ticket.email), el('p', ticket.telegram, 'account-muted')); if (Number(ticket.unread)) box.append(el('p', 'Новых сообщений: ' + ticket.unread, 'account-kicker')); box.append(button('Открыть переписку', () => openConversation(ticket.id, asAdmin))); target.append(box); }
  }
  $('refresh-tickets').onclick = e => run(e.target, () => loadTickets(false));
  const photoPreviews = new WeakMap();
  function showSelectedPhotos(form) {
    const input = form.querySelector('input[name="photos[]"]'), target = form.querySelector('.selected-photos'); if (!input || !target) return;
    for (const url of photoPreviews.get(form) || []) URL.revokeObjectURL(url); photoPreviews.set(form, []); target.replaceChildren(); input.setCustomValidity('');
    const files = Array.from(input.files); form.elements.message.required = files.length === 0;
    let error = files.length > 3 ? 'Можно прикрепить до 3 фотографий.' : '';
    for (const file of files) { if (file.size > 5 * 1024 * 1024) error = 'Размер одного фото — до 5 МБ.'; if (file.type && !['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) error = 'Поддерживаются JPEG, PNG и WebP.'; }
    if (error) { input.setCustomValidity(error); target.append(el('p', error, 'photo-error')); }
    else for (const file of files) { const url = URL.createObjectURL(file); photoPreviews.get(form).push(url); const box = el('div', null, 'selected-photo'), image = el('img'); image.src = url; image.alt = 'Выбранное фото'; box.append(image, el('span', file.name)); target.append(box); }
    if (files.length) target.append(button('Убрать выбранные фото', async () => { input.value = ''; showSelectedPhotos(form); }));
  }
  for (const form of [$('ticket-form'), $('reply-form')]) { form.querySelector('input[name="photos[]"]').addEventListener('change', () => showSelectedPhotos(form)); form.addEventListener('reset', () => queueMicrotask(() => showSelectedPhotos(form))); }

  function closeConversation() { conversation = null; $('conversation').hidden = true; $('conversation-messages').replaceChildren(); $('reply-form').reset(); }
  $('close-conversation').onclick = closeConversation;
  async function openConversation(id, asAdmin) { conversation = { id, asAdmin, lastId: null }; $('reply-form').reset(); await loadConversation(); $('conversation').scrollIntoView({ behavior: 'smooth', block: 'start' }); }
  async function loadConversation() {
    if (!conversation) return; const active = conversation, data = await api('read_ticket', { id: active.id, as_admin: active.asAdmin }); if (conversation !== active) return;
    $('conversation').hidden = false; $('conversation-title').textContent = '#' + data.ticket.id + ' · ' + data.ticket.subject; $('conversation-meta').textContent = (data.ticket.status === 'closed' ? 'Обращение решено' : 'Обращение открыто') + ' · последние 200 сообщений'; $('resolve-ticket').hidden = !active.asAdmin || data.ticket.status === 'closed';
    const last = data.messages.length ? data.messages[data.messages.length - 1].id : 0;
    if (active.lastId !== last) { const target = $('conversation-messages'), atBottom = active.lastId === null || target.scrollTop + target.clientHeight >= target.scrollHeight - 80; target.replaceChildren(); for (const item of data.messages) { const staff = Number(item.is_admin) === 1, box = el('div', null, 'support-message' + (staff ? ' staff' : '')); box.append(el('div', (staff ? 'Поддержка' : item.first_name + ' ' + item.last_name) + ' · ' + date(item.created_at), 'message-author'), el('div', item.body));
      if (item.photos?.length) { const gallery = el('div', null, 'support-photos'); for (const photo of item.photos) { const a = el('a', null, 'support-photo'); a.href = photo.url; a.target = '_blank'; a.rel = 'noopener noreferrer'; const image = el('img'); image.src = photo.url; image.alt = 'Фото к сообщению: ' + photo.file_name; image.loading = 'lazy'; a.append(image, el('span', 'Открыть фото')); gallery.append(a); } box.append(gallery); }
      target.append(box); } if (atBottom) target.scrollTop = target.scrollHeight; active.lastId = last; }
    await updateSummary();
  }
  $('reply-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => { const active = conversation; if (!active) return; const data = new FormData(e.target); data.set('id', active.id); data.set('as_admin', active.asAdmin ? '1' : '0'); await api('reply_ticket', data); e.target.reset(); if (conversation === active) await loadConversation(); await loadTickets(active.asAdmin); message('Сообщение отправлено.'); }); };
  $('resolve-ticket').onclick = e => run(e.target, async () => { if (!conversation) return; await api('set_ticket_status', { id: conversation.id, status: 'closed' }); await loadConversation(); await loadTickets(true); message('Обращение отмечено как решённое.'); });
  function badge(node, value) { if (!node) return; node.hidden = !Number(value); node.textContent = Number(value) > 99 ? '99+' : value; }
  async function updateSummary() {
    if (!user) return; const summary = await api('summary'); badge($('support-unread'), summary.unread_support);
    if (user.is_admin) { const data = await api('admin_summary'); Object.entries(data.unread).forEach(([type, count]) => document.querySelectorAll('[data-count="' + type + '"]').forEach(node => badge(node, count))); const m = data.mail; $('mail-status').textContent = 'Получатель: ' + m.recipient + '\n' + (m.ready ? 'Отправка настроена.' : 'Отправка не настроена. Заполните раздел mail в private/config.php.') + '\nПринято почтовым сервером: ' + m.sent + '. В очереди: ' + m.pending + '. Ошибок после повторов: ' + m.failed + '.'; $('mail-status').style.whiteSpace = 'pre-line'; $('mail-status').classList.toggle('mail-warning', !m.ready || m.failed > 0); }
  }
  function pager(id, key, count, total) { const target = $(id); target.replaceChildren(); const box = el('div', null, 'pager'); const prev = button('← Назад', async () => { pages[key]--; await loadAdmin(key); }); prev.disabled = pages[key] === 0; const next = button('Далее →', async () => { pages[key]++; await loadAdmin(key); }); next.disabled = total === undefined ? count < 100 : (pages[key] + 1) * 100 >= total; box.append(prev, el('span', 'Страница ' + (pages[key] + 1)), next); target.append(box); }
  function readButton(record, reload) { return button('Отметить прочитанным', async () => { await api('read_event', { id: record.event_id }); await reload(); await updateSummary(); }); }
  async function loadAdmin(value) { if (value === 'support') await loadTickets(true); if (value === 'users') await loadUsers(); if (value === 'payments') await loadAdminOrders(); if (value === 'subscriptions') await loadAdminSubscriptions(); await updateSummary(); }
  async function loadUsers() {
    const data = await api('admin_users', undefined, { page: pages.users }), target = $('admin-users'); target.replaceChildren();
    for (const item of data.users) { const fresh = item.event_id && !item.event_read_at, box = el('article', null, 'record' + (fresh ? ' record-new' : '')); box.append(el('h3', '#' + item.id + ' · ' + item.first_name + ' ' + item.last_name), el('p', 'Почта: ' + item.email), el('p', 'Telegram: ' + item.telegram), el('p', 'Регистрация: ' + date(item.created_at), 'account-muted'), el('p', 'Указал подписки: ' + (item.claims.map(c => c.name).join(', ') || 'нет')), el('p', 'Выданы: ' + (item.subscriptions.map(s => s.name).join(', ') || 'пока нет'), 'account-muted')); if (fresh) box.append(readButton(item, loadUsers)); box.append(button('Назначить подписки', () => chooseDirectRecipient(item))); target.append(box); }
    pager('users-pager', 'users', data.users.length);
  }
  async function loadAdminOrders() {
    const data = await api('admin_orders', undefined, { page: pages.payments }), target = $('admin-orders'); target.replaceChildren(); if (!data.orders.length) target.append(el('p', 'Заявок пока нет.', 'account-muted'));
    for (const order of data.orders) {
      const box = orderCard(order); if (order.event_id && !order.event_read_at) box.classList.add('record-new'); box.append(el('p', order.first_name + ' ' + order.last_name + ' · ' + order.email), el('p', order.telegram, 'account-muted')); if (order.payment_reference) box.append(el('p', 'Данные перевода: ' + order.payment_reference));
      if (order.event_id && !order.event_read_at) box.append(readButton(order, loadAdminOrders));
      if (order.status === 'submitted') { const actions = el('div', null, 'order-buttons'); for (const [status, title] of [['paid', 'Подтвердить поступление'], ['rejected', 'Отклонить']]) actions.append(button(title, async () => { if (!confirm(status === 'paid' ? 'Вы проверили поступление полной суммы в Озон Банке?' : 'Отклонить заявку после сверки перевода?')) return; await api('review_order', { id: order.id, status }); await loadAdminOrders(); await updateSummary(); message(status === 'paid' ? 'Оплата подтверждена. Теперь выдайте подписку в этой заявке.' : 'Заявка отклонена.'); }, status === 'paid' ? 'account-primary' : 'account-secondary')); box.append(actions); }
      for (const sub of order.subscriptions) box.append(subscriptionCard(sub));
      if (order.status === 'paid') {
        const details = el('details', null, 'issue-form'); details.open = !order.subscriptions.length; details.append(el('summary', order.subscriptions.length ? 'Выдать ещё одну подписку' : 'Выдать подписку клиенту'));
        const form = el('form'), nameLabel = el('label', 'Название подписки'), name = el('input'); name.name = 'name'; name.required = true; name.maxLength = 100; name.placeholder = 'VPN5089'; nameLabel.append(name);
        const urlLabel = el('label', 'Подписочная ссылка'), url = el('textarea'); url.name = 'url'; url.rows = 2; url.maxLength = 8192; url.placeholder = 'Вставьте ссылку или оставьте пустым для выбора из базы по названию'; urlLabel.append(url);
        const submit = el('button', 'Выдать подписку', 'account-primary'); submit.type = 'submit'; form.append(nameLabel, urlLabel, submit); form.onsubmit = e => { e.preventDefault(); run(submit, async () => { await api('issue_subscription', { order_id: order.id, name: name.value, url: url.value }); await loadAdminOrders(); message('Подписка выдана. Ссылка доступна клиенту в кабинете.'); }); }; details.append(form); box.append(details);
      }
      target.append(box);
    }
    pager('payments-pager', 'payments', data.orders.length);
  }
  $('import-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => { const file = e.target.elements.file.files[0]; if (!file || file.size > 1048576) throw Error('Выберите CSV размером до 1 МБ.'); const result = await api('import_subscriptions', { csv: await file.text() }); e.target.reset(); await loadAdminSubscriptions(); message('Импорт завершён. Добавлено: ' + result.added + ', обновлено: ' + result.updated + ', привязано к клиентам: ' + result.assigned + '.'); }); };
  $('subscription-search').onsubmit = e => { e.preventDefault(); pages.subscriptions = 0; run(e.submitter, loadAdminSubscriptions); };
  async function loadAdminSubscriptions() {
    if (!assignmentUsersLoaded) await loadAssignmentUsers();
    const data = await api('admin_subscriptions', undefined, { page: pages.subscriptions, q: $('subscription-query').value }), target = $('admin-subscriptions'); target.replaceChildren();
    if (!data.subscriptions.length) target.append(el('p', 'Подписок пока нет. Загрузите CSV-файл выше.', 'account-muted'));
    for (const sub of data.subscriptions) { const box = subscriptionCard(sub); box.append(el('p', sub.user_id ? 'Клиент: ' + sub.first_name + ' ' + sub.last_name + ' · ' + sub.email : 'Свободна — ещё не привязана к клиенту', 'account-muted'));
      if (sub.user_id) box.append(button('Снять привязку', async () => { if (!confirm('Снять привязку ' + sub.name + ' у клиента ' + sub.email + '? Ссылка исчезнет из его кабинета, подписка останется в базе, история оплат сохранится.')) return; await api('unassign_subscription', { id: Number(sub.id), user_id: Number(sub.user_id) }); await loadAdminSubscriptions(); message('Привязка снята. Подписка свободна, история оплат сохранена.'); }, 'account-secondary account-danger'));
      if (!sub.user_id) box.append(button('Назначить клиенту', async () => { const input = $('direct-assignment-form').elements.names; const names = input.value.split(/[\s,;]+/).filter(Boolean); if (!names.some(name => name.toUpperCase() === sub.name.toUpperCase())) names.push(sub.name); input.value = names.join(', '); $('direct-assignment').scrollIntoView({ behavior: 'smooth', block: 'start' }); if (!$('assignment-user').value) $('assignment-user').focus(); else input.focus(); }));
      target.append(box); }
    pager('subscriptions-pager', 'subscriptions', data.subscriptions.length, data.total); $('admin-claims').replaceChildren(); if (!data.claims.length) $('admin-claims').append(el('p', 'Нет запросов, ожидающих обработки.', 'account-muted'));
    for (const claim of data.claims) { const box = el('article', null, 'record'); box.append(el('h3', claim.name), el('p', claimStatus[claim.status] || claim.status, 'account-muted'), el('p', claim.first_name + ' ' + claim.last_name + ' · ' + claim.email), el('p', claim.telegram, 'account-muted')); const actions = el('div', null, 'order-buttons'); for (const [status, title] of [['approved', 'Привязать клиенту'], ['rejected', 'Отклонить']]) actions.append(button(title, async () => { await api('resolve_claim', { id: Number(claim.id), status }); await loadAdminSubscriptions(); message('Запрос обработан.'); })); box.append(actions); $('admin-claims').append(box); }
  }
  async function loadAssignmentUsers(query = '', selectedId = 0) {
    const select = $('assignment-user'); select.disabled = true; if (!selectedId) select.value = '';
    try {
      const data = await api('admin_user_search', undefined, { q: query }); select.replaceChildren(new Option('Выберите клиента', ''));
      for (const item of data.users) { assignmentUsers.set(Number(item.id), item); select.append(new Option('#' + item.id + ' · ' + item.first_name + ' ' + item.last_name + ' · ' + item.email, String(item.id))); }
      select.value = selectedId ? String(selectedId) : ''; assignmentUsersLoaded = true;
      $('assignment-user-hint').textContent = data.users.length >= data.limit ? 'Показаны первые ' + data.limit + ' клиентов. Уточните поиск по почте, имени или Telegram.' : data.users.length ? 'Найдено клиентов: ' + data.users.length + '. Проверьте имя и почту получателя.' : 'Клиент не найден. Проверьте поиск или дождитесь его регистрации.';
    } finally { select.disabled = false; }
  }
  async function chooseDirectRecipient(item) {
    await selectAdmin('subscriptions'); $('assignment-user-query').value = item.email; await loadAssignmentUsers(item.email, Number(item.id));
    $('direct-assignment').scrollIntoView({ behavior: 'smooth', block: 'start' }); $('direct-assignment-form').elements.names.focus();
  }
  $('assignment-user-search').onsubmit = e => { e.preventDefault(); run(e.submitter, () => loadAssignmentUsers($('assignment-user-query').value)); };
  function resetDirectRows() { $('direct-new-subscriptions').replaceChildren(); addDirectRow(); }
  function addDirectRow() {
    const target = $('direct-new-subscriptions'); if (target.children.length >= 20) return;
    const row = el('fieldset', null, 'direct-subscription-row'); row.dataset.directRow = '';
    const legend = el('legend', 'Новая подписка'), nameLabel = el('label', 'Название новой подписки'), name = el('input'); name.dataset.directName = ''; name.maxLength = 100; name.placeholder = 'Например VPN5092'; nameLabel.append(name);
    const urlLabel = el('label', 'Новая подписочная ссылка'), url = el('textarea'); url.dataset.directUrl = ''; url.maxLength = 8192; url.rows = 2; url.placeholder = 'Вставьте ссылку'; urlLabel.append(url);
    const syncRequired = () => { name.required = url.required = !!(name.value.trim() || url.value.trim()); }; name.oninput = url.oninput = syncRequired;
    const remove = button('Убрать строку', async () => { row.remove(); $('add-direct-subscription').disabled = false; });
    row.append(legend, nameLabel, urlLabel, remove); target.append(row); $('add-direct-subscription').disabled = target.children.length >= 20;
  }
  $('add-direct-subscription').onclick = addDirectRow; resetDirectRows();
  $('direct-assignment-form').onsubmit = e => {
    e.preventDefault(); run(e.submitter, async () => {
      const select = $('assignment-user'), recipientId = Number(select.value); if (!recipientId || select.disabled) throw Error('Сначала выберите клиента.');
      const rows = Array.from($('direct-new-subscriptions').children).map(row => ({ name: row.querySelector('[data-direct-name]').value, url: row.querySelector('[data-direct-url]').value }));
      const data = await api('direct_assign_subscriptions', { user_id: recipientId, names: e.target.elements.names.value, new_subscriptions: rows });
      e.target.elements.names.value = ''; resetDirectRows(); await loadAdminSubscriptions();
      const recipient = assignmentUsers.get(recipientId); message('Клиенту ' + (recipient ? recipient.email : '#' + recipientId) + ' назначено подписок: ' + data.assigned + '. Новых в базе: ' + data.created + '. Уже были у клиента: ' + data.already_owned + '.');
    });
  };
  $('mail-test').onclick = e => run(e.target, async () => { const data = await api('mail_test', {}); await updateSummary(); message(data.ready ? 'Тестовое письмо поставлено в очередь отправки.' : 'Письмо сохранено в очереди. Сначала настройте отправку в private/config.php.'); });
  $('mail-retry').onclick = e => run(e.target, async () => { const data = await api('mail_retry', {}); await updateSummary(); message(data.ready ? 'Почтовым сервером принято: ' + data.sent + '. Не удалось отправить: ' + data.failed + '.' : 'Сначала настройте отправку писем в private/config.php.'); });
  setInterval(async () => { if (!user || document.hidden) return; try { if (conversation) await loadConversation(); else await updateSummary(); } catch (e) { if (e.status === 401) message('Сессия завершена. Войдите снова.', true); } }, 20000);
  (async () => { try { const data = await api('me'); user = data.user; payment = data.payment; await showCabinet(); if (!user && new URLSearchParams(location.search).get('mode') === 'register') authTab(true); } catch (e) { message(e.message, true); document.querySelectorAll('#auth-section button[type=submit]').forEach(b => b.disabled = true); } })();
})();
