# ADR-0009 — Background jobs: a job table in PostgreSQL, claimed with SKIP LOCKED

- **Status:** Accepted
- **Date:** 2026-11-18
- **Iteration:** I16

## Context
I16 moves background work out of the API into a Worker process: TMDB imports (minutes, about 1000 rate-limited
requests), embedding reindex jobs and order expiry. The API must queue a job and show its state (progress, counters,
errors); the Worker runs it. Until I16 the reindex job lived in the API's memory (a `Channel` and a status field), which
cannot cross a process boundary and is lost on restart. Constraints: one developer, PostgreSQL is the only database
(ADR-0001), Aspire runs everything locally, a job must survive a Worker crash, and two Worker replicas must not run the
same job.

## Options
1. **Job table in PostgreSQL, polled with `FOR UPDATE SKIP LOCKED`.** No new infrastructure; the queue and the job state
   are the same row (status, counters, errors for the admin endpoints); transactional with the rest of the data. Cons:
   polling delay (2 s), we write the lease / retry logic ourselves, not meant for thousands of jobs per second.
2. **Hangfire or Quartz.NET with PostgreSQL storage.** Ready dashboard, retries, cron. Cons: a framework with its own
   tables and serialization of method calls, hides exactly the mechanics this project wants to learn; Hangfire's
   PostgreSQL storage is a third-party package.
3. **Message broker (RabbitMQ, Azure Service Bus) or Redis streams.** Push instead of polling, scales out. Cons: a new
   container and client library, job state still needs a table for the admin endpoints, at-least-once delivery has to
   be handled anyway.
4. **Keep the jobs in the API (hosted services).** No Worker. Cons: long imports compete with HTTP requests, every API
   replica would run the timers, the architecture (spec 02) has a Worker for this.

## Decision
Option 1. Table `ingestion.jobs` (kind, state, trigger, JSON parameters / progress / result, error, attempts,
`cancel_requested`, `worker_id`, `lease_id`, heartbeat). A partial unique index on `kind WHERE state IN ('Queued',
'Running')` allows one active job per kind (the admin endpoints answer 409 otherwise). The Worker claims the oldest due
row with `SELECT … FOR UPDATE SKIP LOCKED LIMIT 1` in a short transaction, sets a new lease id and runs the handler
outside the transaction, writing progress and a heartbeat every 2 s. A running row whose heartbeat is older than the
lease (60 s) is claimed again by any worker, up to 3 attempts; writes with an old lease id are ignored. A cancel request
is seen at the next heartbeat; on shutdown the job is put back in the queue. Jobs are idempotent (upsert by TMDB id,
embeddings by content hash), so at-least-once execution is safe. Reindex runs are serialized across processes by a
session advisory lock.

## Consequences
- Easier: no new container; job state is queryable SQL; tests run the real Worker composition against Testcontainers.
- Harder: a job starts up to one poll interval late; the lease, heartbeat and retry rules are our code (covered by
  `BackgroundJobsTests`). Long single steps must stay shorter than the lease (heartbeats run on their own timer, so only
  a blocked process loses its lease).
- Watch: the table grows by a few rows a day; add a cleanup when it matters. `LISTEN / NOTIFY` could replace polling
  if the delay ever matters.
- Reversal: `IBackgroundJobs` hides the storage; Hangfire or a broker can replace `JobStore` and `JobWorker` without
  changing the handlers or the endpoints.
