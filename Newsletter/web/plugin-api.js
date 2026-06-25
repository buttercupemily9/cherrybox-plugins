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
  getSettings: () => apiRequest('/settings/newsletter'),
  updateSettings: (data) =>
    apiRequest('/settings/newsletter', { method: 'PUT', body: JSON.stringify(data) }),
  getServerSettings: () => apiRequest('/settings/server'),
};

function showMessage(text, isError = false) {
  const el = document.getElementById('message');
  if (!el) return;
  el.textContent = text;
  el.className = isError ? 'error' : 'success';
}

function setFormBusy(busy) {
  document.querySelectorAll('#settingsForm input, #settingsForm select, #settingsForm button').forEach((el) => {
    el.disabled = busy;
  });
}

function fillForm(settings) {
  document.getElementById('welcomeEnabled').checked = settings.welcomeEnabled !== false;
  document.getElementById('weeklyEnabled').checked = Boolean(settings.weeklyEnabled);
  document.getElementById('weeklyDay').value = settings.weeklyDay || 'Sunday';
  document.getElementById('weeklyTime').value = settings.weeklyTime || '09:00';
  document.getElementById('publicBaseUrl').value = settings.publicBaseUrl || '';
}

function readFormValues() {
  return {
    welcomeEnabled: document.getElementById('welcomeEnabled').checked,
    weeklyEnabled: document.getElementById('weeklyEnabled').checked,
    weeklyDay: document.getElementById('weeklyDay').value,
    weeklyTime: document.getElementById('weeklyTime').value,
    publicBaseUrl: document.getElementById('publicBaseUrl').value.trim(),
  };
}

function validateSettings(data) {
  if (!data.publicBaseUrl) return 'Public base URL is required.';
  if (!/^\d{2}:\d{2}$/.test(data.weeklyTime)) return 'Weekly time must use HH:mm format.';
  return null;
}

async function loadSettings() {
  setFormBusy(true);
  showMessage('');
  try {
    const settings = await api.getSettings();
    if (!settings.publicBaseUrl) {
      try {
        const server = await api.getServerSettings();
        if (server?.publicUrl) settings.publicBaseUrl = server.publicUrl;
      } catch {
        // optional hint only
      }
    }
    fillForm(settings);
  } catch (err) {
    showMessage(err instanceof Error ? err.message : 'Failed to load settings', true);
  } finally {
    setFormBusy(false);
  }
}

function bindSettingsForm() {
  const form = document.getElementById('settingsForm');
  if (!form) return;

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    showMessage('');

    const data = readFormValues();
    const validationError = validateSettings(data);
    if (validationError) {
      showMessage(validationError, true);
      return;
    }

    setFormBusy(true);
    try {
      const updated = await api.updateSettings(data);
      fillForm(updated);
      showMessage('Settings saved');
    } catch (err) {
      showMessage(err instanceof Error ? err.message : 'Failed to save settings', true);
    } finally {
      setFormBusy(false);
    }
  });
}

function initNewsletterSettingsUi() {
  bindSettingsForm();
  void loadSettings();
}

window.NewsletterPluginUi = { initNewsletterSettingsUi };
