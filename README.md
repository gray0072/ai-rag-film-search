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

The solution skeleton (I01) is in place: Aspire AppHost, PostgreSQL + pgvector, an API with health checks, tests and CI. See the [roadmap](docs/plan/roadmap.md).

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
| Node.js LTS | web app | I02 |
| [LM Studio](https://lmstudio.ai/) with a chat model and a multilingual embedding model, local server on `http://localhost:1234/v1` | AI features | I05 |

No Aspire workload or Aspire CLI is needed: the AppHost uses the `Aspire.AppHost.Sdk` NuGet SDK.

### Run

```bash
dotnet run --project src/FilmSearch.AppHost
```

The console prints the Aspire dashboard login URL (`https://localhost:17180/login?t=...`). The dashboard shows the `postgres` container, the `filmsearch` database and the `api` project with logs, traces and metrics. The API listens on `https://localhost:7180`:

- `GET /health`: readiness (includes the database), `GET /alive`: liveness
- `GET /api/info`: application name and version
- `GET /openapi/v1.json`: OpenAPI document (Development)

On first start the API applies EF Core migrations (Development only). The Postgres container `filmsearch-postgres` is persistent: it keeps running after you stop the AppHost, and its data lives in the Docker volume `filmsearch-postgres-data`.

### Build and test

```bash
dotnet build
dotnet test                          # unit + integration tests (Docker must be running)
dotnet format --verify-no-changes    # the same format check as CI
```

More commands (migrations, single test project) are in [AGENTS.md](AGENTS.md#commands).

---

This product uses the TMDB API but is not endorsed or certified by TMDB.
