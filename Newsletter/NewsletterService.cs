using CherryBox.Core.Configuration;
using CherryBox.Core.Entities;
using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CherryBox.Newsletter.Plugin;

internal sealed class NewsletterService : INewsletterService
{
    private readonly CherryBoxDbContext _db;
    private readonly IConfigManager _config;
    private readonly NewsletterSettingsStore _settingsStore;
    private readonly NewsletterSubscriptionStore _subscriptionStore;
    private readonly NewsletterWeeklyCacheStore _cacheStore;
    private readonly IEmailService _emailService;
    private readonly IPluginServiceRegistry _plugins;
    private readonly IServiceProvider _services;
    private readonly ILogger<NewsletterService> _logger;

    public NewsletterService(
        CherryBoxDbContext db,
        IConfigManager config,
        NewsletterSettingsStore settingsStore,
        NewsletterSubscriptionStore subscriptionStore,
        NewsletterWeeklyCacheStore cacheStore,
        IEmailService emailService,
        IPluginServiceRegistry plugins,
        IServiceProvider services,
        ILogger<NewsletterService> logger)
    {
        _db = db;
        _config = config;
        _settingsStore = settingsStore;
        _subscriptionStore = subscriptionStore;
        _cacheStore = cacheStore;
        _emailService = emailService;
        _plugins = plugins;
        _services = services;
        _logger = logger;
    }

    public NewsletterStatusDto GetStatus()
    {
        var settings = _settingsStore.Get();
        var emailConfigured = _emailService.GetStatus().Configured;
        var configured = emailConfigured
            && (settings.WelcomeEnabled || settings.WeeklyEnabled);
        return new NewsletterStatusDto(true, configured);
    }

