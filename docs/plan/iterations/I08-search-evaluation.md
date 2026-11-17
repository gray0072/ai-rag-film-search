# I08 — Search evaluation harness

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I04, I06 (dataset needs only I03) |
| **Parallel with** | I07, I10, I15 |
| **Spec** | [09 Evaluation](../../spec/09-evaluation.md) |
| **Requirements** | NFR-9 |

## Goal
One command produces a report with Recall@k, MRR and nDCG for keyword, semantic and hybrid search, by locale and tag. The first baseline report is committed.

## You will learn
Retrieval metrics, building golden datasets, evaluation-driven development, comparing experiments.

## Tasks
- [ ] `evals/FilmSearch.Evals` console app (shares DI with the API), CLI options `--suite`, `--label`, `--profile`, `--repeat`.
- [ ] Metrics library with unit tests (Recall@k, MRR, nDCG with graded relevance).
- [ ] `evals/datasets/search.golden.jsonl`: 90+ queries, at least 25 per language (en, ru, sv), tags (plot, mood, person, title-typo, constraint, negative, cross-language). Can be drafted with an LLM, **every item reviewed by hand**.
- [ ] Dataset schema validation (also runs in CI).
- [ ] Markdown + JSON report writer with the run header (commit, models, params, dataset version).
- [ ] `qu.golden.jsonl` + QU suite (if I07 is merged; otherwise a follow-up).
- [ ] Baseline report for keyword / semantic / hybrid. Then one experiment (e.g. RRF weights, embedding text template, or a second embedding model) with its own report.

## Acceptance criteria
- [ ] Reports committed in `evals/reports/` with a short conclusion each.
- [ ] Spec 05 §7 targets updated with real baseline numbers.
