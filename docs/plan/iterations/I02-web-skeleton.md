# I02 — Web app skeleton (React, i18n, API client)

| | |
|---|---|
| **Status** | done |
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
- [x] Vite + React + TypeScript (strict). Package manager decision (pnpm or npm).
- [x] UI library decision (shadcn/ui + Tailwind or Mantine). Record in the Result section.
- [x] Routing: `/` (catalog), `/movies/:id`, `/search`, `/cart`, `/checkout/success`, `/library`, `/chat`. Placeholder pages.
- [x] Layout: header (search box, cart icon, language switcher, link to chat), footer with TMDB attribution placeholder.
- [x] i18next with `en`, `ru` and `sv` resources; language stored in localStorage and sent as `Accept-Language`.
- [x] API client: `openapi-typescript` (+ `openapi-fetch`) or orval, generated from `/openapi/v1.json`; script `npm run gen:api`. TanStack Query set up.
- [x] AppHost: add the Vite app (`AddViteApp` / npm app) with the API URL injected; dev proxy for `/api`.
- [x] ESLint, Prettier, Vitest + Testing Library with one test. CI job for the web app.

## Out of scope
Real pages (I10), chat (I13).

## Acceptance criteria
- [x] AppHost starts the web app; header language switcher changes all visible texts.
- [x] A health / ping call through the generated client works.
- [x] Lint, typecheck, test and build pass in CI ([#3](https://github.com/gray0072/ai-rag-film-search/pull/3), run 44138696194: web 2 min, .NET 1 min, OpenAPI drift check green on Linux).

## Result

### What was done
- `src/web`: Vite 8 + React 19 + TypeScript 5.9 (strict, plus `noUncheckedIndexedAccess`), npm, Node 24 (`.nvmrc`). Path alias `@/` → `src/`.
- UI: shadcn/ui (Radix base, `new-york` style, neutral colors) + Tailwind CSS v4; components `button`, `input`, `badge`; icons from `lucide-react`.
- Routing (React Router 8, data router): `/`, `/movies/:id`, `/search?q=`, `/cart`, `/checkout/success`, `/library`, `/chat` as placeholder pages (each names the iteration that builds it), and a 404 page.
- Layout: header with logo, main navigation (catalog, library), search box (submits to `/search?q=`), assistant link, language switcher, cart icon; footer with the TMDB attribution text and the API version.
- i18n: i18next + react-i18next, bundled `en` / `ru` / `sv` resources with typed keys (`t("nav.cart")` is checked at compile time), language detection from localStorage (`filmsearch.language`) then the browser, `<html lang>` kept in sync. A test checks that all locales have the same keys.
- API client: the API build writes `src/web/openapi/v1.json` (`Microsoft.Extensions.ApiDescription.Server`); `npm run gen:api` (openapi-typescript) generates `src/api/schema.d.ts`; `openapi-fetch` client with an `Accept-Language` middleware; `openapi-react-query` hooks over TanStack Query. The footer calls `GET /api/info` through it. After a language switch, all queries are invalidated so localized data is refetched.
- AppHost: `AddViteApp("web", "../web").WithNpm(install: true).WithReference(api).WaitFor(api)`; Vite proxies `/api` to the API URL from the injected service discovery variables (fallback `https://localhost:7180`).
- Tooling: ESLint 10 (typescript-eslint `strictTypeChecked` + `stylisticTypeChecked`, react-hooks, react-refresh), Prettier, Vitest 5 + Testing Library + jsdom. 7 tests: layout renders and shows the API version from a stubbed fetch; the language switcher changes visible texts, stores the choice, updates `<html lang>` and refetches with the new `Accept-Language`; API failure state; search box navigation; 404; locale key parity.
- Build: vendor code split into `react` and `vendor` chunks (initial JS ≈ 186 kB gzip).
- CI: new `web` job (npm ci, API types up to date, lint, format check, typecheck, test, build); the .NET job checks that the committed OpenAPI document matches the build output. Dependabot for npm (grouped).
- Docs: [ADR-0007](../../adr/0007-frontend-stack.md), spec 02 (frontend row), spec 04 (where the OpenAPI document comes from), AGENTS.md (frontend rules, web commands, Vitest drive-letter note), README.

### Manual checks
- `dotnet run --project src/FilmSearch.AppHost`: the installer runs `npm install`, then `web` starts (`vite --port <assigned>`) after `api` is healthy. Through the Vite port: `/` and `/movies/42` return the SPA (200); `GET /api/info` is proxied to the API (200 JSON, `Set-Cookie: fs_sid=...`).
- `npm run lint`, `format:check`, `typecheck`, `test` (7/7), `build`: pass. `dotnet build -c Release` (0 warnings), `dotnet format --verify-no-changes`, `dotnet test` (10/10): pass.
- The language switch in a real browser was not clicked through in this session; it is covered by the component test that renders the whole app.

### Decisions and deviations
- Package manager, UI library and API client: see [ADR-0007](../../adr/0007-frontend-stack.md) (npm; shadcn/ui + Tailwind; openapi-typescript + openapi-fetch + openapi-react-query).
- **OpenAPI document generated on build, not fetched from `/openapi/v1.json`** (the plan said "generated from `/openapi/v1.json`"): types can be regenerated without a running API and database, and CI catches contract drift. Build-time generation runs `Program.cs` up to `Build()`, so `AddDatabase()` is skipped when `BuildTimeOpenApi.IsGeneratingDocument` is true.
- **TypeScript 5.9, not 7.0** (latest): typescript-eslint supports `<6.1` and openapi-typescript declares `^5`. Dependabot ignores TypeScript majors until the tools catch up.
- **Language switcher is a native `<select>`**, not a Radix dropdown: accessible by default, works on mobile, simple to test. Language names are endonyms in `src/i18n/languages.ts` (the same in every locale, so not in resource files).
- **Dev proxy instead of CORS**: the browser calls the Vite origin, so the `fs_sid` cookie and `Accept-Language` work without CORS config. The proxy uses `secure: false` because Node does not trust the ASP.NET Core dev certificate (development only). Production hosting of the SPA is decided in I20.
- shadcn's CLI uses its own `cn` package (published by shadcn, MIT) instead of a local `clsx` + `tailwind-merge` helper; kept as generated.
- `npm audit` reports a high-severity ReDoS in `picomatch` (all versions, no fix) pulled in only by dev tooling (Vite, ESLint, Vitest). Not exploitable through the app; recheck when a fix is published.

### Follow-ups
- I10: TMDB logo in the footer, real pages, route-level code splitting if pages grow; a cart count badge on the cart icon.
- I13: markdown rendering for assistant messages without raw HTML.
- I20: how the SPA is served in production (static hosting or behind the API) and the API base URL for it.
- Try TypeScript 6/7 when typescript-eslint and openapi-typescript support it.

### Sessions
- [S03](../../prompts/sessions/2026-11-17_S03_web-skeleton.md)
