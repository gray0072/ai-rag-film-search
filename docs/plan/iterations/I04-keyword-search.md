# I04 — Keyword search and filters

| | |
|---|---|
| **Status** | todo |
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
- [ ] Generated `tsvector` columns (`english`, `russian`, `swedish`) with weights; GIN indexes; migration.
- [ ] `IMovieSearch` contract in Core (`SearchRequest`, `SearchResult` with debug scores). Keyword implementation in `FilmSearch.AI/Search/Keyword` (or Infrastructure; keep it in its own folder to avoid conflicts with I06).
- [ ] Shared filter builder (used by keyword and later vector search).
- [ ] `POST /api/search` endpoint; `mode` other than `keyword` returns 400 until I06/I07.
- [ ] `pg_trgm` title similarity for typos (optional in this iteration).
- [ ] Integration tests: en, ru and sv queries, filters, debug scores.

## Acceptance criteria
- [ ] "nolan dream" finds Inception; "крестный отец" finds The Godfather.
- [ ] Filters combine with the query.
- [ ] p95 < 200 ms locally on the seed data (simple measurement in the test log).
