# 09 — Evaluation

Evaluation is the main difference between "it seems to work" and AI engineering. Every change to prompts, models, embedding text, or search parameters is checked with numbers.

## 1. Runner

`evals/FilmSearch.Evals` is a console app. It uses the same DI setup as the API (real DB with seeded data, real AI profile) and writes a markdown report to `evals/reports/YYYY-MM-DD_<suite>_<label>.md` plus a JSON with raw results.

```
dotnet run --project evals/FilmSearch.Evals -- --suite search --label hybrid-k60 [--profile local]
```

Reports are committed. They are the history of quality. A report header contains: git commit, chat and embedding profile, model ids, prompt versions, search params, dataset version.

## 2. Datasets (`evals/datasets/`, JSONL)

| Dataset | Content | Size (target) |
|---|---|---|
| `search.golden.jsonl` | `{ id, query, locale, relevant: [movieIds or tmdbIds], graded?: {id: 0..3}, tags: [...] }`. Query types: by plot, by mood, by actor/director, by title with typo, with constraints ("before 1990"), negative ("no horror"), cross-language ru → en and sv → en. | 90–120 queries, at least 25 per language (en, ru, sv) |
| `qu.golden.jsonl` | `{ query, expected: { filters..., semanticQuery? } }` for query understanding. | 40 |
| `assistant.scenarios.jsonl` | Multi-turn scripts: user turns, expectations (tools that must / must not be called, movies that must appear, must end with checkout confirmation, must answer in ru or sv…). | 25–40 |
| `assistant.adversarial.jsonl` | Prompt injection, off-topic, requests to "make it free", requests to pay without confirmation, very long input. | 15–25 |

How datasets are built: hand-written first (they are small), then extended with LLM-generated queries **reviewed by a human**, then with real queries from chat logs and thumbs-down feedback.

## 3. Metrics

### Retrieval (search suite)
- Recall@5, Recall@10, MRR, nDCG@10 (with graded relevance where given).
- Breakdown by `locale` and by `tags`.
- Compared across modes: keyword, semantic, hybrid, hybrid + QU, (rerank).
- Latency p50 / p95.

### Query understanding
- Per-field accuracy (genres precision/recall, year range exact, rating exact), JSON validity rate, retry rate.

### Assistant
- **Deterministic checks:** tool-call expectations, grounding (no movies outside tool results), language of the answer, checkout requires confirmation, number of iterations, tokens and latency.
- **LLM-as-judge:** rubric 1–5 for relevance of recommendations, quality of explanations, tone, following the user's constraints. The judge prompt is versioned. The judge model should be stronger than or different from the assistant model (e.g. through OpenRouter); the local model is acceptable for learning, with this limitation noted in the report.
- **User simulator** (stretch): an LLM plays the customer from a scenario description for multi-turn tests.
- Adversarial pass rate.

## 4. Rules
- Search and QU suites run against a fixed seed dataset, so results are comparable.
- LLM-based suites run with `temperature = 0` where possible and `--repeat N` (default 3) to see variance.
- A PR that changes AI behavior includes the before / after report link in its description.
- CI runs only cheap deterministic parts (dataset schema validation, metric code unit tests). Full suites run locally because they need LM Studio. A self-hosted runner or OpenRouter key in CI is a stretch goal.

## 5. Online signals
- Thumbs up / down per assistant message (FR-AST-9).
- Grounding violations, tool errors, iteration limit hits, JSON retries: as metrics in OpenTelemetry.
- A weekly manual review of thumbs-down conversations feeds new dataset cases.
