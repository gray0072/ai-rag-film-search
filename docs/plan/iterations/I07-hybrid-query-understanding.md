# I07 — Hybrid search and query understanding

| | |
|---|---|
| **Status** | done |
| **Depends on** | I04, I06 |
| **Parallel with** | I08, I10, I15 |
| **Spec** | [05 Search §4–5](../../spec/05-search.md#4-hybrid-search) |
| **Requirements** | FR-SRCH-3, FR-SRCH-4, FR-SRCH-6 |

## Goal
Hybrid (RRF) is the default search mode. A free-text query like "фильмы 90-х про космос с рейтингом выше 7, без ужасов" is turned into filters + semantic text by an LLM with structured output.

## You will learn
Hybrid retrieval and rank fusion; structured output with JSON Schema; validation, retries and fallbacks for LLM output; prompt files and versioning.

## Tasks
- [x] `mode=hybrid`: run keyword and semantic in parallel, RRF with configurable `k` and weights, debug scores.
- [x] `IQueryUnderstanding`: C# record → JSON Schema, prompt `Prompts/query-understanding.v1.md` (now v2), genre slugs as enum, validation, one retry with error, fallback.
- [x] `similarToTitles` resolution and query vector blending (try 2 strategies, keep the better one by I08 numbers if available).
- [x] Cache QU results (memory cache).
- [x] `understand=true` in `POST /api/search`; response contains `understoodQuery` and `appliedFilters`.
- [x] Prompt loader (embedded resources or files) with version in telemetry.
- [x] Tests: RRF math (unit), QU with a fake chat client returning invalid then valid JSON (retry path).

## Acceptance criteria
- [x] Hybrid is better than or equal to both single modes on the I08 golden set (if I08 is done; otherwise on 10 manual queries, written in Result).
- [x] QU produces valid filters for the example queries in spec 05; invalid output never breaks search.

## Result

### What was done
- **Hybrid mode** (`MovieSearch.HybridAsync`, default mode of `POST /api/search`): top `Search:Hybrid:CandidatesPerList` (50) keyword and semantic candidates with the same pre-filters, fused by `ReciprocalRankFusion` (`Σ weight / (k + rank)`, deterministic ties). The query embedding (HTTP) runs while the keyword query runs; the DB queries stay sequential (one `DbContext`). Debug: keyword rank / score / match, vector rank / similarity, fused score. When the query cannot be embedded, hybrid returns the keyword list with `degraded: true` (semantic mode keeps its 503). Options `Search:Hybrid` (validated on start): `RrfK` 20, `KeywordWeight` 1, `RelaxedKeywordWeight` 0.1, `SemanticWeight` 1, `CandidatesPerList` 50, `SimilarTitles`, `SimilarTitleWeight`, `MaxSimilarTitles` 3.
- **Query understanding** (`IQueryUnderstanding` in Core, `QueryUnderstandingService` in `FilmSearch.AI`): system prompt `query-understanding.v2` with the catalog genres (slug + en/ru/sv names) and the current year; the user query as the user message; `response_format: json_schema` generated from the record `QueryUnderstandingOutput` (`AIJsonUtilities.CreateJsonSchema`, all properties required, no extra properties, genre enum added at run time); temperature 0. `QueryUnderstandingValidator` checks genres, year range and order, rating, price, language code, title count; an invalid answer goes back to the model with the error for one retry; then fallback (no filters, whole query). Provider errors fall back at once. Result `UnderstoodQuery` (semantic query, filters, similar titles, detected locale, source `llm` / `cache` / `fallback`, prompt version, error).
- **Merging and exclusions:** `UnderstoodFilters.MergeInto` — explicit request filters win, excluded genres add up. New `MovieFilter.ExcludedGenreSlugs` and `ExcludedMovieIds` (applied in `ApplyFilter`, so in both retrievers' SQL); `excludeGenres` in the HTTP filters.
- **"Movies like X":** titles resolved by keyword search (best hit with all words matched) and left out of the results; strategies `AverageVector` (default, `QueryVectors.Blend`) and `ExtraList` (neighbours of each title as an extra RRF list). `SemanticRetriever` split into `EmbedQueryAsync`, `NearestAsync(vector)` and `GetMovieVectorsAsync`.
- **Cache:** valid QU results in `IMemoryCache` per normalized query (case, whitespace, ё/е) + prompt id + chat profile, 60 min (`Search:QueryUnderstanding:CacheMinutes`); the rendered prompt and schema are cached too.
- **Prompt loader** (`PromptLibrary`, `PromptTemplate`): `Prompts/*.vN.md` embedded with a fixed resource name, latest version unless `Search:QueryUnderstanding:PromptVersion` pins one, `{{placeholder}}` rendering that fails on missing or unused values. Span `query_understanding` (source `FilmSearch.AI`, parent of the GenAI chat span) with tags `filmsearch.prompt.id`, `filmsearch.qu.source`, `filmsearch.qu.attempts`; a log line with the prompt id and the result.
- **API:** `mode` default `hybrid`; `understand=true` works in every mode; response `understoodQuery`, `appliedFilters` (merged), `degraded`. OpenAPI document and web types regenerated. Spec 04 and 05 updated.
- **Tests:** unit — RRF math (weights, k, ties, duplicates), vector blending, filter merging, prompt library, validator (10 invalid cases, code fences), QU service through the real M.E.AI / OpenAI pipeline with a fake HTTP provider (schema and enum on the wire, user text not in the system prompt, invalid → retry with the error → valid, two invalid → fallback, provider error → fallback without retry, cache); integration — hybrid default with RRF scores, paging, pre-filtering, QU filters with explicit override and cache, fallback, "like X" with both strategies, QU in all modes, degraded hybrid when embeddings fail. `ScriptedChatClient` answers chat calls in integration tests.

### Manual checks (real models: EmbeddingGemma 300M, Gemma 4 E4B, AppHost) — [report](../../../evals/reports/2026-11-17_I07_hybrid-and-query-understanding.md)
- **Acceptance (hybrid ≥ both single modes) on 46 manual queries** (12 plot descriptions × en / ru / sv + 10 title / name queries), MRR@10:

  | Mode | en plots | ru plots | sv plots | titles, names |
  |---|---|---|---|---|
  | keyword | 0.15 | 0.22 | 0.11 | 0.95 |
  | semantic | 0.87 | 0.78 | 0.74 | 0.83 |
  | hybrid, k 60, weights 1 / 1 (before tuning) | 0.51 | 0.76 | 0.43 | 1.00 |
  | **hybrid, k 20, relaxed keyword weight 0.1 (default)** | **0.87** | **0.92** | **0.74** | **1.00** |

  Median latency: keyword 94 ms, semantic 94 ms, hybrid 113 ms.
- **QU on 14 queries in three languages** (the spec 05 example, decades, ratings, prices, languages, excluded genres, "like X", bare titles, a prompt injection): valid filters for all; the injection gave a harmless `comedy` genre with v2 and a fallback with v1, and search answered in both cases. 2.6–3.2 s per call; cached repeats are free.
- **Prompt v1 → v2:** v1 treated bare titles and names as "similar to" ("the godfather" excluded The Godfather itself) and gave one-word semantic texts; v2 fixed both.
- **Similar-titles strategies** on 10 "like X" queries: equal overall, so `AverageVector` stays (one vector query).
- `dotnet build` (Release, 0 warnings), `dotnet format --verify-no-changes`, OpenAPI document identical in Debug and Release, `dotnet test` 231/231 (unit + integration with Docker); web `gen:api`, lint, format check, typecheck, tests (7/7), build.

### Decisions and deviations
- **Relaxed keyword list weight 0.1 and k = 20** instead of the spec's k = 60 with weights 1.0 / 1.0: plain RRF made hybrid worse than semantic on descriptions (report §1). Spec 05 §4 updated. To be re-tuned with I08.
- **Genres only on request:** the prompt does not guess genres from the plot (the spec example put `science-fiction` for "time travel"): a guessed genre is a hard pre-filter and hides good matches. Spec 05 §5 updated.
- **The keyword retriever searches the semantic text too**, not the raw query: filter words ("90-х", "рейтинг") only add noise to full-text search.
- **Named movies are excluded** from "like X" results (pre-filter `ExcludedMovieIds`).
- **Semantic mode always averages** for "like X" (one vector list keeps `score` a real similarity); `ExtraList` applies to hybrid only.
- **Fallback results are not cached:** a provider outage should not stick for an hour.
- **Prompt v1 kept** next to v2 (versions are history and can be pinned for comparison).
- **No new packages:** `IMemoryCache` comes with the ASP.NET Core shared framework, JSON Schema generation with Microsoft.Extensions.AI.
- **Process change (owner, this session):** no iteration branches; work is committed to `main` after all checks pass locally (AGENTS.md workflow step 9, roadmap, plan template; CI runs on `main` only). The other branches were deleted.
- **Test infrastructure:** `CatalogApiFixture` embeds the shared database once per test run (a static lock): two fixtures embedding at the same time inserted the same rows.

### Follow-ups
- I08: golden set with plot, title, name, filter and "like X" queries; re-tune `RrfK`, the keyword weights and the similar-titles strategy; QU accuracy per field in en / ru / sv.
- I10: show the filters of `understoodQuery` as removable chips (FR-SRCH-4); show `degraded`.
- I11 / I12: the assistant's search tool can use `understand=true` or pass the model's own filters.
- I19: QU adds ≈ 3 s on a local model — a faster profile (`Search:QueryUnderstanding:ChatProfile`) or skipping QU for short title-like queries; a size limit for the QU memory cache; a circuit breaker for query embeddings (hybrid waits for the profile's retries before it degrades).

### Sessions
- [S08](../../prompts/sessions/2026-11-17_S08_hybrid-query-understanding.md)
