using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CherryBox.Newsletter.Plugin;

internal sealed class NewsletterService : INewsletterService
{
    private const int DigestItemLimit = 12;

    private readonly CherryBoxDbContext _db;
    private readonly NewsletterSettingsStore _settingsStore;
    private readonly NewsletterSubscriptionStore _subscriptionStore;
    private readonly IEmailService _emailService;
    private readonly ILogger<NewsletterService> _logger;

    public NewsletterService(
        CherryBoxDbContext db,
        NewsletterSettingsStore settingsStore,
        NewsletterSubscriptionStore subscriptionStore,
        IEmailService emailService,
        ILogger<NewsletterService> logger)
    {
        _db = db;
        _settingsStore = settingsStore;
        _subscriptionStore = subscriptionStore;
        _emailService = emailService;
        _logger = logger;
    }

    public NewsletterStatusDto GetStatus()
    {
        var settings = _settingsStore.Get();
        var emailConfigured = _emailService.GetStatus().Configured;
        var configured = emailConfigured
            && !string.IsNullOrWhiteSpace(settings.PublicBaseUrl)
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
            WeeklyTime = request.WeeklyTime.Trim(),
            PublicBaseUrl = request.PublicBaseUrl?.Trim().TrimEnd('/') ?? string.Empty,
            LastWeeklySentAt = current.LastWeeklySentAt
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

        var since = DateTimeOffset.UtcNow.AddDays(-7);
        var items = await LoadDigestItemsAsync(since, cancellationToken);
        var baseUrl = ResolveBaseUrl(settings);

        var users = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            if (!await _subscriptionStore.IsSubscribedAsync(user.Id, cancellationToken))
                continue;

            var email = await ResolveEmailAsync(user, cancellationToken);
            if (string.IsNullOrWhiteSpace(email))
                continue;

            try
            {
                var theme = NewsletterTemplates.GetTheme(user.SkinId);
                var html = NewsletterTemplates.RenderWeeklyDigest(user.Username, baseUrl, theme, items);
                var plain = NewsletterTemplates.WeeklyPlainText(user.Username, baseUrl, items);
                await _emailService.SendAsync(new SendEmailRequest(
                    email,
                    "Your CherryBox weekly update",
                    plain,
                    html), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send weekly newsletter to user {UserId}", user.Id);
            }
        }

        var current = _settingsStore.Get();
        current.LastWeeklySentAt = DateTimeOffset.UtcNow.UtcDateTime.ToString("O");
        _settingsStore.Save(current);
        _logger.LogInformation("Weekly newsletter sent to subscribed users.");
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

    internal bool ShouldSendWeeklyNow(DateTimeOffset utcNow)
    {
        var settings = _settingsStore.Get();
        if (!settings.WeeklyEnabled)
            return false;

        if (!TryParseWeeklyTime(settings.WeeklyTime, out var scheduledTime))
            return false;

        if (!string.Equals(utcNow.DayOfWeek.ToString(), NormalizeDay(settings.WeeklyDay), StringComparison.OrdinalIgnoreCase))
            return false;

        if (utcNow.TimeOfDay < scheduledTime || utcNow.TimeOfDay >= scheduledTime.Add(TimeSpan.FromHours(1)))
            return false;

        if (DateTimeOffset.TryParse(settings.LastWeeklySentAt, out var lastSent)
            && lastSent > utcNow.AddHours(-20))
        {
            return false;
        }

        return true;
    }

    private async Task<IReadOnlyList<DigestItem>> LoadDigestItemsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var media = await _db.MediaItems.AsNoTracking()
            .Where(m => m.UpdatedAt >= since || m.CreatedAt >= since)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(DigestItemLimit)
            .ToListAsync(cancellationToken);

        var baseUrl = ResolveBaseUrl(_settingsStore.Get());
        return media.Select(m => new DigestItem(
            string.IsNullOrWhiteSpace(m.Title) ? m.FileName : m.Title!,
            FormatMediaType(m.MediaType),
            BuildMediaUrl(baseUrl, m.Id, m.MediaType),
            m.UpdatedAt)).ToList();
    }

    private async Task<string?> ResolveEmailAsync(CherryBox.Core.Entities.User user, CancellationToken cancellationToken)
    {
        var email = await _emailService.GetUserEmailAsync(user.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(email))
            return email.Trim();

        return user.Username.Contains('@', StringComparison.Ordinal) ? user.Username.Trim() : null;
    }

    private static string ResolveBaseUrl(NewsletterSettings settings) =>
        string.IsNullOrWhiteSpace(settings.PublicBaseUrl) ? "http://localhost:8787" : settings.PublicBaseUrl;

    private static string BuildMediaUrl(string baseUrl, Guid id, MediaType mediaType)
    {
        var path = mediaType switch
        {
            MediaType.Video => $"/video/{id}",
            MediaType.Audio => $"/audio/{id}",
            MediaType.Story => $"/story/{id}",
            MediaType.Image => $"/picture/{id}",
            _ => $"/media/{id}"
        };
        return $"{baseUrl.TrimEnd('/')}{path}";
    }

    private static string FormatMediaType(MediaType mediaType) => mediaType switch
    {
        MediaType.Video => "Video",
        MediaType.Audio => "Audio",
        MediaType.Story => "Story",
        MediaType.Image => "Picture",
        _ => mediaType.ToString()
    };

    private static NewsletterSettingsDto ToDto(NewsletterSettings settings) => new(
        settings.WelcomeEnabled,
        settings.WeeklyEnabled,
        settings.WeeklyDay,
        settings.WeeklyTime,
        settings.PublicBaseUrl);

    private static bool TryParseWeeklyTime(string value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return TimeSpan.TryParse(value.Trim(), out time);
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
