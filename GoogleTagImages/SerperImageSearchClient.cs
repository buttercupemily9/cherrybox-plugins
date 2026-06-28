using System.Net.Http.Json;
using System.Text.Json;

namespace CherryBox.GoogleTagImages.Plugin;

internal sealed class SerperImageSearchClient
{
    private readonly HttpClient _http;

    public SerperImageSearchClient(HttpClient http) => _http = http;

    public Task<IReadOnlyList<string>> SearchImageUrlsAsync(
        string apiKey,
        string query,
        CancellationToken cancellationToken) =>
        SearchCandidatesAsync(apiKey, query, cancellationToken)
            .ContinueWith(
                task => (IReadOnlyList<string>)TagImageUrlRanker.Rank(task.Result),
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

    public async Task<IReadOnlyList<TagImageCandidate>> SearchCandidatesAsync(
        string apiKey,
        string query,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/images");
        request.Headers.TryAddWithoutValidation("X-API-KEY", apiKey);
        request.Content = JsonContent.Create(new { q = query, num = 20, safe = "off" });

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ExtractApiError(body, response.StatusCode));

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
            return Array.Empty<TagImageCandidate>();

        var candidates = new List<TagImageCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in images.EnumerateArray())
        {
            if (candidates.Count >= 20)
                break;

            var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            var sourcePage = item.TryGetProperty("link", out var linkEl) ? linkEl.GetString() : null;
            var sourceSite = item.TryGetProperty("source", out var sourceEl) ? sourceEl.GetString() : null;

            TryAddCandidate(candidates, seen, item, "imageUrl", title, sourcePage, sourceSite);
            TryAddCandidate(candidates, seen, item, "thumbnailUrl", title, sourcePage, sourceSite);
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
            if (document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return $"Serper HTTP {(int)statusCode}: {message.GetString()}";
            }
        }
        catch
        {
            // ignore parse errors
        }

        return $"Serper HTTP {(int)statusCode}.";
    }
}
