# Similarity metrics and normalization

> [Docs](../../README.md) › [Guide](../README.md) › [Advanced](README.md) › Similarity metrics

[Chapter 5](../05-embeddings.md) said "closeness is measured by cosine similarity" and "rank, don't threshold". This
chapter explains why: what cosine, dot product and Euclidean distance compute, when they give the same order, which
pgvector operator belongs to which index, and why a similarity score of one model says nothing about another model.

## The question

For "a heist inside dreams" FilmSearch shows Inception first with similarity **0.457**
([I06 experiments](../../../evals/reports/2026-11-17_I06_semantic-search-experiments.md)). A developer new to vectors
then asks: is 0.457 good? Could the store hide results below 0.5? Should it use cosine, dot product or L2? And is a
model that returns 0.8 for the same pair better? Each question has a short answer, and each wrong answer leads to a
real bug: empty result pages, an index that is silently not used, or the wrong model chosen.

## Theory

### Three ways to compare two vectors

For a query vector *q* and a document vector *d* with *n* numbers each:

| Measure | Formula | Range | Larger means |
|---|---|---|---|
| Dot product (inner product) | q·d = Σ qᵢdᵢ | any number | more similar, **and** longer vectors |
| Cosine similarity | q·d / (‖q‖ ‖d‖) | −1 … 1 | same direction; length ignored |
| Cosine distance | 1 − cosine similarity | 0 … 2 | less similar |
| Euclidean (L2) distance | √Σ (qᵢ − dᵢ)² | 0 … ∞ | less similar; length counts |

‖q‖ is the length (L2 norm) of a vector: √Σ qᵢ². The dot product mixes two things, direction and length. Cosine keeps
only the direction. L2 distance measures how far apart the two points are, so a long and a short vector pointing the
same way are far apart.

### A worked example in two dimensions

Query q = (0.6, 0.8), length 1. Three documents:

| Document | Vector | Length | Cosine similarity | Dot product | L2 distance |
|---|---|---|---|---|---|
| a | (0.8, 0.6) | 1.0 | 0.96 | 0.96 | 0.28 |
| b | (1.5, 2.0) | 2.5 | **1.00** | **2.50** | 1.50 |
| c | (0.3, 0.4) | 0.5 | **1.00** | 0.50 | 0.50 |

The three measures give three different orders: cosine says b = c > a, dot product says b > a > c, L2 says
a > c > b. b and c point exactly where the query points; they differ from q only in length.

Now **normalize** every vector (divide it by its length, so every length is 1): b and c both become (0.6, 0.8), a stays
(0.8, 0.6). Cosine: b = c = 1.00, a = 0.96. Dot product: the same numbers. L2: b = c = 0, a = 0.28. One order for all
three. This is not a coincidence:

```text
For unit vectors:   q·d = cos(q, d)          and          ‖q − d‖² = ‖q‖² + ‖d‖² − 2 q·d = 2 − 2 cos(q, d)
```

L2 distance is a decreasing function of cosine, and the dot product *is* the cosine. **When every vector has length 1,
the three measures rank identically**; they differ only in the numbers they print.

### When the choice matters

It matters only when lengths vary and the length means something. Some models are trained so that length carries
information (for example how "typical" a text is), and their documentation then names the metric to use. Most
sentence-embedding models used for search are trained with cosine, and many providers return unit-length vectors
already. The rule: **use the metric the model card names**; if it names cosine, any of the three is fine on normalized
vectors, and cosine is the safe default when you do not know whether they are normalized.

### pgvector operators and operator classes

| Operator | Computes | Operator class for an index | Note |
|---|---|---|---|
| `<->` | L2 distance | `vector_l2_ops` | |
| `<#>` | **negative** inner product | `vector_ip_ops` | negative because PostgreSQL index scans return the smallest values first |
| `<=>` | cosine distance (1 − cosine) | `vector_cosine_ops` | the one FilmSearch uses |
| `<+>` | L1 (taxicab) distance | `vector_l1_ops` | pgvector 0.7 and later |

`halfvec` has the same operators with `halfvec_*_ops`; `bit` vectors use `<~>` (Hamming) with `bit_hamming_ops`
([vector storage](vector-storage.md)). An index serves **one** operator: an index built with `vector_cosine_ops` is
used by `ORDER BY v <=> @q`, but not by `ORDER BY v <-> @q`. The second query still returns correct rows, through an
exact scan, which you notice only when the catalog is large. pgvector's documentation suggests inner product for
unit-length vectors as the cheapest computation (no norms to compute); FilmSearch did not measure the difference, which
is tiny next to the 20–30 ms of embedding a query.

### Scores are relative

A cosine similarity is not a probability and not a percentage. Three effects move it:

1. **The model.** Embeddings are not spread evenly over all directions, so even unrelated texts have a positive
   cosine, and how positive depends on the model.
2. **The prefixes.** The same model with and without its task prefixes gives different baselines.
3. **The kind of comparison.** A four-word query against a 150-word movie profile (asymmetric) scores lower than two
   movie profiles against each other (symmetric).

## What FilmSearch does

- The vector query orders by `<=>` and the HNSW index is built with `vector_cosine_ops`
  ([`VectorIndex`](../../../src/FilmSearch.Infrastructure/Search/Semantic/VectorIndex.cs)). Similarity is
  `1 − distance`, rounded to six digits in
  [`SemanticRetriever`](../../../src/FilmSearch.Infrastructure/Search/Semantic/SemanticRetriever.cs), and shown in the
  debug output of `POST /api/search` (`"debug": true`).
- **No score threshold anywhere.** The semantic list is cut by count (`Search:Semantic:MaxResults` = 100), and hybrid
  search fuses ranks, not scores ([fusion tuning](fusion-tuning.md)).
