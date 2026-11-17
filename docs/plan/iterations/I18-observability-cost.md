# I18 — Observability and cost tracking

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I12 |
| **Parallel with** | I14 |
| **Branch** | `iter/I18-observability` |
| **Spec** | [10 Ops §3](../../spec/10-ops-security.md#3-observability) |
| **Requirements** | NFR-4, NFR-5 |

## Goal
For any chat turn you can see what happened, how long it took and what it cost. AI-specific metrics exist and can be viewed.

## You will learn
LLMOps: GenAI telemetry conventions, tracing agents, token and cost accounting, LLM trace tools.

## Tasks
- [ ] Custom meter `FilmSearch` with the metrics from spec 10 §3.
- [ ] Activity spans for the agent loop, each tool, search stages (keyword, vector, fusion, QU).
- [ ] Prompt / completion content recording switch (Development on, otherwise off).
- [ ] Optional Langfuse container in AppHost; send traces through OTLP; check conversation view and attach feedback scores.
- [ ] Per-session token budget enforcement (NFR-5).
- [ ] Short `docs/runbooks/observability.md`: how to find a slow or bad turn.

## Acceptance criteria
- [ ] One trace shows the full turn tree with token usage per LLM call.
- [ ] Token budget exceeded → friendly message, no LLM call.
