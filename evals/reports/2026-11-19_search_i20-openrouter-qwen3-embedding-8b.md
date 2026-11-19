# Search evaluation: i20-openrouter-qwen3-embedding-8b — 2026-11-19

| | |
|---|---|
| Suite / label | `search` / `i20-openrouter-qwen3-embedding-8b` |
| Started (UTC) | 2026-11-19 12:31:41Z |
| Commit | `9528bc5+dirty` |
| Command | `--suite search --label i20-openrouter-qwen3-embedding-8b --modes semantic,hybrid --profile openrouter` |
| Dataset | `evals/datasets/search.golden.jsonl` version `759f9abeac13`, 105 cases, repeat 1 |
| Database | fresh container pgvector/pgvector:pg17, seed generated 2026-11-17 19:13:07Z |
| Embeddings | profile `openrouter`: qwen/qwen3-embedding-8b (1024 dims, query prefix "Instruct: Given a movie search query, retrieve movies that match it\nQuery: ", document prefix ""); text `movie-profile-v1`; locales en |
| Search parameters | `Semantic:MaxResults=100`, `Hybrid:RrfK=20`, `Hybrid:KeywordWeight=1`, `Hybrid:RelaxedKeywordWeight=0.1`, `Hybrid:SemanticWeight=1`, `Hybrid:CandidatesPerList=50`, `Hybrid:SimilarTitles=AverageVector` |

Metrics are means over cases (spec 09 §3). Recall@k = relevant movies in the top k / min(relevant, k). MRR and nDCG
use the top 10; nDCG uses the grades of the dataset (1–3, gain 2^grade − 1). Excluded-genre rate: share of the top 10
with a genre the query rules out (cases with `excludedGenres` only). Latency: one `IMovieSearch.SearchAsync` call
in process (embedding, SQL, loading movies; query understanding in `hybrid+qu`), without HTTP.

## Summary

| Mode | Recall@5 | Recall@10 | MRR@10 | nDCG@10 | Excluded-genre rate | p50 ms | p95 ms | Degraded | QU fallbacks |
|---|---|---|---|---|---|---|---|---|---|
| semantic | 0.893 | 0.917 | 0.921 | 0.881 | 0.100 | 577 | 4628 | 0 | 0 |
| hybrid | 0.906 | 0.927 | 0.914 | 0.885 | 0.114 | 77 | 114 | 0 | 0 |

## By locale (Recall@10 / MRR@10)

| Group | Cases | semantic | hybrid |
|---|---|---|---|
| en | 35 | 0.912 / 0.936 | 0.929 / 0.919 |
| ru | 35 | 0.942 / 0.934 | 0.942 / 0.934 |
| sv | 35 | 0.896 / 0.893 | 0.911 / 0.888 |

## By tag (Recall@10 / MRR@10)

| Group | Cases | semantic | hybrid |
|---|---|---|---|
| plot | 46 | 1.000 / 0.978 | 1.000 / 0.967 |
| mood | 11 | 0.818 / 0.909 | 0.848 / 0.909 |
| person | 22 | 0.850 / 0.864 | 0.890 / 0.890 |
| title-typo | 15 | 0.886 / 0.880 | 0.886 / 0.880 |
| constraint | 10 | 0.950 / 0.800 | 0.950 / 0.858 |
| negative | 9 | 0.808 / 0.833 | 0.797 / 0.741 |
| cross-language | 15 | 0.946 / 0.967 | 0.946 / 0.967 |

## Misses of every mode (1)

| Case | Query | Expected | Top 3 of hybrid |
|---|---|---|---|
| sv-typo-03 | gudfadren | The Godfather; The Godfather Part II | Ordet; Fanny and Alexander; Tokyo Godfathers |

## Per case

Rank of the first relevant movie in the last run (– = not in the top 10), then Recall@10 of the case.

<details><summary>All cases</summary>

