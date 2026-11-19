# Search evaluation: i20-ollama-embeddinggemma — 2026-11-19

| | |
|---|---|
| Suite / label | `search` / `i20-ollama-embeddinggemma` |
| Started (UTC) | 2026-11-19 12:32:36Z |
| Commit | `9528bc5+dirty` |
| Command | `--suite search --label i20-ollama-embeddinggemma --modes semantic,hybrid --profile ollama --set AI:EmbeddingProfiles:ollama:Endpoint=http://localhost:11435/v1` |
| Dataset | `evals/datasets/search.golden.jsonl` version `759f9abeac13`, 105 cases, repeat 1 |
| Database | fresh container pgvector/pgvector:pg17, seed generated 2026-11-17 19:13:07Z |
| Embeddings | profile `ollama`: embeddinggemma:300m (768 dims, query prefix "task: search result \| query: ", document prefix "title: none \| text: "); text `movie-profile-v1`; locales en |
| Search parameters | `Semantic:MaxResults=100`, `Hybrid:RrfK=20`, `Hybrid:KeywordWeight=1`, `Hybrid:RelaxedKeywordWeight=0.1`, `Hybrid:SemanticWeight=1`, `Hybrid:CandidatesPerList=50`, `Hybrid:SimilarTitles=AverageVector` |
| Overrides | `AI:EmbeddingProfiles:ollama:Endpoint=http://localhost:11435/v1` |

Metrics are means over cases (spec 09 §3). Recall@k = relevant movies in the top k / min(relevant, k). MRR and nDCG
use the top 10; nDCG uses the grades of the dataset (1–3, gain 2^grade − 1). Excluded-genre rate: share of the top 10
with a genre the query rules out (cases with `excludedGenres` only). Latency: one `IMovieSearch.SearchAsync` call
in process (embedding, SQL, loading movies; query understanding in `hybrid+qu`), without HTTP.

## Summary

| Mode | Recall@5 | Recall@10 | MRR@10 | nDCG@10 | Excluded-genre rate | p50 ms | p95 ms | Degraded | QU fallbacks |
|---|---|---|---|---|---|---|---|---|---|
| semantic | 0.829 | 0.865 | 0.797 | 0.787 | 0.157 | 57 | 65 | 0 | 0 |
| hybrid | 0.837 | 0.873 | 0.790 | 0.788 | 0.157 | 75 | 118 | 0 | 0 |

## By locale (Recall@10 / MRR@10)

| Group | Cases | semantic | hybrid |
|---|---|---|---|
| en | 35 | 0.914 / 0.908 | 0.914 / 0.822 |
| ru | 35 | 0.857 / 0.768 | 0.872 / 0.830 |
| sv | 35 | 0.824 / 0.717 | 0.834 / 0.719 |

## By tag (Recall@10 / MRR@10)

