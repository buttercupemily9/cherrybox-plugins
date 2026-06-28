(function () {
  var BUILTIN_SKINS = ['girl', 'boy', 'trans', 'pride'];
  var CORE_SKIN_LINK_ID = 'cherrybox-user-skin';

  function normalizeSkin(value) {
    if (!value) return 'girl';
    var skin = String(value).trim().toLowerCase();
    if (/^[a-z0-9-]+$/.test(skin)) return skin;
    return 'girl';
  }

  function isBuiltInSkin(skin) {
    return BUILTIN_SKINS.indexOf(normalizeSkin(skin)) >= 0;
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

  function readSkinFromQuery() {
    try {
      var params = new URLSearchParams(window.location.search);
      return normalizeSkin(params.get('skin'));
    } catch (_error) {
      return 'girl';
    }
  }

  applyCoreSkin(readSkinFromQuery());

  window.addEventListener('message', function (event) {
    var data = event.data;
    if (!data || data.type !== 'cherrybox:skin') return;
    applyCoreSkin(data.skin);
  });
})();
