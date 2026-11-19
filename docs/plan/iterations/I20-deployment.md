# I20 — Containers and deployment

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I20 · ← [I19](I19-resilience-caching.md) · [I21](I21-documentation.md) → · [Journal: what happened](../../journal/m5-production-ready.md#i20--containers-and-deployment)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I15, I17 |
| **Parallel with** | I19 |
| **Spec** | [10 Ops §5–6](../../spec/10-ops-security.md#6-deployment-i20) |
| **Requirements** | NFR-7, NFR-9 |

## Goal
The app runs as containers outside the dev machine (demo environment) with a cloud AI profile, built and deployed by CI.

## You will learn
Shipping an AI app: configuration per environment, secrets, migrations in deployment, costs of hosted models.

## Tasks
- [x] Choose target (VPS + Docker Compose or Azure Container Apps through `azd`) → ADR.
- [x] Dockerfiles (Api, Worker, Web static build behind a reverse proxy), `deploy/` folder.
- [x] Migrations as a separate step / bundle.
- [x] Production config: OpenRouter profile, Stripe test keys, CORS, HTTPS, prompt content logging off.
- [x] CI: build images on tag, push, deploy. Playwright e2e smoke against the deployed app.
- [x] Spending limits on the OpenRouter key; README section "Demo environment".

## Acceptance criteria
- [ ] Public demo URL works with the fake or Stripe test gateway. **Not done, by the owner's decision** (no server
  for now). The same images and compose file run production-like on the dev machine behind Caddy with HTTPS, and the
  Playwright smoke tests pass there with the fake gateway and OpenRouter chat. The deploy job switches on when the
  GitHub environment `demo` gets a host.
- [x] Rollback procedure documented ([deploy/README.md](../../../deploy/README.md#rollback)) and tried locally.

## Result

### What was done
- **Target** ([ADR-0012](../../adr/0012-deployment-docker-compose-vps.md)): one Linux host with Docker Compose, Caddy
  in front, images in GHCR, deploy over SSH. Azure Container Apps was the alternative. It costs more, ties the
  deployment to one cloud, and the only subscription at hand belongs to a company. The owner chose to prepare everything
  without a public URL for now, and to support both embedding options with local embeddings as the default.
- **`deploy/`**:
  - `compose.yaml`: `postgres` (pgvector pg17), `ollama` + `ollama-pull` (Ollama 0.40.16, `embeddinggemma:300m`,
    profile `ollama`), `migrate`, `api`, `worker`, `web`, and an optional `dashboard` (Aspire dashboard 13.6 on the
    loopback interface). `compose.build.yaml` builds the images from source. `.env.example` lists every setting.
  - `docker/dotnet.Dockerfile` has the targets `api` and `worker`: a restore layer, publish, then chiseled
    `aspnet:10.0-noble-chiseled-extra` images (non-root, no shell, with ICU). `docker/web.Dockerfile` builds with
    Node 24 and serves with Caddy 2.11. Image sizes: API 282 MB, Worker 275 MB, web 93 MB. The root `.dockerignore`
    keeps secrets, tests and docs out of the build context.
  - `Caddyfile`:
    - TLS: Let's Encrypt, or Caddy's local CA for `localhost`.
    - HSTS and security headers; a `no-cache` app shell and immutable assets; SPA routes.
    - `/api`, `/fake-checkout`, `/health` and `/alive` go to the API, with an active health check and
      `lb_try_duration 30s`: requests wait while the API restarts instead of failing.
  - `deploy.sh <tag>`: pull, start PostgreSQL, `backup.sh`, `migrate`, start, wait for Healthy or Degraded, then record
    the tag and a line in `deploy-history.log`. `SKIP_PULL=1` uses images built on the machine.
  - `backup.sh` dumps the database with `pg_dump` and keeps the last 10 dumps. `restore.sh <dump> <tag>` drops the
    database, restores the dump and deploys the given tag.
  - Runbook: `deploy/README.md`.
- **Migrations as a separate step:** `dotnet FilmSearch.Worker.dll migrate` (`WorkerHost.IsMigrateCommand`) applies
  pending migrations with the advisory lock and exits. Compose runs it as the one-off `migrate` service (Worker image),
  and the API and the Worker wait for it. The environment `Demo` does not migrate on startup.
- **Configuration** (`appsettings.Demo.json`, linked into the Worker):
  - Chat profile `openrouter`.
  - Embedding profile `ollama`. It is new in `appsettings.json` with `localhost:11434`; the demo points it at
    `http://ollama:11434/v1`.
  - The alternative is the new `openrouter` embedding profile: Qwen3-Embedding-8B, 1024 dimensions, with an
    instruction prefix on queries.
  - Seed and embedding index on first start. `EnableSensitiveData` and `LogRawHttp` off.
  - Rate limits per minute: chat 10, new conversations 5, search 30.
  - Token budgets: 100k per conversation, 200k per owner and day, 5M for the whole service per day.
- **HTTPS, proxy, CORS:** Caddy terminates TLS. The API trusts `X-Forwarded-*` (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`),
  so the rate limits see the client IP and the cookies stay `Secure`. There it also skips its own HTTPS redirect,
  because Caddy's health probes are plain HTTP. One origin, so no CORS policy.
- **Data protection keys** live in a volume (`DataProtection:KeysPath`, `DataProtectionSetup`), so sign-ins survive a
  redeploy. Checked: a signed-in user stayed signed in after the API container was recreated.
- **Service-wide token budget** (`Assistant:MaxTotalTokensPerDay`, 0 = off):
  - `IConversationStore.GetTotalTokensSinceAsync` sums the tokens of all owners' answers.
  - A turn over the budget gets the error `serviceBudgetExceeded` (texts in en / ru / sv).
  - Why: a public demo has unlimited anonymous owners, so the per-owner budget alone does not cap the spend.
  - The hard cap is the credit limit of a separate OpenRouter key (README).
- **AI health check** reads the model list from the profile's new `ModelsPath` (default `/models`). OpenRouter lists
  embedding models under `/embeddings/models`, so the OpenRouter embedding profile was reported Degraded before.
- **Release workflow** (`.github/workflows/release.yml`), on a `v*` tag:
  1. Build the images.
  2. Start the stack on the runner (Ollama embeddings, no chat key, so Degraded is expected) and run the Playwright
     smoke tests.
  3. Push to GHCR, with `org.opencontainers.image.source`.
  4. When the environment `demo` has a `DEPLOY_HOST`: deploy over SSH and test the demo URL.

  Manual runs build and test, and push only on request.
- **Playwright smoke tests** (`src/web/e2e/smoke.spec.ts`, `npm run e2e`): health behind the proxy, the catalog and a
  movie page, a deep link, search, and a purchase through the fake payment page up to "Thank you!". With
  `E2E_ASSISTANT=1` also a grounded assistant answer.
- **Evaluation** (search suite, semantic and hybrid, 105 cases, the same dataset version as the I16 baseline):

  | Embeddings | Semantic R@10 | Semantic MRR@10 | Semantic nDCG@10 | Hybrid R@10 | Hybrid MRR@10 | Semantic p50 / p95 |
  |---|---|---|---|---|---|---|
  | LM Studio EmbeddingGemma (I16 baseline) | 0.865 | 0.794 | 0.784 | 0.873 | 0.788 | 60 / 73 ms |
  | Ollama EmbeddingGemma (demo default) | 0.865 | 0.797 | 0.787 | 0.873 | 0.790 | 57 / 65 ms |
  | OpenRouter Qwen3-Embedding-8B, 1024 dims | 0.917 | 0.921 | 0.881 | 0.927 | 0.914 | 577 / 4628 ms |

  Ollama gives the dev numbers, so the demo's search is the measured one. Qwen3 is clearly better (semantic R@10 in
  Russian 0.942 vs 0.857, in Swedish 0.896 vs 0.824), but each new query is a network call. Reports:
  `evals/reports/2026-11-19_search_i20-*.md`.
- **Tests:**
  - Unit +7 (375): the service budget (refused before any model call; off by default); `DemoConfigurationTests` (the
    demo configuration passes the AI and assistant validation, the OpenRouter embedding profile too, and without a key
    startup fails); the health check's `ModelsPath`.
  - Integration +1 (149): the total token count over all owners.
  - Web +1 (66): the `serviceBudgetExceeded` text.
  - Eval runner: a prefix with a line break is written as `\n` in the report header.
- **Manual checks** (Docker Desktop, images built from source, `deploy/.env` with an OpenRouter key):
  - Cold start with empty volumes: model pull, all 11 migrations, seed of 1000 movies in 9 s, reindex of 1000 movies in
    Ollama in 2 min 27 s on the desktop CPU. `https://localhost` with HTTPS, HSTS and security headers; HTTP → HTTPS 308.
  - The Russian hybrid query "мрачный фильм про космос и одиночество" returned Alien (1979) first.
  - Playwright: 6 of 6 with OpenRouter chat (fake purchase, grounded assistant answer). In the runner's configuration
    (no key, `/health` Degraded): 5 passed, 1 skipped, also right after a cold start while the reindex was running.
  - `deploy.sh` (several times, once with a Degraded start) and `restore.sh` from a dump (a signed-in user stayed
    signed in). An API restart under load: one request waited 6.4 s and got 200 instead of 503.
  - Switch to OpenRouter embeddings in the running stack: reindex of 1000 movies with Qwen3; `/health` Healthy after
    the `ModelsPath` fix; smoke tests green.

### Deviations and decisions
- **No public URL** (owner's decision): the first acceptance criterion stays open. Everything up to the server is ready
  and was run locally.
- **Environment `Demo`, not `Production`:** the fake payment gateway is refused in Production (ADR-0004) and is the
  default of the demo. Stripe test mode is one setting away. Everything else behaves like production.
- **Migrations by the Worker instead of an EF Core bundle:** the bundle failed in the container with
  `Could not load file or assembly 'System.Diagnostics.DiagnosticSource, Version=11.0.0.0'` (OpenTelemetry.Api 1.21
  needs the 11.0 package). It failed self-contained or not, and with Infrastructure or the Worker as the startup
  project. The Worker command reuses the app's own migration code and lock and needs no extra image.
- **The Worker waits for `ollama-pull`** (`required: false`). On a fresh server the reindex otherwise starts before the
  model exists and fails for good (a 404 is not retried).
- **Health "Degraded" counts as a successful deploy** (with a warning): the store works without AI, and the release
  runner has no chat key.
- **New packages:** `@playwright/test` (web dev dependency, the e2e smoke tests the plan asks for). No new .NET
  packages: data protection is part of the shared framework. New images: Caddy, Ollama, Aspire dashboard.
- Spec updates: 02 (containers, tests, layout), 04 (`serviceBudgetExceeded`), 06 (service budget), 10 (secrets in
  deployment, HTTPS and CORS checked, CI/CD release, §6 deployment).

### Follow-ups
- Rent a VPS and a domain, configure the GitHub environment `demo` and push the first tag. Then the public URL
  criterion can be checked.
- Copy the dumps off the server (object storage or another host) and schedule `backup.sh` (cron).
- A Content-Security-Policy header (first check what Radix and Tailwind inject).
- Query understanding is outside the token budgets. Count its tokens towards the service budget if the demo is abused.
- Try Qwen3-Embedding-0.6B locally (LM Studio) as a better local model, and tune `Hybrid:RrfK` for Qwen3.
- **Done after I20 (S23):** Ollama removed (owner's decision: only LM Studio or OpenRouter). The demo embeds on
  OpenRouter (Qwen3-Embedding-8B); `CHAT_PROFILE` / `EMBEDDING_PROFILE=local` use LM Studio on the Docker host; the
  release runner runs with both profiles unreachable (keyword search). ADR-0012 has a revision note.
- Dependabot for the Docker images and the npm / NuGet packages (spec 10 §4).

Session: [S22](../../prompts/sessions/2026-11-19_S22_deployment.md).
