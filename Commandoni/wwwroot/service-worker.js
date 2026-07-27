self.addEventListener('install',event=>{self.skipWaiting();event.waitUntil(caches.open('commandoni-v4').then(cache=>cache.addAll(['/','/manifest.webmanifest','/css/site.css'])))});
self.addEventListener('activate',event=>event.waitUntil(caches.keys().then(keys=>Promise.all(keys.filter(key=>key.startsWith('commandoni-')&&key!=='commandoni-v4').map(key=>caches.delete(key)))).then(()=>self.clients.claim())));
self.addEventListener('fetch',event=>{if(event.request.method==='GET')event.respondWith(fetch(event.request).catch(()=>caches.match(event.request)));});
