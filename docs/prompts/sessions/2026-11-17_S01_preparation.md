# Session S01 — 2026-11-17 — Project preparation

- **Iteration:** I00 (preparation: spec, plan, repo conventions)
- **Agent:** Claude Code (VS Code)
- **Outcome:** spec, roadmap, iteration plans, ADRs, AGENTS.md created. No code.

---

## Prompt 1

English:

```text
Right now I'm an AI-assisted full-stack developer, .NET + React.
I want to become an AI engineer.
Suggest a pet project for learning, and its stages. I like a project for searching movies, ordering them, and Stripe for payment. You can search yourself or ask an AI assistant. It somehow searches the DB by the described parameters and sells to the customer. In the end the customer has to pay in Stripe.
Write a spec.
Stripe fake at first, then we'll hook up the test one. And an integration with IMDb or some open movie DB. To start, we can add the top 100 movies with info.
We use local AI and embeddings, there is LM Studio. But with the option to connect OpenRouter or similar.
This is a pet project, the goal is to learn and practice, as close to production as possible. Make a work plan, structure it, split it into folders, including the spec. Create agents.md and whatever else is needed.
Preparation first; we write the code later in sessions, iteration by iteration, following the plan. Where possible, make parallel iterations.
Write all my prompts to a separate file as history, preferably split by session
```

Original (ru):

```text
Сейчас я ai assisted fullstack developer. .net + react.
Хочу стать ai-ingeneer.
Предложи пет проект для обучения и этапы. Мне нравится проект для поиска фильмов, заказа и stripe для оплаты. Можно искать самому или просить ИИ ассистента. Тот как-то ищет в бд по описанным параметрам и продает клиенту. В итоге клиент должен оплатить в страйп. 
сделай спеку. 
страйп сначала фиктивный, потом тестовый прикрутим. и интеграцию с imdb или какой-то открытой БД фильмов. для начала можно топ-100 фильмов с инфой добавить.
ии и эмбеддинги используем локальные, есть lm studio. но с возможностью подключить openrouter или аналоги.
это пет-проект, цель научиться и потренироваться, максимально близко к продовому. составь план работы, структурируй, разбивай на папки, в том чисте спеку. создай agents.md и что еще надо. 
сначала подготовка, код делаем позже в сессиях итерациями, по плану. где возможно, сделать параллельные итерации.
все мои промпты пиши в отдельный файл историей, желательно делить по сессиям
```

## Prompt 2

English:

```text
The main language is English, but with Russian support in the UI and in queries to the AI
```

Original (ru):

```text
основной язык англ, но с поддержкой русского в UI и запросах к ИИ
```

## Prompt 3

English:

```text
Commit; we continue in another session
```

Original (ru):

```text
коммит, дальше в другой сессии
```
