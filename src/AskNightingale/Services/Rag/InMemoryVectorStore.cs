using System.Text.Json;

namespace AskNightingale.Services.Rag;

/// <summary>Persisted shape on disk. Public so System.Text.Json can serialise it via reflection.</summary>
public record VectorStoreEntry(Chunk Chunk, float[] Embedding);

/// <summary>
/// Single-book RAG store. ~150 chunks × ~1024 floats fits trivially in memory;
/// cosine over the lot per query is well under a millisecond. JSON persistence
/// skips re-embedding on app restart.
///
/// <br/><br/>Thread safety: writes happen once at boot; reads happen per chat
/// request. They never overlap, so no locking. To productionise, swap for
/// OpenSearch or Bedrock KB behind the same <see cref="IVectorStore"/> interface.
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
