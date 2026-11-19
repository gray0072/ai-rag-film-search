# Demo environment: deploy and operate

The demo runs on one Linux host with Docker Compose ([ADR-0012](../docs/adr/0012-deployment-docker-compose-vps.md)):

```text
browser ──HTTPS──> web (Caddy: TLS, static app, reverse proxy) ──/api, /fake-checkout, /health──> api ──> postgres
                                                                                      worker ──┘    │
                                        api, worker ──embeddings──> ollama (EmbeddingGemma, CPU)      │
                                        api, worker ──chat (and optional embeddings)──> OpenRouter    │
                                        migrate (Worker image, `migrate`): run once per deploy ──────┘
```

| File | What |
|---|---|
| [compose.yaml](compose.yaml) | The services, volumes and configuration (environment `Demo`, settings in `src/FilmSearch.Api/appsettings.Demo.json`) |
| [compose.build.yaml](compose.build.yaml) | Builds the images from source instead of pulling them (local runs, the release workflow) |
| [.env.example](.env.example) | Every setting of a server: copy to `.env` |
| [Caddyfile](Caddyfile) | HTTPS, security headers, caching, SPA routes, proxy to the API |
| [docker/](docker/) | Dockerfiles: `dotnet.Dockerfile` (targets `api`, `worker`), `web.Dockerfile` |
| [deploy.sh](deploy.sh), [backup.sh](backup.sh), [restore.sh](restore.sh) | Deploy / roll back, dump the database, restore a dump |

Images: `ghcr.io/<owner>/filmsearch-api`, `-worker`, `-web`, tagged with the release tag (`v0.20.0`) by the
[release workflow](../.github/workflows/release.yml).

## Run it locally (production-like)

Docker Desktop is enough. From `deploy/`:

```bash
cp .env.example .env     # set POSTGRES_PASSWORD, ADMIN_PASSWORD, OPENROUTER_API_KEY;
                         # IMAGE_PREFIX=filmsearch, FILMSEARCH_TAG=local, SITE_ADDRESS=localhost, PUBLIC_URL=https://localhost
docker compose -f compose.yaml -f compose.build.yaml build
SKIP_PULL=1 ./deploy.sh local
```

Open https://localhost and accept the certificate warning (the certificate comes from Caddy's own local CA). If port 80 is taken (on Windows often by `System`), set
`HTTP_PORT=8080`. Without an OpenRouter key set `CHAT_PROFILE=local`: chat and query understanding then fail softly
(LM Studio is not reachable from the containers), search and the store work.

The first start loads the seed (1000 movies, ~10 s) and queues the embedding reindex; the Worker embeds all movies in
Ollama in about 2–3 minutes on a desktop CPU (expect 10–20 minutes on 2 vCPUs). Until then search mixes keyword
results with the vectors that exist.

Smoke tests (Playwright) against it, from `src/web`: `npm run e2e` (`E2E_BASE_URL`, `E2E_ASSISTANT=1` also asks the
assistant: costs tokens).

## Set up a server (once)

1. **Host**: any Linux VM with Docker Engine and the compose plugin, 2 vCPU / 4 GB RAM (Ollama with the model needs
   about 1.5 GB, PostgreSQL, API and Worker about 1 GB), 20 GB disk. Open ports 80 and 443 (UDP 443 for HTTP/3).
2. **DNS**: an A (and AAAA) record of the demo host name to the server. Caddy gets the Let's Encrypt certificate on the
   first request.
3. **User and folder**: a deploy user in the `docker` group, a folder (e.g. `/opt/filmsearch`) with `.env` from
   [.env.example](.env.example) (`chmod 600 .env`). The workflow copies `compose.yaml` and the scripts on each deploy.
4. **Images**: GHCR packages of a user are private at first. Make them public (package settings → visibility), or
   `docker login ghcr.io` on the server with a token that has `read:packages`.
