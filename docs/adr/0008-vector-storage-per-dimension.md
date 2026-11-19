# ADR-0008 — Vector storage: one table, untyped vector column, partial HNSW index per dimension

> [Docs](../README.md) › [ADRs](README.md) › ADR-0008

- **Status:** Accepted
- **Date:** 2026-11-17
- **Iteration:** I06

## Context
Movie embeddings are stored in PostgreSQL with pgvector (ADR-0001). A vector belongs to a model; models have different
dimensions (EmbeddingGemma 768, bge-m3 or Qwen3-Embedding 1024, …). FR-DATA-4 requires that a new model can be indexed
while the old model's vectors stay in use, and the project wants to compare models (I08). pgvector can only build an
HNSW index on a column with a fixed dimension (`vector(768)`), and HNSW supports at most 2000 dimensions for `vector`.

## Options
1. **Fixed `vector(N)` column** (e.g. `vector(768)`), only models of that size. Simple: EF Core LINQ with
   `CosineDistance` works, one ordinary index. But every model to compare must have N dimensions (or be truncated with
   Matryoshka models); changing N means a migration and a full rewrite.
2. **One table (or column) per dimension** (`movie_embeddings_768`, `movie_embeddings_1024`). Fixed types, ordinary
   indexes. But a new dimension needs a migration and code that picks the table; queries and the indexer are duplicated
   or generated.
3. **One table with an untyped `vector` column, a `dimensions` column and a partial expression HNSW index per
   dimension**: `CREATE INDEX … USING hnsw ((vector::vector(768)) vector_cosine_ops) WHERE dimensions = 768`
   (documented by pgvector for mixed dimensions). Any model fits; vectors of several models live side by side. But the
   query must repeat the index expression and predicate, so the vector part is raw SQL; the index cannot be modeled in
   EF Core.

## Decision
Option 3. Table `search.movie_embeddings` (`model`, `dimensions`, `kind`, `locale`, `text`, `content_hash`, `vector`),
unique per (movie, model, kind, locale).

- The migration creates the index for 768 dimensions (the default model). The embedding indexer runs
  `CREATE INDEX IF NOT EXISTS` for the dimension of the model it indexes, so trying a 1024-dimension model needs no
  migration. Models with more than 2000 dimensions are stored and searched by exact scan.
- The vector query (`VectorIndex.DistanceSql`) is raw SQL with the dimension inlined (an integer from configuration) and
  the query vector and model as parameters; EF Core composes catalog filters and `ORDER BY distance LIMIT n` around it.
  PostgreSQL flattens the subquery, so the index can be used (checked by an integration test against the query plan).
- Search uses the active embedding profile's model; switching models is: reindex with the new profile, switch
  `AI:ActiveEmbeddingProfile`, delete the old model's vectors.

## Consequences
- Easier: comparing and switching models of any size, side by side, without migrations.
- Harder: the vector query is raw SQL (kept in one place, `VectorIndex`), and the HNSW indexes are created outside
  EF Core migrations (the model snapshot does not know them). Vectors of all models of one dimension share one index; a
  search filters by model while it walks the graph (iterative scans, pgvector 0.8).
- Watch: index DDL at runtime needs the database user to own the table (true for the app user today; revisit with a
  separate migration user in I20). Large reindexes leave dead index entries: the indexer vacuums after changes.
- Reverse: if only one model is kept long term, a migration can change the column to `vector(N)` with an ordinary
  index and drop the expression indexes; the query then becomes plain LINQ.

---

**Previous:** [ADR-0007](0007-frontend-stack.md) · **Next:** [ADR-0009](0009-job-queue-in-postgresql.md) · **See also:** [All ADRs](README.md) · [Docs hub](../README.md)
