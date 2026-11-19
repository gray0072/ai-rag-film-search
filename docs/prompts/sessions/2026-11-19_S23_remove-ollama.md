# Session S23 — 2026-11-19 — Remove Ollama

- **Iteration:** — (follow-up of I20)
- **Agent:** Claude Code (VS Code)
- **Outcome:** Ollama removed from the demo stack (compose services and volume, the `ollama` embedding profile, release workflow, docs; ADR-0012 revision note).
  - Demo embeddings default to OpenRouter (Qwen3-Embedding-8B); `CHAT_PROFILE` / `EMBEDDING_PROFILE=local` use LM Studio on the Docker host (`host.docker.internal`, `LMSTUDIO_URL`).
  - Checked locally: stack with LM Studio Healthy, reindex of 1000 movies in 33 s, Playwright 6/6 with the assistant; fresh stack with unreachable AI (the release runner's case) Degraded, 5 passed, 1 skipped. 524 .NET tests green.
  - Docker leftovers of the app removed; a Docker naming rule added to the owner's global CLAUDE.md.

---

## Prompt 1

English:

```text
Remove Ollama, we use only LM Studio or OpenRouter. Commit.
Clean up the images and containers of this application in Docker. Add a rule to the global agents file that images must be named or tagged with the project name.
```

Original (ru):

```text
удали Ollama, используем только LM Studio или OpenRouter. коммит.
почисти в докере образы и контейнеры от этого приложения. добавь в глобальный agents правило, что образы надо именовать или помечать именем проекта
```
