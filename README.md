# AskNightingale

A grounded, domain-restricted RAG chatbot. Built as a one-day prototype to
demonstrate the patterns you'd want for a real NHS-style information assistant.

The chatbot is grounded on Florence Nightingale's *Notes on Nursing* (1859,
public domain — [Project Gutenberg #17366](https://www.gutenberg.org/ebooks/17366)).
**Imagine this is a hospital information assistant** — the architecture and
guardrails are exactly what a real deployment would need; only the corpus
would change.

> **Note**: the chatbot faithfully reflects 1859 nursing knowledge — miasma
> theory of disease and all. For real clinical use you would ground it on
> current clinical guidelines, not a Victorian text. **The bot will refuse
> to give modern medical advice** even when asked to role-play.

## Status

Day 1 in progress. See `CLAUDE.md` for the running brief and decision log.

## Run locally

```bash
cp .env.example .env
# Add your ANTHROPIC_API_KEY and VOYAGE_API_KEY to .env
dotnet run --project src/AskNightingale
```

(End-to-end run-instructions land in PR #3 once the LLM is wired in.)

## Run tests

```bash
dotnet test
```

## Architecture

See `CLAUDE.md` for the full picture. In one line: input guard → retrieval
(Voyage embeddings, in-memory cosine) → prompt + Claude (Anthropic API
direct today, Bedrock tomorrow) → output judge → answer with citation.

## Why these choices

See `docs/decisions.md` (lands in PR #11) for the full decision log. Short
version:

- **RAG, not fine-tuning** — cheaper, citable, gives a natural refusal
  signal, scales to multi-document.
- **Hybrid LLM provider** — Anthropic API direct today (5-min setup),
  Bedrock tomorrow (AWS narrative + managed Guardrails option). Behind one
  `ILlmProvider` interface so the swap is one class.
- **In-memory vector store** — one book is ~50k words, persisted to a
  single JSON file. Pinecone/OpenSearch would be overkill.
- **Layered guardrails** — input filter, retrieval threshold, system
  prompt, output judge. Each layer explainable in code.
