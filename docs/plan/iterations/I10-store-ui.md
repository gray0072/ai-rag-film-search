# I10 — Store UI: catalog, search, cart, checkout

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I10 · ← [I09](I09-cart-orders-fake-payment.md) · [I11](I11-chat-foundation.md) → · [Journal: what happened](../../journal/m1-store-without-ai.md#i10--store-ui)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I02, I04, I09 (I07 for hybrid / understood filters, can be added later) |
| **Parallel with** | I07, I08, I15 |
| **Spec** | [01 Requirements](../../spec/01-requirements.md), [04 API](../../spec/04-api.md) |
| **Requirements** | FR-CAT-*, FR-SRCH-4/5, FR-COM-*, NFR-10/11 |

## Goal
Milestone M1: a user can browse, filter, search, open a movie, add to cart, pay with the fake gateway and see the movie in "My library", in English, Russian or Swedish.

## Tasks
- [x] Catalog page: grid with posters, filters panel (URL query params as state), sorting, paging.
- [x] Search: search box → results page with mode switch (keyword / semantic / hybrid when available) and understood-filter chips (when I07 is merged); debug toggle showing scores.
- [x] Movie page: details, offers (Buy / Rent), similar movies.
- [x] Cart page, checkout button (`Idempotency-Key` generated per attempt), redirect to checkout URL.
- [x] Success page polling order status; failure / cancel pages.
- [x] Library page.
- [x] TMDB attribution in the footer.
- [x] All strings in `en`, `ru` and `sv`.
- [x] Component tests for filters and cart; one Playwright e2e for the purchase flow (optional here, required by I20): component tests done; the e2e flow was run by hand in a browser (see Result), the committed Playwright test moves to I20.

## Acceptance criteria
- [x] Manual walk-through of scenario 1 (spec 00 §5) in both languages.
- [x] No hard-coded user-facing strings (lint rule or review).

## Result

### What was done
- **Pages:** catalog (filters panel, sort, paging; all state in the URL, removable chips), search (mode switch hybrid / keyword / semantic, query understanding on by default with marked "found in your query" chips, degraded notice, debug toggle with keyword / vector ranks and scores), movie page (details, Buy / Rent buttons with DB prices, cart and library state, similar movies), cart (remove, unavailable lines block checkout, checkout button), checkout success (polls the order until the webhook marks it paid / failed / expired; "pay again" after a failure), new checkout cancel page (the order is kept until it expires, "return to payment"), library (owned / rental with end time). Header cart badge; footer TMDB attribution with a link.
- **Shared code:** `features/filters/filters.ts` (URL ↔ UI ↔ API mapping of the FR-CAT-2 filters, chips), `FiltersPanel`, `FilterChips`, `MovieCard` / `MovieGrid` / `Poster` / `Pagination`, cart hooks that put the cart returned by mutations into the query cache, `checkout.ts` (`placeOrderAndPay`: a new `Idempotency-Key` per checkout attempt, then the payment session and a redirect), `commerceErrors.ts` (Problem Details `code` → localized message), `lib/format.ts` (`Intl` money, dates, ratings, language names).
- **API client:** a middleware turns error responses without a JSON body into Problem Details (an empty body made TanStack Query treat the error as success); hand-written calls throw `ApiError`.
- **i18n:** all new strings in `en`, `ru`, `sv`; no plural forms (texts like "Movies: 39"), so the key sets stay equal across locales.
- **Lint rule (NFR-10):** `no-restricted-syntax` in `eslint.config.js` fails on letters in JSX text and in `alt` / `title` / `placeholder` / `aria-label` literals (tests and `components/ui` excluded). No new package.
- **Backend:** `JsonNumberHandling.Strict` for the HTTP JSON options, so the OpenAPI document types numbers as `integer` / `number` (not `integer | string`) and the generated TypeScript types are plain numbers; `Commerce:CancelUrl` = `/checkout/cancel?orderId={orderId}` (was `/cart`); integration test and spec 07 updated.
- **Tests (web, 7 → 36):** filter mapping; catalog (URL → request, panel → URL, chips, reset, sort, paging); search (request body, understood vs explicit chips, removing an understood chip, mode and debug); movie page (add to cart updates the badge, localized 409, owned movie, unknown id); cart (DB total, remove, order with `Idempotency-Key` and redirect, a new key per attempt, unavailable line); checkout success polling until paid, pay again after a failure, cancel page; library (rental end, Swedish). Helper `src/test/mockApi.ts` (in-memory routes over a stubbed `fetch`).

### Manual checks
- AppHost (real Postgres, seed, no LM Studio): scenario 1 of spec 00 §5 driven in Chrome with a throw-away Playwright script, in **English and Russian**, each with a new owner: filters Drama + 1990–2000 + rating 8 → 39 movies (The Shawshank Redemption / «Побег из Шоушенка» first) → movie page → "Rent for 48 h" → cart badge 1 → cart → "Go to payment" → fake page → Pay → the success page shows "Confirming the payment…", then "Thank you!" → the library lists the rental with its end (`Nov 20, 2026, 9:18 AM` / `20 нояб. 2026 г., 09:18`) → cart badge cleared. Keyword search with scores; without LM Studio hybrid search shows the "degraded" notice and the QU fallback line. No browser errors on store pages.
- Found and fixed during the check: with a lower-case drive letter in the working directory (`d:\…`, e.g. the AppHost started from Git Bash or the VS Code terminal), Vite served modules imported through `@/…` from `/@fs/d:/…`, which loaded a second copy of React ("Invalid hook call", blank page). `vite.config.ts` now takes `root` and the `@` alias from `fs.realpathSync.native` (canonical casing).
- `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, OpenAPI document up to date, `dotnet test` green (unit + integration); web `gen:api` (no diff), lint, format check, typecheck, tests 36/36, build.

### Decisions and deviations
- **Removing an understood chip** turns query understanding off for that search and keeps the other applied filters as explicit ones: the API has no "ignore this understood filter" switch, and with QU on the model would find the filter again. The query text stays as typed.
- **Query understanding is on by default** in the UI (`understand=0` in the URL turns it off); without a chat model it falls back silently (I07 contract).
- **Filters are applied with a button**, not on each keystroke (number inputs would send a request per digit).
- **A cancel page** instead of returning to the cart: it says that nothing was charged and offers to resume the same order.
- **Strict JSON numbers** in the API: clients must send numbers as JSON numbers (`"12"` in a body is now a 400). The web client, the evaluation runner and the tests already did.
- **No Playwright test in the repo yet** (optional in this iteration): it needs browsers in CI and a running stack; I20 adds it.
- **shadcn components added:** card, label, skeleton, alert, checkbox (Radix is already a dependency). Tests stub `ResizeObserver` for Radix in jsdom.

### Follow-ups
- I13: chat page (placeholder kept); movie cards from SSE can reuse `MovieCard`.
- I15: absolute success / cancel URLs for Stripe; the pages only read `orderId`.
- I17: sign-in in the header; merge the anonymous cart.
- I20: committed Playwright e2e of the purchase flow (the manual script covers the same steps).
- Maybe: TMDB logo in the footer (now text + link), a mobile drawer for the filters panel, keeping `mode` / `understand` when searching again from the header box.

### Sessions
- [S11](../../prompts/sessions/2026-11-18_S11_store-ui.md)
