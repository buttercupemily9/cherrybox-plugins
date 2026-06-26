const AI_API = '/api/v1/settings/ai';

async function aiRequest(path, options = {}) {
  const token = window.CherryBoxPlugin?.getToken?.() || new URLSearchParams(location.search).get('access_token');
  const headers = { ...(options.headers || {}), 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(`${AI_API}${path}`, { ...options, headers });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || res.statusText);
  }
  if (res.status === 204) return null;
  return res.json();
}

window.AiApi = {
  getSettings: () => aiRequest(''),
  updateSettings: (body) => aiRequest('', { method: 'PUT', body: JSON.stringify(body) }),
  testConnection: (body) => aiRequest('/test', { method: 'POST', body: JSON.stringify(body || {}) }),
};
