# Guardrails

Defence-in-depth around the chat pipeline. Four layers, applied in
pipeline order:

| Layer | Lives in | Mechanism |
|---|---|---|
| 1. System prompt | [`../Prompts/GroundedSystemPrompt.cs`](../Prompts/GroundedSystemPrompt.cs) | Instruction text **inside** the LLM call — tells the model how to behave |
| 2. Retrieval threshold | [`RetrievalGuard.cs`](RetrievalGuard.cs) | Deterministic gate **before** LLM call — refuses if `max(cosine) < 0.45` |
| 3. Input pre-filter | [`InputGuard.cs`](InputGuard.cs) | Deterministic gate **before everything** — regex categories on raw user input |
| 4. Output judge | [`OutputJudge.cs`](OutputJudge.cs) | LLM-as-judge gate **after** LLM call — semantic verification of answer |

## Why Layer 1 lives in `Prompts/`, not here

Layers 2-4 are *gates* — code that wraps the LLM call. Layer 1 is
*guidance* — text that goes inside the LLM call.

- `Prompts/` holds **what we tell the model**.
- `Guardrails/` holds **what wraps the model**.

Different mechanism, different folder. The four-layer architecture is
real; the folder split reflects how each layer operates.
