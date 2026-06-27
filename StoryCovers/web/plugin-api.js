const STORY_COVERS_API = '/api/v1/story-covers';

async function storyCoversRequest(path, options = {}) {
  const token = window.CherryBoxPlugin?.getToken?.() || new URLSearchParams(location.search).get('access_token');
  const headers = { ...(options.headers || {}), 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(`${STORY_COVERS_API}${path}`, { ...options, headers });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || res.statusText);
  }
  if (res.status === 204) return null;
  return res.json();
}

window.StoryCoversApi = {
  getSettings: () => storyCoversRequest('/settings'),
  updateSettings: (body) => storyCoversRequest('/settings', { method: 'PUT', body: JSON.stringify(body) }),
  listJobs: (limit = 50) => storyCoversRequest(`/jobs?limit=${limit}`),
  enqueue: (storyMediaItemId) =>
    storyCoversRequest('/jobs/enqueue', { method: 'POST', body: JSON.stringify({ storyMediaItemId }) }),
  enqueueAllMissing: () => storyCoversRequest('/jobs/enqueue-all', { method: 'POST' }),
  cancelJob: (id) => storyCoversRequest(`/jobs/${id}/cancel`, { method: 'POST' }),
  getWorkerStatus: () => storyCoversRequest('/worker'),
  startWorker: () => storyCoversRequest('/worker/start', { method: 'POST' }),
  stopWorker: () => storyCoversRequest('/worker/stop', { method: 'POST' }),
};
