# Product owner guide

> [Docs](../README.md) › [Handbook](README.md) › Product owner

This page is for the person who decides what FilmSearch should do and whether it is good enough: a product owner or
manager. It lists what the product can do today, how good, fast and expensive it is (with numbers from the evaluation
reports), which levers you can pull without a code change and who pulls them, what to watch after a change, and the
known limitations. Technical terms are explained where they first appear.

**Contents:** [Capabilities](#what-the-product-can-do) · [Quality](#quality-in-numbers) · [Speed](#speed) ·
[Cost](#cost) · [Levers without code](#levers-that-need-no-code) · [Changes that need code](#changes-that-need-code) ·
[What to watch](#what-to-watch) · [Known limitations](#known-limitations-and-open-follow-ups)

## What the product can do

| Area | Today | Customer view |
|---|---|---|
| Catalog | 1000 well-known movies from a committed seed file, more by TMDB import (1182 after one test import); titles and descriptions in English, Russian, Swedish | [Customer guide: catalog](customer.md#catalog-browse-filter-sort) |
| Search | Keyword, semantic (by meaning) and hybrid (both fused; the default), plus "understand the query": an AI model turns "not an action movie, after 1990" into filters | [Customer guide: search](customer.md#search) |
| Assistant | A chat that searches the catalog, explains its picks, shows movie cards with real prices, edits the cart and prepares the order; the customer must press **Go to payment** | [Customer guide: assistant](customer.md#the-assistant) |
| Commerce | Cart, orders, buy or rent (48 h), library; a simulated payment page or Stripe in test mode; no real money | [Customer guide: payment](customer.md#cart-and-payment) |
| Accounts | Email and password; a guest's cart, purchases and chats move to the account on sign-in; one admin account | [Customer guide: account](customer.md#my-library-and-your-account) |
| Operations | Admin API for imports and the search index, budgets and rate limits, telemetry, Docker deployment | [Administrator](administrator.md), [Operator](operator.md) |

Out of scope by design ([spec 00 §4](../spec/00-overview.md#4-scope)): real money, video streaming, an admin UI for
editing the catalog, mobile apps.

## Quality in numbers

**Search** is measured on a golden set: 105 queries (35 each in English, Russian and Swedish) with the movies a person
marked as right. *Recall@10* = share of the right movies found in the top 10; *MRR@10* = how high the first right movie
is (1.0 = always first); *nDCG@10* = both together, weighted by how right each movie is. 1.0 is perfect for all three.

| Mode (1000-movie seed) | Recall@10 | MRR@10 | nDCG@10 | Source |
|---|---|---|---|---|
| Keyword | 0.296 | 0.251 | 0.233 | [I16 before](../../evals/reports/2026-11-18_search_i16-before-1000-seed.md) |
| Semantic, EmbeddingGemma 300M (local) | 0.865 | 0.794 | 0.784 | same |
| Hybrid, EmbeddingGemma (the development default) | 0.873 | 0.788 | 0.786 | same |
| Hybrid + query understanding (Gemma 4 E4B) | 0.870 | 0.827 | 0.812 | same |
| Semantic, Qwen3-Embedding-8B (OpenRouter, the demo) | 0.917 | 0.921 | 0.881 | [I20 Qwen3](../../evals/reports/2026-11-19_search_i20-openrouter-qwen3-embedding-8b.md) |
| Hybrid, Qwen3-Embedding-8B (the demo) | **0.927** | 0.914 | 0.885 | same |

Qwen3 helps most in Russian (Recall@10 0.94 vs 0.86) and Swedish (0.90 vs 0.82)
([journal I20](../journal/m5-production-ready.md#i20--containers-and-deployment)). Query understanding returns a
valid answer 100% of the time and gets every field exactly right in 83% of cases
([QU baseline](../../evals/reports/2026-11-17_qu_baseline.md)). Growing the catalog by 18% cost about 0.01 Recall@10,
mostly because the golden set does not know the new movies ([catalog growth](../../evals/reports/2026-11-18_I16_catalog-growth.md)).

**Assistant** is measured with 34 scripted conversations (*scenarios*: recommend, follow-up, cart, checkout, …) and 20
*adversarial* cases (prompt injection, off-topic, "pay without the button", invented movies), each run 3 times. A
*grounding violation* is an answer that names a movie no search returned. The *judge* is another AI model that scores
each conversation 1–5. Final runs with prompt v3 ([I14 report](../../evals/reports/2026-11-18_I14_assistant-evaluation.md)):

| | Gemma 4 E4B (local, free) | DeepSeek V4.1 Flash (OpenRouter) |
|---|---|---|
| Scenario pass rate | 78% ± 4 | **92% ± 2** |
| Adversarial pass rate | 88% ± 3 | **98% ± 3** |
| Grounding violations, scenarios / adversarial | 2.3% / 0% | 3.8% / 3.4% |
| Judge score (1–5) | 4.00 | 4.33 |
| Orders created without the customer's click | 0 | 0 |

The recommendation from I14: DeepSeek V4.1 Flash for real users, Gemma 4 E4B for development. The demo configuration
(`appsettings.Demo.json`) uses the `openrouter` profile, which is DeepSeek V4.1 Flash. No later assistant evaluation
exists yet. Background: [guide ch. 11 Evaluation](../guide/11-evaluation.md).

## Speed

| What | Typical (p50) | Slow (p95) | Source |
|---|---|---|---|
| Keyword search | 61 ms | 110 ms | [I16 before](../../evals/reports/2026-11-18_search_i16-before-1000-seed.md) |
| Hybrid search, local embeddings | 79 ms | 111 ms | same |
| Hybrid search with query understanding, local model | 3.4 s | 4.1 s | same (the result is cached per query for 24 h) |
| Semantic search, Qwen3 on OpenRouter (new query) | 577 ms | 4.6 s | [I20 Qwen3](../../evals/reports/2026-11-19_search_i20-openrouter-qwen3-embedding-8b.md) (query vectors are cached) |
| Assistant answer, Gemma 4 E4B on a laptop GPU | 21.6 s | 29.4 s | [I14 report](../../evals/reports/2026-11-18_I14_assistant-evaluation.md) |
| Assistant answer, DeepSeek V4.1 Flash | 3.2 s | 9.1 s | same |

Search times are measured inside the server, without the network. When the AI provider goes down, the first request
waits about 10 s, then the system fails fast (about 0.2 s) and search falls back to keyword results
([journal I19](../journal/m5-production-ready.md#i19--resilience-caching-rate-limiting)).

## Cost

AI models are paid per *token* (about ¾ of a word). Every answer stores its tokens and an estimated cost computed from
the price table `AI:Pricing`.

| Item | Cost | Source |
|---|---|---|
| Assistant answer, Gemma 4 E4B local | $0 (about 5 600 input tokens; your electricity) | [guide ch. 13](../guide/13-observability-and-cost.md#cost-control) |
| Assistant answer, DeepSeek V4.1 Flash | about $0.0004 (about 7 000 input tokens) | [I14 report](../../evals/reports/2026-11-18_I14_assistant-evaluation.md) |
| DeepSeek V4.1 Flash price | $0.036 per million input tokens, $0.50 per million output tokens | `AI:Pricing` in [appsettings.json](../../src/FilmSearch.Api/appsettings.json) |
| Embedding the whole catalog with Qwen3-Embedding-8B | less than $0.01 | [deploy runbook](../../deploy/README.md#operate) |
| Demo cap: 5M tokens a day for all users | about $0.20–0.30 a day | [local setup](local-setup.md#demo-environment) |

Budgets stop spending before a model is called ([guide ch. 12](../guide/12-guardrails.md)):

| Budget | Setting | Development | Demo | Customer sees |
|---|---|---|---|---|
| Per conversation | `Assistant:MaxConversationTokens` | 300 000 | 100 000 | "This conversation is too long…" |
| Per visitor per 24 h | `Assistant:MaxOwnerTokensPerDay` | 1 000 000 | 200 000 | "You have used the assistant a lot today…" |
| Whole service per 24 h | `Assistant:MaxTotalTokensPerDay` | 0 (off) | 5 000 000 | "The assistant has reached its usage limit for today…" |

Query understanding in search is not counted in these budgets (cached and rate limited instead). The demo's OpenRouter
key also has its own credit limit as a hard cap.

## Levers that need no code

All of these are settings: the operator changes them in `appsettings.{Environment}.json`, user secrets or `deploy/.env`
and restarts the service. Defaults and the exact place of each one are in the [configuration reference](configuration.md).

| Lever | Setting | Default | Decide | Change |
|---|---|---|---|---|
| Token budgets | `Assistant:MaxConversationTokens`, `MaxOwnerTokensPerDay`, `MaxTotalTokensPerDay` | see the table above | Product owner | Operator |
| Rate limits (requests per minute) | `RateLimiting:Search` / `Chat` / `NewConversation` | 60 / 20 / 10 (demo 30 / 10 / 5) | Product owner | Operator |
| Chat model | `AI:ActiveChatProfile` (`local`, `openrouter`) | `local` (demo `openrouter`) | Product owner, with an evaluation by the AI engineer | Operator (key) |
| Backup chat model when the main one is down | `AI:FallbackChatProfile` | off | Product owner (it costs money per answer) | Operator |
| Search model (embeddings) | `AI:ActiveEmbeddingProfile` + a reindex | `local` (demo `openrouter`) | AI engineer | Administrator |
| Payment provider | `Payments:Provider` (`Fake` / `Stripe`, test keys only) | `Fake` | Product owner | Operator, [administrator](administrator.md#switch-the-payment-provider) |
| Time to pay an order | `Commerce:PaymentTimeoutMinutes` | 30 | Product owner | Operator |
| Time to press the assistant's **Go to payment** | `Assistant:ConfirmationTtlMinutes` | 15 | Product owner | Operator |
| Movies per assistant search | `Assistant:SearchResultLimit` | 6 (max 10) | AI engineer | Operator |
| Daily TMDB import | `Tmdb:Schedule` (`Enabled`, `TimeUtc`, `List`, `Pages`) | off; 03:00, popular, 5 pages | Product owner | Operator |
| One-off catalog growth | admin API, a TMDB list or ids | — | Product owner | [Administrator](administrator.md#add-500-movies-from-tmdb) |
| Languages with search vectors | `Search:Embeddings:Locales` | `en` | AI engineer | Administrator (reindex) |

## Changes that need code

- **Default search mode** (hybrid, with "Understand the query" on): set in the web app, not in configuration.
- **Prices and the rental period**: a pricing rule in code (`PricingRule`): buy $7.99–$14.99 and rent $2.99–$4.99 by
  the movie's age, plus $2 / $1 for movies with 10 000+ votes; rentals last 48 hours. There is no price editor.
- **Assistant behavior** (what it says, when it asks, how it sells): the system prompt, a versioned file. A change needs
  an evaluation run first ([AI engineer](ai-engineer.md)).
- **Search tuning** (`Search:Hybrid:*`) is configuration, but the project rules require an evaluation report before
  any change to it (or to models and prompts).

## What to watch

| Signal | Where | Read it as |
|---|---|---|
| 👍 / 👎 on answers | table `assistant.message_feedback`; Langfuse `user_feedback` scores; metric `assistant.feedback` | Satisfaction; every 👎 leads to the trace of its turn ([runbook](../runbooks/observability.md#find-a-turn)) |
| Turns by outcome | metric `assistant.turns` (`answered`, `budgetExceeded`, `providerUnavailable`, …) | Customers hitting limits or outages |
| Grounding violations | metric `assistant.grounding_violations` | Answers naming movies the search did not return |
| Spend | metrics `ai.tokens`, `ai.cost_usd`; `assistant.conversations`; OpenRouter activity page | Cost per model and per day |
| Search health | `search.requests{degraded=true}`, `search.latency` | Embedding provider down, slow search |
| Payments | `payments.webhooks{result=error}`, `orders.status_changes` | Payments that fail or never reach `paid` |
| Backup model use | `ai.fallbacks` | The main model is failing; each switch may cost money |

The metrics are in the Aspire dashboard (local) or the optional dashboard of the demo; there is no ready-made business
dashboard or alerting yet. How to read a single turn: [observability runbook](../runbooks/observability.md).

## Known limitations and open follow-ups

- **No public demo yet.** Everything up to a server is built and was run production-like locally; renting the server was
  postponed ([journal M5](../journal/m5-production-ready.md)).
- **Assistant weak spots** ([I14](../../evals/reports/2026-11-18_I14_assistant-evaluation.md#follow-ups)): Gemma cannot
  remove a cart line by title; "add the first one" without buy or rent adds the buy offer (both models); Gemma declines
  some English off-topic questions in Swedish; explanations of picks are generic (lowest judge score); cards show every
  search result, also the ones the answer skips.
- **Search gaps:** typos in titles miss in every mode (`gudfadren`, `godfahter`); Swedish is the weakest language with
  local embeddings; the golden set only knows the seed, so quality on a grown catalog is underestimated
  ([I08](../plan/iterations/I08-search-evaluation.md), [I16](../plan/iterations/I16-tmdb-sync-worker.md)).
- **Commerce:** no refunds; prices are a fixed rule; no video player.
- **Accounts:** no password reset or email confirmation; no rate limit on sign-in attempts across accounts
  ([I17](../plan/iterations/I17-authentication.md)).
- **Spend:** query understanding is outside the token budgets ([I20](../plan/iterations/I20-deployment.md)).
- **Catalog sync:** the daily import re-reads list pages instead of TMDB's change feed; the job table is never cleaned.
- **Operations:** no alerts; backups are not copied off the server yet.

---

**See also:** [Customer](customer.md) · [Administrator](administrator.md) · [Operator](operator.md) ·
[Developer](developer.md) · [AI engineer](ai-engineer.md) · [Configuration](configuration.md) ·
[Guide: the business problem](../guide/01-business-problem.md) · [Guide: evaluation](../guide/11-evaluation.md) ·
[Guide: observability and cost](../guide/13-observability-and-cost.md) · [Journal](../journal/README.md) ·
[Roadmap](../plan/roadmap.md) · [Spec: requirements](../spec/01-requirements.md)