    public Task<NewsletterSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Get();
        return Task.FromResult(ToDto(settings));
    }

    public Task<NewsletterSettingsDto> UpdateSettingsAsync(
        UpdateNewsletterSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidOperationException("Request body is required.");

        if (!TryParseWeeklyTime(request.WeeklyTime, out _))
            throw new InvalidOperationException("Weekly time must use HH:mm format.");

        var current = _settingsStore.Get();
        var next = new NewsletterSettings
        {
            WelcomeEnabled = request.WelcomeEnabled,
            WeeklyEnabled = request.WeeklyEnabled,
            WeeklyDay = NormalizeDay(request.WeeklyDay),
            WeeklyTime = NormalizeWeeklyTime(request.WeeklyTime),
            PublicBaseUrl = current.PublicBaseUrl,
            LastWeeklySentAt = current.LastWeeklySentAt,
            LastWeeklyPreparedWeekKey = current.LastWeeklyPreparedWeekKey
        };

        _settingsStore.Save(next);
        return Task.FromResult(ToDto(next));
    }

    public async Task SendWelcomeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Get();
        if (!settings.WelcomeEnabled || !_emailService.GetStatus().Configured)
            return;

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        if (user is null)
            return;

        var email = await ResolveEmailAsync(user, cancellationToken);
        if (string.IsNullOrWhiteSpace(email))
            return;

        var baseUrl = ResolveBaseUrl(settings);
        var theme = NewsletterTemplates.GetTheme(user.SkinId);
        var html = NewsletterTemplates.RenderWelcome(user.Username, baseUrl, theme);
        var plain = NewsletterTemplates.WelcomePlainText(user.Username, baseUrl);

        await _emailService.SendAsync(new SendEmailRequest(
            email,
            "Welcome to CherryBox",
            plain,
            html), cancellationToken);

        await _subscriptionStore.SetSubscribedAsync(userId, true, cancellationToken);
        _logger.LogInformation("Welcome newsletter sent to user {UserId}", userId);
    }

    public async Task SendWeeklyDigestAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Get();
        if (!settings.WeeklyEnabled || !_emailService.GetStatus().Configured)
            return;

        var utcNow = DateTimeOffset.UtcNow;
        var weekKey = GetWeeklySendWeekKey(utcNow);
        if (!_cacheStore.IsReadyForWeek(weekKey))
        {
            _logger.LogInformation("Weekly digest cache not ready for {WeekKey}; preparing now.", weekKey);
            await PrepareWeeklyDigestAsync(cancellationToken);
        }

        var cache = _cacheStore.GetForWeek(weekKey);
        if (cache is null || !HasAllAiVariants(cache))
        {
            _logger.LogWarning("Weekly digest cache is still unavailable for {WeekKey}; skipping send.", weekKey);
            return;
        }

        var baseUrl = ResolveBaseUrl(settings);
        var embeddedImages = cache.EmbeddedImages.Select(i => i.ToEmbeddedImage()).ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .ToListAsync(cancellationToken);

        var sentCount = 0;
        foreach (var user in users)
        {
            if (!await _subscriptionStore.IsSubscribedAsync(user.Id, cancellationToken))
                continue;

            var email = await ResolveEmailAsync(user, cancellationToken);
            if (string.IsNullOrWhiteSpace(email))
                continue;

            try
            {
                var voice = ResolveVoiceForUser(user, weekKey);
                var audience = user.Gender ?? UserGender.Female;
                NewsletterAiVariant.TryGetIntro(cache, voice, audience, user.SexualOrientation, out var aiIntro);
                var (html, plain, _, narratorName) = NewsletterWeeklyComposer.RenderForUser(
                    user.Username,
                    user.SkinId,
                    baseUrl,
                    cache.Items,
                    embeddedImages,
                    voice,
                    aiIntro);

                await _emailService.SendAsync(new SendEmailRequest(
                    email,
                    "Your CherryBox weekly update",
                    plain,
                    html,
                    embeddedImages,
                    narratorName), cancellationToken);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send weekly newsletter to user {UserId}", user.Id);
            }
        }

        if (sentCount > 0)
        {
            var current = _settingsStore.Get();
            current.LastWeeklySentAt = utcNow.ToString("O");
            _settingsStore.Save(current);
            _logger.LogInformation("Weekly newsletter sent to {Count} subscribed user(s).", sentCount);
        }
        else
        {
            _logger.LogWarning("Weekly newsletter run completed without sending any email.");
        }
    }

    internal async Task PrepareWeeklyDigestAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Get();
        if (!settings.WeeklyEnabled || !_emailService.GetStatus().Configured)
            return;

        var utcNow = DateTimeOffset.UtcNow;
        var weekKey = GetWeeklySendWeekKey(utcNow);
        if (_cacheStore.IsReadyForWeek(weekKey))
            return;

        var since = utcNow.AddDays(-7);
        var baseUrl = ResolveBaseUrl(settings);

        _logger.LogInformation("Preparing weekly digest cache for {WeekKey}.", weekKey);

        var (items, embeddedImages) = await NewsletterWeeklyComposer.LoadDigestItemsAsync(
            _db,
            baseUrl,
            since,
            cancellationToken);

        var versions = new Dictionary<string, WeeklyDigestVoiceVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var (voice, audience, orientation) in NewsletterAiVariant.All())
        {
            var intro = await NewsletterWeeklyComposer.TryGenerateAiIntroAsync(
                _plugins,
                _services,
                "[NAME]",
                items,
                voice,
                audience,
                orientation,
                _logger,
                cancellationToken);
            versions[NewsletterAiVariant.CacheKey(voice, audience, orientation)] = new() { AiIntro = intro };
        }

        var cache = new WeeklyDigestCache
        {
            WeekKey = weekKey,
            Since = since,
            GeneratedAt = utcNow,
            Items = items.ToList(),
            EmbeddedImages = embeddedImages.Select(CachedEmbeddedImage.From).ToList(),
            Versions = versions
        };

        _cacheStore.Save(cache);

        var current = _settingsStore.Get();
        current.LastWeeklyPreparedWeekKey = weekKey;
        _settingsStore.Save(current);

        _logger.LogInformation(
            "Weekly digest cache ready for {WeekKey} with {ItemCount} item(s) and narrator/audience AI intros.",
            weekKey,
            items.Count);
    }

    public async Task<NewsletterSubscriptionDto> GetSubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!_emailService.GetStatus().Available)
            return new NewsletterSubscriptionDto(false, false);

        var subscribed = await _subscriptionStore.IsSubscribedAsync(userId, cancellationToken);
        return new NewsletterSubscriptionDto(true, subscribed);
    }

    public Task UpdateSubscriptionAsync(Guid userId, bool subscribed, CancellationToken cancellationToken = default) =>
        _subscriptionStore.SetSubscribedAsync(userId, subscribed, cancellationToken);

    /// <summary>Kept for hosts that still expose this on <see cref="INewsletterService"/>.</summary>
    public bool ShouldSendWeeklyDigestNow(DateTimeOffset utcNow) => ShouldSendWeeklyNow(utcNow);

    internal bool ShouldPrepareWeeklyDigestNow(DateTimeOffset utcNow)
    {
        var settings = _settingsStore.Get();
        if (!settings.WeeklyEnabled || !_emailService.GetStatus().Configured)
            return false;

        if (!IsWeeklySendDay(utcNow, settings))
            return false;

        if (!TryParseWeeklyTime(settings.WeeklyTime, out var scheduledTime))
            return false;

        var localNow = CherryBoxTimeZone.ToConfiguredLocalTime(utcNow, _config.Current.TimeZoneId);
        if (localNow.TimeOfDay >= scheduledTime)
            return false;

        var weekKey = GetWeeklySendWeekKey(utcNow);
        return !_cacheStore.IsReadyForWeek(weekKey);
    }

    internal bool ShouldSendWeeklyNow(DateTimeOffset utcNow)
    {
        var settings = _settingsStore.Get();
        if (!settings.WeeklyEnabled || !_emailService.GetStatus().Configured)
            return false;

        if (!TryParseWeeklyTime(settings.WeeklyTime, out var scheduledTime))
            return false;

        var localNow = CherryBoxTimeZone.ToConfiguredLocalTime(utcNow, _config.Current.TimeZoneId);
        if (!string.Equals(localNow.DayOfWeek.ToString(), NormalizeDay(settings.WeeklyDay), StringComparison.OrdinalIgnoreCase))
            return false;

        if (localNow.TimeOfDay < scheduledTime || localNow.TimeOfDay >= scheduledTime.Add(TimeSpan.FromHours(1)))
            return false;

        if (DateTimeOffset.TryParse(settings.LastWeeklySentAt, out var lastSent)
            && lastSent.ToUniversalTime() > utcNow.AddHours(-20))
        {
            return false;
        }

        return true;
    }

    private static NewsletterNarratorVoice ResolveVoiceForUser(User user, string weekKey)
    {
        var random = user.SexualOrientation == SexualOrientation.Bi
            ? new Random(HashCode.Combine(user.Id, weekKey.GetHashCode(StringComparison.Ordinal)))
            : null;

        return NewsletterVoiceSelector.Resolve(user.Gender, user.SexualOrientation, random);
    }

    private string GetWeeklySendWeekKey(DateTimeOffset utcNow)
    {
        var localNow = CherryBoxTimeZone.ToConfiguredLocalTime(utcNow, _config.Current.TimeZoneId);
        return localNow.ToString("yyyy-MM-dd");
    }

    private bool IsWeeklySendDay(DateTimeOffset utcNow, NewsletterSettings settings)
    {
        var localNow = CherryBoxTimeZone.ToConfiguredLocalTime(utcNow, _config.Current.TimeZoneId);
        return string.Equals(localNow.DayOfWeek.ToString(), NormalizeDay(settings.WeeklyDay), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAllAiVariants(WeeklyDigestCache cache)
    {
        foreach (var (voice, audience, orientation) in NewsletterAiVariant.All())
        {
            if (!cache.Versions.ContainsKey(NewsletterAiVariant.CacheKey(voice, audience, orientation)))
                return false;
        }

        return true;
    }

    private async Task<string?> ResolveEmailAsync(User user, CancellationToken cancellationToken)
    {
        var email = await _emailService.GetUserEmailAsync(user.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(email))
            return email.Trim();

        return user.Username.Contains('@', StringComparison.Ordinal) ? user.Username.Trim() : null;
    }

    private string ResolveBaseUrl(NewsletterSettings settings) =>
        CherryBoxUrlSettings.ResolvePublicBaseUrl(_config.Current, settings.PublicBaseUrl, _config.Current.Port);

    private NewsletterSettingsDto ToDto(NewsletterSettings settings) => new(
        settings.WelcomeEnabled,
        settings.WeeklyEnabled,
        settings.WeeklyDay,
        NormalizeWeeklyTime(settings.WeeklyTime),
        ResolveBaseUrl(settings));

    private static bool TryParseWeeklyTime(string value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return TimeSpan.TryParse(value.Trim(), out time);
    }

    private static string NormalizeWeeklyTime(string value)
    {
        if (!TryParseWeeklyTime(value, out var time))
            return "09:00";

        return $"{time.Hours:D2}:{time.Minutes:D2}";
    }

    private static string NormalizeDay(string day)
    {
        if (string.IsNullOrWhiteSpace(day))
            return "Sunday";

        var trimmed = day.Trim();
        if (trimmed.Length >= 3 && Enum.TryParse<DayOfWeek>(trimmed, true, out var parsed))
            return parsed.ToString();

        return "Sunday";
    }
}
