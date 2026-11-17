# ADR-0001 — PostgreSQL + pgvector as the only database

- **Status:** Accepted
- **Date:** 2026-11-17
- **Iteration:** I00

## Context
We need relational data (catalog, orders, payments with transactions), full-text search in English and Russian, and vector search. The catalog is small (100 → maybe 10 000 movies).

## Options
1. **PostgreSQL + pgvector**: one DB, transactions across orders and vectors, FTS built in, filters and vectors in one SQL query, good EF Core support (`Pgvector.EntityFrameworkCore`), runs in a container.
2. PostgreSQL + a dedicated vector DB (Qdrant, Weaviate): more features (hybrid built in, quantization), but two stores to sync, more infrastructure.
3. SQL Server 2025 vector type: closer to typical .NET work, but weaker FTS for Russian and fewer learning resources for RAG.

## Decision
Option 1. Hybrid search is implemented ourselves (FTS + vectors + RRF), which is better for learning than getting it ready-made.

## Consequences
- Pre-filtering with vectors in one query is simple.
- Index on a `vector` column needs a fixed dimension → embedding models are tied to a dimension (see spec 03).
- A dedicated vector DB can be tried later as a stretch experiment behind `IMovieSearch`.
