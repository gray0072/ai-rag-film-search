# Session S20 — 2026-11-19 — Observability and cost tracking

- **Iteration:** I18 (Observability and cost tracking)
- **Agent:** Claude Code (VS Code)
- **Outcome:** I18 done.
  - Metrics: search; assistant (turns, duration, tool calls, iteration limit, grounding violations, feedback); payment webhooks; order status changes.
  - Spans for the search stages, and the trace id stored with each answer.
  - Content recording now also covers tool spans.
  - A daily token budget per owner (`dailyBudgetExceeded`).
  - Optional self-hosted Langfuse v3 in the AppHost, with filtered OTLP export and user ratings as scores.
  - An observability runbook.
  - 10 unit and 4 integration tests.
  - Manual check of one turn in Langfuse and the Aspire dashboard (OpenRouter, because LM Studio is not installed).
  - Pushed to `main`.

---

## Prompt 1

English:

```text
Let's do I18. Then push.
```

Original (ru):

```text
делаем I18. потом пуш
```
