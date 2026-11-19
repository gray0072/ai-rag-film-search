# I21 — Documentation and project showcase

| | |
|---|---|
| **Status** | in progress |
| **Depends on** | I20 |
| **Parallel with** | — |
| **Spec** | all (documentation only, no behavior change) |
| **Requirements** | — |

## Goal
A newcomer opens the repository and understands in a minute what FilmSearch is, sees it working, runs it, and then
continues in the part of the documentation made for them: a textbook on RAG and AI apps, a build journal (what really
happened), or a handbook for their role. Every part links to the others, and the same Markdown is readable on GitHub
and as a website on GitHub Pages.

## You will learn
Technical writing for different audiences, information architecture, docs-as-code (one Markdown source, a static site
built in CI), explaining AI decisions with evidence (evaluation reports).

## Information architecture

One source: Markdown in the repository, readable on GitHub (Mermaid renders there too). GitHub Pages is a second view
of the same files (navigation, search, dark mode), built by a workflow. Nothing is written twice.

```text
README.md                    showcase: what it is (a pet project), screenshots, features, quick start, "where next"
docs/
  README.md                  documentation hub: by role, by question, map of every section
  images/screenshots/        PNG screenshots (taken with Playwright, script in tools/docs-screenshots)
  guide/                     TEXTBOOK: building an AI store, RAG from zero to production (theory → our code → evidence)
    README.md                contents, how to read, prerequisites
    01..14-*.md              core chapters, each: business problem → theory → what we did → why → evidence → links
    advanced/                deep dives: similarity metrics, embedding model choice, pgvector indexes, vector
                             storage, fusion tuning, LLM-as-judge, …
  journal/                   FACT: the build journal, mirrors the roadmap (plan → fact)
    README.md                plan vs fact table, timeline, numbers, index of problems and solutions
    m1..m5-*.md              one file per milestone, a section per iteration: built, deviations, problems → solutions
  handbook/                  CAPABILITIES BY ROLE: what the system can do and where it is controlled
    README.md                roles, capability × role matrix
    customer.md              (non-technical) using the store and the assistant
    product-owner.md         (low-tech) capabilities, levers, limits, quality and cost numbers
    administrator.md         (medium) catalog, TMDB import, embeddings, jobs, payments, budgets, admin API
    operator.md              (technical) environments, deploy, monitoring, incidents, backup, rollback
    developer.md             (technical) setup, code map, conventions, tests, adding a feature
    ai-engineer.md           (technical) models, profiles, prompts, evaluation suites, safe change process
    configuration.md         reference: every setting, default, where to set it, who changes it
  plan/ spec/ adr/ runbooks/ prompts/        existing, linked from the hub
tools/docs-site/             VitePress config that renders docs/ (and README) for GitHub Pages
.github/workflows/docs.yml   builds and publishes the site on pushes to main that touch docs
```

Rules for every page:
- A breadcrumb line at the top (`Docs › Guide › Embeddings`) and a "Next / See also" block at the bottom, so a reader
  can move between the textbook, the journal and the handbook.
- Links are relative Markdown links (they work on GitHub; the site rewrites links to files outside `docs/` to GitHub
  URLs).
- Diagrams in Mermaid (renders on GitHub and on the site). Screenshots as PNG under `docs/images/`.
- English (project rule). The guide may say where Russian or Swedish matter (multilingual search).

