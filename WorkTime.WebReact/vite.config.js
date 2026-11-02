import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Dev-сервер Vite проксирует /api на ASP.NET Core (http://localhost:5114),
// чтобы запросы логина/синхронизации не давали 404 в режиме npm run dev
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5114',
        changeOrigin: true
      }
    }
  }
})
