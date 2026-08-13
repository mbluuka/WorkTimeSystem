import React, { useState, useEffect } from 'react'
import { api } from '../api'

export default function AdminView({ user }) {
  const [users, setUsers] = useState([])
  const [auditLogs, setAuditLogs] = useState([])
  const [projects, setProjects] = useState([])
  const [tab, setTab] = useState('users')
  const [error, setError] = useState('')
  const [newUser, setNewUser] = useState({ email: '', password: '', fullName: '', role: 'Employee' })
  const [newProject, setNewProject] = useState({ name: '', description: '' })

  useEffect(() => {
    loadUsers()
    loadAuditLogs()
    loadProjects()
  }, [])

  async function loadUsers() {
    try { setUsers(await api.getUsers() || []) } catch (e) { setError(e.message) }
  }
  async function loadAuditLogs() {
    try {
      const data = await api.getAuditLogs(1, 100)
      setAuditLogs(data.items || [])
    } catch (e) { setError(e.message) }
  }
  async function loadProjects() {
    try { setProjects(await api.getProjects() || []) } catch (e) { setError(e.message) }
  }

  const handleCreateUser = async (e) => {
    e.preventDefault()
    try {
      await api.createUser(newUser)
      setNewUser({ email: '', password: '', fullName: '', role: 'Employee' })
      await loadUsers()
      await loadAuditLogs()
    } catch (err) { setError(err.message) }
  }

  const handleToggleBlock = async (u) => {
    try {
      if (u.isBlocked) await api.unblockUser(u.id)
      else await api.blockUser(u.id)
      await loadUsers()
      await loadAuditLogs()
    } catch (err) { setError(err.message) }
  }

  const handleDeleteUser = async (u) => {
    if (!confirm(`Удалить пользователя ${u.email}?`)) return
    try {
      await api.deleteUser(u.id)
      await loadUsers()
      await loadAuditLogs()
    } catch (err) { setError(err.message) }
  }

  const handleCreateProject = async (e) => {
    e.preventDefault()
    try {
      await api.createProject(newProject)
      setNewProject({ name: '', description: '' })
      await loadProjects()
    } catch (err) { setError(err.message) }
  }

  return (
    <div className="admin-view">
      <div className="tabs">
        <button className={`tab ${tab === 'users' ? 'active' : ''}`} onClick={() => setTab('users')}>Пользователи</button>
        <button className={`tab ${tab === 'projects' ? 'active' : ''}`} onClick={() => setTab('projects')}>Проекты</button>
        <button className={`tab ${tab === 'audit' ? 'active' : ''}`} onClick={() => setTab('audit')}>Аудит</button>
      </div>

      {error && <div className="error-box">{error}</div>}

      {tab === 'users' && (
        <div className="card">
          <h2>Управление пользователями</h2>
          <form className="inline-form" onSubmit={handleCreateUser}>
            <input
              type="email" placeholder="Email" required
              value={newUser.email}
              onChange={e => setNewUser({ ...newUser, email: e.target.value })}
            />
            <input
              type="password" placeholder="Пароль" required
              value={newUser.password}
              onChange={e => setNewUser({ ...newUser, password: e.target.value })}
            />
            <input
              type="text" placeholder="ФИО"
              value={newUser.fullName}
              onChange={e => setNewUser({ ...newUser, fullName: e.target.value })}
            />
            <select
              value={newUser.role}
              onChange={e => setNewUser({ ...newUser, role: e.target.value })}
            >
              <option value="Employee">Сотрудник</option>
              <option value="Manager">Руководитель</option>
              <option value="Admin">Администратор</option>
            </select>
            <button type="submit" className="btn btn-primary">Добавить</button>
          </form>

          <table className="table">
            <thead>
              <tr>
                <th>Email</th>
                <th>ФИО</th>
                <th>Роль</th>
                <th>Статус</th>
                <th>Действия</th>
              </tr>
            </thead>
            <tbody>
              {users.map(u => (
                <tr key={u.id}>
                  <td>{u.email}</td>
                  <td>{u.fullName || '—'}</td>
                  <td><span className="role-badge">{u.role}</span></td>
                  <td>{u.isBlocked ? <span className="badge-danger">Заблокирован</span> : <span className="badge-ok">Активен</span>}</td>
                  <td>
                    <button className="btn btn-small" onClick={() => handleToggleBlock(u)}>
                      {u.isBlocked ? 'Разблокировать' : 'Заблокировать'}
                    </button>
                    <button className="btn btn-small btn-danger" onClick={() => handleDeleteUser(u)}>Удалить</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {tab === 'projects' && (
        <div className="card">
          <h2>Проекты</h2>
          <form className="inline-form" onSubmit={handleCreateProject}>
            <input
              type="text" placeholder="Название проекта" required
              value={newProject.name}
              onChange={e => setNewProject({ ...newProject, name: e.target.value })}
            />
            <input
              type="text" placeholder="Описание"
              value={newProject.description}
              onChange={e => setNewProject({ ...newProject, description: e.target.value })}
            />
            <button type="submit" className="btn btn-primary">Создать</button>
          </form>

          <table className="table">
            <thead>
              <tr>
                <th>Название</th>
                <th>Описание</th>
                <th>Создан</th>
              </tr>
            </thead>
            <tbody>
              {projects.map(p => (
                <tr key={p.id}>
                  <td>{p.name}</td>
                  <td>{p.description || '—'}</td>
                  <td>{new Date(p.createdAt).toLocaleDateString('ru-RU')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {tab === 'audit' && (
        <div className="card">
          <h2>Журнал аудита</h2>
          <table className="table">
            <thead>
              <tr>
                <th>Время</th>
                <th>Администратор</th>
                <th>Действие</th>
                <th>Цель</th>
                <th>Детали</th>
              </tr>
            </thead>
            <tbody>
              {auditLogs.length === 0 && <tr><td colSpan="5" className="empty">Нет записей</td></tr>}
              {auditLogs.map(l => (
                <tr key={l.id}>
                  <td>{new Date(l.timestamp).toLocaleString('ru-RU')}</td>
                  <td>{l.adminName || l.adminId}</td>
                  <td><span className="type-badge">{l.action}</span></td>
                  <td>{l.targetUserName || '—'}</td>
                  <td>{l.details || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}