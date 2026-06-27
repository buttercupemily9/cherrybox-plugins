using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CherryBox.Ai.Plugin;

internal sealed class VeniceImageClient
{
    private const string ImageUrl = "https://api.venice.ai/api/v1/image/generate";
    private readonly HttpClient _http;

    public VeniceImageClient(HttpClient http) => _http = http;

    public async Task<(byte[] Data, string MimeType)> GenerateAsync(
        string apiKey,
        string model,
        string prompt,
        int width,
        int height,
        string format,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Venice API key is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, ImageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = JsonContent.Create(new VeniceImageRequest
        {
            Model = string.IsNullOrWhiteSpace(model) ? "venice-sd35" : model.Trim(),
            Prompt = prompt.Trim(),
            Width = Math.Clamp(width, 256, 2048),
            Height = Math.Clamp(height, 256, 2048),
            Format = string.IsNullOrWhiteSpace(format) ? "webp" : format.Trim().ToLowerInvariant(),
            ReturnBinary = true
        });

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(body)
                    ? $"Venice image generation failed ({(int)response.StatusCode})."
                    : $"Venice image generation failed ({(int)response.StatusCode}): {body}");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var binary = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (binary.Length == 0)
                throw new InvalidOperationException("Venice image generation returned an empty image.");

            return (binary, contentType);
        }

        var payload = await response.Content.ReadFromJsonAsync<VeniceImageResponse>(cancellationToken);
        var base64 = payload?.Images?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(base64))
            throw new InvalidOperationException("Venice image generation returned no image data.");

        var data = Convert.FromBase64String(base64);
        var mimeType = GuessMimeType(format);
        return (data, mimeType);
    }

    private static string GuessMimeType(string format) =>
        format.Trim().ToLowerInvariant() switch
        {
            "png" => "image/png",
            "jpeg" or "jpg" => "image/jpeg",
            _ => "image/webp"
        };

    private sealed class VeniceImageRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; } = "webp";

        [JsonPropertyName("return_binary")]
        public bool ReturnBinary { get; set; }
    }

    private sealed class VeniceImageResponse
    {
        [JsonPropertyName("images")]
        public List<string>? Images { get; set; }
    }
}
