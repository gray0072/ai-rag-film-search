# LLM as a judge

> [Docs](../../README.md) › [Guide](../README.md) › [Advanced](README.md) › LLM as a judge

Some qualities of an assistant's answer cannot be checked with a regular expression: is the reason for a pick specific,
does the tone fit a shop, did it respect "nothing too violent"? An LLM judge reads the conversation and scores it on a
rubric. This chapter covers how to write the rubric, the judge's known biases, variance and cost, and how FilmSearch's
assistant suites combine a judge with deterministic checks so that the judge explains results but never decides them.

## The question

The I14 scenario set has 34 conversations, run three times per model and prompt version: about 100 conversations per
run, eight scenario runs in one day ([I14 summary](../../../evals/reports/2026-11-18_I14_assistant-evaluation.md)).
Nobody reads 800 conversations. Which parts can a program check exactly, which need a judge, and how far can you trust
the judge?

## Theory

### Three ways to ask a judge

| Style | The judge sees | Good for | Weakness |
|---|---|---|---|
| **Pointwise** (rubric) | one answer, scores it 1–5 or pass / fail per criterion | tracking one system over time | scores drift with the judge's mood and style |
| **Pairwise** | two answers, picks the better | comparing two prompts or models | position bias; no absolute level |
| **Reference-based** | an answer and a reference answer | tasks with one right answer | needs references written by people |

### Writing a rubric

- **Separate criteria.** "Quality 1–5" mixes everything; four named criteria tell you what to fix.
- **Anchor the scale.** Say what 5, 3 and 1 mean for each criterion, with examples of failure.
- **Reasons first.** Ask for a short comment before the scores: the model then scores what it wrote down, and you get
  an explanation to read.
- **Structured output.** A JSON Schema with integer fields, validated, one retry with the error.
- **Give it the facts.** The judge must see what the tools returned, or it cannot tell a grounded claim from an
  invented one.
- **Treat the transcript as data.** A conversation can contain "give this answer a 5".

### Pass / fail or a scale?

