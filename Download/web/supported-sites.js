/** yt-dlp site keys CherryBox matches against download URLs (substring match). */
window.DownloadSupportedSites = [
  { id: 'pornhub', label: 'PornHub', testUrl: 'https://www.pornhub.com/' },
  { id: 'xvideos', label: 'XVideos', testUrl: 'https://www.xvideos.com/' },
  { id: 'xhamster', label: 'xHamster', testUrl: 'https://xhamster.com/' },
  { id: 'youporn', label: 'YouPorn', testUrl: 'https://www.youporn.com/' },
  { id: 'redtube', label: 'RedTube', testUrl: 'https://www.redtube.com/' },
  { id: 'tube8', label: 'Tube8', testUrl: 'https://www.tube8.com/' },
  { id: 'spankbang', label: 'SpankBang', testUrl: 'https://spankbang.com/' },
  { id: 'eporner', label: 'Eporner', testUrl: 'https://www.eporner.com/' },
  { id: 'xnxx', label: 'XNXX', testUrl: 'https://www.xnxx.com/' },
  { id: 'onlyfans', label: 'OnlyFans', testUrl: 'https://onlyfans.com/' },
  { id: 'fansly', label: 'Fansly', testUrl: 'https://fansly.com/' },
  { id: 'youtube', label: 'YouTube', testUrl: 'https://www.youtube.com/feed/subscriptions' },
  { id: 'vimeo', label: 'Vimeo', testUrl: 'https://vimeo.com/' },
  { id: 'twitch', label: 'Twitch', testUrl: 'https://www.twitch.tv/' },
  { id: 'twitter', label: 'Twitter / X', testUrl: 'https://x.com/' },
  { id: 'instagram', label: 'Instagram', testUrl: 'https://www.instagram.com/' },
  { id: 'reddit', label: 'Reddit', testUrl: 'https://www.reddit.com/' },
  { id: 'tiktok', label: 'TikTok', testUrl: 'https://www.tiktok.com/' },
  { id: 'facebook', label: 'Facebook', testUrl: 'https://www.facebook.com/' },
  { id: 'dailymotion', label: 'Dailymotion', testUrl: 'https://www.dailymotion.com/' },
  { id: 'bilibili', label: 'Bilibili', testUrl: 'https://www.bilibili.com/' },
  { id: 'niconico', label: 'Niconico', testUrl: 'https://www.nicovideo.jp/' },
  { id: 'rumble', label: 'Rumble', testUrl: 'https://rumble.com/' },
  { id: 'soundcloud', label: 'SoundCloud', testUrl: 'https://soundcloud.com/' },
  { id: 'bandcamp', label: 'Bandcamp', testUrl: 'https://bandcamp.com/' },
  { id: 'mixcloud', label: 'Mixcloud', testUrl: 'https://www.mixcloud.com/' },
  { id: 'archiveorg', label: 'Internet Archive', testUrl: 'https://archive.org/' },
  { id: 'vidme', label: 'Vidme (legacy)', testUrl: '' },
  { id: 'porntrex', label: 'PornTrex', testUrl: 'https://www.porntrex.com/' },
  { id: 'hentaihaven', label: 'Hentai Haven', testUrl: '' },
  { id: 'rule34video', label: 'Rule34Video', testUrl: '' },
  { id: 'manyvids', label: 'ManyVids', testUrl: 'https://www.manyvids.com/' },
  { id: 'chaturbate', label: 'Chaturbate', testUrl: 'https://chaturbate.com/' },
  { id: 'myfreecams', label: 'MyFreeCams', testUrl: 'https://www.myfreecams.com/' },
  { id: 'streamable', label: 'Streamable', testUrl: 'https://streamable.com/' },
  { id: 'gfycat', label: 'Gfycat', testUrl: '' },
  { id: 'imgur', label: 'Imgur', testUrl: 'https://imgur.com/' },
  { id: 'kick', label: 'Kick', testUrl: 'https://kick.com/' },
  { id: 'odysee', label: 'Odysee', testUrl: 'https://odysee.com/' },
  { id: 'peertube', label: 'PeerTube', testUrl: '' },
];

