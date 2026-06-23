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

  var profiles = [];
  var libraries = [];
  var overrides = [];

  function showMessage(text, kind) {
    msg.textContent = text;
    msg.className = 'plugin-message ' + (kind || 'success');
    msg.hidden = false;
  }

  function profileOptions(selected) {
    return ['<option value="">— none —</option>'].concat(profiles.map(function (p) {
      return '<option value="' + p.id + '"' + (p.id === selected ? ' selected' : '') + '>' + p.name + '</option>';
    })).join('');
  }

  function renderOverrides() {
    document.getElementById('overrides').innerHTML = overrides.map(function (o, i) {
      return '<div class="form-row"><select data-i="' + i + '" class="override-library">' +
        libraries.map(function (l) {
          return '<option value="' + l.id + '"' + (l.id === o.libraryFolderId ? ' selected' : '') + '>' + l.displayName + '</option>';
        }).join('') +
        '</select><select data-i="' + i + '" class="override-profile">' + profileOptions(o.profileId) +
        '</select><label><input type="checkbox" data-i="' + i + '" class="override-enabled"' + (o.enabled ? ' checked' : '') +
        '/> Enabled</label><button type="button" data-remove="' + i + '">Remove</button></div>';
    }).join('');
  }

  async function load() {
    var results = await Promise.all([api.listProfiles(), api.listLibraries()]);
    profiles = results[0];
    libraries = results[1];
    var assignments = await api.getAssignments();
    document.getElementById('globalEnabled').checked = assignments.globalEnabled;
    document.getElementById('autoEnqueueOnScan').checked = assignments.autoEnqueueOnScan;
    document.getElementById('backgroundWorkerEnabled').checked = assignments.backgroundWorkerEnabled;
    document.getElementById('globalDefaultProfileId').innerHTML = profileOptions(assignments.globalDefaultProfileId);
    overrides = (assignments.libraryOverrides || []).slice();
    renderOverrides();
  }

  document.getElementById('add-override').onclick = function () {
    if (!libraries.length || !profiles.length) return;
    overrides.push({ libraryFolderId: libraries[0].id, profileId: profiles[0].id, enabled: true });
    renderOverrides();
  };
  document.getElementById('overrides').onclick = function (e) {
    var remove = e.target.getAttribute('data-remove');
    if (remove != null) {
      overrides.splice(Number(remove), 1);
      renderOverrides();
    }
  };
  document.getElementById('assignments-form').onsubmit = async function (e) {
    e.preventDefault();
    document.querySelectorAll('.override-library').forEach(function (el) {
      var i = Number(el.getAttribute('data-i'));
      overrides[i].libraryFolderId = el.value;
    });
    document.querySelectorAll('.override-profile').forEach(function (el) {
      var i = Number(el.getAttribute('data-i'));
      overrides[i].profileId = el.value;
    });
    document.querySelectorAll('.override-enabled').forEach(function (el) {
      var i = Number(el.getAttribute('data-i'));
      overrides[i].enabled = el.checked;
    });
    await api.updateAssignments({
      globalDefaultProfileId: document.getElementById('globalDefaultProfileId').value || null,
      globalEnabled: document.getElementById('globalEnabled').checked,
      backgroundWorkerEnabled: document.getElementById('backgroundWorkerEnabled').checked,
      autoEnqueueOnScan: document.getElementById('autoEnqueueOnScan').checked,
      libraryOverrides: overrides,
      profileLibraryBindings: [],
    });
    showMessage('Assignments saved.');
  };

  load().catch(function (err) { showMessage(err.message, 'error'); });
})();
