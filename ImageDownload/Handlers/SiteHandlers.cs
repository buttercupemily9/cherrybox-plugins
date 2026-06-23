using CherryBox.Plugins.Abstractions;
using HtmlAgilityPack;
using ImageDownload.Plugin.Common;

namespace ImageDownload.Plugin.Handlers;

public abstract class HtmlImageDownloadHandlerBase : IImageDownloadHandler
{
    public abstract string SiteId { get; }
    public abstract string SiteName { get; }
    protected abstract bool Matches(Uri url);
    protected abstract string[] ImageSelectors { get; }

    public bool CanHandle(Uri url) => Matches(url);

    public async Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken = default)
    {
        var pageUri = SiteHostMatcher.NormalizeUrl(url);
        var html = await ImageFetchHelper.GetStringAsync(pageUri.ToString(), cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var imageUrls = HtmlGalleryScraper.CollectImageUrls(doc, pageUri, ImageSelectors);
        if (imageUrls.Count == 0)
            return null;

        var title = HtmlGalleryScraper.PageTitle(doc) ?? SiteName;
        var description = HtmlGalleryScraper.MetaContent(doc, "og:description")
            ?? HtmlGalleryScraper.MetaContent(doc, "description");

        var items = imageUrls.Select((u, i) => new ImageDownloadItem(u, $"image-{i + 1}{Path.GetExtension(new Uri(u).AbsolutePath)}")).ToList();
        return new ImageDownloadPlan(
            title,
            pageUri.ToString(),
            SiteName,
            ImageDownloadExecutionMode.DirectHttp,
            items,
            description,
            Tags: null);
    }
}

public sealed class Rule34XxxHandler : IImageDownloadHandler
{
    public string SiteId => "rule34-xxx";
    public string SiteName => "Rule34";

    public bool CanHandle(Uri url) =>
        SiteHostMatcher.HostEndsWith(url, "rule34.xxx") &&
        !url.Host.Contains("paheal", StringComparison.OrdinalIgnoreCase);

    public Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken = default) =>
        Rule34ApiHelper.TryBuildPostPlanAsync(url, SiteName, cancellationToken);
}

public sealed class Rule34PahealHandler : HtmlImageDownloadHandlerBase
{
    public override string SiteId => "rule34-paheal";
    public override string SiteName => "Rule34 Paheal";
    protected override bool Matches(Uri url) => SiteHostMatcher.HostEndsWith(url, "rule34.paheal.net");
    protected override string[] ImageSelectors => ["//img[@id='pic']", "//a[@id='pic']", "//img[contains(@class,'shm-image')]"];
}

public sealed class Rule34WorldHandler : HtmlImageDownloadHandlerBase
{
    public override string SiteId => "rule34-world";
    public override string SiteName => "Rule34 World";
    protected override bool Matches(Uri url) => SiteHostMatcher.HostEndsWith(url, "rule34.world");
    protected override string[] ImageSelectors =>
        ["//img[contains(@class,'post-image')]", "//a[contains(@href,'/images/')]", "//img[@data-src]"];
}

public sealed class ImagefapHandler : IImageDownloadHandler
{
    public string SiteId => "imagefap";
    public string SiteName => "ImageFap";

    public bool CanHandle(Uri url) => SiteHostMatcher.HostEndsWith(url, "imagefap.com");

    public async Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken = default)
    {
        var pageUri = SiteHostMatcher.NormalizeUrl(url);
        var html = await ImageFetchHelper.GetStringAsync(pageUri.ToString(), cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var urls = HtmlGalleryScraper.CollectImageUrls(
            doc,
            pageUri,
            "//a[contains(@href,'/photo/')]",
            "//img[contains(@class,'photo')]",
            "//img[@id='photo']");

        if (urls.Count == 0)
            return YtDlpPlanHelper.BuildYtDlpPlan(url, SiteName);

        return new ImageDownloadPlan(
            HtmlGalleryScraper.PageTitle(doc) ?? SiteName,
            url,
            SiteName,
            ImageDownloadExecutionMode.DirectHttp,
            urls.Select(u => new ImageDownloadItem(u)).ToList());
    }
}

public sealed class SexComHandler : HtmlImageDownloadHandlerBase
{
    public override string SiteId => "sex-com";
    public override string SiteName => "Sex.com";
    protected override bool Matches(Uri url) => SiteHostMatcher.HostEndsWith(url, "sex.com");
    protected override string[] ImageSelectors =>
        ["//img[contains(@class,'image')]", "//img[@data-src]", "//meta[@property='og:image']"];
}

