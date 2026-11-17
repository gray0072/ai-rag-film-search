# I09 — Cart, orders, fake payment

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I03 |
| **Parallel with** | I04, I06 |
| **Branch** | `iter/I09-commerce` |
| **Spec** | [03 Domain](../../spec/03-domain-model.md), [04 API](../../spec/04-api.md#commerce), [07 Payments](../../spec/07-payments.md), [ADR-0004](../../adr/0004-payment-gateway-abstraction.md) |
| **Requirements** | FR-COM-1..7 |

## Goal
A user can put movies in the cart, create an order, pay on a fake hosted page, and get entitlements after an asynchronous signed webhook.

## You will learn
Production payment patterns without the external dependency: state machine, idempotency keys, webhook dedup, async confirmation, optimistic concurrency.

## Tasks
- [ ] Entities: Cart, CartItem, Order, OrderItem, Payment, PaymentEvent, Entitlement. Commerce schema migration.
- [ ] `Order` aggregate with state transitions (unit tests for every transition and invalid ones).
- [ ] `ICartService`, `ICheckoutService` in Core (used later by the agent).
- [ ] `IPaymentGateway` + `FakePaymentGateway`; fake checkout page; delayed HMAC-signed webhooks; config switches for duplicates / reordering / delay.
- [ ] Webhook processing pipeline (spec 07 §5), shared by all gateways.
- [ ] `Idempotency-Key` handling for `POST /api/orders` (stored key → same response).
- [ ] Order expiry: hosted service in the API for now (moves to Worker in I16).
- [ ] Endpoints from spec 04 Commerce + `/api/library`.
- [ ] Integration tests: happy path, decline, duplicate webhook, out-of-order events, expiry with `FakeTimeProvider`, concurrent webhooks.

## Acceptance criteria
- [ ] Full flow works through HTTP (test) and manually through the fake page.
- [ ] The same webhook delivered 3 times creates entitlements once.
- [ ] Order is not `Paid` before the webhook arrives.
