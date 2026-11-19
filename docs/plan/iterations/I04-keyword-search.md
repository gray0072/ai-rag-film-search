# I04 — Keyword search and filters

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I04 · ← [I03](I03-catalog-seed.md) · [I05](I05-ai-providers.md) → · [Journal: what happened](../../journal/m1-store-without-ai.md#i04--keyword-search-and-filters)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I03 |
| **Parallel with** | I06, I09 |
| **Branch** | `iter/I04-keyword-search` |
| **Spec** | [05 Search §2](../../spec/05-search.md#2-keyword-search) |
| **Requirements** | FR-SRCH-1, FR-SRCH-5, FR-SRCH-6 |

## Goal
`POST /api/search` with `mode=keyword` finds movies by words in title, people, keywords and overview in English, Russian and Swedish, with all catalog filters.

## You will learn
Full-text search in PostgreSQL (the "sparse" half of hybrid search), ranking, why keyword search is still needed in RAG.

## Tasks
- [x] Generated `tsvector` columns (`english`, `russian`, `swedish`) with weights; GIN indexes; migration.
- [x] `IMovieSearch` contract in Core (`SearchRequest`, `SearchResult` with debug scores). Keyword implementation in `FilmSearch.AI/Search/Keyword` (or Infrastructure; keep it in its own folder to avoid conflicts with I06).
- [x] Shared filter builder (used by keyword and later vector search).
- [x] `POST /api/search` endpoint; `mode` other than `keyword` returns 400 until I06/I07.
- [ ] `pg_trgm` title similarity for typos (optional in this iteration). Not done: moved to I07 (see follow-ups).
- [x] Integration tests: en, ru and sv queries, filters, debug scores.

## Acceptance criteria
- [x] "nolan dream" finds Inception; "крестный отец" finds The Godfather.
- [x] Filters combine with the query.
- [x] p95 < 200 ms locally on the seed data (simple measurement in the test log).

## Result

### What was done
- **Seed rebuilt from TMDB** (I03 follow-up): 1000 movies, 19 genres, Russian titles for 976 movies, Swedish titles for 483 (details in [data/seed/README.md](../../../data/seed/README.md)). `SeedJson` now writes LF line endings. Two I03 tests that assumed the 34-movie hand-made seed now derive their expectations from the seed file.
- **Search document** `search.movie_documents` (entity `MovieSearchDocument`): texts per locale with English fallback, original title, keywords, people; generated `search_en`, `search_ru`, `search_sv` `tsvector` columns with weights A/B/C/D and GIN indexes. Migration `I04_AddKeywordSearch` also creates the SQL function `search.refresh_movie_documents(uuid[])` and fills documents for existing movies.
- **`ICatalogChangeHandler`** (Core): the catalog writer calls every handler inside its write transaction with the ids of created and changed movies. `MovieDocumentIndexer` (the keyword handler) runs the SQL function. I06 can use the same hook to find movies that need new embeddings.
- **`IMovieSearch`** (Core: `SearchQuery`, `SearchResult`, `SearchItem`, `SearchDebug`, `SearchMode`). `MovieSearch` (Infrastructure/Search) runs `KeywordRetriever` and loads the movies through `ICatalogReader.GetMoviesAsync` in the request locale, keeping the order.
- **`KeywordRetriever`** (Infrastructure/Search/Keyword): one LINQ query (Npgsql full-text functions) joining the filtered movies (`MovieQueryExtensions.ApplyFilter`, the shared filter builder from I03) with the documents; `websearch_to_tsquery` per config, a match on any config, score = best `ts_rank_cd` (normalization 32), ties by popularity and id. If nothing matches all words, one retry with any word (`debug.keywordMatch = any`). `ё` → `е` in documents and queries.
- **API** `POST /api/search` (Search module): JSON body with `query`, `mode` (enum, default `keyword`), `filters` (same as the catalog list, genres as an array), `page`, `pageSize`, `debug`, `understand`. Validation through `AddValidation()`, including the nested filters; `semantic` / `hybrid` / `understand=true` → 400 Problem Details. Response `{ items: [{ movie, score, debug? }], appliedFilters, page, pageSize, total }`. OpenAPI document and web types regenerated.
- **Tests:** unit 67 (+16: request mapping and validation, query normalization, any-word relaxation); integration 56 (+25: en/ru/sv title queries including a query language different from the UI locale, "nolan dream", title weight vs overview, filters combined with the query including price / offer / language, any-word relaxation, empty result, paging continues the ranks, debug only on request, documents follow catalog changes, 7 validation cases, 3 malformed bodies, latency). All pass.

### Manual checks
- AppHost against the existing dev database (34 hand-made movies): migration applied, documents backfilled; "nolan dream" → Inception before any reseed. `POST /api/admin/ingestion/seed` with the new file: created 968, updated 32 in 6.3 s.
- `POST /api/search`: "nolan dream" → Inception; "крестный отец" (ru) → Крёстный отец, 2, 3; "gudfadern" (sv) → Gudfadern del II, Gudfadern, Gudfadern del III; "Тёмный рыцарь" → the Dark Knight movies; "kärlek" with `Accept-Language: ru` → 42 movies with Russian titles; "space" with `yearFrom 2010, minRating 8` → 11 movies (Interstellar among them); `mode: semantic` → 400.
- Latency (test log, in-process API, 48 requests over 1000 movies including loading the summaries): **p50 29 ms, p95 40 ms**.
- `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, `dotnet test`; web `gen:api`, lint, format check, typecheck, tests, build.
- CI: the first run on `main` failed the "OpenAPI document is up to date" check: multi-line doc comments on `SearchDebug` became `\r\n` in `v1.json` on Windows and `\n` on Linux. Fixed by single-line comments; the rule is in AGENTS.md.

### Decisions and deviations
- **Separate document table instead of `SearchVector*` columns on `movies`** (spec 03 and 05 updated). A generated column can only read its own row, and the texts are spread over `movies`, `movie_translations`, `movie_credits` and `people`. The table lives in the `search` schema (Search owns its indexes, spec 02 §5); deleting a movie cascades.
- **Flattening in a SQL function, not in C#.** The migration needs it for the backfill and the writer for updates; one definition serves both, in the same transaction as the catalog change. Changing the document shape means a new migration with `CREATE OR REPLACE FUNCTION` and a refresh of all rows.
- **Catalog → Search coupling through `ICatalogChangeHandler`** in Core, not a direct call: the catalog writer does not know about search tables.
- **Any-word relaxation** when the all-words query finds nothing. The AND semantics of `websearch_to_tsquery` return nothing for most longer queries; the relaxed query still ranks movies that match more words higher. It is marked in debug, so evaluations (I08) can measure it.
- **`ё` folding** in both the generated columns and the query: TMDB uses "Крёстный отец", most people type "крестный".
- **`mode` and `offerType` are JSON enums** in the body (case-insensitive camelCase strings), unlike the catalog query string, where minimal APIs bind enums case-sensitively. The OpenAPI document lists the values.
- **Keyword code in Infrastructure** (`Search/Keyword`), not in `FilmSearch.AI`: it is EF Core / SQL code, and `FilmSearch.AI` does not reference Infrastructure. `InternalsVisibleTo FilmSearch.UnitTests` added to Infrastructure for unit tests of internal helpers.
- **TMDB secrets** moved from the files `tmdb-api-key` / `tmdb-read-access` in the repository root (untracked; deleted after the values were checked in the store) to the user secrets of `tools/seed-builder`: `Tmdb:ApiKey` = read access token (used), `Tmdb:ApiKeyV3` = v3 key (kept). User secrets instead of a `.env` file: .NET reads them without extra packages, the project already uses them, and they are outside the repository.

### Follow-ups
- I07: `pg_trgm` title similarity for typos ("incepton") and an exact-title boost. With normalization 32 many title matches get the same score (0.5), so sequels tie and popularity decides ("gudfadern" → Part II first).
- I07 / I08: natural-language queries ("фильмы про мафию") barely match keyword search; that is the semantic half's job. Compare keyword and hybrid on such queries in I08.
- I16: when a person's name changes, the documents of their other movies are not refreshed (only changed movies are). Refresh by person, or rebuild all documents after a sync.
- I06: use `ICatalogChangeHandler` to find movies that need new embeddings. `text-embedding-embeddinggemma-300m` in LM Studio returns 768-dimensional vectors (input for the vector column ADR).

### Sessions
- [S05](../../prompts/sessions/2026-11-17_S05_keyword-search.md)
