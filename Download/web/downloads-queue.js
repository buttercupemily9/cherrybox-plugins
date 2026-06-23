(function (global) {
  function mountDownloadsQueue(container, messageEl, api, ui, options) {
    if (!container || !api || !ui) return { refresh: function () {}, destroy: function () {} };

    var STATUS_LABEL = ui.STATUS_LABEL;
    var formatTime = ui.formatTime;
    var escapeHtml = ui.escapeHtml;
    var showMessage = ui.showMessage;
    var shortUrl = ui.shortUrl || function (url) { return url; };
    var emptyText = (options && options.emptyText) || 'No downloads queued yet.';
    var pollMs = (options && options.pollMs) || 4000;
    var pollMsEnriching = (options && options.pollMsEnriching) || 1500;

    var retryBusy = null;
    var cancelBusy = null;
    var deleteBusy = null;
    var pollTimer = null;
    var downloadsCache = [];

    function canRetry(status) {
      return status === 'Failed' || status === 'Cancelled' || status === 'Blocked';
    }

    function canCancel(status) {
      return status === 'Pending' || status === 'Running';
    }

    var downloadCoverUrl = (options && options.downloadCoverUrl) || (ui.downloadCoverUrl || function () { return null; });

    function coverUrl(job) {
      return job.hasCover ? downloadCoverUrl(job.id) : null;
    }

    function needsEnrichment(job) {
      return (job.status === 'Pending' || job.status === 'Running') && (!job.title || !job.hasCover);
    }

    function renderProgress(job) {
      if (job.status !== 'Running' || job.progressPercent == null)
        return '';

      var percent = Math.max(0, Math.min(100, Math.round(job.progressPercent)));
      return (
        '<div class="download-progress" aria-label="Download progress">' +
        '<div class="download-progress__bar" style="width:' +
        percent +
        '%"></div>' +
        '</div>' +
        '<p class="meta download-progress__label">' +
        percent +
        '%</p>'
      );
    }

    function renderCover(job) {
      var cover = coverUrl(job);
      if (cover) {
        return (
          '<img class="download-row__cover" src="' +
          escapeHtml(cover) +
          '" alt="" loading="lazy" />'
        );
      }

      if (needsEnrichment(job)) {
        return '<div class="download-row__cover download-row__cover--loading" aria-hidden="true"></div>';
      }

      return '<div class="download-row__cover download-row__cover--placeholder" aria-hidden="true"></div>';
    }

    function renderTitle(job) {
      if (job.title) {
        return '<h3 class="download-row__title">' + escapeHtml(job.title) + '</h3>';
      }

      if (needsEnrichment(job)) {
        return '<h3 class="download-row__title download-row__title--pending">Fetching video info…</h3>';
      }

      return '<h3 class="download-row__title download-row__title--muted">' + escapeHtml(shortUrl(job.url)) + '</h3>';
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
            var siteName = d.siteName ? escapeHtml(d.siteName) : 'Unknown';
            return (
              '<li class="download-row">' +
              renderCover(d) +
              '<div class="download-row__info">' +
              '<div class="download-row__meta-line">' +
              '<span class="status">' +
              escapeHtml(STATUS_LABEL[d.status] ?? d.status) +
              '</span>' +
              '<span class="download-row__site">' +
              siteName +
              '</span>' +
              (showUser
                ? '<span class="meta download-row__user">' +
                  escapeHtml(d.createdByUsername || 'Unknown user') +
                  '</span>'
                : '') +
              '</div>' +
              renderTitle(d) +
              '<a class="download-row__url" href="' +
              escapeHtml(d.url) +
              '" target="_blank" rel="noreferrer" title="' +
              escapeHtml(d.url) +
              '">' +
              escapeHtml(shortUrl(d.url)) +
              '</a>' +
              renderProgress(d) +
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
              (d.retryCount > 0
                ? '<p class="meta">Retry attempt ' + d.retryCount + ' of 10</p>'
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
              '<button type="button" class="secondary" data-delete="' +
              d.id +
              '" ' +
              (deleteBusy ? 'disabled' : '') +
              '>' +
              (deleteBusy === d.id ? 'Removing…' : 'Remove') +
              '</button>' +
              '</div>' +
              '</li>'
            );
          })
          .join('') +
        '</ul>';

      var retryDownload = options.retryDownload || api.retryDownload.bind(api);
      var cancelDownload = options.cancelDownload || api.cancelDownload.bind(api);
      var deleteDownload = options.deleteDownload || api.deleteDownload.bind(api);

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

      container.querySelectorAll('[data-delete]').forEach(function (button) {
        button.addEventListener('click', function () {
          var id = button.getAttribute('data-delete');
          deleteBusy = id;
          render(downloadsCache);
          deleteDownload(id)
            .then(function () {
              if (messageEl) showMessage(messageEl, 'Removed from queue');
              return refresh();
            })
            .catch(function (err) {
              if (messageEl) showMessage(messageEl, err.message, true);
            })
            .finally(function () {
              deleteBusy = null;
            });
        });
      });
    }

    function schedulePoll() {
      if (pollTimer !== null)
        global.clearInterval(pollTimer);

      var interval = downloadsCache.some(needsEnrichment) ? pollMsEnriching : pollMs;
      pollTimer = global.setInterval(function () {
        refresh().catch(function () {});
      }, interval);
    }

    function refresh() {
      var listDownloads = options.listDownloads || api.listDownloads.bind(api);
      return listDownloads().then(function (downloads) {
        render(downloads);
        schedulePoll();
      });
    }

    schedulePoll();
    refresh().catch(function () {});

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
