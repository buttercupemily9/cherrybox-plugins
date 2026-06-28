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
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var text = body.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(body);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(text)
                    ? $"Venice image generation failed ({(int)response.StatusCode})."
                    : $"Venice image generation failed ({(int)response.StatusCode}): {text}");
        }

        if (TryReadJsonError(body, out var jsonError))
            throw new InvalidOperationException(jsonError);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            if (body.Length == 0)
                throw new InvalidOperationException("Venice image generation returned an empty image.");

            return (body, contentType);
        }

        if (body.Length > 0 && body[0] == '{')
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("images", out var images) &&
                images.ValueKind == System.Text.Json.JsonValueKind.Array &&
                images.GetArrayLength() > 0)
            {
                var base64 = images[0].GetString();
                if (!string.IsNullOrWhiteSpace(base64))
                {
                    var data = Convert.FromBase64String(base64);
                    return (data, GuessMimeType(format));
                }
            }

            if (TryReadJsonError(body, out jsonError))
                throw new InvalidOperationException(jsonError);
        }

        throw new InvalidOperationException("Venice image generation returned no image data.");
    }

    private static bool TryReadJsonError(byte[] body, out string message)
    {
        message = string.Empty;
        if (body.Length == 0 || body[0] != '{')
            return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                message = error.ValueKind == System.Text.Json.JsonValueKind.String
                    ? error.GetString() ?? "Venice image generation failed."
                    : error.ToString();
                return true;
            }

            if (root.TryGetProperty("message", out var messageProp) &&
                messageProp.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var text = messageProp.GetString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    text.Contains("terms", StringComparison.OrdinalIgnoreCase))
                {
                    message = text;
                    return true;
                }
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.TryGetProperty("message", out var itemMessage) &&
                        itemMessage.ValueKind == System.Text.Json.JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(itemMessage.GetString()))
                    {
                        message = itemMessage.GetString()!;
                        return true;
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }

        return false;
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
}
