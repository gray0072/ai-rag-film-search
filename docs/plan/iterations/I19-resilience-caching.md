# I19 — Resilience, caching, rate limiting

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I12 |
| **Parallel with** | I20 |
| **Branch** | `iter/I19-resilience` |
| **Spec** | [01 NFR-6, NFR-7](../../spec/01-requirements.md#non-functional-requirements) |
| **Requirements** | NFR-6, NFR-7 |

## Goal
The system degrades gracefully when the AI provider is slow or down, repeated work is cached, and abuse is limited.

## You will learn
Failure modes of LLM systems, fallbacks between models / providers, caching strategies for embeddings and LLM output.

## Tasks
- [ ] Resilience pipelines for AI and payment HTTP clients (timeouts tuned for local models, retries only for safe cases, circuit breaker).
- [ ] Fallback chat profile (e.g. local → OpenRouter, or bigger → smaller model) with a flag on the message.
- [ ] Search degrades to keyword-only if embeddings are unavailable (flag in response).
- [ ] Query embedding cache (normalized query → vector) and QU cache tuning.
- [ ] Rate limiting policies (spec 10 §4).
- [ ] Chaos tests: fake chat client that times out / errors; check user-facing behavior.

## Acceptance criteria
- [ ] LM Studio stopped → search still works (keyword), chat shows a clear error or uses the fallback profile.
- [ ] Cache hit rate visible in metrics.
