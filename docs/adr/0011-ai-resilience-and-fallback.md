# ADR-0011 — AI resilience: a pipeline per profile, a fallback chat profile, keyword search without embeddings

- **Status:** Accepted
- **Date:** 2026-11-19
- **Iteration:** I19

## Context
NFR-6 asks for timeouts, retries with backoff and a circuit breaker on AI and payment calls, and makes a fallback chat profile optional. LLM providers fail in their own ways:

- A local model (LM Studio on a laptop) is **slow**, not just up or down. A chat call can take minutes. When the client times out, LM Studio keeps generating the request in its queue. A retry does not help; it doubles the load and the wait.
- A stopped LM Studio server refuses the connection at once. A cloud provider returns 429 / 502 / 503 when it is overloaded. These failures are fast, and repeating the request is safe: no model has worked on it yet.
- Until I19, retries were the OpenAI SDK's (`ClientRetryPolicy`: 408, 429, 5xx and network errors, timeouts included). One shared HTTP client served every profile, with no circuit breaker. With LM Studio down, every request waited for its own failures.
- Stored movie vectors belong to one embedding model. Another model's query vector cannot be compared with them.

## Options
1. **Keep the SDK retry policy, add a breaker at the `IChatClient` level.** Small change. But the SDK decides which failures to retry (timeouts too). A middleware breaker cannot tell an attempt timeout from a caller's cancellation, and it would not cover embeddings.
2. **A Polly pipeline per profile on its HTTP client** (`Microsoft.Extensions.Http.Resilience`, already used for TMDB): retry → circuit breaker → attempt timeout, with our own predicates. The SDK's retries are off. The fallback is a delegating `IChatClient` above the profile clients.
3. **A gateway** (LiteLLM, OpenRouter's own fallbacks) in front of all providers. Fallbacks and retries are outside our code. But it is one more service to run locally, the behavior is hidden from the learning project, and it does not help when the local server itself is the one that is down.

## Decision
Option 2.

- **One named HTTP client per profile** (`FilmSearch.AI.chat.local`, `FilmSearch.AI.embedding.local`, …), so a slow chat model does not open the breaker of the embedding model on the same LM Studio server.
- **Retry** (`MaxRetries`, exponential backoff with jitter, `Retry-After` wins) only for connection errors, 429, 502, 503 and 504. Timeouts and 500 are not retried. A stream that has started is never retried; the HTTP handler is done by then.
- **Circuit breaker**: it opens for `CircuitBreakSeconds` (30) when half of at least 3 attempts in a minute failed (connection errors, timeouts, 408, 429, 5xx). While it is open, calls fail at once (`BrokenCircuitException`, a provider failure for `AiProviderErrors`).
- **Attempt timeout** `TimeoutSeconds` until the response headers. The SDK network timeout stays 10 s longer, as a backstop for reading a streamed body.
- **Fallback chat profile** (`AI:FallbackChatProfile`, empty by default): `FallbackChatClient` is the unkeyed `IChatClient`. When the active profile fails before it has streamed anything, the same call goes to the fallback. The call's updates are marked, and the assistant then flags the answer (`fallback` on the message and on `message.completed`) and prices those tokens with the fallback's model. A failure in the middle of a stream is not switched: the user would see two answers mixed together.
- **No embedding fallback.** Vectors of another model are not comparable with the stored ones. Semantic and hybrid search return the keyword results with `degraded: true` instead (semantic mode returned 503 before I19). Cached query vectors (I19) keep repeated queries semantic during an outage.
- **Payments** get their own pipeline too. A retry happens only when a repeat cannot have a second effect: GET / HEAD, or a request with an `Idempotency-Key` (Stripe returns the first result for the same key). A POST without a key is never retried. The standard handler retries every method.

## Consequences
- Every configured profile gets its HTTP client at startup (profiles are read from configuration then). A profile added at run time would need a restart; that is the case for all options anyway.
- Breaker state is per process. With several API instances each one learns the outage on its own; that is fine at this scale.
- The fallback costs money when it is a cloud profile: `ai.fallbacks` (from, to, operation) shows how often it happens. Query understanding also goes through the fallback, because it uses the unkeyed client.
- Tests: unit tests drive the real pipeline with a fake HTTP handler (`AiResilienceTests`), and integration tests switch profile clients between up, refused, timed out and circuit open (`SwitchableChatClient`).
- To reverse it: set `MaxRetries` / `CircuitBreakSeconds` per profile, or drop `FallbackChatProfile`. Going back to SDK retries means setting `ClientRetryPolicy(profile.MaxRetries)` in `AiClientFactory` and removing the resilience handler.
