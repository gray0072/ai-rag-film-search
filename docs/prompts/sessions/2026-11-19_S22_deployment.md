# Session S22 — 2026-11-19 — Containers and deployment

- **Iteration:** I20 (Containers and deployment)
- **Agent:** Claude Code (VS Code)
- **Outcome:** I20 done, without a public URL (owner's decision).
  - Docker images (chiseled API and Worker, Caddy web), Docker Compose stack with Ollama embeddings (default) or OpenRouter embeddings, migrations as the Worker's `migrate` step, environment `Demo`, data protection keys in a volume, deploy / backup / restore scripts and a runbook; ADR-0012.
  - Service-wide daily token budget of the assistant; OpenRouter embedding health check fixed (`ModelsPath`).
  - Release workflow: images on tag, the stack and Playwright smoke tests on the runner, push to GHCR, deploy over SSH when a server is configured.
  - Search evaluation: Ollama EmbeddingGemma equals LM Studio; Qwen3-Embedding-8B on OpenRouter is better (R@10 0.917 vs 0.865) but slower.
  - Pushed to `main`.

---

## Prompt 1

English:

```text
Let's do I20. Then push.
```

Original (ru):

```text
делаем I20. потом пуш
```

## Prompt 2

Answers to the agent's questions (target: "Prepare only, no public URL now"; embeddings in the demo environment):

English:

```text
Target: Prepare only, no public URL now.
Embeddings: both must be supported, the local one by default.
```

Original (ru):

```text
Target: Prepare only, no public URL now.
Embeddings: оба должны поддерживаться, локальный по умолчанию
```
