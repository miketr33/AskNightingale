using AskNightingale.Services.Rag;

namespace AskNightingale.Services.Guardrails;


/// <summary>
/// Refuses before the LLM call when retrieval is too weak to support an answer.
/// Deterministic, cheap, fast (single max comparison).
///
/// <br/><br/>Threshold configurable via RAG_MIN_SCORE (default 0.45). Tuned from
/// the eval set: on-topic queries score 0.52-0.64, off-topic 0.17-0.32, and
/// adversarial-with-corpus-vocabulary 0.37-0.50. 0.45 sits in the gap with
/// 0.07 margin from the on-topic floor.
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
