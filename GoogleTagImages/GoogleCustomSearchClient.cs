using System.Text.Json;

namespace CherryBox.GoogleTagImages.Plugin;

internal sealed class GoogleCustomSearchClient
{
    private readonly HttpClient _http;

    public GoogleCustomSearchClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<string>> SearchImageUrlsAsync(
        string apiKey,
        string searchEngineId,
        string query,
        CancellationToken cancellationToken)
    {
        var uri =
            "https://www.googleapis.com/customsearch/v1?" +
            $"key={Uri.EscapeDataString(apiKey)}" +
            $"&cx={Uri.EscapeDataString(searchEngineId)}" +
            $"&q={Uri.EscapeDataString(query)}" +
            "&searchType=image" +
            "&num=10" +
            "&safe=off";

        using var response = await _http.GetAsync(uri, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ExtractApiError(body, response.StatusCode));

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            if (urls.Count >= 10)
                break;

            TryAddUrl(urls, seen, item, "link");
            if (item.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object)
                TryAddUrl(urls, seen, image, "thumbnailLink");
        }

        return urls;
    }

    private static void TryAddUrl(List<string> urls, HashSet<string> seen, JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return;

        var url = value.GetString();
        if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
            return;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return;

        urls.Add(url);
    }

    private static string ExtractApiError(string body, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString() ?? string.Empty;
                if (text.Contains("blocked", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("does not have the access", StringComparison.OrdinalIgnoreCase))
                {
                    return "Google Custom Search HTTP 403: API access is blocked for new Google Cloud projects. "
                        + "Google closed the Custom Search JSON API to new customers. "
                        + "Switch Search provider to Serper in plugin settings (free tier at serper.dev), "
                        + "or use an existing Google CSE account created before the API closed.";
                }

                return $"Google Custom Search HTTP {(int)statusCode}: {text}";
            }
        }
        catch
        {
            // ignore parse errors
        }

        return $"Google Custom Search HTTP {(int)statusCode}.";
    }
}
