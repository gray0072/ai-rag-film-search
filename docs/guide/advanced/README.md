# Advanced chapters

> [Docs](../../README.md) › [Guide](../README.md) › Advanced

Deep dives for readers who finished the core chapters, in particular [5. Embeddings](../05-embeddings.md),
[6. Vector search](../06-vector-search.md), [7. Hybrid search](../07-hybrid-search.md) and
[11. Evaluation](../11-evaluation.md). Each chapter has the same parts: the question it answers for the store, the
theory, what FilmSearch does (with code links), the evidence from committed evaluation reports, commands to run the
experiment yourself, and pitfalls. Where something was never measured in this project, the chapter says so and gives
only the theory.

## Chapters

1. **[Similarity metrics and normalization](similarity-metrics.md).** What cosine, dot product and Euclidean distance
   compute, a worked example where they disagree, and why they rank identically once vectors have length 1. The
   pgvector operators (`<=>`, `<#>`, `<->`) and their operator classes, why a similarity of 0.457 can be the best match,
   and why models are compared by Recall and MRR on a golden set, never by their scores.
2. **[Choosing an embedding model](choosing-an-embedding-model.md).** Languages, retrieval quality on your own data,
   query and document prefixes, context length, dimensions and Matryoshka truncation, local vs hosted. The evidence:
   EmbeddingGemma 300M against an English-only model and against Qwen3-Embedding-8B on the golden set, and the
   switch procedure that changes models without a migration or downtime.
3. **[pgvector indexes](pgvector-indexes.md).** Exact scan, HNSW and IVFFlat with their parameters, how `ef_search`
   caps the rows of a scan, filters and iterative scans, dead tuples and `VACUUM`, and why the planner prefers an exact
   scan at 1000 rows. Includes a SQL script that measures recall against exact search.
4. **[Vector storage](vector-storage.md).** One untyped `vector` column with a partial expression index per dimension
   (ADR-0008) against the alternatives, pgvector types and their limits, `halfvec`, binary quantization, Matryoshka
   truncation, TOAST, storage math for 1000 to 10 million movies, and the content hash that skips unchanged texts.
5. **[Fusion tuning](fusion-tuning.md).** Reciprocal Rank Fusion knob by knob: what k does to the weight of rank 1,
   when a noisy list beats the right answer, per-list weights, candidates per list. The I07 grid and the I08
   one-parameter experiments with per-language numbers, how to run a sweep, and how not to overfit a small golden set.
6. **[LLM as a judge](llm-as-judge.md).** Pointwise, pairwise and reference-based judging, rubrics, biases (position,
   verbosity, self-preference, leniency), variance and cost. How the I14 assistant suites let deterministic checks
   decide and the judge explain, with examples where each caught what the other missed.

## Before you start

The commands need the local setup of the [handbook](../../handbook/local-setup.md): Docker Desktop for the evaluation
runner's PostgreSQL container and LM Studio with the embedding model. SQL examples run in the AppHost database
container `filmsearch-postgres` (database `filmsearch`); the ones that change something are wrapped in
`BEGIN … ROLLBACK`. Some numbers in these chapters were measured on a development database while writing them; they
are marked as such, the rest come from the [evaluation reports](../../../evals/reports/).

---

**Previous:** [14. Resilience and shipping](../14-resilience-and-shipping.md) · **Next:** [Similarity metrics](similarity-metrics.md) ·
**See also:** [Guide contents](../README.md#advanced) · [Evaluations README](../../../evals/README.md) ·
[Journal](../../journal/README.md) · [ADRs](../../adr/README.md)
