# I07 — Hybrid search and query understanding

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I04, I06 |
| **Parallel with** | I08, I10, I15 |
| **Branch** | `iter/I07-hybrid-qu` |
| **Spec** | [05 Search §4–5](../../spec/05-search.md#4-hybrid-search) |
| **Requirements** | FR-SRCH-3, FR-SRCH-4, FR-SRCH-6 |

## Goal
Hybrid (RRF) is the default search mode. A free-text query like "фильмы 90-х про космос с рейтингом выше 7, без ужасов" is turned into filters + semantic text by an LLM with structured output.

## You will learn
Hybrid retrieval and rank fusion; structured output with JSON Schema; validation, retries and fallbacks for LLM output; prompt files and versioning.

## Tasks
- [ ] `mode=hybrid`: run keyword and semantic in parallel, RRF with configurable `k` and weights, debug scores.
- [ ] `IQueryUnderstanding`: C# record → JSON Schema, prompt `Prompts/query-understanding.v1.md`, genre slugs as enum, validation, one retry with error, fallback.
- [ ] `similarToTitles` resolution and query vector blending (try 2 strategies, keep the better one by I08 numbers if available).
- [ ] Cache QU results (memory cache).
- [ ] `understand=true` in `POST /api/search`; response contains `understoodQuery` and `appliedFilters`.
- [ ] Prompt loader (embedded resources or files) with version in telemetry.
- [ ] Tests: RRF math (unit), QU with a fake chat client returning invalid then valid JSON (retry path).

## Acceptance criteria
- [ ] Hybrid is better than or equal to both single modes on the I08 golden set (if I08 is done; otherwise on 10 manual queries, written in Result).
- [ ] QU produces valid filters for the example queries in spec 05; invalid output never breaks search.
