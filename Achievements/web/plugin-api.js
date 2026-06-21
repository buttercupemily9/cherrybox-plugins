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
  listAchievements: () => apiRequest('/achievements'),
  getAchievementSummary: () => apiRequest('/achievements/summary'),
  syncAchievements: () => apiRequest('/achievements/sync', { method: 'POST' }),
};

window.AchievementsPluginUi = {
  api: api,
  escapeHtml: function (value) {
    return String(value ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  },
  formatTime: function (value) {
    if (!value) return '—';
    return new Date(value).toLocaleString();
  },
};