Why VitePress and not MkDocs or Jekyll: the project already uses Node 24 and npm; VitePress renders plain Markdown with
built-in local search and needs no Python toolchain. Jekyll (GitHub's default) has no useful navigation or search out
of the box.

## Tasks

### Session 1 (S24): structure, showcase, journal
- [x] Prompt log, this plan, roadmap row.
- [x] Screenshots of the running app (store, search, movie, assistant, cart, payment, library, Aspire traces) with a
  repeatable Playwright script.
- [x] README rewrite: pet project, screenshots, features, architecture picture, quick start, where to go next.
- [x] GitHub repository info: description, topics, homepage (Pages URL once it exists), social preview image.
- [x] Documentation hub `docs/README.md`.
- [x] Journal: `docs/journal/` README (plan vs fact, timeline, problem index) and milestone files M1–M5.

### Session 2: textbook core
- [x] `docs/guide/README.md` and core chapters 01–14 (business problem, architecture, LLM basics, embeddings, vector
  search, hybrid search, query understanding, RAG and grounding, agents and tools, evaluation, guardrails,
  observability and cost, resilience, shipping). Mermaid diagrams, links to plan, journal, code, ADRs and reports.

### Session 3: textbook advanced, handbook
- [x] `docs/guide/advanced/`: similarity metrics and normalization, choosing an embedding model, pgvector indexes
  (exact, HNSW, IVFFlat; filtering), vector storage per dimension, fusion tuning, LLM-as-judge.
- [x] `docs/handbook/`: README with the role matrix, the six role pages and the configuration reference, with
  screenshots.

### Session 4: website and polish
- [ ] VitePress site in `tools/docs-site`, link rewriting for files outside `docs/`, Mermaid, local search.
- [ ] `.github/workflows/docs.yml` (build + deploy to Pages), Pages enabled, homepage set in the repository info.
- [ ] Link check over all Markdown, breadcrumbs and "next" blocks everywhere, README and hub point to the site.

## Out of scope
- Changes to application behavior.
- Translating the documentation to Russian or Swedish.

## Acceptance criteria
- [ ] README shows the project in screenshots and gets a new developer to a running app.
- [ ] From the README every role finds its page in one click, and every docs page links onward.
- [ ] The journal covers I00–I20 with problems and solutions, linked to plans, sessions and reports.
- [ ] The textbook explains RAG from the business problem to production with diagrams and evidence from the reports.
- [ ] The GitHub Pages site builds in CI and is linked from the repository header.
- [ ] No broken relative links (checked by a script).

## Open questions
- None. (License: MIT, chosen by the owner in S24.)

## Result

### Progress
_Updated at the end of every session; the next session continues from the first unchecked task._

**After S24 (session 1 of 4): done.**
- Screenshots (14) in `docs/images/screenshots/`, taken by `src/web/scripts/docs-screenshots.mjs`
  (`npm run docs:screenshots`; `BASE_URL` = the web URL from the Aspire dashboard, `ASPIRE_LOGIN_URL` for the trace
  shots). The run needs LM Studio with both models; it registers a user, chats with the assistant and buys a movie
  through the fake page. Shots: catalog, search, search-scores, movie, assistant, assistant-checkout, cart,
  fake-payment, checkout-success, library, search-ru, assistant-sv, aspire-trace, aspire-span.
- `docs/images/social-preview.png` (1280×640) for the repository's social preview.
- README rewritten as a showcase. The detailed setup that was in the README moved, with fixed links, to
  [docs/handbook/local-setup.md](../../handbook/local-setup.md).
- GitHub: description and 20 topics set, wiki switched off (the docs live in the repository). The homepage is set in
  session 4 when the Pages URL exists. The social preview image has no API: upload it by hand in Settings → General.
- Hub [docs/README.md](../../README.md); skeletons [guide/README.md](../../guide/README.md) (full table of contents)
  and [handbook/README.md](../../handbook/README.md) (roles, capability matrix).
- Journal: [docs/journal/](../../journal/README.md) with the plan vs fact table, numbers, timeline, test growth, the
  problem index by kind, cross-cutting lessons, and M1–M5 with a section per iteration (problem → cause → solution
  tables, commits, links to plans, sessions and reports).

**After S25 (session 2 of 4): done.**
- Guide core chapters [01](../../guide/01-business-problem.md)–[14](../../guide/14-resilience-and-shipping.md), each
  with the parts business problem → theory → what FilmSearch does (code links) → why (ADRs, alternatives) → evidence
  (numbers from the reports, journal sections) → pitfalls → previous / next. 16 Mermaid diagrams; screenshots
  `search-scores`, `search-ru`, `assistant`, `assistant-checkout`, `aspire-trace`, `aspire-span`.
- [guide/README.md](../../guide/README.md): chapters linked from the contents, a "How to read it" section and a reading
  map; status note says the advanced chapters come in session 3.
- Links to specific chapters from the hub (product owner row, "What is RAG", "Why pgvector", "How good is search") and
  from the journal (I04, I12, M2 and M3 "See also").
- Facts checked against the code while writing: the embedding sync is a stale-only reindex job (not the catalog change
  hook, which only updates keyword documents), rate limits are per owner for chat and per IP for search and new
  conversations, the hybrid experiments of I08 changed the all-words keyword weight (not the relaxed weight).
- The throw-away link check of S24 was run again: all relative links and anchors resolve (false alarms: inline code in
  this plan).
- The advanced chapters are still linked as `README.md#advanced` from chapters 5, 6, 7 and 11; session 3 replaces those
  links with the real files.

**After S26 (session 3 of 4): done.**
- Advanced chapters in [guide/advanced/](../../guide/advanced/README.md): similarity metrics, choosing an embedding
  model, pgvector indexes, vector storage, fusion tuning, LLM as a judge (4 Mermaid diagrams). Numbers come from the
  reports; a few were measured on the development database while writing (vector norms, `ef_search` with and without
  iterative scan, index sizes for float / halfvec / bit) and are labelled that way. IVFFlat, halfvec and binary recall
  were not measured and the chapters say so.
- Handbook role pages: [customer](../../handbook/customer.md), [product owner](../../handbook/product-owner.md),
  [administrator](../../handbook/administrator.md), [operator](../../handbook/operator.md),
  [developer](../../handbook/developer.md), [AI engineer](../../handbook/ai-engineer.md) and the
  [configuration reference](../../handbook/configuration.md) (every key from appsettings, options classes and
  `deploy/.env.example`, with defaults, overrides, roles and validation rules).
- Placeholders replaced: the `README.md#advanced` links in chapters 5, 6, 7, 11, 14; the role rows of the README, the
  hub, the handbook README and the journal now point at the role pages.
- Writing against the code found stale statements, fixed in the same change: there is no "Watch" button (README, spec
  00); `POST /api/admin/ai/chat` is mapped in every environment (spec 06); the search query limit is 500 characters
  (spec 10, as spec 04 and the code); fake payment endpoints are off only in Production (spec 10); semantic search
  degrades to keyword results instead of 503 (spec 05 §3); the planner *estimates* the exact scan as cheaper at 1000
  rows but HNSW is faster (journal M2, AGENTS.md, a test comment); prefixes cut the unrelated cosine from 0.33 to 0.19,
  the gap grew 1.3–1.5×, not 2× (guide ch. 5); `ef_search` = rows × locales clamped to 40–1000 (guide ch. 6); the
  observability runbook lists `cancelled` as an outcome and the error codes `serviceBudgetExceeded`,
  `toolsNotSupported` and the budget `Assistant:MaxTotalTokensPerDay`; sign-in also merges purchases, and changing
  `Auth:Admin:Password` does not reset an existing admin (local setup); `Stripe:PublishableKey` is stored but not read
  (AGENTS.md).
- Follow-ups found, not done (outside I21): the Dockerfiles in `deploy/docker/` have no `LABEL project=filmsearch`;
  there is no admin UI (admin work is API only, as spec 00 says); the default search mode is fixed in the web app, not
  a setting.

**Notes for the next sessions.**
- Page conventions: a breadcrumb line under the title (`> [Docs](../README.md) › [Guide](README.md) › Title`), a
  "See also / Next" line at the end, relative links only, Mermaid for diagrams, screenshots from
  `docs/images/screenshots/`. Anchors follow GitHub's rule (`## I05 — AI provider layer` → `#i05--ai-provider-layer`).
- The guide's chapter list is fixed in [guide/README.md](../../guide/README.md): file names `01-business-problem.md`
  … `14-resilience-and-shipping.md`, `advanced/*.md`; link each chapter from the contents when it is written, and
  point the README / hub "Guide" rows at chapter 1 if useful. Each chapter: business problem → theory → what
  FilmSearch does (code paths) → why (ADR, alternatives) → evidence (reports, journal sections) → pitfalls → see also.
  Good material: the I06 experiments report (HNSW vs exact, VACUUM), I07 report (RRF tuning), I08 baseline and
  experiments, I14 summary, I16 catalog growth, I20 Qwen3 report; screenshots `search-scores.png` (hybrid ranks),
  `aspire-trace.png` / `aspire-span.png` (a RAG turn as a trace).
- Handbook role pages replace the "(I21, session 3)" placeholders in [handbook/README.md](../../handbook/README.md)
  and the role rows of the README and the hub (they point at `handbook/README.md#roles` for now). The configuration
  reference can be generated from `src/FilmSearch.Api/appsettings*.json` (sections Search, RateLimiting, Jobs, Tmdb,
  Commerce, Assistant, Payments, AI, Auth, DataProtection) plus `deploy/.env.example`.
- A link check was run by a throw-away script (all relative links and anchors resolve; its only false alarm is
  `AGENTS.md#commands`). Session 4 adds a committed checker.
- License: MIT (`LICENSE`, badge in the README). The social preview image was uploaded by the owner.