| Case | Tags | semantic | hybrid |
|---|---|---|---|
| en-plot-01 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-02 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-03 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-04 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-05 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-06 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-07 | plot | 1 (1.000) | 2 (1.000) |
| en-plot-08 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-09 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-10 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-11 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-12 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-13 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-14 | plot | 1 (1.000) | 1 (1.000) |
| en-mood-01 | mood | 1 (0.500) | 1 (0.500) |
| en-mood-02 | mood | 1 (0.667) | 1 (1.000) |
| en-mood-03 | mood | 2 (1.000) | 2 (1.000) |
| en-mood-04 | mood | 1 (1.000) | 1 (1.000) |
| en-person-01 | person | 1 (0.700) | 1 (0.700) |
| en-person-02 | person | 1 (0.500) | 1 (0.500) |
| en-person-03 | person | 1 (0.900) | 1 (1.000) |
| en-person-04 | person | 1 (0.750) | 1 (1.000) |
| en-person-05 | person | 1 (0.800) | 1 (0.800) |
| en-typo-01 | title-typo | 1 (1.000) | 1 (1.000) |
| en-typo-02 | title-typo | 1 (1.000) | 1 (1.000) |
| en-typo-03 | title-typo | 1 (1.000) | 1 (1.000) |
| en-typo-04 | title-typo | 1 (1.000) | 1 (1.000) |
| en-typo-05 | title-typo | 1 (1.000) | 1 (1.000) |
| en-constraint-01 | constraint, person | 4 (1.000) | 3 (1.000) |
| en-constraint-02 | constraint, person | 2 (0.833) | 1 (0.833) |
| en-constraint-03 | constraint | 1 (1.000) | 1 (1.000) |
| en-constraint-04 | constraint, person | 1 (1.000) | 1 (1.000) |
| en-negative-01 | negative | 1 (1.000) | 2 (1.000) |
| en-negative-02 | negative | 1 (0.571) | 1 (0.571) |
| en-negative-03 | negative | 2 (0.700) | 3 (0.600) |
| ru-plot-01 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-02 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-03 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-04 | plot | 1 (1.000) | 2 (1.000) |
| ru-plot-05 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-06 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-07 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-08 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-09 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-10 | plot | 2 (1.000) | 1 (1.000) |
| ru-plot-11 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-12 | plot | 1 (1.000) | 1 (1.000) |
| ru-cross-01 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| ru-cross-02 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| ru-cross-03 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| ru-cross-04 | person, cross-language | 1 (0.600) | 1 (0.600) |
| ru-mood-01 | mood | 1 (0.500) | 1 (0.500) |
| ru-mood-02 | mood | 1 (1.000) | 1 (1.000) |
| ru-mood-03 | mood | 1 (1.000) | 1 (1.000) |
| ru-mood-04 | mood | 1 (1.000) | 1 (1.000) |
| ru-person-01 | person, cross-language | 1 (0.700) | 1 (0.700) |
| ru-person-02 | person, cross-language | 1 (1.000) | 1 (1.000) |
| ru-person-03 | person, cross-language | 2 (1.000) | 2 (1.000) |
| ru-person-04 | person, cross-language | 1 (0.889) | 1 (0.889) |
| ru-typo-01 | title-typo | 1 (1.000) | 1 (1.000) |
| ru-typo-02 | title-typo | 1 (1.000) | 1 (1.000) |
| ru-typo-03 | title-typo | 1 (1.000) | 1 (1.000) |
| ru-typo-04 | title-typo | 1 (1.000) | 1 (1.000) |
| ru-typo-05 | title-typo | 5 (0.286) | 5 (0.286) |
| ru-constraint-01 | constraint, person | 1 (1.000) | 1 (1.000) |
| ru-constraint-02 | constraint, person | 1 (1.000) | 1 (1.000) |
| ru-constraint-03 | constraint | 1 (1.000) | 1 (1.000) |
| ru-negative-01 | negative | 1 (1.000) | 1 (1.000) |
| ru-negative-02 | negative | 2 (1.000) | 2 (1.000) |
| ru-negative-03 | negative | 1 (1.000) | 1 (1.000) |
| sv-plot-01 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-02 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-03 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-04 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-05 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-06 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-07 | plot | 2 (1.000) | 2 (1.000) |
| sv-plot-08 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-09 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-10 | plot | 1 (1.000) | 1 (1.000) |
| sv-cross-01 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-02 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-03 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-04 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-05 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-06 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-07 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-mood-01 | mood | 1 (0.667) | 1 (0.667) |
| sv-mood-02 | mood | 2 (0.667) | 2 (0.667) |
| sv-mood-03 | mood | 1 (1.000) | 1 (1.000) |
| sv-person-01 | person | 1 (0.800) | 1 (0.900) |
| sv-person-02 | person | 2 (1.000) | 2 (1.000) |
| sv-person-03 | person | 1 (0.667) | 1 (1.000) |
| sv-person-04 | person | 1 (0.900) | 1 (1.000) |
| sv-typo-01 | title-typo | 1 (1.000) | 1 (1.000) |
| sv-typo-02 | title-typo | 1 (1.000) | 1 (1.000) |
| sv-typo-03 | title-typo | – (0.000) | – (0.000) |
| sv-typo-04 | title-typo | 1 (1.000) | 1 (1.000) |
| sv-typo-05 | title-typo | 1 (1.000) | 1 (1.000) |
| sv-constraint-01 | constraint, person | 4 (0.667) | 4 (0.667) |
| sv-constraint-02 | constraint, person | 1 (1.000) | 1 (1.000) |
| sv-constraint-03 | constraint, person | 1 (1.000) | 1 (1.000) |
| sv-negative-01 | negative | 2 (0.500) | 3 (0.500) |
| sv-negative-02 | negative | 1 (0.500) | 1 (0.500) |
| sv-negative-03 | negative | 1 (1.000) | 1 (1.000) |

</details>

## Conclusion

I20: the optional OpenRouter embedding profile of the demo, Qwen3-Embedding-8B at 1024 dimensions (MRL truncation
through the `dimensions` parameter) with its instruction prefix on queries only. Against EmbeddingGemma (Ollama run of
the same day, same dataset): semantic Recall@10 0.917 vs 0.865, MRR@10 0.921 vs 0.797, nDCG@10 0.881 vs 0.787,
excluded-genre rate 0.100 vs 0.157; hybrid Recall@10 0.927 vs 0.873. The gain is largest for Russian (semantic
Recall@10 0.942 vs 0.857) and Swedish (0.896 vs 0.824): an 8B multilingual model against a 300M one.

The price is latency and a dependency: semantic p50 577 ms and p95 4.6 s (a network call per new query; the hybrid
pass reused the cached query vectors of the semantic pass, so its 77 ms is not a fair number), and search degrades to
keyword when OpenRouter is down. Indexing the catalog cost less than $0.01. Decision: kept as an option of the demo
(`EMBEDDING_PROFILE=openrouter`), not the default (the owner chose local embeddings by default). Next: try Qwen3
embeddings locally (Qwen3-Embedding-0.6B in Ollama) and as the dev model; tune `Hybrid:RrfK` for it.
