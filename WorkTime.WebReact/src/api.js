// API-клиент с JWT-аутентификацией
const API_BASE = import.meta.env.VITE_API_BASE || '';

let token = localStorage.getItem('wt_token') || '';
let currentUser = null;

export function setToken(t) {
  token = t || '';
  if (t) localStorage.setItem('wt_token', t);
  else localStorage.removeItem('wt_token');
}

export function getToken() {
  return token;
}

export function setCurrentUser(u) {
  currentUser = u;
  if (u) localStorage.setItem('wt_user', JSON.stringify(u));
  else localStorage.removeItem('wt_user');
}

export function getCurrentUser() {
  if (currentUser) return currentUser;
  try {
    const raw = localStorage.getItem('wt_user');
    currentUser = raw ? JSON.parse(raw) : null;
  } catch {
    currentUser = null;
  }
  return currentUser;
}

export function isAuthenticated() {
  return !!token;
}

async function request(path, options = {}) {
  const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const resp = await fetch(`${API_BASE}${path}`, { ...options, headers });

  if (resp.status === 401) {
    setToken('');
    setCurrentUser(null);
    throw new Error('Не авторизован');
  }

  if (!resp.ok) {
    let msg = `HTTP ${resp.status}`;
    try {
      const data = await resp.json();
      if (data.message) msg = data.message;
    } catch { /* ignore */ }
    throw new Error(msg);
  }

  if (resp.status === 204) return null;
  return resp.json();
}

// Экспорт отдаёт бинарные файлы (PDF/XLSX), поэтому ответ читается как Blob,
// а не как JSON, и сразу сохраняется на диск.
async function downloadFile(path, filename) {
  const headers = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const resp = await fetch(`${API_BASE}${path}`, { headers });

  if (resp.status === 401) {
    setToken('');
    setCurrentUser(null);
    throw new Error('Не авторизован');
  }

  if (!resp.ok) {
    let msg = `HTTP ${resp.status}`;
    try {
      const data = await resp.json();
      if (data.message) msg = data.message;
    } catch { /* binary or empty body */ }
    throw new Error(msg);
  }

  const blob = await resp.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

export const api = {
  // Auth
  login: (email, password) => request('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ email, password })
  }),
  register: (email, password, fullName) => request('/api/auth/register', {
    method: 'POST',
    body: JSON.stringify({ email, password, fullName })
  }),

  // WorkEvents
  createEvent: (data) => request('/api/workevents', {
    method: 'POST',
    body: JSON.stringify(data)
  }),
  getEvents: (params = '') => request(`/api/workevents${params}`),
  getEventsSince: (userId, sinceServerOrder) =>
    request(`/api/workevents/events/since?userId=${userId}&sinceServerOrder=${sinceServerOrder || ''}`),
  syncBatch: (events) => request('/api/workevents/batch', {
    method: 'POST',
    body: JSON.stringify(events)
  }),
  // Ручная корректировка события (FR7)
  updateEvent: (id, data) => request(`/api/workevents/${id}`, {
    method: 'PUT',
    body: JSON.stringify(data)
  }),
  deleteEvent: (id) => request(`/api/workevents/${id}`, { method: 'DELETE' }),

  // Sync operations
  postOperations: (ops) => request('/api/sync/operations', {
    method: 'POST',
    body: JSON.stringify(ops)
  }),

  // Users (admin)
  getUsers: () => request('/api/users'),
  createUser: (data) => request('/api/users', { method: 'POST', body: JSON.stringify(data) }),
  updateUser: (id, data) => request(`/api/users/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
  deleteUser: (id) => request(`/api/users/${id}`, { method: 'DELETE' }),
  blockUser: (id) => request(`/api/users/${id}/block`, { method: 'POST' }),
  unblockUser: (id) => request(`/api/users/${id}/unblock`, { method: 'POST' }),

  // Projects
  getProjects: () => request('/api/projects'),
  createProject: (data) => request('/api/projects', { method: 'POST', body: JSON.stringify(data) }),
  updateProject: (id, data) => request(`/api/projects/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
  deleteProject: (id) => request(`/api/projects/${id}`, { method: 'DELETE' }),

  // TimeEntries
  getTimeEntries: (params = '') => request(`/api/timeentries${params}`),
  createTimeEntry: (data) => request('/api/timeentries', { method: 'POST', body: JSON.stringify(data) }),
  updateTimeEntry: (id, data) => request(`/api/timeentries/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
  deleteTimeEntry: (id) => request(`/api/timeentries/${id}`, { method: 'DELETE' }),

  // Devices
  getDevices: () => request('/api/devices'),
  registerDevice: (data) => request('/api/devices', { method: 'POST', body: JSON.stringify(data) }),

  // Reports
  getSummary: (userId, from, to) =>
    request(`/api/reports/summary?userId=${userId}&from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
  getTeamReport: (from, to) =>
    request(`/api/reports/team?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
  exportPdf: (userId, from, to) =>
    downloadFile(`/api/reports/export/pdf?userId=${userId}&from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
      `report_${userId}.pdf`),
  exportXlsx: (userId, from, to) =>
    downloadFile(`/api/reports/export/xlsx?userId=${userId}&from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
      `report_${userId}.xlsx`),

  // Audit logs (admin)
  getAuditLogs: (page = 1, pageSize = 50) =>
    request(`/api/auditlogs?page=${page}&pageSize=${pageSize}`)
};