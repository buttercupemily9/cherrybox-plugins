const API_BASE = '/api/v1';

function getToken() {
  try {
    const params = new URLSearchParams(window.location.search);
    const queryToken = params.get('access_token');
    if (queryToken) return queryToken;
  } catch {
    // ignore
  }
  return localStorage.getItem('cherrybox_token');
}

async function apiRequest(path, options = {}) {
  const headers = { ...(options.headers || {}) };
  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;
  if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';

  const response = await fetch(`${API_BASE}${path}`, { ...options, headers });
  const text = await response.text();
  if (!response.ok) {
    let message = text || response.statusText;
    try {
      const parsed = JSON.parse(text);
      message = parsed.error ?? parsed.detail ?? parsed.title ?? message;
    } catch {
      // keep raw text
    }
    throw new Error(message);
  }

  if (response.status === 204 || !text)
    return null;

  return JSON.parse(text);
}

async function downloadRequest(path, fileName) {
  const token = getToken();
  const response = await fetch(`${API_BASE}${path}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (!response.ok) {
    const text = await response.text();
    let message = text || response.statusText;
    try {
      const parsed = JSON.parse(text);
      message = parsed.error ?? parsed.detail ?? parsed.title ?? message;
    } catch {
      // keep raw text
    }
    throw new Error(message);
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName || 'backup.box';
  link.style.display = 'none';
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

async function uploadFormRequest(path, file) {
  const token = getToken();
  const form = new FormData();
  form.append('file', file);
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: form,
  });
  const text = await response.text();
  if (!response.ok) {
    let message = text || response.statusText;
    try {
      const parsed = JSON.parse(text);
      message = parsed.error ?? parsed.detail ?? parsed.title ?? message;
    } catch {
      // keep raw text
    }
    throw new Error(message);
  }
  return text ? JSON.parse(text) : null;
}

const api = {
  listBackups: () => apiRequest('/backups'),
  createBackup: () => apiRequest('/backups', { method: 'POST' }),
  downloadBackup: (id, fileName) => downloadRequest(`/backups/${encodeURIComponent(id)}/download`, fileName),
  restoreBackup: (id) => apiRequest(`/backups/${encodeURIComponent(id)}/restore`, { method: 'POST' }),
  deleteBackup: (id) => apiRequest(`/backups/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  importBackupUpload: (file) => uploadFormRequest('/backups/import', file),
  restoreBackupUpload: (file) => uploadFormRequest('/backups/restore', file),
  getBackupSettings: () => apiRequest('/settings/backup'),
  updateBackupSettings: (data) =>
    apiRequest('/settings/backup', { method: 'PUT', body: JSON.stringify(data) }),
};

function formatTime(value) {
  if (!value) return '—';
  return new Date(value).toLocaleString();
}

function formatBytes(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function showMessage(el, text, isError = false) {
  if (!el) return;
  el.textContent = text;
  el.className = isError ? 'plugin-message error' : 'plugin-message meta';
  el.hidden = !text;
}

window.BackupPluginUi = { api, formatTime, formatBytes, escapeHtml, showMessage };
