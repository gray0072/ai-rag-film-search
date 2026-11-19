# Choosing an embedding model

> [Docs](../../README.md) › [Guide](../README.md) › [Advanced](README.md) › Choosing an embedding model

The embedding model decides more of search quality than the index, the fusion or the database. This chapter lists what
to look at when choosing one (languages, dimensions, prefixes, context length, local vs hosted, cost and latency), shows
how FilmSearch measured three candidates, and how it switches models without a migration or downtime.

## The question

The store's customers write in English, Russian and Swedish; most overviews are English. The developer laptop has an
8 GB GPU; the public demo has no GPU at all. Which model, at what size, running where? And when a better model appears,
how do you prove it is better and move to it without breaking search for a day?

## Theory

### What to look at, in order

| Property | Why it matters | How to check |
|---|---|---|
| **Languages** | A model sees only the languages it was trained on. Cross-language search (Russian query, English text) needs a multilingual model. | The model card, then your own cross-language queries |
| **Retrieval quality on your data** | Public leaderboards measure other texts and other queries. | Your golden set ([ch. 11](../11-evaluation.md)) |
| **Query / document prefixes** | Many models were trained with task instructions; without them quality drops or the score scale shifts. | The model card; a run with and without |
| **Context length** | Text beyond the window is cut off, usually silently. | Token count of your longest document |
| **Dimensions** | Storage, index memory and distance cost grow linearly. HNSW in pgvector indexes `vector` up to 2000 dimensions. | [Vector storage](vector-storage.md) |
| **Latency and availability** | Every new search query is one embedding call. | p50 / p95 per query in the evaluation report |
| **Cost** | Indexing is paid once per text; queries are paid per search. | Price per million tokens × tokens |

### Prefixes are part of the model

Retrieval models are often trained on (query, relevant passage) pairs with an instruction in front, so that a short
question and a long answer land near each other (asymmetric retrieval). The prefix must be the one the model was trained
with, on the right side:

| Model | Query prefix | Document prefix |
|---|---|---|
| EmbeddingGemma 300M | `task: search result \| query: ` | `title: none \| text: ` |
| nomic-embed-text v1.5 | `search_query: ` | `search_document: ` |
| Qwen3-Embedding-8B | `Instruct: Given a movie search query, retrieve movies that match it\nQuery: ` | none |

Qwen3 is "instruction-aware": the query instruction may describe your task in your own words; documents get nothing.

### Dimensions and Matryoshka truncation

More dimensions can hold more nuance, but quality comes from training, not width: a 768-dimension multilingual model
beat a 768-dimension English model by a wide margin in Russian (below). **Matryoshka Representation Learning (MRL)**
trains a model so that the first *k* numbers of a vector are themselves a usable embedding. You can then keep 1024 of
4096 numbers (the full size of Qwen3-Embedding-8B, according to its model card), renormalize, and lose little
quality while storing a quarter. OpenAI-compatible APIs expose this as the `dimensions` request parameter. Truncating a
model that was *not* trained with MRL destroys it.

### Context length

A FilmSearch movie profile has 700 characters on average and 1239 at most (measured on a development database while
writing this chapter), roughly 175–310 English tokens. That fits every model here (EmbeddingGemma's model card gives a
2048-token window). Chunking is needed only for long documents such as reviews or subtitles, which the store does not
embed (`EmbeddingKind.Profile` is the only kind).

### Local or hosted

A local model costs nothing per call, works offline and answers in tens of milliseconds on a GPU, but limits the size
you can run. A hosted model can be much larger, but each new query is a network round trip with a long tail, and an
outage of the provider is an outage of semantic search.

## What FilmSearch does

Embedding models are **profiles** in configuration
([`appsettings.json`](../../../src/FilmSearch.Api/appsettings.json), `AI:EmbeddingProfiles`):

