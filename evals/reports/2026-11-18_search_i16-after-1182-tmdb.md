# Search evaluation: i16-after-1182-tmdb — 2026-11-18

| | |
|---|---|
| Suite / label | `search` / `i16-after-1182-tmdb` |
| Started (UTC) | 2026-11-18 20:14:49Z |
| Commit | `2715796+dirty` |
| Command | `--suite search --label i16-after-1182-tmdb --connection <connection>` |
| Dataset | `evals/datasets/search.golden.jsonl` version `759f9abeac13`, 105 cases, repeat 1 |
| Database | existing database (--connection), seed generated 2026-11-17 19:13:07Z |
| Embeddings | profile `local`: text-embedding-embeddinggemma-300m (768 dims, query prefix "task: search result \| query: ", document prefix "title: none \| text: "); text `movie-profile-v1`; locales en |
| Chat | profile `local`: google/gemma-4-e4b (reasoning None) |
| Prompts | `query-understanding.v2` |
| Search parameters | `Semantic:MaxResults=100`, `Hybrid:RrfK=20`, `Hybrid:KeywordWeight=1`, `Hybrid:RelaxedKeywordWeight=0.1`, `Hybrid:SemanticWeight=1`, `Hybrid:CandidatesPerList=50`, `Hybrid:SimilarTitles=AverageVector` |

Metrics are means over cases (spec 09 §3). Recall@k = relevant movies in the top k / min(relevant, k). MRR and nDCG
use the top 10; nDCG uses the grades of the dataset (1–3, gain 2^grade − 1). Excluded-genre rate: share of the top 10
with a genre the query rules out (cases with `excludedGenres` only). Latency: one `IMovieSearch.SearchAsync` call
in process (embedding, SQL, loading movies; query understanding in `hybrid+qu`), without HTTP.

## Summary

| Mode | Recall@5 | Recall@10 | MRR@10 | nDCG@10 | Excluded-genre rate | p50 ms | p95 ms | Degraded | QU fallbacks |
|---|---|---|---|---|---|---|---|---|---|
| keyword | 0.217 | 0.287 | 0.246 | 0.227 | 0.129 | 77 | 117 | 0 | 0 |
| semantic | 0.811 | 0.855 | 0.770 | 0.769 | 0.143 | 66 | 77 | 0 | 0 |
| hybrid | 0.822 | 0.866 | 0.757 | 0.767 | 0.157 | 95 | 128 | 0 | 0 |
| hybrid+qu | 0.844 | 0.858 | 0.814 | 0.800 | 0.000 | 3338 | 3832 | 0 | 0 |

## By locale (Recall@10 / MRR@10)

| Group | Cases | keyword | semantic | hybrid | hybrid+qu |
|---|---|---|---|---|---|
| en | 35 | 0.266 / 0.255 | 0.894 / 0.871 | 0.894 / 0.814 | 0.873 / 0.880 |
| ru | 35 | 0.307 / 0.232 | 0.848 / 0.750 | 0.871 / 0.770 | 0.835 / 0.776 |
| sv | 35 | 0.288 / 0.252 | 0.824 / 0.689 | 0.834 / 0.687 | 0.867 / 0.787 |

## By tag (Recall@10 / MRR@10)

| Group | Cases | keyword | semantic | hybrid | hybrid+qu |
|---|---|---|---|---|---|
| plot | 46 | 0.283 / 0.124 | 0.966 / 0.811 | 0.966 / 0.783 | 0.966 / 0.837 |
| mood | 11 | 0.076 / 0.068 | 0.803 / 0.794 | 0.803 / 0.794 | 0.621 / 0.673 |
| person | 22 | 0.266 / 0.412 | 0.877 / 0.823 | 0.908 / 0.801 | 0.873 / 0.883 |
| title-typo | 15 | 0.629 / 0.667 | 0.700 / 0.606 | 0.733 / 0.700 | 0.800 / 0.767 |
| constraint | 10 | 0.083 / 0.063 | 0.917 / 0.794 | 0.950 / 0.744 | 0.775 / 0.825 |
| negative | 9 | 0.111 / 0.037 | 0.527 / 0.620 | 0.527 / 0.509 | 0.740 / 0.852 |
| cross-language | 15 | 0.161 / 0.210 | 0.924 / 0.802 | 0.924 / 0.724 | 0.918 / 0.800 |

## Misses of every mode (3)

| Case | Query | Expected | Top 3 of hybrid+qu |
|---|---|---|---|
| en-typo-03 | godfahter | The Godfather; The Godfather Part II | Thor: Love and Thunder; New Gods: Nezha Reborn; Faust |
| ru-typo-05 | гари потер | Harry Potter and the Philosopher's Stone; Harry Potter and the Chamber of Secrets; Harry Potter and the Prisoner of Azkaban; … | Warrior; Short Term 12; Hotarubi no Mori e |
| sv-plot-02 | en man upptäcker att hela hans liv är en dokusåpa | The Truman Show | It's Such a Beautiful Day; All My Life; tmdb:891 |

