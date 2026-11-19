# I15 — Stripe test mode

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I15 · ← [I14](I14-assistant-eval-guardrails.md) · [I16](I16-tmdb-sync-worker.md) → · [Journal: what happened](../../journal/m4-real-integrations.md#i15--stripe-test-mode)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I09 |
| **Parallel with** | I07, I08, I10 |
| **Spec** | [07 Payments §4](../../spec/07-payments.md#4-stripe-test-mode-i15) |
| **Requirements** | FR-COM-3..5, NFR-7 |

## Goal
With `Payments:Provider=Stripe`, checkout goes through a real Stripe Checkout page in test mode and orders are confirmed by Stripe webhooks.

## You will learn
Stripe Checkout, webhook signatures, Stripe CLI, idempotency with a real provider, test cards and 3-D Secure.

## Tasks
- [x] Stripe test account, keys in user-secrets (document the steps in README, no keys in the repo).
- [x] `StripePaymentGateway`: Checkout Session with `price_data`, metadata, expiry, idempotency key.
- [x] Webhook endpoint with signature verification; mapping of event types to normalized events.
- [x] Startup guard: only `sk_test_` keys.
- [x] Session expiry on order expiry.
- [x] Tests: webhook parsing with Stripe fixture payloads and a test signature; gateway behind an interface in other tests.
- [x] Manual runbook: `stripe listen`, test cards (success, decline, 3DS), `stripe trigger` for edge cases. Results in the Result section.

## Acceptance criteria
- [x] Purchase in the UI with `4242…` → order `Paid` → movie in library.
- [x] Declined card and expired session handled.
- [x] Replayed webhook (`stripe events resend`) does not duplicate entitlements.

## Result

### What was done
- **`StripePaymentGateway`** (Infrastructure, `Commerce/StripeGateway`, Stripe.net 53.1.0): Checkout Session in `payment` mode with `price_data` line items from the order snapshot, `metadata.orderId` (also on the PaymentIntent), `client_reference_id`, absolute success / cancel URLs, `expires_at` = order expiry + 30 min, idempotency key `order:{id}:attempt:{n}`; session expiry (400 for a closed session and 404 are no-ops); webhook parsing with the signature check on `TimeProvider` (5 min tolerance) and no API version check. Registered when `Stripe:SecretKey` is set; HTTP through a named client with the standard resilience handler (the SDK's own retries off).
- **Webhook endpoint** `POST /api/payments/webhooks/stripe`; the handler is shared with the fake endpoint now (`PaymentWebhookEndpoints.ReceiveAsync`). Event mapping: `checkout.session.completed` (paid / no payment required) and `async_payment_succeeded` → `Succeeded`, `async_payment_failed` → `Failed`, `expired` → `Expired`; other types, unpaid completions and sessions without our order id → 200 `ignored`, not stored.
- **Startup guards:** only `sk_test_` keys (live and restricted keys are refused); `Payments:Provider = Stripe` requires the webhook secret and absolute return URLs. In Production the fake page and the fake webhook endpoint are not registered.
- **Absolute return URLs:** `Commerce:WebBaseUrl` (relative success / cancel URLs are resolved against it); the AppHost passes the Vite endpoint to the API.
- **Session expiry on order expiry:** the existing expiry job closes Stripe sessions through the gateway; checked live and in tests.
- **Tests:** unit (26) — parsing of real test mode events captured with the Stripe CLI (completed, expired, `payment_intent.payment_failed`, a `stripe trigger` session without metadata), asynchronous results, another API version, bad / old / missing signatures, format errors, the session creation form (idempotency key, line items, metadata, URLs, `expires_at`), relative URLs refused, expiry no-ops, startup validation (live / restricted keys, missing webhook secret, relative URLs), gateway registration, URL resolution. Integration (6, `CommerceStripeTests` with `FakeStripeApi`) — one session per attempt with absolute URLs; completed → paid once with 3 concurrent replays (one event row, one entitlement, library, cart emptied); bad and old signatures → 400, not stored; ignored events, and unpaid → asynchronous success; asynchronous failure → failed → a new attempt closes the old session; order expiry closes the session and a payment made before still counts.
- **Docs:** spec 07 §1/§3/§4/§5, spec 04 (webhook row), README "Payments: Stripe test mode" (account, CLI, user secrets, `stripe listen`, test cards), AGENTS.md (test notes, Stripe CLI, Checkout DOM for Playwright).

### Manual checks (runbook)
The API from the worktree (`Payments__Provider=Stripe`, `Commerce__WebBaseUrl=http://localhost:5173`) against the dev database, the Vite dev server, `stripe listen --api-key … --events checkout.session.*,payment_intent.payment_failed --forward-to https://localhost:7180/api/payments/webhooks/stripe --skip-verify`; the web UI driven by Playwright (Chromium) through Stripe's hosted page:
- `4242 4242 4242 4242`: movie page → Buy → cart → Go to payment → Stripe Checkout → the success page shows "Thank you!" → order `paid` (the webhook came before the browser returned) → the movie is in the library as owned.
- `4000 0000 0000 9995`: Stripe shows "Your card has insufficient funds. Try a different card."; the order stays `pendingPayment` (`payment_intent.payment_failed` → 200, ignored, not stored); then 4242 on the same page → `paid`.
- `4000 0025 0000 3155`: 3-D Secure challenge → Complete → `paid`, in the library.
- Cancel (the back link on Stripe's page) → `/checkout/cancel`, the order stays `pendingPayment`.
- `stripe events resend` of a `checkout.session.completed` 3 times → 3 × `Duplicate`, still 1 entitlement.
- `stripe trigger checkout.session.completed` (a session without our metadata) → 200, ignored, not stored.
- `stripe checkout sessions expire cs_…` → `checkout.session.expired` → payment `Expired` (applied), the order still pending; checkout again → a new attempt and session.
- Order expiry: `expires_at` of a pending order moved into the past → the job expired the order and closed its Stripe session → Stripe's `checkout.session.expired` arrived → `Ignored` (no change).
- AppHost (Fake provider): Pay on the fake page redirects to `http://localhost:<vite port>/checkout/success?...`, so `Commerce:WebBaseUrl` is passed.
- Checks: `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, OpenAPI document unchanged, `dotnet test` 406/406 (434/434 after the rebase onto I14); web `gen:api` (no diff), lint, format check, typecheck, tests 56/56, build.

### Decisions and deviations
- **`ParseWebhook` returns `ParsedWebhook`** (the event or null + a reason) instead of `PaymentWebhookEvent`: Stripe delivers event types the shop does not act on, and they must get 200 (a 400 makes Stripe retry for days). Spec 07 §1 updated.
- **Session lifetime = order expiry + 30 min**, not the order's expiry: Stripe's minimum is 30 minutes after creation, and the value must not depend on the current time, or a retry with the same idempotency key is refused. The expiry job closes the session at the order's expiry; a payment in between is a late success (handled since I09).
- **A declined card does not fail the order:** Stripe Checkout keeps the customer on its page to try another card and sends only `payment_intent.payment_failed` (without the session id). The order stays pending; `Failed` comes from asynchronous methods only. Spec 07 §4 says so.
- **The Stripe gateway is registered whenever a secret key is set**, not only when it is the active provider, so webhooks of old Stripe sessions still work after switching back to Fake.
- **Retries:** the standard resilience handler of ServiceDefaults instead of the SDK's own retries (one retry policy, HTTP traces in the dashboard); session creation is safe to retry because of the idempotency key.
- **`stripe listen` needs `--events`** in Stripe CLI 1.55 (not in the original runbook).
- **New package:** `Stripe.net` (planned by spec 02 and ADR-0004). `FilmSearch.Infrastructure` gets a `FrameworkReference` to `Microsoft.AspNetCore.App` for `IHttpClientFactory`, as `FilmSearch.AI` has. **New tool:** Stripe CLI (winget).
- `Payment.ProviderPaymentIntentId` stays empty: it is needed for refunds (S07).

### Follow-ups
- S07 refunds: `charge.refunded` → `Refunded` needs the PaymentIntent id on the payment (from `checkout.session.completed`).
- I16: the expiry job (which closes Stripe sessions) moves to the Worker; the Worker needs the Stripe gateway registration too.
- I20: a Stripe webhook endpoint in the dashboard for the deployed URL (its own `whsec_`), the absolute `Commerce:WebBaseUrl` of the deployed web app.
- Maybe: run `stripe listen` from the AppHost as an executable resource when Stripe is the provider.

### Sessions
- [S17](../../prompts/sessions/2026-11-18_S17_stripe-test-mode.md)
