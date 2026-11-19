# AGENTS.md

Instructions for AI coding agents (Claude Code, Codex, Cursor, …) working in this repository. Read this file fully at the start of every session.

## Project in one paragraph

**FilmSearch** is a learning pet project, built to be as close to production as practical: an online store of digital movies with keyword, semantic and hybrid search, and an AI sales assistant that finds movies in the catalog, adds them to the cart and leads the user to payment (fake gateway first, then Stripe test mode). Backend: .NET 10 + ASP.NET Core + EF Core + PostgreSQL/pgvector, orchestrated with .NET Aspire. Frontend: React + TypeScript + Vite. AI: Microsoft.Extensions.AI with OpenAI-compatible providers. Local LM Studio is the default; OpenRouter or similar can be switched on in config. The owner's goal is to grow from full-stack developer to AI engineer, so **explain AI-specific decisions** briefly when you make them.

## Where things are

| What | Where |
|---|---|
| Spec (source of truth for behavior) | [docs/spec/](docs/spec/00-overview.md) |
| Roadmap, iteration status, parallel waves | [docs/plan/roadmap.md](docs/plan/roadmap.md) |
| Plan of each iteration | [docs/plan/iterations/](docs/plan/iterations/) |
| Architecture decisions | [docs/adr/](docs/adr/README.md) |
| Prompt history (one file per session) | [docs/prompts/](docs/prompts/README.md) |
| Learning map | [docs/learning-path.md](docs/learning-path.md) |
| Code layout (target) | [docs/spec/02-architecture.md §4](docs/spec/02-architecture.md#4-solution-layout-target) |
| Evaluation runner, datasets and reports | [evals/](evals/README.md) |
| Runbooks (observability: find a slow or bad turn) | [docs/runbooks/](docs/runbooks/observability.md) |
| Seed data | `data/seed/` (from I03) |
| Background jobs, TMDB import | `src/FilmSearch.Worker`, `src/FilmSearch.Infrastructure/Ingestion` (from I16) |
| Deployment: Dockerfiles, compose, Caddy, deploy scripts, runbook | [deploy/](deploy/README.md) (from I20) |
| Documentation hub, guide, journal, handbook | [docs/README.md](docs/README.md) (I21) |
| Documentation website (VitePress, GitHub Pages) and link checker | [tools/docs-site/](tools/docs-site/README.md) (I21) |

## Session workflow

1. **Log the prompt first** (see "Prompt logging" below).
2. Find the iteration: the owner names it (e.g. "do I06"), or take the next `todo` iteration from the roadmap whose dependencies are `done`. Read its plan file and the spec sections it links.
3. Set the iteration status to `in progress` in [roadmap.md](docs/plan/roadmap.md) and in the plan file.
4. Work directly on `main`. No iteration branches and no pull requests (owner's decision, 2026-11-17). Do not create other branches; delete stray local and remote branches when asked.
5. Work in small steps. Build and run tests often. Tick tasks in the plan file as they are done.
6. If the plan or spec turns out wrong, **update the doc** in the same change and say so. Do not silently deviate. A real decision with alternatives gets an ADR.
7. At the end of the iteration: fill in the "Result" section of the plan (what was done, deviations, decisions, follow-ups, session ids), set status `done`, update the README if setup changed.
8. Commit only when the owner asks. Commit message rules are below.
9. **Before every push, run all checks locally** — do not leave the checking to GitHub. That is: `dotnet build` (warnings are errors), `dotnet format --verify-no-changes`, `git diff --exit-code -- src/web/openapi` after the build, **all** .NET tests (`dotnet test`, unit and integration; start Docker Desktop if it is not running), and in `src/web` `npm run gen:api` (no diff), lint, format check, typecheck, tests and build (see Commands). Push only when everything is green. Then push `main` and check the CI run (`gh run list --branch main`); fix a red run right away.

If a session ends before the iteration is finished, write the current state and next steps in the "Result" section under a "Progress" heading so the next session can continue.

## Prompt logging (required)

Every prompt from the owner is saved to the prompt history, **in English first, then verbatim in the original language**:
- File: `docs/prompts/sessions/YYYY-MM-DD_SNN_<topic>.md`. `SNN` continues the numbering across the project (look at the existing files to find the next number). One file per session; parallel sessions each get their own file.
- Add each new prompt of the session as `## Prompt N` with two ```` ```text ```` blocks: `English:` (a faithful, complete translation) and `Original (ru):` / `Original (sv):` / … (verbatim: do not shorten or fix typos). A prompt written in English gets only the `English:` block. Messages sent mid-turn are separate prompts. Format example: [docs/prompts/README.md](docs/prompts/README.md).
- Replace secrets with `<redacted>`.
- Add the session to the index table in [docs/prompts/README.md](docs/prompts/README.md) and fill in the "Outcome" line at the end.

## Languages

- **English** for code, comments, docs, commit messages, UI default locale, API messages.
- **Russian and Swedish are supported** in the UI (i18n `ru` and `sv` locales) and in search / assistant queries. Every user-facing string goes to `en`, `ru` and `sv` resource files. Evaluation datasets contain English, Russian and Swedish cases.
- The owner may write prompts in Russian. Answer the owner in the language of the prompt; write project files in English.

## Engineering rules

### General
- Follow the spec. Requirement ids (`FR-…`, `NFR-…`) can be referenced in tests and PR descriptions.
- Keep modules separate: a module uses other modules only through interfaces in `FilmSearch.Core`. No cross-module table access.
- Register each module with `AddXxxModule()` / `MapXxxEndpoints()`. Keep `Program.cs` small; this also reduces merge conflicts between parallel iterations.
- Do not add a new package, framework or infrastructure component without a reason written in the iteration Result (or an ADR if it is a significant choice). Package versions live in `Directory.Packages.props`.

### .NET
- .NET 10, nullable enabled, warnings as errors, file-scoped namespaces, `sealed` by default for classes not meant for inheritance.
- Minimal APIs with endpoint groups; `TypedResults`; Problem Details for errors; input validation at the boundary.
- `TimeProvider` instead of `DateTime.UtcNow`; money as integer minor units + currency.
- Async all the way with `CancellationToken`.
- EF Core: migrations per module, named after the iteration and change (`I09_AddCommerce`). Never edit an applied migration; if two parallel sessions both added migrations, the one committed second regenerates its migration (see roadmap).

### AI code
- Business code depends only on `IChatClient` and `IEmbeddingGenerator`. No provider SDK types outside `FilmSearch.AI`'s provider factory.
- Prompts live in versioned files (`src/FilmSearch.AI/Prompts/<name>.vN.md`). A meaningful prompt change creates a new version; the version is logged and stored with messages.
- Structured output: JSON Schema from C# types, validate, retry once with the error, then fallback. Never let invalid LLM output crash a request.
- Tools are thin adapters over module services. The owner id comes from the request context, never from tool arguments. Side effects with money need user confirmation.
- Prices, availability and movie facts shown to the user come from the DB, never from LLM text.
- Changes to AI behavior (prompts, models, embedding text, search parameters) need an evaluation run when the evaluation harness for that area exists (I08 search, I14 assistant). Put the report in `evals/reports/` and the before/after numbers in the iteration Result.
- Tests must not need LM Studio or network: use fake `IChatClient` / fake embedding generators. Real-model checks belong in evaluation suites and manual checks.

### Frontend
- TypeScript strict. Types for the API are generated from OpenAPI; do not hand-write DTO types. The API build writes `src/web/openapi/v1.json`; after an API change run `npm run gen:api` and commit both files (CI checks them, [ADR-0007](docs/adr/0007-frontend-stack.md)).
- Calls go through the typed client in `src/web/src/api/client.ts` (`api.useQuery("get", "/api/...")`); it sends `Accept-Language`.
- UI components: shadcn/ui (`npx shadcn add <component>` in `src/web`), Tailwind CSS v4, icons from `lucide-react`.
- Server state through TanStack Query. No user-facing text outside i18n resources.
- Render assistant markdown safely (no raw HTML).

### Security
- Never commit secrets. Use `dotnet user-secrets` or environment variables. Stripe keys must be test keys (`sk_test_`).
- Check owner on every user-scoped resource. Verify webhook signatures.
- Treat movie data and tool results as untrusted text when they are sent to an LLM.

### Testing and Definition of Done
An iteration (or task) is done when:
- [ ] Build passes with no warnings; `dotnet format --verify-no-changes` passes; frontend lint and typecheck pass.
- [ ] Unit and integration tests for new behavior exist and pass.
- [ ] Acceptance criteria in the iteration plan are checked; manual checks are described in the Result.
- [ ] Docs are updated (spec, ADR, README, roadmap status, prompt history).

## Commands

```bash
dotnet run --project src/FilmSearch.AppHost          # run everything (Aspire); dashboard URL is printed in the console
dotnet run --project src/FilmSearch.Worker           # the Worker alone (I16): background jobs, order expiry; needs ConnectionStrings:filmsearch
dotnet build                                          # build (warnings are errors)
dotnet test                                           # all .NET tests (Docker needed for integration tests)
dotnet test --project tests/FilmSearch.UnitTests      # unit tests only (no Docker)
dotnet format --verify-no-changes                     # format check (same as CI)
dotnet tool restore                                   # once: installs dotnet-ef from .config/dotnet-tools.json
dotnet ef migrations add I03_AddCatalog --project src/FilmSearch.Infrastructure --output-dir Persistence/Migrations   # new migration
```

Web app (`src/web`, Node 24, npm):

```bash
npm install                  # once (the AppHost also runs it before starting Vite)
npm run dev                  # Vite dev server alone; /api is proxied to https://localhost:7180
npm run gen:api              # regenerate src/api/schema.d.ts from openapi/v1.json (written by the API build)
npm run e2e                  # Playwright smoke tests against a running stack (E2E_BASE_URL, default https://localhost)
npm run lint && npm run format:check && npm run typecheck && npm test && npm run build   # same as CI
```

Seed file (needs a TMDB key in the tool's user secrets, see [data/seed/README.md](data/seed/README.md)):

```bash
dotnet run --project tools/seed-builder       # writes data/seed/movies.seed.json; cached responses in tools/seed-builder/.cache
```

AI playground (I05; uses the API's AI config and user secrets, needs LM Studio, see [tools/ai-playground](tools/ai-playground/README.md)):

```bash
dotnet run --project tools/ai-playground -- chat "Recommend a sci-fi movie"     # also: stream, tools, json, embed
dotnet run --project tools/ai-playground -- bench-chat --chat-model <id> --reasoning None --out <file>
dotnet run --project tools/ai-playground -- bench-embed --embedding-model <id> --query-prefix "..." --document-prefix "..."
```

Demo environment (I20; `deploy/`, Docker; scripts run in Git Bash, see [deploy/README.md](deploy/README.md)):

```bash
docker compose -f compose.yaml -f compose.build.yaml build   # in deploy/: build the api, worker and web images from source
SKIP_PULL=1 ./deploy.sh local                                 # backup, migrate, start, wait for health (needs deploy/.env)
docker compose down -v                                        # stop and delete the local stack with its data
dotnet run --project src/FilmSearch.Worker -- migrate         # apply migrations and exit (the deploy's migration step)
```

Documentation (I21; `tools/docs-site`, Node 24, see [tools/docs-site/README.md](tools/docs-site/README.md)):

```bash
node tools/docs-site/check-links.mjs     # every relative link and anchor in all Markdown (CI: Docs workflow)
npm run dev                              # in tools/docs-site: live preview of the website
npm run build                            # in tools/docs-site: static site in dist/, fails on dead page links
```

Evaluations (I08, I14; Docker + LM Studio, a fresh pgvector container per run, see [evals/README.md](evals/README.md)):

```bash
dotnet run --project evals/FilmSearch.Evals -- --suite search --label <name>                      # keyword, semantic, hybrid, hybrid+qu
dotnet run --project evals/FilmSearch.Evals -- --suite search --label <name> --modes hybrid --set Search:Hybrid:RrfK=40
dotnet run --project evals/FilmSearch.Evals -- --suite qu --label <name>                          # query understanding, --repeat 3
dotnet run --project evals/FilmSearch.Evals -- --suite validate                                    # datasets vs seed (also a unit test)
dotnet run --project evals/FilmSearch.Evals -- --suite assistant --label <name> --parallel 4 --judge-profile openrouter --judge-model openai/gpt-6-luna    # I14 scenarios
dotnet run --project evals/FilmSearch.Evals -- --suite adversarial --label <name> --parallel 4 --judge-profile openrouter --judge-model openai/gpt-6-luna  # I14 adversarial
```

Notes:
- Tests run on **Microsoft.Testing.Platform** (`global.json` → `test.runner`), xUnit v3. Pass runner options after `--`, e.g. `dotnet test -- --filter-method "*Health*"`.
- Assertions: **Shouldly** (MIT). FluentAssertions v8 is not used because of its commercial license.
- Integration tests: one shared `pgvector/pgvector:pg17` container per test assembly (`PostgresFixture`), API in-process through `FilmSearchApiFactory` (`WebApplicationFactory<Program>`), migrations applied on startup. Catalog tests use `CatalogApiFixture` (seed loaded once). The database is shared: assert on properties of results, not on global counts; a test that adds many rows gets its own database (`PostgresFixture.CreateDatabaseAsync`).
- Minimal APIs bind enum query parameters case-sensitively. Query enums (`sort`, `offerType`) are strings parsed case-insensitively in the request class; JSON enums are camelCase strings.
- Migrations live in `src/FilmSearch.Infrastructure/Persistence/Migrations`. `DesignTimeDbContextFactory` lets `dotnet ef` work without a running DB or the API. The API applies migrations on startup when `Database:MigrateOnStartup` is true (Development, tests).
- Doc comments (`<summary>`, `<param>`) of types and endpoints that appear in the OpenAPI document must be single-line: the generator joins lines with the OS newline, so a multi-line comment gives `\r\n` in `v1.json` on Windows and fails the CI check "OpenAPI document is up to date".
- Embeddings (I06): `search.movie_embeddings` has an untyped `vector` column and one partial HNSW index per dimension (ADR-0008); the vector query is raw SQL in `VectorIndex` composed with LINQ filters. Integration tests embed with `HashingEmbeddingGenerator` (bag of words, registered for every profile by `FilmSearchApiFactory`): it checks mechanics, not meaning; cross-language quality is checked by hand or in evaluations. On ~1000 rows the planner estimates an exact scan as cheaper than HNSW (it is not, but both take milliseconds); a plan test must switch off `enable_seqscan` **and** `enable_sort` (the btree `(model, locale)` index plus a sort is the next cheapest plan).
- Hybrid search and query understanding (I07): integration tests answer chat calls with `ScriptedChatClient` (`FilmSearchApiFactory.Chat.Script(userMessage, answers…)`; an unscripted message gets a non-JSON answer, so QU falls back). QU results are cached in memory per normalized query for the lifetime of the factory: use a unique query text per test. Tuning numbers of hybrid search and QU are in `evals/reports/2026-11-17_I07_*.md`.
- Auth (I17) integration tests: every `FilmSearchApiFactory` seeds the admin `AuthTestExtensions.AdminEmail`; admin endpoints need `await factory.CreateAdminClientAsync()` (an `https://localhost` client signed in as admin; the auth cookie is `Secure`). `AuthTestExtensions.RegisterAsync(client)` registers a unique user and signs that browser in; `LoginAsync` / `LogoutAsync`. New owner-scoped tables need an `IOwnerMergeHandler` (login merge), with a test in `AuthApiTests`. Web tests: mock `"GET /api/auth/me": aUser()` (or `anonymousUser`) for pages behind `RequireAuth` (the library).
- Commerce (I09) integration tests: every commerce fixture (`CommerceFixtureBase`) has its own database with four test movies, because the expiry job and time travel touch every order of a database. Clients use `https://localhost` (the owner cookie is `Secure`) and do not follow redirects; each `CreateClient()` is a new owner. Fake webhooks go through the TestServer handler and are recorded in `Factory.Webhooks`; `PostWebhookAsync` sends signed events directly (any order, any count). `CommerceTimeFixture` puts a `FakeTimeProvider` into the whole app and turns the expiry service off (tests call `OrderExpiryJob`).
- Web tests (I10): `src/test/mockApi.ts` stubs `fetch` with in-memory routes (`mockApi({ "GET /api/movies/:id": movie, "POST /api/cart/items": (request) => … })`, unknown routes → 404 Problem Details) and has builders (`aMovie`, `aCart`, `anOrder`); render pages with `renderApp(url)`. Mock `@/lib/navigation` to check redirects to the payment page. ESLint fails on letters in JSX text and in `alt` / `title` / `placeholder` / `aria-label` literals: use `t()`.
- Chat UI tests (I13): `src/test/sse.ts` has `sseResponse(events)` (a complete answer) and `controlledSse()` (`answer` as the `mockApi` route, then `send(...events)` / `close()`; aborting the request errors the body like a real fetch), plus the builders `aMessage`, `aConversation`, `aChatMovie`. After `message.completed` the UI reloads `GET /api/chat/conversations/{id}`, so give that route the stored messages. `apiClient` sends the first call of a page load alone (owner cookie race, spec 04).
- `vite.config.ts` takes `root` and the `@` alias from `fs.realpathSync.native`: with a lower-case drive letter in the working directory Vite otherwise served `@/…` modules from `/@fs/d:/…` with a second copy of React (blank page, "Invalid hook call").
- Assistant (I11) integration tests script streaming model calls with `Factory.Chat.ScriptStream(lastUserMessage, call1Contents, call2Contents…)`: one content array per model call of the turn (e.g. a `FunctionCallContent` for `search_movies`, then `TextContent` chunks); use a unique message text per test. SSE bodies are parsed with `AssistantApiTests.ParseEvents`. Unit tests of the agent loop use `AssistantHarness` (scripted model, in-memory store, fake search). Manual requests: `src/FilmSearch.Api/Assistant.http`.
- Assistant tools (I12): unit tests build every tool with fakes (`AssistantHarness.CreateTools()`: in-memory cart, confirmations whose `ResolveAsync` throws). Integration tests of the sales flow (`AssistantCheckoutApiTests`) use a commerce fixture of their own and script the model's `add_to_cart` / `request_checkout` calls with `Chat.ScriptStream`; an order may exist only after `POST .../confirmations/{id}`.
- Assistant evaluations (I14): `--suite assistant` / `--suite adversarial` play the cases through `IAssistantChat` with an `EvalOwnerContext` per case run (see evals/README.md). LM Studio shares one KV cache of the loaded context length between its parallel slots: with `--parallel 4` load the chat model with `lms load google/gemma-4-e4b -c 32768 --parallel 4 -y`, otherwise turns fail with "Context size has been exceeded" (`answered` check, error `providerUnavailable`). Do not build the solution while a run is going: the runner's DLLs are locked.
- Stripe (I15) integration tests: `StripeCommerceFixture` makes Stripe the active provider; the real `StripePaymentGateway` and SDK call `Factory.StripeApi` (`FakeStripeApi`, an in-memory create / get / expire of Checkout Sessions, registered for every factory so no test reaches the network). `PostStripeEventAsync` sends a Checkout Session event signed with the test endpoint secret. Unit tests parse real test mode events captured with the Stripe CLI (`tests/FilmSearch.UnitTests/Commerce/StripeEvents/*.json`); sign them in the test, never commit account names or keys.
- Worker and jobs (I16): the Worker (`src/FilmSearch.Worker`) has no own appsettings: the API's files are linked into its output and it shares the API's user secrets (`Tmdb:ApiKey` lives there). Jobs are rows of `ingestion.jobs` (ADR-0009): the API only enqueues (`IBackgroundJobs`, `IEmbeddingJobs`), the Worker's `JobWorker` claims them. Integration tests host the real Worker composition in-process with `TestWorker.StartAsync(connectionString)` next to a `FilmSearchApiFactory` on the same database (own database per test); TMDB is `TmdbStub` (`tests/fixtures`, recorded and trimmed responses, scriptable status codes per path, shared by unit and integration tests). The integration project references the Worker with `Aliases="worker"` because both hosts have a public `Program`. Store-level queue tests use `JobStore` with a `FakeTimeProvider` (`BackgroundJobsTests`). Do not read `Database.GetConnectionString()` after EF opened a connection: Npgsql strips the password (use the context's own connection, as `ReindexLock` does).
- Observability (I18) tests: `MeterRecorder(factory.Services, "instrument", …)` records only the meters of that app's `IMeterFactory` (meters carry their factory as scope), so parallel tests do not count; unit tests use `TestMeterFactory.Owns`. `SpanRecorder` gives a random trace id; send its `TraceParent` as the `traceparent` header, and the server spans of that request land in `Spans`. The integration factory replaces the chat and embedding clients with fakes, so there are no GenAI `chat` / `embeddings` spans there (the provider pipeline is tested in `AiProviderPipelineTests`). Langfuse scores go to `Factory.FeedbackScores`. Langfuse in the AppHost is optional (`Langfuse:Enabled`); a killed AppHost leaves its containers running: `docker rm -f $(docker ps -q --filter name=langfuse)`.
- Resilience (I19) tests: `FilmSearchApiFactory` turns rate limiting off (`RateLimiting:Enabled=false`); `RateLimitingApiTests` turn it on with small limits, one factory per test (limiter state). Pass `configureServices` to the factory for a failing client of one profile: a keyed registration (`"local"`) wins over the `AnyKey` fakes. `SwitchableChatClient` fails like a provider (refused, timed out, circuit open); the fallback needs `AI:FallbackChatProfile` plus that profile's `ApiKey`. Unit tests of the real HTTP pipeline use `AiTestHost` (every HTTP client ends in `FakeProviderHandler`, `Delay` for a slow model; base config sets `RetryDelayMs` 1) and record metrics with `OwnedMeasurements(TestMeterFactory)`, never process-wide. Search no longer returns 503 when embeddings fail: assert `degraded`. Query vectors are cached per normalized text for the factory's lifetime: a degraded-search test needs a query text that no earlier test embedded.
- Testing Cyrillic or Swedish queries with `curl -d "..."` from Git Bash on Windows mangles the text; send a UTF-8 file (`--data-binary @query.json`) or use a script.
- Demo environment (I20): environment name `Demo` (`appsettings.Demo.json`, linked into the Worker too), not `Production`, because the fake payment gateway is refused in Production. Images are chiseled (no shell): containers have no `curl`; `deploy.sh` checks health through the Caddy container (`wget`). The API skips `UseHttpsRedirection` when `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is set (behind Caddy). An EF Core migrations bundle failed to load `System.Diagnostics.DiagnosticSource` 11 (OpenTelemetry): migrations run as `FilmSearch.Worker migrate` instead (ADR-0012). Embeddings and chat run on OpenRouter; `CHAT_PROFILE=local` / `EMBEDDING_PROFILE=local` in `deploy/.env` use LM Studio on the host through `host.docker.internal` (no Ollama). On Windows port 80 may be taken by `System`: `HTTP_PORT=8080` in `deploy/.env`. Caddy holds requests up to 30 s while the API restarts. OpenRouter lists embedding models under `/embeddings/models` (profile `ModelsPath`), not `/models`.
- Documentation (I21): Markdown in `docs/` is the single source; the website renders it as is. Pages start with a breadcrumb line under the title (`> [Docs](../README.md) › …`) and stable pages end with Previous / Next / See also links; iteration plans and session logs carry their navigation in the breadcrumb line, so appended text stays at the end. Use relative links; links to files outside `docs/` become GitHub links on the site. A raw tag in prose (`<Title>` outside backticks) breaks the site build. Run the link checker after moving or renaming any Markdown file or heading.
- Playwright (I20): `src/web/e2e`, config `playwright.config.ts`, typed by `tsconfig.e2e.json`, excluded from Vitest. Install the browser once with `npx playwright install chromium`. With `CI` set it retries once and writes `playwright-report/`.
- The AppHost Postgres container is named `filmsearch-postgres`, uses `ContainerLifetime.Persistent` and the named volume `filmsearch-postgres-data`: it keeps running after the AppHost stops. Its password is stored in the AppHost user secrets. To reset the DB: `docker rm -f filmsearch-postgres` and `docker volume rm filmsearch-postgres-data`.

## Git

- Only one branch: `main` (always green). No iteration branches, no pull requests; commits go straight to `main` after the local checks (see Session workflow, step 9). CI runs on pushes to `main`.
- Commit message: **one subject line only**, imperative, English, may start with the iteration id: `I06: add HNSW index for movie embeddings`. No body, no trailers, no `Co-Authored-By`, no mention of AI tools.
- Commit or push only when the owner asks.

## Local environment notes

- OS of the owner: Windows 11; shells: PowerShell and Git Bash.
- LM Studio: the app is `C:\Program Files\LM Studio\LM Studio.exe` (older installs may have left traces elsewhere; use this one), the CLI `%USERPROFILE%\.lmstudio\bin\lms.exe`, models in `%USERPROFILE%\.lmstudio`. Local server: `http://localhost:1234/v1` (OpenAI-compatible). With the server stopped, a refused connection to `localhost` takes about 2 s per attempt on Windows, so the first AI call of an outage takes ~10 s until the circuit opens (I19). Gemma 4 / Qwen 3.5 think before answering unless the request has `reasoning_effort: "none"` (profile `ReasoningEffort: None`); a hung LM Studio queue (requests left from a killed process) is cleared with `lms unload --all` and loading again. On battery power the laptop runs models on the CPU and calls time out. Model ids are in `appsettings.json` profiles after I05. The `lms` CLI is installed and can run it from the console: `lms server start`, `lms load <model> -y`, `lms ps`, `lms unload --all`, `lms server stop`, `lms ls`. Owner's preferred models: chat `google/gemma-4-e4b`, embeddings `text-embedding-embeddinggemma-300m` (768 dimensions). Unload models and stop the server when a session no longer needs them.
- Stripe CLI is installed (winget, user scope; `stripe`, at `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Stripe.StripeCli_Microsoft.Winget.Source_8wekyb3d8bbwe\stripe.exe` if a shell started before the install). It is not logged in: pass `--api-key` with the test key from user secrets. `stripe listen` requires `--events` (list in README "Payments: Stripe test mode"); its signing secret is stable and stored as `Stripe:WebhookSecret`. Stripe Checkout's form is inside the `elements-inner-habanero` iframe (inputs `#payment-numberInput`, `#payment-expiryInput`, `#payment-cvcInput`, `#payment-countryInput`; 3-D Secure button `#test-source-authorize-3ds`) for browser checks with Playwright.
- Stripe test keys are in the API user secrets (`Stripe:SecretKey`, `Stripe:WebhookSecret`; `Stripe:PublishableKey` is stored but not read by the code, since Checkout is a hosted page). `Payments:Provider` is `Fake` unless set to `Stripe` (user secrets or environment). An OpenRouter key, when the owner adds one, goes to `AI:ChatProfiles:openrouter:ApiKey` in the same place.
- TMDB credentials are in the user secrets of `tools/seed-builder` (`Tmdb:ApiKey` = v4 read access token, `Tmdb:ApiKeyV3` = v3 key). Never write them to files in the repository.
- Docker Desktop is required for PostgreSQL (Aspire) and Testcontainers.
- `gh` CLI is available for GitHub operations.
- Vitest on Windows: run it from a path with an upper-case drive letter (`D:\Projects\...`). With `d:\...` (the VS Code terminal default) Vitest loads its runner twice and every test file fails with "Vitest failed to find the current suite". CI (Linux) is not affected.
