using CherryBox.Plugins.Abstractions;

namespace CherryBox.Email.Plugin;

internal sealed class EmailService : IEmailService
{
    private readonly EmailSettingsStore _settingsStore;
    private readonly UserEmailStore _emailStore;
    private readonly SmtpEmailSender _emailSender;

    public EmailService(EmailSettingsStore settingsStore, UserEmailStore emailStore, SmtpEmailSender emailSender)
    {
        _settingsStore = settingsStore;
        _emailStore = emailStore;
        _emailSender = emailSender;
    }

    public EmailStatusDto GetStatus()
    {
        var settings = _settingsStore.Get();
        var configured = !string.IsNullOrWhiteSpace(settings.SmtpHost)
            && !string.IsNullOrWhiteSpace(settings.FromAddress);
        return new EmailStatusDto(true, configured);
    }

    public Task<EmailSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Get();
        return Task.FromResult(ToDto(settings));
    }

    public Task<EmailSettingsDto> UpdateSettingsAsync(UpdateEmailSettingsRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidOperationException("Request body is required.");

        var current = _settingsStore.Get();
        var next = MergeSettings(request, current);
        _settingsStore.Save(next);
        return Task.FromResult(ToDto(next));
    }

    public async Task SendTestEmailAsync(
        string toAddress,
        UpdateEmailSettingsRequest? draftSettings = null,
        CancellationToken cancellationToken = default)
    {
        var current = _settingsStore.Get();
        var settings = draftSettings is null ? current : MergeSettings(draftSettings, current);
        ValidateSendSettings(settings);

        if (!string.IsNullOrWhiteSpace(settings.Username) && string.IsNullOrWhiteSpace(settings.Password))
            throw new InvalidOperationException("SMTP password is required to send a test email.");

        await _emailSender.SendAsync(
            settings,
            toAddress,
            "CherryBox email test",
            "This is a test email from the CherryBox email plugin.",
            null,
            null,
            cancellationToken);
    }

    public async Task SendAsync(SendEmailRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidOperationException("Request body is required.");
        if (string.IsNullOrWhiteSpace(request.ToAddress))
            throw new InvalidOperationException("Recipient email is required.");
        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new InvalidOperationException("Subject is required.");
        if (string.IsNullOrWhiteSpace(request.PlainTextBody) && string.IsNullOrWhiteSpace(request.HtmlBody))
            throw new InvalidOperationException("Email body is required.");

        var settings = _settingsStore.Get();
        ValidateSendSettings(settings);

        await _emailSender.SendAsync(
            settings,
            request.ToAddress.Trim(),
            request.Subject.Trim(),
            request.PlainTextBody,
            request.HtmlBody,
            request.EmbeddedImages,
            cancellationToken);
    }

    public Task<string?> GetUserEmailAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _emailStore.GetAsync(userId, cancellationToken);

    public Task SetUserEmailAsync(Guid userId, string? email, CancellationToken cancellationToken = default) =>
        _emailStore.SetAsync(userId, email, cancellationToken);

    public Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        _emailStore.FindUserIdByEmailAsync(email, cancellationToken);

    private static EmailSettings MergeSettings(UpdateEmailSettingsRequest request, EmailSettings current) =>
        new()
        {
            SmtpHost = request.SmtpHost?.Trim() ?? string.Empty,
            SmtpPort = request.SmtpPort,
            UseTls = request.UseTls,
            Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
            Password = string.IsNullOrWhiteSpace(request.Password) ? current.Password : request.Password,
            FromAddress = request.FromAddress?.Trim() ?? string.Empty,
            FromDisplayName = string.IsNullOrWhiteSpace(request.FromDisplayName) ? "CherryBox" : request.FromDisplayName.Trim()
        };

    private static EmailSettingsDto ToDto(EmailSettings settings) => new(
        settings.SmtpHost,
        settings.SmtpPort,
        settings.UseTls,
        settings.Username,
        !string.IsNullOrEmpty(settings.Password),
        settings.FromAddress,
        settings.FromDisplayName);

    private static void ValidateSendSettings(EmailSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
            throw new InvalidOperationException("SMTP host is required.");
        if (settings.SmtpPort <= 0)
            throw new InvalidOperationException("SMTP port must be positive.");
        if (string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException("From address is required.");
    }
}
