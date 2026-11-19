# I16 — TMDB sync and Worker

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I16 · ← [I15](I15-stripe-test-mode.md) · [I17](I17-authentication.md) → · [Journal: what happened](../../journal/m4-real-integrations.md#i16--tmdb-sync-and-worker)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I06 |
| **Parallel with** | I11, I17 |
| **Spec** | [08 Ingestion §3–4](../../spec/08-data-ingestion.md#3-tmdb-import-i16) |
| **Requirements** | FR-DATA-2..5 |

## Goal
A Worker process imports movies from TMDB (lists or ids), upserts them, and keeps embeddings fresh. The catalog can grow to 1 000+ movies.

## You will learn
Ingestion pipelines for RAG, background processing, rate-limited external APIs, how search quality changes with catalog size.

## Tasks
- [x] `FilmSearch.Worker` project in AppHost; move order expiry and embedding indexing jobs there.
- [x] TMDB typed client with resilience and rate limiting; DTOs; mapping shared with the seed path.
- [x] `ingestion_jobs` table; admin endpoints to start and inspect jobs; jobs picked up by the Worker (DB-polling queue or a simple job table with `FOR UPDATE SKIP LOCKED`).
- [x] Incremental embedding after import.
- [x] Optional daily schedule (disabled by default).
- [x] Tests: TMDB client with recorded responses (WireMock.Net or a handler stub).
- [x] Import 1 000 movies; rerun search evaluation; note how metrics changed (dataset may need extra relevant ids).

## Acceptance criteria
- [x] Import of top-rated pages 1–50 completes, rerun is idempotent.
- [x] New movies are searchable after the job ends.

## Result

### What was done
- **Worker** (`src/FilmSearch.Worker`, `Microsoft.NET.Sdk.Worker`): the same modules as the API without HTTP (`WorkerHost.AddWorkerServices`: database, AI providers, catalog, search, commerce infrastructure, `AddBackgroundWork`). It reads the API's appsettings files (linked into its output) and shares the API's user secrets, so module configuration and keys live in one place; it applies migrations itself in Development (advisory lock), so the AppHost starts it without waiting for the API. AppHost resource `worker` (database reference, `Commerce__WebBaseUrl` like the API).
- **Job queue** (ADR-0009): table `ingestion.jobs` (migration `I16_AddIngestionJobs`) with kind, state, trigger, JSON parameters / progress / result, error, attempts, cancel flag, worker id, lease id, heartbeat; a partial unique index allows one queued or running job per kind. `JobStore` (`IBackgroundJobs`): enqueue (the active job comes back on a conflict), get, list, cancel, and for the Worker claim (`FOR UPDATE SKIP LOCKED`, new lease id), heartbeat (lease-checked, reports cancel requests and lost leases), finish, release. `JobWorker`: one job at a time, handler in its own scope, heartbeat loop beside it, lost leases re-claimed after 60 s up to 3 attempts, the job put back in the queue on shutdown. Activity `jobs.run` with kind, id, trigger, attempt, state.
- **Reindex jobs moved from the API's memory to the queue:** `IEmbeddingJobs` is async over `IBackgroundJobs` (`EmbeddingJobStatus` now has `JobState` and `Trigger`); the in-memory `EmbeddingJobRunner` is gone. The API queues the startup reindex (Development) and the admin reindex; the Worker runs them. Reindex runs are serialized across processes by a session advisory lock on the context's connection (`ReindexLock`).
- **Order expiry moved to the Worker** (`AddOrderExpiryService`); the job class is unchanged and still used by the commerce tests.
- **TMDB client** (`Infrastructure/Tmdb/TmdbClient.cs`): typed `HttpClient`, auth handler (v4 token → bearer, v3 key → `api_key`), own resilience pipeline: total timeout → retry (transient errors, exponential backoff with jitter, `Retry-After` wins) → sliding window rate limiter shared by the process (40 per 10 s) → attempt timeout. 404 of a movie → null.
- **TMDB import** (`TmdbImporter`, job `TmdbImport`): genres in en / ru / sv (`TmdbMovieMapper.ToGenres`, now also used by the seed builder) → ids of list pages or given ids → details 4 at a time in chunks of 100 → `TmdbMovieMapper` → `ICatalogWriter` (`MovieSource.Tmdb`) → stale-only reindex. Progress per phase (`genres`, `listing`, `movies`, `embeddings`) and counters (created, updated, unchanged, skipped, notFound, failed, first 20 errors, requests). A failing movie does not stop the job; every movie failing or the embedding phase failing fails it with the saved counters in the result.
- **Daily schedule** (`TmdbImportScheduler`, `Tmdb:Schedule`, off by default): queues one import (popular, 5 pages) per day after 03:00 UTC, asking the job table so restarts and replicas do not queue twice.
- **Admin endpoints:** `POST /api/admin/ingestion/tmdb` (202 / 409 / 400), `GET /api/admin/jobs?kind=&limit=`, `GET /api/admin/jobs/{id}`, `POST /api/admin/jobs/{id}/cancel`; the reindex endpoint returns `Location: /api/admin/jobs/{id}`.
- **Evaluation runner fix:** the report header redacts the value of `--connection` (it carried the database password); unit test.
- **Tests:** unit — `TmdbClientTests` (6: recorded responses, key placement, 429 with `Retry-After` and 5xx retries, 404 / 401 without retry and without the key in the message, rate limit waits for the next window, no request without a key), `TmdbImporterTests` (8: list import with genres in three locales and the embedding phase, unknown / failing ids, embedding failure keeps the counters, all failing, invalid requests), `TmdbImportSchedulerTests`, `RunHeaderTests`. Integration — `BackgroundJobsTests` (5, real PostgreSQL with a fake clock: one active job per kind, four concurrent claims → one winner, lost lease re-claimed / stale writes refused / failed after 3 attempts, cancel queued and running, release), `WorkerTests` (4, API + in-process Worker with `TestWorker`: list import searchable in keyword, semantic and hybrid mode and an idempotent rerun, import by ids with a changed movie re-embedded and a 404 id, 409 / 400 / 404 of the endpoints and cancel, missing key), the reindex endpoint test now runs through the Worker. Recorded TMDB responses (`tests/fixtures/tmdb`, trimmed real responses of three movies, genres in three languages, one list page) and `TmdbStub` are shared by both test projects. .NET tests 459/459, web 56/56.
- **Docs:** ADR-0009, spec 08 §3–4 (import, job queue), spec 04 (admin rows), spec 03 (Job entity), spec 07 (expiry in the Worker), README (Worker, TMDB key and import), AGENTS.md (Worker command, test notes, connection string pitfall), evaluation reports.

### Manual checks
AppHost (Postgres, API, Worker, web) against the dev database, the real TMDB API with the v4 token copied from the seed builder's secrets to the API's secrets:
- Startup: the API queued the startup reindex, the Worker ran it (1002 unchanged).
- `{ "list": "topRated", "pages": 50 }`: 987 movies (50 pages without duplicates), 180 created, 807 updated (vote counts and popularity moved since the seed was built), 0 failed, 1040 requests in 258 s (4.0 / s: the rate limit holds). LM Studio's server was not running: the embedding phase failed after the retries and the job ended `failed` with the counters and the "start a reindex" message; keyword search already found *Cast Away*, semantic search did not.
- The same import again (LM Studio up): 0 created, 985 unchanged, 2 updated, 198 texts embedded (180 new + 18 changed texts) in 14 s, `succeeded`. 1182 movies, 1182 distinct TMDB ids. Semantic search "a man stranded alone on an island talks to a volleyball" → *Cast Away* #1; in Russian → «Изгой» #2.
- Cancel: a running popular import cancelled after 32 of 93 details → `cancelled` at the next heartbeat.
- Lost worker: an import running when the AppHost was killed stayed `running`; after the restart the new Worker claimed it when the lease ran out (attempts 2, other worker id) and finished it: 23 unchanged (saved by the first attempt), 49 created, 21 updated.
- Search evaluation before / after the import (1000 → 1182 movies): [2026-11-18_I16_catalog-growth.md](../../../evals/reports/2026-11-18_I16_catalog-growth.md). As labelled: semantic Recall@10 0.865 → 0.855, MRR 0.794 → 0.770; hybrid 0.873 → 0.866, MRR 0.788 → 0.757; hybrid+qu 0.870 → 0.858, MRR 0.827 → 0.814. Most of the drop are relevant new movies the golden set does not know (Tom Hanks → *Cast Away*, *Philadelphia*; Kubrick before 1970 → *The Killing*; Tarkovsky → *The Sacrifice*); with 13 of them judged relevant semantic MRR is 0.805, hybrid 0.792, Recall@10 within 0.004 of the 1000-movie numbers. The rest are vague cross-language plot queries that meet more near neighbours.
- Checks: `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, OpenAPI document regenerated (new admin endpoints) and `npm run gen:api`, all .NET tests, web lint, format check, typecheck, tests, build.

### Decisions and deviations
- **Job table in PostgreSQL with `SKIP LOCKED`, leases and heartbeats** instead of Hangfire / Quartz or a broker: [ADR-0009](../../adr/0009-job-queue-in-postgresql.md). Table name `ingestion.jobs` (module schema) instead of `ingestion_jobs`; it also holds the reindex jobs.
- **One active job per kind** (partial unique index): a second import while one is queued or running gets 409 with the active job, as the reindex endpoint already did.
- **Embeddings inside the import job** (not a chained reindex job): the job only succeeds when the new movies are searchable in every mode (acceptance criterion), and a reindex job queued meanwhile cannot miss the import's changes.
- **The Worker shares the API's appsettings and user secrets** (linked files, same `UserSecretsId`, like the evaluation runner): one configuration of the modules, no duplicated `AI` section. The TMDB key is `Tmdb:ApiKey` in the API's user secrets.
- **The Worker migrates in Development** (advisory lock), so it does not need to wait for the API's health (which is Degraded while LM Studio is off).
- **JSON names:** the list is `topRated` / `popular` (camelCase like every enum of the API) instead of `top_rated`; the import endpoint returns the whole job status (202 with `Location`), not only `{ jobId }`. Spec 04 updated.
- **The seed builder keeps its own small TMDB client** (disk cache keyed by response, resumable runs); it now shares the genre mapping with the import. Unifying the clients would need a caching handler; not worth it for a dev tool.
- **Package:** `Microsoft.Extensions.Http.Resilience` is now referenced by `FilmSearch.Infrastructure` too (the TMDB pipeline; it was already central, used by ServiceDefaults). No new package version.
- **Pitfalls found:** Aspire's retrying execution strategy needs `CreateExecutionStrategy()` around the claim transaction; `Database.GetConnectionString()` has no password after EF opened a connection (the first `ReindexLock` used it; it now locks on the context's own connection); the integration test project references the Worker with an alias because both hosts have a public `Program`. LM Studio did not start from the agent's shell (the exe exited at once with code 0); the owner started it by hand.

### Follow-ups
- Golden set for a grown catalog: "also relevant if present" ids, or LLM-judged relevance of unlabelled top results, so evaluations on an imported catalog are fair (see the catalog growth report).
- Job table cleanup (keep N days) when it grows; `LISTEN / NOTIFY` instead of polling if the start delay ever matters.
- I17: the admin endpoints need the `admin` role. I18: job metrics (duration, failures) in the dashboard. I20: the Worker container and its secrets (TMDB key) in deployment.
- Sync of changed movies (TMDB `/movie/changes`) for the daily schedule instead of re-reading list pages.

### Sessions
- [S18](../../prompts/sessions/2026-11-18_S18_tmdb-sync-worker.md)
