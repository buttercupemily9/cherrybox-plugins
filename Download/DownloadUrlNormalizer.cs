namespace CherryBox.Download.Plugin;

public static class DownloadUrlNormalizer
{
    private static readonly HashSet<string> TrackingQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "ref", "referrer"
    };

    public static string Normalize(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return url.Trim().ToLowerInvariant();

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = string.Empty
        };

        if (builder.Path.EndsWith('/'))
            builder.Path = builder.Path.TrimEnd('/');

        if (!string.IsNullOrEmpty(builder.Query))
        {
            var query = builder.Query.TrimStart('?');
            var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParsePair)
                .Where(p => !TrackingQueryKeys.Contains(p.Key))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}");

            var rebuilt = string.Join('&', parts);
            builder.Query = string.IsNullOrEmpty(rebuilt) ? string.Empty : rebuilt;
        }

        return builder.Uri.ToString();
    }

    private static (string Key, string Value) ParsePair(string part)
    {
        var index = part.IndexOf('=');
        if (index < 0)
            return (Uri.UnescapeDataString(part), string.Empty);

        return (
            Uri.UnescapeDataString(part[..index]),
            Uri.UnescapeDataString(part[(index + 1)..]));
    }
}
