# 00 — Overview

> Product working name: **FilmSearch** (code namespace `FilmSearch`).

## 1. What it is

An online store that sells digital movies (buy or rent). The customer can:

1. **Search alone**: use the catalog with filters, keyword search and semantic search ("a quiet movie about loneliness in a big city").
2. **Ask the AI assistant** in a chat. The assistant asks clarifying questions, searches the catalog, explains why it picked each movie, adds movies to the cart and leads the customer to checkout.

Payment: first a **fake gateway** (simulated checkout page and webhooks), then **Stripe in test mode** (Checkout Session and webhooks).

Movie data: first a **seed of about 1000 top movies** in a JSON file, then **TMDB API** sync. See [ADR-0003](../adr/0003-movie-data-source-tmdb.md) for why TMDB and not IMDb.

## 2. Why: learning goals

This is a pet project. The goal is to move from "AI-assisted full-stack developer (.NET + React)" to **AI engineer**. The project should be as close to production as practical. The skills it trains:

| Area | Where in the project |
|---|---|
| LLM integration through a provider-agnostic abstraction | AI provider layer (local LM Studio, OpenRouter) |
| Embeddings, vector DB, indexing | pgvector, ingestion pipeline, reindexing |
| RAG: retrieval, hybrid search, reranking | Search, chat answers grounded in the catalog |
| Structured output, query understanding | Natural language to filters (JSON Schema) |
| Tool calling and agents | Sales assistant with tools, confirmation step (human in the loop) |
| Evaluation | Golden datasets, retrieval metrics, LLM-as-judge, regression runs |
| Guardrails and safety | Prompt injection, grounding, off-topic, PII |
| LLMOps and observability | OpenTelemetry GenAI, tokens, latency, cost, tracing |
| Production engineering | Payments, webhooks, idempotency, resilience, CI/CD, deployment |

The full learning map is in [learning-path.md](../learning-path.md).

## 3. Languages

- **Primary language: English.** Code, docs, UI default locale, API messages, commit messages.
- **Russian and Swedish are supported** in:
  - UI: i18n with `en` (default), `ru` and `sv` locales and a language switcher.
  - Search and AI: the customer can type queries and chat in Russian or Swedish. The assistant answers in the language of the customer's message.
  - Catalog data: titles and overviews are stored in `en`, `ru` and `sv` when the source has them (TMDB has translations for all three).
- Supported locales are one list in configuration (`en`, `ru`, `sv`), so adding a language later means resources, translations and evaluation cases, not code changes.
- Consequence for AI: the embedding model must be **multilingual**, so that a Russian or Swedish query finds a movie with an English description. Evaluation datasets contain queries in all three languages.

## 4. Scope

### In scope
- Movie catalog: list, filters, movie page, prices (buy or rent).
- Search: keyword (full-text), semantic (vector), hybrid, and natural language to filters.
- AI chat assistant: streaming, conversation history, tool calling, recommendations with explanations, cart, starting checkout.
- Cart, orders, payment (fake, then Stripe test), entitlements ("My library").
- Data: seed of about 1000 top movies, then TMDB import and sync with re-embedding.
- Authentication (ASP.NET Core Identity) for order history and the library. Before it exists, an anonymous session is used.
- Observability, evaluation, guardrails, CI, containerized deployment.

### Out of scope (non-goals)
- Real video streaming. A purchased movie only appears in the library and is marked as owned or rented on its page.
- Real money and live Stripe mode.
- Admin UI for catalog editing. Admin actions are API endpoints or CLI commands.
- Mobile apps.
- Training our own models. Fine-tuning may appear as a stretch goal.

## 5. Main user scenarios

1. **Self-search.** The user filters by "Drama, 1990–2000, rating > 8", gets a list, opens a movie, chooses "Rent 48h", pays.
2. **Semantic search.** The user types "фильм про путешествие во времени, но без боевиков" in Russian and gets relevant movies with no Action genre.
3. **Assistant.** "Want something for an evening with my wife, we liked Before Sunrise and La La Land." The assistant asks one clarifying question, offers 3–5 movies from the catalog with reasons, the user says "take the second one, buy it", the assistant adds it to the cart and shows a **Go to payment** button. The user clicks it, pays in Stripe, and the movie appears in the library.
4. **Data refresh.** An admin runs TMDB sync. New movies get embeddings and appear in search.

## 6. Glossary

| Term | Meaning |
|---|---|
| Movie | A catalog item (film). |
| Offer | A way to buy a movie: `Buy` (forever) or `Rent` (48 hours), with a price. |
| Entitlement | The right to watch a movie, created after a successful payment. |
| Embedding document | The text built from a movie's fields that gets embedded. |
| Golden set | A labeled set of queries and relevant movies used for evaluation. |
| Provider | An LLM or embeddings backend: LM Studio, OpenRouter, any OpenAI-compatible API. |
| Profile | A named AI config (chat model, embedding model, parameters) that can be switched by config. |

## 7. Spec documents

| # | Document |
|---|---|
| 01 | [Requirements](01-requirements.md): functional and non-functional |
| 02 | [Architecture](02-architecture.md): components, stack, solution layout |
| 03 | [Domain model](03-domain-model.md): entities, DB schema, order state machine |
| 04 | [API](04-api.md): REST and SSE contracts |
| 05 | [Search](05-search.md): keyword, semantic, hybrid, query understanding |
| 06 | [AI assistant](06-ai-assistant.md): agent, tools, prompts, guardrails, providers |
| 07 | [Payments](07-payments.md): fake gateway, Stripe test, webhooks |
| 08 | [Data ingestion](08-data-ingestion.md): seed, TMDB, embeddings pipeline |
| 09 | [Evaluation](09-evaluation.md): datasets, metrics, how to run |
| 10 | [Ops and security](10-ops-security.md): observability, config, secrets, deployment |
