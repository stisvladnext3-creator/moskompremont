(() => {
  'use strict';
  const config = window.SITE_CONFIG || {};
  const byId = id => document.getElementById(id);
  const linksDialog = byId('links-dialog');
  const contactDialog = byId('contact-dialog');
  const mobileMenu = byId('mobile-menu');
  const maxLinks = 10;
  let controller = null;
  let requestVersion = 0;
  let preferredIndex = null;
  let returnFocus = null;

  function element(tag, text, className) {
    const node = document.createElement(tag);
    if (text) node.textContent = text;
    if (className) node.className = className;
    return node;
  }
  function icon(name, className = 'w-5 h-5') {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'icon ' + className);
    svg.setAttribute('aria-hidden', 'true');
    const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
    use.setAttribute('href', 'assets/icons.svg#' + name);
    svg.append(use);
    return svg;
  }
  function safeSubscription(value) {
    if (typeof value !== 'string' || !value || /[\s\u0000-\u001f]/.test(value)) return null;
    try {
      const url = new URL(value);
      return ['https:', 'http:', 'vless:', 'vmess:', 'trojan:', 'ss:', 'hysteria2:'].includes(url.protocol) && url.hostname ? value : null;
    } catch { return null; }
  }
  function contactUrl(value) {
    try {
      const url = new URL(value);
      return url.protocol === 'https:' && !url.username && !url.password ? url.href : null;
    } catch { return null; }
  }
  function validLinks(links) {
    return Array.isArray(links) && links.length >= 1 && links.length <= maxLinks && Array.from(links).every(safeSubscription);
  }
  // Also refresh the wording in pages installed before support for shorter lists.
  byId('dialog-description').textContent = 'Выберите доступную ссылку. Скопируйте её и используйте по инструкции для своего устройства.';
  const heroNote = document.querySelector('.hero-actions + p');
  if (heroNote) heroNote.textContent = 'Ссылки подключения · понятные инструкции · помощь с подключением';
  const stepNote = document.querySelector('.steps-grid [data-links-open] .text-xs');
  if (stepNote) stepNote.textContent = 'Доступные варианты';
  byId('year').textContent = new Date().getFullYear();

  const servers = Array.isArray(config.servers) ? config.servers : [];
  if (servers.length) byId('servers').replaceChildren();
  servers.forEach((server, index) => {
    if (!server || typeof server.name !== 'string') return;
    const card = element('article', '', 'service-card server-card');
    const top = element('div', '', 'flex justify-between items-center gap-3 mb-5');
    const symbol = element('div', '', 'glow-icon');
    symbol.append(icon('server', 'w-6 h-6 text-cyan-400'));
    top.append(symbol, element('span', String(index + 1).padStart(2, '0'), 'server-meta'));
    card.append(top, element('h3', server.name, 'text-xl font-bold text-white mb-2'));
    if (server.location) card.append(element('p', server.location, 'server-meta mb-3'));
    card.append(element('p', server.description || 'За информацией о подключении обратитесь к нам.', 'server-description text-sm text-gray-400 leading-relaxed mb-5'));
    const action = element('button', 'Получить ссылку', 'server-action btn-outline-neon px-5 py-3 text-sm inline-flex items-center gap-2');
    action.type = 'button';
    action.setAttribute('data-links-open', '');
    action.setAttribute('aria-haspopup', 'dialog');
    action.setAttribute('aria-controls', 'links-dialog');
    if (Number.isInteger(server.subscriptionIndex) && server.subscriptionIndex >= 0 && server.subscriptionIndex < maxLinks) action.dataset.linkIndex = String(server.subscriptionIndex);
    action.append(icon('arrow-up-right', 'w-4 h-4'));
    card.append(action);
    byId('servers').append(card);
  });

  if (window.MKRInstructions) window.MKRInstructions.render(byId('instructions'), config.instructions);

  const phone = typeof config.phone === 'string' && /^\+[1-9]\d{6,14}$/.test(config.phone) ? config.phone : '';
  const phoneLabel = typeof config.phoneLabel === 'string' && config.phoneLabel.trim() ? config.phoneLabel : phone;
  document.querySelectorAll('[data-contact-phone]').forEach(node => {
    if (phone) { node.textContent = phoneLabel; node.href = 'tel:' + phone; }
    else { node.textContent = 'Контакт скоро появится'; node.removeAttribute('href'); }
  });
  const channels = [
    { label: 'СМС', className: 'widget-sms', href: phone ? 'sms:' + phone : null, icon: 'message-square-text' },
    { label: 'Telegram', className: 'widget-telegram', href: contactUrl(config.telegramUrl), icon: 'telegram' },
    { label: 'WhatsApp', className: 'widget-whatsapp', href: contactUrl(config.whatsappUrl), icon: 'whatsapp' },
    { label: 'Макс', className: 'widget-maks', href: contactUrl(config.maxUrl), icon: 'max' }
  ];
  document.querySelectorAll('[data-contact-channels]').forEach(container => {
    channels.forEach(channel => {
      const link = element(channel.href ? 'a' : 'button', '', 'floating-widget ' + channel.className);
      link.setAttribute('aria-label', channel.label + (channel.href ? ': написать сообщение' : ': контакт ещё не добавлен'));
      link.title = channel.label;
      if (channel.href) {
        link.href = channel.href;
        if (!channel.href.startsWith('sms:')) { link.target = '_blank'; link.rel = 'noopener noreferrer'; }
      } else {
        link.type = 'button'; link.disabled = true; link.setAttribute('aria-disabled', 'true');
      }
      if (channel.icon === 'max') {
        const logo = element('span', '', 'max-logo');
        logo.setAttribute('aria-hidden', 'true');
        logo.append(icon('max'));
        const image = element('img');
        image.alt = '';
        image.setAttribute('data-optional-image', '');
        image.src = 'images/maxlogo.png';
        logo.append(image);
        link.append(logo);
      } else link.append(icon(channel.icon));
      link.append(element('span', channel.label));
      container.append(link);
    });
  });

  function closeDialogs() {
    document.querySelectorAll('dialog[open]').forEach(dialog => dialog.close());
  }
  function openContacts(trigger) {
    const previousTrigger = returnFocus;
    const fromDialog = trigger.closest('dialog');
    closeDialogs();
    returnFocus = fromDialog ? previousTrigger || byId('urgent') : trigger;
    contactDialog.showModal();
  }
  function setStatus(message, state = 'info') {
    byId('links-status').textContent = message;
    byId('links-status').dataset.state = state;
  }
  function renderLinks(links) {
    const container = byId('link-options');
    container.replaceChildren();
    const slots = links.length || maxLinks;
    container.style.gridTemplateColumns = links.length && links.length < 5 ? 'repeat(' + links.length + ', minmax(0, 1fr))' : '';
    for (let i = 0; i < slots; i++) {
      const url = safeSubscription(links[i]);
      const button = element('button', '', 'link-option');
      button.type = 'button'; button.disabled = !url;
      button.setAttribute('aria-label', 'Ссылка ' + (i + 1) + (url ? '' : ' пока недоступна'));
      button.setAttribute('aria-pressed', 'false');
      button.append(element('span', String(i + 1).padStart(2, '0')), element('small', 'Ссылка'));
      button.addEventListener('click', () => {
        container.querySelectorAll('button').forEach(item => item.setAttribute('aria-pressed', String(item === button)));
        byId('subscription').value = url;
        byId('selected-link').hidden = false;
        byId('copy').textContent = 'Копировать';
        setStatus('Выбрана ссылка ' + (i + 1) + '. Скопируйте её и откройте инструкцию.');
      });
      container.append(button);
    }
    if (Number.isInteger(preferredIndex) && links[preferredIndex]) container.children[preferredIndex].click();
    else if (links.length === 1) container.firstElementChild.click();
  }
  function resetSelection() {
    byId('selected-link').hidden = true;
    byId('subscription').value = '';
    byId('copy').textContent = 'Копировать';
    byId('retry-links').hidden = true;
    renderLinks([]);
  }
  async function loadLinks() {
    if (controller) controller.abort();
    const version = ++requestVersion;
    resetSelection();
    if (!config.subscriptionEndpoint) {
      const links = Array.isArray(config.subscriptionLinks) ? config.subscriptionLinks : [];
      if (validLinks(links)) {
        setStatus('Готово. Выберите доступную ссылку.');
        renderLinks(links);
      } else if (!links.length) setStatus('Ссылки пока не добавлены. Попробуйте позднее или напишите нам.');
      else setStatus('Ссылки временно недоступны. Пожалуйста, напишите нам.', 'error');
      return;
    }
    const requestController = new AbortController();
    controller = requestController;
    const timeout = setTimeout(() => requestController.abort(), 12000);
    byId('link-options').setAttribute('aria-busy', 'true');
    setStatus('Получаем ссылки…');
    try {
      const endpoint = new URL(config.subscriptionEndpoint, location.href);
      if (endpoint.origin !== location.origin || endpoint.username || endpoint.password || !['http:', 'https:'].includes(endpoint.protocol)) throw new Error('Invalid endpoint');
      const response = await fetch(endpoint, { signal: requestController.signal, cache: 'no-store', credentials: 'same-origin', headers: { Accept: 'application/json' } });
      if (!response.ok) throw new Error('Request failed');
      const data = await response.json();
      if (!validLinks(data.links)) throw new Error('Invalid response');
      if (version !== requestVersion || !linksDialog.open) return;
      setStatus('Готово. Выберите доступную ссылку.');
      renderLinks(data.links);
    } catch {
      if (version !== requestVersion || !linksDialog.open) return;
      setStatus('Не удалось получить ссылки. Попробуйте ещё раз или напишите нам.', 'error');
      byId('retry-links').hidden = false;
    } finally {
      clearTimeout(timeout);
      if (version === requestVersion) { controller = null; byId('link-options').removeAttribute('aria-busy'); }
    }
  }
  function openLinks(trigger) {
    closeDialogs();
    returnFocus = trigger;
    preferredIndex = trigger.hasAttribute('data-link-index') ? Number(trigger.dataset.linkIndex) : null;
    linksDialog.showModal();
    loadLinks();
  }
  document.addEventListener('click', event => {
    const target = event.target instanceof Element ? event.target : null;
    if (!target) return;
    const contactTrigger = target.closest('[data-contact-open]');
    const linksTrigger = target.closest('#urgent, [data-links-open]');
    const closeTrigger = target.closest('[data-dialog-close]');
    if (contactTrigger) openContacts(contactTrigger);
    else if (linksTrigger) openLinks(linksTrigger);
    else if (closeTrigger) closeTrigger.closest('dialog').close();
  });
  document.querySelectorAll('dialog').forEach(dialog => {
    let pointerOnBackdrop = false;
    const outside = event => {
      const box = dialog.getBoundingClientRect();
      return event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom;
    };
    dialog.addEventListener('pointerdown', event => { pointerOnBackdrop = event.target === dialog && outside(event); });
    dialog.addEventListener('click', event => {
      if (pointerOnBackdrop && event.target === dialog && outside(event)) dialog.close();
      pointerOnBackdrop = false;
    });
    dialog.addEventListener('close', () => {
      // Native close events are queued; an old event must not cancel a new opening.
      if (dialog.open) return;
      if (dialog === linksDialog) {
        requestVersion++; controller?.abort(); controller = null;
        byId('link-options').removeAttribute('aria-busy');
      }
      if (dialog === mobileMenu) byId('mobile-menu-btn').setAttribute('aria-expanded', 'false');
      if (!document.querySelector('dialog[open]') && returnFocus?.isConnected) returnFocus.focus({ preventScroll: true });
    });
  });
  byId('mobile-menu-btn').addEventListener('click', event => {
    returnFocus = event.currentTarget;
    mobileMenu.showModal();
    byId('mobile-menu-btn').setAttribute('aria-expanded', 'true');
  });
  mobileMenu.querySelectorAll('a').forEach(link => link.addEventListener('click', () => mobileMenu.close()));
  matchMedia('(min-width: 1536px)').addEventListener('change', event => { if (event.matches && mobileMenu.open) mobileMenu.close(); });
  byId('retry-links').addEventListener('click', loadLinks);
  byId('to-guide').addEventListener('click', () => linksDialog.close());
  byId('copy').addEventListener('click', async () => {
    const value = byId('subscription').value;
    if (!safeSubscription(value)) return;
    try {
      await navigator.clipboard.writeText(value);
      byId('copy').textContent = 'Скопировано';
      setStatus('Ссылка скопирована. Теперь откройте инструкцию для вашего устройства.');
    } catch {
      byId('subscription').focus(); byId('subscription').select();
      setStatus('Выделили ссылку. Скопируйте её вручную.');
    }
  });
  // Keep the floating help button from covering prices on narrow screens.
  const pricingSection = byId('pricing-section');
  const floatingHelp = document.querySelector('.help-widget');
  if (pricingSection && floatingHelp && typeof IntersectionObserver === 'function') {
    const narrowScreen = matchMedia('(max-width: 767px)');
    let pricingInView = false;
    const updateHelpVisibility = () => { floatingHelp.hidden = narrowScreen.matches && pricingInView; };
    const pricingObserver = new IntersectionObserver(entries => {
      pricingInView = entries[0].isIntersecting;
      updateHelpVisibility();
    });
    pricingObserver.observe(pricingSection);
    narrowScreen.addEventListener('change', updateHelpVisibility);
  }
  const navbar = document.querySelector('.nav-glass');
  let previousScroll = 0;
  window.addEventListener('scroll', () => {
    const currentScroll = Math.max(0, window.scrollY);
    navbar.classList.toggle('nav-hidden', currentScroll > previousScroll && currentScroll > 120 && !document.querySelector('dialog[open]'));
    previousScroll = currentScroll;
  }, { passive: true });
})();
