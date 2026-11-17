# I06 — Embeddings and semantic search

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I03, I05 |
| **Parallel with** | I04, I09 |
| **Branch** | `iter/I06-embeddings` |
| **Spec** | [05 Search §3](../../spec/05-search.md#3-semantic-search), [08 Ingestion §4](../../spec/08-data-ingestion.md#4-embeddings-pipeline) |
| **Requirements** | FR-SRCH-2, FR-DATA-3, FR-DATA-4 |

## Goal
Every movie has an embedding. `POST /api/search` with `mode=semantic` finds movies by meaning, including Russian queries against English texts. `GET /api/movies/{id}/similar` works.

## You will learn
Embeddings in practice: what text to embed, query vs document prefixes, cosine similarity, HNSW index, reindexing, model/dimension management.

## Tasks
- [ ] Decide vector storage for dimensions (fixed `vector(N)` vs table per dimension) → ADR-0007.
- [ ] `MovieEmbedding` entity, migration, HNSW index (`vector_cosine_ops`).
- [ ] `MovieEmbeddingTextBuilder` (versioned template, unit tests with snapshots).
- [ ] Embedding indexer: find stale rows by `ContentHash`, batch, call `IEmbeddingGenerator`, upsert. Admin endpoint `POST /api/admin/embeddings/reindex` + run after seed in Development.
- [ ] Semantic search implementation in `Search/Semantic` with pre-filtering (shared filter builder from I04, or a temporary copy if I04 is not merged yet; reconcile on merge).
- [ ] Similar movies endpoint.
- [ ] Experiment: exact scan vs HNSW results on the seed (same top-10?). Experiment: en-only vectors vs en+ru vectors for Russian queries (quick manual check now, proper numbers in I08).
- [ ] Integration tests with a **fake deterministic embedding generator** (e.g. hashed bag of words) so tests do not need LM Studio.

## Acceptance criteria
- [ ] "a heist inside dreams" → Inception in top 3; "фильм про мафиозную семью" → The Godfather in top 3 (manual check with the real model, written in Result).
- [ ] Changing a movie overview and running reindex re-embeds only that movie.
- [ ] Switching to a second embedding model and reindexing works without deleting the old vectors.
