import React, { useState, useEffect } from 'react'
import { api } from '../api'

export default function ManagerView({ user }) {
  const [users, setUsers] = useState([])
  const [teamReport, setTeamReport] = useState(null)
  const [from, setFrom] = useState(() => {
    const d = new Date(); d.setDate(d.getDate() - 7); return d.toISOString().slice(0, 10)
  })
  const [to, setTo] = useState(() => new Date().toISOString().slice(0, 10))
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    loadUsers()
    loadTeamReport()
  }, [])

  async function loadUsers() {
    try {
      const data = await api.getUsers()
      setUsers(data || [])
    } catch (e) {
      setError('Ошибка загрузки пользователей: ' + e.message)
    }
  }

  async function loadTeamReport() {
    setLoading(true)
    setError('')
    try {
      const data = await api.getTeamReport(
        new Date(from).toISOString(),
        new Date(to + 'T23:59:59').toISOString()
      )
      setTeamReport(data)
    } catch (e) {
      setError('Ошибка загрузки отчёта: ' + e.message)
    } finally {
      setLoading(false)
    }
  }

  const handleExport = async (format) => {
    try {
      const data = format === 'pdf'
        ? await api.exportPdf(user.id, new Date(from).toISOString(), new Date(to + 'T23:59:59').toISOString())
        : await api.exportXlsx(user.id, new Date(from).toISOString(), new Date(to + 'T23:59:59').toISOString())
      alert(JSON.stringify(data, null, 2))
    } catch (e) {
      alert('Ошибка экспорта: ' + e.message)
    }
  }

  return (
    <div className="manager-view">
      <div className="card">
        <div className="card-header">
          <h2>Сводный отчёт по команде</h2>
          <div className="report-controls">
            <label>С: <input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label>
            <label>По: <input type="date" value={to} onChange={e => setTo(e.target.value)} /></label>
            <button className="btn btn-primary" onClick={loadTeamReport} disabled={loading}>
              {loading ? 'Загрузка...' : 'Сформировать'}
            </button>
            <button className="btn btn-outline" onClick={() => handleExport('pdf')}>PDF</button>
            <button className="btn btn-outline" onClick={() => handleExport('xlsx')}>XLSX</button>
          </div>
        </div>

        {error && <div className="error-box">{error}</div>}

        {teamReport && (
          <>
            <div className="summary-grid">
              <div className="summary-card">
                <div className="summary-value">{teamReport.employees?.length || 0}</div>
                <div className="summary-label">Сотрудников</div>
              </div>
              <div className="summary-card">
                <div className="summary-value">{Math.round(teamReport.totalTeamMinutes / 60)}</div>
                <div className="summary-label">Часов командой</div>
              </div>
            </div>

            <table className="table">
              <thead>
                <tr>
                  <th>Сотрудник</th>
                  <th>Часов</th>
                  <th>Перерывов (мин)</th>
                  <th>Дней</th>
                </tr>
              </thead>
              <tbody>
                {teamReport.employees?.map(emp => (
                  <tr key={emp.userId}>
                    <td>{emp.userName}</td>
                    <td>{Math.round(emp.totalWorkMinutes / 60)}</td>
                    <td>{emp.totalBreakMinutes}</td>
                    <td>{emp.daysWorked}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </div>

      <div className="card">
        <h2>Сотрудники</h2>
        <table className="table">
          <thead>
            <tr>
              <th>Email</th>
              <th>ФИО</th>
              <th>Роль</th>
            </tr>
          </thead>
          <tbody>
            {users.map(u => (
              <tr key={u.id}>
                <td>{u.email}</td>
                <td>{u.fullName || '—'}</td>
                <td><span className="role-badge">{u.role}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}