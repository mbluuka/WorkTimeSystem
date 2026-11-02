import React, { useState, useEffect, useRef, useCallback } from 'react'
import { api, getCurrentUser } from '../api'
import { createEvent, getOutbox, syncOutbox, updateOutboxEvent, deleteOutboxEvent } from '../pwaClient'

// ============================================================
// Вспомогательные функции
// ============================================================

function formatDuration(ms) {
  const totalSec = Math.floor(ms / 1000)
  const h = Math.floor(totalSec / 3600)
  const m = Math.floor((totalSec % 3600) / 60)
  const s = totalSec % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

function formatDurationShort(minutes) {
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  if (h === 0) return `${m} мин`
  if (m === 0) return `${h} ч`
  return `${h} ч ${m} мин`
}

function formatDateRu(date) {
  return new Date(date).toLocaleDateString('ru-RU', {
    day: 'numeric', month: 'short', year: 'numeric'
  })
}

function formatTimeRu(date) {
  return new Date(date).toLocaleTimeString('ru-RU', {
    hour: '2-digit', minute: '2-digit'
  })
}

const TYPE_LABELS = {
  StartWork: 'Начало работы',
  StartBreak: 'Перерыв',
  EndBreak: 'Конец перерыва',
  EndWork: 'Конец работы'
}

const TYPE_ICONS = {
  StartWork: '▶',
  StartBreak: '⏸',
  EndBreak: '▶',
  EndWork: '■'
}

const STATUS_CONFIG = {
  idle:   { text: 'День не начат',     color: '#adb5bd', bg: '#f8f9fa' },
  working:{ text: 'Работаю',           color: '#28a745', bg: '#d4edda' },
  break:  { text: 'Перерыв',           color: '#e0a800', bg: '#fff3cd' },
  done:   { text: 'День завершён',     color: '#0d6efd', bg: '#d1ecf1' }
}

// ============================================================
// Модальное окно
// ============================================================

function Modal({ isOpen, onClose, title, children }) {
  if (!isOpen) return null
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <h3>{title}</h3>
        {children}
        <div className="modal-actions">
          <button className="btn btn-small" onClick={onClose}>Отмена</button>
        </div>
      </div>
    </div>
  )
}

// ============================================================
// Основной компонент Dashboard
// ============================================================

