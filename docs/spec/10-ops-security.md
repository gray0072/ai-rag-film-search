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
- Custom metrics (meter `FilmSearch`): `search.requests` (by mode), `search.latency`, `assistant.turns`, `assistant.tool_calls` (by tool, ok/error), `assistant.iteration_limit_hits`, `assistant.grounding_violations`, `ai.tokens` (by model, direction), `ai.cost_usd`, `payments.webhooks` (by type, result), `orders.status_changes`.
- Trace per chat turn: HTTP request → agent loop → each LLM call → each tool → DB queries.
- Optional (I18): Langfuse (self-hosted container in AppHost) as an LLM-specific trace viewer through its OTLP endpoint, for prompt / conversation inspection and scores.

## 4. Security checklist

- [ ] HTTPS everywhere, HSTS outside Development.
- [ ] CORS: only the web origin.
- [ ] Anonymous session cookie: HttpOnly, Secure, SameSite=Lax, random 128-bit id.
- [ ] Rate limiting (`Microsoft.AspNetCore.RateLimiting`): chat 20 msgs/min per owner, search 60 req/min per IP.
- [ ] Input limits: chat message ≤ 2000 chars, search query ≤ 300 chars.
- [ ] Stripe webhook signature check; Fake webhook HMAC; Fake endpoints mapped only in Development.
- [ ] Startup refuses live Stripe keys.
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
