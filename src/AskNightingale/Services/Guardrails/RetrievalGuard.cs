using AskNightingale.Services.Rag;

namespace AskNightingale.Services.Guardrails;

// Layer 2 guardrail: refuses BEFORE the LLM call when retrieval is too
// weak to support an answer. Cheap (no LLM cost), fast (single max
// comparison), and deterministic. Catches off-topic queries that
// retrieve nothing relevant in the corpus.
//
// Threshold is configurable via RAG_MIN_SCORE env var (default 0.3).
// Cosine similarity range is [-1, 1]; for Voyage normalised embeddings
// in this corpus, on-topic matches typically score 0.5-0.9, off-topic
// 0.0-0.3. The 0.3 default is a starting point — tunable post-hoc
// against the eval set.
public class RetrievalGuard
{
    public float MinScore { get; }

    public RetrievalGuard(IConfiguration config)
    {
        MinScore = float.TryParse(config["RAG_MIN_SCORE"], out var v) ? v : 0.3f;
    }

    public bool ShouldRefuse(IReadOnlyList<RetrievalResult> results)
    {
        if (results.Count == 0) return true;
        return results.Max(r => r.Score) < MinScore;
    }
}
