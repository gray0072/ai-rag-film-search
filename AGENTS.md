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
| Evaluation datasets and reports | `evals/` (from I08) |
| Seed data | `data/seed/` (from I03) |

## Session workflow

1. **Log the prompt first** (see "Prompt logging" below).
2. Find the iteration: the owner names it (e.g. "do I06"), or take the next `todo` iteration from the roadmap whose dependencies are `done`. Read its plan file and the spec sections it links.
3. Set the iteration status to `in progress` in [roadmap.md](docs/plan/roadmap.md) and in the plan file.
4. Work on branch `iter/Ixx-<slug>` (create it from `main` if needed). In a parallel track, work in a git worktree (see the roadmap).
5. Work in small steps. Build and run tests often. Tick tasks in the plan file as they are done.
6. If the plan or spec turns out wrong, **update the doc** in the same branch and say so. Do not silently deviate. A real decision with alternatives gets an ADR.
7. At the end of the iteration: fill in the "Result" section of the plan (what was done, deviations, decisions, follow-ups, session ids), set status `done`, update the README if setup changed.
8. Commit only when the owner asks. Commit message rules are below.
9. **No pull requests.** When the owner asks to push: push the iteration branch, merge it into `main` with `git merge --ff-only` (rebase the branch on `main` first if needed), push `main`, and check the CI run on `main` (`gh run list --branch main`). Fix a red run right away.

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
- EF Core: migrations per module, named after the iteration and change (`I09_AddCommerce`). Never edit an applied migration; if two branches conflict, the second one regenerates its migration (see roadmap).

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
npm run lint && npm run format:check && npm run typecheck && npm test && npm run build   # same as CI
```

Seed file (needs a TMDB key in the tool's user secrets, see [data/seed/README.md](data/seed/README.md)):

```bash
dotnet run --project tools/seed-builder       # writes data/seed/movies.seed.json; cached responses in tools/seed-builder/.cache
```

Expected later: `dotnet run --project evals/FilmSearch.Evals -- --suite search --label <name>` (I08).

Notes:
- Tests run on **Microsoft.Testing.Platform** (`global.json` → `test.runner`), xUnit v3. Pass runner options after `--`, e.g. `dotnet test -- --filter-method "*Health*"`.
- Assertions: **Shouldly** (MIT). FluentAssertions v8 is not used because of its commercial license.
- Integration tests: one shared `pgvector/pgvector:pg17` container per test assembly (`PostgresFixture`), API in-process through `FilmSearchApiFactory` (`WebApplicationFactory<Program>`), migrations applied on startup. Catalog tests use `CatalogApiFixture` (seed loaded once). The database is shared: assert on properties of results, not on global counts; a test that adds many rows gets its own database (`PostgresFixture.CreateDatabaseAsync`).
- Minimal APIs bind enum query parameters case-sensitively. Query enums (`sort`, `offerType`) are strings parsed case-insensitively in the request class; JSON enums are camelCase strings.
- Migrations live in `src/FilmSearch.Infrastructure/Persistence/Migrations`. `DesignTimeDbContextFactory` lets `dotnet ef` work without a running DB or the API. The API applies migrations on startup when `Database:MigrateOnStartup` is true (Development, tests).
- The AppHost Postgres container is named `filmsearch-postgres`, uses `ContainerLifetime.Persistent` and the named volume `filmsearch-postgres-data`: it keeps running after the AppHost stops. Its password is stored in the AppHost user secrets. To reset the DB: `docker rm -f filmsearch-postgres` and `docker volume rm filmsearch-postgres-data`.

## Git

- Branches: `main` (always green), `iter/Ixx-<slug>` per iteration. No pull requests: the branch is fast-forward merged into `main` and pushed (see Session workflow, step 9). CI runs on pushes to `main` and to `iter/**` branches.
- Commit message: **one subject line only**, imperative, English, may start with the iteration id: `I06: add HNSW index for movie embeddings`. No body, no trailers, no `Co-Authored-By`, no mention of AI tools.
- Commit or push only when the owner asks.

## Local environment notes

- OS of the owner: Windows 11; shells: PowerShell and Git Bash.
- LM Studio local server: `http://localhost:1234/v1` (OpenAI-compatible). Model ids are in `appsettings.json` profiles after I05. The `lms` CLI is installed and can run it from the console: `lms server start`, `lms load <model> -y`, `lms ps`, `lms unload --all`, `lms server stop`, `lms ls`. Owner's preferred models: chat `google/gemma-4-e4b`, embeddings `text-embedding-embeddinggemma-300m` (768 dimensions). Unload models and stop the server when a session no longer needs them.
- TMDB credentials are in the user secrets of `tools/seed-builder` (`Tmdb:ApiKey` = v4 read access token, `Tmdb:ApiKeyV3` = v3 key). Never write them to files in the repository.
- Docker Desktop is required for PostgreSQL (Aspire) and Testcontainers.
- `gh` CLI is available for GitHub operations.
- Vitest on Windows: run it from a path with an upper-case drive letter (`D:\Projects\...`). With `d:\...` (the VS Code terminal default) Vitest loads its runner twice and every test file fails with "Vitest failed to find the current suite". CI (Linux) is not affected.
