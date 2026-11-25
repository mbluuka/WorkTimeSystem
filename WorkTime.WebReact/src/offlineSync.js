// Фоновая синхронизация офлайн-очереди (FR10).
//
// Требование: при восстановлении связи очередь должна уйти на сервер
// автоматически, без действий пользователя. Здесь собраны все три
// механизма, которыми это достигается в браузере:
//
//  1. событие `online` — основной сигнал о восстановлении связи;
//  2. Background Sync API (`SyncManager.register('sync-outbox')`) —
//     браузер сам поднимает service worker, когда сеть появилась, даже
//     если вкладка была закрыта. Service worker не может отправить запрос
//     с JWT (токен живёт в localStorage страницы), поэтому он лишь будит
//     открытые вкладки сообщением `SYNC_OUTBOX`;
//  3. возврат на вкладку (`visibilitychange`) — страховка для браузеров
//     без Background Sync (Firefox, Safari).
//
// Все запуски проходят через `runSync`, поэтому параллельные сигналы
// (online + сообщение от SW + видимость) не создают дублирующих запросов.

import { getToken } from './api'
import { getOutbox, syncOutbox } from './pwaClient'

/** Событие успешной синхронизации: на него подписан Dashboard. */
export const SYNCED_EVENT = 'worktime:synced'

/** Событие неудачной попытки — UI может показать «нет связи». */
export const SYNC_FAILED_EVENT = 'worktime:sync-failed'

const BACKGROUND_SYNC_TAG = 'sync-outbox'

let syncInFlight = null
let started = false

/**
 * Регистрирует тег фоновой синхронизации в service worker.
 *
 * Возвращает true, если браузер поддерживает Background Sync и тег принят.
 * Отсутствие поддержки — не ошибка: остаются события `online` и
 * `visibilitychange`.
 */
export async function requestBackgroundSync() {
  if (!('serviceWorker' in navigator)) return false

  try {
    const registration = await navigator.serviceWorker.ready
    if (!registration.sync) return false
    await registration.sync.register(BACKGROUND_SYNC_TAG)
    return true
  } catch {
    // Приватный режим, отключённые разрешения и т.п. — молча деградируем.
    return false
  }
}

/**
 * Одна попытка синхронизации очереди.
 *
 * Возвращает true, если данные ушли, false при ошибке сети и null, если
 * синхронизировать нечего или нет сессии. Повторный вызов во время
 * выполнения возвращает тот же промис — двойной отправки не будет.
 */
export function runSync(reason = 'manual') {
  if (syncInFlight) return syncInFlight

  if (!getToken()) return Promise.resolve(null)
  if (typeof navigator !== 'undefined' && navigator.onLine === false) {
    return Promise.resolve(null)
  }

  syncInFlight = (async () => {
    try {
      const outbox = await getOutbox()
      if (!outbox.length) return null

      await syncOutbox({ apiBase: '' })
      window.dispatchEvent(new CustomEvent(SYNCED_EVENT, { detail: { reason } }))
      return true
    } catch (error) {
      window.dispatchEvent(
        new CustomEvent(SYNC_FAILED_EVENT, { detail: { reason, error } })
      )
      return false
    } finally {
      syncInFlight = null
    }
  })()

  return syncInFlight
}

/**
 * Подписывается на все сигналы восстановления связи.
 *
 * Вызывается один раз при старте приложения (main.jsx).
 */
export function startOfflineSync() {
  if (started) return
  started = true

  // 1. Сеть появилась.
  window.addEventListener('online', () => {
    runSync('online')
    // Дублируем сигналом браузеру: если вкладку закроют, SW дошлёт сам.
    requestBackgroundSync()
  })

  // 2. Service worker разбудил вкладку по тегу sync-outbox.
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.addEventListener('message', (event) => {
      if (event.data && event.data.type === 'SYNC_OUTBOX') {
        runSync('background-sync')
      }
    })
  }

  // 3. Пользователь вернулся на вкладку после офлайна.
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible' && navigator.onLine) {
      runSync('visibility')
    }
  })
}
