# Session S24 — 2026-11-19 — Documentation system

> [Docs](../../README.md) › [Prompt history](../README.md) › S24 · ← [S23](2026-11-19_S23_remove-ollama.md) · [S25](2026-11-19_S25_guide-core.md) → · [I21 plan](../../plan/iterations/I21-documentation.md)

- **Iteration:** I21 (Documentation and project showcase)
- **Agent:** Claude Code (VS Code)
- **Outcome:** session 1 of 4 of I21. Documentation structure designed (README → hub → guide / journal / handbook /
  reference; Markdown as the source, GitHub Pages as a second view in session 4) and written down in the I21 plan.
  - 14 screenshots with a repeatable Playwright script (`npm run docs:screenshots`), a social preview image.
  - README rewritten as a showcase (pet project, screenshots, evidence, quick start, "where next" by role); the
    detailed setup moved to `docs/handbook/local-setup.md`. GitHub description and topics set, wiki off.
  - Docs hub, guide contents, handbook roles and capability matrix, build journal (plan vs fact, problem index,
    M1–M5). MIT license added; the owner uploaded the social preview. Next: guide core chapters (session 2).

---

## Prompt 1

English:

```text
Make a clear and beautiful README. Say that this is a pet project, how to run it locally. Add a few screenshots. Fill in the repository info so that GitHub looks good.
We need to organize several new document structures.
1. The plan already exists in docs\plan\roadmap.md
2. A structure similar to the plan, but with the actual results; the results can be pulled from the session outcomes. Which problems came up and how we solved them.
3. Write a textbook on RAG and on developing such an application in general. It needs theory, links to examples from the plan and the actual results, explanations of what we do, why and for what reason. It needs diagrams and illustrations. It should be clear which business problems we solve. More advanced topics can go into an advanced part, such as choosing metrics for embeddings, choosing the index in PG and the storage method. Links to evals\reports will be appropriate.
4. We need documentation for a developer or a manager describing the capabilities of the system and how and where it can be controlled. It can be divided into sections, less and more technical. Screenshots are needed. If you see more roles, you can single them out too and re-split the docs. Each role should understand the capabilities of the system and how to work with them.

Think about how to organize this into one system with the entry from the README, so that a new user can find their way and continue in the section that interests them, but along the way be able to move to other sections.
Part can be done in GitHub MD files, part can be hosted on GitHub Pages; decide yourself what is more logical and clearer.

Do it yourself step by step, without agents. If appropriate, we will split it into several sessions; then the next one should understand how to continue.
```

Original (ru):

```text
сделай понятный и красивый readme. напиши что это пет-проект, запуск локально. добавь несколько скриншотов. заполни инфу о репозитории, чтобы гитхаб выглядел хорошо.
надо организовать несколько новых структур документов.
1. план уже есть в docs\plan\roadmap.md
2. аналогичная плану структура с фактом, можно результаты вынятуть из итогов сессий. какие проблемы встречались и как мы их решали.
3. написать учебник по RAG и по разработке такого приложения в целом. нужна теория, ссылки на примеры из плана и факта, объяснения что делаем зачем и почему. нужны схемы и иллюстрации. должно быть понятно какие бизнес проблемы мы решаем. более продвинутые темы можно вынести в advanced часть, такие как выбор метрик для эмбеддингов, выбор индекса в PG и способа хранения. уместны будут ссылки в evals\reports
4. нужна документация для разработчика или менеджера с описанием возможностей системы и как и где ее можно контролировать. можно разделить на разделы, менее и более технические. нужны скрины. если видишь еще роли, можно их тоже выделить и переразбить доки. каждой роли должно быть понятны возможности системы и как с ними работать.

подумай как организовать это в единую систему с входом из readme, чтобы новый пользователь мог сориентироваться и продолжить в том разделе, который ему интересен, но в процессе иметь возможность переходить в другие разделы. 
часть можно сделать на гитхаб MD файлах, часть можно хостить на github pages, сам придумай как будет логичнее и понятнее.

делай сам по шагам, без агентов. если уместно, разделим на несколько сессий, тогда следующей должно быть понятно, как продолжить.
```

## Prompt 2

English:

```text
MIT. I uploaded the image.
Commit.
```

Original (ru):

```text
mit. картинку загрузил
коммит
```
