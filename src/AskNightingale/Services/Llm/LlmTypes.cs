namespace AskNightingale.Services.Llm;

public record LlmMessage(string Role, string Content);

public record LlmRequest(
    string? SystemPrompt,
    IReadOnlyList<LlmMessage> Messages,
    int? MaxTokens = null
);

public record LlmResponse(
    string Content,
    string Model,
    int InputTokens,
    int OutputTokens
);
