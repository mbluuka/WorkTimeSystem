import React, { useState, useEffect } from 'react'
import { api, getCurrentUser } from '../api'
import { createEvent, getOutbox, syncOutbox } from '../pwaClient'

export default function Dashboard({ onLogout }) {
  const user = getCurrentUser()
  const [status, setStatus] = useState('idle') // idle | working | break | done
  const [outbox, setOutbox] = useState([])
  const [events, setEvents] = useState([])
  const [summary, setSummary] = useState(null)
  const [log, setLog] = useState('')
  const [deviceId, setDeviceId] = useState(localStorage.getItem('deviceId') || '')
  const [period, setPeriod] = useState('week')
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!deviceId) {
      const newDeviceId = crypto.randomUUID()
      setDeviceId(newDeviceId)
      localStorage.setItem('deviceId', newDeviceId)
    }
    loadOutbox()
    loadEvents()
    loadSummary()
  }, [])

  async function loadOutbox() {
    const items = await getOutbox()
    setOutbox(items)
  }

  async function loadEvents() {
    try {
      const data = await api.getEvents()
      setEvents(data || [])
      updateStatus(data || [])
    } catch (e) {
      append('Ошибка загрузки событий: ' + e.message)
    }
  }

  async function loadSummary() {
    try {
      const now = new Date()
      const from = new Date(now)
      from.setDate(now.getDate() - 7)
      const s = await api.getSummary(user.id, from.toISOString(), now.toISOString())
      setSummary(s)
    } catch (e) {
      append('Ошибка загрузки отчёта: ' + e.message)
    }
  }

  function updateStatus(evts) {
    const sorted = [...evts].sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
    const last = sorted[sorted.length - 1]
    if (!last) { setStatus('idle'); return }
    switch (last.type) {
      case 'StartWork': setStatus('working'); break
      case 'StartBreak': setStatus('break'); break
      case 'EndBreak': setStatus('working'); break
      case 'EndWork': setStatus('done'); break
      default: setStatus('idle')
    }
  }

  function append(s) {
    setLog(l => l + s + '\n')
  }

  const handleAction = async (type) => {
    if (!user?.id || !deviceId) { append('Нет userId/deviceId'); return }
    setLoading(true)
    try {
      const ev = await createEvent({ userId: user.id, deviceId, type })
      append(`Создано: ${type} (${ev.Id})`)
      await loadOutbox()
      // Попытка синхронизации
      try {
        await syncOutbox({ apiBase: '' })
        append('Синхронизировано')
        await loadOutbox()
      } catch (e) {
        append('Офлайн: событие сохранено локально (' + e.message + ')')
      }
      await loadEvents()
      await loadSummary()
    } catch (e) {
      append('Ошибка: ' + e.message)
    } finally {
      setLoading(false)
    }
  }

  const handleSync = async () => {
    setLoading(true)
    try {
      const res = await syncOutbox({ apiBase: '' })
      append('Синхронизация: ' + JSON.stringify(res))
      await loadOutbox()
      await loadEvents()
      await loadSummary()
    } catch (e) {
      append('Ошибка синхронизации: ' + e.message)
    } finally {
      setLoading(false)
    }
  }

  const statusLabels = {
    idle: { text: 'День не начат', cls: 'status-idle' },
    working: { text: 'Работаю', cls: 'status-working' },
    break: { text: 'Перерыв', cls: 'status-break' },
    done: { text: 'День завершён', cls: 'status-done' }
  }

  const st = statusLabels[status]

  return (
    <div className="dashboard">
      <header className="topbar">
        <div className="topbar-left">
          <h1>WorkTime</h1>
          <span className="role-badge">{user.role}</span>
        </div>
        <div className="topbar-right">
          <span className="user-name">{user.email}</span>
          <button className="btn btn-outline" onClick={onLogout}>Выйти</button>
        </div>
      </header>

      <div className="container">
        <div className="status-card">
          <div className={`status-indicator ${st.cls}`}>
            <span className="status-dot"></span>
            <span className="status-text">{st.text}</span>
          </div>
          <div className="status-actions">
            <button
              className="btn btn-primary btn-lg"
              onClick={() => handleAction('StartWork')}
              disabled={loading || status === 'working' || status === 'break'}
            >▶ Начать день</button>
            <button
              className="btn btn-warning btn-lg"
              onClick={() => handleAction('StartBreak')}
              disabled={loading || status !== 'working'}
            >⏸ Перерыв</button>
            <button
              className="btn btn-info btn-lg"
              onClick={() => handleAction('EndBreak')}
              disabled={loading || status !== 'break'}
            >▶ Вернуться</button>
            <button
              className="btn btn-danger btn-lg"
              onClick={() => handleAction('EndWork')}
              disabled={loading || status === 'idle' || status === 'done'}
            >■ Закончить день</button>
          </div>
          <div className="sync-status">
            <span className={`sync-dot ${outbox.length > 0 ? 'pending' : 'ok'}`}></span>
            {outbox.length > 0
              ? `Есть неотправленные данные (${outbox.length})`
              : 'Всё синхронизировано'}
            <button className="btn btn-small" onClick={handleSync} disabled={loading}>
              Синхронизировать
            </button>
          </div>
        </div>

        {summary && (
          <div className="summary-grid">
            <div className="summary-card">
              <div className="summary-value">{summary.totalWorkHours}</div>
              <div className="summary-label">Часов отработано</div>
            </div>
            <div className="summary-card">
              <div className="summary-value">{summary.totalBreakMinutes}</div>
              <div className="summary-label">Минут перерывов</div>
            </div>
            <div className="summary-card">
              <div className="summary-value">{summary.remainingMinutes}</div>
              <div className="summary-label">Осталось до 8ч</div>
            </div>
            <div className="summary-card">
              <div className="summary-value">{summary.days?.length || 0}</div>
              <div className="summary-label">Дней отработано</div>
            </div>
          </div>
        )}

        <div className="card">
          <div className="card-header">
            <h2>История записей</h2>
            <div className="period-selector">
              {['day', 'week', 'month'].map(p => (
                <button
                  key={p}
                  className={`btn btn-small ${period === p ? 'active' : ''}`}
                  onClick={() => setPeriod(p)}
                >{p === 'day' ? 'День' : p === 'week' ? 'Неделя' : 'Месяц'}</button>
              ))}
            </div>
          </div>
          <table className="table">
            <thead>
              <tr>
                <th>Время</th>
                <th>Тип</th>
                <th>Статус</th>
              </tr>
            </thead>
            <tbody>
              {events.length === 0 && (
                <tr><td colSpan="3" className="empty">Нет записей</td></tr>
              )}
              {events.map(ev => (
                <tr key={ev.id}>
                  <td>{new Date(ev.timestamp).toLocaleString('ru-RU')}</td>
                  <td>
                    <span className={`type-badge type-${ev.type.toLowerCase()}`}>
                      {ev.type}
                    </span>
                  </td>
                  <td>{ev.serverOrder ? 'Синхронизировано' : 'Локально'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="card">
          <h2>Очередь синхронизации ({outbox.length})</h2>
          {outbox.length === 0
            ? <p className="empty">Очередь пуста</p>
            : <pre className="code-block">{JSON.stringify(outbox, null, 2)}</pre>}
        </div>

        <div className="card">
          <h2>Лог</h2>
          <pre className="log-block">{log || 'Нет записей'}</pre>
        </div>
      </div>
    </div>
  )
}