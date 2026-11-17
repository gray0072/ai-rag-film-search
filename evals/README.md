# Evaluations

Numbers for every change to search and AI behavior (spec [09 Evaluation](../docs/spec/09-evaluation.md)).

| Path | What |
|---|---|
| [FilmSearch.Evals](FilmSearch.Evals/) | Runner: a suite over a golden dataset, with the API's configuration and DI registrations |
| [datasets/search.golden.jsonl](datasets/search.golden.jsonl) | Search golden set: queries in en / ru / sv with relevant movies (TMDB ids) |
| [datasets/qu.golden.jsonl](datasets/qu.golden.jsonl) | Query understanding golden set: queries with the expected filters |
| [reports/](reports/) | Committed reports (markdown + raw JSON): the history of quality |

## Run

Needs Docker (each run starts a fresh `pgvector/pgvector:pg17` container, loads the seed and embeds it: about a minute)
and the AI providers of the active profiles (LM Studio with the embedding model; the chat model for `hybrid+qu` and `qu`).

```bash
dotnet run --project evals/FilmSearch.Evals -- --suite validate                     # datasets vs seed, no DB or models (also a unit test in CI)
dotnet run --project evals/FilmSearch.Evals -- --suite search --label baseline      # keyword, semantic, hybrid, hybrid+qu
dotnet run --project evals/FilmSearch.Evals -- --suite search --label rrf-k20 --modes hybrid --set Search:Hybrid:RrfK=20
dotnet run --project evals/FilmSearch.Evals -- --suite qu --label baseline          # --repeat 3 by default
dotnet run --project evals/FilmSearch.Evals -- --help
```

- `--set key=value` overrides any configuration key for the run (search parameters, `AI:...` profiles, prompt version
  `Search:QueryUnderstanding:PromptVersion`). Overrides are listed in the report header.
- `--profile` / `--chat-profile` switch the embedding / chat profile (`AI:EmbeddingProfiles`, `AI:ChatProfiles`).
- `--connection "<connection string>"` runs against an existing database instead of a fresh container (the seed is
  loaded only when the catalog is empty; stale vectors are embedded). Results are comparable only on the same data.
- The report goes to `reports/YYYY-MM-DD_<suite>_<label>.md` with raw results in `.json` next to it. Write the
  "Conclusion" section by hand and commit both files.

## Metrics

Search suite, per case, top 10 of `IMovieSearch.SearchAsync` without UI filters, in the case's locale:

- **Recall@k** = relevant movies in the top k / min(relevant, k). The cap keeps "Christopher Nolan movies" (10
  relevant) comparable with a one-movie plot query.
- **MRR@10**: 1 / rank of the first relevant movie (0 when none is in the top 10).
- **nDCG@10** with graded relevance: gain 2^grade − 1, discount log2(rank + 1); grades 1–3 from `graded`, default 1.
- **Excluded-genre rate**: share of the top 10 with a genre in `excludedGenres` (negative queries).
- **Latency** p50 / p95 of one in-process search call (no HTTP). The first call of each mode is a warm-up.

Means over cases, broken down by locale and tag. Query understanding: valid-answer rate, retry rate, exact match,
accuracy per field, genre precision / recall, latency. The cache is cleared before every call.

## Datasets

One JSON object per line. Search case:

```json
{"id":"ru-plot-02","query":"сын главы мафиозной семьи против своей воли становится новым доном","locale":"ru",
 "relevant":[238,240],"graded":{"238":3,"240":2},"tags":["plot"]}
```

- `relevant`: TMDB ids (stable across databases, unlike catalog GUIDs). `graded` is optional (1–3). `excludedGenres`
  (optional): genre slugs the query rules out.
- Tags: `plot`, `mood`, `person`, `title-typo`, `constraint` (years, decades, a director plus a genre…), `negative`
  ("not about World War II", "no horror"), `cross-language` (a ru / sv query where the words to match exist only in
  another language: the movie has no overview in the query's language, or a transliterated name).
- Spec targets: 90–120 queries, at least 25 per language. Validation (`--suite validate`, unit test
  `DatasetTests`) checks fields, ids against the seed, grades, tags, genres and the per-language minimum.

Query understanding case: `{"id","query","locale","expected":{"genresInclude","genresExclude","yearFrom","yearTo",
"minRating","maxPriceMinor","language","similarToTitles"},"tags"}`. A missing field means "not in the query": a filter
the model invents counts as wrong. `similarToTitles` are written as in the query (the prompt keeps them as written).

**How to change a dataset.** Every case is checked by hand against the seed (`data/seed/movies.seed.json`): the
relevant movies must really answer the query, and the list should contain every movie of the catalog that does
(otherwise a correct result counts as a miss). A changed dataset gets a new version (short SHA-256 in the report
header), so reports before and after the change are not comparable; rerun the baseline.
