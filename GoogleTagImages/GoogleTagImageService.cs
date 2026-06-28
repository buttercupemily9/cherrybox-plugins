using CherryBox.Plugins.Abstractions;

namespace CherryBox.GoogleTagImages.Plugin;

internal sealed class GoogleTagImageService : IGoogleTagImageService
{
    private readonly GoogleTagImageSettingsStore _settings;
    private readonly GoogleCustomSearchClient _search;

    public GoogleTagImageService(GoogleTagImageSettingsStore settings, GoogleCustomSearchClient search)
    {
        _settings = settings;
        _search = search;
    }

    public Task<GoogleTagImageSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ToDto(_settings.Get()));

    public Task<GoogleTagImageSettingsDto> UpdateSettingsAsync(
        UpdateGoogleTagImageSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = _settings.Update(settings =>
        {
            if (request.ClearApiKey)
                settings.ApiKey = null;
            else if (!string.IsNullOrWhiteSpace(request.ApiKey))
                settings.ApiKey = request.ApiKey.Trim();

            if (request.SearchEngineId is not null)
                settings.SearchEngineId = string.IsNullOrWhiteSpace(request.SearchEngineId)
                    ? null
                    : request.SearchEngineId.Trim();

            if (request.SearchQuerySuffix is not null)
                settings.SearchQuerySuffix = request.SearchQuerySuffix.Trim();

            settings.MaxTagsPerRun = Math.Clamp(request.MaxTagsPerRun, 1, 500);
            settings.RequestDelayMs = Math.Clamp(request.RequestDelayMs, 0, 10_000);
        });

        return Task.FromResult(ToDto(updated));
    }

    public async Task<GoogleTagImageTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.Get();
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.SearchEngineId))
            return new GoogleTagImageTestResult(false, "Configure an API key and Search Engine ID first.");

        try
        {
            var urls = await _search.SearchImageUrlsAsync(
                settings.ApiKey,
                settings.SearchEngineId,
                "test",
                cancellationToken);
            return urls.Count > 0
                ? new GoogleTagImageTestResult(true, $"Connection OK. Found {urls.Count} image result(s).")
                : new GoogleTagImageTestResult(true, "Connection OK, but no image results were returned for the test query.");
        }
        catch (Exception ex)
        {
            return new GoogleTagImageTestResult(false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<string>> SearchImageUrlsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings.Get();
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.SearchEngineId))
            return Array.Empty<string>();

        var searchQuery = string.IsNullOrWhiteSpace(settings.SearchQuerySuffix)
            ? query
            : $"{query} {settings.SearchQuerySuffix}".Trim();

        return await _search.SearchImageUrlsAsync(
            settings.ApiKey,
            settings.SearchEngineId,
            searchQuery,
            cancellationToken);
    }

    private static GoogleTagImageSettingsDto ToDto(GoogleTagImageSettings settings)
    {
        var hasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
        var hasSearchEngineId = !string.IsNullOrWhiteSpace(settings.SearchEngineId);
        return new GoogleTagImageSettingsDto(
            hasApiKey,
            hasApiKey && hasSearchEngineId,
            settings.SearchEngineId,
            settings.SearchQuerySuffix,
            Math.Clamp(settings.MaxTagsPerRun, 1, 500),
            Math.Clamp(settings.RequestDelayMs, 0, 10_000));
    }
}
