(function () {
  var api = window.GoogleTagImagesApi;
  var messageEl = document.getElementById('message');

  function showMessage(text, kind) {
    messageEl.hidden = false;
    messageEl.textContent = text;
    messageEl.className = 'plugin-message ' + (kind || '');
  }

  async function load() {
    var settings = await api.getSettings();
    document.getElementById('apiKeyStatus').textContent = settings.hasApiKey
      ? 'A Google API key is saved.'
      : 'No API key saved yet.';
    document.getElementById('searchEngineId').value = settings.searchEngineId || '';
    document.getElementById('searchQuerySuffix').value = settings.searchQuerySuffix || '';
    document.getElementById('maxTagsPerRun').value = settings.maxTagsPerRun || 50;
    document.getElementById('requestDelayMs').value = settings.requestDelayMs || 500;
  }

  document.getElementById('settings-form').onsubmit = async function (e) {
    e.preventDefault();
    try {
      var payload = {
        clearApiKey: document.getElementById('clearApiKey').checked,
        searchEngineId: document.getElementById('searchEngineId').value.trim(),
        searchQuerySuffix: document.getElementById('searchQuerySuffix').value,
        maxTagsPerRun: Number(document.getElementById('maxTagsPerRun').value),
        requestDelayMs: Number(document.getElementById('requestDelayMs').value)
      };
      var apiKey = document.getElementById('apiKey').value.trim();
      if (apiKey) payload.apiKey = apiKey;
      await api.updateSettings(payload);
      document.getElementById('apiKey').value = '';
      document.getElementById('clearApiKey').checked = false;
      showMessage('Settings saved.', 'success');
      await load();
    } catch (err) {
      showMessage(err.message || 'Failed to save settings', 'error');
    }
  };

  document.getElementById('test-btn').onclick = async function () {
    try {
      var result = await api.testConnection();
      showMessage(result.message, result.ok ? 'success' : 'error');
    } catch (err) {
      showMessage(err.message || 'Test failed', 'error');
    }
  };

  load().catch(function (err) {
    showMessage(err.message || 'Failed to load settings', 'error');
  });
})();
