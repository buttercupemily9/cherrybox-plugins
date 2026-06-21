const API_BASE = '/api/v1';
const INTERVAL_DAY_PRESETS = [1, 7, 14, 21, 28, 30];

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

  if (response.status === 204 || !text) return null;
  return JSON.parse(text);
}

const api = {
  getUserTimePolicy: () => apiRequest('/settings/user-time-policy'),
  updateUserTimePolicy: (defaultAutoReplenishIntervalDays, defaultAutoReplenishMinutes) =>
    apiRequest('/settings/user-time-policy', {
      method: 'PATCH',
      body: JSON.stringify({ defaultAutoReplenishIntervalDays, defaultAutoReplenishMinutes }),
    }),
  listUsers: () => apiRequest('/users'),
  updateUser: (id, data) =>
    apiRequest(`/users/${encodeURIComponent(id)}`, { method: 'PATCH', body: JSON.stringify(data) }),
  listTimeRequests: () => apiRequest('/users/time-requests'),
  resolveTimeRequest: (id, approve, grantedMinutes, adminNote) =>
    apiRequest(`/users/time-requests/${encodeURIComponent(id)}/resolve`, {
      method: 'POST',
      body: JSON.stringify({
        approve,
        grantedMinutes: grantedMinutes ?? null,
        adminNote: adminNote ?? null,
      }),
    }),
};

function formatDate(value) {
  if (!value) return '';
  return new Date(value).toLocaleDateString();
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

function intervalOptions(selected) {
  return INTERVAL_DAY_PRESETS.map(function (days) {
    return (
      '<option value="' +
      days +
      '"' +
      (Number(selected) === days ? ' selected' : '') +
      '>' +
      days +
      ' days</option>'
    );
  }).join('');
}

window.ViewTimePluginUi = {
  api,
  INTERVAL_DAY_PRESETS,
  formatDate,
  escapeHtml,
  showMessage,
  intervalOptions,
};
