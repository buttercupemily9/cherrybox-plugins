const TRANSCODE_API = '/api/v1/transcode';

async function transcodeRequest(path, options = {}) {
  const token = window.CherryBoxPlugin?.getToken?.() || new URLSearchParams(location.search).get('access_token');
  const headers = { ...(options.headers || {}), 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(`${TRANSCODE_API}${path}`, { ...options, headers });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || res.statusText);
  }
  if (res.status === 204) return null;
  return res.json();
}

var TranscodeApi = {
  listProfiles: () => transcodeRequest('/profiles'),
  createProfile: (body) => transcodeRequest('/profiles', { method: 'POST', body: JSON.stringify(body) }),
  updateProfile: (id, body) => transcodeRequest(`/profiles/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteProfile: (id) => transcodeRequest(`/profiles/${id}`, { method: 'DELETE' }),
  importProfile: (json) => transcodeRequest('/profiles/import', { method: 'POST', body: JSON.stringify({ json }) }),
  exportProfile: (id) => transcodeRequest(`/profiles/${id}/export`),
  getAssignments: () => transcodeRequest('/assignments'),
  updateAssignments: (body) => transcodeRequest('/assignments', { method: 'PUT', body: JSON.stringify(body) }),
  listJobs: (limit = 100) => transcodeRequest(`/jobs?limit=${limit}`),
  enqueue: (body) => transcodeRequest('/jobs/enqueue', { method: 'POST', body: JSON.stringify(body) }),
  workerStatus: () => transcodeRequest('/worker'),
  startWorker: () => transcodeRequest('/worker/start', { method: 'POST' }),
  stopWorker: () => transcodeRequest('/worker/stop', { method: 'POST' }),
  listLibraries: () => fetch('/api/v1/library/folders', {
    headers: { Authorization: `Bearer ${window.CherryBoxPlugin?.getToken?.() || new URLSearchParams(location.search).get('access_token') || ''}` }
  }).then(r => r.json()),
};

window.TranscodeApi = TranscodeApi;
