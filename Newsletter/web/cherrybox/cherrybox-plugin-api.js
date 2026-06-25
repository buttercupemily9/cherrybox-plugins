(function (global) {
  var BUILTIN_SKINS = ['girl', 'boy', 'trans', 'pride'];
  var CORE_SKIN_LINK_ID = 'cherrybox-user-skin';
  var messageListenerAttached = false;

  function resolveAssetBase() {
    var script = document.currentScript;
    if (!script) {
      script = document.querySelector('script[src*="cherrybox-plugin-api.js"]');
    }
    if (script && script.src) {
      try {
        var url = new URL(script.src, window.location.href);
        return url.origin + url.pathname.replace(/[^/]+$/, '');
      } catch (_error) {
        // fall through
      }
    }
    return window.location.origin + '/skins';
  }

  function resolveAsset(fileName) {
    var base = resolveAssetBase();
    if (base.charAt(base.length - 1) !== '/') base += '/';
    return base + String(fileName || '').replace(/^\//, '');
  }

  var STOCK_CSS = resolveAsset('stock-plugin.css');
  var SHELL_JS = resolveAsset('cherrybox-plugin-shell.js');

  function normalizeSkin(value) {
    var skin = String(value || 'girl').trim().toLowerCase();
    if (/^[a-z0-9-]+$/.test(skin)) return skin;
    return 'girl';
  }

  function isBuiltInSkin(skin) {
    return BUILTIN_SKINS.indexOf(normalizeSkin(skin)) >= 0;
  }

  function applyCoreSkin(skin) {
    var normalized = normalizeSkin(skin);
    document.documentElement.dataset.skin = normalized;
    if (isBuiltInSkin(normalized)) {
      removeStylesheet(CORE_SKIN_LINK_ID);
      return;
    }
    ensureStylesheet(
      CORE_SKIN_LINK_ID,
      '/api/v1/skins/' + encodeURIComponent(normalized) + '/assets/theme.css'
    );
  }

  function readCoreSkin() {
    try {
      return normalizeSkin(
        document.documentElement.dataset.skin ||
          new URLSearchParams(window.location.search).get('skin')
      );
    } catch (_error) {
      return 'girl';
    }
  }

  function getAccessToken() {
    try {
      var queryToken = new URLSearchParams(window.location.search).get('access_token');
      if (queryToken) return queryToken;
    } catch (_error) {
      // ignore
    }
    try {
      return localStorage.getItem('cherrybox_token') || '';
    } catch (_error) {
      return '';
    }
  }

  function getPluginIdFromPath() {
    var match = window.location.pathname.match(/\/api\/v1\/plugins\/([^/]+)\/web\//i);
    return match ? decodeURIComponent(match[1]) : null;
  }

  function pluginWebUrl(pluginId, file) {
    var token = getAccessToken();
    var segments = String(file || 'index.html').replace(/^\//, '').split('/').map(function (part) {
      return encodeURIComponent(part);
    }).join('/');
    var base = '/api/v1/plugins/' + encodeURIComponent(pluginId) + '/web/' + segments;
    var skin = readCoreSkin();
    var params = new URLSearchParams();
    if (token) params.set('access_token', token);
    if (skin) params.set('skin', skin);
    var query = params.toString();
    return query ? base + '?' + query : base;
  }

  function apiRequest(path, options) {
    var token = getAccessToken();
    var headers = Object.assign({ Accept: 'application/json' }, (options && options.headers) || {});
    if (token) headers.Authorization = 'Bearer ' + token;
    return fetch(path, Object.assign({}, options || {}, { headers: headers })).then(function (response) {
      if (!response.ok) {
        return response.text().then(function (text) {
          throw new Error(text || response.statusText);
        });
      }
      if (response.status === 204) return null;
      return response.json();
    });
  }

  function browseFolders(path, options) {
    var params = new URLSearchParams();
    if (path) params.set('path', path);
    if (options && options.includeFiles) params.set('includeFiles', 'true');
    var query = params.toString();
    return apiRequest('/api/v1/system/browse' + (query ? '?' + query : ''));
  }

  function ensureStylesheet(id, href) {
    if (!href) return null;
    var existing = document.getElementById(id);
    if (existing) {
      if (existing.getAttribute('href') !== href) existing.setAttribute('href', href);
      return existing;
    }
    var link = document.createElement('link');
    link.id = id;
    link.rel = 'stylesheet';
    link.href = href;
    document.head.appendChild(link);
    return link;
  }

  function removeStylesheet(id) {
    var node = document.getElementById(id);
    if (node) node.remove();
  }

  function ensureScript(src) {
    if (document.querySelector('script[data-cherrybox-shell="true"]')) return;
    var script = document.createElement('script');
    script.src = src;
    script.defer = true;
    script.setAttribute('data-cherrybox-shell', 'true');
    document.head.appendChild(script);
  }

  function bootstrapTheme() {
    ensureStylesheet('cherrybox-stock-plugin-css', STOCK_CSS);
    ensureScript(SHELL_JS);
    applyCoreSkin(readCoreSkin());
  }

  function CherryBoxPlugin(pluginId, state) {
    this.pluginId = pluginId;
    this.state = state || { pluginSkinId: null, overrides: null, pluginSkins: [] };
    this._overrideLinkId = 'cherrybox-plugin-skin-overrides';
    this._pluginSkinLinkId = 'cherrybox-plugin-skin-local';
  }

  CherryBoxPlugin.prototype.getCoreSkin = function () {
    return readCoreSkin();
  };

  CherryBoxPlugin.prototype.getPluginSkinId = function () {
    return this.state.pluginSkinId || null;
  };

  CherryBoxPlugin.prototype.getPluginSkins = function () {
    return this.state.pluginSkins || [];
  };

  CherryBoxPlugin.prototype.applyTheme = function () {
    bootstrapTheme();

    var coreSkin = this.getCoreSkin();
    var overridePath = this.state.overrides && this.state.overrides[coreSkin];
    if (overridePath) {
      ensureStylesheet(this._overrideLinkId, pluginWebUrl(this.pluginId, overridePath));
    } else {
      removeStylesheet(this._overrideLinkId);
    }

    var pluginSkinId = this.getPluginSkinId();
    var pluginSkin = (this.state.pluginSkins || []).find(function (entry) {
      return entry.id === pluginSkinId;
    });
    if (pluginSkin && pluginSkin.stylesheet) {
      ensureStylesheet(this._pluginSkinLinkId, pluginWebUrl(this.pluginId, pluginSkin.stylesheet));
    } else {
      removeStylesheet(this._pluginSkinLinkId);
    }
  };

  CherryBoxPlugin.prototype.setPluginSkin = function (pluginSkinId) {
    var self = this;
    return apiRequest('/api/v1/plugins/' + encodeURIComponent(this.pluginId) + '/ui/skin', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ pluginSkinId: pluginSkinId || null }),
    }).then(function (result) {
      self.state.pluginSkinId = result.pluginSkinId || null;
      self.applyTheme();
      return self.state.pluginSkinId;
    });
  };

  CherryBoxPlugin.prototype.mountSkinPicker = function (container) {
    var skins = this.getPluginSkins();
    if (!skins.length) return null;

    var root = document.createElement('div');
    root.className = 'plugin-skin-picker';

    var label = document.createElement('label');
    label.textContent = 'Plugin skin';
    root.appendChild(label);

    var select = document.createElement('select');
    var stockOption = document.createElement('option');
    stockOption.value = '';
    stockOption.textContent = 'Stock';
    select.appendChild(stockOption);

    skins.forEach(function (entry) {
      var option = document.createElement('option');
      option.value = entry.id;
      option.textContent = entry.label;
      select.appendChild(option);
    });

    select.value = this.getPluginSkinId() || '';
    var self = this;
    select.addEventListener('change', function () {
      self.setPluginSkin(select.value || null).catch(function (error) {
        console.error('Failed to set plugin skin', error);
      });
    });

    root.appendChild(select);
    container.appendChild(root);
    return { root: root, select: select };
  };

  function attachSkinMessageListener(plugin) {
    if (messageListenerAttached) return;
    messageListenerAttached = true;
    window.addEventListener('message', function (event) {
      var data = event.data;
      if (!data || data.type !== 'cherrybox:skin') return;
      if (data.skin) applyCoreSkin(data.skin);
      if (plugin) plugin.applyTheme();
    });
  }

  function initCherryBoxPlugin(options) {
    bootstrapTheme();

    var pluginId = (options && options.pluginId) || getPluginIdFromPath();
    if (!pluginId) {
      return Promise.resolve(null);
    }

    var plugin = new CherryBoxPlugin(pluginId, {
      pluginSkinId: null,
      overrides: null,
      pluginSkins: [],
    });
    plugin.applyTheme();
    attachSkinMessageListener(plugin);

    return apiRequest('/api/v1/plugins/' + encodeURIComponent(pluginId) + '/ui/skin')
      .then(function (state) {
        plugin.state = state || plugin.state;
        plugin.applyTheme();
        if (options && options.mountSkinPicker) {
          plugin.mountSkinPicker(options.mountSkinPicker);
        }
        return plugin;
      })
      .catch(function (error) {
        console.warn('CherryBox plugin skin API unavailable; using stock theme.', error);
        if (options && options.mountSkinPicker) {
          plugin.mountSkinPicker(options.mountSkinPicker);
        }
        return plugin;
      });
  }

  bootstrapTheme();

  global.CherryBoxPluginApi = {
    initCherryBoxPlugin: initCherryBoxPlugin,
    pluginWebUrl: pluginWebUrl,
    normalizeSkin: normalizeSkin,
    bootstrapTheme: bootstrapTheme,
    browseFolders: browseFolders,
  };
})(window);
