# Session S07 — 2026-11-17 — Embeddings and semantic search

- **Iteration:** I06 (Embeddings and semantic search)
- **Agent:** Claude Code (VS Code)
- **Outcome:** I06 done: movie embeddings in pgvector (one table, partial HNSW index per dimension, ADR-0008), versioned embedding text, hash-based reindex as a background job with admin endpoints, semantic search with pre-filtering, similar movies. EmbeddingGemma finds plot queries in en 12/12, ru 12/12, sv 8/12 (top 3); HNSW recall@10 = 1.0 on the seed. Fixed 500 → 503 when LM Studio is down (AggregateException from SDK retries).

---

## Prompt 1

English:

```text
Let's do I06 and push.
```

Original (ru):

```text
делаем I06 и пуш.
```