export default function Dashboard({ onLogout }) {
  const user = getCurrentUser()
  const [status, setStatus] = useState('idle')
  const [outbox, setOutbox] = useState([])
  const [events, setEvents] = useState([])
  const [summary, setSummary] = useState(null)
  const [projects, setProjects] = useState([])
  const [loading, setLoading] = useState(false)
  const [syncing, setSyncing] = useState(false)
  const [elapsed, setElapsed] = useState(0)
  const [period, setPeriod] = useState('week')
  const [showProjectPicker, setShowProjectPicker] = useState(false)
  const [selectedProjectId, setSelectedProjectId] = useState(null)
  const [editEvent, setEditEvent] = useState(null)
  const [deleteConfirm, setDeleteConfirm] = useState(null)
  const [error, setError] = useState('')
  const [syncMessage, setSyncMessage] = useState('')
  const [deviceId, setDeviceId] = useState(() => localStorage.getItem('deviceId') || '')
  const [clearOutboxConfirm, setClearOutboxConfirm] = useState(false)
  const elapsedRef = useRef(null)
  const statusRef = useRef(status)
  statusRef.current = status

  // ---------- Загрузка данных ----------

  useEffect(() => {
    if (!deviceId) {
      const newId = crypto.randomUUID()
      setDeviceId(newId)
      localStorage.setItem('deviceId', newId)
    }
    ensureDeviceRegistered(deviceId || localStorage.getItem('deviceId'))
    loadProjects()
    loadOutbox()
    loadEvents()
    loadSummary()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // ---------- Живой таймер ----------

  useEffect(() => {
    if (statusRef.current !== 'working') {
      setElapsed(0)
      return
    }
    const lastStart = getLastStartWorkEvent()
    if (!lastStart) { setElapsed(0); return }
    setElapsed(Date.now() - new Date(lastStart.timestamp).getTime())
    if (elapsedRef.current) clearInterval(elapsedRef.current)
    elapsedRef.current = setInterval(() => {
      const now = new Date(lastStart.timestamp)
      setElapsed(Date.now() - now.getTime())
    }, 1000)
    return () => { if (elapsedRef.current) clearInterval(elapsedRef.current) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status])

  function getLastStartWorkEvent() {
    const sorted = [...events].sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp))
    for (const ev of sorted) {
      if (ev.type === 'StartWork') {
        // Проверяем, что после StartWork не было EndWork
        const afterEndWork = sorted.find(e =>
          e.type === 'EndWork' && new Date(e.timestamp) > new Date(ev.timestamp)
        )
        if (!afterEndWork) return ev
      }
    }
    return null
  }

  // ---------- Device ----------

  async function ensureDeviceRegistered(id) {
    if (!id || !user?.id) return
    try {
      await api.registerDevice({ id, userId: user.id, name: 'Web PWA', platform: navigator.platform || 'Web' })
    } catch { /* silently fail, will retry on sync */ }
  }

  // ---------- Загрузка ----------

  async function loadProjects() {
    try { setProjects(await api.getProjects() || []) } catch (e) { setError('Не удалось загрузить проекты') }
  }

  async function loadOutbox() {
    try { setOutbox(await getOutbox()) } catch { /* ignore */ }
  }

  async function loadEvents() {
    try {
      const data = await api.getEvents()
      setEvents(data || [])
      updateStatus(data || [])
    } catch (e) { setError('Ошибка загрузки событий: ' + e.message) }
  }

  async function loadSummary() {
    try {
      const now = new Date()
      const from = new Date(now)
      from.setDate(from.getDate() - 7)
      const s = await api.getSummary(user.id, from.toISOString(), now.toISOString())
      setSummary(s)
    } catch (e) { setError('Ошибка загрузки отчёта: ' + e.message) }
  }

  // ---------- Статус ----------

  function updateStatus(evts) {
    const sorted = [...evts].sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
    const last = sorted[sorted.length - 1]
    if (!last) { setStatus('idle'); return }
    switch (last.type) {
      case 'StartWork':  setStatus('working'); break
      case 'StartBreak': setStatus('break'); break
      case 'EndBreak':   setStatus('working'); break
      case 'EndWork':    setStatus('done'); break
      default:           setStatus('idle')
    }
  }

  // ---------- Действия ----------

  const handleAction = useCallback(async (type) => {
    if (!user?.id || !deviceId) { setError('Не хватает данных пользователя'); return }
    setLoading(true)
    setError('')
    try {
      const payload = {
        userId: user.id,
        deviceId,
        type,
        projectId: selectedProjectId || null
      }
      const ev = await createEvent(payload)
      // Не синхронизируем автоматически — событие попадёт в outbox
      // и будет отправлено при нажатии «Синхронизировать»
      await loadOutbox()
      await loadEvents()
      setSelectedProjectId(null)
      setShowProjectPicker(false)
    } catch (e) { setError('Ошибка: ' + e.message) }
    finally { setLoading(false) }
  }, [user, deviceId, selectedProjectId])

  async function handleSync() {
    setLoading(true)
    setError('')
    setSyncMessage('')
    setSyncing(true)
    try {
      const outboxBefore = await getOutbox()
      if (!outboxBefore.length) {
        setSyncMessage('Нечего синхронизировать — всё уже на сервере')
        setSyncing(false)
        return
      }
      setSyncMessage(`Отправка ${outboxBefore.length} записей...`)
      await syncOutbox({ apiBase: '' })
      await loadOutbox()
      await loadEvents()
      await loadSummary()
      setSyncMessage('✓ Синхронизировано')
    } catch (e) {
      setError('Ошибка синхронизации: ' + e.message)
      setSyncMessage('✗ Ошибка синхронизации')
    } finally {
      setLoading(false)
      setSyncing(false)
      // Убираем сообщение через 5 секунд
      setTimeout(() => setSyncMessage(''), 5000)
    }
  }

  // ---------- Очистка очереди ----------

  async function handleClearOutbox() {
    setLoading(true)
    setError('')
    try {
      const items = await getOutbox()
      await Promise.all(items.map(it => deleteOutboxEvent(it.Id)))
      await loadOutbox()
      setClearOutboxConfirm(false)
      setSyncMessage('✓ Очередь очищена')
      setTimeout(() => setSyncMessage(''), 5000)
    } catch (e) { setError('Не удалось очистить очередь: ' + e.message) }
    finally { setLoading(false) }
  }

  async function handleDeleteOutboxItem(id) {
    setLoading(true)
    try {
      await deleteOutboxEvent(id)
      await loadOutbox()
    } catch (e) { setError('Ошибка: ' + e.message) }
    finally { setLoading(false) }
  }

  // ---------- Редактирование события (FR7) ----------

  async function handleUpdateEvent(ev) {
    setLoading(true)
    setError('')
    try {
      // Если событие ещё в outbox — редактируем локально
      const outboxItem = outbox.find(o => o.Id === ev.id)
      if (outboxItem) {
        const updated = { ...outboxItem, Type: ev.type, Timestamp: ev.timestamp, ProjectId: ev.projectId || null }
        await updateOutboxEvent(updated)
        await loadOutbox()
      } else {
        await api.updateEvent(ev.id, { type: ev.type, timestamp: ev.timestamp, projectId: ev.projectId || null })
        await loadEvents()
        await loadSummary()
      }
      setEditEvent(null)
    } catch (e) { setError('Ошибка обновления: ' + e.message) }
    finally { setLoading(false) }
  }

  async function handleDeleteEvent(ev) {
    setLoading(true)
    setError('')
    try {
      const outboxItem = outbox.find(o => o.Id === ev.id)
      if (outboxItem) {
        await deleteOutboxEvent(ev.id)
        await loadOutbox()
      } else {
        await api.deleteEvent(ev.id)
        await loadEvents()
        await loadSummary()
      }
      setDeleteConfirm(null)
    } catch (e) { setError('Ошибка удаления: ' + e.message) }
    finally { setLoading(false) }
  }

  // ---------- Фильтрация событий по периоду ----------

  function getFilteredEvents() {
    const now = new Date()
    const cutoff = new Date(now)
    if (period === 'day') cutoff.setDate(now.getDate() - 1)
    else if (period === 'week') cutoff.setDate(now.getDate() - 7)
    else cutoff.setMonth(now.getMonth() - 1)

    return events.filter(e => new Date(e.timestamp) >= cutoff).sort(
      (a, b) => new Date(b.timestamp) - new Date(a.timestamp)
    )
  }

  // ---------- Проект ----------

  function getProjectName(id) {
    if (!id) return null
    return projects.find(p => p.id === id)?.name || null
  }

  // ---------- Состояния ----------

  const st = STATUS_CONFIG[status]
  const filtered = getFilteredEvents()
  const progressPct = Math.min(100, Math.round((summary?.totalWorkMinutes || 0) / 480 * 100))
  const syncCount = outbox.length

  return (
    <div className="dashboard">
      {/* ===== Верхняя панель ===== */}
      <header className="topbar">
        <div className="topbar-left">
          <h1>⏱ WorkTime</h1>
          <span className="role-badge">{user.role}</span>
        </div>
        <div className="topbar-right">
          <span className="user-name">{user.email}</span>
          <button className="btn btn-outline" onClick={onLogout}>Выйти</button>
        </div>
      </header>

      <div className="container">
        {/* ===== Ошибка ===== */}
        {error && (
          <div className="error-box" onClick={() => setError('')}>
            ⚠ {error}
          </div>
        )}

        {/* ===== Карточка статуса ===== */}
        <div className="status-card">
          <div className="status-header">
            <div className={`status-badge ${st.bg}`} style={{ borderColor: st.color }}>
              <span className="status-dot" style={{ background: st.color }}></span>
              {st.text}
            </div>
            {status === 'working' && (
              <div className="timer-display">{formatDuration(elapsed)}</div>
            )}
          </div>

          {/* Прогресс-бар */}
          {summary && (
            <div className="progress-section">
              <div className="progress-info">
                <span>{formatDurationShort(summary.totalWorkMinutes)}</span>
                <span>из 8 ч</span>
              </div>
              <div className="progress-bar">
                <div
                  className="progress-fill"
                  style={{
                    width: `${progressPct}%`,
                    background: progressPct >= 100 ? '#28a745' : progressPct >= 75 ? '#ffc107' : '#0f3460'
                  }}
                ></div>
              </div>
              <div className="progress-info">
                <span>{summary.remainingMinutes} мин до нормы</span>
              </div>
            </div>
          )}

          {/* Кнопки действий */}
          <div className="status-actions">
            <button
              className="btn btn-success btn-lg"
              onClick={() => { setShowProjectPicker(true); setSelectedProjectId(null) }}
              disabled={loading || status === 'working' || status === 'break'}
            >
              ▶ Начать день
            </button>
            <button
              className="btn btn-warning btn-lg"
              onClick={() => handleAction('StartBreak')}
              disabled={loading || status !== 'working'}
            >
              ⏸ Перерыв
            </button>
            <button
              className="btn btn-info btn-lg"
              onClick={() => handleAction('EndBreak')}
              disabled={loading || status !== 'break'}
            >
              ▶ Вернуться
            </button>
            <button
              className="btn btn-danger btn-lg"
              onClick={() => handleAction('EndWork')}
              disabled={loading || status === 'idle' || status === 'done'}
            >
              ■ Закончить
            </button>
          </div>

          {/* Статус синхронизации */}
          <div className="sync-row">
            <span className={`sync-dot ${syncCount > 0 ? 'pending' : 'ok'}`}></span>
            {syncing
              ? (syncMessage || 'Синхронизация...')
              : syncMessage
                ? syncMessage
                : syncCount > 0
                  ? `${syncCount} несохранённых записей`
                  : 'Всё синхронизировано'}
            <div className="sync-buttons">
              <button
                className="btn btn-small btn-primary"
                onClick={handleSync}
                disabled={loading || syncing}
              >
                {syncing ? 'Синхронизация...' : 'Синхронизировать'}
              </button>
              {syncCount > 0 && (
                <button
                  className="btn btn-small btn-outline btn-ghost"
                  onClick={() => setClearOutboxConfirm(true)}
                  disabled={loading || syncing}
                  title="Удалить все записи из очереди"
                >
                  Очистить
                </button>
              )}
            </div>
          </div>
        </div>

        {/* ===== Сводка ===== */}
        {summary && (
          <div className="summary-grid">
            <SummaryCard value={`${summary.totalWorkHours} ч`} label="Отработано" icon="⏱" />
            <SummaryCard value={`${summary.totalBreakMinutes} мин`} label="Перерывы" icon="☕" />
            <SummaryCard value={`${summary.remainingMinutes} мин`} label="До 8 часов" icon="🎯" />
            <SummaryCard value={summary.days?.length || 0} label="Дней" icon="📅" />
          </div>
        )}

        {/* ===== История событий ===== */}
        <div className="card events-card">
          <div className="card-header">
            <h2>История записей</h2>
            <div className="period-selector">
              {['day', 'week', 'month'].map(p => (
                <button
                  key={p}
                  className={`btn btn-small btn-ghost ${period === p ? 'active' : ''}`}
                  onClick={() => setPeriod(p)}
                >
                  {p === 'day' ? 'День' : p === 'week' ? 'Неделя' : 'Месяц'}
                </button>
              ))}
            </div>
          </div>

          {filtered.length === 0 && <p className="empty">Нет записей за этот период</p>}

          <table className="table">
            <thead>
              <tr>
                <th>Дата и время</th>
                <th>Тип</th>
                <th>Проект</th>
                <th>Статус</th>
                <th style={{ textAlign: 'right' }}>Действия</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(ev => (
                <tr key={ev.id}>
                  <td>
                    {formatDateRu(ev.timestamp)} {formatTimeRu(ev.timestamp)}
                  </td>
                  <td>
                    <span className={`type-badge type-${ev.type.toLowerCase()}`}>
                      {TYPE_ICONS[ev.type]} {TYPE_LABELS[ev.type]}
                    </span>
                  </td>
                  <td>{getProjectName(ev.projectId) || '—'}</td>
                  <td>
                    {ev.serverOrder
                      ? <span className="badge-ok">✓ Сервер</span>
                      : <span className="badge-danger">○ Офлайн</span>
                    }
                  </td>
                  <td style={{ textAlign: 'right' }}>
                    <button
                      className="btn btn-small"
                      title="Редактировать"
                      onClick={() => setEditEvent({
                        ...ev,
                        type: ev.type,
                        timestamp: ev.timestamp,
                        projectId: ev.projectId
                      })}
                    >✏</button>
                    <button
                      className="btn btn-small btn-danger"
                      title="Удалить"
                      onClick={() => setDeleteConfirm(ev)}
                    >🗑</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* ===== Выбор проекта при старте ===== */}
      <Modal isOpen={showProjectPicker} onClose={() => setShowProjectPicker(false)} title="Выберите проект">
        <div className="project-picker">
          <button
            className="btn btn-small btn-outline project-option"
            onClick={() => setSelectedProjectId(null)}
          >
            Без проекта
          </button>
          {projects.map(p => (
            <button
              key={p.id}
              className={`btn btn-small btn-outline project-option ${selectedProjectId === p.id ? 'active' : ''}`}
              onClick={() => setSelectedProjectId(p.id)}
            >
              {p.name}
              {p.description && <span className="project-desc">{p.description}</span>}
            </button>
          ))}
          <div style={{ marginTop: 16, display: 'flex', gap: 8 }}>
            <button
              className="btn btn-primary"
              onClick={() => {
                handleAction('StartWork')
                setShowProjectPicker(false)
              }}
              disabled={loading}
              style={{ flex: 1 }}
            >
              Начать с {selectedProjectId ? getProjectName(selectedProjectId) : 'без проекта'}
            </button>
          </div>
        </div>
      </Modal>

      {/* ===== Редактирование события ===== */}
      <Modal isOpen={!!editEvent} onClose={() => setEditEvent(null)} title="Редактировать запись">
        {editEvent && (
          <div className="edit-form">
            <div className="form-group">
              <label>Тип события</label>
              <select
                value={editEvent.type}
                onChange={e => setEditEvent({ ...editEvent, type: e.target.value })}
              >
                {Object.entries(TYPE_LABELS).map(([k, v]) => (
                  <option key={k} value={k}>{v}</option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label>Время</label>
              <input
                type="datetime-local"
                value={new Date(editEvent.timestamp).toISOString().slice(0, 16)}
                onChange={e => setEditEvent({ ...editEvent, timestamp: new Date(e.target.value).toISOString() })}
              />
            </div>
            <div className="form-group">
              <label>Проект</label>
              <select
                value={editEvent.projectId || ''}
                onChange={e => setEditEvent({ ...editEvent, projectId: e.target.value || null })}
              >
                <option value="">Без проекта</option>
                {projects.map(p => (
                  <option key={p.id} value={p.id}>{p.name}</option>
                ))}
              </select>
            </div>
            <div className="modal-actions">
              <button className="btn btn-small" onClick={() => setEditEvent(null)}>Отмена</button>
              <button
                className="btn btn-primary"
                onClick={() => handleUpdateEvent(editEvent)}
                disabled={loading}
              >Сохранить</button>
            </div>
          </div>
        )}
      </Modal>

      {/* ===== Подтверждение удаления ===== */}
      <Modal isOpen={!!deleteConfirm} onClose={() => setDeleteConfirm(null)} title="Удалить запись?">
        <p>Вы уверены, что хотите удалить запись "{TYPE_LABELS[deleteConfirm?.type]}"?</p>
        <div className="modal-actions">
          <button className="btn btn-small" onClick={() => setDeleteConfirm(null)}>Отмена</button>
          <button
            className="btn btn-danger"
            onClick={() => handleDeleteEvent(deleteConfirm)}
            disabled={loading}
          >Удалить</button>
        </div>
      </Modal>

      {/* ===== Очистка очереди ===== */}
      <Modal isOpen={clearOutboxConfirm} onClose={() => setClearOutboxConfirm(false)} title="Очистить очередь синхронизации?">
        <p>
          Из локальной очереди будут удалены все {syncCount} записей. Они <b>не будут отправлены</b> на сервер.
          Используйте, если записии зависли и не синхронизируются.
        </p>
        <div className="modal-actions">
          <button className="btn btn-small" onClick={() => setClearOutboxConfirm(false)}>Отмена</button>
          <button
            className="btn btn-danger"
            onClick={handleClearOutbox}
            disabled={loading}
          >Очистить</button>
        </div>
      </Modal>
    </div>
  )
}

// ============================================================
// Вспомогательные компоненты
// ============================================================

function SummaryCard({ value, label, icon }) {
  return (
    <div className="summary-card">
      <div className="summary-icon">{icon}</div>
      <div className="summary-value">{value}</div>
      <div className="summary-label">{label}</div>
    </div>
  )
}
