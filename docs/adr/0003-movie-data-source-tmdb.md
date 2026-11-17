# ADR-0003 — TMDB as the movie data source (seed first)

- **Status:** Accepted
- **Date:** 2026-11-17
- **Iteration:** I00

## Context
We need movie metadata with plot descriptions (for embeddings), genres, people, ratings, posters, and ideally Russian and Swedish translations.

## Options
1. **IMDb**: no free public API. The free IMDb datasets (TSV) have titles, ratings and people but **no plot descriptions**, and are for non-commercial use only. The official API is paid (AWS Data Exchange).
2. **TMDB**: free API key for non-commercial use, overviews, taglines, keywords, credits, posters, translations including `ru-RU` and `sv-SE`, and `imdb_id` for cross-links.
3. Wikidata / Wikipedia: open, but harder to query and normalize.
4. OMDb: needs a key, limited free tier, mostly IMDb data.

## Decision
TMDB. Start with a committed seed of about 1000 top movies (built once from TMDB), so the app runs without keys. Full TMDB sync comes in I16. `ImdbId` is stored for links.

## Consequences
- TMDB attribution in the UI is required.
- TMDB ratings differ from IMDb ratings. We show TMDB rating.
- Rate limits and resilience must be handled in the import job.
