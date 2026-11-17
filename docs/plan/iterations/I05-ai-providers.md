# I05 — AI provider layer (LM Studio / OpenRouter)

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I01 |
| **Parallel with** | I02, I03 |
| **Branch** | `iter/I05-ai-providers` |
| **Spec** | [06 AI §1](../../spec/06-ai-assistant.md#1-providers-and-profiles), [ADR-0002](../../adr/0002-ai-abstraction-meai.md) |
| **Requirements** | NFR-1, NFR-2, NFR-4, NFR-5 |

## Goal
`IChatClient` and `IEmbeddingGenerator` are registered from config profiles. Switching between LM Studio and OpenRouter is a config change. Calls are traced with token usage. Models for the project are chosen with a small benchmark.

## You will learn
LLM APIs (chat completions, streaming, tool calls, structured output, embeddings) on the wire; Microsoft.Extensions.AI middleware; model selection by measurement.

## Tasks
- [ ] `AiOptions` with chat and embedding profiles (spec 06 §1), validation on start.
- [ ] Factory that builds `IChatClient` / `IEmbeddingGenerator` from a profile (OpenAI client with custom endpoint); keyed services for named profiles + default from `Active*Profile`.
- [ ] Pipeline: OpenTelemetry, logging, usage tracking middleware (tokens, estimated cost from `Pricing`).
- [ ] A Development-only HTTP logging handler that shows raw requests / responses once (to see what goes over the wire).
- [ ] Health check: provider reachable, model listed (`GET /v1/models`).
- [ ] `tools/ai-playground` console (or Development endpoints): chat, streaming, tool call, JSON-schema output, embeddings + cosine similarity of a few en/ru/sv phrases.
- [ ] **Model benchmark** (documented in the Result section): 2–3 chat models in LM Studio × tasks (tool call correctness on 10 prompts, valid JSON on 10 prompts, Russian and Swedish quality, tokens/s); 2–3 embedding models × similarity sanity checks (ru ↔ en and sv ↔ en pairs). Pick the defaults.
- [ ] OpenRouter profile tested with one cheap model (key in user-secrets).
- [ ] Unit tests with a fake `IChatClient`.

## Out of scope
Agent loop (I11–I12), embedding storage (I06).

## Acceptance criteria
- [ ] Switching `ActiveChatProfile` from `local` to `openrouter` changes the model without code changes.
- [ ] Aspire dashboard shows a trace for an LLM call with model and token counts.
- [ ] Benchmark results and chosen models recorded; `appsettings.json` defaults updated.

## Open questions
- Does the chosen local model support parallel tool calls? Note it for I12.
