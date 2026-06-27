namespace CherryBox.Plugins.Abstractions;

public sealed record AiSettingsDto(
    bool HasApiKey,
    string Model,
    string ChatModel,
    string ImageModel,
    string Voice,
    string ResponseFormat,
    double Speed,
    int MaxCharsPerRequest);

public sealed record UpdateAiSettingsRequest(
    string? ApiKey,
    bool ClearApiKey,
    string Model,
    string ChatModel,
    string ImageModel,
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

public sealed record AiImageRequest(
    string Prompt,
    string? Model = null,
    int Width = 768,
    int Height = 1024,
    string Format = "webp");

public sealed record AiImageResult(byte[] Data, string MimeType);

public interface IAiService
{
    Task<AiSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<AiSettingsDto> UpdateSettingsAsync(UpdateAiSettingsRequest request, CancellationToken cancellationToken = default);
    Task<AiTestResult> TestConnectionAsync(AiTestRequest request, CancellationToken cancellationToken = default);
    Task<byte[]> SynthesizeSpeechAsync(string text, CancellationToken cancellationToken = default);
    Task<string> CompleteChatAsync(AiChatRequest request, CancellationToken cancellationToken = default);
}

public interface IAiImageService
{
    Task<AiImageResult> GenerateImageAsync(AiImageRequest request, CancellationToken cancellationToken = default);
}
