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
| GET | `/api/movies` | Paged list. Query: `page` (default 1), `pageSize` (default 20, ≤ 50), `sort` (`popularity` default, `rating`, `year`, `title`; case-insensitive; `title` uses the localized title), `genres` (csv slugs, any of), `yearFrom`, `yearTo`, `minRating`, `runtimeMin`, `runtimeMax`, `language` (original language), `priceMax` (minor units), `offerType` (`buy`/`rent`; with `priceMax` both apply to the same offer). Response `{ items: [MovieSummary], page, pageSize, total }`. Invalid input → 400 Problem Details. |
| GET | `/api/movies/{id}` | Movie details: localized texts, genres, keywords, `directors`, `writers`, `cast` (top 10), active offers. 404 Problem Details for an unknown id. |
| GET | `/api/movies/{id}/similar` | Similar movies (from I06): nearest neighbours of the movie's vector of the active embedding model, best first, without the movie itself. Query: `limit` (1–50, default 10). Response `[{ movie: MovieSummary, score }]` (`score` = cosine similarity). Empty while the movie has no vector; 404 for an unknown id. |
| GET | `/api/genres` | Genres `{ id, slug, name }` with localized names, sorted by name. |

Locale: from `Accept-Language` (`en`, `ru`, `sv`; `sv-SE` → `sv`; anything else → `en`), echoed in `Content-Language`. Missing texts fall back to English per field. JSON enums are camelCase strings (`buy`, `rent`). Posters are TMDB paths (`posterPath`); the UI builds the image URL.

## Search
| Method | Path | Description |
|---|---|---|
| POST | `/api/search` | Body: `{ query (1–500 chars), mode: "hybrid"\|"keyword"\|"semantic" (default hybrid), filters: { genres: [slug], excludeGenres: [slug], yearFrom, yearTo, minRating, runtimeMin, runtimeMax, language, priceMax, offerType }, understand: bool, page, pageSize (≤ 50, default 20), debug: bool }`. Response: `{ items: [{ movie: MovieSummary, score, debug? }], appliedFilters, understoodQuery?, page, pageSize, total, degraded }`; `debug` = `{ keywordRank, keywordScore, keywordMatch: "all"\|"any", vectorRank, similarity, fusedScore }`. Movie texts follow `Accept-Language`; the query may be in any supported language. `semantic` (from I06): `score` = cosine similarity, `total` ≤ `Search:Semantic:MaxResults` (100); 503 Problem Details when the query cannot be embedded (provider down). `hybrid` (I07): `score` = fused RRF score, `total` = size of the union of the candidate lists (≤ 2 × 50); when the query cannot be embedded it returns the keyword results with `degraded: true` instead of 503. `understand=true` (I07, FR-SRCH-4) runs query understanding first: `understoodQuery` = `{ semanticQuery, filters: { genresInclude, genresExclude, yearFrom, yearTo, minRating, maxPriceMinor, language }, similarToTitles, detectedLocale, source: "llm"\|"cache"\|"fallback", promptVersion, error? }`; `appliedFilters` are the request filters merged with the found ones (explicit filters win, excluded genres add up). It works with every mode and never fails because of the model: invalid output or a provider outage gives `source: "fallback"`. |

## Commerce
| Method | Path | Description |
|---|---|---|
| GET | `/api/cart` | Current cart with prices from the DB: `{ items: [{ movieId, title, year, posterPath, offerId, offerType, priceMinor, currency, rentalHours, addedBy, addedAt, available }], totalMinor, currency }`. `available: false` = the offer is no longer sold (not in the total, blocks the order). |
| POST | `/api/cart/items` | `{ movieId, offerType }`. Replaces another offer of the same movie. Response: the cart. 404 `movieNotFound`; 409 `offerNotAvailable`, `alreadyOwned`, `alreadyRented` (renting a rented movie; buying it is allowed). |
| DELETE | `/api/cart/items/{movieId}` | Remove (no error when the movie is not in the cart). Response: the cart. |
| POST | `/api/orders` | Create a `pendingPayment` order from the cart with a price snapshot. Header `Idempotency-Key` required (1–200 chars, e.g. a new UUID per checkout click; missing → 400). A repeated key returns the order created with it (201, header `Idempotent-Replayed: true`), whatever the cart holds now. 409 `cartEmpty`, `cartHasUnavailableItems`, `alreadyOwned`, `alreadyRented`. The cart is kept until the order is paid. |
| GET | `/api/orders` / `/api/orders/{id}` | Order history (newest first, at most 100) / details with status: `{ id, status, totalMinor, currency, createdAt, expiresAt, paidAt, source, items: [{ movieId, offerId, offerType, title, priceMinor, rentalHours }] }`. Another owner's order → 404. |
| POST | `/api/orders/{id}/checkout` | Create a payment session. Response `{ orderId, checkoutUrl, expiresAt }`. Idempotent per order while the session is valid; after a failed payment it starts a new attempt and closes the old session. 409 `orderNotPayable` (paid, expired; past `expiresAt` the order is expired at once). |
| GET | `/api/library` | Active entitlements of the owner, one per movie (owned wins over a rental), newest first: `[{ movieId, title, year, posterPath, type: "owned"\|"rental", validFrom, validUntil, orderId }]`. Ended rentals are not listed. |
| POST | `/api/payments/webhooks/stripe` | Stripe webhooks (I15). Signature check, no auth. |
| POST | `/api/payments/webhooks/fake` | Fake gateway webhooks. Signature header `Fake-Signature`; 400 for a bad signature or body; `{ eventId, result }`. Not in the OpenAPI document. |
| GET | `/fake-checkout/{sessionId}` | Fake hosted payment page: "Pay" / "Decline" / "Cancel" buttons (`POST /fake-checkout/{sessionId}/pay` etc., redirects). Not in the OpenAPI document. The fake gateway is refused in Production. |

Commerce errors are Problem Details with a camelCase `code` extension (`CommerceError`), so the web app and the assistant can react without parsing text.

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
| POST | `/api/admin/embeddings/reindex` | `{ profile?, onlyStale: bool (default true), locales?: ["en", "ru", "sv"] }`. Starts a background job → 202 with the job status; 409 while a job is queued or running; 400 for an unknown profile or locale. `profile` is an embedding profile (model, dimension and prefixes come with it); vectors of other locales of that model are deleted. |
| GET | `/api/admin/embeddings` | Active profile, model and dimension; vector counts per model and locale; the current or last job (state, progress, result, error). |
| DELETE | `/api/admin/embeddings?model=` | Deletes all vectors of a model; 400 for the active model. |

## Health
`/health` (readiness: DB, AI provider reachable) and `/alive` (liveness), from ServiceDefaults.
