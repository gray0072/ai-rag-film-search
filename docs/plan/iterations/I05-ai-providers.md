# I05 — AI provider layer (LM Studio / OpenRouter)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I01 |
| **Parallel with** | I02, I03 |
| **Branch** | `iter/I05-ai-providers` |
| **Spec** | [06 AI §1](../../spec/06-ai-assistant.md#1-providers-and-profiles), [ADR-0002](../../adr/0002-ai-abstraction-meai.md) |
| **Requirements** | NFR-1, NFR-2, NFR-4, NFR-5 |

## Goal
`IChatClient` and `IEmbeddingGenerator` are registered from config profiles. Switching between LM Studio and OpenRouter is a config change. Calls are traced with token usage. Models for the project are chosen with a small benchmark.

## You will learn
LLM APIs (chat completions, streaming, tool calls, structured output, embeddings) on the wire; Microsoft.Extensions.AI middleware; model selection by measurement.

## Tasks
- [x] `AiOptions` with chat and embedding profiles (spec 06 §1), validation on start.
- [x] Factory that builds `IChatClient` / `IEmbeddingGenerator` from a profile (OpenAI client with custom endpoint); keyed services for named profiles + default from `Active*Profile`.
- [x] Pipeline: OpenTelemetry, logging, usage tracking middleware (tokens, estimated cost from `Pricing`).
- [x] A Development-only HTTP logging handler that shows raw requests / responses once (to see what goes over the wire).
- [x] Health check: provider reachable, model listed (`GET /v1/models`).
- [x] `tools/ai-playground` console (or Development endpoints): chat, streaming, tool call, JSON-schema output, embeddings + cosine similarity of a few en/ru/sv phrases.
- [x] **Model benchmark** (documented in the Result section): 2–3 chat models in LM Studio × tasks (tool call correctness on 10 prompts, valid JSON on 10 prompts, Russian and Swedish quality, tokens/s); 2–3 embedding models × similarity sanity checks (ru ↔ en and sv ↔ en pairs). Pick the defaults.
- [x] OpenRouter profile tested with one cheap model (key in user-secrets).
- [x] Unit tests with a fake `IChatClient`.

## Out of scope
Agent loop (I11–I12), embedding storage (I06).

## Acceptance criteria
- [x] Switching `ActiveChatProfile` from `local` to `openrouter` changes the model without code changes.
- [x] Aspire dashboard shows a trace for an LLM call with model and token counts.
- [x] Benchmark results and chosen models recorded; `appsettings.json` defaults updated.

## Open questions
- Does the chosen local model support parallel tool calls? Note it for I12. → Yes: Gemma 4 E4B returns two `add_to_cart` calls in one response (see the benchmark report). gpt-oss-20b and Qwen 3.8 Flash return one, so the loop must handle both.

## Result

### What was done
- **`FilmSearch.AI` provider layer** (`Providers/`): `AiOptions` (section `AI`: chat and embedding profiles, `Pricing`, `EnableSensitiveData`, `LogRawHttp`, `HealthChecksEnabled`) with a startup validator (active profiles exist, absolute http(s) endpoints, ranges, an `ApiKey` for the active profiles only). `AiClientFactory` builds clients from a profile with the OpenAI .NET client pointed at the profile endpoint (the only place that knows the SDK). `builder.AddAiProviders()` registers every profile as a keyed `IChatClient` / `IEmbeddingGenerator<string, Embedding<float>>` (`KeyedService.AnyKey`) and the active ones unkeyed.
- **Pipeline:** `ConfigureOptions` (profile defaults: model, temperature, max output tokens, reasoning effort) → `UseOpenTelemetry` (source and meter `FilmSearch.AI`, content recorded in Development) → `UseLogging` → `UsageTrackingChatClient` / `UsageTrackingEmbeddingGenerator` (metrics `ai.tokens`, `ai.cost_usd` on meter `FilmSearch`, span tags `filmsearch.ai.profile` / `filmsearch.ai.cost_usd`, one log line, `AiUsage` in `AdditionalProperties` for I11) → OpenAI client with the profile timeout and retry policy → `IHttpClientFactory` client `FilmSearch.AI`.
- **New profile setting `ReasoningEffort`** (→ `ChatOptions.Reasoning` → `reasoning_effort` on the wire), found necessary in the benchmark.
- **`RawHttpLoggingHandler`** (Development + `AI:LogRawHttp`): request JSON and response body, including SSE events of streaming calls (passed through a tee stream), no headers.
- **Health checks** `ai-chat`, `ai-embeddings`: `GET {Endpoint}/models`, model listed → Healthy, otherwise Degraded.
- **API:** `builder.AddAiProviders()` in `Program.cs`, `AI` section in `appsettings.json`, `POST /api/admin/ai/chat` (admin, any profile, returns text, tokens, estimated cost; 400 for an unknown profile or a missing key, 503 when the provider fails). OpenAPI document and web types regenerated. ServiceDefaults traces and meters `FilmSearch.AI`.
- **`tools/ai-playground`** console: `chat`, `stream`, `tools` (hand-written tool loop over fake catalog tools), `json` (`SearchIntent` structured output), `embed`, `bench-chat`, `bench-embed`; model / profile / reasoning / prefix overrides from the command line; shares the API config and user secrets.
- **Model benchmark:** [evals/reports/2026-11-17_I05_model-benchmark.md](../../../evals/reports/2026-11-17_I05_model-benchmark.md): 7 local runs (6 models, ≤ 20 GB and ≤ 20B, latest generation), 5 OpenRouter models (≤ $0.50 per 1M output tokens), 2 embedding models.
- **Tests:** unit 94 (+27: options validation, pricing, prefixes; the whole pipeline against a fake OpenAI-compatible HTTP handler: profile defaults on the wire, call options override, switching `ActiveChatProfile` changes endpoint / key / model, keyed profiles, usage and cost, streaming metrics, GenAI span tags, embeddings, retries, validation on resolve; health checks; raw logging incl. key redaction and SSE passthrough); integration 59 (+3: admin chat with a fake keyed client, unknown profile, empty message). Integration tests switch the AI health checks off. All pass.

### Benchmark summary and chosen defaults
| Profile | Model | Tools | JSON correct | Speed |
|---|---|---|---|---|
| `local` chat | `google/gemma-4-e4b`, `ReasoningEffort: None` | 10/10 | 10/10 | ~48 tokens/s, first text 0.1–0.2 s |
| `openrouter` chat | `deepseek/deepseek-v4.1-flash-1017`, `ReasoningEffort: None` | 10/10 | 10/10 | ~133 tokens/s, $0.0012 per benchmark run |
| `local` embeddings | `text-embedding-embeddinggemma-300m`, 768 dims, query / document prefixes | ru 10/10, sv 8/10 retrieval top-1 | | 20–30 ms per query |

Runner-ups: `google/gemma-4-12b-qat` (same scores, 2.3× slower), `openai/gpt-6-luna` (same scores and cost). Thinking on (`Medium`) made Gemma 4 E4B 6.6× slower with worse JSON; small models (Gemma 4 E2B, Nemotron 3 Nano 4B, gpt-oss-20b) invent movie facts in free answers.

### Manual checks
- Playground against LM Studio: `chat`, `stream` (41 tokens/s), `json`, `embed`, `tools` ("Найди фантастику дешевле 4 долларов и добавь самый новый фильм в корзину" → `search_movies` → `add_to_cart(1, buy)` → answer in Russian), `--raw` shows the request with `response_format.json_schema`, `reasoning_effort` and the SSE `reasoning_content` events.
- OpenRouter: `--chat-profile openrouter` with five models; the same through the API.
- AppHost: `/health` → `Healthy` with LM Studio running. `POST /api/admin/ai/chat` with `profile: local` → gemma-4-e4b answer, 17 + 21 tokens, cost null (no price for a local model); `profile: openrouter` → DeepSeek answer, 13 + 41 tokens, $0.000021; `profile: nope` → 400. The dashboard telemetry HTTP API needs an API key (401), so the trace in the dashboard UI was **not checked by the agent**; the span and its tags are covered by `Chat_call_produces_a_GenAI_span_with_model_tokens_and_profile`, and `FilmSearch.AI` is registered in ServiceDefaults. Owner: open the dashboard → Traces → `POST /api/admin/ai/chat` → the `chat google/gemma-4-e4b` span shows `gen_ai.usage.input_tokens` / `output_tokens`.
- `dotnet build` (0 warnings, Debug and Release), `dotnet format --verify-no-changes`, `dotnet test`; web `gen:api`, lint, format check, typecheck.

### Decisions and deviations
- **Retries in the OpenAI client pipeline, not the resilience handler.** ServiceDefaults gives every HTTP client the standard resilience handler with a 10 s attempt timeout, which would cut local generation. The `FilmSearch.AI` client removes it (`RemoveAllResilienceHandlers`, marked experimental, suppressed with a comment) and uses `ClientRetryPolicy(MaxRetries)` + `NetworkTimeout` of the profile: per-profile settings, honours `Retry-After`.
- **Keyed services with `AnyKey`** instead of registering profile names read at startup: any configured profile resolves by name, unknown names fail on resolve.
- **Health checks report Degraded**, so the store works and Aspire starts the web app without LM Studio.
- **Profile names** `local` for both chat and embeddings (spec example said `local-multilingual`); spec 06 §1 updated with all settings, the pipeline, the enum rule for structured output and the benchmark link. Spec 10 §3 names the `FilmSearch.AI` source.
- **Admin chat endpoint** in addition to the playground: an LLM call inside the Aspire-run API is needed to see a GenAI trace in the dashboard.
- **Packages:** `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.OpenAI` 10.11.0 (ADR-0002; same release train as `Microsoft.Extensions.Http.Resilience` 10.11.0; 11.0.0 is a week old and targets the next .NET train). `OpenAI` 2.14.0 comes with them.
- **Benchmark scope** narrowed by the owner during the session: latest-generation local models ≤ 20 GB and ≤ 20B parameters (Qwen 3.5 and Gemma 3 excluded; runs of larger models were dropped from the report), five cloud models ≤ $0.50 per 1M output tokens.
- **Secrets:** Stripe test keys moved from the files `Stripe public key` / `Stripe secret key` (untracked, deleted) to the API user secrets `Stripe:PublishableKey` / `Stripe:SecretKey` (I15 will read them); the OpenRouter key from `open-router-key` to `AI:ChatProfiles:openrouter:ApiKey`. The playground shares the API `UserSecretsId`.

### Follow-ups
- I06: use the active embedding profile's `FormatQuery` / `FormatDocument` and store `Model` + `Dimensions` with each vector. Swedish queries are the weakest (8/10): check on the real catalog in I08.
- I07: query understanding with enums for language and genre slugs in the schema; Gemma 4 E4B extracts years, prices and genres reliably with `ReasoningEffort: None`.
- I11: store `AiUsage` from `ChatResponse.AdditionalProperties` per message (NFR-5). Streaming responses record metrics only: aggregate `UsageContent` there.
- I12: handle both one and several tool calls per response; `ministral-3-14b-reasoning` ignores `reasoning_effort: none` and GLM 5.3 Flash requires reasoning: check `SupportsTools` / reasoning per model when adding profiles.
- I18: LLM-specific trace viewer; a dashboard API key would let agents read traces.

### Sessions
- [S06](../../prompts/sessions/2026-11-17_S06_ai-providers.md)
