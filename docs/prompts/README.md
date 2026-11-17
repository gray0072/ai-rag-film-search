# Prompt history

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

```text
<verbatim prompt, including typos, in the original language>
```

## Prompt 2
...
````

Rules:
- Copy prompts exactly. Do not translate or fix typos.
- Messages sent mid-turn count as separate prompts.
- Never save secrets (API keys etc.). Replace them with `<redacted>`.
- If several agents work in parallel (worktrees), each session gets its own file.

## Index

| Session | Date | Iteration | Topic |
|---|---|---|---|
| [S01](sessions/2026-11-17_S01_preparation.md) | 2026-11-17 | I00 | Project preparation: spec, plan, AGENTS.md |
