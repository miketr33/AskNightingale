namespace AskNightingale.Services;

public interface IChatService
{
    Task<string> RespondAsync(string userMessage, CancellationToken ct = default);
}