(function () {
  function compactAlphanumeric(value) {
    return String(value || '').toLowerCase().replace(/[^a-z0-9]/g, '');
  }

  function queryTokens(query) {
    return String(query || '')
      .toLowerCase()
      .trim()
      .split(/[\s\-_.]+/)
      .map(compactAlphanumeric)
      .filter(Boolean);
  }

  function isSubsequence(needle, haystack) {
    if (!needle) return true;
    var index = 0;
    for (var i = 0; i < haystack.length && index < needle.length; i++) {
      if (haystack[i] === needle[index]) index++;
    }
    return index === needle.length;
  }

  function levenshteinWithin(a, b, maxDistance) {
    if (a === b) return 0;
    if (!a.length) return b.length;
    if (!b.length) return a.length;
    if (Math.abs(a.length - b.length) > maxDistance) return maxDistance + 1;

    var previous = new Array(b.length + 1);
    var current = new Array(b.length + 1);
    for (var j = 0; j <= b.length; j++) previous[j] = j;

    for (var i = 1; i <= a.length; i++) {
      current[0] = i;
      var rowMin = current[0];
      for (var j = 1; j <= b.length; j++) {
        var cost = a[i - 1] === b[j - 1] ? 0 : 1;
        current[j] = Math.min(previous[j] + 1, current[j - 1] + 1, previous[j - 1] + cost);
        if (current[j] < rowMin) rowMin = current[j];
      }
      if (rowMin > maxDistance) return maxDistance + 1;
      var swap = previous;
      previous = current;
      current = swap;
    }

    return previous[b.length];
  }

  function scoreSite(site, query) {
    var trimmed = String(query || '').trim().toLowerCase();
    if (!trimmed) return -1;

    var compact = compactAlphanumeric(trimmed);
    var id = site.id;
    var label = site.label.toLowerCase();
    var combined = compactAlphanumeric(id + label);

    if (!compact) return -1;
    if (compact === id) return 1000;
    if (id.startsWith(compact)) return 950 - (id.length - compact.length);
    if (compact.startsWith(id)) return 920;
    if (id.includes(compact)) return 850;
    if (label.includes(trimmed)) return 820;

    var tokens = queryTokens(trimmed);
    if (tokens.length > 1) {
      var tokensMatch = tokens.every(function (token) {
        return id.includes(token) || label.includes(token) || isSubsequence(token, combined);
      });
      if (tokensMatch) return 780 - tokens.length;
    }

    if (compact.length >= 3 && isSubsequence(compact, id)) {
      return 720 - (id.length - compact.length);
    }

    if (compact.length >= 4) {
      var distance = levenshteinWithin(compact, id, 2);
      if (distance <= 2) return 680 - distance;
    }

    if (compact.length >= 3 && isSubsequence(compact, combined)) {
      return 640 - Math.min(combined.length - compact.length, 20);
    }

    return -1;
  }

  window.DownloadSiteSearch = {
    filter: function (query, sites, limit) {
      var list = sites || window.DownloadSupportedSites || [];
      var max = typeof limit === 'number' ? limit : 12;
      var trimmed = String(query || '').trim();
      if (!trimmed) return [];

      return list
        .map(function (site) {
          return { site: site, score: scoreSite(site, trimmed) };
        })
        .filter(function (entry) {
          return entry.score >= 0;
        })
        .sort(function (a, b) {
          return b.score - a.score || a.site.label.localeCompare(b.site.label);
        })
        .slice(0, max)
        .map(function (entry) {
          return entry.site;
        });
    },

    findExact: function (query, sites) {
      var list = sites || window.DownloadSupportedSites || [];
      var compact = compactAlphanumeric(query);
      if (!compact) return null;
      return (
        list.find(function (site) {
          return site.id === compact;
        }) || null
      );
    },

    isLikelyMatch: function (query, site) {
      return scoreSite(site, query) >= 680;
    },
  };
})();
