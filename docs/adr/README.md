# Architecture Decision Records

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