| Profile | Model | Dimensions | Runs where | Used for |
|---|---|---|---|---|
| `local` (default) | `text-embedding-embeddinggemma-300m` | 768 | LM Studio on the laptop | development, evaluations |
| `nomic` | `text-embedding-nomic-embed-text-v1.5` | 768 | LM Studio | the I06 comparison |
| `openrouter` | `qwen/qwen3-embedding-8b` | 1024 (MRL) | OpenRouter | the demo stack ([`deploy/.env.example`](../../../deploy/.env.example), `EMBEDDING_PROFILE`) |

- The profile's `Dimensions` is sent as the `dimensions` parameter
  ([`AiClientFactory`](../../../src/FilmSearch.AI/Providers/AiClientFactory.cs)), which is how Qwen3 returns 1024
  numbers. [`ProfileTextEmbedder`](../../../src/FilmSearch.AI/Embeddings/ProfileTextEmbedder.cs) refuses a vector of
  another length, so a wrong setting fails loudly instead of storing garbage.
- Prefixes live in the profile (`QueryPrefix`, `DocumentPrefix`). The document prefix is part of the embedded text
  and therefore of its content hash: changing it re-embeds every movie on the next reindex.
- Every vector row stores `model` and `dimensions`; search reads only the active model's rows. The query vector cache
  key contains profile, model and dimensions, so a switch never reuses a vector of the old model.
- **There is no embedding fallback** to another provider ([ADR-0011](../../adr/0011-ai-resilience-and-fallback.md)):
  a query vector of model B cannot be compared with stored vectors of model A. When embeddings fail, hybrid search
  returns the keyword list with `degraded: true`.

### Switching models without downtime

1. Add the new profile (and its key in user secrets or the environment).
2. `POST /api/admin/embeddings/reindex` with `{"profile": "<new>"}`: the Worker embeds the catalog with the new
   model **next to** the old vectors. The first time a dimension appears, the indexer creates its partial HNSW index
   ([ADR-0008](../../adr/0008-vector-storage-per-dimension.md)). Search keeps using the old model meanwhile.
3. Evaluate: `--suite search --profile <new>`, compare with the current baseline.
4. Set `AI:ActiveEmbeddingProfile` to the new profile and restart.
5. `DELETE /api/admin/embeddings?model=<old model id>` (refused with 400 while that model is active).

