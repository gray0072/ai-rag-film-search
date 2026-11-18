# Evaluations

Numbers for every change to search and AI behavior (spec [09 Evaluation](../docs/spec/09-evaluation.md)).

| Path | What |
|---|---|
| [FilmSearch.Evals](FilmSearch.Evals/) | Runner: a suite over a golden dataset, with the API's configuration and DI registrations |
| [datasets/search.golden.jsonl](datasets/search.golden.jsonl) | Search golden set: queries in en / ru / sv with relevant movies (TMDB ids) |
| [datasets/qu.golden.jsonl](datasets/qu.golden.jsonl) | Query understanding golden set: queries with the expected filters |
| [datasets/assistant.scenarios.jsonl](datasets/assistant.scenarios.jsonl) | Assistant scenarios: multi-turn conversations with expectations per turn (I14) |
| [datasets/assistant.adversarial.jsonl](datasets/assistant.adversarial.jsonl) | Assistant adversarial set: injection, off-topic, prices, payment without the button, long input (I14) |
| [FilmSearch.Evals/Prompts](FilmSearch.Evals/Prompts/) | Versioned rubric prompt of the LLM judge (`assistant-judge.vN.md`) |
| [reports/](reports/) | Committed reports (markdown + raw JSON): the history of quality |

## Run

Needs Docker (each run starts a fresh `pgvector/pgvector:pg17` container, loads the seed and embeds it: about a minute)
and the AI providers of the active profiles (LM Studio with the embedding model; the chat model for `hybrid+qu` and `qu`).

```bash
dotnet run --project evals/FilmSearch.Evals -- --suite validate                     # datasets vs seed, no DB or models (also a unit test in CI)
dotnet run --project evals/FilmSearch.Evals -- --suite search --label baseline      # keyword, semantic, hybrid, hybrid+qu
dotnet run --project evals/FilmSearch.Evals -- --suite search --label rrf-k20 --modes hybrid --set Search:Hybrid:RrfK=20
dotnet run --project evals/FilmSearch.Evals -- --suite qu --label baseline          # --repeat 3 by default
dotnet run --project evals/FilmSearch.Evals -- --suite assistant --label gemma-e4b-v2 --parallel 4 --judge-profile openrouter --judge-model openai/gpt-6-luna
dotnet run --project evals/FilmSearch.Evals -- --suite adversarial --label gemma-e4b-v2 --parallel 4 --no-judge
dotnet run --project evals/FilmSearch.Evals -- --suite assistant --label v1 --set Assistant:PromptVersion=1 --cases "en-*,ru-space"
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

### Assistant suites (I14)

`assistant` and `adversarial` play each case through the real `IAssistantChat` (agent loop, tools, prompts, storage;
no HTTP) as a new owner, one DI scope per turn like an HTTP request, `--repeat 3` by default. After each turn the runner
records the events (text, tool calls, cards, `cart.updated`, `confirmation.required`, usage), the cart and the orders
of the owner, and the turn span (model calls, iteration limit). Then:

- **Deterministic checks** per turn (spec 09 §3). Every answer: `answered` (no error event, not empty), `language`
  (a crude guess without the bold / quoted titles; `en`, `ru`, `sv`), `grounding` (the product's `GroundingCheck`
  against every tool result and user message of the conversation and the system prompt), `no-order` (the assistant
  alone never creates an order), `iterations` (no tool iteration limit). From the case: `tools` (called / not called,
  `*` = none), `movies` (one of these TMDB ids shown), `cards` (min / max count, years, excluded genres of the shown
  movies), `cart` (lines, offer, count, unchanged), `confirmation` (checkout button shown or not), `text` (regular
  expressions the answer must / must not match).
- **LLM judge** per case run: rubric 1–5 for relevance, explanations, tone, constraints
  ([`assistant-judge.v1`](FilmSearch.Evals/Prompts/assistant-judge.v1.md)), structured output, temperature 0, one retry.
  The judge should be another, stronger model than the assistant: `--judge-profile openrouter --judge-model <id>`
  copies a profile (endpoint and key) with another model. `--no-judge` skips it.
- **Report**: pass rate (share of cases with every check passed) per repeat as mean ± standard deviation, stable /
  flaky / failing cases, pass rate per check, **grounding violation rate** (turns naming a movie no tool returned /
  turns naming movies), judge scores ± deviation, tokens, estimated cost, model calls, latency; breakdown by locale and
  tag; every failed turn with the check details, tools, cart and the answer.
- `--parallel <n>` plays n case runs at once (LM Studio serves 4 parallel requests by default, cloud providers more).
  `--cases` picks cases by id or prefix (`en-*`). Assistant options can be changed with `--set`, e.g.
  `--set Assistant:PromptVersion=1`.

Spend: an OpenRouter run of the scenarios with `--repeat 3` is about 300 model calls of the assistant plus 100 of the
judge, a few cents with the models of `AI:Pricing`.

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

Assistant case (both assistant files):

```json
{"id":"en-ask-buy-or-rent","locale":"en","tags":["buy-or-rent","checkout"],
 "description":"…","judge":"what a good conversation does (given to the judge)",
 "setup":{"cart":[{"tmdbId":105,"offer":"buy"}],"stoppedMessages":["…"]},
 "turns":[{"user":"I'll take the first one.","expect":{"toolsNotCalled":["add_to_cart","request_checkout"],"answerMatches":["\bbuy","\brent"]}}]}
```

- `expect` fields: `toolsCalled`, `toolsNotCalled`, `moviesAnyOf`, `cardsExcludeGenres`, `cardsYearFrom`,
  `cardsYearTo`, `minCards`, `maxCards`, `cart` (`contains`, `notContains`, `offer`, `count`, `unchanged`),
  `confirmation`, `language` (default: the case locale; `any`), `answerMatches`, `answerNotMatches`, `maxModelCalls`.
  Write expectations that any good answer meets (a set of acceptable movies, not one exact list); leave the rest to
  the judge.
- `setup.stoppedMessages` are user messages already in the conversation whose answers were stopped (what the API
  stores after Stop): the next turn sees the same history as in production.
- Tags: `recommend`, `constraint`, `similar`, `details`, `clarify`, `no-results`, `follow-up`, `cart`, `checkout`,
  `buy-or-rent`, `language`, `real` (from a real conversation that went wrong); adversarial: `injection`, `off-topic`,
  `price`, `payment`, `long-input`, `hallucination`, `privacy`.
- Spec targets: 25–40 scenarios, 15–25 adversarial cases, every locale covered. Real conversations with thumbs-down
  (`assistant.message_feedback.rating = -1`) are added as cases with the tag `real`.

**How to change a dataset.** Every case is checked by hand against the seed (`data/seed/movies.seed.json`): the
relevant movies must really answer the query, and the list should contain every movie of the catalog that does
(otherwise a correct result counts as a miss). A changed dataset gets a new version (short SHA-256 in the report
header), so reports before and after the change are not comparable; rerun the baseline.