public sealed class PornhubAlbumsHandler : IImageDownloadHandler
{
    public string SiteId => "pornhub-albums";
    public string SiteName => "Pornhub";

    public bool CanHandle(Uri url) =>
        SiteHostMatcher.HostEndsWith(url, "pornhub.com") &&
        url.AbsolutePath.Contains("/album", StringComparison.OrdinalIgnoreCase);

    public Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken = default) =>
        Task.FromResult<ImageDownloadPlan?>(YtDlpPlanHelper.BuildYtDlpPlan(url, SiteName));
}

public sealed class StufferDbHandler : HtmlImageDownloadHandlerBase
{
    public override string SiteId => "stufferdb";
    public override string SiteName => "StufferDB";
    protected override bool Matches(Uri url) => SiteHostMatcher.HostEndsWith(url, "stufferdb.com");
    protected override string[] ImageSelectors =>
        ["//div[contains(@class,'gallery')]//a", "//img[contains(@src,'/images/')]", "//a[contains(@href,'/view/')]"];
}

public sealed class MotherlessImagesHandler : IImageDownloadHandler
{
    public string SiteId => "motherless";
    public string SiteName => "Motherless";

    public bool CanHandle(Uri url) =>
        SiteHostMatcher.HostEndsWith(url, "motherless.com") &&
        (url.AbsolutePath.Contains("/images", StringComparison.OrdinalIgnoreCase) ||
         url.AbsolutePath.Contains("/gallery", StringComparison.OrdinalIgnoreCase));

    public Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken = default) =>
        Task.FromResult<ImageDownloadPlan?>(YtDlpPlanHelper.BuildYtDlpPlan(url, SiteName));
}

public sealed class XhamsterPhotosHandler : IImageDownloadHandler
{
    public string SiteId => "xhamster-photos";
    public string SiteName => "xHamster";

    public bool CanHandle(Uri url) =>
        SiteHostMatcher.HostEndsWith(url, "xhamster.com") &&
        url.AbsolutePath.Contains("/photos", StringComparison.OrdinalIgnoreCase);

    public Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken = default) =>
        Task.FromResult<ImageDownloadPlan?>(YtDlpPlanHelper.BuildYtDlpPlan(url, SiteName));
}

public sealed class NsfwXxxHandler : HtmlImageDownloadHandlerBase
{
    public override string SiteId => "nsfw-xxx";
    public override string SiteName => "NSFW.xxx";
    protected override bool Matches(Uri url) => SiteHostMatcher.HostEndsWith(url, "nsfw.xxx");
    protected override string[] ImageSelectors =>
        ["//img[contains(@class,'image')]", "//a[contains(@href,'/photo/')]", "//img[@data-src]"];
}

public sealed class PictoaHandler : HtmlImageDownloadHandlerBase
{
    public override string SiteId => "pictoa";
    public override string SiteName => "Pictoa";
    protected override bool Matches(Uri url) => SiteHostMatcher.HostEndsWith(url, "pictoa.com");
    protected override string[] ImageSelectors =>
        ["//div[contains(@class,'gallery')]//img", "//a[contains(@href,'/galleries/')]", "//img[@data-src]"];
}

public sealed class JjgirlsHandler : HtmlImageDownloadHandlerBase
{
    public override string SiteId => "jjgirls";
    public override string SiteName => "JJGirls";
    protected override bool Matches(Uri url) => SiteHostMatcher.HostEndsWith(url, "jjgirls.com");
    protected override string[] ImageSelectors =>
        ["//div[contains(@class,'gallery')]//img", "//img[contains(@src,'/photos/')]", "//a[contains(@href,'/girls/')]"];
}

public sealed class PichunterHandler : HtmlImageDownloadHandlerBase
{
    public override string SiteId => "pichunter";
    public override string SiteName => "PicHunter";
    protected override bool Matches(Uri url) => SiteHostMatcher.HostEndsWith(url, "pichunter.com");
    protected override string[] ImageSelectors =>
        ["//div[contains(@class,'gallery')]//img", "//img[contains(@class,'thumb')]", "//a[contains(@href,'/gallery/')]"];
}

public sealed class SxypixHandler : HtmlImageDownloadHandlerBase
{
    public override string SiteId => "sxypix";
    public override string SiteName => "Sxypix";
    protected override bool Matches(Uri url) => SiteHostMatcher.HostEndsWith(url, "sxypix.com");
    protected override string[] ImageSelectors =>
        ["//div[contains(@class,'gallery')]//img", "//img[contains(@src,'/images/')]", "//a[contains(@href,'/pic/')]"];
}
