# Search evaluation: vectors-en-ru-sv — 2026-11-17

| | |
|---|---|
| Suite / label | `search` / `vectors-en-ru-sv` |
| Started (UTC) | 2026-11-17 23:03:28Z |
| Commit | `836f94e+dirty` |
| Command | `--suite search --label vectors-en-ru-sv --modes semantic,hybrid --set Search:Embeddings:Locales:1=ru --set Search:Embeddings:Locales:2=sv` |
| Dataset | `evals/datasets/search.golden.jsonl` version `759f9abeac13`, 105 cases, repeat 1 |
| Database | fresh container pgvector/pgvector:pg17, seed generated 2026-11-17 19:13:07Z |
| Embeddings | profile `local`: text-embedding-embeddinggemma-300m (768 dims, query prefix "task: search result \| query: ", document prefix "title: none \| text: "); text `movie-profile-v1`; locales en, ru, sv |
| Search parameters | `Semantic:MaxResults=100`, `Hybrid:RrfK=20`, `Hybrid:KeywordWeight=1`, `Hybrid:RelaxedKeywordWeight=0.1`, `Hybrid:SemanticWeight=1`, `Hybrid:CandidatesPerList=50`, `Hybrid:SimilarTitles=AverageVector` |
| Overrides | `Search:Embeddings:Locales:1=ru`, `Search:Embeddings:Locales:2=sv` |

Metrics are means over cases (spec 09 §3). Recall@k = relevant movies in the top k / min(relevant, k). MRR and nDCG
use the top 10; nDCG uses the grades of the dataset (1–3, gain 2^grade − 1). Excluded-genre rate: share of the top 10
with a genre the query rules out (cases with `excludedGenres` only). Latency: one `IMovieSearch.SearchAsync` call
in process (embedding, SQL, loading movies; query understanding in `hybrid+qu`), without HTTP.

## Summary

| Mode | Recall@5 | Recall@10 | MRR@10 | nDCG@10 | Excluded-genre rate | p50 ms | p95 ms | Degraded | QU fallbacks |
|---|---|---|---|---|---|---|---|---|---|
| semantic | 0.819 | 0.879 | 0.809 | 0.797 | 0.214 | 73 | 83 | 0 | 0 |
| hybrid | 0.820 | 0.877 | 0.792 | 0.786 | 0.200 | 91 | 133 | 0 | 0 |

## By locale (Recall@10 / MRR@10)

| Group | Cases | semantic | hybrid |
|---|---|---|---|
| en | 35 | 0.946 / 0.920 | 0.943 / 0.858 |
| ru | 35 | 0.863 / 0.762 | 0.853 / 0.769 |
| sv | 35 | 0.829 / 0.746 | 0.834 / 0.748 |

## By tag (Recall@10 / MRR@10)

| Group | Cases | semantic | hybrid |
|---|---|---|---|
| plot | 46 | 0.944 / 0.758 | 0.944 / 0.743 |
| mood | 11 | 0.803 / 0.818 | 0.773 / 0.818 |
| person | 22 | 0.869 / 0.884 | 0.889 / 0.884 |
| title-typo | 15 | 0.900 / 0.933 | 0.900 / 0.933 |
| constraint | 10 | 0.900 / 0.844 | 0.900 / 0.794 |
| negative | 9 | 0.605 / 0.630 | 0.564 / 0.556 |
| cross-language | 15 | 0.844 / 0.680 | 0.844 / 0.646 |

## Misses of every mode (4)

