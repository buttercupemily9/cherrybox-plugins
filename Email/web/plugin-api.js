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
  getSettings: () => apiRequest('/settings/email'),
  updateSettings: (data) =>
    apiRequest('/settings/email', { method: 'PUT', body: JSON.stringify(data) }),
  sendTestEmail: (toAddress, settings) =>
    apiRequest('/settings/email/test', {
      method: 'POST',
      body: JSON.stringify({ toAddress, settings }),
    }),
};

function showMessage(text, isError = false) {
  const el = document.getElementById('message');
  if (!el) return;
  el.textContent = text;
  el.className = isError ? 'error' : 'success';
}

function setFormBusy(busy) {
  document.querySelectorAll('#settingsForm input, #settingsForm button, #testEmail, #testEmailBtn').forEach((el) => {
    el.disabled = busy;
  });
}

function fillForm(settings) {
  document.getElementById('smtpHost').value = settings.smtpHost || '';
  document.getElementById('smtpPort').value = settings.smtpPort || 587;
  document.getElementById('useTls').checked = settings.useTls !== false;
  document.getElementById('username').value = settings.username || '';
  document.getElementById('password').value = '';
  document.getElementById('password').placeholder = settings.hasPassword
    ? 'Leave blank to keep current password'
    : 'SMTP password';
  document.getElementById('fromAddress').value = settings.fromAddress || '';
  document.getElementById('fromDisplayName').value = settings.fromDisplayName || 'CherryBox';
}

function readFormValues() {
  return {
    smtpHost: document.getElementById('smtpHost').value.trim(),
    smtpPort: Number(document.getElementById('smtpPort').value),
    useTls: document.getElementById('useTls').checked,
    username: document.getElementById('username').value.trim() || null,
    password: document.getElementById('password').value || null,
    fromAddress: document.getElementById('fromAddress').value.trim(),
    fromDisplayName: document.getElementById('fromDisplayName').value.trim() || 'CherryBox',
  };
}

function validateSettings(data) {
  if (Number.isNaN(data.smtpPort) || data.smtpPort <= 0)
    return 'SMTP port must be a positive number.';
  if (!data.smtpHost) return 'SMTP host is required.';
  if (!data.fromAddress) return 'From address is required.';
  return null;
}

function validateTestSettings(data) {
  const validationError = validateSettings(data);
  if (validationError) return validationError;
  if (data.username && !data.password) {
    const passwordField = document.getElementById('password');
    const hasStoredPassword = passwordField?.placeholder?.includes('keep current password');
    if (!hasStoredPassword) return 'SMTP password is required to send a test email.';
  }
  return null;
}

async function loadSettings() {
  setFormBusy(true);
  showMessage('');
  try {
    const settings = await api.getSettings();
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

function bindTestEmailButton() {
  document.getElementById('testEmailBtn')?.addEventListener('click', async () => {
    showMessage('');
    const toAddress = document.getElementById('testEmail')?.value?.trim();
    if (!toAddress) {
      showMessage('Enter a test recipient email', true);
      return;
    }

    const data = readFormValues();
    const validationError = validateTestSettings(data);
    if (validationError) {
      showMessage(validationError, true);
      return;
    }

    setFormBusy(true);
    try {
      await api.sendTestEmail(toAddress, data);
      showMessage(`Test email sent to ${toAddress}`);
    } catch (err) {
      showMessage(err instanceof Error ? err.message : 'Failed to send test email', true);
    } finally {
      setFormBusy(false);
    }
  });
}

function initEmailSettingsUi() {
  bindSettingsForm();
  bindTestEmailButton();
  void loadSettings();
}

window.EmailPluginUi = { initEmailSettingsUi };
