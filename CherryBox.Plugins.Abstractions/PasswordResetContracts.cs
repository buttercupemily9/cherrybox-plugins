namespace CherryBox.Plugins.Abstractions;

public sealed record PasswordResetStatusDto(bool Available, bool Configured);

public sealed record PasswordResetSettingsDto(
    bool Enabled,
    string PublicBaseUrl,
    int TokenLifetimeMinutes);

public sealed record UpdatePasswordResetSettingsRequest(
    bool Enabled,
    string PublicBaseUrl,
    int TokenLifetimeMinutes);

public interface IPasswordResetService
{
    PasswordResetStatusDto GetStatus();
    Task<PasswordResetSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<PasswordResetSettingsDto> UpdateSettingsAsync(UpdatePasswordResetSettingsRequest request, CancellationToken cancellationToken = default);
    Task RequestResetAsync(string usernameOrEmail, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}
