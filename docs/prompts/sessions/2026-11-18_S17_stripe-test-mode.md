# Session S17 — 2026-11-18 — Stripe test mode

> [Docs](../../README.md) › [Prompt history](../README.md) › S17 · ← [S16](2026-11-18_S16_assistant-eval-guardrails.md) · [S18](2026-11-18_S18_tmdb-sync-worker.md) → · [I15 plan](../../plan/iterations/I15-stripe-test-mode.md)

- **Iteration:** I15 (Stripe test mode)
- **Agent:** Claude Code (VS Code)
- **Outcome:** I15 done: Stripe Checkout gateway (price_data, metadata, idempotency key, session expiry), signed webhook endpoint with event mapping and ignored events, test-key-only startup guard, absolute return URLs from the AppHost; 26 unit tests on real captured events and 6 integration tests with an in-memory Stripe API. Live runbook with the Stripe CLI and Playwright in the web UI: 4242, decline then 4242, 3-D Secure, cancel, resend (no duplicate entitlements), trigger, session and order expiry. Pushed after the I14 session finished.

---

## Prompt 1

English:

```text
Let's do I15. Then push. Wait until the I14 session finishes. Then shut down the computer.
```

Original (ru):

```text
делаем I15. потом пуш. дождись пока закончит сессия I14. потом выключи комп
```
