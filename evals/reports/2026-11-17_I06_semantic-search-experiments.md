# I06 semantic search experiments — 2026-11-17

Manual experiments of I06 (no evaluation harness yet: that is I08). Two questions from the iteration plan:

1. Does the HNSW index return the same top 10 as an exact scan on the seed?
2. Do vectors of Russian and Swedish movie texts (per-locale vectors) help Russian and Swedish queries, compared with
   English vectors only and a multilingual model?

Plus the acceptance checks of I06 and a model switch (EmbeddingGemma → nomic-embed-text and back).

**Setup.** Branch `iter/I06-embeddings` on top of `681bcee`. Catalog: the 1000-movie seed + 2 hand-added movies (1002).
Embedding text template `movie-profile-v1` ([MovieEmbeddingText](../../src/FilmSearch.Core/Search/MovieEmbeddingText.cs)).
Default model `text-embedding-embeddinggemma-300m` (768 dimensions, query prefix `task: search result | query: `,
document prefix `title: none | text: `); second model `text-embedding-nomic-embed-text-v1.5` (768, `search_query: ` /
`search_document: `). LM Studio on the laptop from the I05 benchmark (RTX 4070 Laptop, AC power). PostgreSQL 17 +
pgvector 0.8.7 (Aspire container). Semantic search: `Search:Semantic:MaxResults` 100, `hnsw.ef_search` = max(40, rows
requested), `hnsw.iterative_scan = relaxed_order`.

## Acceptance checks (real model)

| Query | Result |
|---|---|
| `a heist inside dreams` | **Inception #1** (similarity 0.457), then In the Grey, Dreams |
| `фильм про мафиозную семью` | GoodFellas #1 (0.448), **The Godfather #2** (0.442), A Bronx Tale #3 |
| Similar to Inception | Tenet, Batman Begins, The Dark Knight Rises, Catch Me If You Can, Interstellar |
| Similar to The Godfather | The Godfather Part II (0.775), Part III, GoodFellas, A Bronx Tale, Scarface |

Indexing: 1002 movies in 26.9 s (≈ 37 texts/s, batches of 32); a second run with no changes: 0 embedded, 1002 unchanged
in 1.9 s (only text hashes are compared). nomic-embed-text: 1002 movies in 18.8 s.

Pitfall found on the way: Cyrillic text passed to `curl -d` on the Windows command line is mangled before it reaches the
API (the Russian query above returned unrelated movies with similarity ≈ 0.24). Send UTF-8 from a file
(`--data-binary @query.json`) or from a script.

## 1. HNSW vs exact search

Method: every movie's English vector is a query (1002 doc-to-doc queries, the "similar movies" case). The top 10 rows of
the HNSW index (`enable_seqscan` and `enable_sort` off) are compared with the exact top 10 (sequential scan + sort):
recall@10 = |HNSW ∩ exact| / 10. Script run with `psql` in the AppHost container; times are for all 1002 queries.

| Vectors in the table | Exact scan | HNSW ef_search=10 | ef_search=40 (default) | ef_search=100 |
|---|---|---|---|---|
| 3006 (en+ru+sv) | 12.4 s (12.3 ms/query) | recall 0.9996, 0.15 ms/query | 0.9998, 0.32 ms/query | 0.9998, 0.51 ms/query |
| 1002 (en), right after deleting 2004 ru/sv vectors | 3.4 s (3.4 ms/query) | **0.4655**, 0 identical lists | 0.9991 | 0.9999 |
| 1002 (en), after `VACUUM` | 3.5 s (3.5 ms/query) | 0.9999 | **1.0000**, 0.22 ms/query | 1.0000, 0.37 ms/query |

Findings:

- **On this catalog HNSW is practically exact**: recall@10 ≥ 0.999 from `ef_search = 40` (1002 of 1002 identical top-10
  lists after vacuum). Approximation is not a quality concern at 1000–3000 vectors.
- **HNSW is 10–40× faster per query**, but **the planner still chooses the exact scan** (estimated cost ≈ 460–600 vs.
  ≈ 1640 to start the HNSW scan). Its cost model does not see the real price of a sequential scan here: a 768-float
  vector is 3 KB, more than the 2 KB TOAST threshold, so every row's vector is fetched out of line and the distance is
  computed for all rows. Both plans are far below the time to embed the query (20–30 ms), so the planner's choice is
  left as is; the index matters at larger catalogs (I16) — revisit in I19 (e.g. `SET STORAGE PLAIN` for the column, or
  planner settings), measured with I08.
