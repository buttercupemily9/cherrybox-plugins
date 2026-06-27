using CherryBox.Plugins.Abstractions;

namespace CherryBox.Ai.Plugin;

internal sealed class AiService : IAiService, IAiImageService
{
    private readonly AiSettingsStore _settings;
    private readonly VeniceTtsClient _venice;
    private readonly VeniceChatClient _chat;
    private readonly VeniceImageClient _images;

    public AiService(
        AiSettingsStore settings,
        VeniceTtsClient venice,
        VeniceChatClient chat,
        VeniceImageClient images)
    {
        _settings = settings;
        _venice = venice;
        _chat = chat;
        _images = images;
    }

    public Task<AiSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ToDto(_settings.Get()));

    public Task<AiSettingsDto> UpdateSettingsAsync(UpdateAiSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var updated = _settings.Update(settings =>
        {
            if (request.ClearApiKey)
                settings.ApiKey = null;
            else if (!string.IsNullOrWhiteSpace(request.ApiKey))
                settings.ApiKey = request.ApiKey.Trim();

            settings.Model = string.IsNullOrWhiteSpace(request.Model) ? "tts-kokoro" : request.Model.Trim();
            settings.ChatModel = string.IsNullOrWhiteSpace(request.ChatModel) ? "venice-uncensored" : request.ChatModel.Trim();
            settings.ImageModel = string.IsNullOrWhiteSpace(request.ImageModel) ? "venice-sd35" : request.ImageModel.Trim();
            settings.Voice = string.IsNullOrWhiteSpace(request.Voice) ? "af_sky" : request.Voice.Trim();
            settings.ResponseFormat = string.IsNullOrWhiteSpace(request.ResponseFormat) ? "mp3" : request.ResponseFormat.Trim();
            settings.Speed = Math.Clamp(request.Speed, 0.25, 4.0);
            settings.MaxCharsPerRequest = Math.Clamp(request.MaxCharsPerRequest, 500, 12000);
        });

        return Task.FromResult(ToDto(updated));
    }

    public async Task<AiTestResult> TestConnectionAsync(AiTestRequest request, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Get();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return new AiTestResult(false, "Venice API key is not configured.");

        try
        {
            var sample = string.IsNullOrWhiteSpace(request.SampleText)
                ? "CherryBox story text to speech test."
                : request.SampleText.Trim();
            await _venice.SynthesizeAsync(settings.ApiKey, sample, settings, cancellationToken);
            return new AiTestResult(true, "Venice text-to-speech connection succeeded.");
        }
        catch (Exception ex)
        {
            return new AiTestResult(false, ex.Message);
        }
    }

    public async Task<byte[]> SynthesizeSpeechAsync(string text, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Get();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Venice API key is not configured.");

        return await _venice.SynthesizeAsync(settings.ApiKey, text, settings, cancellationToken);
    }

    public async Task<string> CompleteChatAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.UserPrompt))
            throw new InvalidOperationException("A user prompt is required.");

        var settings = _settings.Get();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Venice API key is not configured.");

        return await _chat.CompleteAsync(
            settings.ApiKey,
            settings.ChatModel,
            request.SystemPrompt,
            request.UserPrompt,
            request.MaxTokens,
            cancellationToken);
    }

    public async Task<AiImageResult> GenerateImageAsync(AiImageRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Prompt))
            throw new InvalidOperationException("An image prompt is required.");

        var settings = _settings.Get();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Venice API key is not configured.");

        var model = string.IsNullOrWhiteSpace(request.Model) ? settings.ImageModel : request.Model.Trim();
        var (data, mimeType) = await _images.GenerateAsync(
            settings.ApiKey,
            model,
            request.Prompt,
            request.Width,
            request.Height,
            request.Format,
            cancellationToken);

        return new AiImageResult(data, mimeType);
    }

    private static AiSettingsDto ToDto(AiSettings settings) => new(
        !string.IsNullOrWhiteSpace(settings.ApiKey),
        settings.Model,
        settings.ChatModel,
        settings.ImageModel,
        settings.Voice,
        settings.ResponseFormat,
        settings.Speed,
        settings.MaxCharsPerRequest);
}
