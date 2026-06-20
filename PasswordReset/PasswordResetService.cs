using CherryBox.Auth;
using CherryBox.Core.Entities;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CherryBox.PasswordReset.Plugin;

internal sealed class PasswordResetService : IPasswordResetService
{
    private readonly CherryBoxDbContext _db;
    private readonly IAuthService _auth;
    private readonly PasswordResetSettingsStore _settingsStore;
    private readonly ResetTokenStore _tokenStore;
    private readonly UserEmailStore _emailStore;
    private readonly SmtpEmailSender _emailSender;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        CherryBoxDbContext db,
        IAuthService auth,
        PasswordResetSettingsStore settingsStore,
        ResetTokenStore tokenStore,
        UserEmailStore emailStore,
        SmtpEmailSender emailSender,
        ILogger<PasswordResetService> logger)
    {
        _db = db;
        _auth = auth;
        _settingsStore = settingsStore;
        _tokenStore = tokenStore;
        _emailStore = emailStore;
        _emailSender = emailSender;
        _logger = logger;
    }

    public PasswordResetStatusDto GetStatus()
    {
        var settings = _settingsStore.Get();
        var configured = settings.Enabled
            && !string.IsNullOrWhiteSpace(settings.SmtpHost)
            && !string.IsNullOrWhiteSpace(settings.FromAddress)
            && !string.IsNullOrWhiteSpace(settings.PublicBaseUrl);
        return new PasswordResetStatusDto(true, configured);
    }

    public Task<PasswordResetSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Get();
        return Task.FromResult(ToDto(settings));
    }

    public Task<PasswordResetSettingsDto> UpdateSettingsAsync(UpdatePasswordResetSettingsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TokenLifetimeMinutes < 5 || request.TokenLifetimeMinutes > 24 * 60)
            throw new InvalidOperationException("Token lifetime must be between 5 and 1440 minutes.");

        var current = _settingsStore.Get();
        var next = new PasswordResetSettings
        {
            Enabled = request.Enabled,
            SmtpHost = request.SmtpHost.Trim(),
            SmtpPort = request.SmtpPort,
            UseTls = request.UseTls,
            Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
            Password = string.IsNullOrWhiteSpace(request.Password) ? current.Password : request.Password,
            FromAddress = request.FromAddress.Trim(),
            FromDisplayName = string.IsNullOrWhiteSpace(request.FromDisplayName) ? "CherryBox" : request.FromDisplayName.Trim(),
            PublicBaseUrl = request.PublicBaseUrl.Trim().TrimEnd('/'),
            TokenLifetimeMinutes = request.TokenLifetimeMinutes
        };

        _settingsStore.Save(next);
        return Task.FromResult(ToDto(next));
    }

    public async Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Get();
        ValidateSendSettings(settings);

        await _emailSender.SendAsync(
            settings,
            toAddress,
            "CherryBox password reset test",
            "This is a test email from the CherryBox password reset plugin.",
            cancellationToken);
    }

    public async Task RequestResetAsync(string usernameOrEmail, CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Get();
        if (!settings.Enabled)
            return;

        try
        {
            ValidateSendSettings(settings);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Password reset requested but SMTP is not configured.");
            return;
        }

        var normalized = usernameOrEmail.Trim();
        var lowered = normalized.ToLowerInvariant();
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.IsActive && u.Username.ToLower() == lowered, cancellationToken);

        if (user is null && normalized.Contains('@', StringComparison.Ordinal))
            user = await FindUserByEmailAsync(normalized, cancellationToken);

        if (user is null)
            return;

        var email = await ResolveEmailAsync(user.Id, user.Username, cancellationToken);
        if (string.IsNullOrWhiteSpace(email))
            return;

        await _tokenStore.PurgeExpiredAsync(cancellationToken);

        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var tokenHash = ResetTokenStore.HashToken(token);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(settings.TokenLifetimeMinutes);
        await _tokenStore.StoreAsync(tokenHash, user.Id, expiresAt, cancellationToken);

        var resetUrl = $"{settings.PublicBaseUrl}/reset-password?token={Uri.EscapeDataString(token)}";
        var body =
            $"""
            Hello {user.Username},

            We received a request to reset your CherryBox password.

            Open this link to choose a new password:
            {resetUrl}

            This link expires in {settings.TokenLifetimeMinutes} minutes. If you did not request a reset, you can ignore this email.
            """;

        await _emailSender.SendAsync(
            settings,
            email,
            "Reset your CherryBox password",
            body,
            cancellationToken);

        _logger.LogInformation("Password reset email queued for user {UserId}", user.Id);
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPassword))
            return false;

        var userId = await _tokenStore.ConsumeAsync(ResetTokenStore.HashToken(token.Trim()), cancellationToken);
        if (userId is null)
            return false;

        return await _auth.SetPasswordAsync(userId.Value, newPassword, cancellationToken);
    }

    public Task<string?> GetUserEmailAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _emailStore.GetAsync(userId, cancellationToken);

    public Task SetUserEmailAsync(Guid userId, string? email, CancellationToken cancellationToken = default) =>
        _emailStore.SetAsync(userId, email, cancellationToken);

    public Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        _emailStore.FindUserIdByEmailAsync(email, cancellationToken);

    private async Task<User?> FindUserByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var userId = await _emailStore.FindUserIdByEmailAsync(email, cancellationToken);
        if (userId is null)
            return null;

        return await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId.Value && u.IsActive, cancellationToken);
    }

    private async Task<string?> ResolveEmailAsync(Guid userId, string username, CancellationToken cancellationToken)
    {
        var email = await _emailStore.GetAsync(userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(email))
            return email.Trim();

        return username.Contains('@', StringComparison.Ordinal) ? username.Trim() : null;
    }

    private static PasswordResetSettingsDto ToDto(PasswordResetSettings settings) => new(
        settings.Enabled,
        settings.SmtpHost,
        settings.SmtpPort,
        settings.UseTls,
        settings.Username,
        !string.IsNullOrEmpty(settings.Password),
        settings.FromAddress,
        settings.FromDisplayName,
        settings.PublicBaseUrl,
        settings.TokenLifetimeMinutes);

    private static void ValidateSendSettings(PasswordResetSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
            throw new InvalidOperationException("SMTP host is required.");
        if (settings.SmtpPort <= 0)
            throw new InvalidOperationException("SMTP port must be positive.");
        if (string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException("From address is required.");
        if (string.IsNullOrWhiteSpace(settings.PublicBaseUrl))
            throw new InvalidOperationException("Public base URL is required.");
    }
}
