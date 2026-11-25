// Service worker PWA WorkTime.
//
// Лежит в public/, поэтому Vite копирует его в dist как есть и он доступен
// по стабильному пути /service-worker.js (в отличие от src/, откуда файл
// в production-сборку не попадает).
//
// Задача SW в проекте — не кэшировать API, а поддерживать FR10: браузер
// поднимает SW по тегу `sync-outbox`, когда восстановилась связь, даже если
// вкладка была закрыта. Токен JWT хранится в localStorage страницы, поэтому
// сам SW запросы на сервер не отправляет: он будит открытые вкладки
// сообщением SYNC_OUTBOX, а страница выполняет синхронизацию.

const CACHE_NAME = 'worktime-static-v4'
const APP_SHELL = ['/index.html']

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(APP_SHELL))
  )
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      // Удаляем кэши прошлых версий, чтобы после деплоя не отдавать старый бандл.
      const keys = await caches.keys()
      await Promise.all(
        keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))
      )
      await self.clients.claim()
    })()
  )
})

self.addEventListener('fetch', (event) => {
  const req = event.request
  const url = new URL(req.url)

  // API всегда идёт в сеть: данные офлайна живут в IndexedDB, а не в кэше.
  if (url.pathname.startsWith('/api/')) {
    event.respondWith(fetch(req))
    return
  }

  // Навигация (index.html) — network-first. Кэш здесь только страховка для
  // офлайна: если отдавать HTML из кэша первым, после пересборки страница
  // будет ссылаться на уже удалённый бандл с другим хешем и покажет пустой
  // экран.
  if (req.mode === 'navigate') {
    event.respondWith(
      (async () => {
        try {
          const fresh = await fetch(req)
          const cache = await caches.open(CACHE_NAME)
          cache.put('/index.html', fresh.clone())
          return fresh
        } catch {
          const cached = await caches.match('/index.html')
          return cached || Response.error()
        }
      })()
    )
    return
  }

  // Статика Vite содержит хеш в имени файла, поэтому cache-first безопасен:
  // новый релиз приносит новые имена, а старые записи удалит activate.
  if (url.pathname.startsWith('/assets/')) {
    event.respondWith(caches.match(req).then((resp) => resp || fetch(req)))
    return
  }

  // Остальное (иконки, манифест) — из кэша, при промахе из сети.
  event.respondWith(caches.match(req).then((resp) => resp || fetch(req)))
})

// Background Sync (FR10): браузер вызывает этот обработчик, когда появилась
// сеть. Будим все открытые вкладки — они отправят очередь с токеном.
self.addEventListener('sync', (event) => {
  if (event.tag === 'sync-outbox') {
    event.waitUntil(notifyClientsToSync())
  }
})

async function notifyClientsToSync() {
  const clients = await self.clients.matchAll({
    includeUncontrolled: true,
    type: 'window'
  })

  for (const client of clients) {
    client.postMessage({ type: 'SYNC_OUTBOX' })
  }
}
