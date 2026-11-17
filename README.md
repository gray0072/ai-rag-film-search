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

The skeleton is in place: Aspire AppHost, PostgreSQL + pgvector, an API with health checks (I01), a React app with routing, i18n (`en`, `ru`, `sv`) and a typed API client (I02), and the catalog: movies, genres, people and offers with filters, sorting and localized texts, loaded from a committed seed file of 1000 TMDB movies (I03); keyword search in English, Russian and Swedish with the same filters, `POST /api/search` (I04); the AI provider layer: `IChatClient` / `IEmbeddingGenerator` from config profiles (LM Studio or OpenRouter), GenAI traces with tokens and cost, and an AI playground console with a model benchmark (I05); tests and CI for all of it. Seed details: [data/seed](data/seed/README.md). See the [roadmap](docs/plan/roadmap.md).

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

The console prints the Aspire dashboard login URL (`https://localhost:17180/login?t=...`). The dashboard shows the `postgres` container, the `filmsearch` database, the `api` project and the `web` app with logs, traces and metrics. Open the **web** URL from the dashboard to use the app; before the first start the AppHost runs `npm install` for it, which takes a minute. The web app's dev server proxies `/api` to the API.

The API listens on `https://localhost:7180`:

- `GET /health`: readiness (includes the database), `GET /alive`: liveness
- `GET /api/info`: application name and version
- `GET /api/movies`, `GET /api/movies/{id}`, `GET /api/genres`: catalog; texts follow `Accept-Language` (`en`, `ru`, `sv`)
- `POST /api/search` with `{ "query": "nolan dream", "debug": true }`: keyword search (filters as in `GET /api/movies`)
- `POST /api/admin/ingestion/seed`: reload the seed file (Development only, idempotent)
- `POST /api/admin/ai/chat` with `{ "message": "Hi", "profile": "local" }`: one call to a chat profile, with token usage (Development only); the call shows up as a GenAI trace in the dashboard
- `GET /openapi/v1.json`: OpenAPI document (Development)

On first start the API applies EF Core migrations and, if the catalog is empty, loads `data/seed/movies.seed.json` (Development only). The Postgres container `filmsearch-postgres` is persistent: it keeps running after you stop the AppHost, and its data lives in the Docker volume `filmsearch-postgres-data`.

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

`/health` reports `Degraded` (not `Unhealthy`) when LM Studio is off: the store works without AI. To try chat, streaming, tools, JSON output and embeddings from the console, or to benchmark another model: [tools/ai-playground](tools/ai-playground/README.md). Stripe test keys (for I15) also live in the API user secrets: `Stripe:SecretKey`, `Stripe:PublishableKey`.

More commands (migrations, single test project) are in [AGENTS.md](AGENTS.md#commands). Rebuilding the seed file from TMDB: [data/seed/README.md](data/seed/README.md).

---

This product uses the TMDB API but is not endorsed or certified by TMDB.
