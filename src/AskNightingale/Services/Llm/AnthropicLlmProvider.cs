using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AskNightingale.Services.Llm;

public class AnthropicLlmProvider(HttpClient http, IConfiguration config) : ILlmProvider
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    private readonly string _apiKey = config["ANTHROPIC_API_KEY"]
                                      ?? throw new InvalidOperationException(
                                          "ANTHROPIC_API_KEY is not configured. Set it in .env, user secrets, or environment.");
    private readonly string _model = config["ANTHROPIC_MODEL"] ?? "claude-haiku-4-5";

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var body = new AnthropicRequest(
            Model: _model,
            MaxTokens: request.MaxTokens ?? 500,
            System: request.SystemPrompt,
            Messages: request.Messages
                .Select(m => new AnthropicMessage(m.Role, m.Content))
                .ToArray()
        );

        using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        req.Content = JsonContent.Create(body);
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", ApiVersion);

        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var error = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Anthropic API returned {(int)resp.StatusCode}: {error}");
        }

        var parsed = await resp.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty response from Anthropic API.");

        var text = string.Concat(parsed.Content
            .Where(c => c.Type == "text")
            .Select(c => c.Text));

        return new LlmResponse(text, parsed.Model, parsed.Usage.InputTokens, parsed.Usage.OutputTokens);
    }

    private record AnthropicRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? System,
        [property: JsonPropertyName("messages")] AnthropicMessage[] Messages
    );

    private record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content
    );

    private record AnthropicResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("content")] AnthropicContent[] Content,
        [property: JsonPropertyName("usage")] AnthropicUsage Usage
    );

    private record AnthropicContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text
    );

    private record AnthropicUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens
    );
}
