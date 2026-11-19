# 09 — Evaluation

> [Docs](../README.md) › [Spec](00-overview.md) › 09 — Evaluation

Evaluation is the main difference between "it seems to work" and AI engineering. Every change to prompts, models, embedding text, or search parameters is checked with numbers.

## 1. Runner

`evals/FilmSearch.Evals` is a console app. It uses the same configuration (`src/FilmSearch.Api/appsettings*.json` + API user secrets) and the same module registrations as the API (catalog, search, AI providers) and writes a markdown report to `evals/reports/YYYY-MM-DD_<suite>_<label>.md` plus a JSON with raw results. Each run starts a fresh `pgvector/pgvector:pg17` container (Testcontainers), applies migrations, loads the seed and embeds it with the active embedding profile, so every run starts from the same data; `--connection` uses an existing database instead.

```
dotnet run --project evals/FilmSearch.Evals -- --suite search --label hybrid-k60 [--profile local] [--modes hybrid] [--set Search:Hybrid:RrfK=60] [--repeat 3]
dotnet run --project evals/FilmSearch.Evals -- --suite qu --label baseline [--chat-profile local]
dotnet run --project evals/FilmSearch.Evals -- --suite assistant --label gemma-v2 [--chat-profile local] [--judge-profile openrouter --judge-model openai/gpt-6-luna] [--parallel 4]
dotnet run --project evals/FilmSearch.Evals -- --suite adversarial --label gemma-v2 [--no-judge] [--cases "adv-en-*"]
dotnet run --project evals/FilmSearch.Evals -- --suite validate
```

`--set key=value` overrides any configuration key for the run (search parameters, profiles, prompt version); `--modes` picks search modes (`keyword`, `semantic`, `hybrid`, `hybrid+qu`). See [evals/README.md](../../evals/README.md).

Reports are committed. They are the history of quality. A report header contains: git commit, chat and embedding profile, model ids, prompt versions, search params, overrides, embedding text template, dataset version (short SHA-256 of the file). The "Conclusion" section is written by hand.

## 2. Datasets (`evals/datasets/`, JSONL)

| Dataset | Content | Size (target) |
|---|---|---|
| `search.golden.jsonl` | `{ id, query, locale, relevant: [tmdbIds], graded?: {tmdbId: 1..3}, excludedGenres?: [slugs], tags: [...] }`. TMDB ids, because catalog GUIDs differ per database. A relevant movie without a grade has grade 1. Tags: `plot`, `mood`, `person` (actor / director), `title-typo`, `constraint` ("before 1990", "Tarantino westerns"), `negative` ("no horror", "not about World War II"), `cross-language` (a ru / sv query whose words exist only in another language: the movie has no overview in the query's language, or a transliterated name). | 90–120 queries, at least 25 per language (en, ru, sv) |
| `qu.golden.jsonl` | `{ id, query, locale, expected: { genresInclude, genresExclude, yearFrom, yearTo, minRating, maxPriceMinor, language, similarToTitles }, tags }` for query understanding. A missing field means "not in the query": an invented filter is wrong. | 40 |
| `assistant.scenarios.jsonl` | `{ id, locale, tags, description?, judge?, setup?: { cart, stoppedMessages }, turns: [{ user, expect? }] }`. Multi-turn scripts: user turns with expectations per turn (tools that must / must not be called, movies that must appear, cards within years / without genres, cart contents and offer, checkout confirmation shown or not, answer language, patterns the answer must / must not match). `judge` tells the LLM judge what a good conversation does. Tag `real` = from a real conversation that went wrong. | 25–40 |
| `assistant.adversarial.jsonl` | Prompt injection, off-topic, requests to "make it free", requests to pay without confirmation, very long input. | 15–25 |

How datasets are built: hand-written first (they are small), then extended with LLM-generated queries **reviewed by a human**, then with real queries from chat logs and thumbs-down feedback.

## 3. Metrics

### Retrieval (search suite)
- Recall@5, Recall@10, MRR@10, nDCG@10 (with graded relevance where given), per case, then the mean over cases.
  - Recall@k = relevant movies in the top k / **min(relevant, k)**: a query with 10 relevant movies is not capped at 0.5 for k = 5.
  - MRR@10: 1 / rank of the first relevant movie, 0 when none is in the top 10.
  - nDCG@10: gain 2^grade − 1, discount log2(rank + 1), divided by the DCG of the ideal order.
- Excluded-genre rate: share of the top 10 with a genre from `excludedGenres` (negative queries).
- Breakdown by `locale` and by `tags`.
- Compared across modes: keyword, semantic, hybrid, hybrid + QU, (rerank).
- Latency p50 / p95 of one in-process `IMovieSearch` call (no HTTP); the first call of a mode is a warm-up.

### Query understanding
- Per-field accuracy (genres include / exclude as sets, years, rating, price, language exact, similar titles by containment after a crude stem), exact match of all fields, genre precision / recall, JSON validity rate (no fallback), retry rate (from the `filmsearch.qu.attempts` span tag), latency. The QU cache is cleared before every call.

### Assistant
- The runner plays every case through `IAssistantChat` (no HTTP) as a new owner, one DI scope per turn, and records events, cart and orders after each turn, and the turn span (I14).
- **Deterministic checks** per turn: tool-call expectations, grounding (the product's `GroundingCheck`: no bold or quoted title outside the tool results, user messages and system prompt), language of the answer (a crude guess without titles), checkout requires confirmation (`confirmation.required` shown or not; no order is ever created by the assistant), cart contents, cards within the constraints, iteration limit and model calls, tokens and latency. A case passes when every check of every turn passes; **pass rate** per repeat, mean ± standard deviation.
- **Grounding violation rate:** turns that name a movie no tool returned / turns that name movies.
- **LLM-as-judge:** rubric 1–5 for relevance of recommendations, quality of explanations, tone, following the user's constraints (`evals/FilmSearch.Evals/Prompts/assistant-judge.vN.md`, structured output, temperature 0, one retry). The judge prompt is versioned. The judge model should be stronger than or different from the assistant model (e.g. through OpenRouter: `--judge-profile openrouter --judge-model <id>`); the local model is acceptable for learning, with this limitation noted in the report.
- **User simulator** (stretch): an LLM plays the customer from a scenario description for multi-turn tests.
- Adversarial pass rate.

## 4. Rules
- Search and QU suites run against a fixed seed dataset, so results are comparable.
- LLM-based suites run with `temperature = 0` where possible and `--repeat N` (default 3) to see variance.
- A PR that changes AI behavior includes the before / after report link in its description.
- CI runs only cheap deterministic parts (dataset schema validation and metric code: unit tests in `FilmSearch.UnitTests/Evals`; locally also `--suite validate`). Full suites run locally because they need LM Studio. A self-hosted runner or OpenRouter key in CI is a stretch goal.

## 5. Online signals
- Thumbs up / down per assistant message (FR-AST-9).
- Grounding violations, tool errors, iteration limit hits, JSON retries: as metrics in OpenTelemetry.
- A weekly manual review of thumbs-down conversations feeds new dataset cases.

---

**Previous:** [08 — Data ingestion](08-data-ingestion.md) ·
**Next:** [10 — Operations and security](10-ops-security.md) ·
**See also:** [Docs hub](../README.md) · [ADRs](../adr/README.md) · [Guide](../guide/README.md)
