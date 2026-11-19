# ADR-0004 — Payment gateway abstraction: Fake first, then Stripe test mode

> [Docs](../README.md) › [ADRs](README.md) › ADR-0004

- **Status:** Accepted
- **Date:** 2026-11-17
- **Iteration:** I00

## Context
Payments must be done "like in production" (hosted checkout, async webhooks, idempotency), but Stripe setup should not block early iterations, and the AI parts should be testable without external services.

## Decision
`IPaymentGateway` with two implementations:
- **Fake** (I09): hosted page inside our API, signed webhooks with delay, switches for duplicates and reordering. Default in Development and tests.
- **Stripe** (I15): Checkout Session in test mode, webhooks through Stripe CLI locally.

Order logic depends only on normalized webhook events, so both gateways share it.

## Consequences
- Integration tests of the full order flow run without network.
- The Fake gateway must behave like Stripe in the important ways (async confirmation, duplicates), or the Stripe step will reveal bugs late. Its behavior is listed in spec 07.
- Live keys are refused at startup.

---

**Previous:** [ADR-0003](0003-movie-data-source-tmdb.md) · **Next:** [ADR-0005](0005-dotnet-aspire.md) · **See also:** [All ADRs](README.md) · [Docs hub](../README.md)
