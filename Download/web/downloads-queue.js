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
      if (!downloadsCache.length) {
        container.innerHTML = '<p class="meta">' + escapeHtml(emptyText) + '</p>';
        return;
      }

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

      container.querySelectorAll('[data-retry]').forEach(function (button) {
        button.addEventListener('click', function () {
          var id = button.getAttribute('data-retry');
          retryBusy = id;
          render(downloadsCache);
          api
            .retryDownload(id)
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
          api
            .cancelDownload(id)
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
      return api.listDownloads().then(render);
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
