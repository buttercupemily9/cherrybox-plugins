using CherryBox.Plugins.Abstractions;

namespace CherryBox.GoogleTagImages.Plugin;

internal sealed class GoogleTagImageService : IGoogleTagImageService
{
    private readonly GoogleTagImageSettingsStore _settings;
    private readonly GoogleCustomSearchClient _googleSearch;
    private readonly SerperImageSearchClient _serperSearch;

    public GoogleTagImageService(
        GoogleTagImageSettingsStore settings,
        GoogleCustomSearchClient googleSearch,
        SerperImageSearchClient serperSearch)
    {
        _settings = settings;
        _googleSearch = googleSearch;
        _serperSearch = serperSearch;
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
            {
                settings.SearchEngineId = string.IsNullOrWhiteSpace(request.SearchEngineId)
                    ? null
                    : request.SearchEngineId.Trim();
                settings.SearchProvider = string.IsNullOrWhiteSpace(settings.SearchEngineId)
                    ? TagImageSearchProviders.Serper
                    : TagImageSearchProviders.GoogleCustomSearch;
            }

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
        if (!IsConfigured(settings))
        {
            return ResolveProvider(settings) == TagImageSearchProviders.GoogleCustomSearch
                ? new GoogleTagImageTestResult(false, "Configure a Google API key and Search Engine ID first.")
                : new GoogleTagImageTestResult(false, "Configure a Serper API key first.");
        }

        try
        {
            var urls = await SearchWithSettingsAsync(settings, "test", cancellationToken);
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
        if (!IsConfigured(settings))
            return Array.Empty<string>();

        foreach (var searchQuery in TagImageSearchQueries.BuildQueries(query, settings.SearchQuerySuffix))
        {
            var urls = await SearchWithSettingsAsync(settings, searchQuery, cancellationToken);
            if (urls.Count > 0)
                return urls;
        }

        return Array.Empty<string>();
    }

    private Task<IReadOnlyList<string>> SearchWithSettingsAsync(
        GoogleTagImageSettings settings,
        string query,
        CancellationToken cancellationToken)
    {
        if (ResolveProvider(settings) == TagImageSearchProviders.GoogleCustomSearch)
        {
            return _googleSearch.SearchImageUrlsAsync(
                settings.ApiKey!,
                settings.SearchEngineId!,
                query,
                cancellationToken);
        }

        return _serperSearch.SearchImageUrlsAsync(settings.ApiKey!, query, cancellationToken);
    }

    private static bool IsConfigured(GoogleTagImageSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return false;

        return ResolveProvider(settings) == TagImageSearchProviders.GoogleCustomSearch
            ? !string.IsNullOrWhiteSpace(settings.SearchEngineId)
            : true;
    }

    private static string ResolveProvider(GoogleTagImageSettings settings)
    {
        if (string.Equals(settings.SearchProvider, TagImageSearchProviders.GoogleCustomSearch, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.SearchEngineId))
            return TagImageSearchProviders.GoogleCustomSearch;

        if (string.Equals(settings.SearchProvider, TagImageSearchProviders.Serper, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(settings.SearchEngineId))
            return TagImageSearchProviders.Serper;

        return TagImageSearchProviders.GoogleCustomSearch;
    }

    private static GoogleTagImageSettingsDto ToDto(GoogleTagImageSettings settings)
    {
        var hasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
        return new GoogleTagImageSettingsDto(
            ResolveProvider(settings),
            hasApiKey,
            IsConfigured(settings),
            settings.SearchEngineId,
            settings.SearchQuerySuffix,
            Math.Clamp(settings.MaxTagsPerRun, 1, 500),
            Math.Clamp(settings.RequestDelayMs, 0, 10_000));
    }
}