The I06 experiment did exactly this with nomic and back; results stayed identical while both sets were stored
([I06](../../../evals/reports/2026-11-17_I06_semantic-search-experiments.md#model-switch-fr-data-4)).

## Evidence

**I05, small benchmark** (10 queries per language against 10 English overviews,
[report](../../../evals/reports/2026-11-17_I05_model-benchmark.md)):

| Model, prefixes | Top-1 en / ru / sv | MRR en / ru / sv | Cosine: translation vs unrelated (ru) |
|---|---|---|---|
| EmbeddingGemma, with prefixes | 10 / 10 / 8 | 1.00 / 1.00 / 0.86 | 0.84 vs 0.19 |
| EmbeddingGemma, no prefixes | 10 / 10 / 8 | 1.00 / 1.00 / 0.86 | 0.82 vs 0.33 |
| nomic-embed-text v1.5 | 10 / 2 / 4 | 1.00 / 0.34 / 0.51 | 0.49 vs 0.48 |

Prefixes did not change the ranking on this tiny set, but they separated related from unrelated texts more widely
(unrelated cosine 0.19 instead of 0.33). nomic is an English model: a Russian query and its translation are no closer
than unrelated text.

**I06, 36 plot queries on the real catalog**, expected movie in the top 3: EmbeddingGemma 12 / 12 / 8 (en / ru / sv),
nomic 9 / 0 / 1 ([I06](../../../evals/reports/2026-11-17_I06_semantic-search-experiments.md)). An English-only model
is not an option for this store, whatever its English quality.

**Per-locale vectors do not replace a better model.** Embedding the Russian and Swedish texts too (3× vectors) barely
moved Russian and Swedish queries (semantic MRR ru 0.769 → 0.762, sv 0.719 → 0.746) and made cross-language queries
worse (0.806 → 0.680) ([report](../../../evals/reports/2026-11-17_search_vectors-en-ru-sv.md)).

**I20, the same golden set, three setups** ([Qwen3](../../../evals/reports/2026-11-19_search_i20-openrouter-qwen3-embedding-8b.md),
[EmbeddingGemma](../../../evals/reports/2026-11-19_search_i20-ollama-embeddinggemma.md), [baseline](../../../evals/reports/2026-11-17_search_baseline.md)):

| Semantic mode | Recall@10 en / ru / sv | MRR@10 | p50 / p95 per query |
|---|---|---|---|
| EmbeddingGemma 300M, LM Studio (baseline) | 0.914 / 0.857 / 0.824 | 0.794 | 58 / 72 ms |
| EmbeddingGemma 300M, Ollama (same model, another runtime) | 0.914 / 0.857 / 0.824 | 0.797 | 57 / 65 ms |
| Qwen3-Embedding-8B @ 1024, OpenRouter | 0.912 / **0.942** / **0.896** | **0.921** | 577 / 4628 ms |

- The same model in two runtimes gave the same quality (differences of 0.003 are numeric noise of two builds).
- The 8B model closed most of the Russian and Swedish gap, at 10× the median latency and a 4.6 s tail for new
  queries. Indexing the catalog cost less than $0.01 (price in `AI:Pricing`: $0.01 per million tokens). The query
  vector cache (I19) hides the latency for repeated queries.
- Decision trail: the report kept Qwen3 as an option; after Ollama was removed (S23) the demo stack embeds on
  OpenRouter by default, and development stays on the local model
  ([journal I20](../../journal/m5-production-ready.md#i20--containers-and-deployment)).

## How to experiment yourself

Quick check of a candidate in LM Studio (languages, prefixes, latency):

```bash
dotnet run --project tools/ai-playground -- bench-embed --embedding-model <id> --query-prefix "..." --document-prefix "..."
```

Full check on the golden set, with a temporary profile defined on the command line (nothing to edit):

```bash
dotnet run --project evals/FilmSearch.Evals -- --suite search --label trial-<model> --modes semantic,hybrid \
  --set AI:EmbeddingProfiles:trial:Endpoint=http://localhost:1234/v1 --set AI:EmbeddingProfiles:trial:Model=<id> \
  --set AI:EmbeddingProfiles:trial:ApiKey=lm-studio --set AI:EmbeddingProfiles:trial:Dimensions=<n> --profile trial
```

`--set` trims its value, so a prefix that ends with a space (most do) loses it. Put prefixes in the API's user secrets
instead, which the runner reads too:
`dotnet user-secrets set AI:EmbeddingProfiles:trial:QueryPrefix "<prefix> " --project src/FilmSearch.Api`.

Compare the per-locale and per-tag tables with the baseline, not only the summary line. An open follow-up from I20:
Qwen3-Embedding-0.6B locally as a better development model, not measured yet.

## Pitfalls

- **Choosing from a leaderboard.** Measure on your queries, in your languages.
- **Forgetting the prefix, or putting it on the wrong side.** The quality loss can be invisible on a small test.
- **Truncating a non-MRL model** to fit an index. Use `halfvec` instead ([vector storage](vector-storage.md)).
- **Switching the active profile before the reindex finished.** Search then has no vectors of the new model.
- **Judging a hosted model by its median.** The p95 of 4.6 s is what a customer feels on a new query.
- **Reading a model change into an embedded-text effect.** Swedish queries were pulled to Swedish-language films by the
  `Original title` line ([I06](../../../evals/reports/2026-11-17_I06_semantic-search-experiments.md)); a model swap
  would not have explained that.

---

**Previous:** [Similarity metrics](similarity-metrics.md) · **Next:** [pgvector indexes](pgvector-indexes.md) ·
**See also:** [5. Embeddings](../05-embeddings.md) · [ADR-0008](../../adr/0008-vector-storage-per-dimension.md) ·
[Journal: I05](../../journal/m2-smart-search.md#i05--ai-provider-layer) ·
[Journal: I20](../../journal/m5-production-ready.md#i20--containers-and-deployment) · [Evaluations README](../../../evals/README.md)
