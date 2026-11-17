# I16 — TMDB sync and Worker

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I06 |
| **Parallel with** | I11, I17 |
| **Branch** | `iter/I16-tmdb-worker` |
| **Spec** | [08 Ingestion §3–4](../../spec/08-data-ingestion.md#3-tmdb-import-i16) |
| **Requirements** | FR-DATA-2..5 |

## Goal
A Worker process imports movies from TMDB (lists or ids), upserts them, and keeps embeddings fresh. The catalog can grow to 1 000+ movies.

## You will learn
Ingestion pipelines for RAG, background processing, rate-limited external APIs, how search quality changes with catalog size.

## Tasks
- [ ] `FilmSearch.Worker` project in AppHost; move order expiry and embedding indexing jobs there.
- [ ] TMDB typed client with resilience and rate limiting; DTOs; mapping shared with the seed path.
- [ ] `ingestion_jobs` table; admin endpoints to start and inspect jobs; jobs picked up by the Worker (DB-polling queue or a simple job table with `FOR UPDATE SKIP LOCKED`).
- [ ] Incremental embedding after import.
- [ ] Optional daily schedule (disabled by default).
- [ ] Tests: TMDB client with recorded responses (WireMock.Net or a handler stub).
- [ ] Import 1 000 movies; rerun search evaluation; note how metrics changed (dataset may need extra relevant ids).

## Acceptance criteria
- [ ] Import of top-rated pages 1–50 completes, rerun is idempotent.
- [ ] New movies are searchable after the job ends.
