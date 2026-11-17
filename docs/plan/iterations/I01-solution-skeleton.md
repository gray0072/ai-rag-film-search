# I01 — Solution skeleton, Aspire, DB, CI

| | |
|---|---|
| **Status** | todo |
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
- [ ] `global.json` (.NET 10 SDK), `Directory.Build.props` (nullable, warnings as errors, latest C#), `Directory.Packages.props`.
- [ ] Solution `FilmSearch.slnx` with projects from spec 02 §4 (AppHost, ServiceDefaults, Api, Core, Infrastructure, AI; Worker comes in I16). Project references as described.
- [ ] AppHost: PostgreSQL container with the `pgvector/pgvector` image, data volume, database `filmsearch`; Api referencing it.
- [ ] Infrastructure: `AppDbContext`, snake_case naming, `CREATE EXTENSION vector` + `pg_trgm` in the first migration; apply migrations on startup in Development.
- [ ] Api: ProblemDetails, OpenAPI document, health checks (DB), anonymous owner cookie middleware (`fs_sid`), `IOwnerContext`.
- [ ] Module registration pattern: `AddXxxModule()` / `MapXxxEndpoints()` with an empty example module.
- [ ] Tests: `FilmSearch.UnitTests`, `FilmSearch.IntegrationTests` with a shared PostgreSQL Testcontainers fixture and `WebApplicationFactory`. One smoke test per project.
- [ ] GitHub Actions workflow: restore, build, `dotnet format --verify-no-changes`, tests.
- [ ] README: prerequisites and run instructions. Fill in the "Commands" section of AGENTS.md.

## Out of scope
Domain entities (I03), frontend (I02), AI (I05).

## Acceptance criteria
- [ ] AppHost starts; Aspire dashboard shows Api and Postgres healthy; `/health` returns 200.
- [ ] Integration smoke test starts the API against a Testcontainers DB and calls `/health`.
- [ ] CI is green on the PR.
- [ ] AGENTS.md "Commands" section filled in with real commands.

## Open questions
- Aspire version and project templates current in Nov 2026: check before starting.
- Assertions library: FluentAssertions (license changed in v8) vs Shouldly. Decide and note in AGENTS.md.
