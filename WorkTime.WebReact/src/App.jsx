import React, { useState, useEffect } from 'react'
import { createEvent, getOutbox, syncOutbox } from './pwaClient'

export default function App(){
  const [userId, setUserId] = useState(localStorage.getItem('userId') || '')
  const [deviceId, setDeviceId] = useState(localStorage.getItem('deviceId') || '')
  const [log, setLog] = useState('')
  const [outbox, setOutbox] = useState([])
  const [lastResults, setLastResults] = useState([])

  useEffect(() => { loadOutbox() }, [])

  async function loadOutbox(){
    const items = await getOutbox()
    setOutbox(items)
  }

  function append(s){
    setLog(l => l + s + '\n')
  }

  const onCreate = async (type) => {
    if (!userId || !deviceId) { append('userId/deviceId required'); return }
    const ev = await createEvent({ userId, deviceId, type })
    append('Created: ' + JSON.stringify(ev))
    await loadOutbox()
    // register background sync when service worker available
    if ('serviceWorker' in navigator && 'SyncManager' in window){
      try{ const reg = await navigator.serviceWorker.ready; await reg.sync.register('sync-outbox'); append('Background sync registered'); }catch(e){ append('Background sync registration failed: '+e.message) }
    }
  }

  const onSync = async () => {
    append('Sync started...')
    try{
      const res = await syncOutbox({ apiBase: '' })
      append('Sync result: ' + JSON.stringify(res))
      setLastResults(res || [])
      await loadOutbox()
    }catch(e){
      append('Sync error: ' + e.message)
    }
  }

  return (
    <div style={{ padding: 20 }}>
      <h1>WorkTime PWA</h1>
      <div>
        <label>User ID: <input value={userId} onChange={e => { setUserId(e.target.value); localStorage.setItem('userId', e.target.value) }} /></label>
        <label style={{ marginLeft: 10 }}>Device ID: <input value={deviceId} onChange={e => { setDeviceId(e.target.value); localStorage.setItem('deviceId', e.target.value) }} /></label>
      </div>
      <div style={{ marginTop: 10 }}>
        <button onClick={() => onCreate('StartWork')}>Start Work</button>
        <button onClick={() => onCreate('StartBreak')} style={{ marginLeft: 8 }}>Start Break</button>
        <button onClick={() => onCreate('EndBreak')} style={{ marginLeft: 8 }}>End Break</button>
        <button onClick={() => onCreate('EndWork')} style={{ marginLeft: 8 }}>End Work</button>
      </div>
      <div style={{ marginTop: 10 }}>
        <button onClick={onSync}>Sync Now</button>
      </div>
      <h3>Outbox ({outbox.length})</h3>
      <pre style={{ maxHeight: 200, overflow: 'auto', background: '#f0f0f0', padding: 8 }}>{JSON.stringify(outbox, null, 2)}</pre>

      <h3>Last sync results ({lastResults.length})</h3>
      <div style={{ maxHeight: 200, overflow: 'auto', background: '#fff', padding: 8 }}>
        {lastResults.length === 0 && <div style={{ color: '#666' }}>Нет последних результатов</div>}
        {lastResults.map(r => (
          <div key={r.id} style={{ padding: 6, borderBottom: '1px solid #eee' }}>
            <strong>{r.id}</strong> — <em>{r.status}</em>
            {r.message && <div style={{ color: '#a00' }}>{r.message}</div>}
          </div>
        ))}
      </div>
      <h3>Log</h3>
      <pre style={{ whiteSpace: 'pre-wrap', background: '#222', color: '#fff', padding: 8 }}>{log}</pre>
    </div>
  )
}
