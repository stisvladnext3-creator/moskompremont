(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  if (!$('auth-section')) return;
  let authEpoch = 0;
  let csrf = '', user = null, payment = { available: false }, mode = 'client';
  let clientTab = 'subscriptions', adminTab = 'support', conversation = null;
  let keeneticEnabled = false, keeneticUser = null, keeneticUsersLoaded = false, keeneticRequest = 0;
  let routerHostPreview = null;
  let routerKeyUsersLoaded = false; let assignmentUsersLoaded = false; const assignmentUsers = new Map();
  const pages = { accounts: 0, support: 0, tickets: 0, terms: 0, clients: 0, users: 0, payments: 0, subscriptions: 0, 'router-keys': 0 };
  const orderStatus = { created: 'Ожидает оплаты', submitted: 'На ручной проверке', paid: 'Оплата подтверждена', rejected: 'Оплата не подтверждена' };
  const claimStatus = { pending: 'Ожидает ссылки или проверки', approved: 'Подписка добавлена', rejected: 'Запрос отклонён', revoked: 'Привязка снята администратором' };
  const money = n => new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB', maximumFractionDigits: 0 }).format(Number(n));
  const date = value => value ? new Date(value.replace(' ', 'T') + 'Z').toLocaleString('ru-RU', { timeZone: 'Europe/Moscow' }) : '';
  const el = (tag, text, className) => { const node = document.createElement(tag); if (text !== undefined && text !== null) node.textContent = text; if (className) node.className = className; return node; };
  function message(text, error = false) { $('account-message').textContent = text; $('account-message').dataset.error = String(error); }
  function button(text, fn, className = 'account-secondary') { const b = el('button', text, className); b.type = 'button'; b.addEventListener('click', () => run(b, fn)); return b; }
  async function run(b, fn) { message(''); if (b) b.disabled = true; try { await fn(); } catch (e) { if (e.stale) return; message(e.message, true); $('account-message').scrollIntoView({ behavior: 'smooth', block: 'nearest' }); } finally { if (b && b.isConnected) b.disabled = false; } }
  async function api(action, data, params = {}, retry = true) {
    const requestEpoch = authEpoch, multipart = data instanceof FormData;
    const response = await fetch('api/account.php?' + new URLSearchParams({ action, ...params }), { method: data === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store', headers: data === undefined ? {} : { ...(multipart ? {} : { 'Content-Type': 'application/json' }), 'X-CSRF-Token': csrf }, body: data === undefined ? undefined : multipart ? data : JSON.stringify(data) });
    let result; try { result = await response.json(); } catch { throw Error(user?.is_admin ? 'Сервер вернул некорректный ответ (HTTP ' + response.status + '). Проверьте журнал ошибок PHP на хостинге.' : 'Не удалось обработать ответ сервера. Обновите страницу или напишите в поддержку.'); }
    if (requestEpoch !== authEpoch) { const e = Error('Аккаунт изменился.'); e.stale = true; throw e; }
    if (result.csrf) csrf = result.csrf; if (!response.ok && response.status === 403 && result.code === 'csrf_refresh' && retry) return api(action, data, params, false);
    if (!response.ok) { if (response.status === 401 && user) signedOut(); const e = Error(result.error || 'Ошибка запроса'); e.status = response.status; throw e; }
    if (result.csrf) csrf = result.csrf; return result;
  }
  function authTab(register) { $('login-form').hidden = register; $('register-form').hidden = !register; $('login-tab').setAttribute('aria-pressed', String(!register)); $('register-tab').setAttribute('aria-pressed', String(register)); message(''); }
  $('login-tab').onclick = () => authTab(false); $('register-tab').onclick = () => authTab(true);
  $('has-subscriptions').onchange = e => { $('registration-subscriptions').hidden = !e.target.checked; $('existing-subscriptions').disabled = !e.target.checked; $('existing-subscriptions').required = e.target.checked; };
  for (const action of ['login', 'register']) $(action + '-form').onsubmit = event => {
    event.preventDefault(); run(event.submitter, async () => {
      const data = Object.fromEntries(new FormData(event.target)); data.remember = event.target.elements.remember.checked; if (action === 'register') data.has_existing_subscriptions = $('has-subscriptions').checked;
      const result = await api(action, data); user = result.user; authEpoch++; event.target.reset(); $('registration-subscriptions').hidden = true; $('existing-subscriptions').disabled = true; $('existing-subscriptions').required = false;
      await showCabinet(); message(action === 'register' ? 'Аккаунт создан. Найденные подписки уже в разделе «Мои подписки».' : 'Вы вошли в кабинет.');
    });
  };
  function signedOut() {
    authEpoch++; user = null; conversation = null; clearSubscriptionHostPreview(); notificationPage = 0; notificationBaseline = null; templates.clear(); pushInfo = null; $('notification-toast').hidden = true; $('notifications-unread').hidden = $('notifications-tab-unread').hidden = true; $('reply-template-tools').hidden = true; clearRouterHostPreview(); setKeeneticVisibility(false); clearKeeneticManagement(); keeneticUsersLoaded = false; $('monitor-token-value').value = ''; $('monitor-token-box').hidden = true; $('monitor-form').reset(); $('service-notice-form').reset(); $('service-notice-preview').textContent = ''; $('service-notice-saved').textContent = ''; $('auth-section').hidden = false; $('cabinet-section').hidden = true; $('conversation').hidden = true;
    for (const id of ['subscriptions-list', 'claims-list', 'orders-list', 'tickets-list', 'admin-tickets', 'admin-clients', 'admin-terms', 'keenetic-files', 'admin-keenetic-files', 'mail-diagnostics', 'admin-users', 'admin-orders', 'admin-subscriptions', 'admin-claims', 'router-keys-list', 'admin-router-keys', 'conversation-messages', 'admin-accounts', 'admin-templates', 'notifications-list', 'push-diagnostics']) $(id).replaceChildren();
    document.querySelectorAll('#cabinet-section form').forEach(form => form.reset()); $('reply-form').reset(); assignmentUsers.clear(); assignmentUsersLoaded = false; routerKeyUsersLoaded = false; $('keenetic-user').replaceChildren(new Option('Выберите клиента', '')); $('router-key-user').replaceChildren(new Option('Выберите клиента', '')); $('assignment-user').replaceChildren(new Option('Выберите клиента', '')); resetDirectRows();
  }
  $('logout-button').onclick = e => run(e.target, async () => { await disconnectPushOnLogout(); await api('logout', {}); signedOut(); message('Вы вышли из кабинета.'); });
  async function showCabinet() {
    $('auth-section').hidden = !!user; $('cabinet-section').hidden = !user; if (!user) return;
    $('notification-description').textContent = 'Ответы поддержки, выдача и обновления подписок и ключей.' + (user.is_admin ? ' Для администратора — также регистрации, оплаты и сообщения клиентов.' : '');
    $('profile-name').textContent = user.first_name + ' ' + user.last_name; $('profile-contact').textContent = user.email + ' · ' + user.telegram;
    $('client-mode').hidden = !user.is_admin; $('admin-mode').hidden = !user.is_admin;
    $('payment-unavailable').hidden = payment.available; $('order-form').querySelector('button').disabled = !payment.available;
    mode = user.is_admin ? 'admin' : 'client'; if (location.hash === '#payment') { mode = 'client'; clientTab = 'payments'; } if (location.hash === '#instructions') { mode = 'client'; clientTab = 'instructions'; } if (['#servers', '#service-status'].includes(location.hash)) { mode = 'client'; clientTab = 'servers'; }
    await loadKeenetic(); if (location.hash === '#keenetic') { mode = 'client'; clientTab = 'keenetic'; }
    if (location.hash === '#notifications' || new URLSearchParams(location.search).has('notification')) { mode = 'client'; clientTab = 'notifications'; }
    if (clientTab === 'keenetic' && !keeneticEnabled) clientTab = 'subscriptions';
    await setMode(mode); window.MKRServiceNotice?.refresh(); await updateSummary(); await openNotificationDeepLink();
  }
  async function setMode(value) { mode = value; $('service-status-button').setAttribute('aria-pressed', String(value === 'client' && clientTab === 'servers')); $('client-shell').hidden = value !== 'client'; $('admin-shell').hidden = value !== 'admin'; closeConversation(); if (value === 'admin') await selectAdmin(adminTab); else await selectClient(clientTab); }
  $('service-status-button').onclick = e => run(e.target, async () => { clientTab = 'servers'; await setMode('client'); $('cabinet-servers').scrollIntoView({ behavior: 'smooth', block: 'start' }); });
  $('client-mode').onclick = e => run(e.target, () => setMode('client')); $('admin-mode').onclick = e => run(e.target, () => setMode('admin'));
  async function selectClient(value) { if (value === 'keenetic' && !keeneticEnabled) value = 'subscriptions'; clientTab = value; $('service-status-button').setAttribute('aria-pressed', String(mode === 'client' && value === 'servers')); closeConversation(); document.querySelectorAll('[data-client-tab]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.clientTab === value))); document.querySelectorAll('[data-client-panel]').forEach(p => p.hidden = p.dataset.clientPanel !== value); if (value === 'subscriptions') await loadSubscriptions(); if (value === 'router-keys') await loadRouterKeys(); if (value === 'payments') await loadOrders(); if (value === 'support') await loadTickets(false); if (value === 'instructions') loadInstructions(); if (value === 'servers') await window.MKRServerStatus?.refresh(); if (value === 'keenetic') await loadKeenetic(); if (value === 'notifications') { await loadNotifications(); await loadPushDevice(); } }
  document.querySelectorAll('[data-client-tab]').forEach(b => b.onclick = () => run(b, () => selectClient(b.dataset.clientTab)));
  let instructionsLoaded = false;
  function loadInstructions() {
    if (instructionsLoaded) return;
    if (!window.MKRInstructions) { $('cabinet-instructions-list').replaceChildren(el('p', 'Не удалось загрузить инструкции. Обновите страницу или напишите в поддержку.', 'account-muted')); return; }
    window.MKRInstructions.render($('cabinet-instructions-list'), window.SITE_CONFIG?.instructions, { cabinet: true });
    instructionsLoaded = true;
  }
  $('instructions-subscriptions').onclick = e => run(e.target, () => selectClient('subscriptions'));
  $('instructions-router-keys').onclick = e => run(e.target, () => selectClient('router-keys'));
  $('account-instructions-link').onclick = e => {
    if (!user) return;
    e.preventDefault(); run(null, async () => { clientTab = 'instructions'; await setMode('client'); $('cabinet-instructions').scrollIntoView({ behavior: 'smooth', block: 'start' }); });
  };
  async function selectAdmin(value) { adminTab = value; closeConversation(); document.querySelectorAll('[data-admin-tab]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.adminTab === value))); document.querySelectorAll('[data-admin-panel]').forEach(p => p.hidden = p.dataset.adminPanel !== value); await loadAdmin(value); }
  document.querySelectorAll('[data-admin-tab]').forEach(b => b.onclick = () => run(b, () => selectAdmin(b.dataset.adminTab)));
  document.querySelectorAll('[data-admin-refresh]').forEach(b => b.onclick = () => run(b, () => loadAdmin(b.dataset.adminRefresh)));
  function linkRow(url, name = 'подписка') {
    const row = el('div', null, 'link-row'), input = el('input'); input.value = url; input.readOnly = true; input.setAttribute('aria-label', 'Ссылка ' + name); row.append(input);
    const copy = button('Копировать', async () => { try { await navigator.clipboard.writeText(url); copy.textContent = 'Скопировано ✓'; setTimeout(() => copy.textContent = 'Копировать', 2000); } catch { input.focus(); input.select(); message('Скопируйте выделенную ссылку.'); } }); row.append(copy);
    if (/^(https?|vless|vmess|trojan|ss|hysteria2):\/\//i.test(url)) { const a = el('a', 'Открыть'); a.href = url; a.target = '_blank'; a.rel = 'noopener noreferrer'; row.append(a); } return row;
  }
  function subscriptionCard(sub, revealExpired = false) {
    const card = el('article', null, 'subscription-card'); card.append(el('h3', sub.name), subscriptionTermBadge(sub), resourceUpdated(sub));
    if (sub.expires_at) card.append(el('p', 'Действует до: ' + date(sub.expires_at), 'account-muted'));
    if (sub.url && (!sub.is_expired || revealExpired)) { const link = linkRow(sub.url, sub.name); link.classList.add('subscription-link'); link.dataset.hideExpired = String(!revealExpired); card.append(link); }
    else if (sub.is_expired) card.append(el('p', 'Срок подписки истёк. Для продления выберите тариф или напишите в поддержку.', 'account-note subscription-expired-note'));
    return card;
  }
  async function loadSubscriptions() {
    const data = await api('subscriptions'); $('subscriptions-list').replaceChildren(); $('claims-list').replaceChildren();
    if (!data.subscriptions.length) $('subscriptions-list').append(el('p', 'У вас пока нет выданных подписок. Укажите имеющиеся названия ниже или выберите тариф.', 'account-muted'));
    data.subscriptions.forEach(s => $('subscriptions-list').append(subscriptionCard(s)));
    for (const claim of data.claims.filter(c => c.status !== 'approved')) { const box = el('div', null, 'record'); box.append(el('h3', claim.name), el('span', claimStatus[claim.status] || claim.status, 'status-badge')); $('claims-list').append(box); }
  }
  $('refresh-subscriptions').onclick = e => run(e.target, loadSubscriptions);
  $('claim-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => { await api('claim_subscriptions', { names: e.target.elements.names.value }); e.target.reset(); await loadSubscriptions(); message('Запрос обработан. Найденные подписки добавлены в кабинет.'); }); };
  function total() {
    const renewal = $('order-purpose').value === 'renew';
    $('order-renewal-label').hidden = !renewal; $('order-renewal-subscription').required = renewal; $('order-renewal-subscription').disabled = !renewal;
    $('order-renewal-note').hidden = !renewal;
    if (renewal) $('order-form').elements.plan.value = 'monthly';
    $('order-form').elements.plan.disabled = renewal;
    const form = $('order-form'), count = Number(form.elements.devices.value), monthly = form.elements.plan.value === 'monthly', months = Number(form.elements.months.value), rates = monthly ? [250, 225, 200, 175] : [3000, 2500, 2000, 1650];
    $('order-months-label').hidden = !monthly;
    $('order-total').textContent = Number.isInteger(count) && count >= 1 && count <= 20 ? money(rates[Math.min(count, 4) - 1] * count * (monthly ? months : 1)) + (monthly ? ' за ' + months + ' мес.' : ' разово') : 'Укажите от 1 до 20 устройств';
  }
  $('order-form').oninput = total; total();
  $('order-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => { const renewal = $('order-purpose').value === 'renew'; await api('create_order', { plan: e.target.elements.plan.value, devices: Number(e.target.elements.devices.value), months: e.target.elements.plan.value === 'monthly' ? Number(e.target.elements.months.value) : 1, renewal_subscription_id: renewal ? Number($('order-renewal-subscription').value) : null }); await loadOrders(); message(renewal ? 'Заявка на продление создана. Срок добавится после подтверждения оплаты администратором.' : 'Заявка создана. Оплатите по QR-коду ниже.'); $('orders-list').scrollIntoView({ behavior: 'smooth' }); }); };
  function orderCard(order) { const box = el('article', null, 'account-order'); box.dataset.orderId = order.id; const title = order.renewal ? 'Продление «' + order.renewal.subscription_name + '» на ' + (order.months || 1) + ' мес.' : order.plan === 'monthly' ? 'Подписка на ' + (order.months || 1) + ' мес.' : 'Навсегда'; box.append(el('h3', title + ' · ' + order.devices + ' устр. · ' + money(order.amount)), el('p', 'Заявка ' + order.id, 'account-muted'), el('span', orderStatus[order.status] || order.status, 'status-badge status-' + order.status)); if (order.renewal?.applied_at) box.append(el('p', 'Подписка продлена до: ' + date(order.renewal.new_expires_at), 'account-note')); if (order.deleted_subscriptions?.length) box.append(el('p', 'Удалённые подписки: ' + order.deleted_subscriptions.map(s => s.name).join(', '), 'account-muted')); return box; }
  function renewalChoices(subscriptions) {
    const selector = $('order-renewal-subscription'), previous = selector.value;
    selector.replaceChildren(new Option('Выберите подписку', ''));
    for (const sub of subscriptions) {
      const suffix = sub.term_kind === 'lifetime' ? ' — бессрочная' : sub.is_expired ? ' — срок истёк' : sub.expires_at ? ' — до ' + date(sub.expires_at) : ' — срок не указан';
      const option = new Option(sub.name + suffix, String(sub.id)); option.disabled = sub.term_kind === 'lifetime'; selector.append(option);
    }
    if (Array.from(selector.options).some(option => option.value === previous && !option.disabled)) selector.value = previous;
    const available = subscriptions.some(sub => sub.term_kind !== 'lifetime');
    $('order-purpose').querySelector('[value="renew"]').disabled = !available;
    if (!available && $('order-purpose').value === 'renew') $('order-purpose').value = 'new';
    total();
  }
  async function loadOrders() {
    const [data, catalog] = await Promise.all([api('orders'), api('subscriptions')]); renewalChoices(catalog.subscriptions); $('orders-list').replaceChildren(); if (!data.orders.length) $('orders-list').append(el('p', 'У вас пока нет заявок.', 'account-muted'));
    for (const order of data.orders) {
      const box = orderCard(order);
      if (order.status === 'created' && payment.available) {
        box.append(el('p', 'К оплате: ' + money(order.amount))); const image = el('img'); image.src = payment.qr_image; image.alt = 'QR-код оплаты в Озон Банке'; image.className = 'payment-qr'; box.append(image);
        const a = el('a', 'Открыть QR-код в отдельной вкладке'); a.href = payment.qr_image; a.target = '_blank'; a.rel = 'noopener'; box.append(a, el('p', 'Получатель: ' + payment.recipient), el('p', 'Проверьте получателя и введите точную сумму. ' + payment.note, 'account-note'));
        const form = el('form'), label = el('label', 'Данные перевода для сверки'), input = el('input'); input.name = 'reference'; input.required = true; input.minLength = 4; input.maxLength = 500; input.placeholder = 'Например: 15:20, телефон 3015'; label.append(input);
        const submit = el('button', 'Я оплатил — отправить на проверку', 'account-primary'); submit.type = 'submit'; form.append(label, submit); form.onsubmit = e => { e.preventDefault(); run(submit, async () => { await api('submit_payment', { id: order.id, reference: input.value }); await loadOrders(); message('Вы сообщили об оплате. Администратору создано уведомление; после проверки он выдаст подписку.'); }); }; box.append(form);
      }
      if (order.status === 'submitted') box.append(el('p', 'Ожидайте проверки поступления. Повторно платить не нужно.', 'account-muted'));
      if (order.status === 'paid' && !order.subscriptions.length && !order.renewal) box.append(el('p', order.deleted_subscriptions?.length ? 'Подписка удалена администратором. История оплаты сохранена.' : order.revoked_subscriptions?.length ? 'Привязка подписки снята администратором. История оплаты сохранена.' : 'Оплата подтверждена. Ожидайте выдачи подписки администратором.', 'account-note'));
      for (const sub of order.subscriptions) box.append(subscriptionCard(sub));
      if (order.status === 'rejected') box.append(el('p', 'Свяжитесь с поддержкой для сверки перевода перед повторной оплатой.', 'account-muted'));
      $('orders-list').append(box);
    }
  }
  $('refresh-orders').onclick = e => run(e.target, loadOrders);
  $('ticket-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => { const data = await api('create_ticket', new FormData(e.target)); e.target.reset(); await loadTickets(false); await openConversation(data.id, false); message('Обращение отправлено. Администратору создано уведомление и поставлено письмо в очередь.'); }); };
  async function loadTickets(asAdmin) {
    const key = asAdmin ? 'support' : 'tickets', data = await api(asAdmin ? 'admin_tickets' : 'tickets', undefined, { page: pages[key], ...(asAdmin ? { scope: ticketScope } : {}) }), target = $(asAdmin ? 'admin-tickets' : 'tickets-list'); target.replaceChildren();
    if (asAdmin) { $('tickets-active').setAttribute('aria-pressed', String(ticketScope === 'active')); $('tickets-archive').setAttribute('aria-pressed', String(ticketScope === 'archive')); $('tickets-scope-hint').textContent = ticketScope === 'archive' ? 'Решённые обращения. Ответ клиента вернёт обращение в активные.' : 'Обращения в обработке и ожидающие ответа клиента.'; }
    if (!data.tickets.length) target.append(el('p', asAdmin && ticketScope === 'archive' ? 'В архиве пока нет обращений.' : 'Обращений пока нет.', 'account-muted'));
    for (const ticket of data.tickets) { const box = el('article', null, 'record' + (Number(ticket.unread) ? ' record-new' : '')); box.dataset.ticketId = ticket.id; box.append(el('h3', '#' + ticket.id + ' · ' + ticket.subject), el('span', ticketLabels[ticket.status] || ticket.status_label, 'status-badge ticket-state-' + ticket.status), el('p', date(ticket.updated_at), 'account-muted')); if (asAdmin) box.append(el('p', ticket.first_name + ' ' + ticket.last_name + ' · ' + ticket.email), el('p', ticket.telegram, 'account-muted')); if (Number(ticket.unread)) box.append(el('p', 'Новых сообщений: ' + ticket.unread, 'account-kicker')); box.append(button('Открыть переписку', () => openConversation(Number(ticket.id), asAdmin))); target.append(box); }
    smallPager(asAdmin ? 'admin-tickets-pager' : 'tickets-pager', pages[key], data.tickets.length, data.total, 100, async page => { pages[key] = page; await loadTickets(asAdmin); });
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

  function closeConversation() { conversation = null; $('reply-template-tools').hidden = true; $('conversation').hidden = true; $('conversation-messages').replaceChildren(); $('reply-form').reset(); }
  $('close-conversation').onclick = closeConversation;
  async function openConversation(id, asAdmin) { conversation = { id, asAdmin, lastId: null }; $('reply-form').reset(); $('reply-template-tools').hidden = !asAdmin; if (asAdmin) { try { await refreshTemplates(true); } catch (e) { $('reply-template-hint').textContent = e.message; } } await loadConversation(); $('conversation').scrollIntoView({ behavior: 'smooth', block: 'start' }); }
  async function loadConversation() {
    if (!conversation) return; const active = conversation, data = await api('read_ticket', { id: active.id, as_admin: active.asAdmin }); if (conversation !== active) return;
    $('conversation').hidden = false; $('conversation-title').textContent = '#' + data.ticket.id + ' · ' + data.ticket.subject; $('conversation-meta').textContent = (ticketLabels[data.ticket.status] || data.ticket.status_label) + ' · последние 200 сообщений'; $('resolve-ticket').hidden = !active.asAdmin || data.ticket.status === 'closed'; $('reopen-ticket').hidden = !active.asAdmin || data.ticket.status !== 'closed';
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
    if (!user) return; const summary = await api('summary'); notificationSummary(summary, true); badge($('support-unread'), summary.unread_support);
    setKeeneticVisibility(!!summary.keenetic_enabled); if (!keeneticEnabled && clientTab === 'keenetic' && mode === 'client') await selectClient('subscriptions');
    if (user.is_admin) { const data = await api('admin_summary'); badge($('tickets-archive-count'), data.support_archive); Object.entries(data.unread).forEach(([type, count]) => document.querySelectorAll('[data-count="' + type + '"]').forEach(node => badge(node, count))); const m = data.mail; $('mail-status').textContent = 'Получатель: ' + m.recipient + '\n' + (m.ready ? 'Отправка настроена.' : 'Отправка не настроена. Заполните раздел mail в private/config.php.') + '\nПринято почтовым сервером: ' + m.sent + '. В очереди: ' + m.pending + '. Ошибок после повторов: ' + m.failed + '.'; $('mail-status').style.whiteSpace = 'pre-line'; $('mail-status').classList.toggle('mail-warning', !m.ready || m.failed > 0); }
  }
  function pager(id, key, count, total) { const target = $(id); target.replaceChildren(); const box = el('div', null, 'pager'); const prev = button('← Назад', async () => { pages[key]--; await loadAdmin(key); }); prev.disabled = pages[key] === 0; const next = button('Далее →', async () => { pages[key]++; await loadAdmin(key); }); next.disabled = total === undefined ? count < 100 : (pages[key] + 1) * 100 >= total; box.append(prev, el('span', 'Страница ' + (pages[key] + 1)), next); target.append(box); }
  function readButton(record, reload) { return button('Отметить прочитанным', async () => { await api('read_event', { id: record.event_id }); await reload(); await updateSummary(); }); }
  async function loadAdmin(value) { if (value === 'accounts') await loadAccounts(); if (value === 'templates') await refreshTemplates(); if (value === 'push') await loadAdminPush(); if (value === 'support') await loadTickets(true); if (value === 'users') await loadUsers(); if (value === 'clients') await loadClients(); if (value === 'payments') await loadAdminOrders(); if (value === 'subscriptions') await loadAdminSubscriptions(); if (value === 'router-keys') await loadAdminRouterKeys(); if (value === 'terms') await loadAdminTerms(); if (value === 'keenetic') await loadAdminKeenetic(); if (value === 'service-notice') await loadServiceNotice(); if (value === 'monitoring') await loadMonitoring(); if (value === 'mail') await loadMailDiagnostics(); await updateSummary(); }
  $('clients-search').onsubmit = e => { e.preventDefault(); pages.clients = 0; run(e.submitter, loadClients); };
  async function loadClients() {
    const data = await api('admin_clients', undefined, { offset: pages.clients * 100, q: $('clients-query').value.trim(), router_keys: $('clients-router-filter').value, terms: $('clients-term-filter').value });
    $('admin-clients').replaceChildren(); $('clients-total').textContent = 'Найдено клиентов: ' + data.total + (data.keenetic_ready === false ? '. Для Keenetic импортируйте private/migrations/006-keenetic.sql.' : '');
    for (const item of data.users) {
      const box = el('article', null, 'record client-directory-card');
      box.append(el('h3', '#' + item.id + ' · ' + item.first_name + ' ' + item.last_name), el('p', item.email + ' · ' + item.telegram, 'account-muted'));
      const assigned = el('div', null, 'client-assigned-columns'), subs = el('div'), keys = el('div');
      subs.append(el('h4', 'Подписки (' + item.subscriptions.length + ')'));
      if (item.subscriptions.length) { const list = el('ul'); for (const sub of item.subscriptions) {
        const row = el('li', null, 'client-assigned-item'); row.dataset.subscriptionId = sub.id; row.append(el('span', sub.name)); row.append(subscriptionTermBadge(sub));
        const actions = el('div', null, 'order-buttons'); actions.append(button('Срок', () => chooseSubscriptionTerm(sub))); actions.append(button('Удалить подписку', async () => {
          if (!confirm('Полностью удалить подписку «' + sub.name + '» у ' + item.email + ' из базы кабинета? Ссылка и связанные запросы будут удалены. История оплаты сохранится. Подключение в 3x-ui отключается отдельно.')) return;
          await api('admin_delete_subscription', { id: Number(sub.id), user_id: Number(item.id) }); await loadClients(); message('Подписка удалена из базы кабинета.');
        }, 'account-secondary account-danger')); row.append(actions); list.append(row);
      } subs.append(list); } else subs.append(el('p', 'Не привязаны', 'account-muted'));
      keys.append(el('h4', 'Ключи для роутеров (' + item.router_keys.length + ')'));
      if (item.router_keys.length) { const list = el('ul'); for (const key of item.router_keys) {
        const row = el('li', null, 'client-assigned-item'); row.dataset.routerKeyId = key.id; row.append(el('span', key.label + ' · ' + (routerFormats[key.key_format] || key.key_format)));
        const actions = el('div', null, 'order-buttons'); actions.append(button('Удалить ключ', async () => {
          if (!confirm('Полностью удалить ключ «' + key.label + '» у ' + item.email + ' из базы кабинета? Подключение на VPN-сервере отключается отдельно.')) return;
          await api('admin_delete_router_key', { id: Number(key.id), user_id: Number(item.id) }); await loadClients(); message('Ключ удалён из базы кабинета.');
        }, 'account-secondary account-danger')); row.append(actions); list.append(row);
      } keys.append(list); } else keys.append(el('p', 'Не привязаны', 'account-muted'));
      assigned.append(subs, keys); box.append(assigned);
      const accessLabel = el('label', null, 'checkbox-label'), access = el('input'); access.type = 'checkbox'; access.checked = !!item.keenetic_enabled; access.disabled = data.keenetic_ready === false;
      accessLabel.append(access, document.createTextNode('Показывать вкладку Keenetic')); box.append(accessLabel, el('p', 'Файлов Keenetic: ' + (item.keenetic_files_count || 0), 'account-muted'));
      access.onchange = () => { const enabled = access.checked; run(null, async () => { access.disabled = true; try { await api('admin_keenetic_access', { user_id: Number(item.id), enabled }); item.keenetic_enabled = enabled; message(enabled ? 'Вкладка Keenetic включена для ' + item.email + '.' : 'Вкладка Keenetic и скачивание файлов отключены для ' + item.email + '.'); } catch (e) { access.checked = !enabled; throw e; } finally { access.disabled = false; } }); };
      box.append(button('Файлы Keenetic', () => chooseKeeneticRecipient(item)), button('Назначить подписки', () => chooseDirectRecipient(item)), button('Выдать ключ роутера', () => chooseRouterKeyRecipient(item))); $('admin-clients').append(box);
    }
    pager('clients-pager', 'clients', data.users.length, data.total);
  }
  async function loadUsers() {
    const data = await api('admin_users', undefined, { page: pages.users }), target = $('admin-users'); target.replaceChildren();
    for (const item of data.users) { const fresh = item.event_id && !item.event_read_at, box = el('article', null, 'record' + (fresh ? ' record-new' : '')); box.append(el('h3', '#' + item.id + ' · ' + item.first_name + ' ' + item.last_name), el('p', 'Почта: ' + item.email), el('p', 'Telegram / телефон: ' + item.telegram), el('p', 'Регистрация: ' + date(item.created_at), 'account-muted'), el('p', 'Указал подписки: ' + (item.claims.map(c => c.name).join(', ') || 'нет')), el('p', 'Выданы: ' + (item.subscriptions.map(s => s.name).join(', ') || 'пока нет'), 'account-muted')); if (fresh) box.append(readButton(item, loadUsers)); box.append(button('Назначить подписки', () => chooseDirectRecipient(item))); box.append(button('Выдать ключ роутера', () => chooseRouterKeyRecipient(item))); target.append(box); }
    pager('users-pager', 'users', data.users.length);
  }
  async function loadAdminOrders() {
    const data = await api('admin_orders', undefined, { page: pages.payments }), target = $('admin-orders'); target.replaceChildren(); if (!data.orders.length) target.append(el('p', 'Заявок пока нет.', 'account-muted'));
    for (const order of data.orders) {
      const box = orderCard(order); if (order.event_id && !order.event_read_at) box.classList.add('record-new'); box.append(el('p', order.first_name + ' ' + order.last_name + ' · ' + order.email), el('p', order.telegram, 'account-muted')); if (order.payment_reference) box.append(el('p', 'Данные перевода: ' + order.payment_reference));
      if (order.event_id && !order.event_read_at) box.append(readButton(order, loadAdminOrders));
      if (order.status === 'submitted') { const actions = el('div', null, 'order-buttons'); for (const [status, title] of [['paid', order.renewal ? 'Подтвердить оплату и продлить' : 'Подтвердить поступление'], ['rejected', 'Отклонить']]) actions.append(button(title, async () => { if (!confirm(status === 'paid' ? 'Вы проверили поступление полной суммы в Озон Банке?' + (order.renewal ? ' Срок подписки «' + order.renewal.subscription_name + '» будет продлён на ' + order.months + ' мес.' : '') : 'Отклонить заявку после сверки перевода?')) return; const result = await api('review_order', { id: order.id, status }); await loadAdminOrders(); await updateSummary(); message(status === 'paid' ? result.renewed ? 'Оплата подтверждена. Срок подписки автоматически продлён.' : 'Оплата подтверждена. Теперь выдайте подписку в этой заявке.' : 'Заявка отклонена.'); }, status === 'paid' ? 'account-primary' : 'account-secondary')); box.append(actions); }
      for (const sub of order.subscriptions) box.append(subscriptionCard(sub, true));
      if (order.status === 'paid' && !order.renewal) {
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
    for (const sub of data.subscriptions) { const box = subscriptionCard(sub, true); box.append(el('p', sub.user_id ? 'Клиент: ' + sub.first_name + ' ' + sub.last_name + ' · ' + sub.email : 'Свободна — ещё не привязана к клиенту', 'account-muted'));
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
      $('assignment-user-hint').textContent = data.users.length >= data.limit ? 'Показаны первые ' + data.limit + ' клиентов. Уточните поиск по почте, имени, Telegram / телефону.' : data.users.length ? 'Найдено клиентов: ' + data.users.length + '. Проверьте имя и почту получателя.' : 'Клиент не найден. Проверьте поиск или дождитесь его регистрации.';
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
  function setKeeneticVisibility(enabled) {
    keeneticEnabled = enabled; $('keenetic-client-tab').hidden = !enabled;
    if (!enabled) { $('keenetic-files').replaceChildren(); $('cabinet-keenetic').hidden = true; }
  }
  function keeneticFileCard(file) {
    const card = el('article', null, 'subscription-card keenetic-file-card');
    card.append(el('h3', file.file_name), el('p', (Number(file.byte_size) < 1024 ? file.byte_size + ' Б' : Number(file.byte_size) < 1048576 ? Math.ceil(Number(file.byte_size) / 1024) + ' КБ' : (Number(file.byte_size) / 1048576).toLocaleString('ru-RU', { maximumFractionDigits: 1 }) + ' МБ') + ' · ' + date(file.created_at), 'account-muted'));
    const actions = el('div', null, 'order-buttons'), download = el('a', 'Скачать файл', 'account-secondary'); download.href = 'api/keenetic-file.php?' + new URLSearchParams({ id: file.id }); download.download = file.file_name; actions.append(download); card.append(actions); return { card, actions };
  }
  async function loadKeenetic() {
    const owner = user?.id; if (!owner) return;
    const data = await api('keenetic'); if (user?.id !== owner) return;
    setKeeneticVisibility(!!data.enabled); $('keenetic-files').replaceChildren();
    if (!data.enabled) { if (clientTab === 'keenetic' && mode === 'client') await selectClient('subscriptions'); return; }
    if (!data.files.length) $('keenetic-files').append(el('p', 'Конфигурации пока не загружены. Администратор добавит их в этот раздел.', 'account-muted'));
    for (const file of data.files) $('keenetic-files').append(keeneticFileCard(file).card);
  }
  $('refresh-keenetic').onclick = e => run(e.target, loadKeenetic);
  function clearKeeneticManagement() {
    keeneticRequest++; keeneticUser = null; $('keenetic-management').hidden = true; $('admin-keenetic-files').replaceChildren(); $('keenetic-upload-form').reset(); $('keenetic-enabled').checked = false;
  }
  async function loadKeeneticUsers(query = '', selectedId = 0) {
    clearKeeneticManagement(); const request = keeneticRequest, select = $('keenetic-user'); select.disabled = true; select.replaceChildren(new Option('Выберите клиента', ''));
    try {
      const data = await api('admin_user_search', undefined, { q: query }); if (request !== keeneticRequest || !user?.is_admin) return;
      for (const item of data.users) select.append(new Option('#' + item.id + ' · ' + item.first_name + ' ' + item.last_name + ' · ' + item.email, String(item.id)));
      select.value = selectedId ? String(selectedId) : ''; keeneticUsersLoaded = true;
      $('keenetic-user-hint').textContent = data.users.length >= data.limit ? 'Уточните поиск, если нужного клиента нет среди первых ' + data.limit + '.' : 'Найдено клиентов: ' + data.users.length + '. Выберите получателя конфигураций.';
    } finally { if (request === keeneticRequest) select.disabled = false; }
    if (selectedId && select.value) await loadKeeneticRecipient();
  }
  function keeneticAccessHint() {
    $('keenetic-access-hint').textContent = $('keenetic-enabled').checked ? 'Клиент видит вкладку Keenetic и может скачивать свои файлы.' : 'Вкладка скрыта, скачивание для клиента закрыто. Можно заранее загрузить файлы, затем включить доступ.';
  }
  async function loadKeeneticRecipient() {
    const id = Number($('keenetic-user').value); clearKeeneticManagement(); if (!id) return;
    const request = keeneticRequest, data = await api('admin_keenetic', undefined, { user_id: id });
    if (request !== keeneticRequest || !user?.is_admin) return;
    keeneticUser = data.user; $('keenetic-management').hidden = false; $('keenetic-recipient').textContent = 'Клиент: ' + data.user.first_name + ' ' + data.user.last_name + ' · ' + data.user.email;
    $('keenetic-enabled').checked = data.enabled; keeneticAccessHint();
    if (!data.files.length) $('admin-keenetic-files').append(el('p', 'У этого клиента пока нет файлов Keenetic.', 'account-muted'));
    for (const file of data.files) {
      const { card, actions } = keeneticFileCard(file);
      actions.append(button('Удалить файл', async () => {
        if (!confirm('Удалить файл «' + file.file_name + '» у ' + data.user.email + '?')) return;
        await api('admin_keenetic_delete', { id: file.id, user_id: id }); if (Number(keeneticUser?.id) === id) await loadKeeneticRecipient(); message('Файл «' + file.file_name + '» удалён.');
      }, 'account-secondary account-danger')); $('admin-keenetic-files').append(card);
    }
  }
  async function loadAdminKeenetic() {
    if (!keeneticUsersLoaded) await loadKeeneticUsers($('keenetic-user-query').value);
    else if ($('keenetic-user').value) await loadKeeneticRecipient();
  }
  async function chooseKeeneticRecipient(item) {
    await selectAdmin('keenetic'); $('keenetic-user-query').value = item.email; await loadKeeneticUsers(item.email, Number(item.id)); $('admin-keenetic-panel').scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
  $('keenetic-user-search').onsubmit = e => { e.preventDefault(); run(e.submitter, () => loadKeeneticUsers($('keenetic-user-query').value)); };
  $('keenetic-user').onchange = () => run(null, loadKeeneticRecipient);
  $('keenetic-enabled').onchange = () => {
    const recipient = keeneticUser, enabled = $('keenetic-enabled').checked; if (!recipient) return;
    run(null, async () => {
      $('keenetic-enabled').disabled = true;
      try { await api('admin_keenetic_access', { user_id: Number(recipient.id), enabled }); message('Доступ к Keenetic для ' + recipient.email + (enabled ? ' включён.' : ' отключён.')); }
      catch (e) { if (keeneticUser?.id === recipient.id) $('keenetic-enabled').checked = !enabled; throw e; }
      finally { $('keenetic-enabled').disabled = false; keeneticAccessHint(); }
    });
  };
  $('keenetic-upload-form').onsubmit = e => {
    e.preventDefault(); run(e.submitter, async () => {
      const recipient = keeneticUser; if (!recipient) throw Error('Сначала выберите клиента.');
      const input = e.target.querySelector('input[type=file]'), files = Array.from(input.files);
      if (!files.length || files.length > 3 || files.some(f => !f.size || f.size > 5 * 1024 * 1024)) throw Error('Выберите от 1 до 3 непустых файлов, до 5 МБ каждый.');
      if (files.some(f => !/\.(conf|ovpn|cfg|config|json|txt|yaml|yml|zip)$/i.test(f.name))) throw Error('Поддерживаются .conf, .ovpn, .cfg, .config, .json, .txt, .yaml, .yml и .zip.');
      const data = new FormData(e.target); data.set('user_id', String(recipient.id)); await api('admin_keenetic_upload', data);
      if (keeneticUser?.id === recipient.id) await loadKeeneticRecipient();
      message('Загружено файлов: ' + files.length + ' · ' + recipient.email + '.');
    });
  };
  const routerFormats = { connection: 'Ключ подключения', wireguard: 'WireGuard', openvpn: 'OpenVPN', text: 'Текстовая конфигурация' };
  function routerKeyCard(item) {
    const card = el('article', null, 'subscription-card router-key-card'); card.append(el('h3', item.label), el('p', routerFormats[item.key_format] || 'Ключ для роутера', 'account-muted'), resourceUpdated(item));
    const details = el('details'), summary = el('summary', 'Показать ключ'), text = el('textarea'); text.readOnly = true; text.rows = item.key_format === 'connection' ? 3 : 8; text.spellcheck = false; text.autocapitalize = 'off'; text.placeholder = 'Загрузка ключа…'; text.setAttribute('aria-label', 'Ключ: ' + item.label); details.append(summary, text); card.append(details);
    let material = null, pending = null;
    async function ensureKey() {
      if (material !== null) return material;
      if (!pending) pending = api('router_key', undefined, { id: item.id });
      try { const data = await pending; material = data.key.key_value; text.value = material; return material; } finally { pending = null; }
    }
    details.addEventListener('toggle', () => { if (details.open && material === null) run(null, ensureKey); });
    const copy = button('Скопировать ключ', async () => { const value = await ensureKey(); try { await navigator.clipboard.writeText(value); copy.textContent = 'Скопировано'; setTimeout(() => copy.textContent = 'Скопировать ключ', 2000); } catch { details.open = true; text.focus(); text.select(); message('Скопируйте выделенный ключ.'); } });
    const download = el('a', 'Скачать ' + (item.key_format === 'wireguard' ? '.conf' : item.key_format === 'openvpn' ? '.ovpn' : '.txt')); download.href = 'api/router-key.php?' + new URLSearchParams({ id: item.id });
    const actions = el('div', null, 'order-buttons'); actions.append(copy, download); card.append(actions); return { card, ensureKey };
  }
  async function loadRouterKeys() {
    const data = await api('router_keys'); $('router-keys-list').replaceChildren();
    if (!data.keys.length) $('router-keys-list').append(el('p', 'Ключей для роутера пока нет. Администратор добавит их в ваш кабинет.', 'account-muted'));
    for (const key of data.keys) { const { card } = routerKeyCard(key); if (key.assigned_at) card.append(el('p', 'Выдан: ' + date(key.assigned_at), 'account-muted')); $('router-keys-list').append(card); }
  }
  $('refresh-router-keys').onclick = e => run(e.target, loadRouterKeys);
  async function loadRouterKeyUsers(query = '', selectedId = 0) {
    const select = $('router-key-user'); select.disabled = true; if (!selectedId) select.value = '';
    try { const data = await api('admin_user_search', undefined, { q: query }); select.replaceChildren(new Option('Выберите клиента', '')); for (const item of data.users) { assignmentUsers.set(Number(item.id), item); select.append(new Option('#' + item.id + ' · ' + item.first_name + ' ' + item.last_name + ' · ' + item.email, String(item.id))); } select.value = selectedId ? String(selectedId) : ''; routerKeyUsersLoaded = true; $('router-key-user-hint').textContent = data.users.length >= data.limit ? 'Уточните поиск, если нужного клиента нет среди первых ' + data.limit + '.' : 'Найдено клиентов: ' + data.users.length + '. Проверьте получателя.'; }
    finally { select.disabled = false; }
  }
  async function chooseRouterKeyRecipient(item) { await selectAdmin('router-keys'); $('router-key-user-query').value = item.email; await loadRouterKeyUsers(item.email, Number(item.id)); $('router-key-assignment').scrollIntoView({ behavior: 'smooth', block: 'start' }); $('router-key-form').elements.label.focus(); }
  $('router-key-user-search').onsubmit = e => { e.preventDefault(); run(e.submitter, () => loadRouterKeyUsers($('router-key-user-query').value)); };
  $('router-key-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => {
    const select = $('router-key-user'), recipientId = Number(select.value); if (!recipientId || select.disabled) throw Error('Выберите получателя ключа.');
    const data = Object.fromEntries(new FormData(e.target)); data.user_id = recipientId; const result = await api('admin_assign_router_key', data);
    e.target.elements.label.value = ''; e.target.elements.key_value.value = ''; await loadAdminRouterKeys();
    const recipient = assignmentUsers.get(recipientId); message(result.already_assigned ? 'Этот ключ уже был назначен выбранному клиенту. Дубликат не создан.' : 'Ключ выдан клиенту ' + (recipient ? recipient.email : '#' + recipientId) + ' и доступен в разделе «Ключи для роутера».');
  }); };
  $('router-key-search').onsubmit = e => { e.preventDefault(); pages['router-keys'] = 0; run(e.submitter, loadAdminRouterKeys); };
  async function loadAdminRouterKeys() {
    if (!routerKeyUsersLoaded) await loadRouterKeyUsers(); const data = await api('admin_router_keys', undefined, { q: $('router-key-query').value, page: pages['router-keys'] }); $('admin-router-keys').replaceChildren();
    if (!data.keys.length) $('admin-router-keys').append(el('p', 'Ключей пока нет. Вставьте готовый ключ в форму выше и выберите клиента.', 'account-muted'));
    for (const key of data.keys) {
      const { card, ensureKey } = routerKeyCard(key); card.append(el('p', key.user_id ? 'Клиент: ' + key.first_name + ' ' + key.last_name + ' · ' + key.email : 'Ключ свободен — привязка снята', 'account-muted'));
      if (key.user_id) card.append(button('Снять привязку ключа', async () => { if (!confirm('Снять привязку ключа «' + key.label + '» у ' + key.email + '? Он исчезнет из кабинета клиента и останется в базе.')) return; await api('admin_unassign_router_key', { id: Number(key.id), user_id: Number(key.user_id) }); await loadAdminRouterKeys(); message('Привязка ключа снята.'); }, 'account-secondary account-danger'));
      else card.append(button('Выдать ключ клиенту', async () => { const value = await ensureKey(); const form = $('router-key-form'); form.elements.label.value = key.label; form.elements.key_format.value = key.key_format; form.elements.key_value.value = value; $('router-key-assignment').scrollIntoView({ behavior: 'smooth', block: 'start' }); $('router-key-user').focus(); }));
      card.append(button('Удалить ключ', async () => {
        if (!confirm('Удалить ключ «' + key.label + '» из базы кабинета' + (key.email ? ' и убрать его у ' + key.email : '') + '?')) return;
        await api('admin_delete_router_key', { id: Number(key.id), user_id: key.user_id === null ? null : Number(key.user_id) }); await loadAdminRouterKeys(); message('Ключ удалён из базы кабинета.');
      }, 'account-secondary account-danger'));
      $('admin-router-keys').append(card);
    }
    pager('router-keys-pager', 'router-keys', data.keys.length, data.total);
  }
  function termText(sub, seconds) {
    if (sub.term_kind === 'lifetime') return 'Бессрочная подписка';
    if (sub.term_kind !== 'fixed' || seconds === null || seconds === undefined) return 'Срок не указан';
    if (seconds <= 0) return 'Срок истёк';
    const days = Math.floor(seconds / 86400), hours = Math.floor(seconds % 86400 / 3600), minutes = Math.floor(seconds % 3600 / 60);
    return days ? 'Осталось: ' + days + ' дн. ' + hours + ' ч.' : hours ? 'Осталось: ' + hours + ' ч. ' + minutes + ' мин.' : minutes ? 'Осталось: ' + minutes + ' мин.' : 'Осталось меньше минуты';
  }
  function subscriptionTermBadge(sub) {
    const badge = el('span', termText(sub, sub.remaining_seconds), 'subscription-term'); badge.dataset.termKind = sub.term_kind || 'unspecified';
    if (sub.term_kind === 'fixed' && Number.isFinite(sub.remaining_seconds)) badge.dataset.termDeadline = String(performance.now() + sub.remaining_seconds * 1000);
    badge.dataset.expired = String(!!sub.is_expired); return badge;
  }
  function refreshTermBadges() {
    document.querySelectorAll('[data-term-deadline]').forEach(badge => {
      const seconds = Math.max(0, Math.ceil((Number(badge.dataset.termDeadline) - performance.now()) / 1000)); const text = termText({ term_kind: 'fixed' }, seconds); if (badge.textContent !== text) badge.textContent = text; badge.dataset.expired = String(seconds <= 0);
      const card = badge.closest('.subscription-card'); if (seconds <= 0 && card) {
        const link = card.querySelector('.subscription-link[data-hide-expired=true]'); if (link) { link.remove(); if (!card.querySelector('.subscription-expired-note')) card.append(el('p', 'Срок подписки истёк. Для продления выберите тариф или напишите в поддержку.', 'account-note subscription-expired-note')); }
      }
    });
  }
  setInterval(() => { if (!document.hidden) refreshTermBadges(); }, 15000); document.addEventListener('visibilitychange', refreshTermBadges);
  function termDateInput(value) {
    if (!value) return ''; const d = new Date(value.replace(' ', 'T') + 'Z'), two = n => String(n).padStart(2, '0');
    return d.getFullYear() + '-' + two(d.getMonth() + 1) + '-' + two(d.getDate()) + 'T' + two(d.getHours()) + ':' + two(d.getMinutes());
  }
  $('terms-search').onsubmit = e => { e.preventDefault(); pages.terms = 0; run(e.submitter, loadAdminTerms); };
  async function chooseSubscriptionTerm(sub) { $('terms-query').value = sub.name; pages.terms = 0; await selectAdmin('terms'); $('admin-terms').scrollIntoView({ behavior: 'smooth', block: 'start' }); }
  async function loadAdminTerms() {
    const data = await api('admin_subscription_terms', undefined, { q: $('terms-query').value.trim(), page: pages.terms }), target = $('admin-terms'); target.replaceChildren();
    if (!data.subscriptions.length) target.append(el('p', 'Подписки не найдены.', 'account-muted'));
    for (const sub of data.subscriptions) {
      const card = el('article', null, 'subscription-card term-editor'); card.dataset.termSubscriptionId = sub.id;
      card.append(el('h3', sub.name), el('p', sub.user_id ? sub.first_name + ' ' + sub.last_name + ' · ' + sub.email : 'Свободная подписка', 'account-muted'), subscriptionTermBadge(sub));
      if (sub.expires_at) card.append(el('p', 'Окончание: ' + date(sub.expires_at), 'account-muted'));
      const form = el('form'), modeLabel = el('label', 'Настройка срока'), mode = el('select'); mode.name = 'mode'; for (const [value, label] of [['lifetime', 'Бессрочная'], ['until', 'До указанной даты'], ['extend', 'Продлить на несколько месяцев']]) mode.append(new Option(label, value)); modeLabel.append(mode); mode.value = sub.term_kind === 'fixed' ? 'until' : 'lifetime';
      const dateLabel = el('label', 'Дата и время окончания'), input = el('input'); input.type = 'datetime-local'; input.name = 'expires_at'; input.value = termDateInput(sub.expires_at); dateLabel.append(input);
      const monthsLabel = el('label', 'Количество месяцев'), months = el('select'); months.name = 'months'; for (let n = 1; n <= 12; n++) months.append(new Option(String(n), String(n))); monthsLabel.append(months);
      const sync = () => { dateLabel.hidden = mode.value !== 'until'; input.required = mode.value === 'until'; monthsLabel.hidden = mode.value !== 'extend'; }; mode.onchange = sync; sync();
      const submit = el('button', 'Сохранить срок', 'account-primary'); submit.type = 'submit'; form.append(modeLabel, dateLabel, monthsLabel, submit);
      form.onsubmit = e => { e.preventDefault(); run(submit, async () => {
        const body = { id: Number(sub.id), user_id: sub.user_id === null ? null : Number(sub.user_id), mode: mode.value };
        if (mode.value === 'until') { const parsed = new Date(input.value); if (!Number.isFinite(parsed.getTime())) throw Error('Укажите дату и время окончания.'); body.expires_at = parsed.toISOString().replace(/\.\d{3}Z$/, 'Z'); }
        if (mode.value === 'extend') body.months = Number(months.value);
        await api('admin_subscription_term_save', body); await loadAdminTerms(); message('Срок подписки «' + sub.name + '» сохранён.');
      }); }; card.append(form); target.append(card);
    }
    pager('terms-pager', 'terms', data.subscriptions.length, data.total);
  }
  let subscriptionHostPreview = null, subscriptionHostRequest = 0;
  function clearSubscriptionHostPreview() {
    subscriptionHostRequest++; subscriptionHostPreview = null; $('subscription-host-preview').hidden = true; $('subscription-host-items').replaceChildren(); $('subscription-host-summary').textContent = '';
  }
  $('subscription-host-form').oninput = clearSubscriptionHostPreview;
  $('subscription-host-form').onsubmit = e => {
    e.preventDefault(); run(e.submitter, async () => {
      clearSubscriptionHostPreview(); const request = subscriptionHostRequest, input = Object.fromEntries(new FormData(e.target)), data = await api('admin_subscription_host_preview', input);
      if (!user?.is_admin || request !== subscriptionHostRequest || JSON.stringify(input) !== JSON.stringify(Object.fromEntries(new FormData(e.target)))) return;
      subscriptionHostPreview = data; $('subscription-host-preview').hidden = false;
      $('subscription-host-summary').textContent = 'К замене: ' + data.changed + '. Уже указан этот адрес: ' + data.already_same + '. Пропущено: ' + data.skipped + '. Новый адрес: ' + data.new_host + '.';
      for (const item of data.items) {
        const box = el('div', null, 'router-host-preview-item'); box.append(el('strong', '#' + item.id + ' · ' + item.name), el('p', item.owner || (item.user_id ? 'Клиент #' + item.user_id : 'Свободная подписка'), 'account-muted'), el('p', item.old_host + ' → ' + item.new_host)); $('subscription-host-items').append(box);
      }
      if (data.changed > data.items.length) $('subscription-host-items').append(el('p', 'Показаны первые ' + data.items.length + ' из ' + data.changed + ' изменений.', 'account-muted'));
      $('subscription-host-apply').hidden = !data.changed;
      if (!data.changed) message('Подходящих подписок для изменения нет. Проверьте адрес и фильтр.');
    });
  };
  $('subscription-host-apply').onclick = e => run(e.target, async () => {
    const preview = subscriptionHostPreview; if (!preview) throw Error('Сначала выполните предпросмотр замены.');
    if (!confirm('Заменить домен / IP на «' + preview.new_host + '» в ' + preview.changed + ' ссылках подписок? Порт, путь и остальные параметры сохранятся.')) return;
    const data = await api('admin_subscription_host_apply', { preview_token: preview.preview_token }); clearSubscriptionHostPreview(); await loadAdminSubscriptions(); await updateSummary(); message('Адрес обновлён. Изменено подписок: ' + data.changed + '. В кабинетах клиентов обновятся ссылки, дата изменения и уведомления.');
  });

  function clearRouterHostPreview() {
    routerHostPreview = null; $('router-host-preview').hidden = true; $('router-host-items').replaceChildren(); $('router-host-summary').textContent = '';
  }
  $('router-host-form').oninput = clearRouterHostPreview;
  $('router-host-form').onsubmit = e => {
    e.preventDefault(); run(e.submitter, async () => {
      clearRouterHostPreview(); const input = Object.fromEntries(new FormData(e.target)), data = await api('admin_router_host_preview', input);
      if (!user?.is_admin || JSON.stringify(input) !== JSON.stringify(Object.fromEntries(new FormData(e.target)))) return;
      routerHostPreview = data; $('router-host-preview').hidden = false;
      $('router-host-summary').textContent = 'К замене: ' + data.changed + '. Уже указан этот адрес: ' + data.already_same + '. Пропущено: ' + data.skipped + '. Новый адрес: ' + data.new_host + '.';
      for (const item of data.items) {
        const box = el('div', null, 'router-host-preview-item'); box.append(el('strong', '#' + item.id + ' · ' + item.label), el('p', item.owner || (item.user_id ? 'Клиент #' + item.user_id : 'Свободный ключ'), 'account-muted'), el('p', item.old_host + ' → ' + item.new_host)); $('router-host-items').append(box);
      }
      if (data.changed > data.items.length) $('router-host-items').append(el('p', 'Показаны первые ' + data.items.length + ' из ' + data.changed + ' изменений.', 'account-muted'));
      $('router-host-apply').hidden = !data.changed;
      if (!data.changed) message('Подходящих ключей для изменения нет. Проверьте адрес и фильтр.');
    });
  };
  $('router-host-apply').onclick = e => run(e.target, async () => {
    const preview = routerHostPreview; if (!preview) throw Error('Сначала выполните предпросмотр замены.');
    if (!confirm('Заменить адрес на «' + preview.new_host + '» в ' + preview.changed + ' VLESS-ключах?')) return;
    const data = await api('admin_router_host_apply', { preview_token: preview.preview_token }); clearRouterHostPreview(); await loadAdminRouterKeys(); message('Адрес обновлён. Изменено ключей: ' + data.changed + '. Клиенты получат обновлённые ключи после обновления раздела в кабинете.');
  });
  function renderServiceNoticeSettings(data) {
    const form = $('service-notice-form'); form.elements.enabled.checked = data.enabled; form.elements.message.value = data.message; form.elements.level.value = data.level || 'warning';
    $('service-notice-preview').textContent = data.message || 'Текст не задан.';
    $('service-notice-saved').textContent = (data.enabled ? 'Статус показывается.' : 'Статус скрыт.') + (data.updated_at ? ' Сохранено: ' + date(data.updated_at) : '');
  }
  async function loadServiceNotice() { const data = await api('admin_service_notice'); if (user?.is_admin) renderServiceNoticeSettings(data); }
  $('service-notice-form').onsubmit = e => {
    e.preventDefault(); run(e.submitter, async () => {
      const form = e.target, data = await api('admin_service_notice_save', { enabled: form.elements.enabled.checked, message: form.elements.message.value, level: form.elements.level.value });
      if (!user?.is_admin) return; renderServiceNoticeSettings(data); window.MKRServiceNotice?.refresh();
      message(data.enabled ? 'Статус сохранён. Цвет и текст обновятся на открытых страницах в течение минуты.' : 'Статус скрыт. Текст и цвет сохранены для следующего включения.');
    });
  };
  async function loadMonitoring() {
    const data = await api('admin_monitor'); const form = $('monitor-form');
    form.elements.enabled.checked = data.enabled; form.elements.subscription_url.value = ''; form.elements.main_host.value = data.main_host; form.elements.main_port.value = data.main_port;
    $('monitor-source-hint').textContent = data.subscription_set ? 'Подписка сохранена. Оставьте поле пустым, чтобы сохранить текущую ссылку, или вставьте новую.' : 'Вставьте одну ссылку-подписку. Служба получит из неё список подключений.';
    const sourceMessages = { fetch_error: ' Не удалось скачать подписку.', parse_error: ' Нужна подписка в виде списка ссылок или Base64.', limit_exceeded: ' В подписке более 100 подключений.' };
    $('monitor-status').textContent = (data.enabled ? 'Мониторинг включён.' : 'Мониторинг отключён.') + (data.token_ready ? ' Токен создан.' : ' Создайте токен для Ubuntu.') + (data.reported_at ? ' Последний отчёт: ' + date(data.reported_at) + '.' : ' Отчётов пока нет.') + (sourceMessages[data.source_status] || '');
    $('monitor-endpoint').textContent = 'Адрес для установки: ' + new URL('api/monitor-agent.php', location.href).href;
  }
  $('monitor-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => {
    const form = e.target; await api('admin_monitor_save', { enabled: form.elements.enabled.checked, subscription_url: form.elements.subscription_url.value.trim(), main_host: form.elements.main_host.value.trim(), main_port: Number(form.elements.main_port.value) });
    form.elements.subscription_url.value = ''; await loadMonitoring(); message('Настройки мониторинга сохранены.');
  }); };
  $('monitor-create-token').onclick = e => run(e.target, async () => {
    if (!confirm('Создать новый токен? Прежний перестанет работать, его потребуется заменить в службе Ubuntu.')) return;
    const data = await api('admin_monitor_token', {}); $('monitor-token-value').value = data.token; $('monitor-token-box').hidden = false; await loadMonitoring(); message('Токен создан. Скопируйте его для установки службы.');
  });
  $('monitor-copy-token').onclick = e => run(e.target, async () => {
    try { await navigator.clipboard.writeText($('monitor-token-value').value); message('Токен скопирован.'); }
    catch { $('monitor-token-value').focus(); $('monitor-token-value').select(); message('Скопируйте выделенный токен.'); }
  });
  async function loadMailDiagnostics() {
    const data = await api('admin_mail_diagnostics'), target = $('mail-diagnostics'); target.replaceChildren();
    if (data.issues.length) { const list = el('ul', null, 'mail-issues'); data.issues.forEach(issue => list.append(el('li', issue))); target.append(list); }
    target.append(el('p', data.worker?.mail_checked_at ? 'Последняя обработка очереди: ' + date(data.worker.mail_checked_at) : 'Фоновая обработка очереди ещё не запускалась.', 'account-note'));
    target.append(el('p', 'Служба Ubuntu отправляет письма раз в минуту, в том числе при выключенном мониторинге. Также можно использовать cron хостинга.', 'account-muted'));
    for (const item of data.messages) { const box = el('div', null, 'record'), states = { pending: 'В очереди', retry: 'Повторная попытка', sending: 'Отправляется', sent: 'Принято почтовым сервером', failed: 'Требуется повторить отправку' }; box.append(el('p', 'Письмо #' + item.id + ' · ' + (states[item.status] || item.status)), el('p', 'Попыток: ' + item.attempts, 'account-muted')); if (item.last_error) box.append(el('p', item.last_error, 'mail-warning')); if (item.status === 'retry') box.append(el('p', 'Следующая попытка: ' + date(item.next_attempt_at), 'account-muted')); target.append(box); }
  }
  $('mail-test').onclick = e => run(e.target, async () => { const data = await api('mail_test', {}); await updateSummary(); await loadMailDiagnostics(); message(data.ready ? 'Тестовое письмо поставлено в очередь отправки.' : 'Письмо сохранено в очереди. Сначала настройте отправку в private/config.php.'); });
  $('mail-retry').onclick = e => run(e.target, async () => { const data = await api('mail_retry', {}); await updateSummary(); await loadMailDiagnostics(); message(data.ready ? 'Почтовым сервером принято: ' + data.sent + '. Не удалось отправить: ' + data.failed + '.' + (data.errors?.length ? ' ' + data.errors.join(' ') : '') : 'Сначала исправьте настройки почты: ' + (data.issues || []).join(' ')); });
  const ticketLabels = { open: 'В обработке', waiting: 'Ожидает вашего ответа', closed: 'Решено' };
  let ticketScope = 'active', notificationPage = 0, notificationBaseline = null, toastTimer;
  let templates = new Map(), pushRegistration = null, pushInfo = null;
  function resourceUpdated(item) { return el('p', item.content_updated_at ? 'Обновлено: ' + date(item.content_updated_at) : 'Дата обновления пока не зафиксирована', 'account-muted resource-updated'); }
  function smallPager(id, page, count, total, size, change) {
    const target = $(id); target.replaceChildren(); if (total <= size && page === 0) return;
    const prev = button('← Назад', () => change(page - 1)), next = button('Далее →', () => change(page + 1)); prev.disabled = page === 0; next.disabled = (page + 1) * size >= total;
    const box = el('div', null, 'pager'); box.append(prev, el('span', 'Страница ' + (page + 1)), next); target.append(box);
  }
  for (const [id, scope] of [['tickets-active', 'active'], ['tickets-archive', 'archive']]) $(id).onclick = e => run(e.target, async () => { ticketScope = scope; pages.support = 0; closeConversation(); await loadTickets(true); });
  $('reopen-ticket').onclick = e => run(e.target, async () => { if (!conversation?.asAdmin) return; await api('set_ticket_status', { id: Number(conversation.id), status: 'open' }); await loadConversation(); await loadTickets(true); message('Обращение возвращено в обработку.'); });
  for (const id of ['clients-router-filter', 'clients-term-filter']) $(id).onchange = e => run(e.target, async () => { pages.clients = 0; await loadClients(); });
  function showNotificationToast() {
    const box = $('notification-toast'); box.replaceChildren(el('span', 'У вас новые уведомления'), button('Открыть', async () => { box.hidden = true; await openNotifications(); })); box.hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => box.hidden = true, 10000);
  }
  function notificationSummary(data, toast = false) {
    badge($('notifications-unread'), data.unread_notifications); badge($('notifications-tab-unread'), data.unread_notifications);
    $('notifications-button').setAttribute('aria-label', 'Уведомления' + (data.unread_notifications ? ': непрочитанных ' + data.unread_notifications : ''));
    const latest = Number(data.latest_notification_id || 0); if (toast && notificationBaseline !== null && latest > notificationBaseline) showNotificationToast(); notificationBaseline = Math.max(notificationBaseline || 0, latest);
  }
  async function openNotifications() { clientTab = 'notifications'; await setMode('client'); $('cabinet-notifications').scrollIntoView({ behavior: 'smooth', block: 'start' }); }
  $('notifications-button').onclick = e => run(e.currentTarget, openNotifications);
  $('notifications-refresh').onclick = e => run(e.target, async () => { await loadNotifications(); await loadPushDevice(); });
  $('notifications-read-all').onclick = e => run(e.target, async () => { const data = await api('notification_read', { all: true }); notificationSummary(data); await loadNotifications(); });
  async function openNotification(item) {
    const data = await api('notification_read', { id: Number(item.id) }); notificationSummary(data);
    if (item.target === 'ticket' && item.target_id) { clientTab = 'support'; await setMode('client'); await openConversation(Number(item.target_id), false); }
    else if (item.target.startsWith('admin-') && user?.is_admin) {
      if (item.target === 'admin-users' && item.target_id) { $('accounts-query').value = String(item.target_id); pages.accounts = 0; adminTab = 'accounts'; }
      else adminTab = ({ 'admin-payments': 'payments', 'admin-ticket': 'support', 'admin-mail': 'mail' })[item.target] || 'support';
      if (adminTab === 'support') { ticketScope = 'active'; pages.support = 0; }
      await setMode('admin'); if (item.target === 'admin-ticket' && item.target_id) await openConversation(Number(item.target_id), true);
      if (item.target_ref) { const card = Array.from($('admin-orders').children).find(n => n.dataset.orderId === item.target_ref); if (card) card.scrollIntoView({ behavior: 'smooth', block: 'start' }); else message('Номер заявки: ' + item.target_ref + '. Заявка может находиться на следующей странице оплат.'); }
    } else if (['subscriptions', 'router-keys'].includes(item.target)) { clientTab = item.target; await setMode('client'); }
    else await loadNotifications();
  }
  async function loadNotifications() {
    const owner = user?.id, data = await api('notifications', undefined, { page: notificationPage }); if (owner !== user?.id) return; notificationSummary(data);
    const target = $('notifications-list'); target.replaceChildren(); if (!data.notifications.length) target.append(el('p', 'Пока нет уведомлений. Здесь появятся новые события вашего кабинета.', 'account-muted'));
    for (const item of data.notifications) {
      const box = el('article', null, 'record notification-card' + (!item.read_at ? ' record-new' : '')); box.dataset.notificationId = item.id;
      box.append(el('h3', item.title), el('p', item.body, 'notification-body'), el('p', date(item.created_at) + (item.read_at ? ' · Прочитано' : ' · Новое'), 'account-muted'));
      const actions = el('div', null, 'order-buttons'); actions.append(button('Открыть', () => openNotification(item)));
      if (!item.read_at) actions.append(button('Прочитано', async () => { const result = await api('notification_read', { id: Number(item.id) }); notificationSummary(result); await loadNotifications(); })); box.append(actions); target.append(box);
    }
    smallPager('notifications-pager', notificationPage, data.notifications.length, data.total, 50, async page => { notificationPage = page; await loadNotifications(); });
  }
  async function openNotificationDeepLink() {
    const query = new URLSearchParams(location.search), raw = query.get('notification'); if (!raw || !/^\d+$/.test(raw)) return;
    query.delete('notification'); history.replaceState(null, '', location.pathname + (query.size ? '?' + query : '') + '#notifications');
    try { const data = await api('notification', undefined, { id: raw }); await openNotification(data.notification); } catch (e) { if (e.status !== 404) throw e; message('Это уведомление больше недоступно. Остальные события — в разделе «Уведомления».'); }
  }
  async function refreshTemplates(forReply = false) {
    const data = await api('admin_reply_templates'); templates = new Map(data.templates.map(t => [Number(t.id), t]));
    const select = $('reply-template-select'), previous = select.value; select.replaceChildren(new Option('Выберите шаблон', '')); for (const t of data.templates) select.append(new Option(t.title, String(t.id))); select.value = previous;
    $('reply-template-hint').textContent = data.templates.length ? 'Шаблон можно отредактировать перед отправкой.' : 'Создайте заготовки в разделе «Шаблоны ответов».';
    if (forReply) return;
    const target = $('admin-templates'); target.replaceChildren(); if (!data.templates.length) target.append(el('p', 'Шаблонов пока нет. Добавьте первый в форме выше.', 'account-muted'));
    for (const item of data.templates) { const box = el('article', null, 'record'); box.append(el('h3', item.title), el('p', item.body, 'template-body'), el('p', 'Изменён: ' + date(item.updated_at), 'account-muted'));
      const actions = el('div', null, 'order-buttons'); actions.append(button('Редактировать', async () => { const form = $('template-form'); form.elements.id.value = item.id; form.elements.title.value = item.title; form.elements.body.value = item.body; $('template-form-title').textContent = 'Редактирование шаблона'; form.scrollIntoView({ behavior: 'smooth', block: 'start' }); }), button('Удалить', async () => { if (!confirm('Удалить шаблон «' + item.title + '»?')) return; await api('admin_template_delete', { id: Number(item.id) }); if (Number($('template-form').elements.id.value) === Number(item.id)) newTemplate(); await refreshTemplates(); }, 'account-secondary account-danger')); box.append(actions); target.append(box); }
  }
  function newTemplate() { $('template-form').reset(); $('template-form').elements.id.value = ''; $('template-form-title').textContent = 'Новый шаблон'; }
  $('template-new').onclick = newTemplate;
  $('template-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => { const form = e.target; await api('admin_template_save', { id: form.elements.id.value ? Number(form.elements.id.value) : null, title: form.elements.title.value, body: form.elements.body.value }); newTemplate(); await refreshTemplates(); message('Шаблон сохранён.'); }); };
  $('reply-template-insert').onclick = e => run(e.target, async () => { if (!conversation?.asAdmin) return; const template = templates.get(Number($('reply-template-select').value)); if (!template) throw Error('Выберите шаблон ответа.'); const input = $('reply-form').elements.message; input.value += (input.value ? '\n\n' : '') + template.body; if (input.value.length > 4000) { input.value = input.value.slice(0, 4000); message('Текст ограничен 4000 символами. Проверьте ответ перед отправкой.'); } input.focus(); });
  $('accounts-search').onsubmit = e => { e.preventDefault(); pages.accounts = 0; run(e.submitter, loadAccounts); };
  async function loadAccounts() {
    const target = $('admin-accounts'); target.replaceChildren(); const data = await api('admin_accounts', undefined, { q: $('accounts-query').value.trim(), page: pages.accounts }); $('accounts-total').textContent = 'Найдено аккаунтов: ' + data.total;
    if (!data.accounts.length) target.append(el('p', 'Аккаунты не найдены.', 'account-muted'));
    for (const item of data.accounts) {
      const self = Number(item.id) === Number(user.id), box = el('article', null, 'record account-management'); box.dataset.accountId = item.id;
      box.append(el('h3', '#' + item.id + ' · ' + item.first_name + ' ' + item.last_name + (self ? ' · Ваш аккаунт' : '')), el('p', item.email + ' · ' + item.telegram, 'account-muted'), el('p', 'Зарегистрирован: ' + date(item.created_at), 'account-muted'));
      const details = el('details', null, 'issue-form'); details.append(el('summary', 'Сбросить пароль')); const form = el('form'), label = el('label', 'Новый пароль'), input = el('input'); input.type = 'password'; input.name = 'password'; input.autocomplete = 'new-password'; input.required = true; input.minLength = 6; input.maxLength = 72; label.append(input);
      const submit = el('button', 'Сохранить новый пароль', 'account-primary'); submit.type = 'submit'; form.append(label, el('p', 'От 6 символов, до 72 байт. Все прежние входы будут завершены. Передайте новый пароль клиенту лично.', 'account-muted'), submit);
      form.onsubmit = e => { e.preventDefault(); run(submit, async () => { if (!confirm('Сбросить пароль аккаунта ' + item.email + '? Все прежние входы будут завершены.' + (self ? ' Вам потребуется войти заново.' : ''))) return; const result = await api('admin_account_password', { id: Number(item.id), password: input.value }); input.value = ''; if (result.logged_out) { signedOut(); message('Ваш пароль изменён. Войдите с новым паролем.'); } else { details.open = false; message('Пароль изменён. Прежние входы клиента завершены.'); } }); }; details.append(form); box.append(details);
      if (!self) { const remove = el('details', null, 'issue-form'); remove.append(el('summary', 'Удалить аккаунт')); const removeForm = el('form'), choiceLabel = el('label', 'Подписки и ключи клиента'), select = el('select'); select.append(new Option('Снять привязки и оставить в общей базе', 'release'), new Option('Полностью удалить из базы кабинета', 'delete')); choiceLabel.append(select); const removeButton = el('button', 'Удалить аккаунт', 'account-secondary account-danger'); removeButton.type = 'submit'; removeForm.append(choiceLabel, el('p', 'Вход будет закрыт, аккаунт исчезнет из списков. История оплат и обращений сохранится с пометкой «Удалённый аккаунт».', 'account-muted'), removeButton);
        removeForm.onsubmit = e => { e.preventDefault(); run(removeButton, async () => { if (!confirm('Удалить аккаунт ' + item.email + '? ' + (select.value === 'delete' ? 'Его подписки и ключи будут удалены из базы кабинета.' : 'Его подписки и ключи станут свободными.'))) return; await api('admin_account_delete', { id: Number(item.id), resources: select.value }); assignmentUsers.clear(); assignmentUsersLoaded = routerKeyUsersLoaded = keeneticUsersLoaded = false; await loadAccounts(); message('Аккаунт удалён. ' + (select.value === 'delete' ? 'Его подписки и ключи удалены из базы кабинета.' : 'Подписки и ключи остались в общей базе без владельца.')); }); }; remove.append(removeForm); box.append(remove); } target.append(box);
    }
    pager('accounts-pager', 'accounts', data.accounts.length, data.total);
  }
  async function accountWorker() { if (!pushRegistration) { await navigator.serviceWorker.register('account-sw.js'); pushRegistration = await navigator.serviceWorker.ready; } return pushRegistration; }
  function fromBase64Url(value) { return Uint8Array.from(atob(value.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - value.length % 4) % 4)), c => c.charCodeAt(0)); }
  function bytesEqual(a, b) { const x = new Uint8Array(a || []), y = new Uint8Array(b || []); return x.length === y.length && x.every((v, i) => v === y[i]); }
  function pushSupported() { return window.isSecureContext && 'Notification' in window && 'serviceWorker' in navigator && 'PushManager' in window; }
  async function loadPushDevice() {
    $('push-enable').hidden = $('push-disable').hidden = true; const hint = $('push-device-hint');
    if (!pushSupported()) { hint.textContent = 'Этот браузер не поддерживает push. На iPhone/iPad запустите сайт с экрана «Домой». Уведомления в кабинете доступны всегда.'; return; }
    const data = await api('push_settings'); pushInfo = data;
    if (!data.ready) { hint.textContent = 'Push пока не включены администратором. Уведомления остаются доступны в кабинете.'; return; }
    const reg = await accountWorker(), sub = await reg.pushManager.getSubscription(); const status = sub ? await api('push_device_status', { endpoint: sub.endpoint }) : { registered: false };
    $('push-disable').hidden = !sub; const denied = Notification.permission === 'denied'; $('push-enable').hidden = denied || (status.registered && !!sub && bytesEqual(sub.options.applicationServerKey, fromBase64Url(data.public_key)));
    hint.textContent = denied ? 'Уведомления запрещены в настройках браузера. Разрешите их для этого сайта и обновите раздел.' : status.registered ? 'Push включены для вашего аккаунта на этом устройстве.' : 'Включите push, чтобы получать новые события, когда сайт закрыт.';
  }
  $('push-enable').onclick = e => run(e.target, async () => {
    if (!pushSupported() || !pushInfo?.ready) throw Error('Откройте раздел заново и проверьте доступность push.');
    // Permission request starts directly in this click handler, before any network await (Safari).
    const permission = await Notification.requestPermission(); if (permission !== 'granted') { await loadPushDevice(); return; }
    const reg = await accountWorker(); let sub = await reg.pushManager.getSubscription(); const key = fromBase64Url(pushInfo.public_key);
    if (sub && !bytesEqual(sub.options.applicationServerKey, key)) { try { await api('push_unsubscribe', { endpoint: sub.endpoint }); } catch {} await sub.unsubscribe(); sub = null; }
    if (!sub) sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key }); await api('push_subscribe', sub.toJSON()); await loadPushDevice(); message('Push включены на этом устройстве.');
  });
  $('push-disable').onclick = e => run(e.target, async () => { const reg = await accountWorker(), sub = await reg.pushManager.getSubscription(); if (sub) { await api('push_unsubscribe', { endpoint: sub.endpoint }); await sub.unsubscribe(); } await loadPushDevice(); message('Push отключены на этом устройстве.'); });
  async function disconnectPushOnLogout() {
    if (!pushSupported()) return; try { const reg = await navigator.serviceWorker.getRegistration(new URL('account-sw.js', location.href)), sub = await reg?.pushManager.getSubscription(); if (sub) { await api('push_unsubscribe', { endpoint: sub.endpoint }); await sub.unsubscribe(); } } catch { /* Logout must remain possible if a push service is unavailable. */ }
  }
  function renderAdminPush(data) {
    $('push-settings-form').elements.enabled.checked = data.enabled; $('push-setup').hidden = data.keys_ready;
    const target = $('push-admin-status'); target.replaceChildren(el('p', data.ready ? 'Push включены. Подключено устройств: ' + data.devices + '.' : data.keys_ready ? 'Ключи созданы. Включите отправку и проверьте требования PHP.' : 'Создайте ключи один раз, затем разрешите push.', 'account-note'));
    for (const issue of data.issues) target.append(el('p', issue, 'mail-warning'));
    target.append(el('p', data.checked_at ? 'Последняя обработка: ' + date(data.checked_at) : 'Очередь ещё не обрабатывалась.', 'account-muted'));
    const diag = $('push-diagnostics'); diag.replaceChildren(); const states = { pending: 'В очереди', retry: 'Повторная попытка', sending: 'Отправляется', sent: 'Принято push-сервисом', failed: 'Не отправлено', cancelled: 'Уже прочитано / отменено' };
    for (const item of data.messages) { const row = el('div', null, 'record'); row.append(el('p', 'Push #' + item.id + ' · ' + (states[item.status] || item.status)), el('p', 'Попыток: ' + item.attempts, 'account-muted')); if (item.last_error) row.append(el('p', item.last_error, 'mail-warning')); diag.append(row); }
  }
  async function loadAdminPush() { $('push-settings-form').inert = true; try { renderAdminPush(await api('admin_push_settings')); } finally { $('push-settings-form').inert = false; } }
  $('push-setup').onclick = e => run(e.target, async () => { renderAdminPush(await api('admin_push_setup', {})); message('Ключи push созданы и сохранены.'); });
  $('push-settings-form').onsubmit = e => { e.preventDefault(); run(e.submitter, async () => { renderAdminPush(await api('admin_push_save', { enabled: e.target.elements.enabled.checked })); message('Настройки push сохранены. Чтобы получать уведомления на своём устройстве, включите их в разделе «Уведомления».'); }); };
  $('push-retry').onclick = e => run(e.target, async () => { const data = await api('admin_push_retry', {}); await loadAdminPush(); message(data.ready ? 'Принято push-сервисом: ' + data.sent + '. Ошибок: ' + data.failed + '.' : 'Сначала включите push и проверьте требования PHP.'); });

  setInterval(async () => { if (!user || document.hidden) return; try { if (conversation) await loadConversation(); else { await updateSummary(); if (mode === 'client' && clientTab === 'support') await loadTickets(false); if (mode === 'admin' && adminTab === 'support') await loadTickets(true); if (mode === 'client' && clientTab === 'notifications') await loadNotifications(); } } catch (e) { if (e.status === 401) message('Сессия завершена. Войдите снова.', true); } }, 20000);
  (async () => { try { const data = await api('me'); user = data.user; authEpoch++; payment = data.payment; await showCabinet(); if (!user && new URLSearchParams(location.search).get('mode') === 'register') authTab(true); } catch (e) { message(e.message, true); document.querySelectorAll('#auth-section button[type=submit]').forEach(b => b.disabled = true); } })();
})();
