const GOOGLE_TAG_IMAGES_API = '/api/v1/settings/google-tag-images';

async function googleTagImagesRequest(path, options = {}) {
  const token = window.CherryBoxPlugin?.getToken?.() || new URLSearchParams(location.search).get('access_token');
  const headers = { ...(options.headers || {}), 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(`${GOOGLE_TAG_IMAGES_API}${path}`, { ...options, headers });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || res.statusText);
  }
  if (res.status === 204) return null;
  return res.json();
}

window.GoogleTagImagesApi = {
  getSettings: () => googleTagImagesRequest(''),
  updateSettings: (body) => googleTagImagesRequest('', { method: 'PUT', body: JSON.stringify(body) }),
  testConnection: () => googleTagImagesRequest('/test', { method: 'POST', body: '{}' }),
};
