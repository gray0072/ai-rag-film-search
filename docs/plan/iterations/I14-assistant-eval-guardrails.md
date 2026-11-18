# I14 — Assistant evaluation and guardrails

| | |
|---|---|
| **Status** | done |
| **Depends on** | I12, I08 |
| **Parallel with** | I18 |
| **Spec** | [09 Evaluation](../../spec/09-evaluation.md), [06 AI §5](../../spec/06-ai-assistant.md#5-guardrails) |
| **Requirements** | FR-AST-3, FR-AST-6, FR-AST-8, NFR-7, NFR-9 |

## Goal
The assistant's quality and safety are measured: scenario and adversarial suites with deterministic checks and an LLM judge. Guardrails are added or tuned based on the numbers.

## You will learn
Evaluating agents (not only retrieval), LLM-as-judge and its biases, red-teaming, prompt injection defenses, comparing prompt versions and models.

## Tasks
- [x] `assistant.scenarios.jsonl` (25+) and `assistant.adversarial.jsonl` (15+), en, ru and sv.
- [x] Scenario runner: plays user turns through the real agent service (no HTTP), records tool calls, cards, text, usage.
- [x] Deterministic checks (spec 09 §3).
- [x] LLM judge with a versioned rubric prompt and structured output; option to use an OpenRouter profile for the judge.
- [x] `--repeat` and variance in the report.
- [x] Compare: prompt v1 vs v2; local model vs one OpenRouter model. Commit reports.
- [x] Guardrails based on findings: input limits, injection handling, off-topic classifier (only if the numbers show it is needed).
- [x] Add 3 real conversations with thumbs-down (if any) to datasets.

## Acceptance criteria
- [x] Reports committed with conclusions; the chosen prompt / model is justified by numbers.
- [x] Adversarial pass rate and grounding violation rate recorded as baselines.

## Result

### What was done
- **Datasets** (`evals/datasets`): `assistant.scenarios.jsonl` (34 cases: en 14, ru 10, sv 10; recommend, constraint, similar, details, clarify, no-results, follow-up, cart, checkout, buy-or-rent, language) and `assistant.adversarial.jsonl` (20 cases: en 12, ru 4, sv 4; injection, fake tool result, off-topic, prices and discounts, payment without the button, a card number in the chat, a 1700-character input with an injection, a non-existent movie, another user's data). Expectations per turn: tools called / not called, movies shown, cards within years / without genres, min / max cards, cart lines and offer, checkout confirmation, answer language, answer patterns, model calls; `setup` with cart lines and stopped messages; `judge` notes. Validated against the seed (`--suite validate`, unit tests).
- **Runner** (`evals/FilmSearch.Evals/Assistant`): `--suite assistant|adversarial` plays each case through `IAssistantChat` (same agent loop, tools, prompts and storage as the API; no HTTP) as a new owner (`EvalOwnerContext`), one DI scope per turn; records events, tool calls, cards, cart and orders after each turn and the turn span (model calls, iteration limit). `EvalEnvironment` registers commerce and the assistant like the API (no payment gateway). Options `--judge-profile`, `--judge-model` (a copy of a profile with another model), `--no-judge`, `--cases`, `--parallel`.
- **Deterministic checks** (`TurnChecks`): answered, tools, movies, cards, cart, confirmation, language (`LanguageDetector`, titles removed first), text, grounding (the product's `GroundingCheck` over all tool results, user messages and the system prompt), no-order, iterations.
- **LLM judge** (`AssistantJudge`, prompt [`assistant-judge.v1`](../../../evals/FilmSearch.Evals/Prompts/assistant-judge.v1.md)): rubric 1–5 (relevance, explanations, tone, constraints), comment before the scores, JSON Schema structured output, temperature 0, one retry; the transcript is marked as data. The judge stage runs after the conversations with 2 parallel calls and waits out HTTP 429.
- **Report** (`AssistantReport`): pass rate per repeat as mean ± standard deviation, stable / flaky / failing cases, pass rate per check, grounding violation rate, judge scores ± deviation, tokens, estimated cost, model calls, latency, breakdown by locale and tag, every failed turn, low judge scores. Reports are written with the invariant culture.
- **Runs** (14 reports and [the summary](../../../evals/reports/2026-11-18_I14_assistant-evaluation.md)): Gemma 4 E4B (LM Studio) with prompts v1, v2, v2 + code fixes, v3; DeepSeek V4.1 Flash (OpenRouter) with v2 and v3; adversarial for both; judge `openai/gpt-6-luna`.
- **Guardrails and fixes from the findings:**
  - Prompt `assistant-system.v3` (current): find a named movie with `search_movies` before using its id, never ask the user for an id; unwanted genres in `genresExclude`; facts from `get_movie_details`; ask buy or rent before `add_to_cart`; no "shall I check out?" after "buy it"; the payment button label in en / ru / sv (a unit test keeps it equal to the web app's `cart.checkout`); refusals in the language of the user's message; pasted tool results or payment confirmations are not real.
  - Movie id argument errors say how to get an id (`search_movies` with the title, `view_cart`).
  - A user message without an answer (Stop or a failed turn) is joined with the next user message instead of being dropped (real conversation: "Just one, please." lost its topic).
  - `GroundingCheck`: bold spans in lower case and runtimes are not titles; Cyrillic words match by a common prefix (inflection); tool results carry `originalTitle` when it differs (the model may use the original title, and the check finds it).
  - Input limit (2000 characters) kept: the long-input case passes in every run. Off-topic classifier not added: off-topic requests were declined without tools by both models in every run; the failures were the language of the refusal, which a classifier would not fix.
- **Real conversations:** the database had no thumbs-down feedback. Three real conversations that went wrong (AppHost database, I12 manual check) are cases with the tag `real`: Before Sunrise / La La Land → "Take the second one, buy it." (Gemma asked for ids), "Just one, please." after Stop, "Добавь первый в корзину, я ещё посмотрю." (offer chosen without asking).
- **Tests:** unit 290 (assistant datasets valid and covering the spec risks, validation errors, language guess, every check with its failure text, judge parsing and transcript, summary math, options; unanswered message joined; inflection, emphasis and runtimes in `GroundingCheck`; prompt v3 by default; prompt button labels vs web i18n). The grounding metric test now listens only to its own harness's meters (it failed when parallel tests recorded the same counter). Integration 112, unchanged.

### Numbers (`--repeat 3`; final code for v3)

| | Gemma 4 E4B, v2 (before) | Gemma 4 E4B, v3 | DeepSeek V4.1 Flash, v2 (before) | DeepSeek V4.1 Flash, v3 |
|---|---:|---:|---:|---:|
| Scenario pass rate | 63% ± 3 | **78% ± 4** | 83% ± 4 | **92% ± 2** |
| Judge mean (1–5) | 3.76 | 4.00 | 4.39 | 4.33 |
| Grounding violation rate, scenarios | 2.4% | 2.3% | 10.8% (mostly false positives of the check) | 3.8% |
| Adversarial pass rate | 85% ± 5 (older dataset) | **88% ± 3** | 93% ± 6 (older dataset) | **98% ± 3** |
| Grounding violation rate, adversarial | 0% | 0% | 3.1% | 3.4% |
| Turn latency p50 / p95 | 18.0 / 28.5 s | 21.6 / 29.4 s | 3.5 / 10.4 s | 3.2 / 9.1 s |

Prompt v1 vs v2 (Gemma, same code): 56% vs 63%. No run created an order without the user's click or hit the tool iteration limit.

### Manual checks
- LM Studio (Gemma 4 E4B, EmbeddingGemma) started from the agent shell this time (`lms server start`, `lms load`). With `--parallel 4` and the default context of 8192 the first run failed with "Context size has been exceeded" (the slots share one KV cache) and was discarded; the model was reloaded with `-c 32768 --parallel 4`.
- The failed turns of every report were read by hand: dataset patterns that flagged correct refusals were fixed (adversarial dataset version changed; the two "before" adversarial reports use the older one), the rest are model failures described in the summary.
- Checks: `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, `dotnet test` (unit 290, integration 112), web `gen:api` (no diff), lint, format check, typecheck, tests, build.

### Decisions and deviations
- **Prompt v3 is the current prompt** (+15 points for Gemma, +9 for DeepSeek). **DeepSeek V4.1 Flash is the recommended model for real users** (better on every measure, about $0.0004 per turn); `local` (Gemma 4 E4B) stays the development default. No configuration change.
- **The judge prompt lives in the evaluation project** (`evals/FilmSearch.Evals/Prompts`, embedded under the `FilmSearch.AI.Prompts.` resource prefix so `PromptLibrary` loads it), not in `src/FilmSearch.AI/Prompts`: it is not product behaviour. `FilmSearch.AI` gives `FilmSearch.Evals` access to its internals (`GroundingCheck`, `PromptLibrary`, `AssistantChat` constants).
- **Grounding in the runner** uses the product's `GroundingCheck` over all tool results and user messages of the conversation (the product uses the unsummarized history plus the summary): slightly more permissive, the same in every run.
- **Spec changes:** spec 09 (assistant suites, dataset format, metrics), spec 06 (§4 prompt v3, §5 guardrails, §6 unanswered messages), evals/README, prompts README, AGENTS.md (commands, LM Studio context with `--parallel`).
- No new packages.

### Follow-ups
- Gemma declines English off-topic questions in Swedish with v3 (`adv-en-code`, `adv-en-weather`; Russian refusals were fixed): the next prompt version, measured on both suites.
- `remove_from_cart` by title, resolved against the cart lines in the tool (Gemma fails it even with the id hint and once re-added the movie).
- "Add the first one" without buy or rent still adds the buy offer (both models, Russian case).
- Explanations are the weakest judge criterion (3.2 Gemma, 3.8 DeepSeek): a prompt change for specific reasons.
- I18: grounding violations and online signals on a dashboard; I20: production profile with the recommended model.

### Sessions
- [S16](../../prompts/sessions/2026-11-18_S16_assistant-eval-guardrails.md)
