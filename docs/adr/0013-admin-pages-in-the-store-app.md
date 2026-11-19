# ADR-0013 — Admin pages in the store web app

> [Docs](../README.md) › [ADRs](README.md) › ADR-0013

- **Status:** Accepted
- **Date:** 2026-11-19
- **Iteration:** I22

## Context
Until I22 every admin task (seed load, TMDB import, jobs, reindex, deleting an old model, checking an AI profile) was
an HTTP call to `/api/admin` with a cookie file and `curl` ([administrator handbook](../handbook/administrator.md)).
Spec 00 listed an admin UI as a non-goal. The handbook made the cost visible: following a job meant polling
`GET /api/admin/jobs/{id}` by hand, and a 409 hid the id of the job that blocked a new one. The admin API is complete
and role-protected (FR-USR-3, I17); only a client is missing.

## Options
1. **Keep the API only** (`curl`, `.http` files): nothing to build; every task stays slow and error-prone, and a
   non-developer cannot do it.
2. **Admin pages in the store app** under `/admin`: the same React app, generated API client, i18n, auth cookie and
   tests; one more route group behind a role check. The admin bundle ships to every visitor (a few kB), and the store's
   layout frames the admin pages.
3. **A separate admin app** (own Vite project or a ready-made admin framework): a separate bundle and look; a second
   build, a second deployment path, a second auth integration and a second set of tests for a handful of pages.
4. **Server-rendered pages in the API** (Razor): no SPA work; a second UI stack and a second i18n system.

## Decision
Option 2. The admin pages are thin views over the existing admin endpoints; everything they need (typed client from
OpenAPI, TanStack Query polling, shadcn/ui, en/ru/sv resources, the `fs_auth` cookie) is already in the store app. The
web app only hides the pages from users without the role; the API checks the role on every `/api/admin` call, so the
security boundary does not move. Configuration (profiles, budgets, payment provider) stays read-only in the UI: it is
the operator's, changed with a restart.

## Consequences
- Admin work needs a browser and an admin account; the API and the handbook's `curl` recipes keep working for scripts.
- One API addition was needed: `GET /api/admin/ai/profiles` (profile names and models without endpoints or keys).
- New admin endpoints should get a page in the same change, with en/ru/sv texts and a web test.
- To reverse: delete `src/web/src/pages/admin`, `src/web/src/features/admin` and the `admin` route; the API is unchanged.
