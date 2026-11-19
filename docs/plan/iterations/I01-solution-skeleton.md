# I01 — Solution skeleton, Aspire, DB, CI

> [Docs](../../README.md) › [Roadmap](../roadmap.md) › I01 · ← [I00](I00-preparation.md) · [I02](I02-web-skeleton.md) → · [Journal: what happened](../../journal/m1-store-without-ai.md#i01--solution-skeleton-aspire-db-ci)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I00 |
| **Parallel with** | — (blocks all others) |
| **Branch** | `iter/I01-skeleton` |
| **Spec** | [02 Architecture](../../spec/02-architecture.md), [10 Ops](../../spec/10-ops-security.md) |
| **Requirements** | NFR-4, NFR-8, NFR-9 |

## Goal
`dotnet run --project src/FilmSearch.AppHost` starts PostgreSQL with pgvector and an empty API with health checks. CI is green. The structure is ready for parallel module work.

## You will learn
Aspire, central package management, Testcontainers, CI for .NET.

## Tasks
- [x] `global.json` (.NET 10 SDK), `Directory.Build.props` (nullable, warnings as errors, latest C#), `Directory.Packages.props`.
- [x] Solution `FilmSearch.slnx` with projects from spec 02 §4 (AppHost, ServiceDefaults, Api, Core, Infrastructure, AI; Worker comes in I16). Project references as described.
- [x] AppHost: PostgreSQL container with the `pgvector/pgvector` image, data volume, database `filmsearch`; Api referencing it.
- [x] Infrastructure: `AppDbContext`, snake_case naming, `CREATE EXTENSION vector` + `pg_trgm` in the first migration; apply migrations on startup in Development.
- [x] Api: ProblemDetails, OpenAPI document, health checks (DB), anonymous owner cookie middleware (`fs_sid`), `IOwnerContext`.
- [x] Module registration pattern: `AddXxxModule()` / `MapXxxEndpoints()` with an empty example module.
- [x] Tests: `FilmSearch.UnitTests`, `FilmSearch.IntegrationTests` with a shared PostgreSQL Testcontainers fixture and `WebApplicationFactory`. One smoke test per project.
- [x] GitHub Actions workflow: restore, build, `dotnet format --verify-no-changes`, tests.
- [x] README: prerequisites and run instructions. Fill in the "Commands" section of AGENTS.md.

## Out of scope
Domain entities (I03), frontend (I02), AI (I05).

## Acceptance criteria
- [x] AppHost starts; Aspire dashboard shows Api and Postgres healthy; `/health` returns 200.
- [x] Integration smoke test starts the API against a Testcontainers DB and calls `/health`.
- [x] CI is green on the PR ([#1](https://github.com/gray0072/ai-rag-film-search/pull/1), run 44134894525, 1 min).
- [x] AGENTS.md "Commands" section filled in with real commands.

## Open questions
- ~~Aspire version and project templates current in Nov 2026~~: Aspire 13.6.5, no templates needed (see Result).
- ~~Assertions library~~: Shouldly (see Result).

## Result

### What was done
- Build setup: `global.json` (SDK 10.0.100+, `latestFeature`, test runner = Microsoft.Testing.Platform), `Directory.Build.props` (net10.0, nullable, warnings as errors, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`), central package management with transitive pinning, `.config/dotnet-tools.json` (dotnet-ef 10.0.15).
- Projects: `FilmSearch.AppHost`, `.ServiceDefaults`, `.Api`, `.Core`, `.Infrastructure`, `.AI` (empty, has `Prompts/`), `tests/FilmSearch.UnitTests`, `tests/FilmSearch.IntegrationTests`. References follow spec 02 §4.
- AppHost: `postgres` (container `filmsearch-postgres`, image `pgvector/pgvector:pg17`, volume `filmsearch-postgres-data`, persistent lifetime), database `filmsearch`, `api` with `WaitFor(database)` and an HTTP health check on `/health`.
- Infrastructure: one `AppDbContext` (entity configurations from the assembly), snake_case naming, migrations history table `public.__ef_migrations_history`, Aspire `EnrichNpgsqlDbContext` (retries, DbContext health check, Npgsql OpenTelemetry). Migration `I01_Initial` creates the `vector` and `pg_trgm` extensions. `DesignTimeDbContextFactory` for `dotnet ef`. Migrations run on startup when `Database:MigrateOnStartup=true` (Development, tests).
- Api: Problem Details (`UseExceptionHandler`, `UseStatusCodePages`), OpenAPI at `/openapi/v1.json` (Development), `AddValidation()`, enums as strings in JSON, HSTS outside Development, `/health` + `/alive` (mapped in all environments for container probes), owner cookie middleware + `IOwnerContext`, example module `AppInfo` (`GET /api/info`).
- Tests (10): unit tests for the owner cookie middleware; integration smoke tests (`/health` 200, extensions created and no pending migrations, `/api/info` JSON + `fs_sid` cookie, unknown route returns `application/problem+json`).
- CI: `.github/workflows/ci.yml` (restore, Release build, format check, tests with Testcontainers). Dependabot for NuGet and GitHub Actions.
- Docs: README (prerequisites, run, test), AGENTS.md "Commands", spec 04 (service endpoints, cookie details).

### Manual checks
- `dotnet run --project src/FilmSearch.AppHost`: dashboard on `https://localhost:17180`; `GET https://localhost:7180/health` returns 200 `Healthy`; `GET /api/info` returns 200 with `Set-Cookie: fs_sid=<32 hex>; max-age=31536000; path=/; secure; samesite=lax; httponly`; `/openapi/v1.json` is served.
- In the Aspire Postgres container: PostgreSQL 17.12, extensions `vector 0.8.7` and `pg_trgm 1.6`, migration `20261117170418_I01_Initial` in the history table.
- `dotnet build` (0 warnings), `dotnet format --verify-no-changes` (clean), `dotnet test` (10/10 passed; integration tests take about 35 s including the container start).

### Decisions and deviations
- **Versions:** .NET 10 LTS packages (EF Core 10.0.15, ASP.NET Core 10.0.15, Microsoft.Extensions.* 10.11.0), although 11.0 packages are already published: the project targets .NET 10 (spec 02). Aspire 13.6.5, Npgsql EF 10.0.3, OpenTelemetry 1.21.0, xUnit v3 4.0.3, Testcontainers 4.17.0. Dependabot ignores major updates of Microsoft / Npgsql / EFCore packages.
- **No Aspire templates or CLI:** the projects are written by hand. The AppHost uses `Aspire.AppHost.Sdk/13.6.5` with the default `AspireUseCliBundle=false` (DCP and the dashboard come from NuGet), warning `ASPIRE010` is suppressed. `Aspire.Hosting.AppHost` is referenced implicitly by the SDK, so it is not in `Directory.Packages.props` (NU1009 otherwise).
- **EF Core pinned centrally** (`Microsoft.EntityFrameworkCore`, `.Relational`), so Npgsql / Aspire transitive versions match `.Design` (MSB3277 conflict otherwise).
- **Test runner:** xUnit v3 4.x runs on Microsoft.Testing.Platform. The .NET 10 SDK no longer runs it through VSTest, so `global.json` opts into the new `dotnet test`, and `Microsoft.NET.Test.Sdk` / `xunit.runner.visualstudio` are not used.
- **Assertions:** Shouldly 4.3.0 (MIT). FluentAssertions v8 needs a commercial license.
- **Owner id:** `Guid` made from 16 bytes of `RandomNumberGenerator` (128 random bits, as the security checklist says); the cookie value uses the `N` format. It is resolved only for `/api/*` paths, so health probes do not get cookies. The cookie is not signed: the id is an unguessable bearer token, like a session id. I17 can use `IdentityUser<Guid>` so `OwnerId` stays a `Guid`.
- **Example module** is `AppInfo` (`/api/info`), not `System`: a `FilmSearch.Api.Modules.System` namespace would shadow the `System` namespace.
- **Health endpoints in all environments** (the Aspire template maps them only in Development): deployment (I20) needs probes, and the response is only the status text.
- **`Pgvector.EntityFrameworkCore`** is not added yet: there are no vector columns until I06. The extension already exists in the database.
- **Analyzers:** `CA1707` (underscores in names) is off for `tests/**`; migrations are marked `generated_code` in `.editorconfig`.
- **Dependabot** was added (security checklist, spec 10 §4); it was not listed in the plan.
- **Spec changes requested by the owner in S02:** Swedish (`sv`) is a supported locale next to `ru` (UI, movie texts, FTS config `swedish`, assistant answers, evaluation cases), and the seed grows from about 100 to about 1000 movies (`data/seed/movies.seed.json`, batched loading). Updated: spec 00, 01, 02, 03, 04, 05, 06, 08, 09; ADR-0001, ADR-0003; AGENTS.md, README; plans I02–I06, I08, I10, I11, I13, I14; roadmap (I03 title).

### Follow-ups
- The `ubuntu-latest` runner moves to Ubuntu 26 (GitHub notice); check the Testcontainers job after the switch.
- `main` was first pushed in S02 (the remote was empty); the default branch is set to `main`.
- I06: add `Pgvector.EntityFrameworkCore` and call `UseVector()` in `DatabaseSetup.Configure`.
- I02: add the web job to CI and the Vite app to the AppHost.
- I20: decide on `UseHttpsRedirection` behind a reverse proxy (health probes over HTTP).

### Sessions
- [S02](../../prompts/sessions/2026-11-17_S02_solution-skeleton.md)
