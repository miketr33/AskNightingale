using AskNightingale.Services.Embeddings;
using AskNightingale.Services.Llm;
using AskNightingale.Services.Rag;

namespace AskNightingale.Services;

public class LlmChatService(
    ILlmProvider llm,
    IEmbeddingProvider embedder,
    IVectorStore store) : IChatService
{
    private const int TopK = 4;
    private const int CitationSnippetMaxChars = 150;

    public async Task<ChatResponse> RespondAsync(string userMessage, CancellationToken ct = default)
    {
        // 1. Embed the user's question (Query mode improves retrieval quality on Voyage).
        var queryEmbeddings = await embedder.EmbedAsync(
            [userMessage], EmbeddingPurpose.Query, ct);

        // 2. Find the closest k chunks of the corpus.
        var results = await store.GetTopKAsync(queryEmbeddings[0], TopK, ct);

        // 3. Stitch the retrieved chunks into a CONTEXT block. PR #7 hardens
        //    this prompt with the strict refusal/no-modern-medical-advice
        //    rules; PR #8 adds retrieval-threshold refusal upstream.
        var context = string.Join("\n\n",
            results.Select(r => $"[Section {r.Chunk.Index}]\n{r.Chunk.Text}"));

        var systemPrompt = $$"""
            Answer the user's question using ONLY the CONTEXT below from
            Notes on Nursing by Florence Nightingale. If the answer isn't
            in the context, say so plainly.

            CONTEXT:
            {{context}}
            """;

        // 4. Call the LLM with the augmented prompt.
        var response = await llm.CompleteAsync(new LlmRequest(
            SystemPrompt: systemPrompt,
            Messages: [new LlmMessage("user", userMessage)]
        ), ct);

        // 5. Return answer + citations so the UI can surface the sources.
        //    The user can verify any quoted span themselves — exposing
        //    hallucinated quotation, the failure mode PR #7 + #10 also tackle.
        var citations = results
            .Select(r => new Citation(
                r.Chunk.Index,
                Truncate(r.Chunk.Text, CitationSnippetMaxChars),
                r.Score))
            .ToArray();

        return new ChatResponse(response.Content, citations);
    }

    private static string Truncate(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..maxChars].TrimEnd() + "…";
    }
}
