namespace CherryBox.Plugins.Abstractions;

public sealed record PasswordResetStatusDto(bool Available, bool Configured);

public sealed record PasswordResetSettingsDto(
    bool Enabled,
    string SmtpHost,
    int SmtpPort,
    bool UseTls,
    string? Username,
    bool HasPassword,
    string FromAddress,
    string FromDisplayName,
    string PublicBaseUrl,
    int TokenLifetimeMinutes);

public sealed record UpdatePasswordResetSettingsRequest(
    bool Enabled,
    string SmtpHost,
    int SmtpPort,
    bool UseTls,
    string? Username,
    string? Password,
    string FromAddress,
    string FromDisplayName,
    string PublicBaseUrl,
    int TokenLifetimeMinutes);

public sealed record SendPasswordResetTestEmailRequest(string ToAddress);

public sealed record SetUserEmailRequest(string? Email);

public interface IPasswordResetService
{
    PasswordResetStatusDto GetStatus();
    Task<PasswordResetSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<PasswordResetSettingsDto> UpdateSettingsAsync(UpdatePasswordResetSettingsRequest request, CancellationToken cancellationToken = default);
    Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default);
    Task RequestResetAsync(string usernameOrEmail, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);
    Task<string?> GetUserEmailAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetUserEmailAsync(Guid userId, string? email, CancellationToken cancellationToken = default);
    Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default);
}
