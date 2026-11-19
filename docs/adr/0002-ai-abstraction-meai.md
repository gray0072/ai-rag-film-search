# ADR-0002 — Microsoft.Extensions.AI with OpenAI-compatible providers

> [Docs](../README.md) › [ADRs](README.md) › ADR-0002

- **Status:** Accepted
- **Date:** 2026-11-17
- **Iteration:** I00

## Context
AI must run locally (LM Studio) and be switchable to OpenRouter or other providers by config only. Chat and embeddings may use different providers.

## Options
1. **Microsoft.Extensions.AI** (`IChatClient`, `IEmbeddingGenerator`) + the OpenAI client pointed at any OpenAI-compatible base URL. Middleware pipeline (telemetry, logging, caching, function invocation) and structured output helpers.
2. Semantic Kernel as the main abstraction: more features, more concepts, partly built on (1) anyway.
3. Raw HTTP calls to `/v1/chat/completions`: maximum control, a lot of plumbing (streaming, tool-call parsing).

## Decision
Option 1. LM Studio and OpenRouter both speak the OpenAI API, so one adapter covers both. Business code depends only on the two interfaces.

## Consequences
- Provider-specific features (e.g. OpenRouter routing options, reasoning settings) go through `AdditionalProperties` or a small adapter.
- Differences between models (tool-calling quality, JSON reliability) are real and must be measured, not hidden by the abstraction → evaluations.
- Once in the project, raw HTTP requests are captured once (logging handler) so the owner sees what is actually sent on the wire.

---

**Previous:** [ADR-0001](0001-postgresql-pgvector.md) · **Next:** [ADR-0003](0003-movie-data-source-tmdb.md) · **See also:** [All ADRs](README.md) · [Docs hub](../README.md)
