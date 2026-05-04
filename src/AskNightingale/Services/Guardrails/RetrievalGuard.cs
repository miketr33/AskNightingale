using AskNightingale.Services.Rag;

namespace AskNightingale.Services.Guardrails;


/// <summary>
/// Layer 2 guardrail: refuses BEFORE the LLM call when retrieval is too
/// weak to support an answer.
///
/// <br/><br/>Cheap (no LLM cost), fast (single max
/// comparison), and deterministic. Catches off-topic queries that
/// retrieve nothing relevant in the corpus.
///
/// <br/><br/>Threshold is configurable via RAG_MIN_SCORE env var (default 0.45).
/// Cosine similarity range is [-1, 1]; for Voyage normalised embeddings
/// in this corpus, eval set found:
/// <br/>- on-topic matches typically score 0.52-0.64
/// <br/>- adversarial with corpus vocab: 0.37-0.50(translate hypothetical 0.50
///   rp-continuation 0.46)
/// <br/>- off-topic 0.17-0.32.
/// <br/>The 0.45 default has been tuned from original 0.3 after eval set results
/// </summary>
public class RetrievalGuard
{
    public float MinScore { get; }

    public RetrievalGuard(IConfiguration config)
    {
        MinScore = float.TryParse(config["RAG_MIN_SCORE"], out var v) ? v : 0.45f;
    }

    public bool ShouldRefuse(IReadOnlyList<RetrievalResult> results)
    {
        if (results.Count == 0) return true;
        return results.Max(r => r.Score) < MinScore;
    }
}
