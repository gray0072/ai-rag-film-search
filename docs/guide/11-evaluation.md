# 11. Evaluation

> [Docs](../README.md) › [Guide](README.md) › 11. Evaluation

Evaluation is what turns "it seems to work" into AI engineering. Every number in this guide comes from a repeatable run
of a fixed dataset against a fixed catalog, with a committed report. This chapter covers golden sets, retrieval metrics,
checks for an agent, an LLM as a judge, variance, and the habit that mattered most: reading the failures.

## The business problem

A prompt change that fixes one conversation breaks two others. A new embedding model is "better" on the five queries
you tried. A search parameter from a paper is wrong for your data ([ch. 7](07-hybrid-search.md)). Without a measurement
you cannot tell a regression from an improvement, and you cannot answer the product questions: which model, is the
4-second LLM step worth it, is the assistant safe to put in front of customers?

## Theory

### Golden sets

A **golden set** is a list of inputs with the expected outcome, written (and reviewed) by people. Small is fine if it
covers the kinds of queries that matter. FilmSearch's sets ([evals/datasets](../../evals/datasets/)):

| Dataset | Size | Labels |
|---|---|---|
| `search.golden.jsonl` | 105 queries, 35 per language | relevant TMDB ids (graded 1–3), excluded genres; tags `plot`, `mood`, `person`, `title-typo`, `constraint`, `negative`, `cross-language` |
| `qu.golden.jsonl` | 40 queries | expected filters; a filter not in the query must stay empty |
| `assistant.scenarios.jsonl` | 34 multi-turn conversations | per turn: tools that must / must not be called, cards within constraints, cart contents, checkout shown or not, language, answer patterns |
| `assistant.adversarial.jsonl` | 20 cases | injection, fake tool results, off-topic, "make it free", "pay without the button", a card number, a 1700-character input, another user's data |

Golden sets reference **TMDB ids**, not database GUIDs, so they work against any database. A unit test validates them
against the seed in CI.

### Retrieval metrics

For one query with relevant movies *R* and a ranked result list:

| Metric | Formula | Answers |
|---|---|---|
| **Recall@k** | relevant in the top k / **min(\|R\|, k)** | Did we find them? (min: a director with 10 movies is not capped at 0.5 for k = 5) |
| **MRR@10** | 1 / rank of the first relevant, 0 if none in the top 10 | How high is the first good result? |
| **nDCG@10** | DCG / ideal DCG, gain 2^grade − 1, discount log2(rank + 1) | Is the order good, with graded relevance? |
| **Excluded-genre rate** | share of the top 10 with a ruled-out genre | Are "no horror" requests respected? |

Report means per language and per tag, not only overall: an average hides that Swedish is weaker.

### Evaluating an agent

An agent's output is a conversation, not a list. Two kinds of checks:

1. **Deterministic checks** per turn: which tools were called, with what; which cards were shown; what is in the cart;
   was a checkout confirmation shown; **was an order created without a click** (must never happen); answer language;
   grounding. Cheap, exact, and they decide pass or fail.
2. **LLM as a judge**: a different, stronger model scores the conversation on a rubric (relevance, explanations, tone,
   constraints), 1–5, with structured output. It catches what patterns cannot ("the reasons are generic") but has
   biases: it favours longer, confident answers and its own model family. So it **explains**; it does not decide.

### Variance

Models are not deterministic even at temperature 0 (batching, parallel slots). Every LLM suite runs each case three
times and reports mean ± standard deviation and **stable / flaky / failing** cases. A 3-point change with ± 4 points of
spread is noise.

## What FilmSearch does

### The runner

[`evals/FilmSearch.Evals`](../../evals/README.md) is a console app that uses the API's own configuration and module
registrations. For every run it:

1. starts a **fresh** `pgvector/pgvector:pg17` container (Testcontainers), applies migrations, loads the seed and embeds
   it with the active profile (≈ 45 s);
2. runs the suite in process (search through `IMovieSearch`, the assistant through `IAssistantChat`, each case as a new
   owner);
3. writes `evals/reports/YYYY-MM-DD_<suite>_<label>.md` + `.json` with the commit, dataset version (hash), models,
   prompt versions, parameters and overrides in the header; the conclusion is written by hand.

