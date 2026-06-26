const STORY_TTS_API = '/api/v1/story-tts';

async function storyTtsRequest(path, options = {}) {
  const token = window.CherryBoxPlugin?.getToken?.() || new URLSearchParams(location.search).get('access_token');
  const headers = { ...(options.headers || {}), 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(`${STORY_TTS_API}${path}`, { ...options, headers });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || res.statusText);
  }
  if (res.status === 204) return null;
  return res.json();
}

async function coreRequest(path, options = {}) {
  const token = window.CherryBoxPlugin?.getToken?.() || new URLSearchParams(location.search).get('access_token');
  const headers = { ...(options.headers || {}), 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(path, { ...options, headers });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || res.statusText);
  }
  if (res.status === 204) return null;
  return res.json();
}

var StoryTtsApi = {
  getSettings: () => storyTtsRequest('/settings'),
  updateSettings: (body) => storyTtsRequest('/settings', { method: 'PUT', body: JSON.stringify(body) }),
  listJobs: (limit = 100) => storyTtsRequest(`/jobs?limit=${limit}`),
  enqueue: (body) => storyTtsRequest('/jobs/enqueue', { method: 'POST', body: JSON.stringify(body) }),
  workerStatus: () => storyTtsRequest('/worker'),
  startWorker: () => storyTtsRequest('/worker/start', { method: 'POST' }),
  stopWorker: () => storyTtsRequest('/worker/stop', { method: 'POST' }),
  listStories: () => coreRequest('/api/v1/stories'),
  listLibraryFolders: () => coreRequest('/api/v1/library/folders'),
};

window.StoryTtsApi = StoryTtsApi;
