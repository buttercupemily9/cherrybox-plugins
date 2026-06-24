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

  function showMessage(text, kind) {
    msg.textContent = text;
    msg.className = 'plugin-message ' + (kind || 'success');
    msg.hidden = false;
  }

  async function refresh() {
    var results = await Promise.all([api.workerStatus(), api.listJobs(100)]);
    var status = results[0];
    var jobs = results[1];
    document.getElementById('worker-status').textContent =
      'Worker ' + (status.backgroundWorkerEnabled ? 'enabled' : 'disabled') +
      ' · ' + (status.isProcessing ? 'processing' : 'idle') +
      ' · pending ' + status.pendingCount +
      ' · failed ' + status.failedCount;
    document.querySelector('#jobs-table tbody').innerHTML = jobs.map(function (j) {
      return '<tr><td>' + j.status + '</td><td>' + (j.mediaTitle || j.sourcePath || j.mediaItemId) +
        '</td><td>' + new Date(j.updatedAt).toLocaleString() + '</td><td>' + (j.errorMessage || '') + '</td></tr>';
    }).join('');
  }

  document.getElementById('start-worker').onclick = async function () {
    await api.startWorker();
    showMessage('Worker started.');
    await refresh();
  };
  document.getElementById('stop-worker').onclick = async function () {
    await api.stopWorker();
    showMessage('Worker stopped.');
    await refresh();
  };
  document.getElementById('enqueue-all').onclick = async function () {
    var result = await api.enqueue({ allVideos: true, retryFailed: false });
    showMessage(result.message);
    await refresh();
  };
  document.getElementById('retry-failed').onclick = async function () {
    var result = await api.enqueue({ allVideos: false, retryFailed: true });
    showMessage(result.message);
    await refresh();
  };

  refresh().catch(function (err) { showMessage(err.message, 'error'); });
  setInterval(function () { refresh().catch(function () {}); }, 5000);
})();
