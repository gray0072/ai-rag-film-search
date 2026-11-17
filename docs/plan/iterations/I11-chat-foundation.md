# I11 — Chat foundation: conversations, streaming, grounded answers

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I05, I07 |
| **Parallel with** | I16, I17 |
| **Branch** | `iter/I11-chat` |
| **Spec** | [06 AI assistant](../../spec/06-ai-assistant.md), [04 API §Assistant](../../spec/04-api.md#assistant) |
| **Requirements** | FR-AST-1..4, FR-AST-7, FR-AST-9, NFR-5 |

## Goal
A user can chat with the assistant through the API. Answers stream over SSE. The assistant uses one tool, `search_movies`, so recommendations are grounded in the catalog (classic RAG through a tool). Conversations are stored.

## You will learn
Streaming LLM responses end to end, conversation state and context window management, RAG with retrieved context, system prompt design, the agent loop basics (ADR-0006).

## Tasks
- [ ] Conversation, ChatMessage, MessageFeedback entities + migration (Assistant schema).
- [ ] SSE writer and event types from spec 04 (all event types defined now, some used from I12).
- [ ] Agent loop v1 (own implementation): history + system prompt + `search_movies` tool, max iterations, cancellation on client disconnect.
- [ ] `search_movies` tool over `IMovieSearch` (hybrid + QU-style filters as tool arguments); compact tool result; `movies` event with cards from tool results.
- [ ] System prompt `Prompts/assistant.system.v1.md` (spec 06 §4), `PromptVersion` stored on messages.
- [ ] Context management: last K messages; rolling summary when over a token threshold.
- [ ] Usage and cost per message and conversation.
- [ ] Feedback endpoint.
- [ ] Tests with a scripted fake `IChatClient` (tool call → result → final text), SSE parsing test.
- [ ] Manual check with `curl -N` or a `.http` file; examples in the Result.

## Acceptance criteria
- [ ] "Посоветуй что-нибудь про космос" → streamed Russian answer with movies only from the catalog and `movies` cards.
- [ ] "Tipsa om en film om rymden" → the same in Swedish.
- [ ] Conversation can be continued after reload (GET returns history).
- [ ] Trace shows the turn: LLM call → tool → LLM call.
