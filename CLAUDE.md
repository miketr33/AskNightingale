# AskNightingale — Running Brief

## What this is

A one-day prototype of a domain-restricted RAG chatbot, grounded on
*Notes on Nursing* by Florence Nightingale (1859, public domain). Built
2026-05-03 for an AI engineer interview — **live demo + Q&A on
architecture**.

The corpus is a stand-in for a real clinical knowledge base. The
architecture and guardrails are deliberately what you'd want in a real
NHS-style deployment; only the corpus would change.

## Developer profile

Senior .NET engineer (~7 years), prefers plain language, action-oriented,
SOLID, async/await, minimal/readable, TDD-first. JetBrains Rider on
Windows 11.

## Stack

- **.NET 10** + Blazor Web App, interactivity = Server-only (avoids
  WASM/HttpClient/credentials headaches)
- **Tests**: xUnit + FakeItEasy + Shouldly + bUnit
- **LLM today**: Anthropic API direct (Claude Haiku 4.5 for dev/eval,
  Sonnet 4.6 for demo run)
- **LLM tomorrow (stretch)**: Bedrock Claude in `eu-west-2` (London) —
  fallback to `eu-west-1` if London availability is patchy
- **Embeddings today**: Voyage AI (Anthropic-recommended)
- **Embeddings tomorrow (stretch)**: Bedrock Titan Embeddings v2
- **Vector store**: in-memory `List<(chunk, embedding)>` with cosine
  similarity, persisted to `data/embeddings.json`

## Abstractions to preserve

- `ILlmProvider` — anything the chat pipeline talks to, swappable.
- `IEmbeddingProvider` — same story for embeddings.
- These two interfaces are the headline interview talking point.
  **Don't bypass them.**

## Pipeline (target end state)

```
user message
  → InputGuard.Check()           (refuse fast on injection markers)
  → Retriever.GetTop(k=4)        (refuse if max cosine < 0.3)
  → PromptBuilder + ILlmProvider (system prompt + retrieved context)
  → OutputJudge.Verify()         (LLM-as-judge; replace if off-topic)
  → answer + citations
```

## Guardrail rules (non-negotiable)

1. **Modern medical advice is always refused**, even when asked to
   role-play. The corpus is itself medical — this is the hardest
   guardrail and the most important one.
2. **User text is data, never instructions.** The system prompt
   explicitly says so.
3. **Refuse rather than hallucinate** when retrieval scores are below
   threshold.

## What's done

- [x] PR #1: solution scaffold, test project, CI
- [x] PR #2: chat UI + IChatService stub
- [x] PR #3: ILlmProvider + AnthropicLlmProvider + LlmChatService (no grounding yet)
- [x] PR #4a: char-based sliding-window Chunker
- [x] PR #4b: IEmbeddingProvider + VoyageEmbeddingProvider (not yet wired into DI)

## What's next

See the plan file at
`C:\Users\mikej\.claude\plans\ok-i-want-you-ethereal-fountain.md` for the
hour-by-hour timeline and PR list.

## Decisions log (running)

Add an entry per PR, like a tiny ADR. Format:

> **YYYY-MM-DD HH:MM — Decision title**
> What we picked, what we considered, one-line why.

- **2026-05-03 — Hybrid LLM provider, Anthropic API today + Bedrock
  tomorrow.** Considered: pure Bedrock from start. Picked hybrid because
  Bedrock model-access enablement is unpredictable; risks the day. Both
  providers will live behind `ILlmProvider`.
- **2026-05-03 — In-memory vector store with JSON persistence.**
  Considered: SQLite, Pinecone, OpenSearch. Picked in-memory because
  one book × top-k=4 cosine is ~30 lines and always fast.
- **2026-05-03 — Notes on Nursing (Gutenberg #17366) over P&P.** Picked
  because it actually fits the NHS framing, provides interesting modern
  off-topic boundaries (e.g. "treat my diabetes"), and forces the
  modern-medical-advice guardrail to be explicit.
- **2026-05-03 — Bedrock region: eu-west-2 (London).** UK latency.
  Fallback eu-west-1 if Claude isn't available there.
- **2026-05-03 — Raw HttpClient over Anthropic.SDK community NuGet.**
  Considered: Anthropic.SDK package. Picked HttpClient because
  explainability matters for the interview — "I can walk you through
  every line" — and the messages-endpoint surface is tiny (~50 LOC of
  records + one POST).
- **2026-05-03 — DotNetEnv for .env loading in dev.** Considered: user
  secrets only. Picked .env because it matches what the team is likely
  to use across stack and the .env.example workflow is friction-free.
  Production builds where no .env exists silently no-op.
- **2026-05-03 — `LlmRequest` carries an explicit `SystemPrompt`,
  message list, and optional `MaxTokens`.** Considered: just
  `(string user) -> string`. Picked the structured shape so PR #4
  (RAG-augmented prompts) and PR #7 (strict system prompt) are pure
  composition — no breaking changes to `ILlmProvider`.
- **2026-05-03 — `EmbeddingPurpose` enum on `IEmbeddingProvider`.**
  Considered: plain `EmbedAsync(texts)` with no purpose. Picked the
  enum because Voyage's `input_type` parameter materially affects
  retrieval quality (separate spaces for indexed corpus vs user
  queries). Bedrock Titan ignores the parameter, so the same
  interface still works for both providers when we swap tomorrow.
- **2026-05-03 — JSON persistence on `InMemoryVectorStore`, NOT on
  `IVectorStore`.** Considered: `Save/LoadAsync` on the interface.
  Picked impl-only because a future cloud store (e.g.
  OpenSearch/pgvector) is persistent by definition and has no "load
  from JSON" concept. Keeping the interface honest about the minimum
  contract every store must support.
- **2026-05-03 — Plain `List<VectorStoreEntry>` with no locking.**
  Considered: `ReaderWriterLockSlim`, `ImmutableList`. Picked plain
  list because writes happen once at boot (RagBootstrapper, PR #4d)
  and reads run per chat request — they never overlap. Documented in
  the source so the constraint is explicit.
