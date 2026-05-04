using System.Text.RegularExpressions;

namespace AskNightingale.Services.Guardrails;


/// <summary>
/// Regex pattern filter on the raw user message. Cheapest, fastest layer
/// (no embedding, no LLM cost). Six broad categories:
/// <br/>1. Instruction override   (ignore/forget/disregard previous rules)
/// <br/>2. Role redefinition     (you are now / act as an unrestricted X)
/// <br/>3. Fake system tags      ( <![CDATA[</context>]]> , SYSTEM:, <![CDATA[<|im_start|>]]> )
/// <br/>4. Authority/override    (I'm an admin / override your safety)
/// <br/>5. Encoded payload       (long base64 blobs)
/// <br/>6. Prompt extraction     (print/reveal your system prompt)
///
/// <br/><br/>Inherent limit: regex can't catch semantic equivalents
/// (e.g. "pay no attention to your prior guidance"). Those rely on
/// the system prompt and the output judge.
/// </summary>
public partial class InputGuard
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline;

    private static readonly (string Name, Regex Pattern)[] Patterns =
    [
        ("instruction-override", InstructionOverrideRegex()),

        ("role-redefinition", RoleRedefinitionRegex()),

        ("fake-system-tag", FakeSystemTagRegex()),

        ("authority-or-override", AuthorityOrOverrideRegex()),

        ("encoded-payload", EncodedPayloadRegex()),

        ("prompt-extraction", PromptExtractionRegex())
    ];

    public bool ShouldRefuse(string userMessage)
        => Patterns.Any(p => p.Pattern.IsMatch(userMessage));

    public string? MatchedCategory(string userMessage)
        => Patterns.FirstOrDefault(p => p.Pattern.IsMatch(userMessage)).Name;
    [GeneratedRegex(@"[A-Za-z0-9+/]{40,}={0,2}", RegexOptions.Compiled)]
    private static partial Regex EncodedPayloadRegex();
    [GeneratedRegex(@"\b(print|show|display|reveal|tell\s+me|share|leak|expose|repeat|output|dump|what'?s|what\s+is)\b.{0,40}\b(your|the)\s+(full\s+|exact\s+|complete\s+|original\s+|initial\s+|entire\s+)?(system\s+)?(prompt|instructions?|directives?|configuration)\b", Options, "en-GB")]
    private static partial Regex PromptExtractionRegex();
    [GeneratedRegex(@"(\b(i[''']?m|i\s+am|as\s+an?)\s+(an?\s+|the\s+)?(admin|administrator|developer|operator|moderator|owner|creator|root|superuser)\b)|(\b(override|bypass|disable|circumvent|nullify|deactivate|turn\s+off)\b.{0,30}\b(guardrails?|safety|protections?|restrictions?|filters?|safeguards?)\b)", Options, "en-GB")]
    private static partial Regex AuthorityOrOverrideRegex();
    [GeneratedRegex(@"</?\s*(system|context|instructions?|rules?|prompt|admin|user|assistant)\s*>|<\|im_start\|>|<\|im_end\|>|^\s*SYSTEM\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.Singleline, "en-GB")]
    private static partial Regex FakeSystemTagRegex();
    [GeneratedRegex(@"\b(you\s+are|act\s+as|pretend|behave|roleplay|role\s+play|simulate|from\s+now\s+on|imagine\s+you'?re|imagine\s+you\s+are)\b.{0,40}\b(unrestricted|jailbroken|uncensored|different|new|admin|developer|debug|evil|dan|opposite|unfiltered)\b", Options, "en-GB")]
    private static partial Regex RoleRedefinitionRegex();
    [GeneratedRegex(@"\b(ignore|forget|disregard|bypass|skip|override|void|cancel|disobey|dismiss|abandon)\b.{0,40}\b(previous|prior|earlier|all|any|the|your|original|initial|above|system)\b.{0,40}\b(instructions?|rules?|prompts?|directives?|guidelines?|constraints?|descriptions?|training|programming)\b", Options, "en-GB")]
    private static partial Regex InstructionOverrideRegex();
}