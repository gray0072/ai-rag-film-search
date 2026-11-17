# I02 — Web app skeleton (React, i18n, API client)

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I01 |
| **Parallel with** | I03, I05 |
| **Branch** | `iter/I02-web` |
| **Spec** | [02 Architecture](../../spec/02-architecture.md), [04 API](../../spec/04-api.md) |
| **Requirements** | NFR-10, NFR-11 |

## Goal
A React app in `src/web` starts from the AppHost, has routing, layout, i18n (`en` default, `ru`, `sv`), a typed API client generated from OpenAPI, and CI checks.

## You will learn
Modern React tooling, OpenAPI-driven contracts, i18n.

## Tasks
- [ ] Vite + React + TypeScript (strict). Package manager decision (pnpm or npm).
- [ ] UI library decision (shadcn/ui + Tailwind or Mantine). Record in the Result section.
- [ ] Routing: `/` (catalog), `/movies/:id`, `/search`, `/cart`, `/checkout/success`, `/library`, `/chat`. Placeholder pages.
- [ ] Layout: header (search box, cart icon, language switcher, link to chat), footer with TMDB attribution placeholder.
- [ ] i18next with `en`, `ru` and `sv` resources; language stored in localStorage and sent as `Accept-Language`.
- [ ] API client: `openapi-typescript` (+ `openapi-fetch`) or orval, generated from `/openapi/v1.json`; script `npm run gen:api`. TanStack Query set up.
- [ ] AppHost: add the Vite app (`AddViteApp` / npm app) with the API URL injected; dev proxy for `/api`.
- [ ] ESLint, Prettier, Vitest + Testing Library with one test. CI job for the web app.

## Out of scope
Real pages (I10), chat (I13).

## Acceptance criteria
- [ ] AppHost starts the web app; header language switcher changes all visible texts.
- [ ] A health / ping call through the generated client works.
- [ ] Lint, typecheck, test and build pass in CI.
