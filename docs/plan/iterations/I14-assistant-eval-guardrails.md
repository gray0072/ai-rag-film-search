# I14 — Assistant evaluation and guardrails

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I12, I08 |
| **Parallel with** | I18 |
| **Branch** | `iter/I14-assistant-eval` |
| **Spec** | [09 Evaluation](../../spec/09-evaluation.md), [06 AI §5](../../spec/06-ai-assistant.md#5-guardrails) |
| **Requirements** | FR-AST-3, FR-AST-6, FR-AST-8, NFR-7, NFR-9 |

## Goal
The assistant's quality and safety are measured: scenario and adversarial suites with deterministic checks and an LLM judge. Guardrails are added or tuned based on the numbers.

## You will learn
Evaluating agents (not only retrieval), LLM-as-judge and its biases, red-teaming, prompt injection defenses, comparing prompt versions and models.

## Tasks
- [ ] `assistant.scenarios.jsonl` (25+) and `assistant.adversarial.jsonl` (15+), en and ru.
- [ ] Scenario runner: plays user turns through the real agent service (no HTTP), records tool calls, cards, text, usage.
- [ ] Deterministic checks (spec 09 §3).
- [ ] LLM judge with a versioned rubric prompt and structured output; option to use an OpenRouter profile for the judge.
- [ ] `--repeat` and variance in the report.
- [ ] Compare: prompt v1 vs v2; local model vs one OpenRouter model. Commit reports.
- [ ] Guardrails based on findings: input limits, injection handling, off-topic classifier (only if the numbers show it is needed).
- [ ] Add 3 real conversations with thumbs-down (if any) to datasets.

## Acceptance criteria
- [ ] Reports committed with conclusions; the chosen prompt / model is justified by numbers.
- [ ] Adversarial pass rate and grounding violation rate recorded as baselines.
