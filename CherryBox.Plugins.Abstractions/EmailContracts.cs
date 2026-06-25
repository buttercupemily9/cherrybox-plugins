namespace CherryBox.Plugins.Abstractions;

public sealed record EmailStatusDto(bool Available, bool Configured);

public sealed record EmailSettingsDto(
    string SmtpHost,
    int SmtpPort,
    bool UseTls,
    string? Username,
    bool HasPassword,
    string FromAddress,
    string FromDisplayName);

public sealed record UpdateEmailSettingsRequest(
    string SmtpHost,
    int SmtpPort,
    bool UseTls,
    string? Username,
    string? Password,
    string FromAddress,
    string FromDisplayName);

public sealed record SendEmailTestRequest(
    string ToAddress,
    UpdateEmailSettingsRequest? Settings = null);

public sealed record SendEmailRequest(
    string ToAddress,
    string Subject,
    string? PlainTextBody = null,
    string? HtmlBody = null);

public sealed record SetUserEmailRequest(string? Email);

public interface IEmailService
{
    EmailStatusDto GetStatus();
    Task<EmailSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<EmailSettingsDto> UpdateSettingsAsync(UpdateEmailSettingsRequest request, CancellationToken cancellationToken = default);
    Task SendTestEmailAsync(
        string toAddress,
        UpdateEmailSettingsRequest? draftSettings = null,
        CancellationToken cancellationToken = default);
    Task SendAsync(SendEmailRequest request, CancellationToken cancellationToken = default);
    Task<string?> GetUserEmailAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetUserEmailAsync(Guid userId, string? email, CancellationToken cancellationToken = default);
    Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default);
}
