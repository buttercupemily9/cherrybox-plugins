using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CherryBox.Ai.Plugin;

internal sealed class VeniceChatClient
{
    private const string ChatUrl = "https://api.venice.ai/api/v1/chat/completions";
    private readonly HttpClient _http;

    public VeniceChatClient(HttpClient http) => _http = http;

    public async Task<string> CompleteAsync(
        string apiKey,
        string model,
        string? systemPrompt,
        string userPrompt,
        int? maxTokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Venice API key is not configured.");

        var messages = new List<VeniceChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new VeniceChatMessage { Role = "system", Content = systemPrompt.Trim() });
        messages.Add(new VeniceChatMessage { Role = "user", Content = userPrompt.Trim() });

        using var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = JsonContent.Create(new VeniceChatRequest
        {
            Model = string.IsNullOrWhiteSpace(model) ? "venice-uncensored" : model.Trim(),
            Messages = messages,
            MaxTokens = maxTokens,
            VeniceParameters = new VeniceChatParameters { IncludeVeniceSystemPrompt = false }
        });

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(body)
                    ? $"Venice chat failed ({(int)response.StatusCode})."
                    : $"Venice chat failed ({(int)response.StatusCode}): {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<VeniceChatResponse>(cancellationToken);
        var text = payload?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Venice chat returned an empty response.");

        return text;
    }

    private sealed class VeniceChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public List<VeniceChatMessage> Messages { get; set; } = [];

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("venice_parameters")]
        public VeniceChatParameters? VeniceParameters { get; set; }
    }

    private sealed class VeniceChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private sealed class VeniceChatParameters
    {
        [JsonPropertyName("include_venice_system_prompt")]
        public bool IncludeVeniceSystemPrompt { get; set; }
    }

    private sealed class VeniceChatResponse
    {
        [JsonPropertyName("choices")]
        public List<VeniceChatChoice>? Choices { get; set; }
    }

    private sealed class VeniceChatChoice
    {
        [JsonPropertyName("message")]
        public VeniceChatMessage? Message { get; set; }
    }
}
