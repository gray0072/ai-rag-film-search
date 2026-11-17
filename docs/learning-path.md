# Learning path: from full-stack developer to AI engineer

What an AI engineer does: builds products on top of foundation models. That means choosing and integrating models, building retrieval (RAG), designing tools and agents, **measuring quality** with evaluations, keeping systems safe, observable and affordable, and shipping them. This project covers each area. The table shows where.

## Skill map

| # | Skill | Iterations | What to be able to explain after |
|---|---|---|---|
| 1 | LLM API basics: messages, roles, temperature, tokens, streaming, context window | I05, I11 | What is in a chat completions request and response; why output differs between runs; how tokens and cost are counted. |
| 2 | Provider abstraction, local vs hosted models | I05, I19 | Trade-offs of local (privacy, cost, speed on your GPU) vs hosted (quality, latency, price); how to switch without code changes. |
| 3 | Model selection by measurement | I05, I14 | How to compare models on your own tasks instead of trusting leaderboards. |
| 4 | Embeddings | I06 | What a vector represents; cosine similarity; why the embedded text template matters; multilingual models; query vs document prefixes; dimensions and reindexing. |
| 5 | Vector search and indexes | I06, S06 | Exact vs approximate (HNSW) search, recall vs speed, pre-filtering vs post-filtering. |
| 6 | Hybrid retrieval | I04, I07 | Why keyword search still matters (names, exact titles, typos); Reciprocal Rank Fusion. |
| 7 | Structured output | I07 | JSON Schema output, validation, retries, fallbacks; when the model ignores the schema. |
| 8 | RAG | I11, S08 | Retrieve → ground → generate; grounding and citations; what happens when retrieval fails. |
| 9 | Tool calling and agents | I11, I12, S01, S03 | Tool schemas and descriptions; the agent loop; iteration limits; side effects and confirmations; how frameworks hide the loop. |
| 10 | Prompt engineering as code | I07, I11, I12 | Versioned prompts, system prompt structure, A/B comparison by evaluation. |
| 11 | Evaluation | I08, I14, S09 | Golden sets, Recall@k / MRR / nDCG, deterministic checks for agents, LLM-as-judge and its biases, variance and repeats. |
| 12 | Guardrails and AI security | I12, I14 | Prompt injection (direct and indirect), grounding checks, human in the loop for payments, abuse limits. |
| 13 | LLMOps / observability | I05, I18 | GenAI telemetry conventions, tracing agent turns, token and cost tracking, feedback loops. |
| 14 | Cost and latency engineering | I18, I19, S11 | Caching (exact, semantic, prompt caching), smaller models for sub-tasks, budgets, fallbacks. |
| 15 | Data pipelines for AI | I03, I16 | Ingestion, change detection, re-embedding, data quality effects on retrieval. |
| 16 | Shipping | I20 | Configuration and secrets per environment, hosted model limits, demo deployment. |
| 17 | Model Context Protocol | S01 | Exposing your system as tools for other agents. |
| 18 | Python ecosystem (light) | S05 | Reading and running notebooks, basic numpy / pandas / sklearn for analysis. Most AI material uses Python. |

## Habits to build in every AI iteration

1. **Look at the wire.** Read raw requests and responses at least once per new feature.
2. **Measure before and after.** No prompt or parameter change without numbers once the evaluation harness exists.
3. **Read the failures.** After an evaluation run, read 5–10 failed cases by hand. This teaches more than the averages.
4. **Write down what surprised you** in the iteration Result. That is your learning log.

## Suggested reading and references (check for current versions)

- Microsoft.Extensions.AI docs (learn.microsoft.com, ".NET AI").
- pgvector README (indexes, filtering, tuning).
- Stripe docs: Checkout Sessions, webhooks, testing.
- TMDB API docs.
- OpenTelemetry semantic conventions for GenAI.
- Chip Huyen, *AI Engineering* (O'Reilly, 2025): the reference book for this role.
- Hamel Husain's and Eugene Yan's writing on LLM evaluations.
- OWASP Top 10 for LLM Applications.
- Anthropic's "Building effective agents" article (when to use agents vs workflows).
