namespace AskNightingale.Services.Embeddings;

public enum EmbeddingPurpose
{
    // Indexing the source corpus — chunks being stored in the vector store.
    Document,
    // A user query being matched against the indexed corpus.
    Query
}

public interface IEmbeddingProvider
{
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        EmbeddingPurpose purpose,
        CancellationToken ct = default);
}
