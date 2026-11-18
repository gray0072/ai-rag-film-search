# Session S16 — 2026-11-18 — Assistant evaluation and guardrails

- **Iteration:** I14 (Assistant evaluation and guardrails)
- **Agent:** Claude Code (VS Code)
- **Outcome:** I14 done: assistant scenario (34) and adversarial (20) datasets in en/ru/sv with 3 real failed conversations; runner through `IAssistantChat` with deterministic checks, LLM judge (gpt-6-luna, throttled), variance over repeats; 14 reports + summary. Prompt v3 (ids via search, genresExclude, buy-or-rent, checkout without asking, button labels, refusal language, fake tool results), joined unanswered messages, id error hint, grounding check tuned + originalTitle in tool results. Gemma 63% → 78%, DeepSeek 83% → 92%; adversarial 88% / 98%; grounding violations 2.3% / 3.8%. No off-topic classifier (not needed by numbers). Pushed.

---

## Prompt 1

English:

```text
Let's do I14 and push.
```

Original (ru):

```text
делаем I14 и пуш
```