```bash
dotnet run --project evals/FilmSearch.Evals -- --suite search --label hybrid-k60 --modes hybrid --set Search:Hybrid:RrfK=60
dotnet run --project evals/FilmSearch.Evals -- --suite assistant --label deepseek-v3 --chat-profile openrouter \
    --judge-profile openrouter --judge-model openai/gpt-6-luna --parallel 4
```

`--set` overrides any configuration key for one run, so every experiment is "the default plus one change". Reports
are committed: they are the history of quality ([all reports](../../evals/reports/)).

### Rules

- Changes to AI behavior (prompts, models, embedding text, search parameters) need a before / after report
  ([AGENTS.md](../../AGENTS.md), `NFR-9`).
- CI runs only the deterministic parts: dataset validation and metric code. Full suites need a model and run locally.
- Quality targets per language are set just below the baseline ([spec 05 §7](../spec/05-search.md#7-quality-targets-calibrated-in-i08)):
  dropping a language below its target is a regression to explain.

## Evidence: what evaluations changed

| Finding | From | Change |
|---|---|---|
| RRF k = 60 is worse (MRR 0.788 → 0.732) | [search experiments](../../evals/reports/2026-11-17_search_hybrid-rrf-k60.md) | k stays 20 |
| Per-locale vectors and other weights: within one query | [I08 reports](../journal/m2-smart-search.md#i08--search-evaluation-harness) | **No default changed** (a result too) |
| QU removes ruled-out genres (0.171 → 0.000) for ~4 s | [baseline](../../evals/reports/2026-11-17_search_baseline.md) | A product decision with numbers |
| The prompt's year example was copied ("efter 2000" → 2011) | [QU baseline](../../evals/reports/2026-11-17_qu_baseline.md) | A known prompt issue |
| Prompt v3: Gemma 63% → 78%, DeepSeek 83% → 92% | [I14 summary](../../evals/reports/2026-11-18_I14_assistant-evaluation.md) | v3 is the current prompt |
| DeepSeek beats Gemma on every measure, $0.0004 per turn | Same | DeepSeek for users, Gemma for development |
| Off-topic cases are all declined; the failures are the refusal's *language* | Same | **No** off-topic classifier |
| Qwen3-Embedding-8B: semantic Recall@10 0.87 → 0.92 | [I20 report](../../evals/reports/2026-11-19_search_i20-openrouter-qwen3-embedding-8b.md) | An option for the demo |

### Read the failures

The most useful hour of I14 was reading every failed turn. Half of the first "failures" were not the model's:

- Two answer patterns of the adversarial set flagged *correct* refusals ("I cannot add items **for free**"). The dataset
  got a new version; old reports keep the old hash.
- The grounding check flagged the Russian button label, "**148 minutes**" and inflected titles: 10.8% "violations" for
  DeepSeek, 3.8% after fixing the check.
- The catalog grew by 182 movies in I16 and hybrid MRR "dropped" 0.788 → 0.757. Judged by hand, the new top results
  were relevant movies the golden set did not know (Tom Hanks → *Cast Away*); counted, the numbers were within 0.004
  ([I16 report](../../evals/reports/2026-11-18_I16_catalog-growth.md)).

Averages tell you *that* something moved. Only the cases tell you *why*.

## Pitfalls

- **Evaluating on the dev database.** Hand-added movies and other models' vectors made runs incomparable. Fresh
  container per run.
- **Tuning and testing on the same set.** The 46-query hybrid tuning was optimistic; the golden set is the referee.
- **One run per case.** A single run of an LLM suite cannot separate a fix from luck.
- **A judge from the same family, or the judge deciding.** Self-preference and style bias; keep deterministic checks
  as the gate.
- **Ignoring the infrastructure.** Parallel runs overflowed LM Studio's shared KV cache ("Context size has been
  exceeded": load with `-c 32768 --parallel 4`), and the judge hit OpenRouter's 20 requests per minute. Both looked like
  model failures until read.

---

**Previous:** [10. Agents and tools](10-agents-and-tools.md) · **Next:** [12. Guardrails](12-guardrails.md) ·
**See also:** [Spec 09 Evaluation](../spec/09-evaluation.md) · [Evaluations README](../../evals/README.md) ·
[Journal: I08](../journal/m2-smart-search.md#i08--search-evaluation-harness) · [Journal: I14](../journal/m3-ai-assistant.md#i14--assistant-evaluation-and-guardrails) ·
[Advanced: LLM as a judge](advanced/llm-as-judge.md)
