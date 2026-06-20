const API_BASE = '/api/v1';

function getToken() {
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
      if (parsed.error) message = parsed.error;
    } catch {
      // keep raw text
    }
    throw new Error(message);
  }

  return text ? JSON.parse(text) : null;
}

const api = {
  listDownloads: () => apiRequest('/downloads'),
  listDownloadHistory: () => apiRequest('/downloads/history'),
  enqueueDownload: (url, targetFolderId) =>
    apiRequest('/downloads', {
      method: 'POST',
      body: JSON.stringify({ url, targetFolderId: targetFolderId || null }),
    }),
  retryDownload: (id) => apiRequest(`/downloads/${id}/retry`, { method: 'POST' }),
  cancelDownload: (id) => apiRequest(`/downloads/${id}/cancel`, { method: 'POST' }),
  getDownloadSettings: () => apiRequest('/settings/downloads'),
  updateDownloadSettings: (data) =>
    apiRequest('/settings/downloads', { method: 'PUT', body: JSON.stringify(data) }),
  listFolders: () => apiRequest('/library/folders'),
};

const STATUS_LABEL = {
  Pending: 'Queued',
  Running: 'Downloading…',
  Completed: 'Done',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
  Blocked: 'Blocked',
};

function formatTime(value) {
  if (!value) return '—';
  return new Date(value).toLocaleString();
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

window.DownloadPluginUi = { api, STATUS_LABEL, formatTime, escapeHtml, showMessage };
