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
  getSettings: () => apiRequest('/settings/password-reset'),
  updateSettings: (data) =>
    apiRequest('/settings/password-reset', { method: 'PUT', body: JSON.stringify(data) }),
  getServerSettings: () => apiRequest('/settings/server'),
};

function showMessage(text, isError = false) {
  const el = document.getElementById('message');
  if (!el) return;
  el.textContent = text;
  el.className = isError ? 'error' : 'success';
}

function setFormBusy(busy) {
  document.querySelectorAll('#settingsForm input, #settingsForm button').forEach((el) => {
    el.disabled = busy;
  });
}

function fillForm(settings) {
  document.getElementById('enabled').checked = Boolean(settings.enabled);
  document.getElementById('tokenLifetimeMinutes').value = settings.tokenLifetimeMinutes || 60;
}

function readFormValues() {
  return {
    enabled: document.getElementById('enabled').checked,
    tokenLifetimeMinutes: Number(document.getElementById('tokenLifetimeMinutes').value),
  };
}

function validateSettings(data) {
  if (Number.isNaN(data.tokenLifetimeMinutes) || data.tokenLifetimeMinutes < 5 || data.tokenLifetimeMinutes > 1440)
    return 'Reset link lifetime must be between 5 and 1440 minutes.';

  return null;
}

async function loadSettings() {
  setFormBusy(true);
  showMessage('');
  try {
    const settings = await api.getSettings();
    fillForm(settings);
    if (settings.enabled) {
      try {
        const server = await api.getServerSettings();
        if (!server?.publicUrl) {
          showMessage('Password reset is enabled but no public URL is set. Configure one under Settings → General.', true);
        }
      } catch {
        // optional hint only
      }
    }
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

    if (data.enabled) {
      try {
        const server = await api.getServerSettings();
        if (!server?.publicUrl) {
          showMessage('Set a public URL under Settings → General before enabling password reset.', true);
          return;
        }
      } catch {
        showMessage('Could not verify the public URL. Check Settings → General.', true);
        return;
      }
    }

    setFormBusy(true);
    try {
      const updated = await api.updateSettings({
        enabled: data.enabled,
        publicBaseUrl: '',
        tokenLifetimeMinutes: data.tokenLifetimeMinutes,
      });
      fillForm(updated);
      showMessage('Settings saved');
    } catch (err) {
      showMessage(err instanceof Error ? err.message : 'Failed to save settings', true);
    } finally {
      setFormBusy(false);
    }
  });
}

function initPasswordResetSettingsUi() {
  bindSettingsForm();
  void loadSettings();
}

window.PasswordResetPluginUi = { initPasswordResetSettingsUi };
