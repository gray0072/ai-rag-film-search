# Session S18 — 2026-11-18 — TMDB sync and Worker

> [Docs](../../README.md) › [Prompt history](../README.md) › S18 · ← [S17](2026-11-18_S17_stripe-test-mode.md) · [S19](2026-11-18_S19_authentication.md) → · [I16 plan](../../plan/iterations/I16-tmdb-sync-worker.md)

- **Iteration:** I16 (TMDB sync and Worker)
- **Agent:** Claude Code (VS Code)
- **Outcome:** I16 done: Worker process (AppHost resource) with a job queue in PostgreSQL (`ingestion.jobs`, SKIP LOCKED claims, leases and heartbeats, cancel, ADR-0009); TMDB typed client with retry, Retry-After and a shared rate limiter; TMDB import job (lists or ids, idempotent upsert, embeddings at the end) with admin endpoints and an optional daily schedule; reindex jobs and order expiry moved from the API to the Worker. Live: top-rated pages 1–50 imported (180 new movies, rerun 985 unchanged), cancel and lost-worker recovery checked; search evaluation 1000 vs 1182 movies (drop mostly unlabelled relevant newcomers). 459 .NET and 56 web tests. Pushed.

---

## Prompt 1

English:

```text
Let's do I16. Then push.
```

Original (ru):

```text
делаем I16. потом пуш
```

## Prompt 2

English:

```text
I started LM Studio. Apparently it has to be started by hand.
```

Original (ru):

```text
LM Studio я запустил. видимо ее руками надо запускать
```
