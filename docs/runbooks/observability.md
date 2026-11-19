# Runbook: observability

How to find a slow, expensive or bad assistant turn (or search request) and see what happened inside it. Background: [spec 10 §3](../spec/10-ops-security.md#3-observability), NFR-4, NFR-5.

## Where telemetry goes

| Viewer | What | How to open |
|---|---|---|
| Aspire dashboard | Everything: logs, traces and metrics of the API and the Worker | `dotnet run --project src/FilmSearch.AppHost`, then open the login URL printed in the console |
| Langfuse (optional) | Only traces with AI work (chat turns, searches, embeddings), as LLM traces: generations with tokens, tool calls, sessions per conversation, user scores | Switch it on once: `dotnet user-secrets set Langfuse:Enabled true --project src/FilmSearch.AppHost`. Run the AppHost and open http://localhost:3000. Log in as `admin@filmsearch.local`. The password is `Parameters:langfuse-admin-password` in the AppHost user secrets (`dotnet user-secrets list --project src/FilmSearch.AppHost`) |
| Database | Per answer: tokens, cost, latency, model, prompt version, trace id, rating | `assistant.messages`, `assistant.message_feedback`, `assistant.conversations` |

Prompt and completion texts (model messages, tool arguments and results, the user message and the answer of a turn) are recorded only when `AI:EnableSensitiveData` is on. It is on in Development and off everywhere else.

## One turn as a trace

```
POST /api/chat/conversations/{id}/messages        HTTP request (ASP.NET Core)
└─ invoke_agent assistant                        the turn: conversation id, prompt id, total tokens and cost, grounding counts
   ├─ postgresql …                               history, user message
   ├─ chat <model>                               model call 1: gen_ai.usage.input_tokens / output_tokens, filmsearch.ai.cost_usd
   ├─ execute_tool search_movies                 tool call: name, call id, ok (arguments / result with sensitive data)
   │  └─ search hybrid                           search request: mode, results, degraded
   │     ├─ search.understand                    query understanding (with its own chat span when not cached)
   │     ├─ search.embed_query → embeddings …    query vector (GenAI embeddings span)
   │     ├─ search.keyword → postgresql          full-text search: match all/any, hits
   │     ├─ search.vector → postgresql           nearest vectors: hits
   │     └─ search.fusion                        RRF: lists, rrf_k, fused
   ├─ chat <model>                               model call 2 writes the answer
   └─ postgresql …                               the stored answer
```

## Find a turn

- **A bad answer (thumbs down).** Ratings live in `assistant.message_feedback`, and every answer stores the trace id of its turn:

  ```sql
  SELECT f.created_at, f.rating, f.comment, m.trace_id, m.conversation_id, m.model, m.prompt_version, m.latency_ms, m.input_tokens, m.output_tokens
  FROM assistant.message_feedback f
  JOIN assistant.messages m ON m.id = f.message_id
  WHERE f.rating = -1
  ORDER BY f.created_at DESC
  LIMIT 20;
  ```

  Open the trace by its id: in the Aspire dashboard, Traces → filter by trace id; in Langfuse, `http://localhost:3000/project/filmsearch/traces/<trace_id>`. In Langfuse the rating is also a `user_feedback` score on the trace (1 or -1, with the comment), so Traces → filter by score works too.
- **A slow turn.** Metric `assistant.turn.duration` shows when turns got slow (by `outcome`, `prompt`). Then find the slowest answers: `SELECT trace_id, latency_ms, model FROM assistant.messages WHERE role = 'Assistant' AND latency_ms IS NOT NULL ORDER BY latency_ms DESC LIMIT 20;`. In the trace, compare the chat spans (model time; a local model on battery power runs on the CPU), the tool spans and `search.embed_query` (embedding provider).
- **An expensive conversation.** `SELECT id, owner_id, total_input_tokens + total_output_tokens AS tokens, estimated_cost_usd FROM assistant.conversations ORDER BY tokens DESC LIMIT 20;`. In Langfuse, Sessions lists conversations (session id = conversation id) with their cost.
- **A conversation the user reports.** The conversation id is in the chat URL. Langfuse → Sessions → that id shows all turns in order. In the Aspire dashboard, filter traces by the tag `gen_ai.conversation.id`.
- **Errors.** `assistant.turns{outcome != answered}` counts turns by error code (`providerUnavailable`, `budgetExceeded`, `dailyBudgetExceeded`, `conversationBusy`, `internalError`, `cancelled`). The logs carry the conversation id. Failed spans have status Error.

## Metrics (meter `FilmSearch`)

| Metric | Tags | Read it as |
|---|---|---|
| `assistant.turns`, `assistant.turn.duration` | outcome, prompt | Turn success rate and latency per prompt version |
| `assistant.tool_calls` | tool, ok | Tools that fail or get invalid arguments |
| `assistant.iteration_limit_hits` | prompt | The model loops on tools |
| `assistant.grounding_violations`, `assistant.grounding.titles` | prompt (grounded) | Answers that name movies no tool returned |
| `assistant.feedback` | rating (up/down) | User satisfaction |
| `ai.tokens`, `ai.cost_usd` | model, profile, operation (direction) | Spend per model and call type. `gen_ai.client.token.usage` and `gen_ai.client.operation.duration` (meter `FilmSearch.AI`) come from Microsoft.Extensions.AI |
| `search.requests`, `search.latency` | mode, understand, degraded | Search load and speed; `degraded=true` means the embedding provider is down |
| `payments.webhooks` | provider, type, result | `rejected` = bad signatures, `error` = processing failures (the provider retries) |
| `orders.status_changes` | from, to | Orders that are created but never get to `paid` |

## Budgets (NFR-5)

- `Assistant:MaxConversationTokens` (300 000): a conversation past it gets `error: budgetExceeded`, and the UI asks the user to start a new one.
- `Assistant:MaxOwnerTokensPerDay` (1 000 000): the owner (anonymous session or user) has used that many tokens in answers of the last 24 hours, over all conversations. The next turn gets `error: dailyBudgetExceeded`. Both checks run before any model call, and the warning log names the budget.
- `Assistant:MaxToolIterations` (6) limits model calls per turn. Each hit is counted in `assistant.iteration_limit_hits`.

## Langfuse housekeeping

- Six containers: `langfuse`, `langfuse-worker`, `langfuse-postgres`, `langfuse-clickhouse`, `langfuse-redis`, `langfuse-minio`. They stop with the AppHost. Data stays in the volumes `filmsearch-langfuse-postgres`, `filmsearch-langfuse-clickhouse` and `filmsearch-langfuse-minio`.
- Reset: stop the AppHost, then run `docker volume rm filmsearch-langfuse-postgres filmsearch-langfuse-clickhouse filmsearch-langfuse-minio`. Headless init creates the organization, the project, its API keys and the admin again on the next start.
- If the AppHost was killed (not stopped with Ctrl+C), the containers may keep running. Remove them with `docker rm -f $(docker ps -q --filter name=langfuse)`.
- Traces arrive in Langfuse a few seconds after the turn (batch export, then the Langfuse worker). A trace missing after a minute usually means the API was started without the `Langfuse__*` variables, which the AppHost sets only when `Langfuse:Enabled` is true.
