# I10 — Store UI: catalog, search, cart, checkout

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I02, I04, I09 (I07 for hybrid / understood filters, can be added later) |
| **Parallel with** | I07, I08, I15 |
| **Branch** | `iter/I10-store-ui` |
| **Spec** | [01 Requirements](../../spec/01-requirements.md), [04 API](../../spec/04-api.md) |
| **Requirements** | FR-CAT-*, FR-SRCH-4/5, FR-COM-*, NFR-10/11 |

## Goal
Milestone M1: a user can browse, filter, search, open a movie, add to cart, pay with the fake gateway and see the movie in "My library", in English, Russian or Swedish.

## Tasks
- [ ] Catalog page: grid with posters, filters panel (URL query params as state), sorting, paging.
- [ ] Search: search box → results page with mode switch (keyword / semantic / hybrid when available) and understood-filter chips (when I07 is merged); debug toggle showing scores.
- [ ] Movie page: details, offers (Buy / Rent), similar movies.
- [ ] Cart page, checkout button (`Idempotency-Key` generated per attempt), redirect to checkout URL.
- [ ] Success page polling order status; failure / cancel pages.
- [ ] Library page.
- [ ] TMDB attribution in the footer.
- [ ] All strings in `en`, `ru` and `sv`.
- [ ] Component tests for filters and cart; one Playwright e2e for the purchase flow (optional here, required by I20).

## Acceptance criteria
- [ ] Manual walk-through of scenario 1 (spec 00 §5) in both languages.
- [ ] No hard-coded user-facing strings (lint rule or review).
