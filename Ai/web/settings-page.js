(function () {
  var api = window.AiApi;
  var messageEl = document.getElementById('message');

  function showMessage(text, kind) {
    messageEl.hidden = false;
    messageEl.textContent = text;
    messageEl.className = 'plugin-message ' + (kind || '');
  }

  async function load() {
    var settings = await api.getSettings();
    document.getElementById('apiKeyStatus').textContent = settings.hasApiKey
      ? 'A Venice API key is saved.'
      : 'No API key saved yet.';
    document.getElementById('model').value = settings.model || 'tts-kokoro';
    document.getElementById('chatModel').value = settings.chatModel || 'venice-uncensored';
    document.getElementById('imageModel').value = settings.imageModel || 'venice-sd35';
    document.getElementById('voice').value = settings.voice || 'af_sky';
    document.getElementById('responseFormat').value = settings.responseFormat || 'mp3';
    document.getElementById('speed').value = settings.speed || 1;
    document.getElementById('maxChars').value = settings.maxCharsPerRequest || 4000;
  }

  document.getElementById('settings-form').onsubmit = async function (e) {
    e.preventDefault();
    try {
      var payload = {
        clearApiKey: document.getElementById('clearApiKey').checked,
        model: document.getElementById('model').value,
        chatModel: document.getElementById('chatModel').value,
        imageModel: document.getElementById('imageModel').value,
        voice: document.getElementById('voice').value,
        responseFormat: document.getElementById('responseFormat').value,
        speed: Number(document.getElementById('speed').value),
        maxCharsPerRequest: Number(document.getElementById('maxChars').value)
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
      var result = await api.testConnection({ sampleText: 'CherryBox Venice text to speech test.' });
      showMessage(result.message, result.ok ? 'success' : 'error');
    } catch (err) {
      showMessage(err.message || 'Test failed', 'error');
    }
  };

  load().catch(function (err) {
    showMessage(err.message || 'Failed to load settings', 'error');
  });
})();
