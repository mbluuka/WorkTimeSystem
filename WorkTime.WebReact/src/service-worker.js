const CACHE_NAME = 'worktime-static-v1';
const ASSETS = [
  '/',
  '/index.html'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(ASSETS))
  );
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  // For API calls, always use network (no caching)
  if (req.url.includes('/api/')) {
    event.respondWith(fetch(req));
    return;
  }

  event.respondWith(
    caches.match(req).then((resp) => resp || fetch(req))
  );
});

// Simple background sync registration example (client must register)
self.addEventListener('sync', (event) => {
  if (event.tag === 'sync-outbox') {
    event.waitUntil(syncOutbox())
  }
});

async function syncOutbox(){
  // Post a message to clients to trigger client-led sync, or implement server sync here
  const clients = await self.clients.matchAll({ includeUncontrolled: true });
  for (const client of clients) {
    client.postMessage({ type: 'SYNC_OUTBOX' });
  }
}
