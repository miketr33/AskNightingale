using AskNightingale.Services.Llm;

namespace AskNightingale.Services;

public class LlmChatService(ILlmProvider llm) : IChatService
{
    public async Task<string> RespondAsync(string userMessage, CancellationToken ct = default)
    {
        // No grounding or guardrails yet. PR #4 adds RAG retrieval, PR #7
        // adds the strict system prompt that locks the bot to Notes on Nursing.
        var response = await llm.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", userMessage)]
        ), ct);
        return response.Content;
    }
}
