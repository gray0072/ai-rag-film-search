# FilmSearch — AI-assisted movie store

A learning pet project, built to be as close to production as practical. It is an online store of digital movies with:

- **Search**: filters, keyword (PostgreSQL full-text), semantic (embeddings + pgvector), hybrid (RRF), and natural-language query understanding.
- **AI sales assistant**: a chat that finds movies in the catalog, explains its picks, manages the cart and leads to checkout with an explicit user confirmation.
- **Payments**: a fake gateway first, then Stripe in test mode (Checkout + webhooks).
- **Data**: a seed of about 1000 top movies, then TMDB sync.
- **Local-first AI**: LM Studio for chat and embeddings; OpenRouter or any OpenAI-compatible provider through config.
- **Languages**: English UI and data, with Russian and Swedish supported in the UI, in movie texts and in queries to the AI.

Stack: .NET 10, ASP.NET Core, EF Core, PostgreSQL + pgvector, .NET Aspire, Microsoft.Extensions.AI, React + TypeScript + Vite.

## Status

The skeleton is in place: Aspire AppHost, PostgreSQL + pgvector, an API with health checks (I01), a React app with routing, i18n (`en`, `ru`, `sv`) and a typed API client (I02), and the catalog: movies, genres, people and offers with filters, sorting and localized texts, loaded from a committed seed file of 1000 TMDB movies (I03); keyword search in English, Russian and Swedish with the same filters, `POST /api/search` (I04); the AI provider layer: `IChatClient` / `IEmbeddingGenerator` from config profiles (LM Studio or OpenRouter), GenAI traces with tokens and cost, and an AI playground console with a model benchmark (I05); movie embeddings in pgvector and semantic search across English, Russian and Swedish, similar movies, a background reindex job (I06); hybrid search (keyword + semantic fused with RRF, the default mode) and LLM query understanding that turns free text into filters with structured output, retry and fallback (I07); cart, orders and a fake payment provider (I09), the store UI (I10) and Stripe Checkout in test mode with signed webhooks (I15); the assistant chat API: conversations, answers streamed over SSE, an own agent loop with a `search_movies` tool so recommendations come only from the catalog, rolling summary, token usage and cost per message, feedback (I11); a Worker process with a job queue in PostgreSQL that imports movies from TMDB (lists or ids, rate-limited, idempotent) and keeps embeddings fresh, and runs order expiry (I16); tests and CI for all of it. Seed details: [data/seed](data/seed/README.md). See the [roadmap](docs/plan/roadmap.md).

## Documentation

| | |
|---|---|
| [Spec](docs/spec/00-overview.md) | What we build and how it should behave |
| [Roadmap](docs/plan/roadmap.md) | Iterations, dependencies, parallel waves, status |
| [ADRs](docs/adr/README.md) | Architecture decisions |
| [Learning path](docs/learning-path.md) | AI-engineering skills mapped to iterations |
| [AGENTS.md](AGENTS.md) | Rules for AI coding agents |
| [Prompt history](docs/prompts/README.md) | All prompts used to build the project, by session |

## Getting started

### Prerequisites

