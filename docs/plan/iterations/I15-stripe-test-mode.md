# I15 — Stripe test mode

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I09 |
| **Parallel with** | I07, I08, I10 |
| **Branch** | `iter/I15-stripe` |
| **Spec** | [07 Payments §4](../../spec/07-payments.md#4-stripe-test-mode-i15) |
| **Requirements** | FR-COM-3..5, NFR-7 |

## Goal
With `Payments:Provider=Stripe`, checkout goes through a real Stripe Checkout page in test mode and orders are confirmed by Stripe webhooks.

## You will learn
Stripe Checkout, webhook signatures, Stripe CLI, idempotency with a real provider, test cards and 3-D Secure.

## Tasks
- [ ] Stripe test account, keys in user-secrets (document the steps in README, no keys in the repo).
- [ ] `StripePaymentGateway`: Checkout Session with `price_data`, metadata, expiry, idempotency key.
- [ ] Webhook endpoint with signature verification; mapping of event types to normalized events.
- [ ] Startup guard: only `sk_test_` keys.
- [ ] Session expiry on order expiry.
- [ ] Tests: webhook parsing with Stripe fixture payloads and a test signature; gateway behind an interface in other tests.
- [ ] Manual runbook: `stripe listen`, test cards (success, decline, 3DS), `stripe trigger` for edge cases. Results in the Result section.

## Acceptance criteria
- [ ] Purchase in the UI with `4242…` → order `Paid` → movie in library.
- [ ] Declined card and expired session handled.
- [ ] Replayed webhook (`stripe events resend`) does not duplicate entitlements.
