self.addEventListener('install', event => {
  self.skipWaiting();
  event.waitUntil(caches.open('passworddoni-v11').then(cache => cache.addAll(['/styles.css', '/app.js', '/manifest.webmanifest'])));
});
self.addEventListener('activate', event => {
  event.waitUntil(caches.keys()
    .then(keys => Promise.all(keys.filter(key => key.startsWith('passworddoni-') && key !== 'passworddoni-v11').map(key => caches.delete(key))))
    .then(() => self.clients.claim()));
});
self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;
  if (event.request.mode === 'navigate') return event.respondWith(fetch(event.request));
  event.respondWith(fetch(event.request).catch(() => caches.match(event.request)));
});
