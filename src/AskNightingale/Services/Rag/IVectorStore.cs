namespace AskNightingale.Services.Rag;

public interface IVectorStore
{
    Task AddAsync(IReadOnlyList<(Chunk Chunk, float[] Embedding)> entries, CancellationToken ct = default);
    Task<IReadOnlyList<RetrievalResult>> GetTopKAsync(float[] queryEmbedding, int k, CancellationToken ct = default);
    int Count { get; }
}

public record RetrievalResult(Chunk Chunk, float Score);