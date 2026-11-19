# I03 — Catalog module and 1000-movie seed

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I03 · ← [I02](I02-web-skeleton.md) · [I04](I04-keyword-search.md) → · [Journal: what happened](../../journal/m1-store-without-ai.md#i03--catalog-module-and-seed)

| | |
|---|---|
| **Status** | done |
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
- [x] Entities: Movie, MovieTranslation, Genre, Person, MovieCredit, Offer. EF configuration and migration (Catalog schema).
- [x] `tools/seed-builder`: console app that reads a TMDB API key from user-secrets and writes `data/seed/movies.seed.json` (about 1000 movies: top rated list, topped up with popular; details with credits, keywords, translations en/ru/sv, external ids). Respects the TMDB rate limit, caches responses locally so a rerun resumes. If no key yet: a hand-made seed of 20–30 movies.
- [x] `data/seed/README.md`: file format, how to regenerate, TMDB attribution.
- [x] `PricingRule`: synthetic Buy / Rent prices by year and popularity (unit tested).
- [x] Seed loader: upsert by `TmdbId` in batches, runs on startup in Development if the catalog is empty, plus `POST /api/admin/ingestion/seed`.
- [x] `ICatalogReader` / `ICatalogWriter` in Core.
- [x] Endpoints: `GET /api/movies` (filters and sorting from spec 04), `GET /api/movies/{id}`, `GET /api/genres`. Localized fields by `Accept-Language`.
- [x] Integration tests: filters, paging, locale fallback, idempotent seed.

## Out of scope
Search (I04, I06), TMDB live sync (I16), similar movies (I06).

## Acceptance criteria
- [x] Fresh start → about 1000 movies in the DB in under a minute; second start does not duplicate. (Load time checked with 1000 synthetic TMDB-shaped movies; the committed seed has 34 movies until a TMDB key is available, see Follow-ups.)
- [x] `GET /api/movies?genres=drama&yearFrom=1990&yearTo=2000&minRating=8` returns correct results (test).
- [x] `Accept-Language: ru` / `sv` returns Russian / Swedish titles where present, English otherwise.

## Open questions
- ~~Keep the full TMDB response in a JSONB column for later re-mapping?~~ Not now: see Decisions.

## Result

### What was done
- **Domain (Core):** `Movie`, `MovieTranslation`, `Genre`, `Person`, `MovieCredit`, `Offer`; `MovieFilter`, `MovieSort`, read models (`MovieSummary`, `MovieDetails`, `GenreItem`, …); `ICatalogReader`, `ICatalogWriter`, `ICatalogSeeder`; import model (`MovieImport`, `GenreImport`, …) = seed file format; `SeedFile` + `SeedJson`; `Locales` (`en`/`ru`/`sv`, normalization); `PricingRule`.
- **Persistence:** EF configuration in schema `catalog` (enums as strings, indexes on year, rating, popularity, language; unique TMDB ids, genre slugs, one offer per type), migration `I03_AddCatalog`.
- **Reader:** filters, sorting with a unique tie-breaker, paging, localized projections with per-field English fallback, all in SQL. `MovieQueryExtensions` is public so I04/I06 reuse the same filters.
- **Writer:** upsert by TMDB id in one transaction per batch, serialized by a transaction-level advisory lock; `SourceHash` skips unchanged movies; translations, genres, credits and offers are synced (not deleted and re-inserted); prices from `PricingRule` on every upsert.
- **Seed:** `CatalogSeeder` (batches of 100); loaded on startup in Development when the catalog is empty (`Catalog:Seed:LoadOnStartup`) and by `POST /api/admin/ingestion/seed` (mapped only when `Admin:Enabled`, on in Development and tests). The file is copied to the API output (`Seed/movies.seed.json`).
- **Seed file:** hand-made, 34 movies, 19 genres with en/ru/sv names, overviews in three languages written for the project. Format in [data/seed/README.md](../../../data/seed/README.md).
- **`tools/seed-builder`:** console app: TMDB key from user secrets / env, top rated (800) + popular (200, new titles only), throttled client with retries and a disk cache (resume), stable output sorted by TMDB id. TMDB DTOs and `TmdbMovieMapper` live in `FilmSearch.Infrastructure/Tmdb` for I16.
- **API:** `GET /api/movies`, `GET /api/movies/{id}`, `GET /api/genres`; locale from `Accept-Language` through `RequestLocalization` (also sets `Content-Language`); validation through `AddValidation()` + `IValidatableObject` → 400 Problem Details. OpenAPI document and web types regenerated.
- **Migrations:** `MigrateDatabaseAsync` now holds a session advisory lock (found by parallel test hosts migrating at the same time; the same would happen with several API replicas).
- **Tests:** unit 51 (pricing, locales, seed file validity, request parsing, TMDB mapping); integration 30 (acceptance filter query, genre any-of, runtime / language / price / offer filters, paging, sorting incl. localized title, `Accept-Language` incl. `sv-SE` and unknown, per-field fallback, details with credits and prices, 404, 400 cases, idempotent reseed, 4 concurrent writers, update of changed data, 1000 movies in a separate database). All 81 pass.
- **Workflow docs:** no pull requests from now on (AGENTS.md step 9, roadmap); CI also runs on pushes to `iter/**`. Prompt history format: English translation first, then the original; S01–S03 updated.

### Manual checks
- `dotnet run --project src/FilmSearch.AppHost` against the existing dev database: migration applied, seed loaded on startup; `GET /api/movies?genres=drama&yearFrom=1990&yearTo=2000&minRating=8&sort=rating` with `Accept-Language: sv` → 10 movies, first "Nyckeln till frihet" (1994, buy 9.99); `POST /api/admin/ingestion/seed` → `created 0, updated 0, unchanged 34` in 175 ms.
- `dotnet run --project tools/seed-builder` without a key prints how to set it and exits with code 1. A run against TMDB was not possible (no key).
- 1000-movie load test (separate database): the whole test incl. container start takes about 16 s.
- `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, `dotnet test` (81/81); web `gen:api`, lint, format check, typecheck, tests (7/7).

### Decisions and deviations
- **Hand-made seed instead of 1000 TMDB movies** (allowed by the plan when there is no key). The builder is ready; the file is replaced by one command when a key exists.
- **Seed file is an object with `genres` and `movies`**, not a bare array (spec 08 updated): genre names in ru/sv come from TMDB too, and `version` lets the loader reject an incompatible file.
- **No `SourcePayload` JSONB column.** The seed file holds the mapped model, not raw TMDB responses (raw ones are about 30 KB per movie). The builder keeps raw responses in its cache; I16 can add a payload column if re-mapping without refetching is needed.
- **`SourceHash` on `Movie`** (spec 03 updated): makes a reseed a no-op and gives cheap change detection for embeddings later (FR-DATA-3).
- **Prices use vote count, not TMDB popularity**, for the "popular" premium: popularity changes daily and prices would change with every sync. Currency USD (spec 02).
- **`Genre.NameSv` added; `Person.TmdbId` nullable** (spec 03 updated): Swedish is a supported locale; hand-made people have no TMDB ids and are matched by name.
- **`Year` is a normal column set on write**, not a generated column: `extract(year …)` would also work, but writing it in one place (the writer) keeps the migration simple.
- **`sort` and `offerType` are strings in the request**, parsed case-insensitively: minimal APIs bind enums case-sensitively, so `sort=rating` (spec 04 values) failed with 400. JSON enums switched to camelCase (`buy`, `rent`) to match. Query parameter names are camelCase in OpenAPI through `[FromQuery(Name = …)]`.
- **Default sort `popularity`, default page size 20** (spec 04 updated).
- **Admin endpoints** are mapped when `Admin:Enabled` is true (not by environment name), and always during OpenAPI generation so the contract does not depend on the build environment.
- **`tools/seed-builder` uses `FrameworkReference Microsoft.AspNetCore.App`** for configuration and user secrets: no new NuGet packages.
- **Advisory locks** (migrations, catalog upserts) instead of retries on unique violations: simpler and correct for multiple processes.

### Follow-ups
- **Get a TMDB key and rebuild the seed** (about 1000 movies): see [data/seed/README.md](../../../data/seed/README.md). Then check the acceptance query again and that the full load stays under a minute.
- I04: full-text `tsvector` columns over the catalog (title, keywords, people); reuse `MovieQueryExtensions.ApplyFilter`.
- I06: embedding text and `ContentHash` can build on `SourceHash`; `GET /api/movies/{id}/similar`.
- I10: poster URLs (`https://image.tmdb.org/t/p/w342{posterPath}`) and a placeholder for movies without a poster (all hand-made ones).
- I16: move the TMDB client into Infrastructure with `HttpClient` resilience; consider a `SourcePayload` column.
- I17: protect admin endpoints with the `admin` role instead of `Admin:Enabled`.

### Sessions
- [S04](../../prompts/sessions/2026-11-17_S04_catalog-seed.md)
