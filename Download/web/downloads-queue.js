(function (global) {
  function mountDownloadsQueue(container, messageEl, api, ui, options) {
    if (!container || !api || !ui) return { refresh: function () {}, destroy: function () {} };

    var STATUS_LABEL = ui.STATUS_LABEL;
    var formatTime = ui.formatTime;
    var escapeHtml = ui.escapeHtml;
    var showMessage = ui.showMessage;
    var emptyText = (options && options.emptyText) || 'No downloads queued yet.';
    var pollMs = (options && options.pollMs) || 4000;

    var retryBusy = null;
    var cancelBusy = null;
    var pollTimer = null;
    var downloadsCache = [];

    function canRetry(status) {
      return status === 'Failed' || status === 'Cancelled' || status === 'Blocked';
    }

    function canCancel(status) {
      return status === 'Pending' || status === 'Running';
    }

    function render(downloads) {
      downloadsCache = downloads || [];
      var activeCount = downloadsCache.filter(function (d) {
        return d.status === 'Pending' || d.status === 'Running';
      }).length;
      if (typeof options.onActiveCountChange === 'function')
        options.onActiveCountChange(activeCount);

      if (!downloadsCache.length) {
        container.innerHTML = '<p class="meta">' + escapeHtml(emptyText) + '</p>';
        return;
      }

      var showUser = Boolean(options.showUser);

      container.innerHTML =
        '<ul class="download-list">' +
        downloadsCache
          .map(function (d) {
            return (
              '<li class="download-row">' +
              '<div class="download-row__info">' +
              '<div class="download-row__header">' +
              '<span class="status">' +
              escapeHtml(STATUS_LABEL[d.status] ?? d.status) +
              '</span>' +
              (showUser
                ? '<span class="meta download-row__user">' +
                  escapeHtml(d.createdByUsername || 'Unknown user') +
                  '</span>'
                : '') +
              '<a class="download-row__url" href="' +
              escapeHtml(d.url) +
              '" target="_blank" rel="noreferrer">' +
              escapeHtml(d.url) +
              '</a>' +
              '</div>' +
              (d.outputPath ? '<p class="meta">Saved to ' + escapeHtml(d.outputPath) + '</p>' : '') +
              (d.blockReason || d.errorMessage
                ? '<p class="error">' + escapeHtml(d.blockReason ?? d.errorMessage) + '</p>'
                : '') +
              (d.existingMediaTitle
                ? '<p class="meta">Already in library: ' + escapeHtml(d.existingMediaTitle) + '</p>'
                : '') +
              (d.retryAfterAt && d.status === 'Failed'
                ? '<p class="meta">Auto-retry after ' + formatTime(d.retryAfterAt) + '</p>'
                : '') +
              (showUser && d.createdAt
                ? '<p class="meta">Queued ' + formatTime(d.createdAt) + '</p>'
                : '') +
              '</div>' +
              '<div class="download-row__actions">' +
              (canRetry(d.status)
                ? '<button type="button" class="secondary" data-retry="' +
                  d.id +
                  '" ' +
                  (retryBusy ? 'disabled' : '') +
                  '>' +
                  (retryBusy === d.id ? 'Retrying…' : 'Retry') +
                  '</button>'
                : '') +
              (canCancel(d.status)
                ? '<button type="button" class="secondary" data-cancel="' +
                  d.id +
                  '" ' +
                  (cancelBusy ? 'disabled' : '') +
                  '>' +
                  (cancelBusy === d.id ? 'Cancelling…' : 'Cancel') +
                  '</button>'
                : '') +
              '</div>' +
              '</li>'
            );
          })
          .join('') +
        '</ul>';

      var retryDownload = options.retryDownload || api.retryDownload.bind(api);
      var cancelDownload = options.cancelDownload || api.cancelDownload.bind(api);

      container.querySelectorAll('[data-retry]').forEach(function (button) {
        button.addEventListener('click', function () {
          var id = button.getAttribute('data-retry');
          retryBusy = id;
          render(downloadsCache);
          retryDownload(id)
            .then(function () {
              if (messageEl) showMessage(messageEl, 'Download re-queued');
              return refresh();
            })
            .catch(function (err) {
              if (messageEl) showMessage(messageEl, err.message, true);
            })
            .finally(function () {
              retryBusy = null;
            });
        });
      });

      container.querySelectorAll('[data-cancel]').forEach(function (button) {
        button.addEventListener('click', function () {
          var id = button.getAttribute('data-cancel');
          cancelBusy = id;
          render(downloadsCache);
          cancelDownload(id)
            .then(function () {
              if (messageEl) showMessage(messageEl, 'Download cancelled');
              return refresh();
            })
            .catch(function (err) {
              if (messageEl) showMessage(messageEl, err.message, true);
            })
            .finally(function () {
              cancelBusy = null;
            });
        });
      });
    }

    function refresh() {
      var listDownloads = options.listDownloads || api.listDownloads.bind(api);
      return listDownloads().then(render);
    }

    pollTimer = global.setInterval(function () {
      refresh().catch(function () {});
    }, pollMs);

    return {
      refresh: refresh,
      destroy: function () {
        if (pollTimer !== null) {
          global.clearInterval(pollTimer);
          pollTimer = null;
        }
      },
    };
  }

  global.DownloadQueuePanel = { mount: mountDownloadsQueue };
})(window);