- Normalization is explicit where it matters: "movies like X" averages the query vector with the named movies' vectors,
  and [`QueryVectors.Blend`](../../../src/FilmSearch.Infrastructure/Search/Semantic/QueryVectors.cs) normalizes each
  part first. Cosine ignores length, but an average does not: a longer vector would dominate the blend.
- EmbeddingGemma's vectors are already unit length. Checked on a development database while writing this chapter
  (1231 vectors, `vector_norm`): minimum and maximum norm 1.0000. For one movie's vector against its nearest
  neighbours, `<=>` gave 0.3868, `<#>` −0.6132 and `<->` 0.8795, which is exactly √(2 × 0.3868): the identity above,
  on real data.

## Evidence

Similarity numbers from the reports, all for the same kind of question ("does this text match?"):

| Model, setting | Pair | Cosine similarity | Source |
|---|---|---|---|
| EmbeddingGemma, with prefixes | Russian query vs its English translation | 0.84 | [I05](../../../evals/reports/2026-11-17_I05_model-benchmark.md) |
| EmbeddingGemma, with prefixes | query vs unrelated text | 0.19 | I05 |
| EmbeddingGemma, **no** prefixes | query vs unrelated text | 0.33 | I05 |
| nomic-embed-text v1.5 | Russian query vs its English translation | 0.49 | I05 |
| nomic-embed-text v1.5 | query vs unrelated text | 0.48 | I05 |
| EmbeddingGemma | "a heist inside dreams" vs Inception (correct, #1) | 0.457 | [I06](../../../evals/reports/2026-11-17_I06_semantic-search-experiments.md) |
| EmbeddingGemma | The Godfather vs The Godfather Part II (movie vs movie) | 0.775 | I06 |
| EmbeddingGemma | best match for "90s space movies rated ≥ 7" (the catalog has none) | ≈ 0.2 | [I07](../../../evals/reports/2026-11-17_I07_hybrid-and-query-understanding.md) |

Read across the rows: a correct top result scores 0.457 while an unrelated text without prefixes scores 0.33; a
"similar movie" scores 0.775; for nomic, a translation (0.49) and unrelated text (0.48) are indistinguishable. A fixed
cut-off at 0.5 would have hidden Inception, and the same cut-off would mean something else after a prefix change.

**How the project compared models instead:** with retrieval metrics on the 105-query golden set. Qwen3-Embedding-8B
against EmbeddingGemma, semantic mode
([I20 report](../../../evals/reports/2026-11-19_search_i20-openrouter-qwen3-embedding-8b.md),
[EmbeddingGemma run](../../../evals/reports/2026-11-19_search_i20-ollama-embeddinggemma.md)):

| Semantic mode | Recall@10 | MRR@10 | nDCG@10 | Recall@10 ru | Recall@10 sv |
|---|---|---|---|---|---|
| EmbeddingGemma 300M (768) | 0.865 | 0.797 | 0.787 | 0.857 | 0.824 |
| Qwen3-Embedding-8B (1024) | **0.917** | **0.921** | **0.881** | **0.942** | **0.896** |

Neither report compares raw similarities, and they should not: the numbers live on different scales. Ranks of known
relevant movies are comparable across models; cosines are not.

## How to experiment yourself

Check whether stored vectors are normalized (psql in the AppHost container `filmsearch-postgres`, database
`filmsearch`; the password is in the AppHost user secrets):

```sql
SELECT model, min(vector_norm(vector)), max(vector_norm(vector)) FROM search.movie_embeddings GROUP BY model;
```

Compare the three operators for one movie's neighbours (a norm of 1 makes the orders identical):

```sql
WITH q AS (SELECT vector::vector(768) AS v FROM search.movie_embeddings WHERE dimensions = 768 LIMIT 1)
SELECT e.movie_id, e.vector::vector(768) <=> q.v AS cosine, e.vector::vector(768) <#> q.v AS neg_dot,
       e.vector::vector(768) <-> q.v AS l2
FROM search.movie_embeddings e, q WHERE e.dimensions = 768 ORDER BY cosine LIMIT 5;
```

See how prefixes move the baseline of a model (translation pairs and unrelated texts, I05's method):

```bash
dotnet run --project tools/ai-playground -- bench-embed --embedding-model text-embedding-embeddinggemma-300m \
  --query-prefix "task: search result | query: " --document-prefix "title: none | text: "
dotnet run --project tools/ai-playground -- bench-embed --embedding-model text-embedding-embeddinggemma-300m
```

Compare two models the way the project does: one search evaluation per embedding profile, then compare the reports
([choosing an embedding model](choosing-an-embedding-model.md#how-to-experiment-yourself)).

## Pitfalls

- **A similarity threshold.** It hides good results of one model and lets noise through for another, and it changes
  when a prefix changes. Cut by count, fuse by rank.
- **Comparing models by their scores.** A higher average cosine is not better retrieval. Compare Recall, MRR and nDCG
  on the same golden set.
- **Operator and index out of step.** `ORDER BY <->` against a `vector_cosine_ops` index runs an exact scan, silently.
  Check with `EXPLAIN` ([pgvector indexes](pgvector-indexes.md)).
- **Averaging vectors without normalizing them first.** The longest vector wins the average.
- **Mixing query–document and document–document scores.** "Similar movies" scores are naturally higher; do not
  compare them with search scores.

---

**Previous:** [Advanced chapters](README.md) · **Next:** [Choosing an embedding model](choosing-an-embedding-model.md) ·
**See also:** [5. Embeddings](../05-embeddings.md) · [6. Vector search](../06-vector-search.md) ·
[I05 benchmark](../../../evals/reports/2026-11-17_I05_model-benchmark.md) ·
[Journal: I06](../../journal/m2-smart-search.md#i06--embeddings-and-semantic-search)
