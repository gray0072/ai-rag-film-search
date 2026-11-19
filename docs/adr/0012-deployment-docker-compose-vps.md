# ADR-0012 — Deployment: Docker Compose on one host, Caddy in front, images from CI, migrations by the Worker

- **Status:** Accepted
- **Date:** 2026-11-19
- **Iteration:** I20

## Context
The app must run outside the dev machine as a demo (spec 10 §6, NFR-7, NFR-9): the API, the Worker, the static web app
and PostgreSQL with pgvector, behind HTTPS, with a cloud chat model (the server has no GPU and no LM Studio), deployed
by CI with a way back. It is a pet project: the cost should be a few euros a month, and the setup should teach what
any deployment needs (images, configuration per environment, secrets, migrations, health, rollback) rather than one
cloud's console. Aspire runs the app locally (ADR-0005), but its publishers target specific platforms.

The AI parts add two questions: where embeddings come from without LM Studio, and how to cap the spend of a public chat
that anyone can open (every browser is a new anonymous owner, so the per-owner budget of I18 does not cap the total).

## Options
1. **Azure Container Apps through `azd` and Aspire**: Aspire describes the app, `azd up` creates Container Apps, Azure
   PostgreSQL Flexible Server, a registry and managed identities. Little YAML, scale to zero. But about $20–30 a month
   (the database does not scale to zero), the deployment is tied to Azure and hides much of what happens, and the only
   subscription at hand belongs to a company.
2. **One VPS with Docker Compose**: PostgreSQL, API, Worker, Caddy (automatic Let's Encrypt certificates, static files,
   reverse proxy) and Ollama for embeddings, images built by GitHub Actions and pushed to GHCR, deploy over SSH.
   ~€5 a month, portable to any host, every piece visible. But one machine (no high availability), we run backups and
   upgrades ourselves.
3. **Kubernetes (k3s) on a VPS**: the production standard, but a cluster for five containers is far more to learn and
   operate than the demo needs.

Migrations:
- a. **EF Core migrations bundle** (`dotnet ef migrations bundle`): a standalone executable per release. Tried first:
  the bundle failed to create the DbContext (`System.Diagnostics.DiagnosticSource` 11.0 that OpenTelemetry needs was
  not in the bundle, with either a self-contained or a framework-dependent bundle and either startup project).
- b. **A `migrate` command of the Worker** (`dotnet FilmSearch.Worker.dll migrate`): the same composition, connection
  string and advisory lock as the app (`MigrateDatabaseAsync`), no extra image, runs as a one-off container.
- c. Apply on API startup (as in Development): simplest, but every replica races, and a failed migration takes the
  API down instead of stopping the deploy before it.

Embeddings without LM Studio:
- i. **Ollama on the server with EmbeddingGemma** (the dev model): no per-call cost, the vectors and the evaluation
  numbers of development still hold. ~1.5 GB RAM, CPU only (a query embeds in ~0.1 s, the catalog in minutes).
- ii. **OpenRouter embeddings** (Qwen3-Embedding-8B at 1024 dimensions, $0.01 per 1M tokens): no model on the
  server, but another model means a full reindex and an evaluation, and every query is a network call that can fail.

## Decision
Option 2 with migrations b and both embedding options, i by default (the owner's choice).

- `deploy/`: `compose.yaml` (postgres, ollama + ollama-pull, migrate, api, worker, web, optional dashboard),
  `compose.build.yaml` (build from source), `.env.example`, `Caddyfile`, Dockerfiles, `deploy.sh` / `backup.sh` /
  `restore.sh` and a runbook ([deploy/README.md](../../deploy/README.md)).
- Images: chiseled .NET images (`aspnet:10.0-noble-chiseled-extra`: no shell, non-root, ICU for `ru` / `sv`) for the
  API and the Worker; the web image is Caddy with the Vite build. One origin for the browser (Caddy proxies `/api`,
  `/fake-checkout`, `/health`): no CORS, cookies as in development. The API trusts `X-Forwarded-*` from Caddy
  (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`) and does not redirect to HTTPS itself there.
- Environment **`Demo`** (`appsettings.Demo.json`): production behavior (no OpenAPI UI, HSTS, no prompt content in
  telemetry, migrations not on startup), OpenRouter chat, Ollama embeddings, stricter rate limits and budgets. It is
  not `Production` because the fake payment gateway, refused in Production (ADR-0004), is the default of the demo;
  Stripe test mode is a setting.
- Data protection keys in a volume (`DataProtection:KeysPath`): sign-ins survive deploys.
- Spend caps: a service-wide daily token budget of the assistant (`Assistant:MaxTotalTokensPerDay`, new error code
  `serviceBudgetExceeded`), lower per-owner and per-conversation budgets, the rate limits of I19, and a credit limit on
  a separate OpenRouter key (the hard cap, it also covers query understanding).
- CI (`.github/workflows/release.yml`): on a `v*` tag build the images, start the whole stack on the runner and run the
  Playwright smoke tests (`src/web/e2e`) against it, push to GHCR, then — when the `demo` environment has a host —
  deploy over SSH and run the smoke tests against the public URL.
- Rollback: `./deploy.sh <previous tag>`; migrations must be backward compatible (expand / contract); across a
  migration that is not, `./restore.sh <dump> <previous tag>` with the dump `deploy.sh` takes before every migration.

## Consequences
- Easier: a production-like stack runs on any machine with Docker (`SKIP_PULL=1 ./deploy.sh local`), and the release
  workflow tests exactly the images it ships. The demo's search quality is the measured one (Ollama EmbeddingGemma
  scored the same as LM Studio, report `2026-11-19_search_i20-ollama-embeddinggemma.md`).
- Harder: one host is a single point of failure; backups are dumps on the same disk unless copied off; OS, Docker and
  image updates are ours. A deploy restarts the API (seconds of held requests, Caddy waits up to 30 s).
- No public URL yet (owner's decision for I20): the deploy job is skipped until the `demo` environment is configured.
- Watch: memory of Ollama on a small VM; the reindex time on few vCPUs; OpenRouter spend (cost metrics of I18).
- Reversal: the images and the configuration are platform-neutral, so Azure Container Apps or Kubernetes can run the
  same images later (managed PostgreSQL with pgvector, the `migrate` command as a job, Caddy replaced by the
  platform's ingress).
