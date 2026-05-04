using AskNightingale.Services.Embeddings;
using AskNightingale.Services.Guardrails;
using AskNightingale.Services.Llm;
using AskNightingale.Services.Prompts;
using AskNightingale.Services.Rag;

namespace AskNightingale.Services;

public class LlmChatService(
    ILlmProvider llm,
    IEmbeddingProvider embedder,
    IVectorStore store,
    RetrievalGuard retrievalGuard,
    InputGuard inputGuard,
    OutputJudge outputJudge) : IChatService
{
    private const int TopK = 4;
    private const int CitationSnippetMaxChars = 150;
    private const string Refusal = "I can only answer questions covered by Notes on Nursing.";

    public async Task<ChatResponse> RespondAsync(string userMessage, CancellationToken ct = default)
    {
        if (inputGuard.ShouldRefuse(userMessage))
        {
            return new ChatResponse(Refusal, []);
        }

        // Query mode improves Voyage's retrieval quality vs Document mode used for indexing.
        var queryEmbeddings = await embedder.EmbedAsync([userMessage], EmbeddingPurpose.Query, ct);
        var results = await store.GetTopKAsync(queryEmbeddings[0], TopK, ct);

        // Uncomment for tuning the retrieval threshold against the eval set.
        // Console.WriteLine($"[retrieval] {results.Count} chunks, scores=[{string.Join(", ", results.Select(r => r.Score.ToString("F2")))}]");

        if (retrievalGuard.ShouldRefuse(results))
        {
            return new ChatResponse(Refusal, []);
        }

        var context = string.Join("\n\n",
            results.Select(r => $"[Section {r.Chunk.Index}]\n{r.Chunk.Text}"));

        var response = await llm.CompleteAsync(new LlmRequest(
            SystemPrompt: GroundedSystemPrompt.Build(context),
            Messages: [new LlmMessage("user", userMessage)]
        ), ct);

        var verdict = await outputJudge.VerifyAsync(userMessage, context, response.Content, ct);
        if (!verdict.Approved)
        {
            return new ChatResponse(Refusal, []);
        }

        var citations = results
            .Select(r => new Citation(
                r.Chunk.Index,
                Truncate(r.Chunk.Text, CitationSnippetMaxChars),
                r.Score))
            .ToArray();

        return new ChatResponse(response.Content, citations);
    }

    private static string Truncate(string text, int maxChars)
        => text.Length <= maxChars ? text : text[..maxChars].TrimEnd() + "…";
}
