(function (global) {
  function normalizeCompare(path) {
    return String(path || '').replace(/\\/g, '/').replace(/\/$/, '').toLowerCase();
  }

  function normalizePathForCompare(path) {
    return String(path || '').replace(/\\/g, '/').replace(/\/$/, '');
  }

  function toProgramDataRelative(programDataDirectory, absolutePath) {
    var programData = normalizePathForCompare(programDataDirectory);
    var selected = normalizePathForCompare(absolutePath);
    var selectedLower = selected.toLowerCase();
    var programDataLower = programData.toLowerCase();

    if (selectedLower === programDataLower)
      throw new Error('Choose a subfolder inside ProgramData.');

    var prefix = programDataLower + '/';
    if (!selectedLower.startsWith(prefix))
      throw new Error('Folder must be inside ' + programDataDirectory + '.');

    return selected.slice(programData.length + 1);
  }

  function browseFolders(path, options) {
    if (!global.CherryBoxPluginApi || typeof global.CherryBoxPluginApi.browseFolders !== 'function') {
      return Promise.reject(new Error('CherryBox plugin API is not loaded.'));
    }
    return global.CherryBoxPluginApi.browseFolders(path, options);
  }

  function mountFolderBrowser(container, options) {
    var value = options.value || '';
    var rootPath = options.rootPath || '';
    var includeFiles = Boolean(options.includeFiles);
    var allowFolderSelect = options.allowFolderSelect !== false;
    var selectLabel = options.selectLabel || 'Select this folder';
    var onChange = typeof options.onChange === 'function' ? options.onChange : function () {};

    var view = null;
    var loading = false;
    var error = '';

    container.classList.add('folder-browser');
    container.innerHTML = '';

    var toolbar = document.createElement('div');
    toolbar.className = 'folder-browser__toolbar';

    var upButton = document.createElement('button');
    upButton.type = 'button';
    upButton.className = 'secondary';
    upButton.textContent = 'Up';

    var rootButton = document.createElement('button');
    rootButton.type = 'button';
    rootButton.className = 'secondary';
    rootButton.textContent = rootPath ? 'ProgramData root' : 'Drives';

    var selectButton = document.createElement('button');
    selectButton.type = 'button';
    selectButton.textContent = selectLabel;

    toolbar.appendChild(upButton);
    toolbar.appendChild(rootButton);
    if (allowFolderSelect) toolbar.appendChild(selectButton);

    var pathEl = document.createElement('div');
    pathEl.className = 'folder-browser__path';

    var selectedEl = document.createElement('div');
    selectedEl.className = 'folder-browser__selected';

    var errorEl = document.createElement('div');
    errorEl.className = 'error';
    errorEl.hidden = true;

    var listWrap = document.createElement('div');
    listWrap.className = 'folder-browser__body';

    container.appendChild(toolbar);
    container.appendChild(pathEl);
    container.appendChild(selectedEl);
    container.appendChild(errorEl);
    container.appendChild(listWrap);

    function setError(message) {
      error = message || '';
      errorEl.textContent = error;
      errorEl.hidden = !error;
    }

    function renderSelected() {
      if (value) {
        selectedEl.innerHTML = 'Selected: <code></code>';
        selectedEl.querySelector('code').textContent = value;
        selectedEl.hidden = false;
      } else {
        selectedEl.hidden = true;
        selectedEl.textContent = '';
      }
    }

    function atRoot() {
      if (rootPath) {
        return view && view.currentPath && normalizeCompare(view.currentPath) === normalizeCompare(rootPath);
      }
      return !view || !view.currentPath;
    }

    function updateToolbar() {
      upButton.disabled = atRoot() || !view || !view.currentPath;
      selectButton.disabled = !allowFolderSelect || !view || !view.currentPath;
    }

    function renderList() {
      listWrap.innerHTML = '';
      if (loading) {
        var loadingEl = document.createElement('p');
        loadingEl.className = 'meta';
        loadingEl.textContent = 'Loading…';
        listWrap.appendChild(loadingEl);
        updateToolbar();
        return;
      }

      pathEl.textContent = (view && view.currentPath) || rootPath || 'Choose a drive or folder';
      renderSelected();
      updateToolbar();

      var list = document.createElement('ul');
      list.className = 'folder-browser__list';
      var entries = (view && view.entries) || [];

      entries.forEach(function (entry) {
        var item = document.createElement('li');
        var button = document.createElement('button');
        var isDirectory = entry.isDirectory !== false;
        button.type = 'button';
        button.className = 'folder-browser__item' + (isDirectory ? '' : ' folder-browser__item--file');

        var icon = document.createElement('span');
        icon.className = 'folder-browser__icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = isDirectory ? '📁' : '📄';

        button.appendChild(icon);
        button.appendChild(document.createTextNode(entry.name));
        button.addEventListener('click', function () {
          if (isDirectory) {
            load(entry.path);
            return;
          }
          value = entry.path;
          onChange(value);
          renderSelected();
        });

        item.appendChild(button);
        list.appendChild(item);
      });

      if (entries.length === 0 && view && view.currentPath) {
        var empty = document.createElement('li');
        empty.className = 'meta';
        empty.textContent = includeFiles ? 'No folders or files' : 'No subfolders';
        list.appendChild(empty);
      }

      listWrap.appendChild(list);
    }

    function load(path) {
      loading = true;
      setError('');
      renderList();
      browseFolders(path, { includeFiles: includeFiles })
        .then(function (result) {
          view = result;
        })
        .catch(function (err) {
          view = null;
          setError(err && err.message ? err.message : 'Could not browse folders');
        })
        .finally(function () {
          loading = false;
          renderList();
        });
    }

    upButton.addEventListener('click', function () {
      load(view && view.parentPath ? view.parentPath : undefined);
    });

    rootButton.addEventListener('click', function () {
      load(rootPath || undefined);
    });

    selectButton.addEventListener('click', function () {
      if (!view || !view.currentPath) return;
      value = view.currentPath;
      onChange(value);
      renderSelected();
    });

    load(value || rootPath || undefined);

    return {
      getValue: function () {
        return value;
      },
      setValue: function (nextValue) {
        value = nextValue || '';
        renderSelected();
        load(value || rootPath || undefined);
      },
    };
  }

  function mountProgramDataFolderBrowser(container, options) {
    var programDataDirectory = options.programDataDirectory || '';
    var relativeValue = options.relativeValue || '';
    var resolvedPath = options.resolvedPath || '';
    var onChange = typeof options.onChange === 'function' ? options.onChange : function () {};

    var wrap = document.createElement('div');
    wrap.className = 'folder-browser-wrap';

    var meta = document.createElement('p');
    meta.className = 'meta folder-browser-wrap__meta';

    var errorEl = document.createElement('div');
    errorEl.className = 'error';
    errorEl.hidden = true;

    container.innerHTML = '';
    container.appendChild(wrap);
    container.appendChild(errorEl);
    container.appendChild(meta);

    function setError(message) {
      errorEl.textContent = message || '';
      errorEl.hidden = !message;
    }

    function renderMeta(currentRelative, currentResolved) {
      meta.innerHTML = '<code></code> → <code></code>';
      var codes = meta.querySelectorAll('code');
      codes[0].textContent = currentRelative;
      codes[1].textContent = currentResolved;
    }

    renderMeta(relativeValue, resolvedPath);

    var browser = mountFolderBrowser(wrap, {
      rootPath: programDataDirectory,
      value: resolvedPath,
      selectLabel: 'Select this folder',
      onChange: function (selected) {
        try {
          var relative = toProgramDataRelative(programDataDirectory, selected);
          relativeValue = relative;
          resolvedPath = selected;
          setError('');
          renderMeta(relativeValue, resolvedPath);
          onChange(relativeValue, resolvedPath);
        } catch (err) {
          setError(err && err.message ? err.message : 'Invalid folder');
        }
      },
    });

    return {
      getRelativeValue: function () {
        return relativeValue;
      },
      setValues: function (nextRelative, nextResolved) {
        relativeValue = nextRelative || '';
        resolvedPath = nextResolved || '';
        renderMeta(relativeValue, resolvedPath);
        browser.setValue(resolvedPath);
      },
    };
  }

  global.CherryBoxFolderBrowser = {
    mount: mountFolderBrowser,
    mountProgramData: mountProgramDataFolderBrowser,
    toProgramDataRelative: toProgramDataRelative,
  };
})(window);