- **Dead index entries cost recall.** Deleting two thirds of the rows left them in the HNSW graph until vacuum; a scan
  spent its `ef_search` candidates on them: on average **4.8 rows instead of 10** at `ef_search = 10`. After `VACUUM`:
  10. Re-embedding has the same effect (an update is a delete + insert). Changes made in I06: the indexer runs
  `VACUUM (ANALYZE)` after it changes vectors; the search query uses `ef_search ≥ 40` and iterative scans, which keep
  walking the graph when rows are filtered out.
- **Planner statistics matter right after a bulk load.** Before `ANALYZE`, the planner estimated 1 row for the freshly
  filled table. The `ANALYZE` in the same step fixes it.
- Index size: 12 MB for 3006 vectors (the file does not shrink after vacuum); table 5 MB for 1002 vectors.
- The partial expression index (`vector::vector(768)`, `WHERE dimensions = 768`) is used by the real EF Core query
  (raw SQL composed with LINQ filters): PostgreSQL flattens the subquery. Checked by the integration test
  `Vector_query_can_use_the_hnsw_index`.

## 2. English vectors vs per-locale vectors vs an English-only model

Query set: 12 well-known movies, each described by its plot without title words, in English, Russian and Swedish
(36 queries, e.g. "an orphan boy learns he is a wizard and goes to a school of magic" → Harry Potter and the
Philosopher's Stone). Rank of the expected movie in the first 50 results. Keyword mode on the same queries for reference.
Script: `queries.py` (kept outside the repository; the I08 golden set replaces it).

| Setup | en top-1 / top-3 / MRR@10 | ru top-1 / top-3 / MRR@10 | sv top-1 / top-3 / MRR@10 |
|---|---|---|---|
| **Semantic, EmbeddingGemma, en vectors (default)** | 11/12, 12/12, 0.94 | 9/12, 12/12, 0.86 | 6/12, 8/12, 0.60 |
| Semantic, EmbeddingGemma, en+ru+sv vectors | 12/12, 12/12, 1.00 | 11/12, 12/12, 0.96 | 6/12, 8/12, 0.60 |
| Semantic, nomic-embed-text v1.5 (English-only model), en vectors | 7/12, 9/12, 0.70 | 0/12, 0/12, 0.00 | 1/12, 1/12, 0.08 |
| Keyword (I04), for reference | 0/12, 4/12, 0.19 | 3/12, 4/12, 0.33 | 1/12, 1/12, 0.11 |

Median latency of a semantic search through the API: 75–81 ms (embedding the query + vector query + loading movies).

Findings:

- **The multilingual model is what makes cross-language search work**: with English vectors only, Russian queries find
  the movie in the top 3 in 12 of 12 cases. The English-only nomic model finds none.
- **Per-locale vectors add little** for 3× the vectors and indexing time: Russian moved 2 movies from #2–3 to #1
  (The Godfather, Toy Story), Swedish did not improve (Interstellar #2 → #4, The Truman Show #23 → #40, The Godfather
  #4 → #2). The default stays `Search:Embeddings:Locales = ["en"]`; I08 measures it on the golden set.
- **Swedish is the weak language** (as in the I05 benchmark): 4 of 12 Swedish queries miss the top 3, although 86% of the
  movies have a Swedish overview. Failing queries are pulled towards Swedish-language films: "en kupp inne i drömmar" →
  Let the Right One In, The Seventh Seal; "en man upptäcker att hela hans liv är en dokusåpa" → Persona. A likely cause is
  the `Original title:` line, which puts Swedish words into exactly those documents, so the language of the query
  matches the language of the original title. Candidates for I08: drop or move the original title, compare template
  versions, try a stronger multilingual model.
- **Keyword search fails on plot descriptions** (MRR 0.11–0.33): the words of a description rarely match the overview.
  Semantic search fails on other things (exact titles, names): the reason for hybrid search in I07.

## Model switch (FR-DATA-4)

1. Reindex with profile `nomic` while `local` (EmbeddingGemma) is active: 1002 nomic vectors are added next to the 1002
   EmbeddingGemma vectors; search results stay identical (same similarities).
2. `AI:ActiveEmbeddingProfile = nomic` (temporary user secret), restart: search uses the nomic vectors (table above);
   `DELETE /api/admin/embeddings?model=text-embedding-nomic-embed-text-v1.5` → 400 (active model).
3. Back to `local`: deleting the nomic vectors → 200, 1002 deleted.

## Follow-ups

- I07: hybrid search (RRF) — semantic and keyword fail on different queries.
- I08: golden set with these kinds of queries; compare template versions (original title line), en vs per-locale vectors,
  EmbeddingGemma vs other multilingual models; Swedish first.
- I19: exact vs HNSW plans at larger catalogs, vector storage (`SET STORAGE PLAIN`), `ef_search`.