5. **GitHub**: repository settings → Environments → `demo`: variables `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_PATH`,
   `DEMO_URL` (and `E2E_FAKE_PAYMENTS=0` with Stripe), secrets `DEPLOY_SSH_KEY` (a key of the deploy user only) and
   `DEPLOY_KNOWN_HOSTS` (`ssh-keyscan <host>`). Without `DEPLOY_HOST` the release workflow builds, tests and pushes
   but does not deploy.
6. **OpenRouter**: a key of its own for the demo with a credit limit (README "Demo environment").
7. **Stripe** (optional): `PAYMENTS_PROVIDER=Stripe`, test keys, and a webhook endpoint
   `https://<host>/api/payments/webhooks/stripe` in the Stripe dashboard (test mode) with the events of the README
   section "Payments: Stripe test mode"; its signing secret is `STRIPE_WEBHOOK_SECRET`.

## Deploy

Push a tag: `git tag v0.20.0 && git push origin v0.20.0`. The release workflow builds the images, runs the stack and
the smoke tests on the runner, pushes the images, runs `./deploy.sh v0.20.0` on the server over SSH and the smoke
tests against `DEMO_URL`. By hand on the server: `./deploy.sh v0.20.0`.

`deploy.sh`: pull → start PostgreSQL → `backup.sh` (dump to `backups/`, the last 10 are kept) → migrations
(`docker compose run --rm migrate`) → start the new containers → wait for `Healthy` → write the tag to `.env` and a line
to `deploy-history.log`. While the API restarts Caddy holds requests for up to 30 s, so a deploy has a few seconds of
slow requests, not errors.

**Migrations** run while the old version still serves requests and the previous version must keep working on the new
schema, so that a rollback needs no restore: add columns and tables first (expand), stop using and drop them in a later
release (contract). A migration that cannot be written like that needs a restore to roll back (below).

## Rollback

- **Code only** (no migration in between, or only backward compatible ones): `./deploy.sh <previous tag>`
  (`deploy-history.log` lists the tags). The images of every release stay in GHCR.
- **Across a migration the old version cannot run on**: restore the dump that `deploy.sh` made before that
  migration, then start the old version: `./restore.sh backups/filmsearch-<time>.dump <previous tag>`. Orders, users
  and conversations written after the dump are lost; for a demo this is acceptable, for a real shop it is not (there
  the answer is expand/contract migrations only, plus point-in-time recovery).
- EF Core can also migrate down (`dotnet ef database update <migration>`), but our migrations are not written and
  tested for `Down`, so the dump is the supported way back.

## Operate

| Task | Command (in the deploy folder) |
|---|---|
| Status, logs | `docker compose ps`, `docker compose logs -f api worker` |
| Restart one service | `docker compose restart worker` |
| Backup now | `./backup.sh` (copy `backups/` off the server too) |
| Traces, metrics, logs | `COMPOSE_PROFILES=ollama,dashboard` and `OTEL_EXPORTER_OTLP_ENDPOINT=http://dashboard:18889` in `.env`, `docker compose up -d`; then `ssh -L 18888:127.0.0.1:18888 <host>` and http://localhost:18888 (the login token is in `docker compose logs dashboard`) |
| Switch embeddings to OpenRouter | `EMBEDDING_PROFILE=openrouter` and `COMPOSE_PROFILES=` (empty) in `.env`, `docker compose up -d`, then `docker compose rm -sf ollama` (a service of an inactive profile is not stopped by itself). The API queues a reindex with the new model on startup (Qwen3-Embedding-8B, 1024 dimensions, less than $0.01 for the catalog); until it ends, semantic results cover the movies embedded so far. Back: the reverse, the EmbeddingGemma vectors are still there |
| Free disk | `docker image prune -a` (keeps the images of running containers) |

Secrets live only in `.env` on the server and in the GitHub environment; nothing secret is in the images
(`.dockerignore` excludes `.env`, user secrets are not used in containers).
