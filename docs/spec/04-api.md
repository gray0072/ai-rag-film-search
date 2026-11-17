# 04 — API

Base path: `/api`. JSON with camelCase. Errors: RFC 9457 Problem Details. The OpenAPI document is served at `/openapi/v1.json` in Development. Every API build also writes it to `src/web/openapi/v1.json` (committed), and the frontend generates its types from that file ([ADR-0007](../adr/0007-frontend-stack.md)).

Owner identification: anonymous cookie `fs_sid` (HttpOnly, Secure, SameSite=Lax, 128 random bits as 32 hex chars, max-age 1 year; set on the first `/api` request) before auth; Identity cookie after I17. Every user-scoped endpoint resolves `OwnerId` from them.

Locale: `Accept-Language` (`en`, `ru`, `sv`) selects translated titles and overviews; unknown locales fall back to `en`.

## Service
| Method | Path | Description |
|---|---|---|
| GET | `/health` | Readiness: all health checks, including the database. Plain text `Healthy` / `Unhealthy`. |
| GET | `/alive` | Liveness: the process is up. |
| GET | `/api/info` | `{ name, version }`. Example module from I01. |

## Catalog
| Method | Path | Description |
|---|---|---|
| GET | `/api/movies` | Paged list. Query: `page`, `pageSize` (≤ 50), `sort` (`rating`, `year`, `popularity`, `title`), `genres` (csv slugs), `yearFrom`, `yearTo`, `minRating`, `runtimeMin`, `runtimeMax`, `language`, `priceMax`, `offerType`. |
| GET | `/api/movies/{id}` | Movie details with offers, credits, translations for the locale. |
| GET | `/api/movies/{id}/similar` | Similar movies (vector neighbors, from I06). |
| GET | `/api/genres` | Genres with localized names. |

## Search
| Method | Path | Description |
|---|---|---|
| POST | `/api/search` | Body: `{ query, mode: "keyword"\|"semantic"\|"hybrid", filters: {...same as catalog}, understand: bool, page, pageSize, debug: bool }`. Response: `{ items: [{ movie, score, debug? }], appliedFilters, understoodQuery?, total }`. `understand=true` runs query understanding (FR-SRCH-4). |

## Commerce
| Method | Path | Description |
|---|---|---|
| GET | `/api/cart` | Current cart with prices from the DB. |
| POST | `/api/cart/items` | `{ movieId, offerType }`. Replaces another offer of the same movie. |
| DELETE | `/api/cart/items/{movieId}` | Remove. |
| POST | `/api/orders` | Create an order from the cart. Header `Idempotency-Key` required. |
| GET | `/api/orders` / `/api/orders/{id}` | Order history / details with status. |
| POST | `/api/orders/{id}/checkout` | Create a payment session. Response `{ checkoutUrl }`. Idempotent per order while the session is valid. |
| GET | `/api/library` | Entitlements of the owner. |
| POST | `/api/payments/webhooks/stripe` | Stripe webhooks. Signature check, no auth. |
| POST | `/api/payments/webhooks/fake` | Fake gateway webhooks (Development only). |
| GET | `/fake-checkout/{sessionId}` | Fake hosted payment page: "Pay" / "Decline" / "Cancel" buttons (Development only). |

## Assistant
| Method | Path | Description |
|---|---|---|
| POST | `/api/chat/conversations` | Create a conversation. `{ locale? }` → `{ id }`. |
| GET | `/api/chat/conversations` | Owner's conversations (list). |
| GET | `/api/chat/conversations/{id}` | Messages (user and assistant, plus movie cards). |
| POST | `/api/chat/conversations/{id}/messages` | `{ text }`. Response: `text/event-stream` (see below). |
| POST | `/api/chat/conversations/{id}/confirmations/{confirmationId}` | `{ approved: bool }`. The user's answer to a confirmation request (checkout). Returns `{ checkoutUrl }` when approved. |
| POST | `/api/chat/messages/{messageId}/feedback` | `{ rating: 1\|-1, comment? }`. |

### SSE events (`POST .../messages`)

Each event: `event: <type>` and `data: <json>`.

| Event | Data | Meaning |
|---|---|---|
| `message.started` | `{ messageId }` | Assistant message created. |
| `message.delta` | `{ text }` | Next text chunk. |
| `tool.started` | `{ callId, name, argsSummary }` | Tool call started. UI shows "Searching the catalog…". |
| `tool.completed` | `{ callId, name, ok }` | Tool call finished. |
| `movies` | `{ items: [MovieCard] }` | Movie cards to show next to the message. Built from tool results, not from LLM text. |
| `cart.updated` | `{ cart }` | Cart changed through a tool. |
| `confirmation.required` | `{ confirmationId, kind: "checkout", summary: { items, totalMinor, currency } }` | UI shows a confirmation button. |
| `message.completed` | `{ messageId, usage: { inputTokens, outputTokens }, model, latencyMs }` | End of answer. |
| `error` | `{ code, message }` | Error. The stream ends. |

`MovieCard`: `{ id, title, year, posterUrl, genres, rating, offers: [{ type, priceMinor, currency }], reason? }`.

## Admin (role `admin`; before auth: Development only)
| Method | Path | Description |
|---|---|---|
| POST | `/api/admin/ingestion/seed` | Load the seed file (idempotent). |
| POST | `/api/admin/ingestion/tmdb` | `{ list: "top_rated"\|"popular", pages }` or `{ tmdbIds: [] }`. Starts a job → `{ jobId }`. |
| GET | `/api/admin/jobs/{id}` | Job status and counters. |
| POST | `/api/admin/embeddings/reindex` | `{ model?, onlyStale: bool }`. Starts a job. |

## Health
`/health` (readiness: DB, AI provider reachable) and `/alive` (liveness), from ServiceDefaults.
