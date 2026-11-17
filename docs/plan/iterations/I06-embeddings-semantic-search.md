# I06 — Embeddings and semantic search

| | |
|---|---|
| **Status** | done |
| **Depends on** | I03, I05 |
| **Parallel with** | I04, I09 |
| **Branch** | `iter/I06-embeddings` |
| **Spec** | [05 Search §3](../../spec/05-search.md#3-semantic-search), [08 Ingestion §4](../../spec/08-data-ingestion.md#4-embeddings-pipeline) |
| **Requirements** | FR-SRCH-2, FR-DATA-3, FR-DATA-4 |

## Goal
Every movie has an embedding. `POST /api/search` with `mode=semantic` finds movies by meaning, including Russian and Swedish queries against English texts. `GET /api/movies/{id}/similar` works.

## You will learn
Embeddings in practice: what text to embed, query vs document prefixes, cosine similarity, HNSW index, reindexing, model/dimension management.

## Tasks
- [x] Decide vector storage for dimensions (fixed `vector(N)` vs table per dimension) → ADR-0008 (ADR-0007 was taken by the frontend stack).
- [x] `MovieEmbedding` entity, migration, HNSW index (`vector_cosine_ops`).
- [x] `MovieEmbeddingTextBuilder` (versioned template, unit tests with snapshots) — named `MovieEmbeddingText`.
- [x] Embedding indexer: find stale rows by `ContentHash`, batch, call `IEmbeddingGenerator`, upsert. Admin endpoint `POST /api/admin/embeddings/reindex` + run after seed in Development.
- [x] Semantic search implementation in `Search/Semantic` with pre-filtering (shared filter builder from I04).
- [x] Similar movies endpoint.
- [x] Experiment: exact scan vs HNSW results on the seed (same top-10?). Experiment: en-only vectors vs per-locale (en+ru+sv) vectors for Russian and Swedish queries (quick manual check now, proper numbers in I08).
- [x] Integration tests with a **fake deterministic embedding generator** (e.g. hashed bag of words) so tests do not need LM Studio.

## Acceptance criteria
- [x] "a heist inside dreams" → Inception in top 3; "фильм про мафиозную семью" → The Godfather in top 3 (manual check with the real model, written in Result).
- [x] Changing a movie overview and running reindex re-embeds only that movie.
- [x] Switching to a second embedding model and reindexing works without deleting the old vectors.

## Result

### What was done
- **Storage (ADR-0008):** `search.movie_embeddings` (`MovieEmbedding`: model, dimensions, kind, locale, exact text, content hash, vector, created at; unique per movie / model / kind / locale; cascade on movie delete). The `vector` column has no fixed dimension; the HNSW index (`vector_cosine_ops`, m 16, ef_construction 64) is a partial expression index per dimension, `((vector::vector(768))) WHERE dimensions = 768`. Migration `I06_AddMovieEmbeddings` creates it for 768; the indexer creates it for other dimensions on first use. `UseVector()` on the Npgsql options.
- **Embedding text** (`MovieEmbeddingText`, Core, version `movie-profile-v1`): title (year), original title if different, genres, director, top 5 cast, 20 keywords, tagline, overview; English labels, localized values, empty fields left out. `Hash` = sha256 of the version and the exact text sent (document prefix included).
- **`ITextEmbedder`** (Core contract, implemented in `FilmSearch.AI` over the keyed embedding generators): `EmbeddingModel` (profile, model, dimensions, prefixes, `FormatQuery` / `FormatDocument`), dimension check, provider failures → `AiProviderUnavailableException`. Infrastructure still references only Core (spec 02 §4).
- **Indexer** (`IMovieEmbeddingIndexer`): walks catalog ids in batches of 32 (`ICatalogReader.GetMovieIdsAsync` / `GetMovieDetailsAsync`, both new), per locale, embeds only texts whose hash changed (or all with `onlyStale: false`), saves each batch, deletes vectors of locales no longer indexed, then `VACUUM (ANALYZE)`. Span `embeddings.reindex` and a log line with texts/s. `GetStatsAsync`, `DeleteModelAsync` (refuses the active model).
- **Background jobs** (`IEmbeddingJobs`, `EmbeddingJobRunner`): one job at a time in the API, state in memory (queued / running / succeeded / failed / cancelled, progress, result, error). Development queues a stale-only job on startup after the seed (`Search:Embeddings:IndexOnStartup`).
- **Semantic retrieval** (`SemanticRetriever`): query embedded with the active profile (query prefix); raw SQL from `VectorIndex.DistanceSql` composed with the I04 `ApplyFilter` (pre-filtering) and `ORDER BY distance LIMIT n`; `hnsw.ef_search = max(40, n)` and `hnsw.iterative_scan = relaxed_order` per transaction (`set_config(..., true)`); best vector per movie when there are several locales; list capped at `Search:Semantic:MaxResults` (100) and paged in memory. Similar movies use the movie's own vector (English first).
- **API:** `POST /api/search` accepts `mode: semantic` (score = similarity, debug `vectorRank` / `similarity`, 503 when the provider fails); `GET /api/movies/{id}/similar?limit=` (1–50, 404 for an unknown movie, empty without a vector); admin `GET /api/admin/embeddings`, `POST /api/admin/embeddings/reindex` (202 / 409 / 400), `DELETE /api/admin/embeddings?model=`. Config sections `Search:Semantic`, `Search:Embeddings`; second embedding profile `nomic` in `appsettings.json`. OpenAPI document and web types regenerated.
- **Tests:** unit 115 (+21: embedding text snapshot and rules, hash, `ITextEmbedder` over the fake provider: prefixes, profile lookup, dimension mismatch, 500 and refused connection with retries → `AiProviderUnavailableException`; `MovieFilter.HasConditions`); integration 74 (+15: semantic search on the seed with the fake bag-of-words model — ranking, debug scores, paging and cap, pre-filtering returns exactly the filtered set, filters with ranking, similar movies, limit validation, 404, the HNSW plan of the real query; on own databases — only changed movies are re-embedded, locales added and removed, a 384-dimension second model side by side with its own index, switching the active profile, deleting old / active model, the background job through the API, request validation, 503 with a failing provider). All pass.
- **Experiments:** [evals/reports/2026-11-17_I06_semantic-search-experiments.md](../../../evals/reports/2026-11-17_I06_semantic-search-experiments.md).

### Manual checks (real model, AppHost, LM Studio with EmbeddingGemma 300M)
- Startup job: 1002 movies embedded in 26.9 s (≈ 37 texts/s); next start: 0 embedded, 1002 unchanged in 1.9 s.
- "a heist inside dreams" → **Inception #1**; "фильм про мафиозную семью" → GoodFellas #1, **The Godfather #2**. Similar to Inception: Tenet, Batman Begins, The Dark Knight Rises…; to The Godfather: Part II, Part III, GoodFellas.
- 36 plot queries (12 movies × en / ru / sv), expected movie in the top 3: en 12/12, ru 12/12, sv 8/12 (keyword mode: 4, 4, 1). Per-locale vectors: ru top-1 9 → 11 of 12, sv unchanged. English-only nomic-embed-text: ru 0/12.
- HNSW vs exact on 1002 doc-to-doc queries: recall@10 1.000 at `ef_search` 40 (after vacuum), 0.22 vs 3.5 ms per query; the planner still chooses the exact scan at this size.
- Model switch: reindex with profile `nomic` added 1002 vectors next to EmbeddingGemma's, results unchanged; with `AI:ActiveEmbeddingProfile = nomic` (temporary user secret, removed afterwards) search used nomic, deleting the active model → 400; back on `local`, deleting nomic → 1002 deleted. The dev DB holds only EmbeddingGemma `en` vectors again.
- LM Studio stopped: semantic search → 503 after the profile's retries (≈ 15 s), keyword search 200, admin chat 503, startup job succeeds (nothing stale, no call).
- `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, `dotnet test`; web `gen:api`, lint, format check, typecheck, tests, build.

### Decisions and deviations
- **ADR-0008 instead of ADR-0007** (number taken). Option chosen: one table, untyped `vector`, partial HNSW index per dimension.
- **`ITextEmbedder` in Core** instead of using `IEmbeddingGenerator` in Infrastructure: keeps `Infrastructure → Core` only, and puts profile knowledge (prefixes, dimension) and provider-error mapping in the AI project.
- **Reindex request takes `profile`, not `model`** (spec 04 said `model?`): the profile brings endpoint, dimension and prefixes. Added `locales`. Spec 04 updated, plus the new `GET` / `DELETE` admin endpoints.
- **Semantic `total`** is the length of the capped list (≤ 100): every movie has some similarity, so "number of matches" has no meaning for vectors.
- **Per-locale vectors** are supported but off by default (`Search:Embeddings:Locales = ["en"]`), per the experiment.
- **Catalog changes do not trigger re-embedding by themselves** (an LLM call inside the catalog transaction would be wrong, and a post-commit hook needs the Worker): the next reindex finds them by hash. I16 schedules it.
- **`VACUUM (ANALYZE)` after reindex** — found in the experiments: missing statistics made the planner estimate 1 row, and dead graph entries returned 4.8 of 10 rows at low `ef_search`.
- **Bug fixed in I05 code:** the OpenAI client throws `AggregateException` ("Retry failed after N tries") when every retry fails, so `POST /api/admin/ai/chat` answered 500 instead of 503 with LM Studio off. Shared `AiProviderErrors.IsProviderFailure` now handles it for chat and embeddings.
- **Flaky I05 test fixed after the first CI run:** `Chat_call_produces_a_GenAI_span_…` collected spans of the whole process, so the new `ProfileTextEmbedderTests` (running in parallel, same `FilmSearch.AI` source) added embedding spans. The test now keeps only spans of its own trace; `MeasurementRecorder` returns snapshots under its lock.
- **Package:** `Pgvector.EntityFrameworkCore` 0.3.0 (named in spec 02; brings `Pgvector` 0.3.2): `vector` column type and Npgsql mapping of `Vector` parameters. Requires Npgsql EF ≥ 9.0.1; works with the pinned 10.0.3.

### Follow-ups
- I07: hybrid RRF over the keyword and semantic lists (semantic list = top 100 by default); keyword and semantic fail on different queries (report).
- I08: golden set with plot descriptions in en / ru / sv; compare the `Original title` line (Swedish queries are pulled to Swedish-language films), per-locale vectors, other multilingual models.
- I16: the Worker schedules a stale-only reindex after TMDB sync; move `EmbeddingJobRunner` there; an advisory lock if several processes may reindex.
- I19: exact vs HNSW at larger catalogs (`SET STORAGE PLAIN` for the vector column, planner costs); semantic search with LM Studio down waits for all retries (≈ 15 s) — a shorter timeout or a circuit breaker for query embeddings.
- I20: the indexer creates HNSW indexes at runtime: the app's database user must own the table.

### Sessions
- [S07](../../prompts/sessions/2026-11-17_S07_embeddings-semantic-search.md)