## Per case

Rank of the first relevant movie in the last run (– = not in the top 10), then Recall@10 of the case.

<details><summary>All cases</summary>

| Case | Tags | keyword | semantic | hybrid | hybrid+qu |
|---|---|---|---|---|---|
| en-plot-01 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-plot-02 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 2 (1.000) |
| en-plot-03 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-plot-04 | plot | 10 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-plot-05 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-plot-06 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-plot-07 | plot | – (0.000) | 1 (1.000) | 2 (1.000) | 2 (1.000) |
| en-plot-08 | plot | 2 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-plot-09 | plot | – (0.000) | 3 (1.000) | 3 (1.000) | 3 (1.000) |
| en-plot-10 | plot | – (0.000) | 1 (1.000) | 2 (1.000) | 1 (1.000) |
| en-plot-11 | plot | 5 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-plot-12 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-plot-13 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-plot-14 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-mood-01 | mood | – (0.000) | 5 (0.500) | 5 (0.500) | 5 (0.167) |
| en-mood-02 | mood | 2 (0.667) | 1 (0.667) | 1 (0.667) | 1 (0.667) |
| en-mood-03 | mood | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (0.500) |
| en-mood-04 | mood | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-person-01 | person | 1 (0.400) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-person-02 | person | 9 (0.200) | 3 (0.700) | 3 (0.700) | 1 (0.800) |
| en-person-03 | person | 1 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-person-04 | person | 1 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-person-05 | person | 1 (0.100) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-typo-01 | title-typo | 1 (1.000) | 2 (1.000) | 1 (1.000) | 1 (1.000) |
| en-typo-02 | title-typo | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-typo-03 | title-typo | – (0.000) | – (0.000) | – (0.000) | – (0.000) |
| en-typo-04 | title-typo | 1 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-typo-05 | title-typo | 1 (0.429) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-constraint-01 | constraint, person | – (0.000) | 9 (1.000) | 9 (1.000) | 1 (1.000) |
| en-constraint-02 | constraint, person | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-constraint-03 | constraint | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| en-constraint-04 | constraint, person | 2 (0.500) | 1 (1.000) | 2 (1.000) | 4 (0.500) |
| en-negative-01 | negative | – (0.000) | 1 (0.500) | 2 (0.500) | 1 (1.000) |
| en-negative-02 | negative | – (0.000) | 1 (0.429) | 1 (0.429) | 1 (0.429) |
| en-negative-03 | negative | – (0.000) | 1 (0.500) | 2 (0.500) | 1 (0.500) |
| ru-plot-01 | plot | 4 (1.000) | 2 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-plot-02 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 2 (1.000) |
| ru-plot-03 | plot | – (0.000) | 1 (0.714) | 1 (0.714) | 1 (0.714) |
| ru-plot-04 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-plot-05 | plot | 8 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-plot-06 | plot | 6 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-plot-07 | plot | – (0.000) | 2 (1.000) | 2 (1.000) | 2 (1.000) |
| ru-plot-08 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-plot-09 | plot | 1 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-plot-10 | plot | – (0.000) | 5 (1.000) | 4 (1.000) | 2 (1.000) |
| ru-plot-11 | plot | 9 (1.000) | 2 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-plot-12 | plot | 1 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-cross-01 | plot, cross-language | – (0.000) | 2 (1.000) | 2 (1.000) | 1 (1.000) |
| ru-cross-02 | plot, cross-language | – (0.000) | 1 (1.000) | 2 (1.000) | 1 (1.000) |
| ru-cross-03 | plot, cross-language | – (0.000) | 1 (1.000) | 2 (1.000) | 1 (1.000) |
| ru-cross-04 | person, cross-language | – (0.000) | 3 (0.600) | 3 (0.600) | 3 (0.500) |
| ru-mood-01 | mood | – (0.000) | 5 (0.500) | 5 (0.500) | 2 (0.500) |
| ru-mood-02 | mood | – (0.000) | 1 (1.000) | 1 (1.000) | 2 (1.000) |
| ru-mood-03 | mood | – (0.000) | 1 (0.667) | 1 (0.667) | – (0.000) |
| ru-mood-04 | mood | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-person-01 | person, cross-language | – (0.000) | 2 (0.600) | 2 (0.600) | 3 (0.600) |
| ru-person-02 | person, cross-language | 1 (0.200) | 1 (1.000) | 1 (1.000) | 2 (1.000) |
| ru-person-03 | person, cross-language | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-person-04 | person, cross-language | 1 (0.222) | 1 (0.667) | 1 (0.667) | 1 (0.667) |
| ru-typo-01 | title-typo | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-typo-02 | title-typo | 1 (1.000) | 1 (0.500) | 1 (1.000) | 1 (1.000) |
| ru-typo-03 | title-typo | 1 (1.000) | 3 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-typo-04 | title-typo | 1 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| ru-typo-05 | title-typo | – (0.000) | – (0.000) | – (0.000) | – (0.000) |
| ru-constraint-01 | constraint, person | 8 (0.333) | 2 (0.667) | 2 (1.000) | 1 (1.000) |
| ru-constraint-02 | constraint, person | – (0.000) | 3 (0.500) | 3 (0.500) | 1 (0.250) |
| ru-constraint-03 | constraint | – (0.000) | 1 (1.000) | 1 (1.000) | – (0.000) |
| ru-negative-01 | negative | – (0.000) | 3 (0.500) | 3 (0.500) | 1 (1.000) |
| ru-negative-02 | negative | 3 (1.000) | 1 (1.000) | 1 (1.000) | 2 (1.000) |
| ru-negative-03 | negative | – (0.000) | 2 (0.750) | 2 (0.750) | 1 (1.000) |
| sv-plot-01 | plot | – (0.000) | 4 (1.000) | 4 (1.000) | 1 (1.000) |
| sv-plot-02 | plot | – (0.000) | – (0.000) | – (0.000) | – (0.000) |
| sv-plot-03 | plot | 9 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-plot-04 | plot | – (0.000) | 1 (0.714) | 1 (0.714) | 1 (0.714) |
| sv-plot-05 | plot | – (0.000) | 2 (1.000) | 2 (1.000) | 2 (1.000) |
| sv-plot-06 | plot | 1 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-plot-07 | plot | – (0.000) | 2 (1.000) | 3 (1.000) | 2 (1.000) |
| sv-plot-08 | plot | – (0.000) | 3 (1.000) | 3 (1.000) | 1 (1.000) |
| sv-plot-09 | plot | – (0.000) | 1 (1.000) | 1 (1.000) | 2 (1.000) |
| sv-plot-10 | plot | – (0.000) | 2 (1.000) | 2 (1.000) | 3 (1.000) |
| sv-cross-01 | plot, cross-language | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-cross-02 | plot, cross-language | – (0.000) | 2 (1.000) | 3 (1.000) | 3 (1.000) |
| sv-cross-03 | plot, cross-language | – (0.000) | 5 (1.000) | 5 (1.000) | 2 (1.000) |
| sv-cross-04 | plot, cross-language | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-cross-05 | plot, cross-language | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-cross-06 | plot, cross-language | 1 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-cross-07 | plot, cross-language | 7 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-mood-01 | mood | 4 (0.167) | 3 (0.500) | 3 (0.500) | 5 (0.333) |
| sv-mood-02 | mood | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (0.667) |
| sv-mood-03 | mood | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-person-01 | person | 1 (0.100) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-person-02 | person | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-person-03 | person | 1 (1.000) | 1 (0.667) | 1 (1.000) | 1 (1.000) |
| sv-person-04 | person | 3 (0.800) | 1 (0.900) | 1 (0.900) | 1 (0.900) |
| sv-typo-01 | title-typo | 1 (1.000) | – (0.000) | – (0.000) | – (0.000) |
| sv-typo-02 | title-typo | 1 (1.000) | 4 (1.000) | 2 (1.000) | 2 (1.000) |
| sv-typo-03 | title-typo | – (0.000) | – (0.000) | – (0.000) | 1 (1.000) |
| sv-typo-04 | title-typo | 1 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-typo-05 | title-typo | 1 (1.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-constraint-01 | constraint, person | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-constraint-02 | constraint, person | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-constraint-03 | constraint, person | – (0.000) | 1 (1.000) | 1 (1.000) | 1 (1.000) |
| sv-negative-01 | negative | – (0.000) | 4 (0.667) | 4 (0.667) | 1 (0.333) |
| sv-negative-02 | negative | – (0.000) | 2 (0.400) | 2 (0.400) | 6 (0.400) |
| sv-negative-03 | negative | – (0.000) | – (0.000) | – (0.000) | 1 (1.000) |

</details>

## Conclusion

"After" run of the I16 catalog growth experiment: the dev database after the TMDB import of top-rated pages 1–50 (1182 movies: the seed plus 180 new ones, all embedded), same code and settings as the "before" run. The drop of MRR is mostly new movies that are relevant but not in the golden set (it may name only seed movies); with them judged relevant the metrics are on par. Details: [2026-11-18_I16_catalog-growth.md](2026-11-18_I16_catalog-growth.md).
