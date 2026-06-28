using System.Text.Json;

namespace CherryBox.GoogleTagImages.Plugin;

internal sealed class GoogleCustomSearchClient
{
    private readonly HttpClient _http;

    public GoogleCustomSearchClient(HttpClient http) => _http = http;

    public Task<IReadOnlyList<string>> SearchImageUrlsAsync(
        string apiKey,
        string searchEngineId,
        string query,
        CancellationToken cancellationToken) =>
        SearchCandidatesAsync(apiKey, searchEngineId, query, cancellationToken)
            .ContinueWith(
                task => (IReadOnlyList<string>)TagImageUrlRanker.Rank(task.Result),
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

    public async Task<IReadOnlyList<TagImageCandidate>> SearchCandidatesAsync(
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
            return Array.Empty<TagImageCandidate>();

        var candidates = new List<TagImageCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            if (candidates.Count >= 10)
                break;

            var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            var pageLink = item.TryGetProperty("link", out var linkEl) ? linkEl.GetString() : null;
            var displayLink = item.TryGetProperty("displayLink", out var displayEl) ? displayEl.GetString() : null;

            TryAddCandidate(candidates, seen, item, "link", title, pageLink, displayLink);
            if (item.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object)
                TryAddCandidate(candidates, seen, image, "thumbnailLink", title, pageLink, displayLink);
        }

        return candidates;
    }

    private static void TryAddCandidate(
        List<TagImageCandidate> candidates,
        HashSet<string> seen,
        JsonElement item,
        string propertyName,
        string? title,
        string? sourcePage,
        string? sourceSite)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return;

        var url = value.GetString();
        if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
            return;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return;

        candidates.Add(new TagImageCandidate(url, title, sourcePage, sourceSite));
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
