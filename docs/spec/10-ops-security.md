# 10 — Operations and security

## 1. Local environment

Prerequisites (checked in I01, written to README):
- .NET 10 SDK, Node.js LTS + pnpm (or npm, decided in I02), Docker Desktop (for PostgreSQL through Aspire and Testcontainers).
- LM Studio with the local server on `http://localhost:1234/v1`, one chat model and one embedding model loaded.
- Stripe CLI (from I15). TMDB API key (from I03 seed builder / I16).

Run: `dotnet run --project src/FilmSearch.AppHost`. The Aspire dashboard shows every resource, logs, traces and metrics.

## 2. Configuration and secrets

| Kind | Where |
|---|---|
| Non-secret defaults | `appsettings.json`, `appsettings.Development.json` |
| Secrets in development | `dotnet user-secrets` on the AppHost / Api projects (API keys, Stripe keys, TMDB key) |
| Secrets in deployment | Environment variables / platform secret store |
| Frontend | Only public values (`VITE_*`). No keys in the frontend, ever. |

Never committed: `.env`, `appsettings.*.local.json`, user-secrets, keys of any kind. A `.env.example` / README section lists required variables with fake values.

## 3. Observability

- ServiceDefaults: OpenTelemetry logs, traces, metrics; OTLP exporter to the Aspire dashboard.
- AI calls: `UseOpenTelemetry()` on chat and embedding clients, activity source and meter `FilmSearch.AI` (GenAI semantic conventions: `gen_ai.request.model`, `gen_ai.usage.input_tokens`, …; MEAI metrics `gen_ai.client.token.usage`, `gen_ai.client.operation.duration`). Spans also carry `filmsearch.ai.profile` and `filmsearch.ai.cost_usd`. Recording of prompt / completion content (`AI:EnableSensitiveData`) is on in Development, off by default elsewhere.
- Custom metrics (meter `FilmSearch`, I18): `search.requests` and `search.latency` (by mode, understand, degraded), `assistant.turns` and `assistant.turn.duration` (by outcome: `answered`, `cancelled` or an error code; prompt), `assistant.tool_calls` (by tool, ok), `assistant.iteration_limit_hits`, `assistant.grounding_violations` (answers with an unknown title) and `assistant.grounding.titles`, `assistant.feedback` (up/down), `ai.tokens` (by model, profile, operation, direction), `ai.cost_usd`, `payments.webhooks` (by provider, type, result: `applied`, `duplicate`, `ignored`, `unhandled`, `rejected`, `error`), `orders.status_changes` (from, to; `from=created` for new orders, counted after the commit). I19: `cache.lookups` (cache = `query_embedding` / `query_understanding`, result = `hit` / `miss`; hit rate = hit / (hit + miss)), `cache.entries` (gauge, by cache), `ai.fallbacks` (from, to, operation: calls the fallback chat profile answered). Rate limiter decisions are ASP.NET Core's own `aspnetcore.rate_limiting.requests` (by policy and result), and the resilience pipelines report Polly's `resilience.polly.strategy.events` (retries, breaker opened / closed, timeouts) on the meter `Polly`.
- Trace per chat turn: HTTP request → `invoke_agent assistant` (conversation id, prompt id, total tokens and cost) → each LLM call (`chat <model>`) → each tool (`execute_tool <name>`, `filmsearch.tool.ok`) → search stages (`search {mode}` → `search.understand`, `search.keyword`, `search.embed_query`, `search.vector`, `search.fusion`; activity source `FilmSearch`) → DB queries. The answer stores the trace id of its turn (`assistant.messages.trace_id`): feedback → message → trace. With `AI:EnableSensitiveData` the turn span also carries the user message and the answer, and tool spans their arguments and result (`gen_ai.tool.call.arguments` / `.result`, cut at 8000 characters).
- Optional (I18): Langfuse v3, self-hosted in the AppHost (six containers, `Langfuse:Enabled=true` in the AppHost user secrets) as an LLM-specific trace viewer. The API sends traces to its OTLP endpoint (`/api/public/otel/v1/traces`, HTTP/protobuf, Basic auth with the project keys) through a second exporter that passes on only traces with AI work (`AiTraceFilter`: spans of the `FilmSearch.AI` / `FilmSearch` sources, their descendants, and the request span around them). The Aspire dashboard keeps getting everything. Turn spans set `langfuse.session.id` (= conversation id), `langfuse.user.id` (owner id) and the trace name. User ratings become `user_feedback` scores of the turn's trace (`POST /api/public/scores`, id `feedback-<messageId>`, sent from an in-memory queue, failures only logged). How to use it: [runbook](../runbooks/observability.md).

## 4. Security checklist

- [ ] HTTPS everywhere, HSTS outside Development.
- [ ] CORS: only the web origin.
- [ ] Anonymous session cookie: HttpOnly, Secure, SameSite=Lax, random 128-bit id.
- [x] Token budgets per conversation and per owner and day (NFR-5, I18); prompt and completion texts in traces only with `AI:EnableSensitiveData` (Development).
- [x] Rate limiting (`Microsoft.AspNetCore.RateLimiting`, I19): chat 20 msgs/min per owner, search 60 req/min per IP, new conversations 10/min per IP; 429 Problem Details with `Retry-After`. Behind a reverse proxy the client IP needs the forwarded headers (I20).
- [ ] Input limits: chat message ≤ 2000 chars, search query ≤ 300 chars.
- [ ] Stripe webhook signature check; Fake webhook HMAC; Fake endpoints mapped only in Development.
- [ ] Startup refuses live Stripe keys.
- [x] Resilience (NFR-6, I19, [ADR-0011](../adr/0011-ai-resilience-and-fallback.md)): per-profile AI pipelines (safe retries, circuit breaker, attempt timeout), optional fallback chat profile, search without embeddings degrades to keyword; payment calls retried only for reads and idempotent requests.
- [x] Admin endpoints require the admin role (I17; Development-only before).
- [x] Accounts (I17): passwords hashed by Identity (PBKDF2), at least 8 characters, lockout after 5 failures; auth cookie HttpOnly, Secure, SameSite=Lax (CSRF protection together with JSON-only bodies); the admin account comes from config with the password in user secrets.
- [x] Owner check on every cart / order / conversation / confirmation access (no IDOR; tests between anonymous owners and between users).
- [ ] SQL only through EF Core / parameterized queries (vector and FTS queries included).
- [ ] Dependency scanning (Dependabot) and secret scanning on GitHub.
- [ ] Prompt-injection cases in the adversarial evaluation set.

## 5. CI/CD

- GitHub Actions on PR and push to `main`: restore, build (warnings as errors), `dotnet format --verify-no-changes`, unit tests, integration tests (Testcontainers on the Linux runner), frontend lint + typecheck + unit tests + build.
- Later (I20): build container images, push to a registry, deploy on tag.

## 6. Deployment (I20)

Target decided in I20 and recorded as an ADR. Options: a small VPS with Docker Compose (Caddy, Postgres, Api, Worker, static web) or Azure Container Apps through `azd` with Aspire. AI in deployment: OpenRouter profile (no local GPU on the server), or the deployment is only for demo with a cheap model.
