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
        // Cheap refusal - no LLM involved here
        if (inputGuard.ShouldRefuse(userMessage))
        {
            return new ChatResponse(Refusal, []);
        }
        
        // 1. Embed the user's question (Query mode improves retrieval quality on Voyage).
        var queryEmbeddings = await embedder.EmbedAsync(
            [userMessage], EmbeddingPurpose.Query, ct);

        // 2. Find the closest k chunks of the corpus.
        var results = await store.GetTopKAsync(queryEmbeddings[0], TopK, ct);

        // Debug: Uncomment this line for tuning retrieval guard threshold. Outputs cosine score to terminal
        // Console.WriteLine($"[retrieval] {results.Count} chunks, scores=[{string.Join(", ", results.Select(r => r.Score.ToString("F2")))}]");
        
        
        // 3. PR #8 — Layer 2 guardrail: refuse before paying for the LLM
        //    when nothing in the corpus is close enough to ground an answer.
        //    Deterministic, cheap, fast. Complements the prompt-based
        //    refusal in Rule 1 of GroundedSystemPrompt for borderline cases.
        if (retrievalGuard.ShouldRefuse(results))
        {
            return new ChatResponse(Refusal, []);
        }

        // 4. Stitch the retrieved chunks into a CONTEXT block and prepend
        //    the hardened grounding rules (PR #7). PR #9 adds input filter;
        //    PR #10 adds output judge.
        var context = string.Join("\n\n",
            results.Select(r => $"[Section {r.Chunk.Index}]\n{r.Chunk.Text}"));

        var systemPrompt = GroundedSystemPrompt.Build(context);

        // 5. Call the LLM with the augmented prompt.
        var response = await llm.CompleteAsync(new LlmRequest(
            SystemPrompt: systemPrompt,
            Messages: [new LlmMessage("user", userMessage)]
        ), ct);
        
        // 6. PR #10 — Layer 4: judge verifies the answer is grounded, on-topic,
        //    and not quoting text outside the CONTEXT. Replaces the answer
        //    with a standard refusal if anything's off.
        var verdict = await outputJudge.VerifyAsync(userMessage, context, response.Content, ct);
        if (!verdict.Approved)
        {
            return new ChatResponse(Refusal, []);
        }

        // 7. Return answer + citations so the UI can surface the sources.
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
