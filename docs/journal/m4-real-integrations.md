# M4 — Real integrations

> [Docs](../README.md) › [Journal](README.md) › M4 Real integrations

**Plan:** replace the stand-ins with real systems: Stripe in test mode instead of only the fake gateway, TMDB import in
a separate Worker process instead of a static seed, real accounts instead of an anonymous cookie. **Fact:** done on
2026-11-18 in three sessions (S17–S19) that ran **in parallel** in separate worktrees and were rebased onto each other.

| Iteration | Planned sessions | Fact | Commit | Key number |
|---|---|---|---|---|
| [I15 Stripe test mode](#i15--stripe-test-mode) | 1–2 | S17 | [`2715796`](https://github.com/gray0072/ai-rag-film-search/commit/2715796) | 3 replays of one event → 1 entitlement |
| [I16 TMDB sync and Worker](#i16--tmdb-sync-and-worker) | 2 | S18 | [`03b577d`](https://github.com/gray0072/ai-rag-film-search/commit/03b577d) | 987 movies imported in 258 s at the rate limit |
| [I17 Authentication](#i17--authentication) | 1–2 | S19 | [`07455c2`](https://github.com/gray0072/ai-rag-film-search/commit/07455c2) | Anonymous cart, order and chat survive sign-in |

---

## I15 — Stripe test mode

[Plan](../plan/iterations/I15-stripe-test-mode.md) · [Session S17](../prompts/sessions/2026-11-18_S17_stripe-test-mode.md) ·
[ADR-0004](../adr/0004-payment-gateway-abstraction.md) · [Spec 07](../spec/07-payments.md)

**Built:** a Stripe gateway behind the same `IPaymentGateway` (Checkout Session with price data from the order
snapshot, idempotency key per attempt, absolute return URLs), a signed webhook endpoint sharing the pipeline of the
fake one, event mapping (completed, async succeeded / failed, expired), startup guards that refuse live and restricted
keys. Walked through on Stripe's hosted page with Playwright: success, a declined card and a second card, 3-D Secure,
cancel, resent events, a triggered event without our metadata, session expiry.

| Problem | Cause | Solution |
|---|---|---|
| Unknown Stripe event types would be retried for days | A 400 makes Stripe retry | `ParseWebhook` returns "ignored" with a reason → 200, not stored |
| A retry with the same idempotency key was refused | The session expiry depended on the current time | Session lifetime = order expiry + 30 min (Stripe's minimum); the expiry job closes it earlier |
| A declined card would have failed the order | Assumed one attempt per session | Stripe keeps the customer on its page to try another card; the order stays pending |
| `stripe listen` forwarded nothing | Stripe CLI 1.55 needs `--events` | Runbook and AGENTS.md updated |
| Webhooks of old Stripe sessions after switching back to the fake provider | The gateway was registered only when active | Registered whenever a secret key is set |

**Lesson:** the abstraction from I09 held: Stripe was an additional gateway and an event mapper, and the order state
machine did not change. Its tests with duplicate, late and out-of-order events had already covered Stripe's behavior.

## I16 — TMDB sync and Worker

[Plan](../plan/iterations/I16-tmdb-sync-worker.md) · [Session S18](../prompts/sessions/2026-11-18_S18_tmdb-sync-worker.md) ·
[ADR-0009](../adr/0009-job-queue-in-postgresql.md) · [Catalog growth report](../../evals/reports/2026-11-18_I16_catalog-growth.md)

**Built:** a Worker process with the same modules as the API and no HTTP; a job queue in PostgreSQL (`SKIP LOCKED`
claims, leases, heartbeats, re-claim of a lost lease, cancel, one active job per kind); a TMDB client with retries,
`Retry-After` and a process-wide rate limiter (40 requests per 10 s); an idempotent import job that embeds new and
changed movies before it reports success; a daily schedule; admin endpoints for jobs. Reindex and order expiry moved
from the API to the Worker.

**Manual checks that mattered:** a killed AppHost left a job `running`; after the restart the new Worker re-claimed it
when the lease ran out and finished it without redoing saved work. An import with LM Studio off failed in the
embedding phase *with its counters*, and a rerun embedded only the 198 new and changed texts.

| Problem | Cause | Solution |
|---|---|---|
| Search quality "dropped" after the import (hybrid MRR 0.788 → 0.757) | Relevant new movies the golden set did not know (Tom Hanks → *Cast Away*) | Judged by hand: with 13 of them counted, numbers within 0.004 of before. Follow-up: "also relevant if present" ids |
| The claim transaction failed with Aspire's retrying strategy | EF retries need an execution strategy around a user transaction | `CreateExecutionStrategy()` around the claim |
| The reindex lock connected without a password | `Database.GetConnectionString()` has no password after EF opened a connection | Lock on the context's own connection (in AGENTS.md) |
| Two public `Program` classes in one test project | API and Worker are both hosts | Reference the Worker with an extern alias |
| The evaluation report header contained the database password | `--connection` was printed as is | Redacted, with a unit test |

**Lesson:** a queue table in the database you already have is enough for one Worker and gives transactions, history
and admin queries for free. ADR-0009 lists when to move to a broker.

## I17 — Authentication

[Plan](../plan/iterations/I17-authentication.md) · [Session S19](../prompts/sessions/2026-11-18_S19_authentication.md) ·
[ADR-0010](../adr/0010-authentication-identity-cookie.md)

**Built:** ASP.NET Core Identity in schema `identity`, own register / login / logout / me endpoints with an HttpOnly
cookie, lockout, NIST-style password rules, a merge of everything the anonymous visitor owned (cart, orders, library,
conversations, confirmations) into the account in one transaction on sign-in, an `admin` role and policy on all admin
endpoints, a seeded admin account, login and register pages, a user menu, a protected library.

**Plan vs fact:** own endpoints instead of `MapIdentityApi`: the built-in set has bearer tokens, 2FA and mail flows the
project does not use, but no logout, no place for the merge before the cookie is issued, and no error codes. The I17
migration was regenerated after the rebase onto I16 (the roadmap rule for parallel sessions).

| Problem | Cause | Solution |
|---|---|---|
| Every owner-scoped module must move its data on sign-in | Data spread over modules that must not touch each other's tables | `IOwnerMergeHandler` per module in Core, run in one transaction; a new owner table needs a handler and a test (AGENTS.md) |
| Unknown email and wrong password must not be distinguishable | Account enumeration | One `invalidCredentials` answer; lockout after 5 failures |
| An existing account with the admin email could be taken over | Seeding the role by email | The role is granted only if the configured password matches |
| CSRF with cookies | Cookie authentication | SameSite=Lax and JSON-only bodies (ADR-0010) |

**Lesson:** "anonymous first, account later" is good for conversion but makes ownership a cross-module concern. It was
designed in I01 (a `Guid` owner id that Identity could take over), which kept I17 small.

---

**Previous:** [M3 AI sales assistant](m3-ai-assistant.md) · **Next:** [M5 Production ready](m5-production-ready.md) ·
**See also:** [Administrator guide](../handbook/README.md#roles) · [Spec 08 Data ingestion](../spec/08-data-ingestion.md)