| Tool | Needed for | Since |
|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100 or later) | build, run, tests | I01 |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | PostgreSQL through Aspire, integration tests (Testcontainers) | I01 |
| Trusted HTTPS dev certificate: `dotnet dev-certs https --trust` | Aspire dashboard and API over HTTPS | I01 |
| [Node.js](https://nodejs.org/) 24 LTS (npm 11) | web app | I02 |
| [LM Studio](https://lmstudio.ai/) with `google/gemma-4-e4b` and `text-embedding-embeddinggemma-300m`, local server on `http://localhost:1234/v1` | AI features | I05 |

No Aspire workload or Aspire CLI is needed: the AppHost uses the `Aspire.AppHost.Sdk` NuGet SDK.

### Run

```bash
dotnet run --project src/FilmSearch.AppHost
```

The console prints the Aspire dashboard login URL (`https://localhost:17180/login?t=...`). The dashboard shows the `postgres` container, the `filmsearch` database, the `api` and `worker` projects and the `web` app with logs, traces and metrics. Open the **web** URL from the dashboard to use the app; before the first start the AppHost runs `npm install` for it, which takes a minute. The web app's dev server proxies `/api` to the API.

The API listens on `https://localhost:7180`:

- `GET /health`: readiness (includes the database), `GET /alive`: liveness
- `GET /api/info`: application name and version
- `GET /api/movies`, `GET /api/movies/{id}`, `GET /api/genres`: catalog; texts follow `Accept-Language` (`en`, `ru`, `sv`)
- `POST /api/search` with `{ "query": "nolan dream", "debug": true }`: keyword search (filters as in `GET /api/movies`); `"mode": "semantic"` searches by meaning (`{ "query": "a heist inside dreams", "mode": "semantic" }`, needs LM Studio)
- `GET /api/movies/{id}/similar`: movies nearest to this one by embedding
- `POST /api/admin/ingestion/seed`: reload the seed file (Development only, idempotent)
- `GET /api/admin/embeddings`, `POST /api/admin/embeddings/reindex`, `DELETE /api/admin/embeddings?model=`: embedding index state, a reindex job for the Worker, deleting an old model's vectors (Development only)
- `POST /api/admin/ingestion/tmdb` with `{ "list": "topRated", "pages": 50 }` or `{ "tmdbIds": [27205] }`: a TMDB import job for the Worker (needs a TMDB key, see below); `GET /api/admin/jobs`, `GET /api/admin/jobs/{id}`, `POST /api/admin/jobs/{id}/cancel`: background jobs with progress and counters (Development only)
- `POST /api/admin/ai/chat` with `{ "message": "Hi", "profile": "local" }`: one call to a chat profile, with token usage (Development only); the call shows up as a GenAI trace in the dashboard
- `POST /api/chat/conversations`, then `POST /api/chat/conversations/{id}/messages` with `{ "text": "Recommend a space movie" }`: the assistant's answer as server-sent events (needs a chat model; examples in [Assistant.http](src/FilmSearch.Api/Assistant.http)); `GET /api/chat/conversations/{id}`: the history with movie cards
- `GET /openapi/v1.json`: OpenAPI document (Development)

On first start the API applies EF Core migrations and, if the catalog is empty, loads `data/seed/movies.seed.json` (Development only). Then it queues a reindex job that the Worker runs: it embeds every movie whose embedding text changed (about 30 s for the whole seed with LM Studio running; with LM Studio off the job fails, keyword search works, and the next start or `POST /api/admin/embeddings/reindex` catches up). The Worker (`src/FilmSearch.Worker`) runs every background job from the table `ingestion.jobs` and expires unpaid orders; it reads the API's appsettings files and user secrets. The Postgres container `filmsearch-postgres` is persistent: it keeps running after you stop the AppHost, and its data lives in the Docker volume `filmsearch-postgres-data`.

### Build and test

```bash
dotnet build
dotnet test                          # unit + integration tests (Docker must be running)
dotnet format --verify-no-changes    # the same format check as CI
```

Web app (in `src/web`):

```bash
npm install
npm run dev                          # Vite dev server without Aspire; needs the API on https://localhost:7180
npm run lint && npm run typecheck && npm test && npm run build
npm run gen:api                      # regenerate API types after an API change (the API build writes openapi/v1.json)
```

### AI providers

Start LM Studio's server and load the default models (or use the LM Studio app):

```bash
lms server start
lms load google/gemma-4-e4b -y
lms load text-embedding-embeddinggemma-300m -y
```

The `AI` section of `src/FilmSearch.Api/appsettings.json` has the profiles; switching provider or model is a config change (`AI:ActiveChatProfile`, `AI:ActiveEmbeddingProfile`). For OpenRouter put the key in user secrets and select the profile:

```bash
dotnet user-secrets set AI:ChatProfiles:openrouter:ApiKey <key> --project src/FilmSearch.Api
dotnet user-secrets set AI:ActiveChatProfile openrouter --project src/FilmSearch.Api
```

`/health` reports `Degraded` (not `Unhealthy`) when LM Studio is off: the store works without AI, and semantic search returns 503. To try another embedding model: reindex with its profile (`{ "profile": "nomic" }`), switch `AI:ActiveEmbeddingProfile`, then delete the old model's vectors ([spec 08 §4](docs/spec/08-data-ingestion.md#4-embeddings-pipeline)). To try chat, streaming, tools, JSON output and embeddings from the console, or to benchmark another model: [tools/ai-playground](tools/ai-playground/README.md).

### Payments: Stripe test mode

By default checkout uses the fake payment page inside the API (`Payments:Provider = Fake`). To pay on a real Stripe Checkout page in test mode ([spec 07 §4](docs/spec/07-payments.md#4-stripe-test-mode-i15)):

1. Create a Stripe account (a sandbox is enough) and copy the **test** secret key from Developers → API keys. Live keys are refused at startup: only `sk_test_…` is accepted.
2. Install the Stripe CLI (`winget install Stripe.StripeCli`) and get the webhook signing secret of the CLI: `stripe listen --api-key sk_test_… --print-secret`.
3. Put both in the API user secrets and switch the provider (keys never go to files in the repository):

   ```bash
   dotnet user-secrets set Stripe:SecretKey sk_test_... --project src/FilmSearch.Api
   dotnet user-secrets set Stripe:WebhookSecret whsec_... --project src/FilmSearch.Api
   dotnet user-secrets set Payments:Provider Stripe --project src/FilmSearch.Api
   ```

4. Run the AppHost and, in a second terminal, forward Stripe's events to the API:

   ```bash
   stripe listen --api-key sk_test_... --skip-verify      --events checkout.session.completed,checkout.session.async_payment_succeeded,checkout.session.async_payment_failed,checkout.session.expired      --forward-to https://localhost:7180/api/payments/webhooks/stripe
   ```

Test cards (any future date, any CVC): `4242 4242 4242 4242` succeeds, `4000 0000 0000 9995` is declined (the page shows the error, the order stays pending and you can try another card), `4000 0025 0000 3155` asks for 3-D Secure. `stripe events resend evt_…` replays an event: the order is not paid twice. Stripe sends the browser back to absolute URLs; the AppHost passes the web app's address to the API (`Commerce:WebBaseUrl`). To go back to the fake page: `dotnet user-secrets remove Payments:Provider --project src/FilmSearch.Api`.

### Movies from TMDB

The seed has 1000 movies. To import more from TMDB ([spec 08 §3](docs/spec/08-data-ingestion.md#3-tmdb-import-i16)), get a free API key at themoviedb.org (Settings → API; the v4 read access token or the v3 key) and put it in the API user secrets, which the Worker shares:

```bash
dotnet user-secrets set Tmdb:ApiKey <token> --project src/FilmSearch.Api
```

Then queue an import (`POST /api/admin/ingestion/tmdb` with `{ "list": "topRated", "pages": 50 }`, about 5 minutes at 40 requests per 10 seconds) and follow it with `GET /api/admin/jobs/{id}`. The job upserts by TMDB id (a rerun changes nothing that did not change at TMDB) and embeds the new and changed movies before it ends, so they are searchable in every mode. A daily import can be switched on with `Tmdb:Schedule:Enabled` (off by default; `Tmdb:Schedule` in appsettings).

### Evaluations

Search quality is measured on golden datasets (Docker and LM Studio needed; each run uses a fresh database container):

```bash
dotnet run --project evals/FilmSearch.Evals -- --suite search --label my-change   # Recall@k, MRR, nDCG per mode, locale and tag
dotnet run --project evals/FilmSearch.Evals -- --suite qu --label my-change       # query understanding accuracy
```

Reports go to `evals/reports/`; options, metrics and dataset rules: [evals/README.md](evals/README.md).

More commands (migrations, single test project) are in [AGENTS.md](AGENTS.md#commands). Rebuilding the seed file from TMDB: [data/seed/README.md](data/seed/README.md).

---

This product uses the TMDB API but is not endorsed or certified by TMDB.
