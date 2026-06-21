const API_BASE = '/api/v1';

function getToken() {
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
  getSettings: () => apiRequest('/settings/password-reset'),
  updateSettings: (data) =>
    apiRequest('/settings/password-reset', { method: 'PUT', body: JSON.stringify(data) }),
  sendTestEmail: (toAddress) =>
    apiRequest('/settings/password-reset/test', {
      method: 'POST',
      body: JSON.stringify({ toAddress }),
    }),
};

function showMessage(text, isError = false) {
  const el = document.getElementById('message');
  if (!el) return;
  el.textContent = text;
  el.className = isError ? 'error' : 'success';
}

function fillForm(settings) {
  document.getElementById('enabled').checked = settings.enabled;
  document.getElementById('smtpHost').value = settings.smtpHost || '';
  document.getElementById('smtpPort').value = settings.smtpPort || 587;
  document.getElementById('useTls').checked = settings.useTls !== false;
  document.getElementById('username').value = settings.username || '';
  document.getElementById('password').placeholder = settings.hasPassword ? 'Leave blank to keep current password' : 'SMTP password';
  document.getElementById('fromAddress').value = settings.fromAddress || '';
  document.getElementById('fromDisplayName').value = settings.fromDisplayName || 'CherryBox';
  document.getElementById('publicBaseUrl').value = settings.publicBaseUrl || '';
  document.getElementById('tokenLifetimeMinutes').value = settings.tokenLifetimeMinutes || 60;
}

async function loadSettings() {
  try {
    fillForm(await api.getSettings());
  } catch (err) {
    showMessage(err instanceof Error ? err.message : 'Failed to load settings', true);
  }
}

document.getElementById('settingsForm')?.addEventListener('submit', async (event) => {
  event.preventDefault();
  showMessage('');
  const password = document.getElementById('password').value;
  try {
    const updated = await api.updateSettings({
      enabled: document.getElementById('enabled').checked,
      smtpHost: document.getElementById('smtpHost').value,
      smtpPort: Number(document.getElementById('smtpPort').value),
      useTls: document.getElementById('useTls').checked,
      username: document.getElementById('username').value || null,
      password: password || null,
      fromAddress: document.getElementById('fromAddress').value,
      fromDisplayName: document.getElementById('fromDisplayName').value,
      publicBaseUrl: document.getElementById('publicBaseUrl').value,
      tokenLifetimeMinutes: Number(document.getElementById('tokenLifetimeMinutes').value),
    });
    document.getElementById('password').value = '';
    fillForm(updated);
    showMessage('Settings saved');
  } catch (err) {
    showMessage(err instanceof Error ? err.message : 'Failed to save settings', true);
  }
});

document.getElementById('testEmailBtn')?.addEventListener('click', async () => {
  showMessage('');
  const toAddress = document.getElementById('testEmail').value;
  if (!toAddress) {
    showMessage('Enter a test recipient email', true);
    return;
  }

  try {
    await api.sendTestEmail(toAddress);
    showMessage('Test email sent');
  } catch (err) {
    showMessage(err instanceof Error ? err.message : 'Failed to send test email', true);
  }
});

void loadSettings();
