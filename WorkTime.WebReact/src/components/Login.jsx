import React, { useState } from 'react'
import { api, setToken, setCurrentUser } from '../api'

// Надёжное декодирование JWT payload (base64url → JSON)
function decodeJwtPayload(token) {
  try {
    const parts = token.split('.')
    if (parts.length !== 3) return null
    let payload = parts[1]
    // base64url → standard base64
    payload = payload.replace(/-/g, '+').replace(/_/g, '/')
    const pad = payload.length % 4
    if (pad) payload += '='.repeat(4 - pad)
    const json = atob(payload)
    return JSON.parse(json)
  } catch {
    return null
  }
}

// Извлекаем userId из JWT — .NET 8 может использовать длинный URI или short name
function extractUserId(payload) {
  if (!payload) return null
  return (
    payload.nameid ||
    payload.NameIdentifier ||
    payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'] ||
    payload.unique_name ||
    payload.sub ||
    null
  )
}

export default function Login({ onLogin }) {
  const [mode, setMode] = useState('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [fullName, setFullName] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const handleSubmit = async (e) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      let res
      if (mode === 'login') {
        res = await api.login(email, password)
      } else {
        res = await api.register(email, password, fullName)
      }
      setToken(res.token)

      // Декодируем JWT для получения id пользователя
      const payload = decodeJwtPayload(res.token)
      const userId = extractUserId(payload)
      if (!userId) {
        console.warn('JWT payload:', payload)
        setError('Не удалось извлечь данные пользователя из токена. Проверьте консоль (F12).')
        setToken('')
        setCurrentUser(null)
        return
      }

      setCurrentUser({ id: userId, email, role: res.role })
      onLogin()
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="auth-page">
      <div className="auth-card">
        <h1 className="auth-title">WorkTime</h1>
        <p className="auth-subtitle">Распределённая система учёта рабочего времени</p>

        <div className="auth-tabs">
          <button
            className={`auth-tab ${mode === 'login' ? 'active' : ''}`}
            onClick={() => { setMode('login'); setError('') }}
          >Вход</button>
          <button
            className={`auth-tab ${mode === 'register' ? 'active' : ''}`}
            onClick={() => { setMode('register'); setError('') }}
          >Регистрация</button>
        </div>

        <form onSubmit={handleSubmit}>
          {mode === 'register' && (
            <div className="form-group">
              <label>ФИО</label>
              <input
                type="text"
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                placeholder="Иванов Иван Иванович"
              />
            </div>
          )}
          <div className="form-group">
            <label>Email</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="user@example.com"
              required
            />
          </div>
          <div className="form-group">
            <label>Пароль</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              required
            />
          </div>

          {error && <div className="error-box">{error}</div>}

          <button type="submit" className="btn btn-primary btn-block" disabled={loading}>
            {loading ? 'Загрузка...' : mode === 'login' ? 'Войти' : 'Зарегистрироваться'}
          </button>
        </form>

        <div className="auth-hint">
          <p>Демо-доступы:</p>
          <p>Админ: <code>admin@worktime.local</code> / <code>Admin@123</code></p>
          <p>Сотрудник: <code>employee@worktime.local</code> / <code>Employee@123</code></p>
        </div>
      </div>
    </div>
  )
}