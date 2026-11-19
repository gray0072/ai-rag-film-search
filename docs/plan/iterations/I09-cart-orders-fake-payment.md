# I09 — Cart, orders, fake payment

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I09 · ← [I08](I08-search-evaluation.md) · [I10](I10-store-ui.md) → · [Journal: what happened](../../journal/m1-store-without-ai.md#i09--cart-orders-fake-payment)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I03 |
| **Parallel with** | I04, I06 |
| **Spec** | [03 Domain](../../spec/03-domain-model.md), [04 API](../../spec/04-api.md#commerce), [07 Payments](../../spec/07-payments.md), [ADR-0004](../../adr/0004-payment-gateway-abstraction.md) |
| **Requirements** | FR-COM-1..7 |

## Goal
A user can put movies in the cart, create an order, pay on a fake hosted page, and get entitlements after an asynchronous signed webhook.

## You will learn
Production payment patterns without the external dependency: state machine, idempotency keys, webhook dedup, async confirmation, optimistic concurrency.

## Tasks
- [x] Entities: Cart, CartItem, Order, OrderItem, Payment, PaymentEvent, Entitlement. Commerce schema migration.
- [x] `Order` aggregate with state transitions (unit tests for every transition and invalid ones).
- [x] `ICartService`, `ICheckoutService` in Core (used later by the agent).
- [x] `IPaymentGateway` + `FakePaymentGateway`; fake checkout page; delayed HMAC-signed webhooks; config switches for duplicates / reordering / delay.
- [x] Webhook processing pipeline (spec 07 §5), shared by all gateways.
- [x] `Idempotency-Key` handling for `POST /api/orders` (stored key → same response).
- [x] Order expiry: hosted service in the API for now (moves to Worker in I16).
- [x] Endpoints from spec 04 Commerce + `/api/library`.
- [x] Integration tests: happy path, decline, duplicate webhook, out-of-order events, expiry with `FakeTimeProvider`, concurrent webhooks.

## Acceptance criteria
- [x] Full flow works through HTTP (test) and manually through the fake page.
- [x] The same webhook delivered 3 times creates entitlements once.
- [x] Order is not `Paid` before the webhook arrives.

## Result

### What was done
- **Domain (Core/Commerce):** `Cart`, `CartItem`, `Order` (aggregate), `OrderItem`, `Payment`, `PaymentEvent`, `Entitlement`. `Order` changes state only through `MarkPaid` (creates the entitlements), `MarkFailed`, `RetryPayment`, `Expire`, `MarkRefunded`; webhook-driven methods return false for duplicate or stale events, the others throw `OrderStateException`. `Payment.Apply` is the state machine of one attempt; `Entitlement.Blocks` holds the rule "owned: nothing more; rented: buy only".
- **Contracts for the agent (I12):** `ICartService`, `ICheckoutService`, `ILibraryService`, `IPaymentGateway`, `IPaymentWebhookProcessor`. The owner comes from `IOwnerContext` only. Refusals are `CommerceException` with a `CommerceError` code; the API maps them to Problem Details with a camelCase `code` (404 / 409).
- **Persistence:** schema `commerce`, migration `I09_AddCommerce`. Unique owner per cart, one line per movie (upsert `ON CONFLICT`), unique (owner, idempotency key) on orders, unique (provider, event id) on events, unique (provider, session id) and (order, attempt) on payments, `xmin` row version on orders, a partial index for the expiry scan. Order items have no foreign keys to the catalog (history survives a deleted movie).
- **Checkout:** `POST /api/orders` takes the price snapshot from the catalog (`ICatalogReader`), checks entitlements, and stores the `Idempotency-Key`; a repeated key (also concurrently: unique violation → read the winner) returns the same order with `Idempotent-Replayed: true`. `POST /api/orders/{id}/checkout` locks the order row (`FOR UPDATE`), returns the open session or starts a new attempt (`order:{id}:attempt:{n}`), closing the old sessions; past `ExpiresAt` it expires the order at once.
- **Webhook pipeline** (`PaymentWebhookProcessor`, spec 07 §5): insert-or-skip the event, then in a transaction lock the event row and the payment row, apply, create entitlements, remove paid movies from the cart, set `ProcessedAt`; `xmin` conflicts retry; errors are stored and rethrown (500 → the provider retries). Unknown sessions, stale events and amount mismatches are acknowledged with a note.
- **Fake gateway** (in the API, `Modules/Commerce/FakeGateway`): in-memory sessions, hosted page with Pay / Decline / Cancel, `Fake-Signature` HMAC (Stripe-style `t=…,v1=…`, 5 min tolerance), `FakeWebhookDispatcher` that sends each event over HTTP to the API itself after its own delay on `TimeProvider` (1–3 s), switches for duplicates and late failures. Refused in Production.
- **Order expiry:** `OrderExpiryJob` + `OrderExpiryService` (hosted service in the API, every 60 s; to the Worker in I16).
- **API:** cart, orders, checkout, library endpoints (spec 04), OpenAPI document and web types regenerated; Vite proxies `/fake-checkout`.
- **Tests:** unit — every order transition and the invalid ones, payment transitions, entitlement rules, signatures (32 new); integration (23 new, each commerce fixture on its own database) — cart rules and localization, idempotency (also 5 concurrent requests), happy path through the fake page, decline and retry, cancel, the same webhook 3 times, 10 + 5 concurrent webhooks, failure after success, stale failure of an old attempt, wrong amount, invalid signature, unknown session, owner isolation, the dispatcher's duplicate and out-of-order switches, expiry job; on a `FakeTimeProvider`: not paid before the webhook, expiry after 30 min (paid orders untouched), checkout past expiry, late success after expiry, rentals leave the library after 48 h.

### Manual checks
- AppHost (Development, real Postgres, HTTPS): cart (buy + rent) → order (the repeated key → 201 + `Idempotent-Replayed`) → checkout twice (same URL) → fake page → **Pay** → 302 to `/checkout/success?orderId=…`; the order was `pendingPayment` right after the redirect and `paid` ~2 s later (the webhook went over HTTPS to the API itself, signed); library: owned + rental with `validUntil` +48 h; cart empty; buying the owned movie again → 409.
- `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, OpenAPI document up to date, `dotnet test` 321/321 (unit + integration), commerce integration tests 3 runs in a row green; web `gen:api`, lint, format check, typecheck, tests (7/7), build.

### Decisions and deviations
- **`IPaymentGateway.ParseWebhook(PaymentWebhookRequest)`** (raw body + headers, synchronous) instead of `ParseWebhookAsync(HttpRequest)`: Core must not depend on ASP.NET Core. Added `ExpireCheckoutSessionAsync` (needed for retries and expiry); `RefundAsync` left for the refund stretch goal. Spec 07 §1 updated.
- **The Fake gateway lives in the API project**, not in Infrastructure: it serves a page and calls the host's own endpoint (needs `IServer` addresses, `IHttpClientFactory`). Stripe goes to Infrastructure in I15.
- **Relative checkout / success / cancel URLs** with a Vite proxy for `/fake-checkout`; absolute URLs come with Stripe.
- **Cart is kept until payment** (paid movies are removed by the webhook transaction), so a cancelled or declined payment does not lose the cart.
- **Failed orders expire too** and can be paid by a retry on the provider page; Decline keeps the session open (as Stripe does). Spec 03 §2 diagram updated.
- **Event and payment row locks** (`FOR UPDATE`) in webhook processing: with only the order's `xmin`, two different success events of one session could both report "applied" (found by the concurrency test).
- **Retries of webhook delivery** come from the standard resilience handler of `ServiceDefaults`, not from own code.
- **The webhook client accepts the development certificate** (`CN=localhost`) for the call to itself.
- **Idempotency replay returns the current state** of the stored order (not a byte copy of the first response); the status code (201) is the same.
- **New package:** `Microsoft.Extensions.TimeProvider.Testing` (tests only) for `FakeTimeProvider` — order expiry and webhook timing without waiting.
- **The rental invariant** is checked when adding to the cart and when creating the order, not at payment (two orders paid in parallel could both grant a rental). Spec 03 §3 says so.

### Follow-ups
- I10: cart, checkout, success page (poll `GET /api/orders/{id}`), library UI; handle the `code` of commerce Problem Details.
- I12: agent tools over `ICartService` / `ICheckoutService` (`AddedBy = Assistant`, `Source = Assistant`, `ConversationId`).
- I15: Stripe gateway, absolute success / cancel URLs, `checkout.session.expired` handling, live-key refusal.
- I16: move `OrderExpiryService` to the Worker.
- I17: merge the anonymous cart on login (FR-USR-2); owners of orders and entitlements move to the user id.
- Maybe: do not issue an owner cookie on webhook endpoints; an outbox for follow-up work after payment.

### Sessions
- [S10](../../prompts/sessions/2026-11-18_S10_cart-orders-fake-payment.md)