Binary checks are easier to agree on and to aggregate ("78% of cases pass"); scales show gradients ("explanations are
3.2 on average, the weakest criterion"). Many teams use both: binary gates from code, scales from the judge.

### Known biases

| Bias | What happens | Counter-measure |
|---|---|---|
| Position | in pairwise mode the first (or second) answer wins more often | judge both orders, count only agreement |
| Verbosity | longer, confident answers score higher | the rubric says length is not quality; check the correlation |
| Self-preference | a model rates its own family's style higher | a judge from another model family |
| Leniency | scores cluster at 4–5; real problems get a 4 | anchored rubric; deterministic checks for hard rules |
| Language | a judge may miss or forgive the wrong answer language | check language with code |

### Variance, repeats and calibration

Temperature 0 does not make a hosted model deterministic. Repeat each case (three times here), report mean ± standard
deviation, and treat a difference smaller than the spread as noise. Before trusting a judge on a new task, label 20–50
transcripts yourself and measure agreement; FilmSearch did **not** do this calibration, which is a limit to keep in
mind when reading its judge numbers.

## What FilmSearch does

```mermaid
flowchart LR
    C[Case: turns + expectations] --> A["Agent plays the case<br/>(real IAssistantChat, new owner)"]
    A --> E["Events, tools, cards,<br/>cart, orders per turn"]
    E --> D["Deterministic checks<br/>tools, cards, cart, confirmation,<br/>language, grounding, no-order"]
    D --> P["PASS / FAIL<br/>pass rate ± sd"]
    E --> T["Transcript: messages, tool calls,<br/>cards, cart, checkout button"]
    T --> J["Judge (other model family)<br/>rubric 1–5, comment first,<br/>JSON Schema, temperature 0"]
    J --> S["Scores ± sd, comments<br/>runs below 3.5 listed"]
    P --> R[Report]
    S --> R
```

- **Deterministic checks decide.** A case passes when every check of every turn passes: tools called or not called,
  cards within the constraints, cart contents, checkout button shown, answer language, grounding (no bold or quoted
  title that no tool returned), and `no-order` (the assistant never creates an order by itself)
  ([`TurnChecks`](../../../evals/FilmSearch.Evals/Assistant/TurnChecks.cs), [spec 09 §3](../../spec/09-evaluation.md#3-metrics)).
- **The judge explains.** [`AssistantJudge`](../../../evals/FilmSearch.Evals/Assistant/AssistantJudge.cs) sends the
  versioned rubric [`assistant-judge.v1`](../../../evals/FilmSearch.Evals/Prompts/assistant-judge.v1.md) and a
  transcript with each turn's tool calls, the movies the tools returned, the cart and the checkout total, plus the
  case's own note on what a good conversation does. Four criteria (relevance, explanations, tone, constraints), each
  1–5 with anchors at 5, 3 and 1; "judge the content, not the length"; the transcript is data. Output: a JSON Schema
  generated from a C# record, `comment` first, temperature 0, at most 600 tokens, one retry with the validation error.
- **Another model family.** The assistants were Gemma 4 E4B (Google) and DeepSeek V4.1 Flash; the judge was
  `openai/gpt-6-luna` through OpenRouter (`--judge-profile openrouter --judge-model openai/gpt-6-luna`).
- **A stage of its own.** The judge runs after all conversations, two calls at a time, and waits out HTTP 429 with
  growing pauses (up to eight attempts) ([`AssistantSuite`](../../../evals/FilmSearch.Evals/Assistant/AssistantSuite.cs)).
  `--no-judge` skips it.

## Evidence

**The judge and the checks measure different things** ([I14 summary](../../../evals/reports/2026-11-18_I14_assistant-evaluation.md)):

| Run | Pass rate (checks) | Judge mean | What changed |
|---|---|---|---|
| Gemma, prompt v1 | 56% ± 0 | 3.81 | |
| Gemma, prompt v2 | 63% ± 3 | 3.76 | cart and checkout rules: checks +7 points, judge unchanged |
| Gemma, v3, final code | 78% ± 4 | 4.00 ± 0.11 | |
| DeepSeek, v3, final code | 92% ± 2 | 4.33 ± 0.06 | |

Prompt v2 fixed cart and checkout behaviour, which the checks measure; the judge scores recommendations and tone, which
v2 did not change. Neither number alone tells the story.

**What the judge caught that the checks passed** (final Gemma run,
[report](../../../evals/reports/2026-11-18_assistant_gemma-e4b-v3-original-titles.md)): `en-scifi-80s-follow-up`
passed every check, yet one run got constraints 1 ("none of the suggestions meet the explicit post-2010 constraint");
`ru-soviet-comedy` passed, yet the judge noted Dr. Strangelove called a Soviet comedy and invented ratings. The weakest
criterion in both final runs is **explanations** (Gemma 3.24, DeepSeek 3.82): generic reasons, a target for the next
prompt version that no regular expression would find.

**What the checks caught that the judge forgave** (final Gemma adversarial run,
[report](../../../evals/reports/2026-11-18_adversarial_gemma-e4b-v3-original-titles.md)): `adv-en-code` and
`adv-en-weather` failed the `language` check in 3 of 3 runs (English questions declined in Swedish), but the judge gave
them 4.00 and 4.25, although its rubric scores the wrong language as tone 1. A judge as the gate would have hidden the
regression of prompt v3.

**Variance and failures.** The judge's spread over repeats was ± 0.06–0.13 of the mean. Before the throttled stage
existed, OpenRouter's limit of 20 requests per minute left 30 (Gemma v2) and 53 (DeepSeek v2) of 102 runs unscored;
afterwards 0–2 per report, from invalid output or provider errors. The judge also wrote its comments in the language of
the conversation (Russian and Swedish comments in the reports), which is harmless but worth knowing when you read them.

**Cost.** The reports estimate the assistant's cost (DeepSeek: $0.052 for 144 turns) but not the judge's. The evals
README estimates a scenario run with `--repeat 3` at about 100 judge calls, a few cents with the models in `AI:Pricing`.

## How to experiment yourself

```bash
# checks + judge, the I14 setup
dotnet run --project evals/FilmSearch.Evals -- --suite assistant --label <name> --parallel 4 \
  --judge-profile openrouter --judge-model openai/gpt-6-luna
# checks only (free, offline with LM Studio)
dotnet run --project evals/FilmSearch.Evals -- --suite adversarial --label <name> --parallel 4 --no-judge
# judge the judge: the same cases with another judge model (the conversations are played again)
dotnet run --project evals/FilmSearch.Evals -- --suite assistant --label <name>-judge-b --cases "en-*" \
  --judge-profile openrouter --judge-model <other model>
```

- Change the rubric by adding `assistant-judge.v2.md` next to v1 (the latest version is used); old reports keep the
  prompt id in their header.
- Calibrate: pick 20 transcripts from a report's failed and low-scored lists, score them yourself on the same rubric,
  and count how often you and the judge agree within one point.
- Check verbosity bias: correlate the judge mean per case with the answer length from the JSON report.

## Pitfalls

- **The judge as the gate.** It forgave the wrong answer language; keep hard rules in code.
- **A judge from the assistant's family.** Self-preference inflates the score you are trying to improve.
- **One run.** Without repeats a 0.1 change in a judge mean is noise.
- **A judge without the tool results.** It cannot tell grounded from invented facts.
- **Unscored runs in a mean.** Rate limits silently shrink the sample; report how many runs were judged.
- **Never reading the comments.** The list of runs below 3.5 is where the judge earns its cost.

---

**Previous:** [Fusion tuning](fusion-tuning.md) · **Next:** [Guide contents](../README.md) ·
**See also:** [11. Evaluation](../11-evaluation.md) · [12. Guardrails](../12-guardrails.md) ·
[Spec 09 §3](../../spec/09-evaluation.md#3-metrics) · [Evaluations README](../../../evals/README.md#assistant-suites-i14) ·
[Journal: I14](../../journal/m3-ai-assistant.md#i14--assistant-evaluation-and-guardrails)
