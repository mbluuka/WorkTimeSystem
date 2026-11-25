// Регистрация service worker (FR10).
//
// Путь /service-worker.js стабилен и в dev, и в production: файл лежит в
// public/, откуда Vite копирует его в dist без переименования. Раньше здесь
// регистрировался /src/service-worker.js — в собранном приложении такого
// файла нет, поэтому Background Sync в проде не работал вовсе.
//
// Обработка сообщений SYNC_OUTBOX живёт в offlineSync.js: там же события
// online и visibilitychange, чтобы все сигналы шли через один runSync.

export function register() {
  if (!('serviceWorker' in navigator)) return

  window.addEventListener('load', () => {
    navigator.serviceWorker
      .register('/service-worker.js')
      .catch((err) => console.warn('Service worker registration failed', err))
  })
}
