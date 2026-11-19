# Session S21 — 2026-11-19 — Resilience, caching, rate limiting

> [Docs](../../README.md) › [Prompt history](../README.md) › S21 · ← [S20](2026-11-19_S20_observability-cost.md) · [S22](2026-11-19_S22_deployment.md) → · [I19 plan](../../plan/iterations/I19-resilience-caching.md)

- **Iteration:** I19 (Resilience, caching, rate limiting)
- **Agent:** Claude Code (VS Code)
- **Outcome:** I19 done.
  - A retry / circuit breaker / timeout pipeline per AI profile (no retries of timeouts) and for Stripe (only idempotent requests); an optional fallback chat profile with a flag on the answer; semantic search degrades to keyword instead of 503.
  - Bounded caches of query vectors and QU results with hit / miss metrics; rate limits on search, chat messages and new conversations; ADR-0011.
  - 24 unit, 9 integration and 3 web tests; manual check with LM Studio stopped and restarted.
  - Pushed to `main`.

---

## Prompt 1

English:

```text
Let's do I19. Then push.
The LM Studio shortcut points here: "C:\Program Files\LM Studio\LM Studio.exe". There were other installations; traces of them may have remained.
```

Original (ru):

```text
делаем I19. потом пуш
ярлык LM studio ведет сюда "C:\Program Files\LM Studio\LM Studio.exe". были другие установки, от них могли остаться следы.
```