| Group | Cases | semantic | hybrid |
|---|---|---|---|
| plot | 46 | 0.966 / 0.818 | 0.966 / 0.814 |
| mood | 11 | 0.803 / 0.879 | 0.803 / 0.879 |
| person | 22 | 0.902 / 0.853 | 0.917 / 0.831 |
| title-typo | 15 | 0.700 / 0.639 | 0.733 / 0.700 |
| constraint | 10 | 0.950 / 0.793 | 0.950 / 0.744 |
| negative | 9 | 0.583 / 0.676 | 0.583 / 0.565 |
| cross-language | 15 | 0.925 / 0.806 | 0.925 / 0.794 |

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
| en-mood-01 | mood | 3 (0.500) | 3 (0.500) |
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
| en-typo-03 | title-typo | – (0.000) | – (0.000) |
| en-typo-04 | title-typo | 1 (1.000) | 1 (1.000) |
| en-typo-05 | title-typo | 1 (1.000) | 1 (1.000) |
| en-constraint-01 | constraint, person | 10 (1.000) | 9 (1.000) |
| en-constraint-02 | constraint, person | 1 (1.000) | 1 (1.000) |
| en-constraint-03 | constraint | 1 (1.000) | 1 (1.000) |
| en-constraint-04 | constraint, person | 1 (1.000) | 2 (1.000) |
| en-negative-01 | negative | 1 (1.000) | 2 (1.000) |
| en-negative-02 | negative | 1 (0.429) | 1 (0.429) |
| en-negative-03 | negative | 1 (0.500) | 2 (0.500) |
| ru-plot-01 | plot | 2 (1.000) | 1 (1.000) |
| ru-plot-02 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-03 | plot | 1 (0.714) | 1 (0.714) |
| ru-plot-04 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-05 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-06 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-07 | plot | 2 (1.000) | 1 (1.000) |
| ru-plot-08 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-09 | plot | 1 (1.000) | 1 (1.000) |
| ru-plot-10 | plot | 5 (1.000) | 5 (1.000) |
| ru-plot-11 | plot | 2 (1.000) | 1 (1.000) |
| ru-plot-12 | plot | 1 (1.000) | 1 (1.000) |
| ru-cross-01 | plot, cross-language | 2 (1.000) | 2 (1.000) |
| ru-cross-02 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| ru-cross-03 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| ru-cross-04 | person, cross-language | 3 (0.500) | 3 (0.500) |
| ru-mood-01 | mood | 3 (0.500) | 3 (0.500) |
| ru-mood-02 | mood | 1 (1.000) | 1 (1.000) |
| ru-mood-03 | mood | 1 (0.667) | 1 (0.667) |
| ru-mood-04 | mood | 1 (1.000) | 1 (1.000) |
| ru-person-01 | person, cross-language | 2 (0.600) | 2 (0.600) |
| ru-person-02 | person, cross-language | 1 (1.000) | 1 (1.000) |
| ru-person-03 | person, cross-language | 1 (1.000) | 1 (1.000) |
| ru-person-04 | person, cross-language | 1 (0.778) | 1 (0.778) |
| ru-typo-01 | title-typo | 1 (1.000) | 1 (1.000) |
| ru-typo-02 | title-typo | 1 (0.500) | 1 (1.000) |
| ru-typo-03 | title-typo | 3 (1.000) | 1 (1.000) |
| ru-typo-04 | title-typo | 1 (1.000) | 1 (1.000) |
| ru-typo-05 | title-typo | – (0.000) | – (0.000) |
| ru-constraint-01 | constraint, person | 2 (1.000) | 2 (1.000) |
| ru-constraint-02 | constraint, person | 3 (0.500) | 3 (0.500) |
| ru-constraint-03 | constraint | 1 (1.000) | 1 (1.000) |
| ru-negative-01 | negative | 3 (0.500) | 3 (0.500) |
| ru-negative-02 | negative | 1 (1.000) | 1 (1.000) |
| ru-negative-03 | negative | 1 (0.750) | 1 (0.750) |
| sv-plot-01 | plot | 2 (1.000) | 2 (1.000) |
| sv-plot-02 | plot | – (0.000) | – (0.000) |
| sv-plot-03 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-04 | plot | 1 (0.714) | 1 (0.714) |
| sv-plot-05 | plot | 2 (1.000) | 2 (1.000) |
| sv-plot-06 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-07 | plot | 2 (1.000) | 2 (1.000) |
| sv-plot-08 | plot | 3 (1.000) | 3 (1.000) |
| sv-plot-09 | plot | 1 (1.000) | 1 (1.000) |
| sv-plot-10 | plot | 2 (1.000) | 2 (1.000) |
| sv-cross-01 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-02 | plot, cross-language | 2 (1.000) | 3 (1.000) |
| sv-cross-03 | plot, cross-language | 4 (1.000) | 4 (1.000) |
| sv-cross-04 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-05 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-06 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-cross-07 | plot, cross-language | 1 (1.000) | 1 (1.000) |
| sv-mood-01 | mood | 1 (0.500) | 1 (0.500) |
| sv-mood-02 | mood | 1 (1.000) | 1 (1.000) |
| sv-mood-03 | mood | 1 (1.000) | 1 (1.000) |
| sv-person-01 | person | 1 (1.000) | 1 (1.000) |
| sv-person-02 | person | 1 (1.000) | 1 (1.000) |
| sv-person-03 | person | 1 (0.667) | 1 (1.000) |
| sv-person-04 | person | 1 (0.900) | 1 (0.900) |
| sv-typo-01 | title-typo | – (0.000) | – (0.000) |
| sv-typo-02 | title-typo | 4 (1.000) | 2 (1.000) |
| sv-typo-03 | title-typo | – (0.000) | – (0.000) |
| sv-typo-04 | title-typo | 1 (1.000) | 1 (1.000) |
| sv-typo-05 | title-typo | 1 (1.000) | 1 (1.000) |
| sv-constraint-01 | constraint, person | 1 (1.000) | 1 (1.000) |
| sv-constraint-02 | constraint, person | 1 (1.000) | 1 (1.000) |
| sv-constraint-03 | constraint, person | 1 (1.000) | 1 (1.000) |
| sv-negative-01 | negative | 4 (0.667) | 4 (0.667) |
| sv-negative-02 | negative | 2 (0.400) | 2 (0.400) |
| sv-negative-03 | negative | – (0.000) | – (0.000) |

</details>

## Conclusion

I20: the demo environment embeds with EmbeddingGemma in Ollama (CPU, `embeddinggemma:300m`) instead of LM Studio.
Against the LM Studio baseline of the same seed and dataset (`2026-11-18_search_i16-before-1000-seed.md`): semantic
Recall@10 0.865 = 0.865, MRR@10 0.797 vs 0.794, nDCG@10 0.787 vs 0.784; hybrid Recall@10 0.873 = 0.873, MRR@10 0.790
vs 0.788. The same model in two runtimes (both GGUF builds of EmbeddingGemma 300M): the differences are numeric noise of
the builds, not a change of quality. Latency in process is the same (semantic p50 57 ms); on the server the call is a container
hop. Decision: Ollama EmbeddingGemma is the demo default; the numbers of development hold for the demo.
