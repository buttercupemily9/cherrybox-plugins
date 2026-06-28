using System.Text.RegularExpressions;

namespace ImageDownload.Plugin.Common;

internal static partial class MotherlessUrlHelper
{
    internal const string CanonicalHost = "motherless.xxx";

    private static readonly string[] SupportedHosts = ["motherless.com", "motherless.xxx"];

    internal static bool IsSupportedHost(Uri url) =>
        SupportedHosts.Any(IsHostMatch);

    internal static bool IsContentPath(Uri url)
    {
        var path = url.AbsolutePath;
        if (path.Contains("/images", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/gallery", StringComparison.OrdinalIgnoreCase))
            return true;

        return IsGalleryPath(path) || IsSingleMediaPath(path);
    }

    internal static string ToCanonicalUrl(string url)
    {
        var uri = SiteHostMatcher.NormalizeUrl(url);
        if (!IsSupportedHost(uri))
            return url;

        var builder = new UriBuilder(uri)
        {
            Host = CanonicalHost,
            Scheme = Uri.UriSchemeHttps,
            Port = -1
        };
        return builder.Uri.ToString();
    }

    private static bool IsHostMatch(string host)
    {
        foreach (var supported in SupportedHosts)
        {
            if (host.Equals(supported, StringComparison.OrdinalIgnoreCase)
                || host.Equals("www." + supported, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsGalleryPath(string path)
    {
        var trimmed = path.TrimEnd('/');
        if (trimmed.Length <= 1)
            return false;

        return GalleryPathRegex().IsMatch(trimmed);
    }

    private static bool IsSingleMediaPath(string path)
    {
        var trimmed = path.TrimEnd('/');
        return trimmed.Length > 1 && SingleMediaPathRegex().IsMatch(trimmed);
    }

    [GeneratedRegex(@"^/(G[VIG]?[A-F0-9]+|g[vifm]?/[a-z0-9_]+(?:/|$))", RegexOptions.IgnoreCase)]
    private static partial Regex GalleryPathRegex();

    [GeneratedRegex(@"^/[A-F0-9]{6,8}$", RegexOptions.IgnoreCase)]
    private static partial Regex SingleMediaPathRegex();
}
