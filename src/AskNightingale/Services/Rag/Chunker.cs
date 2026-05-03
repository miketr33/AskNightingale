namespace AskNightingale.Services.Rag;

// Char-based sliding-window chunker.
//
// ~2000 chars ≈ ~500 tokens for English prose, which is a sensible retrieval
// unit for top-k=4 RAG. ~200 chars overlap (~50 tokens) gives the embedding
// enough left-context that semantic meaning isn't sliced at chunk seams.
//
// Char-based (not token-based) keeps the project free of a tokenizer
// dependency. The trade-off: chunk sizes vary slightly when token density
// varies (numbers, code, etc.). Acceptable for natural-language prose.
public class Chunker(int chunkSize = 2000, int overlap = 200)
{
    public IReadOnlyList<Chunk> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (overlap < 0 || overlap >= chunkSize)
            throw new ArgumentOutOfRangeException(nameof(overlap), "Must be 0 ≤ overlap < chunkSize");

        var chunks = new List<Chunk>();
        var step = chunkSize - overlap;
        var index = 0;
        var i = 0;

        while (i < text.Length)
        {
            var end = Math.Min(i + chunkSize, text.Length);
            chunks.Add(new Chunk(index++, text[i..end], i));
            if (end == text.Length) break;
            i += step;
        }

        return chunks;
    }
}
