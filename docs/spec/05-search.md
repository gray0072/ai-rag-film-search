# 05 — Search

Search is the retrieval part of RAG. The UI and the assistant use the same `IMovieSearch` service.

## 1. Pipeline

```mermaid
flowchart LR
    Q[User query<br/>en / ru / sv] --> QU{understand?}
    QU -- yes --> LLM[Query understanding<br/>LLM + JSON Schema]
    LLM --> F[Filters + semantic text]
    QU -- no --> F
    F --> KW[Keyword: PostgreSQL FTS<br/>top 50]
    F --> VEC[Semantic: pgvector cosine<br/>top 50]
    KW --> RRF[Fusion: RRF k=60]
    VEC --> RRF
    RRF --> RR[Rerank<br/>stretch]
    RR --> R[Results + debug scores]
```

Filters (genre, year, rating, price…) are applied **inside** both the keyword and the vector query (pre-filtering in SQL), not after the top-k is taken. Otherwise strict filters can return empty results.

## 2. Keyword search
- Three generated `tsvector` columns: `english`, `russian` and `swedish` configs (each over the texts of its locale, falling back to `en`). Weights: title A, original title A, keywords and people B, tagline C, overview D.
- Query with `websearch_to_tsquery` on all three configs, rank with `ts_rank_cd`, take the best score per movie.
- Title typos: `pg_trgm` similarity on titles as an extra signal (from I07 if needed).

## 3. Semantic search
- **Embedding document** of a movie, built by `MovieEmbeddingTextBuilder` (deterministic, versioned):
  ```
  Title: Inception (2010)
  Genres: Action, Science Fiction, Adventure
  Director: Christopher Nolan
  Cast: Leonardo DiCaprio, Joseph Gordon-Levitt, Elliot Page
  Keywords: dream, subconscious, heist
  Tagline: Your mind is the scene of the crime.
  Overview: Cobb, a skilled thief who commits corporate espionage by infiltrating the subconscious...
  ```
  The builder version is part of `ContentHash`, so changing the template triggers re-embedding.
- One vector per movie and locale (`en` first; `ru` / `sv` vectors are an experiment in I06/I08, compared with "multilingual model + en only").
- Query side: some models need prefixes (`query: ` / `passage: ` for e5; instructions for others). This is part of the embedding profile config.
- Index: HNSW, `vector_cosine_ops`. For 1000–10 000 rows an exact scan is fast too; the index is there to learn how it works and to measure recall vs. exact search.

## 4. Hybrid search
- Reciprocal Rank Fusion: `score = Σ 1 / (k + rank_i)`, `k = 60`, per list. Weights per list are configurable (start 1.0 / 1.0) and tuned with evaluations.
- Debug output per item: `keywordRank`, `keywordScore`, `vectorRank`, `similarity`, `fusedScore`.

## 5. Query understanding (QU)
An LLM call with structured output turns free text into:
```json
{
  "semanticQuery": "time travel story, thoughtful, not action-heavy",
  "filters": {
    "genresInclude": ["science-fiction"],
    "genresExclude": ["action"],
    "yearFrom": null, "yearTo": null,
    "minRating": null,
    "maxPriceMinor": null,
    "language": null
  },
  "similarToTitles": [],
  "detectedLocale": "ru"
}
```
- The JSON Schema is generated from a C# record. The answer is validated. On invalid output: one retry with the error message, then fallback to "no filters, full query as semantic text".
- Genre values must come from the known genre slugs (enum in the schema).
- `similarToTitles` is resolved to movie ids through keyword search, and their vectors are added to the query (e.g. average with the query vector). Tuned in I07.
- The prompt is a versioned file in `src/FilmSearch.AI/Prompts/`.
- QU results are cached per normalized query (memory cache, TTL 1 h).

## 6. Reranking (stretch)
Options to compare in an evaluation: (a) LLM as listwise reranker on top 20, (b) a cross-encoder reranker model behind an HTTP endpoint (e.g. a local `bge-reranker` service), (c) no reranker. Recorded as an ADR if adopted.

## 7. Quality targets (to calibrate in I08)
Initial targets on the golden set, hybrid mode: Recall@10 ≥ 0.8, MRR ≥ 0.6, for each of `en`, `ru` and `sv` queries. The first evaluation run sets the real baseline; targets are adjusted then.
