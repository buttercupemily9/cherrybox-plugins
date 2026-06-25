namespace CherryBox.Download.Plugin;

internal static class DownloadSupportedSites
{
    public static IReadOnlyList<DownloadSupportedSiteInfo> All { get; } =
    [
        new("pornhub", "PornHub", "https://www.pornhub.com/"),
        new("xvideos", "XVideos", "https://www.xvideos.com/"),
        new("xhamster", "xHamster", "https://xhamster.com/"),
        new("youporn", "YouPorn", "https://www.youporn.com/"),
        new("redtube", "RedTube", "https://www.redtube.com/"),
        new("tube8", "Tube8", "https://www.tube8.com/"),
        new("spankbang", "SpankBang", "https://spankbang.com/"),
        new("eporner", "Eporner", "https://www.eporner.com/"),
        new("xnxx", "XNXX", "https://www.xnxx.com/"),
        new("onlyfans", "OnlyFans", "https://onlyfans.com/"),
        new("fansly", "Fansly", "https://fansly.com/"),
        new("youtube", "YouTube", "https://www.youtube.com/feed/subscriptions"),
        new("vimeo", "Vimeo", "https://vimeo.com/"),
        new("twitch", "Twitch", "https://www.twitch.tv/"),
        new("twitter", "Twitter / X", "https://x.com/"),
        new("instagram", "Instagram", "https://www.instagram.com/"),
        new("reddit", "Reddit", "https://www.reddit.com/"),
        new("tiktok", "TikTok", "https://www.tiktok.com/"),
        new("facebook", "Facebook", "https://www.facebook.com/"),
        new("dailymotion", "Dailymotion", "https://www.dailymotion.com/"),
        new("bilibili", "Bilibili", "https://www.bilibili.com/"),
        new("niconico", "Niconico", "https://www.nicovideo.jp/"),
        new("rumble", "Rumble", "https://rumble.com/"),
        new("soundcloud", "SoundCloud", "https://soundcloud.com/"),
        new("bandcamp", "Bandcamp", "https://bandcamp.com/"),
        new("mixcloud", "Mixcloud", "https://www.mixcloud.com/"),
        new("archiveorg", "Internet Archive", "https://archive.org/"),
        new("vidme", "Vidme (legacy)", null),
        new("porntrex", "PornTrex", "https://www.porntrex.com/"),
        new("hentaihaven", "Hentai Haven", null),
        new("rule34video", "Rule34Video", null),
        new("manyvids", "ManyVids", "https://www.manyvids.com/"),
        new("chaturbate", "Chaturbate", "https://chaturbate.com/"),
        new("myfreecams", "MyFreeCams", "https://www.myfreecams.com/"),
        new("streamable", "Streamable", "https://streamable.com/"),
        new("gfycat", "Gfycat", null),
        new("imgur", "Imgur", "https://imgur.com/"),
        new("kick", "Kick", "https://kick.com/"),
        new("odysee", "Odysee", "https://odysee.com/"),
        new("peertube", "PeerTube", null),
    ];
}

internal sealed record DownloadSupportedSiteInfo(string SiteKey, string Label, string? TestUrl);
