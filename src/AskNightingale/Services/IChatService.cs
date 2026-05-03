namespace AskNightingale.Services;

public interface IChatService
{
    Task<ChatResponse> RespondAsync(string userMessage, CancellationToken ct = default);
}
