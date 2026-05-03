namespace AskNightingale.Services;

public record ChatResponse(string Content, IReadOnlyList<Citation> Citations);

public record Citation(int ChunkIndex, string Snippet, float Score);
