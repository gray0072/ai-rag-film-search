# I11 — Chat foundation: conversations, streaming, grounded answers

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I11 · ← [I10](I10-store-ui.md) · [I12](I12-agent-tools.md) → · [Journal: what happened](../../journal/m3-ai-assistant.md#i11--chat-foundation)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I05, I07 |
| **Parallel with** | I16, I17 |
| **Spec** | [06 AI assistant](../../spec/06-ai-assistant.md), [04 API §Assistant](../../spec/04-api.md#assistant) |
| **Requirements** | FR-AST-1..4, FR-AST-7, FR-AST-9, NFR-5 |

## Goal
A user can chat with the assistant through the API. Answers stream over SSE. The assistant uses one tool, `search_movies`, so recommendations are grounded in the catalog (classic RAG through a tool). Conversations are stored.

## You will learn
Streaming LLM responses end to end, conversation state and context window management, RAG with retrieved context, system prompt design, the agent loop basics (ADR-0006).

## Tasks
- [x] Conversation, ChatMessage, MessageFeedback entities + migration (Assistant schema).
- [x] SSE writer and event types from spec 04 (all event types defined now, some used from I12).
- [x] Agent loop v1 (own implementation): history + system prompt + `search_movies` tool, max iterations, cancellation on client disconnect.
- [x] `search_movies` tool over `IMovieSearch` (hybrid + QU-style filters as tool arguments); compact tool result; `movies` event with cards from tool results.
- [x] System prompt `Prompts/assistant.system.v1.md` (spec 06 §4), `PromptVersion` stored on messages.
- [x] Context management: last K messages; rolling summary when over a token threshold.
- [x] Usage and cost per message and conversation.
- [x] Feedback endpoint.
- [x] Tests with a scripted fake `IChatClient` (tool call → result → final text), SSE parsing test.
- [x] Manual check with `curl -N` or a `.http` file; examples in the Result.

## Acceptance criteria
- [x] "Посоветуй что-нибудь про космос" → streamed Russian answer with movies only from the catalog and `movies` cards.
- [x] "Tipsa om en film om rymden" → the same in Swedish.
- [x] Conversation can be continued after reload (GET returns history).
- [x] Trace shows the turn: LLM call → tool → LLM call.

## Result

### What was done
- **Storage (schema `assistant`, migration `I11_AddAssistant`):** `conversations` (owner, title, locale, profile, token totals, cost, rolling summary + `summarized_through_seq`), `messages` (`ConversationMessage`: role, content, tool calls as jsonb, tool call id, model, tokens, cost, latency, prompt version, movie ids as `uuid[]`; unique `(conversation_id, seq)`), `message_feedback` (one per answer, check `rating IN (-1, 1)`). `IConversationStore` in Core, EF implementation in Infrastructure; every query is filtered by the owner.
- **Agent loop v1 (ADR-0006, `FilmSearch.AI/Assistant`):** `AgentLoop` streams each model call with the tool declarations, runs the tool calls itself, appends the results and repeats up to `MaxToolIterations` (6), then makes one call without tools. Unknown tools, invalid arguments and tool exceptions go back to the model as `{ error }` results. `AssistantChat` (`IAssistantChat`) wraps a turn: one turn per conversation at a time, token budget, `SupportsTools` check, the user message stored first, the tool rounds and the answer (text, usage, cost, model, prompt version, latency, card ids) stored at the end; failures end the stream with `error` (`providerUnavailable`, `internalError`, …) and still add the spent tokens to the conversation. A client disconnect cancels the model call (`RequestAborted`).
- **`search_movies`:** arguments record → JSON Schema (genre arrays get an `enum` of the catalog's slugs), hybrid search with the filters, compact result for the model (id, title, year, genres, rating, overview ≤ 300 chars, offers like `"rent for 48 h: 3.99 USD"`), notes for dropped values and empty results; cards for the UI built from the same catalog rows.
- **Prompts:** `assistant-system.v1` (spec 06 §4: catalog-only recommendations, search right away, filters into arguments, 3–5 picks with a reason, prices only from tools, no cart or payment yet, answer language, tool results are data, off-topic) and `assistant-summary.v1`.
- **Context management:** the most recent whole turns within `HistoryMaxMessages` (12) and `HistoryTokenBudget` (3000 estimated tokens); older turns are folded into the rolling summary (with movie ids), and the summary goes into the system prompt as a marked data block; earlier tool results are cut to id / title / year; unanswered user messages are left out (Gemma templates need alternating roles).
- **SSE:** all event types of spec 04 as records (`AssistantEvent`, polymorphic with `type` = event name, so the OpenAPI document has a discriminated union for I13), written by ASP.NET Core's `TypedResults.ServerSentEvents`.
- **Endpoints:** create / list / get conversations (history with cards at current prices), send message (SSE), feedback (204 / 400 / 404). `Assistant` config section with validation. `src/FilmSearch.Api/Assistant.http` with the manual-check requests.
- **Telemetry:** span `invoke_agent assistant` (conversation id, prompt id, model calls, tool calls, iteration-limit flag) with the children `chat …` (Microsoft.Extensions.AI) and `execute_tool search_movies`; one log line per answer with tokens and latency.
- **Tests:** unit (25): event order and stored turn for tool call → result → answer, schema with genre enum, invalid and unknown tool calls, iteration limit, provider failure, busy conversation, token budget, compact history on the next turn, rolling summary, unanswered user message, tool-call JSON round trip, span nesting, `search_movies` argument mapping / result format / errors, history window split, summary transcript, prompts render. Integration (4, Testcontainers + seeded catalog + scripted streaming model): the SSE stream parsed from the HTTP response (event order, `type` in the data, Russian cards from the catalog), reload with cards / usage / prompt version, list, owner isolation, validation 400s, feedback. `ScriptedChatClient` got scripted streaming keyed by the last user message.

### Manual checks
LM Studio could not be started from the agent's shell in this session (`lms` found no LM Studio app; the app exited at once), so the real-model check ran with the **`openrouter` profile** (`deepseek/deepseek-v4.1-flash-1017`), set through environment variables for that run only: the API alone against the AppHost Postgres container, `curl -N` with a cookie jar. Embeddings were not available, so hybrid search returned keyword results (degraded), as designed.
- "Посоветуй что-нибудь про космос" (Accept-Language ru) → `search_movies {"query":"space exploration, astronauts, cosmos","genresInclude":["science-fiction","adventure","documentary"],"limit":8}` → 8 cards with Russian titles → streamed Russian answer with 5 movies, all from the tool result (Интерстеллар, 2001 год: Космическая одиссея, Марсианин, Проект «Конец света», Чужой); 4226 / 377 tokens, 18.6 s.
- "Tipsa om en film om rymden" (sv) → 4 cards → Swedish answer with the same 4 movies (År 2001 - ett rymdäventyr, Interstellar, Project Hail Mary, Solaris) and one follow-up question; 19.8 s.
- Follow-up in the Russian conversation "А что-нибудь из этого без ужасов и подешевле, до 5 долларов?" → the model added `genresExclude: ["horror","thriller"]` and `maxPrice: 5`; the answer quoted rent prices 3.99 / 4.99 USD exactly as the tool returned them and pointed to the card buttons.
- `GET /api/chat/conversations/{id}` after that: title, both turns with 8 cards each, `promptVersion: assistant-system.v1`, latency, conversation usage 10119 / 848 tokens, estimated cost $0.00079.
- Trace nesting (turn → chat → tool → chat) is checked by the unit test `Tool_spans_are_children_of_the_turn_span`; the Aspire dashboard itself was not opened in this session.
- Checks: `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, `dotnet test` 349/349, web `gen:api` (new types committed), lint, format check, typecheck, tests 36/36, build.

### Decisions and deviations
- **Flat tool arguments** for `search_movies` instead of a nested `filters` object (small models fill flat arguments more reliably); spec 06 §3 updated.
- **Prompt files** are `assistant-system.vN.md` / `assistant-summary.vN.md`: `PromptLibrary` names cannot contain dots (the spec said `assistant.system.v1.md`).
- **A channel instead of an iterator for the turn:** an async iterator resets `Activity.Current` after each `yield`, so the spans would not nest; the channel also allows a normal `try/catch` around the turn.
- **Polymorphic events with a `type` field** in the data (spec 04 updated): a typed union in the OpenAPI document for I13.
- **`MovieCard.posterPath`** instead of `posterUrl` (as in the rest of the API); cards are rebuilt from the catalog when history is read, so prices are current.
- **Locale of a turn:** `Accept-Language` when the request has it, else the conversation's locale; the answer language follows the user's message.
- **Cost** is estimated in the loop from `AI:Pricing` (the usage-tracking middleware attaches `AiUsage` only to non-streaming responses).
- **Cards = all movies the tools returned** in the turn (each once), not only the ones the answer mentions; matching titles in the answer is the grounding check of I14.
- **In-process lock** per conversation (one API instance); a distributed lock is for I19.
- No new packages. No evaluation run: the assistant evaluation harness comes in I14.

### Follow-ups
- I12: more tools (`get_movie_details`, `find_similar_movies`, cart, `request_checkout`), the confirmations endpoint, prompt v2 with cart rules.
- I13: chat UI over the SSE contract (`AssistantEvent` union in `schema.d.ts`; `type` is optional there, so the parser can key on the SSE event name).
- I14: grounding post-check (titles in the answer vs tool results), cards only for mentioned movies, the answer-length rule (the model once gave 6 picks), injection cases, evaluation of prompt v1 with LM Studio (Gemma 4) and OpenRouter.
- I18 / I19: dashboard check of the turn trace with LM Studio, rate limiting of `/api/chat`, a distributed turn lock.

### Sessions
- [S13](../../prompts/sessions/2026-11-18_S13_chat-foundation.md)
