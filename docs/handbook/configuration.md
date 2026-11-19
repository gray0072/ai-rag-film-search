# Configuration reference

> [Docs](../README.md) › [Handbook](README.md) › Configuration

Every setting of FilmSearch: its default, the Development and Demo values, what it does, who changes it and whether it
is a secret. Defaults and valid ranges come from the options classes in `src/` (most sections are validated on start:
an invalid value stops the app with a message naming the key). Sources: `src/FilmSearch.Api/appsettings.json`,
`appsettings.Development.json`, `appsettings.Demo.json`, the AppHost, `deploy/compose.yaml` and
[deploy/.env.example](../../deploy/.env.example). Roles are the ones of the [handbook](README.md#roles).

**Contents:** [How to set a value](#how-to-set-a-value) · [Host and environment](#host-and-environment) ·
[Catalog](#catalog) · [Search](#search) · [RateLimiting](#ratelimiting) · [Jobs](#jobs) · [Tmdb](#tmdb) ·
[Commerce](#commerce) · [Payments](#payments) · [Stripe](#stripe) · [Assistant](#assistant) · [AI](#ai) ·
[Auth](#auth) · [Langfuse](#langfuse) · [AppHost](#apphost) · [deploy/.env](#deployenv)

## How to set a value

Configuration is layered; a later source wins:

1. `appsettings.json` (all environments) →
2. `appsettings.{Environment}.json` (`Development` or `Demo`) →
3. user secrets (Development only) →
4. environment variables →
5. command-line arguments.

| Where | How | Use for |
|---|---|---|
| appsettings | Edit `src/FilmSearch.Api/appsettings*.json` and commit | Non-secret defaults; a change of AI behavior needs an evaluation run ([AI engineer](ai-engineer.md#the-safe-change-process)) |
| User secrets (local) | `dotnet user-secrets set AI:ChatProfiles:openrouter:ApiKey <key> --project src/FilmSearch.Api`; `dotnet user-secrets list --project src/FilmSearch.Api` | Keys and passwords on a developer machine. The Worker and the AI playground share the API's secrets (same `UserSecretsId`); the AppHost has its own (`--project src/FilmSearch.AppHost`) |
| Environment variables | `Section__Key` (double underscore for `:`), e.g. `AI__ActiveChatProfile=openrouter`, `Search__Embeddings__Locales__0=en` for arrays | Containers; the AppHost sets some for the API and the Worker |
| `deploy/.env` | Variables that `deploy/compose.yaml` maps to environment variables of the containers ([table below](#deployenv)) | The demo server; `chmod 600`, never committed |
| One run only | Evaluations: `--set Search:Hybrid:RrfK=40`; AI playground: `--AI:<key>=<value>` | Experiments without changing files |

The **Worker** reads the same files as the API: the API's `appsettings*.json` are linked into its output
(`FilmSearch.Worker.csproj`). A setting therefore applies to both hosts unless it is only used by one of them.

Column "Who": the role that normally changes the value. "Secret": never in appsettings or the repository; user secrets
or environment only.

## Host and environment

| Key | Default (Dev / Demo) | Meaning | Who | Secret |
|---|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT` | `Development` (launch profiles, AppHost) / `Demo` (compose) | Selects `appsettings.{Environment}.json`. `Production` refuses the fake payment gateway; see [operator: why Demo](operator.md#environments) | Operator | no |
| `ConnectionStrings:filmsearch` | set by the AppHost (Aspire database `filmsearch`) / built from `POSTGRES_PASSWORD` in compose | PostgreSQL connection string; the API and Worker do not start without it | Operator | yes (password) |
| `Database:MigrateOnStartup` | `false` (code) — Dev `true`, Demo `false` | Apply pending EF Core migrations on startup (API and Worker, under an advisory lock). Demo runs them as the `migrate` step | Operator | no |
| `FORWARDEDHEADERS_ENABLED` (env `ASPNETCORE_FORWARDEDHEADERS_ENABLED`) | unset / `true` for the API in compose | Behind a reverse proxy: take scheme and client IP from `X-Forwarded-*` and skip the API's own HTTPS redirect | Operator | no |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | set by the AppHost / empty in compose | OTLP target of logs, traces and metrics; empty = no export | Operator | no |
| `OTEL_SERVICE_NAME` | project name / `api`, `worker`, `migrate` in compose | Service name in telemetry | Operator | no |
| `ASPNETCORE_HTTP_PORTS` | — / `8080` (image) | Port of the API container | Developer | no |
| `DataProtection:KeysPath` | empty — Demo `/var/lib/filmsearch/keys` | Folder for the data protection keys (auth cookie, antiforgery). Empty: keys in the container, lost on redeploy (everyone signed out). Application name is fixed (`FilmSearch`) | Operator | no (the folder holds keys: mount it only into the API) |
| `Logging:LogLevel:Default` | `Information` | Default log level | Operator | no |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | ASP.NET Core log level | Operator | no |
| `Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command` | — / Demo `Warning` | Hides SQL command logs in Demo | Operator | no |
| `Logging:LogLevel:System.Net.Http.HttpClient` | — / Demo `Warning` | Hides HTTP client logs in Demo | Operator | no |
| `AllowedHosts` | `*` | Host filtering | Operator | no |

## Catalog

`Catalog:Seed` (`CatalogSeedOptions`).

| Key | Default (Dev / Demo) | Meaning | Who | Secret |
|---|---|---|---|---|
| `Catalog:Seed:LoadOnStartup` | `false` — Dev `true`, Demo `true` | Load the seed file on startup when the catalog is empty | Operator | no |
| `Catalog:Seed:FilePath` | `Seed/movies.seed.json` | Seed file; relative to the app folder (the API build copies `data/seed/movies.seed.json` there). Required | Developer | no |
| `Catalog:Seed:BatchSize` | `100` | Movies per transaction (1–1000) | Developer | no |

## Search

`Search:Semantic` (`SemanticSearchOptions`), `Search:Hybrid` (`HybridSearchOptions`), `Search:QueryUnderstanding`
(`QueryUnderstandingOptions`), `Search:Embeddings` (`EmbeddingIndexOptions`). Changes need a search evaluation run.

| Key | Default (Dev / Demo) | Meaning | Who | Secret |
|---|---|---|---|---|
| `Search:Semantic:MaxResults` | `100` | Length of the semantic result list (1–1000); the `total` of a semantic search is at most this | AI engineer | no |
| `Search:Hybrid:RrfK` | `20` | RRF constant k (0–1000); larger flattens rank differences | AI engineer | no |
| `Search:Hybrid:KeywordWeight` | `1.0` | Weight of the keyword list when every query word matched (≥ 0) | AI engineer | no |
| `Search:Hybrid:RelaxedKeywordWeight` | `0.1` | Weight of the keyword list when the query was relaxed to any word (≥ 0) | AI engineer | no |
| `Search:Hybrid:SemanticWeight` | `1.0` | Weight of the vector list (≥ 0) | AI engineer | no |
| `Search:Hybrid:CandidatesPerList` | `50` | Candidates from each retriever before fusion (1–1000) | AI engineer | no |
| `Search:Hybrid:SimilarTitles` | `AverageVector` | "Movies like X": `AverageVector` (average the query vector with the named movies' vectors) or `ExtraList` (their neighbours as extra RRF lists, hybrid only) | AI engineer | no |
| `Search:Hybrid:SimilarTitleWeight` | `1.0` (code) | Weight of each extra list of `ExtraList` (≥ 0) | AI engineer | no |
| `Search:Hybrid:MaxSimilarTitles` | `3` (code) | Titles of "movies like X" resolved to movies (0–10) | AI engineer | no |
| `Search:QueryUnderstanding:PromptVersion` | null (code) = latest | Pins `query-understanding.vN` | AI engineer | no |
| `Search:QueryUnderstanding:ChatProfile` | null (code) = active chat profile | Chat profile for query understanding | AI engineer | no |
| `Search:QueryUnderstanding:MaxOutputTokens` | `400` (code) | Output limit of one call (50–4000) | AI engineer | no |
| `Search:QueryUnderstanding:CacheMinutes` | `1440` | Cache lifetime of a result per normalized query (0–1440; 0 = off) | AI engineer, Operator | no |
| `Search:QueryUnderstanding:CacheMaxEntries` | `10000` | Most cached results (0–1000000) | Operator | no |
| `Search:Embeddings:Locales` | `["en"]` | Locales whose movie texts are embedded (code default: English when empty) | AI engineer | no |
| `Search:Embeddings:BatchSize` | `32` | Texts per embedding call (1–256) | AI engineer | no |
| `Search:Embeddings:IndexOnStartup` | `false` — Dev `true`, Demo `true` | Queue a reindex of stale vectors on startup, after the seed | Operator | no |

## RateLimiting

`RateLimitingOptions`. Token buckets without a queue: `PermitLimit` requests at once, refilled to `PermitLimit` per
`WindowSeconds`; over it, 429 Problem Details with `code: "rateLimited"` and `Retry-After`. Every policy: `PermitLimit`
1–100000, `WindowSeconds` 1–86400.

| Key | Default (Dev / Demo) | Meaning | Who | Secret |
|---|---|---|---|---|
| `RateLimiting:Enabled` | `true` | Off: nothing is limited (integration tests) | Operator | no |
| `RateLimiting:Chat:PermitLimit` / `:WindowSeconds` | `20` / `60` — Demo `10` / `60` | Chat messages per owner | Operator | no |
| `RateLimiting:NewConversation:PermitLimit` / `:WindowSeconds` | `10` / `60` — Demo `5` / `60` | New conversations per client IP | Operator | no |
| `RateLimiting:Search:PermitLimit` / `:WindowSeconds` | `60` / `60` — Demo `30` / `60` | Search requests per client IP | Operator | no |

## Jobs

`JobOptions`: how the Worker polls and holds jobs of `ingestion.jobs`.

| Key | Default | Meaning | Who | Secret |
|---|---|---|---|---|
| `Jobs:PollIntervalMs` | `2000` | Delay between looks at an empty queue (50–60000) | Operator | no |
| `Jobs:HeartbeatIntervalMs` | `2000` | How often a running job writes progress and heartbeat (50–60000) | Operator | no |
| `Jobs:LeaseSeconds` | `60` | A running job without a heartbeat this long is claimed by another worker; must cover at least three heartbeats | Operator | no |
| `Jobs:MaxAttempts` | `3` | Starts of a job (lost workers included) before it fails (1–10) | Operator | no |

## Tmdb

`TmdbOptions` and `Tmdb:Schedule` (`TmdbScheduleOptions`). Used by the Worker.

| Key | Default | Meaning | Who | Secret |
|---|---|---|---|---|
| `Tmdb:ApiKey` | empty | TMDB v4 read access token (bearer) or v3 key; without it imports fail | Administrator, Operator | **yes** |
| `Tmdb:BaseUrl` | `https://api.themoviedb.org/3/` (code) | TMDB API base | Developer | no |
| `Tmdb:RequestsPerWindow` / `Tmdb:WindowSeconds` | `40` / `10` | Client-side rate limit (1–1000 per 1–600 s); requests over it wait | Administrator | no |
| `Tmdb:MaxConcurrency` | `4` | Detail requests in flight (1–32) | Administrator | no |
| `Tmdb:MaxRetries` | `5` (code) | Retries of 429 / 5xx / timeouts (0–10) | Developer | no |
| `Tmdb:RetryBaseDelayMs` | `1000` (code) | First retry delay, exponential with jitter; `Retry-After` wins | Developer | no |
| `Tmdb:AttemptTimeoutSeconds` | `20` (code) | Timeout of one attempt | Developer | no |
| `Tmdb:Schedule:Enabled` | `false` | Queue a daily import | Administrator | no |
| `Tmdb:Schedule:TimeUtc` | `03:00` | Time of day (UTC) after which the day's import is queued | Administrator | no |
| `Tmdb:Schedule:List` | `Popular` | `Popular` or `TopRated` | Administrator | no |
| `Tmdb:Schedule:Pages` | `5` | Pages of 20 movies (1–500) | Administrator | no |

## Commerce

`CommerceOptions`.

| Key | Default (Dev / Demo) | Meaning | Who | Secret |
|---|---|---|---|---|
| `Commerce:PaymentTimeoutMinutes` | `30` | An unpaid order expires after this time (1–1440) | Administrator, Product owner | no |
| `Commerce:ExpiryScanSeconds` | `60` | How often the expiry job runs (0–3600; 0 = off, for tests) | Operator | no |
| `Commerce:SuccessUrl` | `/checkout/success?orderId={orderId}` | Where the provider sends the browser after paying; `{orderId}` is replaced | Developer | no |
| `Commerce:CancelUrl` | `/checkout/cancel?orderId={orderId}` | Where the provider sends the browser on cancel | Developer | no |
| `Commerce:WebBaseUrl` | set by the AppHost (Vite endpoint) / `PUBLIC_URL` in compose | Absolute web app URL; relative return URLs are resolved against it. Required for Stripe | Operator | no |

## Payments

`PaymentOptions` and `Payments:Fake` (`FakePaymentOptions`, Development and Demo only: the fake gateway is not
registered in `Production`).

| Key | Default (Dev / Demo) | Meaning | Who | Secret |
|---|---|---|---|---|
| `Payments:Provider` | `Fake` — Demo `Fake` (`PAYMENTS_PROVIDER`) | `Fake` (built-in fake page) or `Stripe` (test mode). `Fake` in `Production` stops the app | Administrator, Operator | no |
| `Payments:Fake:WebhookDelayMinMs` / `WebhookDelayMaxMs` | `1000` / `3000` | Random delay of each fake webhook (0 ≤ min ≤ max) | Developer | no |
| `Payments:Fake:DuplicateDeliveries` | `0` | Extra deliveries of every event (0–10), to test idempotency | Developer | no |
| `Payments:Fake:DeliverFailuresLate` | `false` | Failure events wait `LateDeliveryDelayMs` longer (out-of-order delivery) | Developer | no |
| `Payments:Fake:LateDeliveryDelayMs` | `5000` (code) | Extra delay of late failures (≥ 0) | Developer | no |
| `Payments:Fake:WebhookSecret` | empty (code) = random per process | HMAC key of fake webhooks | Developer | yes, if set |
| `Payments:Fake:WebhookBaseUrl` | empty (code) = first address the server listens on | Base URL the fake posts webhooks to | Developer | no |
| `Payments:Fake:SignatureToleranceSeconds` | `300` (code) | Accepted age of a fake signature (> 0) | Developer | no |

## Stripe

`StripeOptions` ([local setup](local-setup.md#payments-stripe-test-mode)). Registered when `Stripe:SecretKey` is set.

| Key | Default | Meaning | Who | Secret |
|---|---|---|---|---|
| `Stripe:SecretKey` | empty | Test mode key `sk_test_…`; live and restricted keys stop the app | Operator | **yes** |
| `Stripe:WebhookSecret` | empty | Signing secret `whsec_…` of the webhook endpoint (or of `stripe listen`); required when the provider is `Stripe` | Operator | **yes** |
| `Stripe:WebhookToleranceSeconds` | `300` (code) | Accepted signature age (1–3600) | Developer | no |
| `Stripe:PublishableKey` | — | Kept in user secrets and passed by compose (`STRIPE_PUBLISHABLE_KEY`), but **not read by the code** (Stripe Checkout is a hosted page) | — | treat as config, not secret |

## Assistant

`AssistantOptions` ([spec 06](../spec/06-ai-assistant.md)). Changes to limits that affect answers need an assistant
evaluation run.

| Key | Default (Dev / Demo) | Meaning | Who | Secret |
|---|---|---|---|---|
| `Assistant:MaxToolIterations` | `6` | Model calls with tools per turn, then one without (1–20) | AI engineer | no |
| `Assistant:HistoryMaxMessages` | `12` | Visible messages of earlier turns sent to the model (2–200) | AI engineer | no |
| `Assistant:HistoryTokenBudget` | `3000` | Estimated tokens of earlier turns; older turns go into the rolling summary (200–200000) | AI engineer | no |
| `Assistant:MaxConversationTokens` | `300000` — Demo `100000` | Token budget of one conversation (≥ 1000); then `budgetExceeded` | Product owner decides, Operator sets | no |
| `Assistant:MaxOwnerTokensPerDay` | `1000000` — Demo `200000` | Budget of one owner over 24 hours (≥ `MaxConversationTokens`); then `dailyBudgetExceeded` | Product owner, Operator | no |
| `Assistant:MaxTotalTokensPerDay` | `0` (off, code) — Demo `5000000` | Budget of the whole service over 24 hours (0 or ≥ `MaxOwnerTokensPerDay`); then `serviceBudgetExceeded` | Product owner, Operator | no |
| `Assistant:SearchResultLimit` | `6` | Movies `search_movies` returns when the model sets no limit (1–10) | AI engineer | no |
| `Assistant:SummaryMaxOutputTokens` | `400` | Output tokens of the rolling summary (50–4000) | AI engineer | no |
| `Assistant:ConfirmationTtlMinutes` | `15` | Minutes to press the checkout button of a confirmation (1–120) | Product owner | no |
| `Assistant:PromptVersion` | null (code) = latest | Pins `assistant-system.vN` | AI engineer | no |
| `Assistant:SummaryPromptVersion` | null (code) = latest | Pins `assistant-summary.vN` | AI engineer | no |

## AI

`AiOptions` ([AI engineer guide](ai-engineer.md#chat-and-embedding-profiles), [ADR-0011](../adr/0011-ai-resilience-and-fallback.md)).
Validated on start: the active profiles must exist, the active and fallback profiles need an `ApiKey`.

| Key | Default (Dev / Demo) | Meaning | Who | Secret |
|---|---|---|---|---|
| `AI:ActiveChatProfile` | `local` — Demo `openrouter` (`CHAT_PROFILE`) | Chat profile behind `IChatClient` | AI engineer | no |
| `AI:ActiveEmbeddingProfile` | `local` — Demo `openrouter` (`EMBEDDING_PROFILE`) | Embedding profile behind `IEmbeddingGenerator`; switching needs a reindex | AI engineer | no |
| `AI:FallbackChatProfile` | empty | Chat profile that answers when the active one fails before streaming; must differ from the active one and support tools | AI engineer, Operator | no |
| `AI:QueryEmbeddingCacheMinutes` | `1440` | Query vector cache lifetime (0–10080; 0 = off) | Operator | no |
| `AI:QueryEmbeddingCacheMaxEntries` | `10000` | Most cached query vectors (0–1000000) | Operator | no |
| `AI:EnableSensitiveData` | `false` (code) — Dev `true`, Demo `false` | Record prompt, completion, tool arguments and results in traces | Operator | no |
| `AI:LogRawHttp` | `false` | Log raw provider HTTP traffic; works only in Development; keys are never logged | Developer | no |
| `AI:HealthChecksEnabled` | `true` (code) | Register the provider health checks (Degraded on failure) | Operator | no |

**Chat profiles** `AI:ChatProfiles:<name>:…` (`ChatProfileOptions`). Configured: `local`, `openrouter`.

| Key | Default (code) | `local` | `openrouter` | Meaning | Who | Secret |
|---|---|---|---|---|---|---|
| `Endpoint` | — | `http://localhost:1234/v1` (compose: `LMSTUDIO_URL`) | `https://openrouter.ai/api/v1` | OpenAI-compatible base URL (absolute http(s)) | AI engineer, Operator | no |
| `Model` | — | `google/gemma-4-e4b` | `deepseek/deepseek-v4.1-flash-1017` | Model id at the provider (required) | AI engineer | no |
| `ApiKey` | — | `lm-studio` | — (`OPENROUTER_API_KEY`) | API key; required for the active and the fallback profile | Operator | **yes** (cloud) |
| `ModelsPath` | `/models` | | | Model list read by the health check | AI engineer | no |
| `Temperature` | null | `0.3` | `0.3` | Default temperature (0–2) | AI engineer | no |
| `MaxOutputTokens` | null | `1024` | `1024` | Default output limit (> 0) | AI engineer | no |
| `ReasoningEffort` | null (model default) | `None` | `None` | `None`, `Low`, `Medium`, `High`, `ExtraHigh`; `None` turns thinking off | AI engineer | no |
| `SupportsTools` | `true` | `true` | `true` | The assistant refuses (`toolsNotSupported`) with a profile that cannot call tools | AI engineer | no |
| `TimeoutSeconds` | `120` | `300` | `120` | Timeout of one attempt (> 0) | Operator | no |
| `MaxRetries` | `2` | | | Retries of connection errors, 429, 502–504 (0–10) | Operator | no |
| `RetryDelayMs` | `1000` | | | Base delay of the exponential backoff (1–60000) | Operator | no |
| `CircuitBreakSeconds` | `30` | | | Open time of the circuit after half of ≥ 3 attempts in a minute failed (1–3600) | Operator | no |

**Embedding profiles** `AI:EmbeddingProfiles:<name>:…` (`EmbeddingProfileOptions`): the connection and resilience keys
above (`TimeoutSeconds` defaults to `60`), plus:

| Key | `local` | `nomic` | `openrouter` | Meaning | Who | Secret |
|---|---|---|---|---|---|---|
| `Model` | `text-embedding-embeddinggemma-300m` | `text-embedding-nomic-embed-text-v1.5` | `qwen/qwen3-embedding-8b` | Embedding model | AI engineer | no |
| `Endpoint` | `http://localhost:1234/v1` | `http://localhost:1234/v1` | `https://openrouter.ai/api/v1` | | AI engineer, Operator | no |
| `ApiKey` | `lm-studio` | `lm-studio` | — (`OPENROUTER_API_KEY`) | | Operator | **yes** (cloud) |
| `ModelsPath` | `/models` | `/models` | `/embeddings/models` | | AI engineer | no |
| `Dimensions` | `768` | `768` | `1024` | Vector length (> 0); stored with every vector | AI engineer | no |
| `QueryPrefix` | `task: search result \| query: ` | `search_query: ` | `Instruct: Given a movie search query, retrieve movies that match it\nQuery: ` | Text before a search query | AI engineer | no |
| `DocumentPrefix` | `title: none \| text: ` | `search_document: ` | empty | Text before a movie text | AI engineer | no |

**Pricing** `AI:Pricing:<model id>:InputPerMTokUsd` / `OutputPerMTokUsd` (USD per million tokens, ≥ 0) for the cost
estimate (`ai.cost_usd`, `estimated_cost_usd`). Configured: `anthropic/claude-haiku-5.5`, `openai/gpt-6-luna`,
`deepseek/deepseek-v4.1-flash-1017`, `qwen/qwen3.8-flash`, `z-ai/glm-5.3-flash`, `qwen/qwen3-embedding-8b`. A model
without an entry (local models) has no estimate; ids containing `:` cannot be keys. Who: AI engineer, Product owner.

## Auth

`Auth:Admin` (`AdminAccountOptions`). Password policy, lockout and cookies are fixed in code
([ADR-0010](../adr/0010-authentication-identity-cookie.md)).

| Key | Default (Dev / Demo) | Meaning | Who | Secret |
|---|---|---|---|---|
| `Auth:Admin:Email` | empty — Dev `admin@filmsearch.local`, Demo `ADMIN_EMAIL` | Admin account created on startup with the role `admin` | Operator | no |
| `Auth:Admin:Password` | empty | Its password; without it no admin account is created (warning in the log). An existing account with that email gets the role only if the password matches | Operator | **yes** |

## Langfuse

`LangfuseOptions` of the API, read also by ServiceDefaults for the trace exporter. Set by the AppHost when
`Langfuse:Enabled` is true; Langfuse is on only when all three are set.

| Key | Default | Meaning | Who | Secret |
|---|---|---|---|---|
| `Langfuse:BaseUrl` | empty | Langfuse URL (`http://localhost:3000` from the AppHost) | Operator | no |
| `Langfuse:PublicKey` | empty | Project public key `pk-lf-…` | Operator | no |
| `Langfuse:SecretKey` | empty | Project secret key `sk-lf-…` | Operator | **yes** |

## AppHost

`src/FilmSearch.AppHost` (Development only). Its `appsettings.json` has only log levels.

| Key | Default | Meaning | Who | Secret |
|---|---|---|---|---|
| `Langfuse:Enabled` | unset (off) | `true` starts six Langfuse containers and wires the API to them: `dotnet user-secrets set Langfuse:Enabled true --project src/FilmSearch.AppHost` | Developer, AI engineer | no |
| `Parameters:postgres-password` | generated by Aspire, kept in the AppHost user secrets | Password of the `filmsearch-postgres` container | Developer | **yes** |
| `Parameters:langfuse-*` (`admin-password`, `public-key`, `secret-key`, `postgres-password`, `clickhouse-password`, `redis-password`, `minio-password`, `nextauth-secret`, `salt`, `encryption-key`) | generated once, persisted in the AppHost user secrets | Langfuse credentials; log in as `admin@filmsearch.local` with `langfuse-admin-password` | Developer | **yes** |

The AppHost passes to the API and the Worker: `ConnectionStrings__filmsearch`, `Commerce__WebBaseUrl` (the Vite
endpoint), the OTLP variables of the dashboard and, with Langfuse, `Langfuse__BaseUrl` / `PublicKey` / `SecretKey`
(API only).

## deploy/.env

The demo server's settings ([deploy/README.md](../../deploy/README.md)); `compose.yaml` maps them to the containers.

| Variable | Example / default | Becomes | Who | Secret |
|---|---|---|---|---|
| `IMAGE_PREFIX` | `ghcr.io/<github-owner>/filmsearch` (local: `filmsearch`) | Image names `<prefix>-api`, `-worker`, `-web` | Operator | no |
| `FILMSEARCH_TAG` | `v0.20.0` (local: `local`) | Image tag; `deploy.sh` rewrites it after a successful deploy | Operator | no |
| `SITE_ADDRESS` | `demo.example.com` (local: `localhost`) | Caddy site address; Let's Encrypt certificate for it | Operator | no |
| `PUBLIC_URL` | `https://demo.example.com` | `Commerce__WebBaseUrl` | Operator | no |
| `POSTGRES_PASSWORD` | — (letters and digits) | Postgres password and `ConnectionStrings__filmsearch`; required | Operator | **yes** |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD` | `admin@example.com`, — | `Auth__Admin__Email`, `Auth__Admin__Password` (API) | Operator | password: **yes** |
| `CHAT_PROFILE` | `openrouter` | `AI__ActiveChatProfile` | AI engineer, Operator | no |
| `EMBEDDING_PROFILE` | `openrouter` | `AI__ActiveEmbeddingProfile` | AI engineer, Operator | no |
| `OPENROUTER_API_KEY` | — | `AI__ChatProfiles__openrouter__ApiKey` and `AI__EmbeddingProfiles__openrouter__ApiKey`; required while an `openrouter` profile is active (the app does not start without it). Use a key with a credit limit | Operator | **yes** |
| `LMSTUDIO_URL` | `http://host.docker.internal:1234/v1` | `Endpoint` of the `local` chat and embedding profiles | Operator | no |
| `COMPOSE_PROFILES` | empty (`dashboard` starts the Aspire dashboard) | Optional services | Operator | no |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | empty (`http://dashboard:18889` with the dashboard) | Telemetry export of API, Worker, migrate | Operator | no |
| `PAYMENTS_PROVIDER` | `Fake` | `Payments__Provider` | Administrator, Operator | no |
| `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET` | — | `Stripe__SecretKey`, `Stripe__WebhookSecret` | Operator | **yes** |
| `STRIPE_PUBLISHABLE_KEY` | — | `Stripe__PublishableKey` (not read by the code) | Operator | no |
| `TMDB_API_KEY` | — | `Tmdb__ApiKey` | Operator | **yes** |
| `HTTP_PORT`, `HTTPS_PORT` | `80`, `443` (compose defaults; not in `.env.example`) | Host ports of Caddy; `HTTP_PORT=8080` when port 80 is taken (Windows) | Operator | no |
| `SKIP_PULL` | unset (shell variable of `deploy.sh`) | `1`: use locally built images | Operator | no |
| `IMAGE_SOURCE` | unset (set by the release workflow) | OCI label linking GHCR packages to the repository (`compose.build.yaml`) | — | no |

Fixed in `compose.yaml` (not in `.env`): `ASPNETCORE_ENVIRONMENT=Demo`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (API),
`OTEL_SERVICE_NAME`, the data protection volume at `/var/lib/filmsearch/keys`.

---

**See also:** [Operator](operator.md) · [AI engineer](ai-engineer.md) · [Developer](developer.md) ·
[Administrator](administrator.md) · [Product owner](product-owner.md) · [Customer](customer.md) ·
[Local setup](local-setup.md) · [Deploy runbook](../../deploy/README.md) ·
[Spec 10 §2 Configuration and secrets](../spec/10-ops-security.md#2-configuration-and-secrets) ·
[ADR-0011](../adr/0011-ai-resilience-and-fallback.md) · [ADR-0012](../adr/0012-deployment-docker-compose-vps.md)
