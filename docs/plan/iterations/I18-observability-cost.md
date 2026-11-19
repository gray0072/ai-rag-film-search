# I18 — Observability and cost tracking

| | |
|---|---|
| **Status** | done |
| **Depends on** | I12 |
| **Parallel with** | I14 |
| **Spec** | [10 Ops §3](../../spec/10-ops-security.md#3-observability) |
| **Requirements** | NFR-4, NFR-5 |

## Goal
For any chat turn you can see what happened, how long it took and what it cost. AI-specific metrics exist and can be viewed.

## You will learn
LLMOps: GenAI telemetry conventions, tracing agents, token and cost accounting, LLM trace tools.

## Tasks
- [x] Custom meter `FilmSearch` with the metrics from spec 10 §3.
- [x] Activity spans for the agent loop, each tool, search stages (keyword, vector, fusion, QU).
- [x] Prompt / completion content recording switch (Development on, otherwise off).
- [x] Optional Langfuse container in AppHost; send traces through OTLP; check conversation view and attach feedback scores.
- [x] Per-session token budget enforcement (NFR-5).
- [x] Short `docs/runbooks/observability.md`: how to find a slow or bad turn.

## Acceptance criteria
- [x] One trace shows the full turn tree with token usage per LLM call.
- [x] Token budget exceeded → friendly message, no LLM call.

## Result

### What was done
- **Metrics (meter `FilmSearch`, spec 10 §3):**
  - Assistant (`AssistantMetrics`, now in its own file): `assistant.turns` and `assistant.turn.duration` by outcome (`answered`, `cancelled` or the error code, taken from the last event of the turn) and prompt; `assistant.tool_calls` by tool and ok; `assistant.iteration_limit_hits`; `assistant.grounding_violations` (answers with at least one unknown title; `assistant.grounding.titles` stays); `assistant.feedback` (up / down).
  - Search (`SearchMetrics`): `search.requests` and `search.latency` (seconds) by mode, understand and degraded.
  - Commerce (`CommerceMetrics`): `payments.webhooks` by provider, type and result (`applied`, `duplicate`, `ignored`, plus `unhandled`, `rejected` and `error` from the endpoint); `orders.status_changes` (from, to; `from=created` for new orders). Status changes are counted after the commit: in the webhook processor, the expiry job and checkout (retry after a failure, late expiry).
  - `ai.tokens` and `ai.cost_usd` were already there (I05).
- **Spans:**
  - Turn span (`invoke_agent assistant`): now also has the turn's total tokens and cost, plus `langfuse.session.id` (conversation), `langfuse.user.id` (owner) and the trace name.
  - Tool spans: `gen_ai.tool.type` and `filmsearch.tool.ok`.
  - Search stages (activity source `FilmSearch`): `search {mode}` → `search.understand`, `search.keyword`, `search.embed_query`, `search.vector`, `search.fusion`. They record counts, match kind and the degraded flag, never the query text.
  - The answer stores the trace id of its turn: `assistant.messages.trace_id`, migration `I18_AddMessageTraceId`. This is the link from a rating to the trace.
- **Content recording:** `AI:EnableSensitiveData` (on in Development since I05, off by default) now also controls the tool arguments and results (`gen_ai.tool.call.arguments` / `.result`) and the user message and answer on the turn span. `TraceContent` adds the `langfuse.observation.*` / `langfuse.trace.*` copies and cuts texts at 8000 characters.
- **Daily token budget (NFR-5):** `Assistant:MaxOwnerTokensPerDay` (1 000 000, at least `MaxConversationTokens`) applies per owner over all conversations in the last 24 hours, so starting a new conversation does not get around the conversation budget.
  - New `IConversationStore.GetOwnerTokensSinceAsync` sums the answer tokens.
  - The turn is refused before any model call with `error: dailyBudgetExceeded`, and the warning log names the budget. Web texts in en / ru / sv.
- **Langfuse (optional):**
  - AppHost (`Langfuse.cs`, `Langfuse:Enabled=true`): Langfuse v3 web and worker, PostgreSQL (the app's pgvector image, no extra download), ClickHouse 25.8, Redis 7 and MinIO (Chainguard image), as in the Langfuse compose file. Data lives in named volumes.
  - Secrets are generated Aspire parameters kept in the AppHost user secrets; `ENCRYPTION_KEY` uses its own hex generator. Headless init creates the organization, the project with its keys and the admin login. Langfuse telemetry is off (`TELEMETRY_ENABLED=false`).
  - ServiceDefaults (`LangfuseExport`): when `Langfuse:*` is set, every signal gets its own OTLP exporter, because `UseOtlpExporter` cannot be combined with a second one. Traces also go through `AiTraceFilter` to the Langfuse OTLP endpoint.
  - API: user ratings go through `IFeedbackScoreSink` → an in-memory queue → `LangfuseScoreSender` → `POST /api/public/scores`, as `user_feedback` scores of the turn's trace with id `feedback-<messageId>`, so a new rating replaces the old one. Without Langfuse the sink is a no-op.
- **Runbook** [docs/runbooks/observability.md](../../runbooks/observability.md): where telemetry goes, the trace tree of a turn, SQL and viewer steps to find a bad, slow or expensive turn, the metrics table, budgets and Langfuse housekeeping. New README section "Observability".
- **Tests:**
  - Unit (+10, 344 in total):
    - `AssistantTelemetryTests`: the daily budget (refused without a model call; only the last 24 hours count); outcome, duration, tool-call, iteration-limit and grounding metrics; a provider failure counted as its error code; turn and tool span tags with sensitive data on and off, including the stored trace id.
    - `LangfuseTests`: the trace filter (AI spans, their descendants and the request root pass; other traffic does not), the exporter options, the score request (URL, Basic auth, body) and the no-op sink.
  - Integration (+4, 139 in total):
    - `ObservabilityApiTests`: the daily budget over HTTP and SQL (another owner is not affected); the trace id from the request's `traceparent` stored with the answer; a rating as a metric and as a score with that trace id; the spans and metrics of a hybrid search.
    - `CommerceMetricsTests`: applied, duplicate and rejected webhooks; order status created → pending → paid.
    - New helpers: `MeterRecorder`, `SpanRecorder`, `RecordingFeedbackScoreSink` (see AGENTS.md).
- **Manual check** (AppHost with `Langfuse__Enabled=true`; chat through the OpenRouter profile, because LM Studio is not installed on the machine right now, so embeddings were unavailable and hybrid search ran degraded):
  - All six Langfuse containers became healthy on the first start.
  - One turn ("Recommend one science fiction movie about space travel") went through `search_movies` to an answer.
  - Langfuse showed one trace "assistant turn" in the conversation's session, with input and output and no orphan spans. The tree:
    - the request span;
    - the agent span;
    - a `chat` generation (2606 in / 77 out);
    - the `search_movies` tool with `search hybrid` inside: `search.embed_query` (the failed embedding call with its retries), `search.keyword`, `search.fusion` and the database queries;
    - a second `chat` generation (3414 in / 122 out).

    That is 6020 input tokens, the same as `message.completed`.
  - The thumbs-up became a `user_feedback` score (1, with the comment).
  - No other traffic reached Langfuse.
  - The Aspire dashboard showed the same trace tree (checked with Playwright), so the dashboard export still works next to Langfuse.

### Deviations and decisions
- "Per-session token budget": the per-conversation budget exists since I11. I18 adds the per-owner daily budget (an owner = the anonymous session or the user). It counts tokens stored with answers. Rolling summaries and turns that failed midway count only for the conversation total, an accepted gap that the conversation budget covers.
- No span per loop iteration: every iteration is exactly one `chat` span under the turn span, so an extra level would add nothing.
- Langfuse gets only AI traces (`AiTraceFilter`), while the Aspire dashboard gets everything. Spans that end after AI work of their trace (the request span) are kept, so the tree in Langfuse has its root.
- Langfuse image tag `3` (the major version, as in the Langfuse compose file) and MinIO `latest` from Chainguard: MinIO no longer publishes community images on Docker Hub. This is for local development only; the encryption key parameter refuses to be written to a publish manifest.
- No new NuGet packages: Langfuse is called over plain HTTP and OTLP (the existing OpenTelemetry exporter).
- No evaluation run: prompts, models and search parameters did not change.

### Follow-ups
- I19: rate limiting of `/api/chat` and a distributed turn lock. The count of `assistant.turns{outcome=conversationBusy}` shows whether the lock is needed.
- I20: a dashboard of these metrics for the deployed app (Grafana or the platform's own), and alerts on `payments.webhooks{result=error}` and on the grounding violation rate.
- Langfuse: prompt management and datasets are not used. Evaluation reports could be pushed as Langfuse scores later.

### Sessions
- S20 (2026-11-19), [prompt log](../../prompts/sessions/2026-11-19_S20_observability-cost.md).
