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

  // Если страницей уже управляет старый SW, после активации новой версии
  // перезагружаем вкладку один раз: иначе HTML и бандл могут остаться от
  // предыдущего релиза.
  const hadController = Boolean(navigator.serviceWorker.controller)
  let reloading = false

  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (!hadController || reloading) return
    reloading = true
    window.location.reload()
  })

  window.addEventListener('load', () => {
    navigator.serviceWorker
      .register('/service-worker.js')
      .then((registration) => registration.update())
      .catch((err) => console.warn('Service worker registration failed', err))
  })
}
