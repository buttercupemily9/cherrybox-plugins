(function () {
  var api = window.StoryCoversApi;
  var messageEl = document.getElementById('message');

  function showMessage(text, isError) {
    messageEl.textContent = text;
    messageEl.className = isError ? 'error' : 'success';
  }

  function setBusy(busy) {
    document.querySelectorAll('#settingsForm input, #settingsForm button, #enqueueAllBtn, #startWorkerBtn, #stopWorkerBtn')
      .forEach(function (el) { el.disabled = busy; });
  }

  function fillForm(settings) {
    document.getElementById('backgroundWorkerEnabled').checked = !!settings.backgroundWorkerEnabled;
    document.getElementById('autoGenerateOnIndex').checked = settings.autoGenerateOnIndex !== false;
    document.getElementById('skipWhenCoverExists').checked = settings.skipWhenCoverExists !== false;
    document.getElementById('useChatPromptRefinement').checked = settings.useChatPromptRefinement !== false;
    document.getElementById('imageWidth').value = settings.imageWidth || 768;
    document.getElementById('imageHeight').value = settings.imageHeight || 1024;
    document.getElementById('contextCharLimit').value = settings.contextCharLimit || 2500;
  }

  function readForm() {
    return {
      backgroundWorkerEnabled: document.getElementById('backgroundWorkerEnabled').checked,
      autoGenerateOnIndex: document.getElementById('autoGenerateOnIndex').checked,
      skipWhenCoverExists: document.getElementById('skipWhenCoverExists').checked,
      useChatPromptRefinement: document.getElementById('useChatPromptRefinement').checked,
      imageWidth: Number(document.getElementById('imageWidth').value),
      imageHeight: Number(document.getElementById('imageHeight').value),
      contextCharLimit: Number(document.getElementById('contextCharLimit').value),
    };
  }

  function renderWorkerStatus(status) {
    var parts = [
      status.backgroundWorkerEnabled ? 'Worker enabled' : 'Worker paused',
      status.processing ? 'Processing' : 'Idle',
      status.pendingCount + ' pending',
      status.failedCount + ' failed',
    ];
    if (status.currentJob && status.currentJob.storyTitle) {
      parts.push('Current: ' + status.currentJob.storyTitle);
    }
    document.getElementById('workerStatus').textContent = parts.join(' · ');
  }

  function renderJobs(jobs) {
    var el = document.getElementById('jobList');
    if (!jobs || jobs.length === 0) {
      el.innerHTML = '<p class="meta">No cover jobs yet.</p>';
      return;
    }
    el.innerHTML = jobs.map(function (job) {
      var title = job.storyTitle || job.storyMediaItemId;
      var err = job.errorMessage ? ' — ' + job.errorMessage : '';
      return '<div class="job-row"><strong>' + title + '</strong> · ' + job.status + err + '</div>';
    }).join('');
  }

  async function refreshStatus() {
    var status = await api.getWorkerStatus();
    renderWorkerStatus(status);
    var jobs = await api.listJobs(30);
    renderJobs(jobs);
  }

  document.getElementById('settingsForm').addEventListener('submit', async function (e) {
    e.preventDefault();
    setBusy(true);
    showMessage('');
    try {
      var updated = await api.updateSettings(readForm());
      fillForm(updated);
      showMessage('Settings saved.');
      await refreshStatus();
    } catch (err) {
      showMessage(err.message || 'Failed to save settings', true);
    } finally {
      setBusy(false);
    }
  });

  document.getElementById('enqueueAllBtn').addEventListener('click', async function () {
    setBusy(true);
    showMessage('');
    try {
      var result = await api.enqueueAllMissing();
      showMessage(result.message);
      await refreshStatus();
    } catch (err) {
      showMessage(err.message || 'Failed to queue stories', true);
    } finally {
      setBusy(false);
    }
  });

  document.getElementById('startWorkerBtn').addEventListener('click', async function () {
    setBusy(true);
    try {
      await api.startWorker();
      await refreshStatus();
      showMessage('Worker started.');
    } catch (err) {
      showMessage(err.message || 'Failed to start worker', true);
    } finally {
      setBusy(false);
    }
  });

  document.getElementById('stopWorkerBtn').addEventListener('click', async function () {
    setBusy(true);
    try {
      await api.stopWorker();
      await refreshStatus();
      showMessage('Worker stopped.');
    } catch (err) {
      showMessage(err.message || 'Failed to stop worker', true);
    } finally {
      setBusy(false);
    }
  });

  api.getSettings()
    .then(function (settings) {
      fillForm(settings);
      return refreshStatus();
    })
    .catch(function (err) {
      showMessage(err.message || 'Failed to load settings', true);
    });
})();
