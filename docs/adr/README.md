# Architecture Decision Records

> [Docs](../README.md) › ADRs

Short records of important decisions: context, decision, consequences. A new decision gets the next number. A changed decision gets a new ADR that supersedes the old one (the old one is kept, with status `Superseded by ADR-XXXX`).

Template: [0000-template.md](0000-template.md).

| ADR | Title | Status |
|---|---|---|
| [0001](0001-postgresql-pgvector.md) | PostgreSQL + pgvector as the only database | Accepted |
| [0002](0002-ai-abstraction-meai.md) | Microsoft.Extensions.AI with OpenAI-compatible providers | Accepted |
| [0003](0003-movie-data-source-tmdb.md) | TMDB as the movie data source (seed first) | Accepted |
| [0004](0004-payment-gateway-abstraction.md) | Payment gateway abstraction: Fake first, then Stripe test mode | Accepted |
| [0005](0005-dotnet-aspire.md) | .NET Aspire for local orchestration and observability | Accepted |
| [0006](0006-agent-own-loop-first.md) | Own agent loop first, frameworks later | Accepted |
| [0007](0007-frontend-stack.md) | Frontend stack: npm, shadcn/ui + Tailwind, OpenAPI-generated client | Accepted |
| [0008](0008-vector-storage-per-dimension.md) | Vector storage: one table, untyped vector column, partial HNSW index per dimension | Accepted |
| [0009](0009-job-queue-in-postgresql.md) | Background jobs: a job table in PostgreSQL, claimed with SKIP LOCKED | Accepted |
| [0010](0010-authentication-identity-cookie.md) | Authentication: ASP.NET Core Identity, own cookie endpoints, merge of anonymous data | Accepted |
| [0011](0011-ai-resilience-and-fallback.md) | AI resilience: a pipeline per profile, a fallback chat profile, keyword search without embeddings | Accepted |
| [0012](0012-deployment-docker-compose-vps.md) | Deployment: Docker Compose on one host, Caddy in front, images from CI, migrations by the Worker | Accepted |

---

**See also:** [Docs hub](../README.md) · [Spec 02 Architecture](../spec/02-architecture.md) · [Guide ch. 2: architecture](../guide/02-architecture.md) · [Journal](../journal/README.md)
