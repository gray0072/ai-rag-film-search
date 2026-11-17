# I13 — Chat UI

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I10, I11 (SSE contract from spec 04 is enough to start) |
| **Parallel with** | I12 |
| **Spec** | [04 API §SSE events](../../spec/04-api.md#sse-events-post-messages) |
| **Requirements** | FR-AST-1..3, FR-AST-6, FR-AST-9, NFR-10 |

## Goal
A chat page (and a compact chat drawer available from any page) with streaming text, tool progress, movie cards with "Add to cart", cart updates, a confirmation button for checkout, and feedback buttons.

## You will learn
Streaming UX for AI products: partial rendering, progress for tool calls, cancel, error states, optimistic updates.

## Tasks
- [ ] SSE client over `fetch` + `ReadableStream` with typed events and abort.
- [ ] Mock stream (MSW or a local generator) to develop before I12 is merged.
- [ ] Message list with markdown rendering (safe, no raw HTML), typing indicator, "Stop" button.
- [ ] Tool progress chips ("Searching the catalog…"), movie cards row, cart badge update on `cart.updated`.
- [ ] Confirmation card with items and total from the server → approve → redirect to checkout URL.
- [ ] Conversation list, new conversation, reload restores history.
- [ ] Thumbs up / down.
- [ ] English / Russian / Swedish UI strings; the assistant language follows the user's message.
- [ ] Component tests with the mock stream.

## Acceptance criteria
- [ ] Scenario 3 (spec 00 §5) works end to end in the browser with the fake gateway.
- [ ] Stop cancels generation on the server (visible in logs / trace).
