# I12 — Agent: tools, cart, checkout confirmation

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I11, I09 |
| **Parallel with** | I13 |
| **Spec** | [06 AI §2–5](../../spec/06-ai-assistant.md#3-tools) |
| **Requirements** | FR-AST-5, FR-AST-6, FR-AST-8 |

## Goal
The assistant can sell: details, similar movies, cart operations and checkout, with an explicit user confirmation before any payment session is created (human in the loop).

## You will learn
Tool design (names, descriptions, argument schemas), multi-step agents, safe side effects, confirmation patterns, handling messy model output.

## Tasks
- [ ] Tools: `get_movie_details`, `find_similar_movies`, `view_cart`, `add_to_cart`, `remove_from_cart`, `request_checkout` (spec 06 §3). Owner from request context only.
- [ ] Argument validation against the schema; tool errors returned to the model as structured error results (not exceptions).
- [ ] Confirmation flow: `request_checkout` stores a pending confirmation (cart snapshot hash); `POST .../confirmations/{id}` checks owner and cart, creates the order and the checkout session, returns `checkoutUrl`.
- [ ] `cart.updated` and `confirmation.required` events.
- [ ] Handle: parallel tool calls (if the model supports them), unknown tool names, invalid JSON args, iteration limit → final summary call.
- [ ] Grounding post-check (titles in the answer vs tool results) as a metric + log.
- [ ] System prompt v2 with sales flow rules; keep v1 for comparison.
- [ ] Tests: scripted fake chat client for the full sales flow; confirmation with a changed cart is rejected; another owner's confirmation is rejected.

## Acceptance criteria
- [ ] Scenario 3 (spec 00 §5) works through the API with the local model.
- [ ] No way for the model alone to create a payment session (test).
