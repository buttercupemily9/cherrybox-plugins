(function () {
  var api = window.StoryTtsApi;
  var messageEl = document.getElementById('message');

  function showMessage(text, kind) {
    messageEl.hidden = false;
    messageEl.textContent = text;
    messageEl.className = 'plugin-message ' + (kind || '');
  }

  function audioFolderOptions(folders, selectedId) {
    return ['<option value="">Select audio folder…</option>']
      .concat(folders
        .filter(function (f) { return f.contentKind === 'Audio' || f.contentKind === 'Mix'; })
        .map(function (f) {
          var selected = selectedId && f.id === selectedId ? ' selected' : '';
          return '<option value="' + f.id + '"' + selected + '>' + f.path + ' (' + f.contentKind + ')</option>';
        }))
      .join('');
  }

  async function load() {
    var settings = await api.getSettings();
    var folders = await api.listLibraryFolders();
    document.getElementById('apiKeyStatus').textContent = settings.hasApiKey
      ? 'A Venice API key is saved.'
      : 'No API key saved yet.';
    document.getElementById('model').value = settings.model || 'tts-kokoro';
    document.getElementById('voice').value = settings.voice || 'af_sky';
    document.getElementById('responseFormat').value = settings.responseFormat || 'mp3';
    document.getElementById('speed').value = settings.speed || 1;
    document.getElementById('maxChars').value = settings.maxCharsPerRequest || 4000;
    document.getElementById('backgroundWorkerEnabled').checked = !!settings.backgroundWorkerEnabled;
    document.getElementById('autoLinkOnComplete').checked = settings.autoLinkOnComplete !== false;
    document.getElementById('audioFolder').innerHTML = audioFolderOptions(folders, settings.audioLibraryFolderId);
  }

  document.getElementById('settings-form').onsubmit = async function (e) {
    e.preventDefault();
    try {
      await api.updateSettings({
        apiKey: document.getElementById('apiKey').value || null,
        clearApiKey: document.getElementById('clearApiKey').checked,
        model: document.getElementById('model').value,
        voice: document.getElementById('voice').value,
        responseFormat: document.getElementById('responseFormat').value,
        speed: Number(document.getElementById('speed').value),
        maxCharsPerRequest: Number(document.getElementById('maxChars').value),
        audioLibraryFolderId: document.getElementById('audioFolder').value || null,
        backgroundWorkerEnabled: document.getElementById('backgroundWorkerEnabled').checked,
        autoLinkOnComplete: document.getElementById('autoLinkOnComplete').checked
      });
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
