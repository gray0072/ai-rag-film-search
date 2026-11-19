# I12 — Agent: tools, cart, checkout confirmation

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I12 · ← [I11](I11-chat-foundation.md) · [I13](I13-chat-ui.md) → · [Journal: what happened](../../journal/m3-ai-assistant.md#i12--agent-tools-cart-checkout-confirmation)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I11, I09 |
| **Parallel with** | I13 |
| **Spec** | [06 AI §2–5](../../spec/06-ai-assistant.md#3-tools) |
| **Requirements** | FR-AST-5, FR-AST-6, FR-AST-8 |

## Goal
The assistant can sell: details, similar movies, cart operations and checkout, with an explicit user confirmation before any payment session is created (human in the loop).

## You will learn
Tool design (names, descriptions, argument schemas), multi-step agents, safe side effects, confirmation patterns, handling messy model output.

## Tasks
- [x] Tools: `get_movie_details`, `find_similar_movies`, `view_cart`, `add_to_cart`, `remove_from_cart`, `request_checkout` (spec 06 §3). Owner from request context only.
- [x] Argument validation against the schema; tool errors returned to the model as structured error results (not exceptions).
- [x] Confirmation flow: `request_checkout` stores a pending confirmation (cart snapshot hash); `POST .../confirmations/{id}` checks owner and cart, creates the order and the checkout session, returns `checkoutUrl`.
- [x] `cart.updated` and `confirmation.required` events.
- [x] Handle: parallel tool calls (if the model supports them), unknown tool names, invalid JSON args, iteration limit → final summary call.
- [x] Grounding post-check (titles in the answer vs tool results) as a metric + log.
- [x] System prompt v2 with sales flow rules; keep v1 for comparison.
- [x] Tests: scripted fake chat client for the full sales flow; confirmation with a changed cart is rejected; another owner's confirmation is rejected.

## Acceptance criteria
- [x] Scenario 3 (spec 00 §5) works through the API — checked with the `openrouter` profile; the local model (LM Studio) could not be started from the agent shell, so the local re-check is a follow-up (see Result).
- [x] No way for the model alone to create a payment session (test).

## Result

### What was done
- **Tools (`FilmSearch.AI/Assistant/Tools`):** `get_movie_details`, `find_similar_movies` (`MovieTools.cs`), `view_cart`, `add_to_cart`, `remove_from_cart`, `request_checkout` (`CartTools.cs`), next to `search_movies`; registered in DI as `AssistantTool` (scoped) and declared in that order. Thin adapters over `ICatalogReader`, `IMovieSearch`, `ICartService` and the new `ICheckoutConfirmations`; no owner, cart or price argument anywhere (the services take the owner from `IOwnerContext`). Compact results with prices formatted from DB values (`ToolResults`).
- **Argument validation against the schema:** `AssistantTool<TArgs>` checks the required properties of the generated schema and binds the values; errors name the property and its allowed values ("`offerType` has an invalid value: one of buy, rent."), so the model can fix the call. Commerce refusals become tool errors with a hint. Tools without arguments declare `"properties": {}` (some OpenAI-compatible servers require it).
- **Confirmation flow (FR-AST-6):** table `assistant.confirmations` (migration `I12_AddCheckoutConfirmations`): owner, conversation, answer id, status, SHA-256 cart fingerprint (movie, offer, price, currency, availability per line + total; order-independent), summary JSON, expiry (`Assistant:ConfirmationTtlMinutes` = 15), order id. `request_checkout` stores a pending confirmation (older pending ones of the conversation become `superseded`) and emits `confirmation.required` with the server's summary. `POST /api/chat/conversations/{id}/confirmations/{confirmationId}` `{ approved }`: owner check (404), state (409 `confirmationNotPending`), expiry (409 `confirmationExpired`), fingerprint (409 `cartChanged`), then `ICheckoutService.CreateOrderAsync` (idempotency key `confirmation:{id}`, source `assistant`, conversation id) and `StartCheckoutAsync` → `{ status, orderId, checkoutUrl, expiresAt }`. A second click returns the same session; declining is repeatable. State changes are single UPDATEs (the checkout service clears the shared change tracker; clicks may race).
- **Events:** `cart.updated` (add / remove) and `confirmation.required` (now with `expiresAt`) come from `ToolOutcome.Events`, right after `tool.completed` of their call. `GET /api/chat/conversations/{id}` shows the latest confirmation of each answer with its current status (`MessageView.confirmation`), so I13 can restore the button after a reload.
- **Loop handling:** parallel tool calls run one after another in the model's order (shared `DbContext`; add must precede checkout). Unknown tools, invalid JSON and the iteration limit → final call without tools were already in I11 and stay covered by tests.
- **Grounding post-check (`GroundingCheck`):** bold and quoted spans of the answer (labels, prices, long phrases skipped), normalized (case, diacritics, ё/е, punctuation, trailing year) and matched as whole words against the tool results of the turn and of the unsummarized history, the system prompt with the summary, and the user's messages. Counter `assistant.grounding.titles{grounded,prompt}` (meter `FilmSearch`), span tags, a warning log with the unknown titles. A metric, not a filter.
- **Prompt `assistant-system.v2`** (current; v1 kept and pinnable with `Assistant:PromptVersion: 1`): sales rules (resolve "the second one" from the last list, "buy it" → `add_to_cart` + `request_checkout` in one answer, "add it, I'll keep looking" → add only, ask buy vs rent once, never claim a payment, change the cart only on request, explain cart errors), bold only for titles.
- **Tests:** unit +20 (262 total): the scenario-3 flow over two turns with parallel calls (event order, cart line `addedBy: assistant`, confirmation bound to the answer, never resolved by a tool), tool declarations (7 tools, required / enum, no owner or price argument), a reflection test that no tool depends on `ICheckoutService` / `IPaymentGateway`, schema errors of `add_to_cart`, cart refusals, remove / view / details / similar, empty-cart checkout, grounding metric through the loop, prompt v2 by default and v1 pinned, `GroundingCheck` candidates and normalization, cart fingerprint, confirmation expiry. Integration +5 (112 total; own commerce database, scripted model): scenario 3 end to end (no order before the click; approve, double click, history view, fake payment, library), changed cart rejected (another movie; buy → rent) and accepted again when the cart is back, another owner rejected (404), declined / superseded / expired rejected, empty cart asks for nothing.

### Manual checks
LM Studio could not be started from the agent's shell again (`lms` finds no running app; `LM Studio.exe` exits with code 0 at once, also outside the sandbox), so the real-model check used the **`openrouter` profile** (`deepseek/deepseek-v4.1-flash-1017`), set through environment variables for that run only: the API alone against the AppHost Postgres container (the migration was applied on start) and a small Python client with a cookie jar. Query embeddings were not available, so search ran keyword-only (degraded); `find_similar_movies` used the stored vectors.
- Scenario 3 (en) with the first v2 draft: "Want something for an evening with my wife, we liked Before Sunrise and La La Land." → `search_movies` (romance) + `find_similar_movies` (Sunrise, 1927) → 5 picks, all from tool results, 21 s. "Take the second one, buy it." → `add_to_cart` of the second movie of the list (Singin' in the Rain, buy), but **no** `request_checkout`: the draft treated "buy it" as "add". After "Yes, I want to pay now." → `request_checkout` → `confirmation.required` (7.99 USD); `GET /api/orders` empty; approve → `checkoutUrl`; pay on the fake page → order `paid`, the movie in the library.
- The prompt was changed before release (rule 10: "buy it" → add + checkout in the same answer; rule 11: "add only" without checkout) and the scenario was repeated with a new owner: turn 1 → 5 picks; "Take the second one, buy it." → `add_to_cart` (Titanic, the second of the list) + `request_checkout` in one answer → `cart.updated`, `confirmation.required` 9.99 USD, the answer "check the order and press **Go to payment**", 3.2 s; still no order.
- Russian, new owner: picks with Russian titles; "Добавь первый в корзину, я ещё посмотрю." → `add_to_cart` only (the right movie), no checkout; "Просто оплати сам, без всяких кнопок." → no tool call, a polite refusal that offers the button; no order. Slip: the model chose `rent` although the user did not say buy or rent (rule 9 says ask once): an I14 evaluation case.
- Grounding log: two false positives in these runs: **Go to payment** (the prompt bolds the button name; fixed by adding the system prompt to the references, with a unit test) and «Ла-Ла Ленде» (a Russian inflection of the user's «Ла-Ла Ленд»; documented gap).
- Checks: `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, `dotnet test` 374/374, web `gen:api` (new endpoint and types committed), lint, format check, typecheck, tests 36/36, build.

### Decisions and deviations
- **The confirmation is bound to a cart fingerprint**, not to an order created up front: the order is created only on the click, so an abandoned confirmation leaves nothing to expire in Commerce, and any change of lines, offers or prices invalidates the button (`cartChanged`; the user asks the assistant again). Going back to the cart that was shown makes the button valid again: the user pays exactly what was shown.
- **The confirmations endpoint returns `{ status, orderId, checkoutUrl, expiresAt }`** (spec 04 said `{ checkoutUrl }`); declining uses the same endpoint. `confirmation.required` got `expiresAt`. Spec 04 updated; the `Confirmation` entity was added to spec 03.
- **A newer `request_checkout` supersedes older pending confirmations** of the conversation, so only one payment button works.
- **No card for `get_movie_details`** (as in spec 06): the movie is already on screen from the search.
- **Grounding references include the system prompt** (UI labels such as the button name) and the user's messages; whole-word matching after normalization. It is a heuristic metric; I14 measures it.
- **The acceptance check ran with OpenRouter, not the local model** (LM Studio did not start from the agent shell); see follow-ups.
- No new packages. No evaluation run: the assistant evaluation harness comes in I14.

### Follow-ups
- Owner: repeat scenario 3 with LM Studio (`google/gemma-4-e4b`); `src/FilmSearch.Api/Assistant.http` has the requests.
- I13: chat UI for `cart.updated` and `confirmation.required` (button → confirmations endpoint → redirect to `checkoutUrl`; `MessageView.confirmation` after a reload; messages for `cartChanged` / `confirmationExpired`).
- I14: evaluation cases for "the second one", "buy it" vs "add it", buy vs rent when not said, "pay without the button", grounding false positives (inflected titles), cards only for mentioned movies.
- I19: rate limit on the confirmations endpoint together with `/api/chat`.

### Sessions
- [S14](../../prompts/sessions/2026-11-18_S14_agent-tools.md)
