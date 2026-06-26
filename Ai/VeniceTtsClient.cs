using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CherryBox.Ai.Plugin;

internal sealed class VeniceTtsClient
{
    private const string SpeechUrl = "https://api.venice.ai/api/v1/audio/speech";
    private readonly HttpClient _http;

    public VeniceTtsClient(HttpClient http) => _http = http;

    public async Task<byte[]> SynthesizeAsync(
        string apiKey,
        string text,
        AiSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Venice API key is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, SpeechUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = JsonContent.Create(new VeniceSpeechRequest
        {
            Model = settings.Model,
            Input = text,
            Voice = settings.Voice,
            ResponseFormat = settings.ResponseFormat,
            Speed = settings.Speed
        });

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(body)
                    ? $"Venice TTS failed ({(int)response.StatusCode})."
                    : $"Venice TTS failed ({(int)response.StatusCode}): {body}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private sealed class VeniceSpeechRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("input")]
        public string Input { get; set; } = "";

        [JsonPropertyName("voice")]
        public string Voice { get; set; } = "";

        [JsonPropertyName("response_format")]
        public string ResponseFormat { get; set; } = "mp3";

        [JsonPropertyName("speed")]
        public double Speed { get; set; } = 1.0;
    }
}
