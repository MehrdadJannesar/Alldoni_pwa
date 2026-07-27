self.addEventListener('install',event=>{self.skipWaiting();event.waitUntil(caches.open('musicdoni-v3').then(cache=>cache.addAll(['/','/manifest.webmanifest','/styles.css','/app.js'])))});
self.addEventListener('activate',event=>event.waitUntil(caches.keys().then(keys=>Promise.all(keys.filter(key=>key.startsWith('musicdoni-')&&key!=='musicdoni-v3').map(key=>caches.delete(key)))).then(()=>self.clients.claim())));
self.addEventListener('fetch',event=>{if(event.request.method==='GET')event.respondWith(fetch(event.request).catch(()=>caches.match(event.request)));});
