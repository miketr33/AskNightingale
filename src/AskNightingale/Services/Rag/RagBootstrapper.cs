using AskNightingale.Services.Embeddings;
using Microsoft.Extensions.Logging;

namespace AskNightingale.Services.Rag;

/// <summary>
/// One-shot corpus indexer, called at app startup. Idempotent:
/// already-populated store → no-op; persistence file exists → load it;
/// otherwise → read corpus, strip Gutenberg boilerplate, chunk, embed,
/// populate store, persist to disk.
///
/// <br/><br/>Takes concrete <see cref="InMemoryVectorStore"/> rather than
/// <see cref="IVectorStore"/> because it owns persistence (Save/LoadFromAsync),
/// which only the in-memory impl has.
/// </summary>
public class RagBootstrapper(
    Chunker chunker,
    IEmbeddingProvider embedder,
    InMemoryVectorStore store,
    IConfiguration config,
    ILogger<RagBootstrapper>? logger = null)
{
    private readonly string _corpusPath = config["RAG_CORPUS_PATH"] ?? "data/notes-on-nursing.txt";
    private readonly string _persistencePath = config["RAG_STORE_PATH"] ?? "data/embeddings.json";

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (store.Count > 0)
        {
            logger?.LogDebug("Vector store already populated ({Count} entries) — skipping bootstrap.", store.Count);
            return;
        }

        if (File.Exists(_persistencePath))
        {
            logger?.LogInformation("Loading persisted vector store from {Path}", _persistencePath);
            await store.LoadFromAsync(_persistencePath, ct);
            return;
        }

        logger?.LogInformation("No persisted store found; embedding corpus from {Path}", _corpusPath);
        var raw = await File.ReadAllTextAsync(_corpusPath, ct);
        var clean = StripGutenbergBoilerplate(raw);

        var chunks = chunker.Split(clean);
        if (chunks.Count == 0)
        {
            logger?.LogWarning("Chunker produced no chunks from {Path}", _corpusPath);
            return;
        }

        var embeddings = await embedder.EmbedAsync(
            chunks.Select(c => c.Text).ToArray(),
            EmbeddingPurpose.Document,
            ct);

        var entries = chunks
            .Zip(embeddings, (chunk, embedding) => (chunk, embedding))
            .ToArray();
        await store.AddAsync(entries, ct);

        var dir = Path.GetDirectoryName(_persistencePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await store.SaveToAsync(_persistencePath, ct);

        logger?.LogInformation("Indexed {Count} chunks from {Path}", chunks.Count, _corpusPath);
    }

    // Gutenberg files wrap content in "*** START OF…" / "*** END OF…" markers
    // with editorial preamble and licence outside them.
    private static string StripGutenbergBoilerplate(string text)
    {
        const string startMarker = "*** START OF";
        const string endMarker = "*** END OF";

        var startIdx = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIdx >= 0)
        {
            var newlineAfterStart = text.IndexOf('\n', startIdx);
            if (newlineAfterStart > 0) text = text[(newlineAfterStart + 1)..];
        }

        var endIdx = text.IndexOf(endMarker, StringComparison.Ordinal);
        if (endIdx > 0) text = text[..endIdx];

        return text.Trim();
    }
}
