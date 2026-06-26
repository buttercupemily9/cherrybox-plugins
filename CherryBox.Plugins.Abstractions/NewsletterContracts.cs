namespace CherryBox.Plugins.Abstractions;

public sealed record NewsletterStatusDto(bool Available, bool Configured);

public sealed record NewsletterSettingsDto(
    bool WelcomeEnabled,
    bool WeeklyEnabled,
    string WeeklyDay,
    string WeeklyTime,
    string PublicBaseUrl);

public sealed record UpdateNewsletterSettingsRequest(
    bool WelcomeEnabled,
    bool WeeklyEnabled,
    string WeeklyDay,
    string WeeklyTime,
    string PublicBaseUrl);

public sealed record NewsletterSubscriptionDto(
    bool Available,
    bool Subscribed);

public sealed record UpdateNewsletterSubscriptionRequest(bool Subscribed);

public sealed record NewsletterTestResultDto(bool Success, string Message, string? SentTo);

public interface INewsletterService
{
    NewsletterStatusDto GetStatus();
    Task<NewsletterSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<NewsletterSettingsDto> UpdateSettingsAsync(UpdateNewsletterSettingsRequest request, CancellationToken cancellationToken = default);
    Task SendWelcomeAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SendWeeklyDigestAsync(CancellationToken cancellationToken = default);
    Task<NewsletterSubscriptionDto> GetSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default);
    Task UpdateSubscriptionAsync(Guid userId, bool subscribed, CancellationToken cancellationToken = default);

    Task<NewsletterTestResultDto> SendTestDigestToAdminAsync(Guid adminUserId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Use the host newsletter test API.");
}
