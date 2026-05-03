namespace AskNightingale.Services;

// Stub implementation. Replaced in PR #3 with an LLM-backed service.
public class EchoChatService : IChatService
{
    public Task<string> RespondAsync(string userMessage, CancellationToken ct = default)
        => Task.FromResult($"echo: {userMessage}");
}