| Case | Query | Expected | Top 3 of hybrid |
|---|---|---|---|
| ru-typo-05 | гари потер | Harry Potter and the Philosopher's Stone; Harry Potter and the Chamber of Secrets; Harry Potter and the Prisoner of Azkaban; … | Harakiri; The Hunger Games; Short Term 12 |
| sv-plot-02 | en man upptäcker att hela hans liv är en dokusåpa | The Truman Show | Mishima: A Life in Four Chapters; Donnie Darko; Ace in the Hole |
| sv-cross-03 | en begåvad elev startar en stor fuskverksamhet på prov | Bad Genius | The Hunt; The Breadwinner; The Kid |
| sv-negative-03 | en realistisk film om att överleva i rymden, utan utomjordingar och skräck | The Martian; Interstellar | Society of the Snow; Land of Mine; Flow |

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
| en-plot-09 | plot | 3 (1.000) | 3 (1.000) |
| en-plot-10 | plot | 1 (1.000) | 2 (1.000) |
| en-plot-11 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-12 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-13 | plot | 1 (1.000) | 1 (1.000) |
| en-plot-14 | plot | 1 (1.000) | 2 (1.000) |
| en-mood-01 | mood | 4 (0.500) | 4 (0.500) |
| en-mood-02 | mood | 1 (0.667) | 1 (0.667) |
| en-mood-03 | mood | 1 (1.000) | 1 (1.000) |
| en-mood-04 | mood | 1 (1.000) | 1 (1.000) |
| en-person-01 | person | 1 (1.000) | 1 (1.000) |
| en-person-02 | person | 1 (0.900) | 1 (0.900) |
| en-person-03 | person | 1 (1.000) | 1 (1.000) |
| en-person-04 | person | 1 (1.000) | 1 (1.000) |
| en-person-05 | person | 1 (1.000) | 1 (1.000) |
| en-typo-01 | title-typo | 1 (1.000) | 1 (1.000) |
| en-typo-02 | title-typo | 1 (1.000) | 1 (1.000) |
| en-typo-03 | title-typo | 1 (1.000) | 1 (1.000) |
| en-typo-04 | title-typo | 1 (1.000) | 1 (1.000) |
| en-typo-05 | title-typo | 1 (1.000) | 1 (1.000) |
| en-constraint-01 | constraint, person | 9 (1.000) | 9 (1.000) |
| en-constraint-02 | constraint, person | 1 (1.000) | 1 (1.000) |
| en-constraint-03 | constraint | 1 (1.000) | 1 (1.000) |
| en-constraint-04 | constraint, person | 1 (1.000) | 1 (1.000) |
| en-negative-01 | negative | 1 (1.000) | 2 (1.000) |
| en-negative-02 | negative | 1 (0.429) | 1 (0.429) |
| en-negative-03 | negative | 2 (0.600) | 3 (0.500) |
| ru-plot-01 | plot | 2 (1.000) | 1 (1.000) |
| ru-plot-02 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-03 | plot | 1 (0.714) | 1 (0.714) |
| ru-plot-04 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-05 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-06 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-07 | plot | 3 (1.000) | 2 (1.000) |
| ru-plot-08 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-09 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-10 | plot | 2 (1.000) | 1 (1.000) |
| ru-plot-11 | plot | 4 (1.000) | 3 (1.000) |
| ru-plot-12 | plot | 1 (1.000) | 1 (1.000) |
| ru-cross-01 | plot, cross-language | 3 (1.000) | 3 (1.000) |
| ru-cross-02 | plot, cross-language | 2 (1.000) | 2 (1.000) |
| ru-cross-03 | plot, cross-language | 1 (1.000) | 2 (1.000) |
| ru-cross-04 | person, cross-language | 2 (0.400) | 2 (0.400) |
| ru-mood-01 | mood | 4 (0.333) | 4 (0.333) |
| ru-mood-02 | mood | 1 (1.000) | 1 (1.000) |
| ru-mood-03 | mood | 1 (1.000) | 1 (0.667) |
| ru-mood-04 | mood | 1 (1.000) | 1 (1.000) |
| ru-person-01 | person, cross-language | 2 (0.700) | 2 (0.700) |
| ru-person-02 | person, cross-language | 1 (1.000) | 1 (1.000) |
| ru-person-03 | person, cross-language | 1 (1.000) | 1 (1.000) |
| ru-person-04 | person, cross-language | 1 (0.556) | 1 (0.556) |
| ru-typo-01 | title-typo | 1 (1.000) | 1 (1.000) |
| ru-typo-02 | title-typo | 1 (1.000) | 1 (1.000) |
| ru-typo-03 | title-typo | 1 (1.000) | 1 (1.000) |
| ru-typo-04 | title-typo | 1 (1.000) | 1 (1.000) |
| ru-typo-05 | title-typo | – (0.000) | – (0.000) |
| ru-constraint-01 | constraint, person | 1 (1.000) | 1 (1.000) |
| ru-constraint-02 | constraint, person | 3 (0.250) | 3 (0.250) |
| ru-constraint-03 | constraint | 1 (1.000) | 2 (1.000) |
| ru-negative-01 | negative | 3 (0.500) | 3 (0.500) |
| ru-negative-02 | negative | 1 (1.000) | 1 (1.000) |
| ru-negative-03 | negative | 3 (0.750) | 3 (0.750) |
| sv-plot-01 | plot | 3 (1.000) | 3 (1.000) |
| sv-plot-02 | plot | – (0.000) | – (0.000) |
| sv-plot-03 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-04 | plot | 1 (0.714) | 1 (0.714) |
| sv-plot-05 | plot | 4 (1.000) | 3 (1.000) |
| sv-plot-06 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-07 | plot | 2 (1.000) | 2 (1.000) |
| sv-plot-08 | plot | 2 (1.000) | 2 (1.000) |
| sv-plot-09 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-10 | plot | 6 (1.000) | 6 (1.000) |
| sv-cross-01 | plot, cross-language | 4 (1.000) | 4 (1.000) |
| sv-cross-02 | plot, cross-language | 9 (1.000) | 9 (1.000) |
| sv-cross-03 | plot, cross-language | – (0.000) | – (0.000) |
| sv-cross-04 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-05 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-06 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-07 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-mood-01 | mood | 2 (0.333) | 2 (0.333) |
| sv-mood-02 | mood | 1 (1.000) | 1 (1.000) |
| sv-mood-03 | mood | 1 (1.000) | 1 (1.000) |
| sv-person-01 | person | 1 (1.000) | 1 (1.000) |
| sv-person-02 | person | 1 (1.000) | 1 (1.000) |
| sv-person-03 | person | 1 (0.667) | 1 (1.000) |
| sv-person-04 | person | 1 (0.900) | 1 (1.000) |
| sv-typo-01 | title-typo | 1 (1.000) | 1 (1.000) |
| sv-typo-02 | title-typo | 1 (1.000) | 1 (1.000) |
| sv-typo-03 | title-typo | 1 (0.500) | 1 (0.500) |
| sv-typo-04 | title-typo | 1 (1.000) | 1 (1.000) |
| sv-typo-05 | title-typo | 1 (1.000) | 1 (1.000) |
| sv-constraint-01 | constraint, person | 1 (1.000) | 1 (1.000) |
| sv-constraint-02 | constraint, person | 1 (0.750) | 1 (0.750) |
| sv-constraint-03 | constraint, person | 1 (1.000) | 1 (1.000) |
| sv-negative-01 | negative | 2 (0.667) | 2 (0.500) |
| sv-negative-02 | negative | 1 (0.500) | 1 (0.400) |
| sv-negative-03 | negative | – (0.000) | – (0.000) |

</details>

## Conclusion

Experiment against [baseline](2026-11-17_search_baseline.md): vectors of the movie texts in en, ru and sv
(`Search:Embeddings:Locales` = en, ru, sv: 3000 vectors) instead of English only — the open question of I06.

- Semantic: MRR 0.794 → 0.809, Recall@10 0.865 → 0.879; hybrid: MRR 0.788 → 0.792 (no real change).
- Not where it was expected: Russian and Swedish queries barely move (semantic ru 0.769 → 0.762, sv 0.719 → 0.746).
  The gain is in English (0.894 → 0.920) and title typos (0.606 → 0.933: the ru / sv texts carry localized titles, so
  "nykeln till frihet" or "гари потер" find a close vector). Cross-language queries get worse (0.806 → 0.680): a
  Russian description now also matches the Russian texts of unrelated movies. The excluded-genre rate rises
  (0.171 → 0.214).
- Cost: 3× vectors and 3× indexing time (I06).

**Default stays English only.** The typo gain is better addressed directly (fuzzy title matching, I19) than with three
times the vectors.
