// Клиентский SDK для PWA (веб)
// Хранение событий в IndexedDB (офлайн-очередь)

const DB_NAME = 'worktime_db';
const DB_VERSION = 1;
const OUTBOX_STORE = 'outbox';
const META_STORE = 'meta';

function openDb(){
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = (e) => {
      const db = req.result;
      if (!db.objectStoreNames.contains(OUTBOX_STORE)){
        db.createObjectStore(OUTBOX_STORE, { keyPath: 'Id' });
      }
      if (!db.objectStoreNames.contains(META_STORE)){
        db.createObjectStore(META_STORE, { keyPath: 'key' });
      }
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

export async function nextDeviceSeq(){
  const db = await openDb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(META_STORE, 'readwrite');
    const store = tx.objectStore(META_STORE);
    const get = store.get('device_seq');
    get.onsuccess = () => {
      const cur = (get.result && get.result.value) || 0;
      const next = cur + 1;
      store.put({ key: 'device_seq', value: next });
      tx.oncomplete = () => resolve(next);
    };
    get.onerror = () => reject(get.error);
  });
}

export async function createEvent({ userId, deviceId, type, timestamp = new Date(), projectId = null }){
  const seq = await nextDeviceSeq();
  const ev = {
    Id: crypto.randomUUID(),
    UserId: userId,
    DeviceId: deviceId,
    Type: type,
    Timestamp: new Date(timestamp).toISOString(),
    CreatedAt: new Date().toISOString(),
    DeviceSeq: seq
  };

  // Проект опционален (FR5); null означает "без проекта" и не сериализуется в FK.
  if (projectId) ev.ProjectId = projectId;

  const db = await openDb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(OUTBOX_STORE, 'readwrite');
    const store = tx.objectStore(OUTBOX_STORE);
    store.add(ev);
    tx.oncomplete = () => resolve(ev);
    tx.onerror = () => reject(tx.error || store.error);
  });
}

export async function getOutbox(){
  const db = await openDb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(OUTBOX_STORE, 'readonly');
    const store = tx.objectStore(OUTBOX_STORE);
    const req = store.getAll();
    req.onsuccess = () => resolve(req.result || []);
    req.onerror = () => reject(req.error);
  });
}

export async function clearOutbox(ids){
  const db = await openDb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(OUTBOX_STORE, 'readwrite');
    const store = tx.objectStore(OUTBOX_STORE);
    ids.forEach(id => store.delete(id));
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error || store.error);
  });
}

// Ручная корректировка ещё не отправленного события (FR7):
// запись перезаписывается по тому же ключу Id.
export async function updateOutboxEvent(event){
  const db = await openDb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(OUTBOX_STORE, 'readwrite');
    const store = tx.objectStore(OUTBOX_STORE);
    store.put(event);
    tx.oncomplete = () => resolve(event);
    tx.onerror = () => reject(tx.error || store.error);
  });
}

export async function deleteOutboxEvent(id){
  return clearOutbox([id]);
}

export async function syncOutbox({ apiBase = '', idempotencyKey = null, token = null } = {}){
  const outbox = await getOutbox();
  if (!outbox.length) return { results: [] };

  // Преобразуем события очереди в операции синхронизации
  const ops = outbox.map(ev => ({
    Id: ev.Id,
    UserId: ev.UserId,
    EntityType: 'WorkEvent',
    EntityId: ev.Id,
    Operation: 'create',
    Data: ev,
    Version: ev.DeviceSeq || 0
  }));

  const headers = { 'Content-Type': 'application/json' };
  if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const resp = await fetch(`${apiBase}/api/sync/operations`, {
    method: 'POST',
    headers,
    body: JSON.stringify(ops)
  });

  if (!resp.ok) throw new Error('Sync failed: ' + resp.statusText);

  const data = await resp.json();

  // Удаляем из outbox операции, которые сервер принял или уже имел у себя:
  // - Accepted / Applied — приняты в очередь / применены;
  // - AlreadyQueued / AlreadyProcessed — сервер уже имеет эту же операцию
  //   (идемпотентность по Id): локальная копия больше не нужна;
  // - Deleted — событие удалено на сервере.
  // Conflict / Invalid / Forbidden означают проблему — оставляем в outbox,
  // чтобы пользователь увидел запись и мог скорректировать или удалить её.
  const doneStatuses = ['Accepted', 'Applied', 'AlreadyQueued', 'AlreadyProcessed', 'Deleted'];
  const acceptedIds = (data || [])
    .filter(r => doneStatuses.includes(r.status))
    .map(r => r.id);
  if (acceptedIds.length) await clearOutbox(acceptedIds);

  return data;
}