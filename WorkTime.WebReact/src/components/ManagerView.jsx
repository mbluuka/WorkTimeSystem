import React, { useState, useEffect } from 'react'
import { api } from '../api'

// ============================================================
// Вспомогательные функции
// ============================================================

function formatDurationShort(minutes) {
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  if (h === 0) return `${m} мин`
  if (m === 0) return `${h} ч`
  return `${h} ч ${m} мин`
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
          <button className="btn btn-small" onClick={onClose}>Закрыть</button>
        </div>
      </div>
    </div>
  )
}

// ============================================================
// Компонент детального отчёта по сотруднику
// ============================================================

function EmployeeDetail({ user, from, to, onBack }) {
  const [summary, setSummary] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    loadSummary()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user.id, from, to])

  async function loadSummary() {
    try {
      setLoading(true)
      const data = await api.getSummary(
        user.id,
        new Date(from).toISOString(),
        new Date(to + 'T23:59:59').toISOString()
      )
      setSummary(data)
    } catch (e) {
      setError('Ошибка загрузки: ' + e.message)
    } finally {
      setLoading(false)
    }
  }

  async function handleExport(format) {
    try {
      if (format === 'pdf') {
        await api.exportPdf(user.id, new Date(from).toISOString(), new Date(to + 'T23:59:59').toISOString())
      } else {
        await api.exportXlsx(user.id, new Date(from).toISOString(), new Date(to + 'T23:59:59').toISOString())
      }
    } catch (e) {
      setError('Ошибка экспорта: ' + e.message)
    }
  }

  if (loading) return <p className="empty">Загрузка...</p>
  if (error) return <div className="error-box">{error}</div>

  return (
    <div className="card employee-detail">
      <div className="card-header">
        <h2>👤 {user.fullName || user.email}</h2>
        <div>
          <button className="btn btn-small btn-outline" onClick={() => handleExport('pdf')}>📄 PDF</button>
          <button className="btn btn-small btn-outline" onClick={() => handleExport('xlsx')}>📊 XLSX</button>
          <button className="btn btn-small" onClick={onBack}>← Назад</button>
        </div>
      </div>

      {summary && (
        <>
          <div className="summary-grid">
            <SummaryCard value={`${summary.totalWorkHours} ч`} label="Отработано" icon="⏱" />
            <SummaryCard value={`${summary.totalBreakMinutes} мин`} label="Перерывы" icon="☕" />
            <SummaryCard value={`${summary.remainingMinutes} мин`} label="До 8 часов" icon="🎯" />
            <SummaryCard value={summary.days?.length || 0} label="Дней" icon="📅" />
          </div>

          {summary.days?.length > 0 && (
            <table className="table">
              <thead>
                <tr>
                  <th>Дата</th>
                  <th>Начало</th>
                  <th>Конец</th>
                  <th>Работа (мин)</th>
                  <th>Перерыв (мин)</th>
                </tr>
              </thead>
              <tbody>
                {summary.days.map((day, i) => (
                  <tr key={i}>
                    <td>{new Date(day.date).toLocaleDateString('ru-RU')}</td>
                    <td>{day.startWork ? new Date(day.startWork).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' }) : '—'}</td>
                    <td>{day.endWork ? new Date(day.endWork).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' }) : '—'}</td>
                    <td>{day.workMinutes}</td>
                    <td>{day.breakMinutes}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </div>
  )
}

function SummaryCard({ value, label, icon }) {
  return (
    <div className="summary-card">
      <div className="summary-icon">{icon}</div>
      <div className="summary-value">{value}</div>
      <div className="summary-label">{label}</div>
    </div>
  )
}

// ============================================================
// Основной компонент ManagerView
// ============================================================

export default function ManagerView({ user }) {
  const [users, setUsers] = useState([])
  const [teamReport, setTeamReport] = useState(null)
  const [teamByProject, setTeamByProject] = useState(null)
  const [from, setFrom] = useState(() => {
    const d = new Date(); d.setDate(d.getDate() - 7); return d.toISOString().slice(0, 10)
  })
  const [to, setTo] = useState(() => new Date().toISOString().slice(0, 10))
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [tab, setTab] = useState('team') // team | projects
  const [selectedEmployee, setSelectedEmployee] = useState(null)

  useEffect(() => {
    loadUsers()
    loadTeamReport()
    loadTeamByProject()
    // eslint-disable-next-line react-hooks/exhaustive-deps
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

  async function loadTeamByProject() {
    try {
      const data = await api.getTeamReportByProject(
        new Date(from).toISOString(),
        new Date(to + 'T23:59:59').toISOString()
      )
      setTeamByProject(data)
    } catch (e) {
      setError('Ошибка загрузки отчёта по проектам: ' + e.message)
    }
  }

  // Экспорт команды (суммарный)
  const handleExportTeam = async (format) => {
    try {
      const fromIso = new Date(from).toISOString()
      const toIso = new Date(to + 'T23:59:59').toISOString()
      // Экспортируем по первому сотруднику команды (для общего отчёта)
      if (teamReport?.employees?.length > 0) {
        const firstEmp = teamReport.employees[0]
        if (format === 'pdf') {
          await api.exportPdf(firstEmp.userId, fromIso, toIso)
        } else {
          await api.exportXlsx(firstEmp.userId, fromIso, toIso)
        }
      }
    } catch (e) {
      setError('Ошибка экспорта: ' + e.message)
    }
  }

  // Детали по сотруднику
  function openEmployeeDetail(emp) {
    setSelectedEmployee({
      id: emp.userId,
      email: emp.userName,
      fullName: emp.userName
    })
  }

  function closeEmployeeDetail() {
    setSelectedEmployee(null)
  }

  if (selectedEmployee) {
    return (
      <div className="manager-view">
        <EmployeeDetail
          user={selectedEmployee}
          from={from}
          to={to}
          onBack={closeEmployeeDetail}
        />
      </div>
    )
  }

  return (
    <div className="manager-view">
      <div className="card">
        <div className="card-header">
          <h2>📊 Сводный отчёт по команде</h2>
          <div className="report-controls">
            <label>С: <input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label>
            <label>По: <input type="date" value={to} onChange={e => setTo(e.target.value)} /></label>
            <button className="btn btn-primary" onClick={loadTeamReport} disabled={loading}>
              {loading ? 'Загрузка...' : 'Сформировать'}
            </button>
            <button className="btn btn-outline" onClick={() => handleExportTeam('pdf')}>📄 PDF</button>
            <button className="btn btn-outline" onClick={() => handleExportTeam('xlsx')}>📊 XLSX</button>
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
                <div className="summary-value">{formatDurationShort(teamReport.totalTeamMinutes)}</div>
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
                  <th>Действия</th>
                </tr>
              </thead>
              <tbody>
                {teamReport.employees?.map(emp => (
                  <tr key={emp.userId}>
                    <td>{emp.userName}</td>
                    <td>{formatDurationShort(emp.totalWorkMinutes)}</td>
                    <td>{emp.totalBreakMinutes}</td>
                    <td>{emp.daysWorked}</td>
                    <td>
                      <button
                        className="btn btn-small"
                        onClick={() => openEmployeeDetail(emp)}
                      >👤 Детали</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </div>

      <div className="card">
        <div className="card-header">
          <h2>📂 Отчёт по проектам (FR14)</h2>
        </div>

        {teamByProject && teamByProject.projects?.length > 0 && (
          teamByProject.projects.map(proj => (
            <div key={proj.projectId} className="project-summary">
              <h3>{proj.projectName}</h3>
              <div className="summary-grid">
                <SummaryCard value={formatDurationShort(proj.totalWorkMinutes)} label="Часов" icon="⏱" />
                <SummaryCard value={`${proj.employeeCount}`} label="Сотрудников" icon="👥" />
              </div>
              {proj.employees?.length > 0 && (
                <table className="table" style={{ marginTop: 8 }}>
                  <thead>
                    <tr>
                      <th>Сотрудник</th>
                      <th>Часов</th>
                      <th>Перерывов (мин)</th>
                      <th>Дней</th>
                      <th>Действия</th>
                    </tr>
                  </thead>
                  <tbody>
                    {proj.employees.map(emp => (
                      <tr key={emp.userId}>
                        <td>{emp.userName}</td>
                        <td>{formatDurationShort(emp.totalWorkMinutes)}</td>
                        <td>{emp.totalBreakMinutes}</td>
                        <td>{emp.daysWorked}</td>
                        <td>
                          <button
                            className="btn btn-small"
                            onClick={() => openEmployeeDetail({
                              id: emp.userId,
                              email: emp.userName,
                              fullName: emp.userName
                            })}
                          >👤 Детали</button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          ))
        )}

        {teamByProject && teamByProject.projects?.length === 0 && (
          <p className="empty">Нет данных за выбранный период</p>
        )}
      </div>

      <div className="card">
        <h2>👥 Сотрудники</h2>
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
