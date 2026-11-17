# FilmSearch — AI-assisted movie store

A learning pet project, built to be as close to production as practical. It is an online store of digital movies with:

- **Search**: filters, keyword (PostgreSQL full-text), semantic (embeddings + pgvector), hybrid (RRF), and natural-language query understanding.
- **AI sales assistant**: a chat that finds movies in the catalog, explains its picks, manages the cart and leads to checkout with an explicit user confirmation.
- **Payments**: a fake gateway first, then Stripe in test mode (Checkout + webhooks).
- **Data**: a seed of about 100 top movies, then TMDB sync.
- **Local-first AI**: LM Studio for chat and embeddings; OpenRouter or any OpenAI-compatible provider through config.
- **Languages**: English UI and data, with Russian supported in the UI and in queries to the AI.

Stack: .NET 10, ASP.NET Core, EF Core, PostgreSQL + pgvector, .NET Aspire, Microsoft.Extensions.AI, React + TypeScript + Vite.

## Status

Preparation is done (spec and plan). Code starts with iteration I01. See the [roadmap](docs/plan/roadmap.md).

## Documentation

| | |
|---|---|
| [Spec](docs/spec/00-overview.md) | What we build and how it should behave |
| [Roadmap](docs/plan/roadmap.md) | Iterations, dependencies, parallel waves, status |
| [ADRs](docs/adr/README.md) | Architecture decisions |
| [Learning path](docs/learning-path.md) | AI-engineering skills mapped to iterations |
| [AGENTS.md](AGENTS.md) | Rules for AI coding agents |
| [Prompt history](docs/prompts/README.md) | All prompts used to build the project, by session |

## Getting started

_Filled in during I01._ Expected prerequisites: .NET 10 SDK, Node.js LTS, Docker Desktop, LM Studio with a chat model and a multilingual embedding model loaded and the local server running.

---

This product uses the TMDB API but is not endorsed or certified by TMDB.
