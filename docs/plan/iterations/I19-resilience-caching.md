# I19 — Resilience, caching, rate limiting

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I19 · ← [I18](I18-observability-cost.md) · [I20](I20-deployment.md) → · [Journal: what happened](../../journal/m5-production-ready.md#i19--resilience-caching-rate-limiting)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I12 |
| **Parallel with** | I20 |
| **Spec** | [01 NFR-6, NFR-7](../../spec/01-requirements.md#non-functional-requirements) |
| **Requirements** | NFR-6, NFR-7 |

## Goal
The system degrades gracefully when the AI provider is slow or down, repeated work is cached, and abuse is limited.

## You will learn
Failure modes of LLM systems, fallbacks between models / providers, caching strategies for embeddings and LLM output.

## Tasks
- [x] Resilience pipelines for AI and payment HTTP clients (timeouts tuned for local models, retries only for safe cases, circuit breaker).
- [x] Fallback chat profile (e.g. local → OpenRouter, or bigger → smaller model) with a flag on the message.
- [x] Search degrades to keyword-only if embeddings are unavailable (flag in response).
- [x] Query embedding cache (normalized query → vector) and QU cache tuning.
- [x] Rate limiting policies (spec 10 §4).
- [x] Chaos tests: fake chat client that times out / errors; check user-facing behavior.

## Acceptance criteria
- [x] LM Studio stopped → search still works (keyword), chat shows a clear error or uses the fallback profile.
- [x] Cache hit rate visible in metrics.

## Result

### What was done
- **AI resilience pipeline per profile** ([ADR-0011](../../adr/0011-ai-resilience-and-fallback.md), spec 06 §1):
  - Every configured profile has its own named HTTP client (`FilmSearch.AI.chat.<profile>`, `FilmSearch.AI.embedding.<profile>`) with a Polly pipeline (`AiHttpResilience`): retry → circuit breaker → attempt timeout.
  - Retries (`MaxRetries`, backoff from the new `RetryDelayMs`, jitter, `Retry-After`) only for connection errors, 429, 502, 503 and 504. Timeouts and 500 are not retried: a local model that timed out is still generating in LM Studio's queue.
  - The breaker opens for `CircuitBreakSeconds` (30) when half of at least 3 attempts in a minute failed. It is per profile, so a slow chat model does not block the embedding model on the same server.
  - The OpenAI SDK's own retries are off. Its network timeout (`TimeoutSeconds` + 10 s) is a backstop for streamed bodies.
  - `AiProviderErrors` also counts Polly's rejections (open circuit, attempt timeout) as provider failures.
- **Fallback chat profile** (`AI:FallbackChatProfile`, empty by default; validated: it must exist, differ from the active profile, support tools and have its key):
  - `FallbackChatClient` is the unkeyed `IChatClient`. A provider failure before the first streamed update sends the same call to the fallback. A failure in the middle of a stream is not switched.
  - The assistant flags the answer: `fallback` on the stored message (migration `I19_AddMessageFallback`), on `message.completed` and in the history. The fallback's tokens are priced with its own model, and the conversation's `profileName` becomes the fallback.
  - Counter `ai.fallbacks` (from, to, operation).
  - Web: answers of the backup model get a note (en / ru / sv).
- **Search without embeddings:** semantic mode now degrades to keyword results with `degraded: true`, as hybrid already did; it returned 503 before. `POST /api/search` no longer has a 503 response. The web's "semantic search unavailable" error text is gone.
- **Caches** (`LookupCache`: its own bounded `MemoryCache` per cache; counter `cache.lookups` {cache, result = hit / miss}, gauge `cache.entries`):
  - Query vectors: the new `ITextEmbedder.EmbedQueryAsync` caches by profile, model, dimensions and normalized text (`QueryText.Normalize`: case, whitespace, ё/е). Defaults: 1440 minutes, 10 000 entries. A query embedded before an outage stays semantic during it.
  - Query understanding results moved from the shared, unbounded cache to a bounded one, and `CacheMinutes` went from 60 to 1440 (`CacheMaxEntries` 10 000). Fallbacks are still not cached.
- **Payments:** the Stripe client has its own pipeline (`PaymentHttpResilience`) instead of the standard handler, which retries every method. Total timeout 30 s, then retry, circuit breaker, attempt timeout 10 s. Only GET / HEAD and requests with an `Idempotency-Key` are retried. Session expiry now sends a key (`expire-<sessionId>`).
- **Rate limiting** (`RateLimiting` section, spec 04 / spec 10 §4):
  - Search: 60 per minute per IP. Chat messages: 20 per minute per owner. New conversations: 10 per minute per IP, because a client that drops its cookie gets a new owner, and with it a new chat limit and token budget.
  - Token buckets without a queue. A rejected request gets 429 Problem Details with `code: rateLimited`, `retryAfterSeconds` and `Retry-After`. Web texts for search and chat in en / ru / sv.
- **Telemetry:** ServiceDefaults exports the `Polly` meter (retries, breaker opened / closed, timeouts) and `Microsoft.AspNetCore.RateLimiting`. There is a new runbook section "The AI provider is down".
- **Tests:**
  - Unit (+24, 368 in total):
    - `AiResilienceTests`: the real pipeline against a fake provider. Refused and 503 are retried; 500 and timeouts are not. The open circuit rejects without a request while the other profiles keep working. The fallback works for plain and streamed calls, and the fallback metric is recorded.
    - `FallbackChatClientTests`: no switch after the first update, nor for bugs or the caller's cancellation; both profiles down.
    - Validator cases, query-vector cache tests (hit / miss metrics, served while the provider is down, off), `PaymentHttpResilienceTests`, and `AssistantFallbackTests` (flag and cost).
    - `AiTestHost` routes every HTTP client to the fake. `OwnedMeasurements` records only one host's meters, which fixed a test that read metrics process-wide.
  - Integration (+9, 148 in total):
    - `ResilienceApiTests`: the active profile refused, timed out or with an open circuit → the fallback answers and is flagged; every profile down → `providerUnavailable` and the question is kept; QU fallback; a repeated query embedded once (cache hit metric).
    - `RateLimitingApiTests`: search 429 with `Retry-After`; chat per owner; new conversations per IP.
    - The degraded-search test now covers semantic mode too. The factory turns rate limiting off and takes `configureServices`; there is a new `SwitchableChatClient`.
  - Web (+3, 65 in total): the search rate-limit message, the chat rate-limit message (the text is given back), the backup-model note.
- **Manual check** (AppHost, LM Studio with `google/gemma-4-e4b` and EmbeddingGemma, no fallback profile; the check of the user secret with a fallback was not done, see below):
  - LM Studio up: semantic "a heist inside dreams" returned Inception first in 7.0 s (cold). The same query in another case and spacing took 211 ms (cached vector). A chat turn with `search_movies` answered in 12 s, `fallback: false`.
  - `lms server stop`:
    - The cached query stayed semantic (167 ms).
    - A new semantic query and the same hybrid query returned keyword results with `degraded: true`. The first took 10.2 s: on Windows a refused connection to `localhost` takes about 2 s per attempt (::1, then 127.0.0.1), × 3 attempts with backoff. The breaker then opened, and the hybrid call took 232 ms.
    - Chat: the first turn ended with `error: providerUnavailable` after 9.9 s; the next three got the same error in about 0.2 s (open circuit).
  - Rate limit: 90 parallel searches gave 18 × 200 and 72 × 429 (`application/problem+json`, `Retry-After: 1`, `code: rateLimited`). Sequential searches were never limited, because the bucket refills one token per second.
  - `lms server start`, then 31 s later: semantic and hybrid were no longer degraded (the circuit closed).

### Deviations and decisions
- Retries moved from the SDK to the HTTP pipeline, and timeouts are no longer retried. Before I19, a timed-out local call was sent up to 3 times.
- Semantic mode no longer returns 503 (spec 04 and 05 updated). Search never fails because of the AI provider.
- Token buckets instead of sliding windows: same average limit, but the limiter can tell the client when to retry (a sliding window gives no `Retry-After`), and a short burst is allowed.
- The new-conversation limit per IP is an addition to spec 10 §4 (see above).
- The rate limiter, Polly and the memory caches come from packages and the shared framework that were already referenced. No new packages.
- No evaluation run: caching and the retry rules change neither ranking nor answers (the cache key is the normalized query, and the vector is the one the provider gave). The fallback profile is off by default.
- The manual fallback run was skipped: setting `AI:FallbackChatProfile` in the API user secrets was not allowed in the session. It is covered by `ResilienceApiTests` (all three failure kinds) and `AiResilienceTests` (the real HTTP pipeline). To try it: `dotnet user-secrets set AI:FallbackChatProfile openrouter --project src/FilmSearch.Api`, then stop LM Studio.
- The distributed turn lock (an I18 follow-up) stays in-process: there is one API instance, and I20 decides the deployment.

### Follow-ups
- I20: forwarded headers before the rate limiter (the client IP behind a reverse proxy); the turn lock if there will be more than one API instance.
- The first call after LM Studio stops takes ~10 s on Windows (refused `localhost` connections). `127.0.0.1` in the local endpoints would halve it; `MaxRetries` 0 for local profiles would cut it to one attempt.
- A semantic cache (similar, not only equal, queries) for query understanding could raise the hit rate; measure `cache.lookups` first.

### Sessions
- S21 (2026-11-19), [prompt log](../../prompts/sessions/2026-11-19_S21_resilience-caching.md).
