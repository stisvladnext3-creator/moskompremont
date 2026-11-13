'use strict';
// No fetch handler or cache: account data and private files always come from the authenticated server.
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('push', event => {
  let data = {}; try { data = event.data?.json() || {}; } catch {}
  const account = new URL('account.html', self.registration.scope); let target = account;
  try { const candidate = new URL(data.url, account); if (candidate.origin === account.origin && candidate.pathname === account.pathname) target = candidate; } catch {}
  event.waitUntil(self.registration.showNotification(String(data.title || 'МосКомпРемонт').slice(0, 120), {
    body: String(data.body || 'Новое уведомление в личном кабинете.').slice(0, 240),
    icon: new URL('images/pwa/icon-192.png', self.registration.scope).href,
    tag: String(data.tag || 'mkr-notification').slice(0, 100), data: { url: target.href }
  }));
});
self.addEventListener('notificationclick', event => {
  event.notification.close(); const account = new URL('account.html', self.registration.scope); let target = account;
  try { const candidate = new URL(event.notification.data?.url, account); if (candidate.origin === account.origin && candidate.pathname === account.pathname) target = candidate; } catch {}
  event.waitUntil((async () => {
    for (const client of await self.clients.matchAll({ type: 'window', includeUncontrolled: true })) {
      const url = new URL(client.url); if (url.origin === account.origin && url.pathname === account.pathname && 'navigate' in client) { await client.navigate(target.href); return client.focus(); }
    }
    return self.clients.openWindow(target.href);
  })());
});
