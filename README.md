# AskNightingale

A grounded, domain-restricted RAG chatbot. One-day prototype demonstrating
the patterns you'd want for a real NHS-style information assistant — built
to be discussed, not just demoed.

The bot answers questions about Florence Nightingale's *Notes on Nursing*
(1859, public domain, [Project Gutenberg #17366](https://www.gutenberg.org/ebooks/17366))
and refuses everything else. The corpus is a stand-in: the architecture
and guardrails are exactly what a real hospital deployment would need —
**only the corpus would change**.

> The bot faithfully reflects 1859 nursing knowledge — miasma theory and
> all. For real clinical use you'd ground it on current guidelines, not a
> Victorian text. The bot **will refuse to give modern medical advice**
> even when asked to role-play.

---

## Architecture

```
                       ┌─────────────────────────┐
                       │  User chat input        │
                       └────────────┬────────────┘
                                    ▼
                       ┌─────────────────────────┐
   Layer 3 ─────────►  │  InputGuard             │  cheap regex on raw input.
   (deterministic,     │  6 broad categories     │  Categories: instruction-override,
    fastest)           │                         │  role-redefinition, fake-system-tag,
                       └────────────┬────────────┘  authority/override, base64,
                                    │ pass          prompt-extraction.
                                    ▼
                       ┌─────────────────────────┐
                       │  Voyage embedding       │  query mode
                       │  (single vector)        │
                       └────────────┬────────────┘
                                    ▼
                       ┌─────────────────────────┐
                       │  InMemoryVectorStore    │  cosine top-k=4
                       │  GetTopK                │  ~150 chunks × ~1024 floats
                       └────────────┬────────────┘
                                    ▼
                       ┌─────────────────────────┐
   Layer 2 ─────────►  │  RetrievalGuard         │  refuse if max(cosine) < 0.45
   (deterministic)     └────────────┬────────────┘
                                    │ pass
                                    ▼
                       ┌─────────────────────────┐
   Layer 1 ─────────►  │  GroundedSystemPrompt   │  6 explicit rules:
   (instruction text   │  + retrieved CONTEXT    │  source-isolation, no-modern-
    inside LLM call)   │                         │  medical, quotation honesty,
                       └────────────┬────────────┘  injection defence, citation
                                    ▼               requirement, prompt non-
                       ┌─────────────────────────┐  disclosure.
                       │  Anthropic Claude       │
                       │  Haiku 4.5 / Sonnet 4.6 │
                       └────────────┬────────────┘
                                    ▼
                       ┌─────────────────────────┐
   Layer 4 ─────────►  │  OutputJudge            │  LLM-as-judge: verifies grounded,
   (LLM-as-judge)      │  (second LLM call)      │  on-topic, no fabricated quotes.
                       │                         │  Refuses if any check fails.
                       └────────────┬────────────┘
                                    │ approve
                                    ▼
                       ┌─────────────────────────┐
                       │  Answer + Citations     │  citations show retrieved
                       │                         │  chunk index, score, snippet
                       └─────────────────────────┘
```

### Defence-in-depth

Four independent layers, each catching a different class of failure:

| # | Layer | Mechanism | Catches |
|---|---|---|---|
| 1 | System prompt | Instruction text in the LLM call | Modern medical advice (even role-played); fabricated quotes; instruction-injection in user/context |
| 2 | Retrieval threshold | Deterministic gate before LLM | Off-topic questions (no chunk close enough) |
| 3 | Input pre-filter | Regex on raw input | Obvious adversarial patterns: "ignore previous", base64 payloads, fake `<system>` tags |
| 4 | Output judge | Second LLM call on the answer | Semantic failures the deterministic layers miss: hallucinated facts, paraphrased fabricated quotes |

**Why each layer matters**: any single layer can fail. Layer 3 can't catch
paraphrased injections ("pay no attention to your prior guidance"). Layer 2
can't catch adversarial queries that share corpus vocabulary
("translate the chapter on ventilation"). Layer 1 is *guidance* the model
can ignore; Layer 4 catches that. **Each layer's failure modes are
independent — that's the value of layering.**

---

## Eval results

| Category | Pre-guardrails | Post-guardrails |
|---|---|---|
| On-topic (8) | 8/8 | **7/8** ¹ |
| Off-topic (5) | 5/5 | 5/5 |
| Prompt injection (7) | 6/7 | 7/7 |
| Jailbreak (7) | 6/7 | 7/7 |
| **Total** | **25/27 (92%)** | **26/27 (96%)** |

¹ The single post-guardrails fail (`on-topic-noise`) is a **judge
false-rejection**: the chat produced a faithful, on-topic answer about
noise management; the OutputJudge wrongly refused it; the user got the
canned refusal text. Documented as the inherent cost of LLM-as-judge.

**Honest reading of these numbers**: this is a frontier-aligned model on
a tightly-scoped corpus. Pre-guardrails was already strong because Claude
plus a minimal grounding prompt is well-aligned out of the box. The
guardrail layers exist for portability (when we swap to Bedrock or a
weaker model), defence-in-depth (each layer's failure modes independent),
and to catch adversarial probes the eval doesn't yet contain.

Both eval reports are committed in `evals/` for diffing.

### The headline insight

Cosine similarity measures *topical relatedness*, not *intent*. A
jailbreak phrased nursing-adjacent (*"translate the chapter on
ventilation into French"*) retrieves the same chunks as a legitimate
question about ventilation. Score distribution from instrumenting all 27
eval cases:

- On-topic: **0.52-0.64**
- Clearly off-topic: **0.17-0.32**
- Adversarial-with-corpus-vocabulary: **0.37-0.50**

A 0.02-wide gap between worst adversarial (0.50) and best on-topic floor
(0.52) — which is why the threshold is 0.45 (0.07 margin) and why
adversarial queries scoring 0.45+ must be caught by Layer 1 / 4, not
Layer 2.

---

## Run locally

```bash
cp .env.example .env
# Add ANTHROPIC_API_KEY and VOYAGE_API_KEY to .env
dotnet run --project src/AskNightingale
```

`.env` is loaded automatically at startup (gitignored). Defaults to
`claude-haiku-4-5`; override with `ANTHROPIC_MODEL` for the demo run.

First boot embeds the corpus (~5 seconds, ~£0.001 of Voyage credit).
Subsequent boots load `data/embeddings.json` from disk.

### Run tests

```bash
dotnet test
```

All deterministic; no API keys needed.

### Re-run the eval suite

```powershell
$env:RUN_EVAL=1
$env:EVAL_OUTPUT_FILE="results-post-guardrails.md"
dotnet test --filter "FullyQualifiedName~EvalRunner"
```

~£0.04 per full run (27 cases × main chat + judge call). Hits the live
APIs; gated behind `RUN_EVAL=1` so it doesn't fire on every `dotnet test`.

---

## Project structure

```
src/AskNightingale/
  Components/Pages/Chat.razor         — Blazor UI; bUnit-tested
  Services/
    IChatService.cs / ChatResponse.cs — pipeline contract
    LlmChatService.cs                 — orchestrator (the seam)
    Llm/
      ILlmProvider.cs                 — swappable LLM abstraction
      AnthropicLlmProvider.cs         — raw HttpClient, ~50 LOC
    Embeddings/
      IEmbeddingProvider.cs           — swappable embeddings abstraction
      VoyageEmbeddingProvider.cs      — Voyage v3
    Rag/
      Chunker.cs                      — sliding-window, char-based
      InMemoryVectorStore.cs          — cosine, JSON persistence
      RagBootstrapper.cs              — boot-time corpus indexer
    Prompts/
      GroundedSystemPrompt.cs         — Layer 1: 6 rules
    Guardrails/
      RetrievalGuard.cs               — Layer 2: cosine threshold
      InputGuard.cs                   — Layer 3: regex categories
      OutputJudge.cs                  — Layer 4: LLM-as-judge
  data/
    notes-on-nursing.txt              — corpus (Gutenberg #17366)
    embeddings.json                   — generated, gitignored
tests/AskNightingale.Tests/           — 80+ unit + integration tests
evals/
  cases.json                          — 27 adversarial eval cases
  results-pre-guardrails.md           — baseline before all 4 layers
  results-post-guardrails.md          — current state, all 4 layers active
CLAUDE.md                             — running brief + decision log
```

---

## Key decisions (full log in [`CLAUDE.md`](CLAUDE.md))

- **RAG over fine-tuning** — cheaper, citable, gives a natural grounding
  refusal signal, scales to multi-document.
- **Each guardrail is its own class** under `Services/Guardrails/` —
  architecture-diagram boxes map 1:1 to code files. Layer 1 lives in
  `Services/Prompts/` because it's instruction text, not a gate; see
  [`Services/Guardrails/README.md`](src/AskNightingale/Services/Guardrails/README.md).
- **Threshold tuned 0.3 → 0.45 from measured score distribution** —
  instrumented retrieval scores across all 27 eval cases; bumped from
  the conservative starting default after seeing the on-topic floor at
  0.52.
- **Heuristic eval rubric over LLM-as-judge for the rubric itself** —
  cheap, deterministic, brittle (false negatives possible). The
  *production* layer that needs semantic verification is Layer 4 and
  uses LLM-as-judge there.
- **Raw HttpClient over a community Anthropic SDK** — explainability
  matters for interview; the messages-endpoint surface is ~50 LOC of
  records + one POST.

---

## Known limitations (deliberate trade-offs)

- **OutputJudge can false-reject** — `on-topic-noise` in the
  post-guardrails eval is the live example. Trade-off: false refusals
  are recoverable (retry); false approvals are not. Production fixes
  would calibrate strictness against a labelled eval set.
- **InputGuard can't catch paraphrased injection** ("pay no attention
  to your prior guidance"). That's Layer 4's job. By design.
- **In-memory vector store doesn't scale** beyond a single small corpus
  / single instance. Production would swap for OpenSearch k-NN or
  pgvector behind the same `IVectorStore` interface.
- **Bedrock not wired** — design built behind `ILlmProvider` /
  `IEmbeddingProvider` so a `BedrockLlmProvider` is a one-class drop-in.
  Not done due to model-access enablement risk in a one-day timebox.
- **No live-deploy** — local-only demo by design. EC2/App Runner deploy
  scoped as stretch.

---

## What's NOT in scope

Auth, multi-user state, conversation memory beyond a 5-message window,
streaming responses, deploy automation, Pinecone/Postgres-pgvector,
fine-tuning. Each was actively considered and cut to keep the day's
output focused and explainable.
