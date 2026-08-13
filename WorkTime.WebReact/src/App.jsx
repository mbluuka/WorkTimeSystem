import React, { useState } from 'react'
import Login from './components/Login'
import Dashboard from './components/Dashboard'
import ManagerView from './components/ManagerView'
import AdminView from './components/AdminView'
import { getCurrentUser, isAuthenticated, setToken, setCurrentUser } from './api'

export default function App() {
  const [authed, setAuthed] = useState(isAuthenticated())
  const user = getCurrentUser()

  const handleLogin = () => setAuthed(true)

  const handleLogout = () => {
    setToken('')
    setCurrentUser(null)
    setAuthed(false)
  }

  if (!authed || !user) {
    return <Login onLogin={handleLogin} />
  }

  return (
    <div className="app">
      {user.role === 'Admin' && (
        <>
          <Dashboard onLogout={handleLogout} />
          <div className="container">
            <AdminView user={user} />
          </div>
        </>
      )}
      {user.role === 'Manager' && (
        <>
          <Dashboard onLogout={handleLogout} />
          <div className="container">
            <ManagerView user={user} />
          </div>
        </>
      )}
      {user.role === 'Employee' && (
        <Dashboard onLogout={handleLogout} />
      )}
    </div>
  )
}