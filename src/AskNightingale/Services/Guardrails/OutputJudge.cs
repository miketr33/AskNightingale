using AskNightingale.Services.Llm;

namespace AskNightingale.Services.Guardrails;

/// <summary>
/// Layer 4 guardrail: LLM-as-judge. Runs AFTER the main answer is generated
/// and verifies semantic properties the cheaper layers can't:
/// <br/>- Answer is supported by retrieved CONTEXT (no fabrication)
/// <br/>- Answer is relevant to the QUESTION (catches off-topic slip-through)
/// <br/>- Any quoted span appears verbatim in CONTEXT (closes the
///     hallucinated-quotation gap PR #7's prompt only asked nicely about)
///
/// <br/><br/>Cost: one extra LLM call per chat request — doubles per-message API
/// spend. For a single-user demo this is fine; for production we'd batch,
/// use a cheaper model, or skip on cached/short responses.
/// </summary>
public class OutputJudge(ILlmProvider llm)
{
    private const string PromptTemplate =
        """
        You are a verification judge for a domain-restricted chatbot grounded in Notes on Nursing by Florence Nightingale.

        QUESTION:
        {QUESTION}

        CONTEXT (the only source the answer should draw from):
        {CONTEXT}

        CANDIDATE ANSWER:
        {ANSWER}

        Verify ALL of the following:
        1. Every factual claim in the answer is supported by the CONTEXT.
        2. The answer is relevant to the QUESTION.
        3. Any text in double quotes within the answer appears character-for-character in the CONTEXT (treat newlines and surrounding whitespace as flexible — match on the words and order).
        4. The answer does not give modern medical advice.

        Respond with EXACTLY one of these two formats and nothing else:
        APPROVE
        REFUSE: <one short sentence reason>
        """;

    public async Task<JudgeVerdict> VerifyAsync(
        string question, string context, string answer, CancellationToken ct = default)
    {
        var prompt = PromptTemplate
            .Replace("{QUESTION}", question)
            .Replace("{CONTEXT}", context)
            .Replace("{ANSWER}", answer);

        var resp = await llm.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", prompt)],
            MaxTokens: 200
        ), ct);

        var text = resp.Content.Trim();
        if (text.StartsWith("APPROVE", StringComparison.OrdinalIgnoreCase))
            return new JudgeVerdict(true, null);

        var reason = text.StartsWith("REFUSE:", StringComparison.OrdinalIgnoreCase)
            ? text[7..].Trim()
            : text;
        return new JudgeVerdict(false, reason);
    }
}

public record JudgeVerdict(bool Approved, string? Reason);