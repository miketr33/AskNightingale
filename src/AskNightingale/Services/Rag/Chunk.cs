namespace AskNightingale.Services.Rag;

public record Chunk(int Index, string Text, int StartCharOffset);
