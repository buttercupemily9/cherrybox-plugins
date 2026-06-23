(function () {
  var api = window.TranscodeApi;
  var msg = document.getElementById('message');
  if (!api) {
    if (msg) {
      msg.textContent = 'Transcoder plugin API failed to load.';
      msg.className = 'plugin-message error';
      msg.hidden = false;
    }
    return;
  }

  var tbody = document.querySelector('#profiles-table tbody');
  var editor = document.getElementById('editor');

  function showMessage(text, kind) {
    msg.textContent = text;
    msg.className = 'plugin-message ' + (kind || 'success');
    msg.hidden = false;
  }

  function pascalToCamel(s) {
    return s.charAt(0).toLowerCase() + s.slice(1);
  }

  function readForm() {
    return {
      name: document.getElementById('name').value.trim(),
      container: document.getElementById('container').value,
      video: {
        codec: document.getElementById('videoCodec').value,
        rateControl: document.getElementById('rateControl').value,
        crf: Number(document.getElementById('crf').value) || null,
        bitrateKbps: Number(document.getElementById('videoBitrate').value) || null,
        maxWidth: Number(document.getElementById('maxWidth').value) || null,
        maxHeight: Number(document.getElementById('maxHeight').value) || null,
      },
      audio: {
        codec: document.getElementById('audioCodec').value,
        channels: Number(document.getElementById('channels').value) || 2,
        bitrateKbps: Number(document.getElementById('audioBitrate').value) || 128,
        sampleRateHz: Number(document.getElementById('sampleRate').value) || 48000,
      },
      fileSizeTargetMB: Number(document.getElementById('fileSizeTarget').value) || null,
      skipIfCompatible: document.getElementById('skipIfCompatible').checked,
    };
  }

  function fillForm(profile) {
    document.getElementById('profile-id').value = (profile && profile.id) || '';
    document.getElementById('name').value = (profile && profile.name) || '';
    document.getElementById('container').value = pascalToCamel((profile && profile.container) || 'mp4');
    document.getElementById('videoCodec').value = pascalToCamel((profile && profile.video && profile.video.codec) || 'h264');
    document.getElementById('rateControl').value = pascalToCamel((profile && profile.video && profile.video.rateControl) || 'quality');
    document.getElementById('crf').value = (profile && profile.video && profile.video.crf != null) ? profile.video.crf : 23;
    document.getElementById('videoBitrate').value = (profile && profile.video && profile.video.bitrateKbps != null) ? profile.video.bitrateKbps : '';
    document.getElementById('fileSizeTarget').value = (profile && profile.fileSizeTargetMB != null) ? profile.fileSizeTargetMB : '';
    document.getElementById('maxWidth').value = (profile && profile.video && profile.video.maxWidth != null) ? profile.video.maxWidth : 1920;
    document.getElementById('maxHeight').value = (profile && profile.video && profile.video.maxHeight != null) ? profile.video.maxHeight : 1080;
    document.getElementById('audioCodec').value = pascalToCamel((profile && profile.audio && profile.audio.codec) || 'aac');
    document.getElementById('channels').value = (profile && profile.audio && profile.audio.channels != null) ? profile.audio.channels : 2;
    document.getElementById('audioBitrate').value = (profile && profile.audio && profile.audio.bitrateKbps != null) ? profile.audio.bitrateKbps : 128;
    document.getElementById('sampleRate').value = (profile && profile.audio && profile.audio.sampleRateHz != null) ? profile.audio.sampleRateHz : 48000;
    document.getElementById('skipIfCompatible').checked = profile ? profile.skipIfCompatible !== false : true;
    document.getElementById('editor-title').textContent = profile ? 'Edit profile' : 'New profile';
    editor.hidden = false;
  }

  async function refresh() {
    var profiles = await api.listProfiles();
    tbody.innerHTML = profiles.map(function (p) {
      return '<tr><td>' + p.name + '</td><td>' + p.container + '</td><td>' + p.video.codec + '</td><td>' + p.audio.codec +
        '</td><td><button type="button" data-edit="' + p.id + '">Edit</button> ' +
        '<button type="button" data-export="' + p.id + '">Export</button> ' +
        '<button type="button" data-delete="' + p.id + '">Delete</button></td></tr>';
    }).join('');
  }

  document.getElementById('new-btn').onclick = function () { fillForm(null); };
  document.getElementById('cancel-btn').onclick = function () { editor.hidden = true; };
  document.getElementById('profile-form').onsubmit = async function (e) {
    e.preventDefault();
    var body = readForm();
    var id = document.getElementById('profile-id').value;
    if (id) await api.updateProfile(id, body);
    else await api.createProfile(body);
    editor.hidden = true;
    showMessage('Profile saved.');
    await refresh();
  };
  document.getElementById('import-file').onchange = async function (e) {
    var file = e.target.files && e.target.files[0];
    if (!file) return;
    var text = await file.text();
    await api.importProfile(text);
    showMessage('Profile imported.');
    await refresh();
  };
  tbody.onclick = async function (e) {
    var edit = e.target.getAttribute('data-edit');
    var del = e.target.getAttribute('data-delete');
    var exp = e.target.getAttribute('data-export');
    if (edit) {
      var profiles = await api.listProfiles();
      fillForm(profiles.find(function (p) { return p.id === edit; }));
    }
    if (del && confirm('Delete this profile?')) {
      await api.deleteProfile(del);
      showMessage('Profile deleted.');
      await refresh();
    }
    if (exp) {
      var data = await api.exportProfile(exp);
      var blob = new Blob([data.json], { type: 'application/json' });
      var a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = 'transcode-profile.json';
      a.click();
    }
  };

  refresh().catch(function (err) { showMessage(err.message, 'error'); });
})();
