# Handbook: what FilmSearch can do and who controls it

> [Docs](../README.md) › Handbook

The handbook describes the running system: its capabilities, and where each one is used or controlled. It has one page
per role, from non-technical to technical. The [guide](../guide/README.md) explains *why* the system works this way; the
[journal](../journal/README.md) tells *how* it got there.

## Roles

| Role | Technical level | Page | Covers |
|---|---|---|---|
| 🛒 Customer | none | *Customer guide* (I21, session 3) | Catalog, filters, search modes, languages, the assistant, cart, payment, library, account |
| 📊 Product owner / manager | low | *Product owner guide* (I21, session 3) | Capabilities and limits, quality and cost numbers, levers that need no code, what to watch |
| 🛠️ Administrator | medium | *Administrator guide* (I21, session 3) | Admin account, seed and TMDB import, embeddings and reindex, jobs, payment provider, budgets and rate limits |
| 🚦 Operator / DevOps | high | *Operator guide* (I21, session 3); today: [deploy runbook](../../deploy/README.md), [observability runbook](../runbooks/observability.md) | Environments, deploy and rollback, health, telemetry, incidents, backup |
| 💻 Developer | high | [Local setup](local-setup.md); *Developer guide* (I21, session 3) | Setup, architecture, code map, conventions, tests, adding a feature |
| 🧪 AI engineer | high | *AI engineer guide* (I21, session 3); today: [evaluations](../../evals/README.md) | Models and profiles, prompts, search parameters, evaluation suites, safe change process |
| — | reference | *Configuration reference* (I21, session 3); today: [local setup](local-setup.md) | Every setting, its default, where to set it, who changes it |

## Capabilities at a glance

Where each capability lives and where it is controlled. "Config" means `appsettings.json` / `appsettings.{Environment}.json`
of the API (the Worker reads the same files), user secrets locally, or environment variables (`Section__Key`) in
containers.

| Capability | Used by | Controlled by | Where |
|---|---|---|---|
| Catalog with filters and sorting, three languages | Customer | — | Web: Catalog; `GET /api/movies` |
| Keyword, semantic and hybrid search, "understand the query" | Customer | AI engineer | Web: search page (mode switch, "Show scores"); config `Search:*` |
| Similar movies | Customer | AI engineer | Movie page; embedding profile |
| AI assistant (chat, recommendations, cart, checkout) | Customer | AI engineer, Administrator | Web: Assistant; config `Assistant:*`, prompt version files |
| Feedback on answers (👍 / 👎) | Customer | Product owner reads it | Chat; table `assistant.message_feedback`, Langfuse scores |
| Cart, orders, payment, library | Customer | Administrator | Web; config `Payments:Provider` (`Fake` / `Stripe`), `Commerce:*` |
| Accounts, merge of the anonymous cart on sign-in | Customer | Administrator | Web: Sign in / Create account; config `Auth:*` |
| Admin role and admin API | Administrator | Operator | `Auth:Admin:Email`, password in secrets; `/api/admin/*` |
| Seed load, TMDB import, daily import | Administrator | Operator | `POST /api/admin/ingestion/*`; config `Tmdb:*`, key in secrets |
| Embedding index: state, reindex, delete an old model | Administrator, AI engineer | — | `/api/admin/embeddings`; config `AI:ActiveEmbeddingProfile` |
| Background jobs: list, follow, cancel | Administrator | Operator | `/api/admin/jobs`; config `Jobs:*` |
| AI provider and model choice, fallback model | AI engineer | Operator (keys) | Config `AI:ActiveChatProfile`, `AI:FallbackChatProfile`, `AI:ChatProfiles`, `AI:EmbeddingProfiles` |
| Retry, circuit breaker and timeout per AI provider | Operator | AI engineer | Config `AI:ChatProfiles:<name>:MaxRetries` / `CircuitBreakSeconds` / `TimeoutSeconds` |
| Token budgets (per conversation, visitor, day) | Product owner decides, Operator sets | — | Config `Assistant:MaxConversationTokens`, `MaxOwnerTokensPerDay`, `MaxTotalTokensPerDay` |
| Rate limits on search and chat | Operator | — | Config `RateLimiting:*` |
| Cost per answer, metrics, traces | Operator, Product owner | — | Aspire dashboard, Langfuse, table `assistant.messages` |
| Quality measurement | AI engineer | — | `evals/` runner and reports |
| Deploy, rollback, backup | Operator | — | `deploy/` scripts, release workflow |

Detailed pages for each role are written in [I21](../plan/iterations/I21-documentation.md) (session 3). Until then the
[local setup page](local-setup.md) has every command and setting.

---

**See also:** [Guide](../guide/README.md) · [Journal](../journal/README.md) · [Spec](../spec/00-overview.md) ·
[Docs hub](../README.md)
