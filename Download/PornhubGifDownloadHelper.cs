using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.Download.Plugin;

internal static class PornhubGifDownloadHelper
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex GifIdRegex = new(@"/gif/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ContentUrlRegex = new(@"""contentUrl""\s*:\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex NameRegex = new(@"""name""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Compiled);

    internal static bool IsGifUrl(Uri url) =>
        url.Host.Contains("pornhub.com", StringComparison.OrdinalIgnoreCase) &&
        GifIdRegex.IsMatch(url.AbsolutePath);

    internal static async Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(NormalizeUrl(url), UriKind.Absolute, out var pageUri))
            return null;

        if (!GifIdRegex.IsMatch(pageUri.AbsolutePath))
            return null;

        var gifId = GifIdRegex.Match(pageUri.AbsolutePath).Groups[1].Value;
        var html = await FetchPornhubPageAsync(pageUri.ToString(), cancellationToken);

        var contentUrl = UnescapeJson(ContentUrlRegex.Match(html).Groups[1].Value);
        if (string.IsNullOrWhiteSpace(contentUrl))
            return null;

        if (!TryNormalizeMediaUrl(contentUrl, pageUri, out var absoluteUrl))
            return null;

        var title = UnescapeJson(NameRegex.Match(html).Groups[1].Value)
            ?? $"Pornhub gif {gifId}";

        return new ImageDownloadPlan(
            title,
            pageUri.ToString(),
            "Pornhub",
            ImageDownloadExecutionMode.DirectHttp,
            [new ImageDownloadItem(absoluteUrl, BuildGifFileName(absoluteUrl, gifId))],
            null,
            Tags: null);
    }

    private static string NormalizeUrl(string url) =>
        url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;

    private static async Task<string> FetchPornhubPageAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", "accessAgeDisclaimerPH=1");
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static bool TryNormalizeMediaUrl(string candidate, Uri pageUri, out string absoluteUrl)
    {
        absoluteUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
        {
            absoluteUrl = absolute.ToString();
            return true;
        }

        if (Uri.TryCreate(pageUri, candidate, out absolute))
        {
            absoluteUrl = absolute.ToString();
            return true;
        }

        return false;
    }

    private static string BuildGifFileName(string mediaUrl, string gifId)
    {
        try
        {
            var fileName = Path.GetFileName(new Uri(mediaUrl).AbsolutePath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                if (fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    return Path.ChangeExtension(fileName, ".gif");

                if (!fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    return Path.ChangeExtension(fileName, ".gif");

                return fileName;
            }
        }
        catch
        {
            // fall through
        }

        return $"gif-{gifId}.gif";
    }

    private static string? UnescapeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\/", "/", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Trim();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        client.Timeout = TimeSpan.FromMinutes(2);
        return client;
    }
}

internal sealed class BuiltinPornhubGifHandler : IImageDownloadHandler
{
    public static readonly BuiltinPornhubGifHandler Instance = new();

    public string SiteId => "pornhub-gifs-builtin";
    public string SiteName => "Pornhub";

    public bool CanHandle(Uri url) => PornhubGifDownloadHelper.IsGifUrl(url);

    public Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken = default) =>
        PornhubGifDownloadHelper.BuildPlanAsync(url, cancellationToken);
}
