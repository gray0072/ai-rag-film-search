# I03 — Catalog module and 1000-movie seed

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I01 |
| **Parallel with** | I02, I05 |
| **Branch** | `iter/I03-catalog` |
| **Spec** | [03 Domain](../../spec/03-domain-model.md), [04 API](../../spec/04-api.md#catalog), [08 Ingestion](../../spec/08-data-ingestion.md) |
| **Requirements** | FR-CAT-1..5, FR-DATA-1 |

## Goal
The DB contains about 1000 movies with en/ru/sv texts, genres, people and offers. Catalog endpoints return them with filters and sorting.

## You will learn
Data modeling for search, building a dataset, idempotent loading.

## Tasks
- [ ] Entities: Movie, MovieTranslation, Genre, Person, MovieCredit, Offer. EF configuration and migration (Catalog schema).
- [ ] `tools/seed-builder`: console app that reads a TMDB API key from user-secrets and writes `data/seed/movies.seed.json` (about 1000 movies: top rated list, topped up with popular; details with credits, keywords, translations en/ru/sv, external ids). Respects the TMDB rate limit, caches responses locally so a rerun resumes. If no key yet: a hand-made seed of 20–30 movies.
- [ ] `data/seed/README.md`: file format, how to regenerate, TMDB attribution.
- [ ] `PricingRule`: synthetic Buy / Rent prices by year and popularity (unit tested).
- [ ] Seed loader: upsert by `TmdbId` in batches, runs on startup in Development if the catalog is empty, plus `POST /api/admin/ingestion/seed`.
- [ ] `ICatalogReader` / `ICatalogWriter` in Core.
- [ ] Endpoints: `GET /api/movies` (filters and sorting from spec 04), `GET /api/movies/{id}`, `GET /api/genres`. Localized fields by `Accept-Language`.
- [ ] Integration tests: filters, paging, locale fallback, idempotent seed.

## Out of scope
Search (I04, I06), TMDB live sync (I16), similar movies (I06).

## Acceptance criteria
- [ ] Fresh start → about 1000 movies in the DB in under a minute; second start does not duplicate.
- [ ] `GET /api/movies?genres=drama&yearFrom=1990&yearTo=2000&minRating=8` returns correct results (test).
- [ ] `Accept-Language: ru` / `sv` returns Russian / Swedish titles where present, English otherwise.

## Open questions
- Keep the full TMDB response in a JSONB column for later re-mapping? (Probably yes: `SourcePayload`.)
