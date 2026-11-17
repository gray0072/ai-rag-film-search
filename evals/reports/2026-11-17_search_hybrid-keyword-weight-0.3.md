# Search evaluation: hybrid-keyword-weight-0.3 — 2026-11-17

| | |
|---|---|
| Suite / label | `search` / `hybrid-keyword-weight-0.3` |
| Started (UTC) | 2026-11-17 22:59:23Z |
| Commit | `836f94e+dirty` |
| Command | `--suite search --label hybrid-keyword-weight-0.3 --modes hybrid --set Search:Hybrid:KeywordWeight=0.3` |
| Dataset | `evals/datasets/search.golden.jsonl` version `759f9abeac13`, 105 cases, repeat 1 |
| Database | fresh container pgvector/pgvector:pg17, seed generated 2026-11-17 19:13:07Z |
| Embeddings | profile `local`: text-embedding-embeddinggemma-300m (768 dims, query prefix "task: search result \| query: ", document prefix "title: none \| text: "); text `movie-profile-v1`; locales en |
| Search parameters | `Semantic:MaxResults=100`, `Hybrid:RrfK=20`, `Hybrid:KeywordWeight=0.3`, `Hybrid:RelaxedKeywordWeight=0.1`, `Hybrid:SemanticWeight=1`, `Hybrid:CandidatesPerList=50`, `Hybrid:SimilarTitles=AverageVector` |
| Overrides | `Search:Hybrid:KeywordWeight=0.3` |

Metrics are means over cases (spec 09 §3). Recall@k = relevant movies in the top k / min(relevant, k). MRR and nDCG
use the top 10; nDCG uses the grades of the dataset (1–3, gain 2^grade − 1). Excluded-genre rate: share of the top 10
with a genre the query rules out (cases with `excludedGenres` only). Latency: one `IMovieSearch.SearchAsync` call
in process (embedding, SQL, loading movies; query understanding in `hybrid+qu`), without HTTP.

## Summary

| Mode | Recall@5 | Recall@10 | MRR@10 | nDCG@10 | Excluded-genre rate | p50 ms | p95 ms | Degraded | QU fallbacks |
|---|---|---|---|---|---|---|---|---|---|
| hybrid | 0.837 | 0.873 | 0.797 | 0.789 | 0.171 | 81 | 120 | 0 | 0 |

## By locale (Recall@10 / MRR@10)

| Group | Cases | hybrid |
|---|---|---|
| en | 35 | 0.914 / 0.837 |
| ru | 35 | 0.871 / 0.833 |
| sv | 35 | 0.834 / 0.721 |

## By tag (Recall@10 / MRR@10)

| Group | Cases | hybrid |
|---|---|---|
| plot | 46 | 0.966 / 0.817 |
| mood | 11 | 0.803 / 0.879 |
| person | 22 | 0.917 / 0.854 |
| title-typo | 15 | 0.733 / 0.667 |
| constraint | 10 | 0.950 / 0.794 |
| negative | 9 | 0.583 / 0.630 |
| cross-language | 15 | 0.924 / 0.794 |

## Misses of every mode (6)

| Case | Query | Expected | Top 3 of hybrid |
|---|---|---|---|
| en-typo-03 | godfahter | The Godfather; The Godfather Part II | Thor: Love and Thunder; New Gods: Nezha Reborn; Faust |
| ru-typo-05 | гари потер | Harry Potter and the Philosopher's Stone; Harry Potter and the Chamber of Secrets; Harry Potter and the Prisoner of Azkaban; … | Warrior; Short Term 12; Hotarubi no Mori e |
| sv-plot-02 | en man upptäcker att hela hans liv är en dokusåpa | The Truman Show | Persona; Ikiru; It's Such a Beautiful Day |
| sv-typo-01 | nykeln till frihet | The Shawshank Redemption | Let Go; The Virgin Spring; The Seventh Seal |
| sv-typo-03 | gudfadren | The Godfather; The Godfather Part II | How to Train Your Dragon; How to Train Your Dragon; Guardians of the Galaxy Vol. 3 |
| sv-negative-03 | en realistisk film om att överleva i rymden, utan utomjordingar och skräck | The Martian; Interstellar | Flow; Society of the Snow; Thirteen Lives |

## Per case

Rank of the first relevant movie in the last run (– = not in the top 10), then Recall@10 of the case.

<details><summary>All cases</summary>

