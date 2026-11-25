import React from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import './index.css'
import { register } from './registerServiceWorker'
import { startOfflineSync } from './offlineSync'

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
)

// FR10: очередь уходит на сервер сама, когда восстанавливается связь.
register()
startOfflineSync()
