# Prompts

Prompt templates as versioned files: `<name>.vN.md`. A meaningful change creates a new version (see AGENTS.md, "AI code"); old versions stay for comparison.

- Files are embedded resources (`FilmSearch.AI.csproj`) and loaded by `PromptLibrary`: `Get(name)` returns the latest version, `Get(name, version)` a pinned one.
- Placeholders are `{{name}}` (lower case, digits, `_`); `PromptTemplate.Render` fails when a value or a placeholder is missing.
- The prompt id (`name.vN`) is logged, tagged on the span (`filmsearch.prompt.id`) and stored with the results it produced.
- Put only instructions in a prompt. User text and data (movie texts, tool results) go into user / tool messages, never into the template.

| Prompt | Used by | Versions |
|---|---|---|
| `query-understanding` | `QueryUnderstandingService` (I07, spec 05 §5) | v1, v2 (current: bare titles and names are searches, not "similar to") |
| `assistant-system` | `AssistantChat` (I11, spec 06 §4): system prompt of the assistant | v1 |
| `assistant-summary` | `ConversationSummarizer` (I11, spec 06 §6): rolling summary of older turns | v1 |
