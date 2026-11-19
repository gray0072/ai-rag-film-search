# I22 — Admin UI and I21 follow-ups

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I22 · ← [I21](I21-documentation.md)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I21 |
| **Parallel with** | — |
| **Spec** | [00 §4 Scope](../../spec/00-overview.md#4-scope), [04 Search and Admin](../../spec/04-api.md#search), [05 §3 Hybrid](../../spec/05-search.md) |
| **Requirements** | FR-USR-3, FR-SRCH-3, NFR-10 |

## Goal
The three follow-ups found while writing the handbook (I21) are fixed: the project's images carry the label
`project=filmsearch`, an administrator does the daily work in the web app instead of `curl`, and the default search
mode is a setting instead of a constant in the web app.

## You will learn
Building an internal tool on an existing API (role-gated routes, polling a background job, a 409 that carries the
running job), and moving a product decision from code to configuration without changing the API contract for other
clients.

## Tasks
- [x] Prompt log (S28), this plan, roadmap row.
- [x] `LABEL project=filmsearch` in the final stages of `deploy/docker/dotnet.Dockerfile` (api, worker) and
  `deploy/docker/web.Dockerfile` (global Docker rule).
- [x] `Search:Defaults` (`Mode`, `Understand`): `POST /api/search` without `mode` uses `Search:Defaults:Mode`;
  `GET /api/search/settings` gives both to the web app; the search page starts with them and keeps only a different
  choice in the URL (`mode=…`, `understand=0|1`).
- [x] `GET /api/admin/ai/profiles`: chat and embedding profiles without endpoints and keys (for the admin pages).
- [x] Admin pages under `/admin` ([ADR-0013](../../adr/0013-admin-pages-in-the-store-app.md)): catalog (seed, TMDB
  import), jobs (list, filter, cancel, details), search index (state, reindex, delete an old model), AI models
  (profiles, one test call with tokens and cost). An "Admin" link in the header for admins only. Texts in en, ru, sv.
- [x] Tests: .NET unit and integration (settings endpoint, default mode, profiles endpoint and its role), web tests of
  the admin pages and the search defaults.
- [x] Screenshots `admin-*.png` with a repeatable script (`npm run docs:screenshots:admin`), added to the administrator
  handbook and the README.
- [x] Docs: spec 00, 01, 04, 05; handbook (administrator, product owner, configuration, developer); I21 follow-ups.

## Out of scope
- Editing single movies, prices or users in the UI (spec 00: no catalog editor).
- Changing configuration from the UI: profiles, budgets and the payment provider stay settings the operator changes.

## Acceptance criteria
- [x] `docker images --filter label=project=filmsearch` lists the api, worker and web images after a build.
- [x] With `Search:Defaults:Mode=Keyword` a search page without `mode` searches by keywords, and a request without
  `mode` too; the built-in default stays hybrid with query understanding on.
- [x] An admin can load the seed, queue an import and a reindex, follow and cancel jobs, delete the vectors of an old
  model and try a chat profile without leaving the browser; other users see "No access", visitors go to sign-in.
- [x] Build, format and all tests (.NET unit + integration, web) pass locally before the push, and in CI.
- [x] Docs updated.

## Result

Done in one session (S28).

- **Image label.** The api, worker and web stages of the Dockerfiles have `LABEL project=filmsearch`; build stages are
  not tagged, so they need none.
- **Search defaults.** `SearchDefaultsOptions` (`Search:Defaults:Mode` = `Hybrid`, `Understand` = `true`, validated on
  start). `POST /api/search` uses the mode when the request has none. `understand` keeps its API meaning (false when
  omitted): the setting is the starting value of the checkbox in the web app, so evaluation runs and other clients
  do not start calling the LLM. `GET /api/search/settings` returns both; the search page waits for it (no search with
  the wrong mode first) and uses the built-in defaults when it fails. In the URL `mode` and `understand=0|1` appear only
  when they differ from the defaults.
- **Admin pages** (`src/web/src/features/admin`, `src/web/src/pages/admin`): one more route group in the store app
  behind `AdminLayout` (sign-in redirect, "No access" without the role); the API still checks the role on every call.
  Job views poll every 2 s while a job is queued or running. A 409 of the import or reindex endpoint shows the running
  job with a link. The only API addition is `GET /api/admin/ai/profiles` (names, models, dimensions, whether a key is
  set, the active and fallback profiles; no endpoints or keys).
- **Screenshots**: `admin-catalog`, `admin-jobs`, `admin-job`, `admin-embeddings`, `admin-ai`, taken by
  `src/web/scripts/docs-screenshots-admin.mjs` against the AppHost with LM Studio (the worker id on the job page is
  replaced: it starts with the machine name).
- **Tests**: unit +2 (default mode in `SearchRequest`, defaults of the options), integration +4 (settings endpoint,
  configured default mode, profiles endpoint, profiles need the role), web +13 (admin pages and helpers, search
  defaults).
- **Decision**: [ADR-0013](../../adr/0013-admin-pages-in-the-store-app.md) — admin pages inside the store app, not a
  separate app. Spec 00 no longer lists "admin UI" as a non-goal, only catalog editing.
- **Manual checks**: the five admin pages against the AppHost (seed load, stale-only reindex, job details, a call to
  `google/gemma-4-e4b` with tokens); a live `POST /api/search` without `mode` returned keyword and vector ranks
  (hybrid).
- No new packages. No AI behavior changed (prompts, models, search parameters), so no evaluation run.
