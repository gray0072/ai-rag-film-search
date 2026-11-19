# I13 — Chat UI

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I13 · ← [I12](I12-agent-tools.md) · [I14](I14-assistant-eval-guardrails.md) → · [Journal: what happened](../../journal/m3-ai-assistant.md#i13--chat-ui)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I10, I11 (SSE contract from spec 04 is enough to start) |
| **Parallel with** | I12 |
| **Spec** | [04 API §SSE events](../../spec/04-api.md#sse-events-post-messages) |
| **Requirements** | FR-AST-1..3, FR-AST-6, FR-AST-9, NFR-10 |

## Goal
A chat page (and a compact chat drawer available from any page) with streaming text, tool progress, movie cards with "Add to cart", cart updates, a confirmation button for checkout, and feedback buttons.

## You will learn
Streaming UX for AI products: partial rendering, progress for tool calls, cancel, error states, optimistic updates.

## Tasks
- [x] SSE client over `fetch` + `ReadableStream` with typed events and abort.
- [x] Mock stream (MSW or a local generator) to develop before I12 is merged.
- [x] Message list with markdown rendering (safe, no raw HTML), typing indicator, "Stop" button.
- [x] Tool progress chips ("Searching the catalog…"), movie cards row, cart badge update on `cart.updated`.
- [x] Confirmation card with items and total from the server → approve → redirect to checkout URL.
- [x] Conversation list, new conversation, reload restores history.
- [x] Thumbs up / down.
- [x] English / Russian / Swedish UI strings; the assistant language follows the user's message.
- [x] Component tests with the mock stream.

## Acceptance criteria
- [x] Scenario 3 (spec 00 §5) works end to end in the browser with the fake gateway.
- [x] Stop cancels generation on the server (visible in logs / trace).

## Result

### What was done
- **SSE client (`src/web/src/features/chat`):** `readServerSentEvents` parses a `text/event-stream` body from `fetch` (`EventSource` cannot POST a body): `\n` / `\r\n` / `\r` line ends, multi-line data, comments, chunks split anywhere, multi-byte characters split between chunks. `streamAnswer` posts through the typed client (`parseAs: "stream"`, so `Accept-Language` and Problem Details handling stay) and maps events to the typed union `AssistantEvent` (the `type` comes from the SSE event name; unknown events are skipped). An `AbortSignal` cancels the request.
- **Turn state:** a pure reducer (`turn.ts`) applies events to the answer being streamed: text, tool calls by `callId`, de-duplicated movie cards, the confirmation bound to the answer, end or error. When the answer completes, the conversation is reloaded and the server history replaces the local turn (one source of truth, the same view as after a reload). A stopped or failed answer stays on screen with a note. `message.started` tells whether the server stored the user message; if not (`conversationBusy`, network), the text goes back into the input.
- **Chat page** `/chat` and `/chat/:id`: conversation list (newest first) with "New conversation"; the first message creates the conversation in the UI language and switches the URL. Message list with safe markdown (`react-markdown`, `skipHtml`, no images, links in a new tab with `noopener`), typing indicator, tool chips ("Searching the catalog…" with a spinner, then a check mark or a cross), a row of movie cards with buy / rent buttons (the cart API, "In your cart" afterwards), feedback buttons (FR-AST-9), Enter to send and Shift+Enter for a new line, a **Stop** button while streaming.
- **Drawer:** the header's Assistant button opens the same chat panel in a side sheet on every page except the chat page; it keeps its conversation while the user browses, and has "New conversation" and "Open the chat page".
- **Cart and checkout:** `cart.updated` writes the cart into the query cache (the header badge updates at once). The confirmation card shows the server's lines and total; "Go to payment" calls the confirmations endpoint and redirects to `checkoutUrl`; "Not now" declines. After a reload the card shows the stored state: pending (buttons, valid-until time, turns into "expired" on screen when the time passes), approved (the order's own status from `GET /api/orders/{id}`: paid → link to the library; pending → "Open the payment page"; failed / expired → ask again), declined, superseded, expired. `cartChanged`, `confirmationExpired`, `confirmationNotPending`, `confirmationNotFound` and the commerce codes have their own messages.
- **Errors:** every stream error code of spec 04 (`providerUnavailable`, `conversationBusy`, `budgetExceeded`, `toolsNotSupported`, `internalError`) and a broken connection (`connectionLost`, client side) have their own text.
- **i18n:** all strings in `en`, `ru`, `sv` (`chat.*`); the placeholder page and its strings were removed.
- **Tests:** web 36 → 56: SSE parser (5), turn reducer and event mapping (3), API client first-call gate (1), chat page (9: streaming with tool chips, partial markdown, cards and history reload; Stop aborts the request and keeps the partial text; `cart.updated` badge and confirmation → confirmations endpoint → redirect with no order before the click; `cartChanged`; reload with the final confirmation state and rating; paid order; feedback and add to cart from a card; `conversationBusy` gives the text back; `providerUnavailable`), drawer (2: works from the catalog page and keeps the conversation; Russian UI). The mock stream is `src/test/sse.ts`: `sseResponse(events)` for a complete answer and `controlledSse()` to send events step by step and see the abort.

### Manual checks
Local LM Studio (`google/gemma-4-e4b`, `text-embedding-embeddinggemma-300m`), the AppHost (API, Postgres), Vite on its own port, and a Playwright script driving the installed Chrome (headless; the script and `playwright-core` stayed in the session scratchpad, not in the repo):
- **Scenario 3 (en), three runs:** "Want something for an evening with my wife, we liked Before Sunrise and La La Land." → chips "Finding similar movies…", "Searching the catalog…" → 4–6 picks with cards in 9–13 s (in one run the model first asked a clarifying question, which spec 00 allows). "Take the second one, buy it." → chips "Adding to the cart…", "Preparing the order…", header badge "Cart, items: 1", the order card (The Red Shoes / Before Sunset, buy, total from the server) in about 5 s. Reload → the same 4 messages and the card. "Go to payment" → fake checkout page → Pay → success page → the movie in the library. Back in the conversation the card says "This order is paid." with a library link.
- **Stop:** a long request was stopped after 2.5 s: "Stopped. This answer is not saved."; on the server the conversation has only the user message (no answer stored), and the next message was answered at once (no `conversationBusy`): the server turn was cancelled and its lock released. The log line "the client disconnected; the turn was cancelled" (I11) was not read from the Aspire dashboard in this session.
- **Drawer, Russian UI:** "Посоветуй комедию про путешествие во времени." on the catalog page → a Russian answer with Russian titles and cards ("Назад в будущее", "Купить за 9,99 $").
- Screenshots checked: the chat page while streaming and after the answer, the confirmation card, the drawer. One layout bug was found and fixed (the card row widened the grid column beyond the page: `min-w-0`).

### Decisions and deviations
- **Owner cookie race fixed in the web client** (found by the browser check; a bug since I09/I10): a browser without `fs_sid` sends several requests on the first page load (info, cart, conversation list), each got a new owner, and the cookie of the slowest response won, so a conversation created meanwhile belonged to another owner (404 when the history was reloaded). `apiClient` now sends the first call of a page load alone and lets the others wait for its response (one round trip, once per page load). The server still issues the cookie per request; I17 replaces it with Identity. Spec 04 notes it.
- **History is the source of truth after an answer:** the streamed turn is dropped when the reloaded conversation contains it, so cards, the confirmation state and the rating always match what a reload shows. A stopped answer is not stored by the server (I11) and stays only on screen.
- **Leaving the page does not stop the answer** (closing the drawer, opening another page); only Stop aborts. The server finishes and stores the answer, and it appears when the conversation is opened again; a message sent meanwhile gets `conversationBusy` and its text goes back into the input.
- **No MSW:** the mock stream is a small helper over the existing `mockApi` fetch stub (I12 was merged before I13 started, so no development mock server was needed).
- **New package `react-markdown`** (MIT): renders markdown to React elements without `dangerouslySetInnerHTML`; `skipHtml` drops raw HTML and URLs are sanitized. shadcn/ui `sheet` and `textarea` components added (over the existing `radix-ui`).
- No API change (the OpenAPI document and the generated types are unchanged). No evaluation run: AI behavior did not change.

### Follow-ups
- I14: model slips seen in the runs: a clarifying question although the request was clear enough; "the second one" taken from the text list while the card row has another order (the cards include movies the text does not mention, e.g. results of `find_similar_movies`); the answer after a stopped turn ignored the stored user message. Evaluation cases for these.
- I17: the owner cookie becomes the Identity cookie; the first-call gate in `apiClient` can stay (harmless) or go.
- I18: record a cancelled turn (Stop) as a span event / metric, so the check above can be read from the trace.

### Sessions
- [S15](../../prompts/sessions/2026-11-18_S15_chat-ui.md)
