# Session S08 — 2026-11-17 — Hybrid search and query understanding

> [Docs](../../README.md) › [Prompt history](../README.md) › S08 · ← [S07](2026-11-17_S07_embeddings-semantic-search.md) · [S09](2026-11-17_S09_search-evaluation.md) → · [I07 plan](../../plan/iterations/I07-hybrid-query-understanding.md)

- **Iteration:** I07 (Hybrid search and query understanding)
- **Agent:** Claude Code (VS Code)
- **Outcome:** I07 done: hybrid search (RRF, default mode; tuned to k 20 and weight 0.1 for the relaxed keyword list after plain RRF lost to semantic on descriptions), LLM query understanding with JSON Schema structured output, validation, one retry and fallback, prompt files with versions (v2 fixes bare titles treated as "similar to"), "movies like X" by vector averaging, memory cache. Hybrid ≥ both single modes on 46 manual queries. Process changed: work directly on `main`, all checks run locally before a push, other branches deleted.

---

## Prompt 1

English:

```text
Let's do I07 and push. Then shut down the computer.
Before pushing, run the unit tests locally, and whatever else you can too. Don't defer the checking to GitHub.
Push only to main, no branch needed. Delete the other branches.
Write it in agents.
```

Original (ru):

```text
делаем I07 и пуш. потом выключи комп.
перед пушем запускай юнит тесты локально и какие еще можешь тоже. не откладывай проверку на гитхаб.
пуш только в main, ветка не нужна. удали остальные ветки.
запиши в agents
```

## Prompt 2

English:

```text
Cancel the computer shutdown, don't shut it down, another session will shut it down.
```

Original (ru):

```text
про выключение компа отмена, не выключай, другая сессия выключит.
```
