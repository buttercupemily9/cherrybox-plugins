namespace CherryBox.Plugins.Abstractions;

public sealed record AiSettingsDto(
    bool HasApiKey,
    string Model,
    string ChatModel,
    string Voice,
    string ResponseFormat,
    double Speed,
    int MaxCharsPerRequest);

public sealed record UpdateAiSettingsRequest(
    string? ApiKey,
    bool ClearApiKey,
    string Model,
    string ChatModel,
    string Voice,
    string ResponseFormat,
    double Speed,
    int MaxCharsPerRequest);

public sealed record AiTestRequest(string? SampleText);

public sealed record AiTestResult(bool Ok, string Message);

public sealed record AiChatRequest(
    string UserPrompt,
    string? SystemPrompt = null,
    int? MaxTokens = null);

public interface IAiService
{
    Task<AiSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<AiSettingsDto> UpdateSettingsAsync(UpdateAiSettingsRequest request, CancellationToken cancellationToken = default);
    Task<AiTestResult> TestConnectionAsync(AiTestRequest request, CancellationToken cancellationToken = default);
    Task<byte[]> SynthesizeSpeechAsync(string text, CancellationToken cancellationToken = default);
    Task<string> CompleteChatAsync(AiChatRequest request, CancellationToken cancellationToken = default);
}
