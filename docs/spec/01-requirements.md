# 01 — Requirements

> [Docs](../README.md) › [Spec](00-overview.md) › 01 — Requirements

IDs are referenced from iteration plans and tests. `FR` = functional, `NFR` = non-functional.

## Functional requirements

### Catalog (CAT)
| ID | Requirement |
|---|---|
| FR-CAT-1 | Paged list of movies with sorting by rating, year, popularity, title. |
| FR-CAT-2 | Filters: genres (any of), year range, min rating, runtime range, original language, price range, offer type. |
| FR-CAT-3 | Movie page: title, original title, year, runtime, genres, overview, tagline, director(s), top cast, rating and vote count, poster, offers with prices, similar movies. |
| FR-CAT-4 | Title and overview are shown in the UI locale (`en`/`ru`/`sv`), falling back to `en`. |
| FR-CAT-5 | Prices are stored in the DB. Prices shown to the user (in UI or by the assistant) always come from the DB. |

### Search (SRCH)
| ID | Requirement |
|---|---|
| FR-SRCH-1 | Keyword search over title, original title, overview, tagline, people, keywords (PostgreSQL full-text, `english`, `russian` and `swedish` configs). |
| FR-SRCH-2 | Semantic search: the query is embedded and compared with movie embeddings (cosine). Works across languages (ru or sv query, en text). |
| FR-SRCH-3 | Hybrid search: keyword and semantic results fused with Reciprocal Rank Fusion (RRF). This is the default mode in the UI. |
| FR-SRCH-4 | Query understanding: a natural language query is turned into structured filters plus a free-text "semantic part" with an LLM and JSON Schema structured output. The UI shows the extracted filters as chips that can be removed. |
| FR-SRCH-5 | All filters from FR-CAT-2 can be combined with every search mode. |
| FR-SRCH-6 | In debug mode the response contains per-result scores (keyword rank, vector similarity, fused score) for learning and tuning. |

### Assistant (AST)
| ID | Requirement |
|---|---|
| FR-AST-1 | Chat with streamed answers (SSE). |
| FR-AST-2 | Conversations are stored and can be continued after page reload. |
| FR-AST-3 | The assistant recommends **only movies from the catalog**, found through tools. Every recommendation is shown as a movie card (id, poster, price) next to the text. |
| FR-AST-4 | The assistant explains why each movie fits the request. |
| FR-AST-5 | The assistant can: search, get details, find similar movies, show / add to / remove from the cart, start checkout. |
| FR-AST-6 | Checkout started by the assistant needs an **explicit click** by the user on a confirmation button. The LLM can never create a payment by itself. |
| FR-AST-7 | The assistant answers in the language of the user's last message (`en`, `ru` or `sv`). |
| FR-AST-8 | Off-topic requests are politely declined and steered back to movies. |
| FR-AST-9 | The user can rate an answer (thumbs up/down). The feedback is stored for evaluation. |

### Cart, orders, payment (COM)
| ID | Requirement |
|---|---|
| FR-COM-1 | Cart: add / remove an offer, one line per movie. Rent and Buy of the same movie cannot both be in the cart. |
| FR-COM-2 | An order is created from the cart with a price snapshot. |
| FR-COM-3 | Payment through `IPaymentGateway`: `Fake` and `Stripe` (test mode) implementations, chosen by config. |
| FR-COM-4 | The order becomes `Paid` only after a confirmed webhook, never after the redirect to the success page. |
| FR-COM-5 | Webhooks are processed idempotently (deduplicated by provider event id). |
| FR-COM-6 | After payment, entitlements are created. "My library" lists them; rentals show the expiry time. |
| FR-COM-7 | Unpaid orders expire after 30 minutes. |
| FR-COM-8 | Refunds (stretch): through Stripe test mode, the entitlement is revoked. |

### Data (DATA)
| ID | Requirement |
|---|---|
| FR-DATA-1 | Seed: about 1000 top movies from a committed JSON file, loaded on first start (idempotent, batched, under 1 minute). |
| FR-DATA-2 | TMDB import: by list (top rated / popular) and by id. Upsert by `TmdbId`. |
| FR-DATA-3 | When text fields used for embeddings change, the movie is re-embedded (detected by content hash). |
| FR-DATA-4 | Full reindex when the embedding model changes. Old and new vectors can live side by side while it runs. |
| FR-DATA-5 | TMDB attribution is shown in the UI footer (TMDB terms of use). |

### Users (USR)
| ID | Requirement |
|---|---|
| FR-USR-1 | Before auth exists: an anonymous session id (cookie) owns the cart, orders and conversations. |
| FR-USR-2 | After auth: register / log in with email and password (ASP.NET Core Identity). The anonymous cart is merged into the account on login. |
| FR-USR-3 | Admin role for admin endpoints (ingestion, reindex). |

## Non-functional requirements

| ID | Requirement |
|---|---|
| NFR-1 Provider independence | The chat and embedding providers are switched by config only (LM Studio, OpenRouter, any OpenAI-compatible API). Business code uses only `IChatClient` and `IEmbeddingGenerator`. |
| NFR-2 Local first | The whole system runs offline with LM Studio. Cloud providers are optional. |
| NFR-3 Performance (local, indicative) | Catalog and keyword search p95 < 200 ms. Hybrid search without LLM p95 < 500 ms. Chat time to first token < 3 s on a local 7–14B model (depends on hardware; measured, not enforced). |
| NFR-4 Observability | Every LLM and embedding call is traced (OpenTelemetry GenAI conventions): model, tokens, latency, errors. Logs are structured. Traces can be viewed in the Aspire dashboard. |
| NFR-5 Cost control | Token usage and estimated cost are stored per conversation. A per-session token budget and a max number of tool iterations per turn. |
| NFR-6 Resilience | Timeouts, retries with backoff, circuit breaker on AI and payment calls. Fallback chat profile is optional. |
| NFR-7 Security | Secrets only in user-secrets or environment variables. Stripe webhook signature check. Rate limiting on chat and search. Input length limits. Prompt-injection defenses (see spec 06). |
| NFR-8 Testability | Unit tests for domain logic. Integration tests with Testcontainers (PostgreSQL + pgvector). AI calls in tests use a fake `IChatClient` or recorded responses. Evaluations are a separate suite. |
| NFR-9 Quality gate | CI: build, lint, unit and integration tests. Changes to AI behavior (prompts, models, search parameters) come with an evaluation report. |
| NFR-10 i18n | All UI strings in resource files (`en`, `ru`, `sv`). No hard-coded user-facing text. |
| NFR-11 Accessibility | Keyboard navigation, alt texts, sufficient contrast (basic level). |

---

**Previous:** [00 — Overview](00-overview.md) ·
**Next:** [02 — Architecture](02-architecture.md) ·
**See also:** [Docs hub](../README.md) · [ADRs](../adr/README.md) · [Guide](../guide/README.md)
