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
  getDownloadActiveCount: () => apiRequest('/downloads/active-count'),
  listAdminDownloadQueue: () => apiRequest('/settings/downloads/queue'),
  retryAdminDownload: (id) => apiRequest('/settings/downloads/queue/' + encodeURIComponent(id) + '/retry', { method: 'POST' }),
  cancelAdminDownload: (id) => apiRequest('/settings/downloads/queue/' + encodeURIComponent(id) + '/cancel', { method: 'POST' }),
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
  listSiteAuth: () => apiRequest('/settings/downloads/site-auth'),
  upsertSiteAuth: (data) =>
    apiRequest('/settings/downloads/site-auth', { method: 'PUT', body: JSON.stringify(data) }),
  removeSiteAuth: (siteKey) =>
    apiRequest(`/settings/downloads/site-auth/${encodeURIComponent(siteKey)}`, { method: 'DELETE' }),
  testSiteAuth: (data) =>
    apiRequest('/settings/downloads/site-auth/test', { method: 'POST', body: JSON.stringify(data) }),
  uploadSiteCookies: async (siteKey, file) => {
    const formData = new FormData();
    formData.append('file', file);
    const headers = {};
    const token = getToken();
    if (token) headers.Authorization = `Bearer ${token}`;
    const response = await fetch(
      `${API_BASE}/settings/downloads/site-auth/${encodeURIComponent(siteKey)}/cookies`,
      { method: 'POST', headers, body: formData },
    );
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
  },
  uploadSiteCookiesText: async (siteKey, cookieText) => {
    const file = new File([String(cookieText || '')], 'cookies.txt', { type: 'text/plain' });
    return api.uploadSiteCookies(siteKey, file);
  },
  listFolders: () => apiRequest('/library/folders'),
  getDownloadLimit: () => apiRequest('/downloads/limit'),
  requestMoreDownloads: (data) =>
    apiRequest('/downloads/limit/request', { method: 'POST', body: JSON.stringify(data) }),
  getDownloadLimitPolicy: () => apiRequest('/settings/downloads/limit-policy'),
  updateDownloadLimitPolicy: (data) =>
    apiRequest('/settings/downloads/limit-policy', { method: 'PUT', body: JSON.stringify(data) }),
  listDownloadLimitUsers: () => apiRequest('/settings/downloads/limit-users'),
  updateDownloadLimitUser: (userId, data) =>
    apiRequest('/settings/downloads/limit-users/' + encodeURIComponent(userId), {
      method: 'PUT',
      body: JSON.stringify(data),
    }),
  listDownloadLimitRequests: () => apiRequest('/settings/downloads/limit-requests'),
  resolveDownloadLimitRequest: (id, approve, grantedCount, adminNote) =>
    apiRequest('/settings/downloads/limit-requests/' + encodeURIComponent(id) + '/resolve', {
      method: 'POST',
      body: JSON.stringify({
        approve: approve,
        grantedCount: grantedCount ?? null,
        adminNote: adminNote ?? null,
      }),
    }),
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
