using System.Text.RegularExpressions;

namespace AskNightingale.Services.Guardrails;

// Layer 3 guardrail: regex pattern filter applied BEFORE retrieval and
// LLM call. Cheapest, fastest layer (no embedding, no LLM cost). Catches
// obvious adversarial patterns by signature.
//
// Six broad categories — each one regex covering many phrasings:
//   1. Instruction override (ignore/forget/disregard previous rules)
//   2. Role redefinition   (you are now / act as an unrestricted X)
//   3. Fake system tags    (</context>, SYSTEM:, <|im_start|>)
//   4. Authority/override  (I'm an admin / override your safety)
//   5. Encoded payload     (long base64 blobs)
//   6. Prompt extraction   (print/reveal your system prompt)
//
// Inherent limit: regex can't catch semantic equivalents. "Pay no
// attention to your prior guidance" won't match — that's PR #10
// (LLM-as-judge) territory. The input filter's job is to fail cheap on
// the obvious surface; semantic verification lives in the next layer.
public class InputGuard
{
    private static readonly RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline;

    private static readonly (string Name, Regex Pattern)[] Patterns =
    [
        ("instruction-override", new Regex(
            @"\b(ignore|forget|disregard|bypass|skip|override|void|cancel|disobey|dismiss|abandon)\b" +
            @".{0,40}" +
            @"\b(previous|prior|earlier|all|any|the|your|original|initial|above|system)\b" +
            @".{0,40}" +
            @"\b(instructions?|rules?|prompts?|directives?|guidelines?|constraints?|descriptions?|training|programming)\b",
            Options)),

        ("role-redefinition", new Regex(
            @"\b(you\s+are|act\s+as|pretend|behave|roleplay|role\s+play|simulate|from\s+now\s+on|imagine\s+you'?re|imagine\s+you\s+are)\b" +
            @".{0,40}" +
            @"\b(unrestricted|jailbroken|uncensored|different|new|admin|developer|debug|evil|dan|opposite|unfiltered)\b",
            Options)),

        ("fake-system-tag", new Regex(
            @"</?\s*(system|context|instructions?|rules?|prompt|admin|user|assistant)\s*>" +
            @"|<\|im_start\|>|<\|im_end\|>" +
            @"|^\s*SYSTEM\s*:",
            Options | RegexOptions.Multiline)),

        ("authority-or-override", new Regex(
            @"(\b(i[''']?m|i\s+am|as\s+an?)\s+(an?\s+|the\s+)?(admin|administrator|developer|operator|moderator|owner|creator|root|superuser)\b)" +
            @"|" +
            @"(\b(override|bypass|disable|circumvent|nullify|deactivate|turn\s+off)\b.{0,30}\b(guardrails?|safety|protections?|restrictions?|filters?|safeguards?)\b)",
            Options)),

        ("encoded-payload", new Regex(
            @"[A-Za-z0-9+/]{40,}={0,2}",
            RegexOptions.Compiled)),

        ("prompt-extraction", new Regex(
            @"\b(print|show|display|reveal|tell\s+me|share|leak|expose|repeat|output|dump|what'?s|what\s+is)\b" +
            @".{0,40}" +
            @"\b(your|the)\s+(full\s+|exact\s+|complete\s+|original\s+|initial\s+|entire\s+)?(system\s+)?(prompt|instructions?|directives?|configuration)\b",
            Options))
    ];

    public bool ShouldRefuse(string userMessage)
        => Patterns.Any(p => p.Pattern.IsMatch(userMessage));

    public string? MatchedCategory(string userMessage)
        => Patterns.FirstOrDefault(p => p.Pattern.IsMatch(userMessage)).Name;
}