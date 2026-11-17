# Query understanding evaluation: baseline — 2026-11-17

| | |
|---|---|
| Suite / label | `qu` / `baseline` |
| Started (UTC) | 2026-11-17 23:04:42Z |
| Commit | `836f94e+dirty` |
| Command | `--suite qu --label baseline` |
| Dataset | `evals/datasets/qu.golden.jsonl` version `d4328efc6441`, 40 cases, repeat 3 |
| Database | fresh container pgvector/pgvector:pg17, seed generated 2026-11-17 19:13:07Z |
| Chat | profile `local`: google/gemma-4-e4b (reasoning None) |
| Prompts | `query-understanding.v2` |

Rates are over all runs (cases × repeat). Valid: the model gave a valid answer (no fallback). Retry: a second call
was needed. Exact match: every field right. A field is right when it equals the expected value; "not mentioned" must
stay empty, so an invented filter counts as wrong. Genre precision and recall are micro averages over slugs.

## Summary

| Valid | Retry | Exact match | Genres incl. P / R | Genres excl. P / R | p50 ms | p95 ms |
|---|---|---|---|---|---|---|
| 100% | 0% | 83% | 0.976 / 0.922 | 1.000 / 1.000 | 3839 | 4316 |

## Field accuracy

| Group | Cases | Valid | Exact | genresInclude | genresExclude | yearFrom | yearTo | minRating | maxPriceMinor | language | similarToTitles |
|---|---|---|---|---|---|---|---|---|---|---|---|
| all | 40 | 100% | 83% | 93% | 100% | 95% | 98% | 100% | 100% | 98% | 100% |
| en | 14 | 100% | 88% | 88% | 100% | 100% | 100% | 100% | 100% | 100% | 100% |
| ru | 14 | 100% | 76% | 90% | 100% | 100% | 93% | 100% | 100% | 93% | 100% |
| sv | 12 | 100% | 83% | 100% | 100% | 83% | 100% | 100% | 100% | 100% | 100% |

## Mismatches (8 of 40 cases)

| Case | Query | Wrong runs | Wrong fields | Answer |
|---|---|---|---|---|
| en-qu-03 | movies about space, no horror | 2/3 | genresInclude | `{"filters":{"genresInclude":["science-fiction"],"genresExclude":["horror"],"yearFrom":null,"yearTo":null,"minRating":null,"maxPriceMinor":null,"language":null},"similarToTitles":[],"semanticQuery":"outer space, astronauts, space travel"}` |
| en-qu-05 | Japanese anime about spirits | 3/3 | genresInclude | `{"filters":{"genresInclude":[],"genresExclude":[],"yearFrom":null,"yearTo":null,"minRating":null,"maxPriceMinor":null,"language":"ja"},"similarToTitles":[],"semanticQuery":"spirits"}` |
| ru-qu-04 | советские военные фильмы | 3/3 | language | `{"filters":{"genresInclude":["war"],"genresExclude":[],"yearFrom":null,"yearTo":null,"minRating":null,"maxPriceMinor":null,"language":null},"similarToTitles":[],"semanticQuery":"военный"}` |
| ru-qu-05 | мультфильмы для всей семьи дешевле 2 долларов | 1/3 | genresInclude | `{"filters":{"genresInclude":["family"],"genresExclude":[],"yearFrom":null,"yearTo":null,"minRating":null,"maxPriceMinor":200,"language":null},"similarToTitles":[],"semanticQuery":""}` |
| ru-qu-08 | японское аниме, только не ужасы | 3/3 | genresInclude | `{"filters":{"genresInclude":[],"genresExclude":["horror"],"yearFrom":null,"yearTo":null,"minRating":null,"maxPriceMinor":null,"language":"ja"},"similarToTitles":[],"semanticQuery":""}` |
| ru-qu-09 | вестерны до 1970 года | 3/3 | yearTo | `{"filters":{"genresInclude":["western"],"genresExclude":[],"yearFrom":null,"yearTo":1970,"minRating":null,"maxPriceMinor":null,"language":null},"similarToTitles":[],"semanticQuery":""}` |
| sv-qu-02 | skräckfilmer från 70-talet | 3/3 | yearFrom | `{"filters":{"genresInclude":["horror"],"genresExclude":[],"yearFrom":null,"yearTo":1979,"minRating":null,"maxPriceMinor":null,"language":null},"similarToTitles":[],"semanticQuery":""}` |
| sv-qu-07 | science fiction efter 2000 men inte action | 3/3 | yearFrom | `{"filters":{"genresInclude":["science-fiction"],"genresExclude":["action"],"yearFrom":2011,"yearTo":null,"minRating":null,"maxPriceMinor":null,"language":null},"similarToTitles":[],"semanticQuery":"science-fiction"}` |

## Conclusion

First baseline of query understanding (prompt `query-understanding.v2`, Gemma 4 E4B on LM Studio, 40 cases × 3).

- **Structured output is reliable**: 100% valid answers, no retry needed (JSON Schema + enum of genre slugs).
  Excluded genres, ratings, prices and "like X" titles are always right.
- **Exact match 83%**; the errors are systematic, not random (most fail 3 of 3 runs):
  - **Years after / before / decades**: "efter 2000" → `yearFrom: 2011` (the model copies the prompt's "after 2010 →
    2011" example), "до 1970 года" → `yearTo: 1970` instead of 1969, "från 70-talet" → only `yearTo: 1979`. Off-by-one
    years matter little for search; a lost decade start or a copied year does.
  - **Implied genres and countries**: "anime" does not become `animation` (en and ru), "советские" does not become
    `language: ru`. The prompt forbids guessing genres from the plot, and the model extends that to words that name a
    genre or a country.
  - "movies about space, no horror" got `science-fiction` in 2 of 3 runs: **temperature 0 is not fully deterministic**
    on LM Studio (parallel slots), which is why the suite repeats.
- 3.8 s p50 per call on the laptop GPU.

Follow-ups for a prompt v3 (with before / after reports of this suite and of `hybrid+qu`): explicit year rules with
examples that do not share numbers with real queries, "anime" → animation, country adjectives → language; consider a
stronger or faster model for QU (`Search:QueryUnderstanding:ChatProfile`).