| Case | Tags | hybrid |
|---|---|---|
| en-plot-01 | plot | 1 (1.000) |
| en-plot-02 | plot | 1 (1.000) |
| en-plot-03 | plot | 1 (1.000) |
| en-plot-04 | plot | 1 (1.000) |
| en-plot-05 | plot | 1 (1.000) |
| en-plot-06 | plot | 1 (1.000) |
| en-plot-07 | plot | 2 (1.000) |
| en-plot-08 | plot | 1 (1.000) |
| en-plot-09 | plot | 3 (1.000) |
| en-plot-10 | plot | 2 (1.000) |
| en-plot-11 | plot | 1 (1.000) |
| en-plot-12 | plot | 1 (1.000) |
| en-plot-13 | plot | 1 (1.000) |
| en-plot-14 | plot | 2 (1.000) |
| en-mood-01 | mood | 3 (0.500) |
| en-mood-02 | mood | 1 (0.667) |
| en-mood-03 | mood | 1 (1.000) |
| en-mood-04 | mood | 1 (1.000) |
| en-person-01 | person | 1 (1.000) |
| en-person-02 | person | 1 (0.900) |
| en-person-03 | person | 1 (1.000) |
| en-person-04 | person | 1 (1.000) |
| en-person-05 | person | 1 (1.000) |
| en-typo-01 | title-typo | 2 (1.000) |
| en-typo-02 | title-typo | 1 (1.000) |
| en-typo-03 | title-typo | – (0.000) |
| en-typo-04 | title-typo | 1 (1.000) |
| en-typo-05 | title-typo | 1 (1.000) |
| en-constraint-01 | constraint, person | 9 (1.000) |
| en-constraint-02 | constraint, person | 1 (1.000) |
| en-constraint-03 | constraint | 1 (1.000) |
| en-constraint-04 | constraint, person | 1 (1.000) |
| en-negative-01 | negative | 2 (1.000) |
| en-negative-02 | negative | 1 (0.429) |
| en-negative-03 | negative | 1 (0.500) |
| ru-plot-01 | plot | 1 (1.000) |
| ru-plot-02 | plot | 1 (1.000) |
| ru-plot-03 | plot | 1 (0.714) |
| ru-plot-04 | plot | 1 (1.000) |
| ru-plot-05 | plot | 1 (1.000) |
| ru-plot-06 | plot | 1 (1.000) |
| ru-plot-07 | plot | 1 (1.000) |
| ru-plot-08 | plot | 1 (1.000) |
| ru-plot-09 | plot | 1 (1.000) |
| ru-plot-10 | plot | 3 (1.000) |
| ru-plot-11 | plot | 1 (1.000) |
| ru-plot-12 | plot | 1 (1.000) |
| ru-cross-01 | plot, cross-language | 2 (1.000) |
| ru-cross-02 | plot, cross-language | 1 (1.000) |
| ru-cross-03 | plot, cross-language | 1 (1.000) |
| ru-cross-04 | person, cross-language | 3 (0.600) |
| ru-mood-01 | mood | 3 (0.500) |
| ru-mood-02 | mood | 1 (1.000) |
| ru-mood-03 | mood | 1 (0.667) |
| ru-mood-04 | mood | 1 (1.000) |
| ru-person-01 | person, cross-language | 2 (0.600) |
| ru-person-02 | person, cross-language | 1 (1.000) |
| ru-person-03 | person, cross-language | 1 (1.000) |
| ru-person-04 | person, cross-language | 1 (0.667) |
| ru-typo-01 | title-typo | 1 (1.000) |
| ru-typo-02 | title-typo | 1 (1.000) |
| ru-typo-03 | title-typo | 1 (1.000) |
| ru-typo-04 | title-typo | 1 (1.000) |
| ru-typo-05 | title-typo | – (0.000) |
| ru-constraint-01 | constraint, person | 2 (1.000) |
| ru-constraint-02 | constraint, person | 3 (0.500) |
| ru-constraint-03 | constraint | 1 (1.000) |
| ru-negative-01 | negative | 3 (0.500) |
| ru-negative-02 | negative | 1 (1.000) |
| ru-negative-03 | negative | 1 (0.750) |
| sv-plot-01 | plot | 2 (1.000) |
| sv-plot-02 | plot | – (0.000) |
| sv-plot-03 | plot | 1 (1.000) |
| sv-plot-04 | plot | 1 (0.714) |
| sv-plot-05 | plot | 2 (1.000) |
| sv-plot-06 | plot | 1 (1.000) |
| sv-plot-07 | plot | 2 (1.000) |
| sv-plot-08 | plot | 3 (1.000) |
| sv-plot-09 | plot | 1 (1.000) |
| sv-plot-10 | plot | 2 (1.000) |
| sv-cross-01 | plot, cross-language | 1 (1.000) |
| sv-cross-02 | plot, cross-language | 3 (1.000) |
| sv-cross-03 | plot, cross-language | 4 (1.000) |
| sv-cross-04 | plot, cross-language | 1 (1.000) |
| sv-cross-05 | plot, cross-language | 1 (1.000) |
| sv-cross-06 | plot, cross-language | 1 (1.000) |
| sv-cross-07 | plot, cross-language | 1 (1.000) |
| sv-mood-01 | mood | 1 (0.500) |
| sv-mood-02 | mood | 1 (1.000) |
| sv-mood-03 | mood | 1 (1.000) |
| sv-person-01 | person | 1 (1.000) |
| sv-person-02 | person | 1 (1.000) |
| sv-person-03 | person | 1 (1.000) |
| sv-person-04 | person | 1 (0.900) |
| sv-typo-01 | title-typo | – (0.000) |
| sv-typo-02 | title-typo | 2 (1.000) |
| sv-typo-03 | title-typo | – (0.000) |
| sv-typo-04 | title-typo | 1 (1.000) |
| sv-typo-05 | title-typo | 1 (1.000) |
| sv-constraint-01 | constraint, person | 1 (1.000) |
| sv-constraint-02 | constraint, person | 1 (1.000) |
| sv-constraint-03 | constraint, person | 1 (1.000) |
| sv-negative-01 | negative | 3 (0.667) |
| sv-negative-02 | negative | 2 (0.400) |
| sv-negative-03 | negative | – (0.000) |

</details>

## Conclusion

Experiment against [baseline](2026-11-17_search_baseline.md): `Search:Hybrid:KeywordWeight` 1.0 → 0.3.

Hybrid MRR 0.788 → 0.797, nDCG 0.786 → 0.789; English MRR 0.808 → 0.837, negative queries 0.574 → 0.630; no locale or
tag gets worse, Recall is unchanged. It is the best fusion setting of this sweep, but the gain is about one query
moving up one or two ranks, and the golden set has no exact-title queries, the case the full weight was kept for in
I07 (10 title and name queries, MRR 1.00 with weight 1.0). **Default not changed**; follow-up: add exact-title cases
(tag `title`) to the golden set and rerun this experiment before lowering the weight.
