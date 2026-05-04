namespace AskNightingale.Services.Llm;

// Anything that can take a prompt and return a completion. Swappable
// behind this interface so the chat pipeline doesn't depend on a
// specific LLM provider (e.g. Anthropic API direct vs Bedrock).
public interface ILlmProvider
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}
