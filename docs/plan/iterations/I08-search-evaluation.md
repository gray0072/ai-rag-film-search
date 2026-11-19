# I08 — Search evaluation harness

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I08 · ← [I07](I07-hybrid-query-understanding.md) · [I09](I09-cart-orders-fake-payment.md) → · [Journal: what happened](../../journal/m2-smart-search.md#i08--search-evaluation-harness)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I04, I06 (dataset needs only I03) |
| **Parallel with** | I07, I10, I15 |
| **Spec** | [09 Evaluation](../../spec/09-evaluation.md) |
| **Requirements** | NFR-9 |

## Goal
One command produces a report with Recall@k, MRR and nDCG for keyword, semantic and hybrid search, by locale and tag. The first baseline report is committed.

## You will learn
Retrieval metrics, building golden datasets, evaluation-driven development, comparing experiments.

## Tasks
- [x] `evals/FilmSearch.Evals` console app (shares DI with the API), CLI options `--suite`, `--label`, `--profile`, `--repeat`.
- [x] Metrics library with unit tests (Recall@k, MRR, nDCG with graded relevance).
- [x] `evals/datasets/search.golden.jsonl`: 90+ queries, at least 25 per language (en, ru, sv), tags (plot, mood, person, title-typo, constraint, negative, cross-language). Can be drafted with an LLM, **every item reviewed by hand**.
- [x] Dataset schema validation (also runs in CI).
- [x] Markdown + JSON report writer with the run header (commit, models, params, dataset version).
- [x] `qu.golden.jsonl` + QU suite (if I07 is merged; otherwise a follow-up).
- [x] Baseline report for keyword / semantic / hybrid. Then one experiment (e.g. RRF weights, embedding text template, or a second embedding model) with its own report.

## Acceptance criteria
- [x] Reports committed in `evals/reports/` with a short conclusion each.
- [x] Spec 05 §7 targets updated with real baseline numbers.

## Result

### What was done
- **Runner `evals/FilmSearch.Evals`** (console app in the solution): the API's configuration (`src/FilmSearch.Api/appsettings*.json` + API user secrets) and the same registrations as `Program.cs` (`AddDatabase`, `AddAiProviders`, `AddCatalogInfrastructure`, `AddSearchInfrastructure`); the host is built but not started, so no background jobs run. Each run starts a fresh `pgvector/pgvector:pg17` container (Testcontainers), migrates, loads the seed and embeds it with the active profile (≈ 45 s), so runs are comparable; `--connection` uses an existing database. Suites: `search` (modes `keyword`, `semantic`, `hybrid`, `hybrid+qu` through `IMovieSearch`), `qu` (through `IQueryUnderstanding`, cache cleared before every call, attempts read from the `filmsearch.qu.attempts` span tag), `validate`. Options `--suite`, `--label`, `--modes`, `--profile`, `--chat-profile`, `--repeat` (1 for search, 3 for qu), `--set key=value` (any configuration override), `--connection`, `--dataset`, `--out`.
- **Metrics** (`RetrievalMetrics`): Recall@k capped by min(relevant, k), MRR@10, nDCG@10 with graded gain 2^grade − 1, percentiles; plus the excluded-genre rate for negative queries, latency p50 / p95 (the first call of a mode is a warm-up), degraded and QU-fallback counts. QU: valid rate, retry rate, exact match, per-field accuracy, genre precision / recall, latency.
- **Datasets**: `search.golden.jsonl` 105 queries (35 per language; tags plot 46, person 22, title-typo 15, cross-language 15, mood 11, constraint 10, negative 9), relevant movies as TMDB ids with grades and `excludedGenres`; `qu.golden.jsonl` 40 queries (14 en, 14 ru, 12 sv). Drafted by the agent from the seed and **checked case by case against the seed file** (scripts listing every movie of a director or actor, every war movie, etc.); after the first dry run, the relevant lists of 7 mood / negative cases were completed with catalog movies that also answer the query. The owner has not reviewed the cases yet.
- **Validation** (`DatasetLoader`): unknown or misspelled fields, ids, locales, relevant ids in the seed, grades 1–3, tags, genre slugs, no relevant movie with an excluded genre, ≥ 25 cases per language; QU fields (genres, year order and range, rating, price, language code). Runs as unit tests in CI (`DatasetTests`) and as `--suite validate`.
- **Reports**: markdown with the run header (commit + dirty flag, command, dataset version = short SHA-256, database, embedding profile / model / prefixes / text template / locales, chat profile / model, prompt versions, search parameters, overrides), summary per mode, breakdowns by locale and tag, misses of every mode, a per-case rank table to diff; JSON with every run. LF line endings.
- **Tests**: 35 new unit tests in 20 methods (metrics math, validation rules, loader strictness, QU field comparison and title matching, CLI parsing, committed datasets are valid).
- Docs: spec 09 §1–§4 (runner, dataset schema, metric definitions, CI), spec 05 §4 (I08 check of the fusion parameters) and §7 (targets), [evals/README.md](../../../evals/README.md), README, AGENTS.md commands.

### Evaluation results
[Search baseline](../../../evals/reports/2026-11-17_search_baseline.md) (105 queries, repeat 3):

| Mode | Recall@10 | MRR@10 | nDCG@10 | Excluded-genre rate | p50 ms |
|---|---|---|---|---|---|
| keyword | 0.296 | 0.251 | 0.233 | 0.100 | 61 |
| semantic | 0.865 | 0.794 | 0.784 | 0.171 | 58 |
| hybrid (default) | 0.873 | 0.788 | 0.786 | 0.171 | 78 |
| hybrid + QU | 0.862 | 0.820 | 0.805 | 0.000 | 3865 |

Hybrid per locale (Recall@10 / MRR@10): en 0.914 / 0.808, ru 0.871 / 0.833, sv 0.834 / 0.721. The initial targets (≥ 0.8 / ≥ 0.6) are met; spec 05 §7 raised to ≥ 0.85 / ≥ 0.75 (sv ≥ 0.80 / ≥ 0.70).

Experiments (against the baseline, one parameter each):

| Report | Change | Hybrid MRR@10 | Finding |
|---|---|---|---|
| [hybrid-rrf-k60](../../../evals/reports/2026-11-17_search_hybrid-rrf-k60.md) | `RrfK` 20 → 60 | 0.788 → 0.732 | clearly worse; confirms k = 20 from I07 |
| [hybrid-rrf-k10](../../../evals/reports/2026-11-17_search_hybrid-rrf-k10.md) | `RrfK` 20 → 10 | 0.787 | en up (0.851), ru down (0.788) |
| [hybrid-keyword-weight-0.5](../../../evals/reports/2026-11-17_search_hybrid-keyword-weight-0.5.md) | `KeywordWeight` 1.0 → 0.5 | 0.792 | en only |
| [hybrid-keyword-weight-0.3](../../../evals/reports/2026-11-17_search_hybrid-keyword-weight-0.3.md) | `KeywordWeight` 1.0 → 0.3 | 0.797 | nothing worse, en 0.837; not adopted (see decisions) |
| [vectors-en-ru-sv](../../../evals/reports/2026-11-17_search_vectors-en-ru-sv.md) | `Search:Embeddings:Locales` en → en, ru, sv | 0.792 (semantic 0.794 → 0.809) | title typos up, cross-language down, 3× vectors; default stays en |

[QU baseline](../../../evals/reports/2026-11-17_qu_baseline.md) (prompt v2, Gemma 4 E4B, 40 × 3): valid 100%, retries 0%, exact match 83%, genres include P / R 0.976 / 0.922; excluded genres, rating, price and similar titles 100%. Errors: year rules ("efter 2000" → 2011, "до 1970" → 1970, "70-talet" without its start), "anime" gives no `animation`, "советские" gives no language; 3.8 s p50.

### Decisions and deviations
- **No default changed.** `KeywordWeight` 0.3 is the best fusion setting of the sweep, but the gain (+0.009 MRR, about one query) is within what a dataset change can move, and the set has no exact-title queries, the case I07 kept the full weight for. Recorded as a follow-up with a rerun.
- **Fresh database per run** (Testcontainers in the runner) instead of the AppHost database: the AppHost database may hold hand-added movies and other models' vectors, and a run that starts from the seed is reproducible. Costs ≈ 45 s per run (seed + 1000 embeddings). `Testcontainers.PostgreSql` was already a central package (integration tests); no new package.
- **Recall@k is capped by min(relevant, k)** (spec 09 §3): person queries have up to 10 relevant movies, and plain recall would cap them at 0.5 for k = 5.
- **Dataset schema extended** with `excludedGenres` (negative queries: share of results with a ruled-out genre); relevant movies are TMDB ids only (catalog GUIDs differ per database). Spec 09 §2 updated.
- **QU title matching** compares after a crude stem (last letter of long words dropped): prompt v2 says "as written", but the model returns "Матрица" for "похожие на Матрицу".
- All reports of this iteration show commit `836f94e+dirty`: they were made before the runner was committed.

### Follow-ups
- Golden set: owner review of the cases; exact-title queries (tag `title`) and a rerun of `KeywordWeight` 0.3 vs 1.0; more Swedish negative and constraint cases; real queries from chat logs (I13).
- QU prompt v3: year rules with examples that do not share numbers with real queries, "anime" → animation, country adjectives → language; before / after with `--suite qu` and `--modes hybrid+qu`. A stronger or faster QU model (`Search:QueryUnderstanding:ChatProfile`, OpenRouter).
- I19: fuzzy (trigram) title matching for typos (`godfahter`, `гари потер`, `gudfadren` miss in every mode); QU latency.
- Swedish is still the weakest language (`sv-plot-02`, "dokusåpa" → The Truman Show, missed by every mode): try the embedding text without the `Original title:` line (I06 hypothesis) as a new template version with its own report.
- I14 reuses the runner, the report header and the repeat logic for the assistant suites.

### Sessions
- [S09](../../prompts/sessions/2026-11-17_S09_search-evaluation.md)
