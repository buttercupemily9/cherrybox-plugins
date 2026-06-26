using CherryBox.Auth;
using CherryBox.Core.Configuration;
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
    private readonly IConfigManager _config;
    private readonly PasswordResetSettingsStore _settingsStore;
    private readonly ResetTokenStore _tokenStore;
    private readonly IEmailService _emailService;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        CherryBoxDbContext db,
        IAuthService auth,
        IConfigManager config,
        PasswordResetSettingsStore settingsStore,
        ResetTokenStore tokenStore,
        IEmailService emailService,
        ILogger<PasswordResetService> logger)
    {
        _db = db;
        _auth = auth;
        _config = config;
        _settingsStore = settingsStore;
        _tokenStore = tokenStore;
        _emailService = emailService;
        _logger = logger;
    }

    public PasswordResetStatusDto GetStatus()
    {
        var settings = _settingsStore.Get();
        var emailConfigured = _emailService.GetStatus().Configured;
        var publicUrl = ResolvePublicBaseUrl(settings);
        var configured = settings.Enabled
            && emailConfigured
            && !string.IsNullOrWhiteSpace(CherryBoxUrlSettings.NormalizePublicUrl(_config.Current.PublicUrl)
                ?? CherryBoxUrlSettings.NormalizePublicUrl(settings.PublicBaseUrl));
        return new PasswordResetStatusDto(true, configured);
    }

    public Task<PasswordResetSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Get();
        return Task.FromResult(ToDto(settings));
    }

    public Task<PasswordResetSettingsDto> UpdateSettingsAsync(
        UpdatePasswordResetSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidOperationException("Request body is required.");

        if (request.TokenLifetimeMinutes < 5 || request.TokenLifetimeMinutes > 24 * 60)
            throw new InvalidOperationException("Token lifetime must be between 5 and 1440 minutes.");

        var next = new PasswordResetSettings
        {
            Enabled = request.Enabled,
            PublicBaseUrl = _settingsStore.Get().PublicBaseUrl,
            TokenLifetimeMinutes = request.TokenLifetimeMinutes
        };

        _settingsStore.Save(next);
        return Task.FromResult(ToDto(next));
    }

    public async Task RequestResetAsync(string usernameOrEmail, CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Get();
        if (!settings.Enabled || !_emailService.GetStatus().Configured)
            return;

        var publicBaseUrl = ResolvePublicBaseUrl(settings);
        if (string.IsNullOrWhiteSpace(CherryBoxUrlSettings.NormalizePublicUrl(_config.Current.PublicUrl)
                ?? CherryBoxUrlSettings.NormalizePublicUrl(settings.PublicBaseUrl)))
        {
            _logger.LogWarning("Password reset requested but public URL is not configured in Settings → General.");
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

        var resetUrl = $"{publicBaseUrl}/reset-password?token={Uri.EscapeDataString(token)}";
        var body =
            $"""
            Hello {user.Username},

            We received a request to reset your CherryBox password.

            Open this link to choose a new password:
            {resetUrl}

            This link expires in {settings.TokenLifetimeMinutes} minutes. If you did not request a reset, you can ignore this email.
            """;

        await _emailService.SendAsync(new SendEmailRequest(
            email,
            "Reset your CherryBox password",
            body), cancellationToken);

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

    private async Task<User?> FindUserByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var userId = await _emailService.FindUserIdByEmailAsync(email, cancellationToken);
        if (userId is null)
            return null;

        return await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId.Value && u.IsActive, cancellationToken);
    }

    private async Task<string?> ResolveEmailAsync(Guid userId, string username, CancellationToken cancellationToken)
    {
        var email = await _emailService.GetUserEmailAsync(userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(email))
            return email.Trim();

        return username.Contains('@', StringComparison.Ordinal) ? username.Trim() : null;
    }

    private string ResolvePublicBaseUrl(PasswordResetSettings settings) =>
        CherryBoxUrlSettings.ResolvePublicBaseUrl(_config.Current, settings.PublicBaseUrl, _config.Current.Port);

    private PasswordResetSettingsDto ToDto(PasswordResetSettings settings) => new(
        settings.Enabled,
        ResolvePublicBaseUrl(settings),
        settings.TokenLifetimeMinutes);
}
