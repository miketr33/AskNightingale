using System.Text.Json;

namespace AskNightingale.Services.Rag;

/// <summary>
/// Persisted shape on disk. Public so System.Text.Json can serialise it
/// via reflection without setup ceremony.
/// </summary>
public record VectorStoreEntry(Chunk Chunk, float[] Embedding);


/// <summary>
/// Single-book RAG store. ~150 chunks × ~1024 floats fits trivially in
/// memory; cosine over the lot per query is ~150k float ops, well under
/// a millisecond. JSON persistence skips re-embedding on app restart.
///
/// <br/><br/>Thread safety: writes happen once at boot via RagBootstrapper (PR #4d),
/// reads happen per chat request. We accept the no-lock implementation
/// because writes never overlap with reads in practice.
/// <br/><br/>If we were to productionise, we may consider OpenSearch or AWS Bedrock KB
/// to store the vectors we need to be able to query to find similar vectors in a data base.
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly List<VectorStoreEntry> _entries = new();

    public int Count => _entries.Count;

    public Task AddAsync(
        IReadOnlyList<(Chunk Chunk, float[] Embedding)> entries,
        CancellationToken ct = default)
    {
        foreach (var (chunk, embedding) in entries)
        {
            _entries.Add(new VectorStoreEntry(chunk, embedding));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RetrievalResult>> GetTopKAsync(
        float[] queryEmbedding,
        int k,
        CancellationToken ct = default)
    {
        if (k <= 0 || _entries.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RetrievalResult>>([]);
        }

        var scored = _entries
            .Select(e => new RetrievalResult(e.Chunk, CosineSimilarity(queryEmbedding, e.Embedding)))
            .OrderByDescending(r => r.Score)
            .Take(k)
            .ToArray();

        return Task.FromResult<IReadOnlyList<RetrievalResult>>(scored);
    }

    public async Task SaveToAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, _entries, cancellationToken: ct);
    }

    public async Task LoadFromAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var loaded = await JsonSerializer.DeserializeAsync<List<VectorStoreEntry>>(stream, cancellationToken: ct)
            ?? throw new InvalidOperationException($"Failed to deserialise vector store from {path}.");
        _entries.Clear();
        _entries.AddRange(loaded);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vector dimensions must match.");

        float dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return denom == 0 ? 0 : dot / denom;
    }
}
