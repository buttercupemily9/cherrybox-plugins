const API = '/api/v1/transcode';

async function api(path, options = {}) {
  const token = window.CherryBoxPlugin?.getToken?.() || new URLSearchParams(location.search).get('access_token');
  const headers = { ...(options.headers || {}), 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(`${API}${path}`, { ...options, headers });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || res.statusText);
  }
  if (res.status === 204) return null;
  return res.json();
}

window.TranscodeApi = {
  listProfiles: () => api('/profiles'),
  createProfile: (body) => api('/profiles', { method: 'POST', body: JSON.stringify(body) }),
  updateProfile: (id, body) => api(`/profiles/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteProfile: (id) => api(`/profiles/${id}`, { method: 'DELETE' }),
  importProfile: (json) => api('/profiles/import', { method: 'POST', body: JSON.stringify({ json }) }),
  exportProfile: (id) => api(`/profiles/${id}/export`),
  getAssignments: () => api('/assignments'),
  updateAssignments: (body) => api('/assignments', { method: 'PUT', body: JSON.stringify(body) }),
  listJobs: (limit = 100) => api(`/jobs?limit=${limit}`),
  enqueue: (body) => api('/jobs/enqueue', { method: 'POST', body: JSON.stringify(body) }),
  workerStatus: () => api('/worker'),
  startWorker: () => api('/worker/start', { method: 'POST' }),
  stopWorker: () => api('/worker/stop', { method: 'POST' }),
  listLibraries: () => fetch('/api/v1/library/folders', {
    headers: { Authorization: `Bearer ${window.CherryBoxPlugin?.getToken?.() || new URLSearchParams(location.search).get('access_token') || ''}` }
  }).then(r => r.json()),
};
