namespace AskNightingale.Services.Llm;

// The headline abstraction: anything that can take a prompt
// and return a completion. Today we ship Anthropic API direct; tomorrow
// the same chat pipeline runs on Bedrock by swapping the registration.
public interface ILlmProvider
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}
