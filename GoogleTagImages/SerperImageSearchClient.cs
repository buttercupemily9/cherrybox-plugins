using System.Net.Http.Json;
using System.Text.Json;

namespace CherryBox.GoogleTagImages.Plugin;

internal sealed class SerperImageSearchClient
{
    private readonly HttpClient _http;

    public SerperImageSearchClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<string>> SearchImageUrlsAsync(
        string apiKey,
        string query,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/images");
        request.Headers.TryAddWithoutValidation("X-API-KEY", apiKey);
        request.Content = JsonContent.Create(new { q = query, num = 10, safe = "off" });

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ExtractApiError(body, response.StatusCode));

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in images.EnumerateArray())
        {
            if (urls.Count >= 10)
                break;

            TryAddUrl(urls, seen, item, "imageUrl");
            TryAddUrl(urls, seen, item, "thumbnailUrl");
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
