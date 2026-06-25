(function () {
  var api = window.StoryTtsApi;
  var messageEl = document.getElementById('message');
  var pollTimer;

  function showMessage(text, kind) {
    messageEl.hidden = false;
    messageEl.textContent = text;
    messageEl.className = 'plugin-message ' + (kind || '');
  }

  function formatDate(value) {
    if (!value) return '';
    try { return new Date(value).toLocaleString(); } catch { return value; }
  }

  function renderWorker(status) {
    var current = status.currentJob
      ? ' — processing ' + (status.currentJob.storyTitle || status.currentJob.storyMediaItemId)
      : '';
    document.getElementById('worker-status').textContent =
      'Worker ' + (status.backgroundWorkerEnabled ? 'enabled' : 'stopped') +
      ' · pending ' + status.pendingCount +
      ' · failed ' + status.failedCount +
      current;
  }

  function renderJobs(jobs) {
    var tbody = document.querySelector('#jobs-table tbody');
    tbody.innerHTML = jobs.map(function (job) {
      var progress = job.chunksTotal > 0
        ? job.chunksCompleted + ' / ' + job.chunksTotal
        : '—';
      return '<tr>' +
        '<td>' + job.status + '</td>' +
        '<td>' + (job.storyTitle || job.storyMediaItemId) + '</td>' +
        '<td>' + progress + '</td>' +
        '<td>' + formatDate(job.updatedAt) + '</td>' +
        '<td>' + (job.errorMessage || '') + '</td>' +
        '</tr>';
    }).join('');
  }

  async function refresh() {
    var status = await api.workerStatus();
    var jobs = await api.listJobs(100);
    renderWorker(status);
    renderJobs(jobs);
  }

  async function loadStories() {
    var stories = await api.listStories();
    var select = document.getElementById('storySelect');
    select.innerHTML = stories.map(function (story) {
      var label = story.title || story.fileName || story.id;
      if (story.author) label += ' — ' + story.author;
      return '<option value="' + story.id + '">' + label + '</option>';
    }).join('');
  }

  document.getElementById('start-worker').onclick = async function () {
    try {
      await api.startWorker();
      showMessage('Worker started.', 'success');
      await refresh();
    } catch (err) {
      showMessage(err.message || 'Failed to start worker', 'error');
    }
  };

  document.getElementById('stop-worker').onclick = async function () {
    try {
      await api.stopWorker();
      showMessage('Worker stopped.', 'success');
      await refresh();
    } catch (err) {
      showMessage(err.message || 'Failed to stop worker', 'error');
    }
  };

  document.getElementById('enqueue-all').onclick = async function () {
    try {
      var result = await api.enqueue({ allUnlinkedStories: true });
      showMessage(result.message, result.enqueued > 0 ? 'success' : 'error');
      await refresh();
    } catch (err) {
      showMessage(err.message || 'Failed to queue stories', 'error');
    }
  };

  document.getElementById('enqueue-one').onclick = async function () {
    var storyId = document.getElementById('storySelect').value;
    if (!storyId) return;
    try {
      var result = await api.enqueue({ storyMediaItemId: storyId });
      showMessage(result.message, result.enqueued > 0 ? 'success' : 'error');
      await refresh();
    } catch (err) {
      showMessage(err.message || 'Failed to queue story', 'error');
    }
  };

  Promise.all([loadStories(), refresh()])
    .then(function () {
      pollTimer = window.setInterval(function () {
        refresh().catch(function () { /* ignore transient poll errors */ });
      }, 3000);
    })
    .catch(function (err) {
      showMessage(err.message || 'Failed to load page', 'error');
    });

  window.addEventListener('beforeunload', function () {
    if (pollTimer) window.clearInterval(pollTimer);
  });
})();
