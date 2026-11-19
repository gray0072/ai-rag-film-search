# Prompt history

> [Docs](../README.md) › Prompt history

Every prompt the owner sends to an AI agent in this repo is saved here **verbatim**, one file per session.
Agents do this themselves; see the "Prompt logging" rule in [AGENTS.md](../../AGENTS.md).

## Why

- To see how the project was built and which prompts worked.
- To review and improve prompting over time. This is part of the learning goal.

## Format

File name: `sessions/YYYY-MM-DD_SNN_<short-topic>.md`. `SNN` is a session number that runs across the whole project (S01, S02, …).

````markdown
# Session SNN — YYYY-MM-DD — <topic>

- **Iteration:** I05 (AI provider layer)
- **Agent:** Claude Code / Codex / …
- **Outcome:** <one or two lines, filled in at the end of the session>

---

## Prompt 1

English:

```text
<the prompt in English: a faithful translation, or the prompt itself if it was written in English>
```

Original (ru):

```text
<verbatim prompt, including typos, in the original language>
```

## Prompt 2
...
````

Rules:
- English comes first: a faithful, complete translation (no summarizing). Then the original, copied exactly: no fixes of typos or formatting. The heading names the language: `Original (ru):`, `Original (sv):`, ….
- A prompt written in English is saved once, under `English:`, without an original block.
- Messages sent mid-turn count as separate prompts.
- Never save secrets (API keys etc.). Replace them with `<redacted>`.
- If several agents work in parallel (worktrees), each session gets its own file.

## Index

| Session | Date | Iteration | Topic |
|---|---|---|---|
| [S01](sessions/2026-11-17_S01_preparation.md) | 2026-11-17 | I00 | Project preparation: spec, plan, AGENTS.md |
| [S02](sessions/2026-11-17_S02_solution-skeleton.md) | 2026-11-17 | I01 | Solution skeleton, Aspire, DB, CI |
| [S03](sessions/2026-11-17_S03_web-skeleton.md) | 2026-11-17 | I02 | Web app skeleton: React, i18n, API client |
| [S04](sessions/2026-11-17_S04_catalog-seed.md) | 2026-11-17 | I03 | Catalog module and movie seed |
| [S05](sessions/2026-11-17_S05_keyword-search.md) | 2026-11-17 | I04 | Keyword search and filters |
| [S06](sessions/2026-11-17_S06_ai-providers.md) | 2026-11-17 | I05 | AI provider layer, model benchmark |
| [S07](sessions/2026-11-17_S07_embeddings-semantic-search.md) | 2026-11-17 | I06 | Embeddings and semantic search |
| [S08](sessions/2026-11-17_S08_hybrid-query-understanding.md) | 2026-11-17 | I07 | Hybrid search and query understanding |
| [S09](sessions/2026-11-17_S09_search-evaluation.md) | 2026-11-17 | I08 | Search evaluation harness |
| [S10](sessions/2026-11-18_S10_cart-orders-fake-payment.md) | 2026-11-18 | I09 | Cart, orders, fake payment |
| [S11](sessions/2026-11-18_S11_store-ui.md) | 2026-11-18 | I10 | Store UI: catalog, search, cart, checkout |
| [S12](sessions/2026-11-18_S12_linear-history.md) | 2026-11-18 | — | Linear git history (merge commits removed) |
| [S13](sessions/2026-11-18_S13_chat-foundation.md) | 2026-11-18 | I11 | Chat foundation: conversations, streaming, grounded answers |
| [S14](sessions/2026-11-18_S14_agent-tools.md) | 2026-11-18 | I12 | Agent: tools, cart, checkout confirmation |
| [S15](sessions/2026-11-18_S15_chat-ui.md) | 2026-11-18 | I13 | Chat UI |
| [S16](sessions/2026-11-18_S16_assistant-eval-guardrails.md) | 2026-11-18 | I14 | Assistant evaluation and guardrails |
| [S17](sessions/2026-11-18_S17_stripe-test-mode.md) | 2026-11-18 | I15 | Stripe test mode |
| [S18](sessions/2026-11-18_S18_tmdb-sync-worker.md) | 2026-11-18 | I16 | TMDB sync and Worker |
| [S19](sessions/2026-11-18_S19_authentication.md) | 2026-11-18 | I17 | Authentication (Identity) |
| [S20](sessions/2026-11-19_S20_observability-cost.md) | 2026-11-19 | I18 | Observability and cost tracking |
| [S21](sessions/2026-11-19_S21_resilience-caching.md) | 2026-11-19 | I19 | Resilience, caching, rate limiting |
| [S22](sessions/2026-11-19_S22_deployment.md) | 2026-11-19 | I20 | Containers and deployment |
| [S23](sessions/2026-11-19_S23_remove-ollama.md) | 2026-11-19 | — | Remove Ollama: demo embeddings on OpenRouter or LM Studio |
| [S24](sessions/2026-11-19_S24_documentation.md) | 2026-11-19 | I21 | Documentation system: README, journal, RAG guide, role guides, GitHub Pages |
| [S25](sessions/2026-11-19_S25_guide-core.md) | 2026-11-19 | I21 | Guide: core chapters 01–14 |
| [S26](sessions/2026-11-19_S26_guide-advanced-handbook.md) | 2026-11-19 | I21 | Guide: advanced chapters, handbook role pages |
| [S27](sessions/2026-11-19_S27_docs-site.md) | 2026-11-19 | I21 | Documentation website (VitePress, GitHub Pages), link checker |
| [S28](sessions/2026-11-19_S28_admin-ui.md) | 2026-11-19 | I22 | Admin UI, search defaults setting, image labels |

---

**See also:** [Roadmap](../plan/roadmap.md) · [Journal](../journal/README.md) · [Docs hub](../README.md)
