# AI playground

A console to try the AI provider layer (I05) and to benchmark models. It uses the API's `AI` configuration
(`src/FilmSearch.Api/appsettings*.json`) and the API's user secrets, so a profile that works here works in the API.

```bash
lms server start && lms load google/gemma-4-e4b -y && lms load text-embedding-embeddinggemma-300m -y

dotnet run --project tools/ai-playground -- chat "Recommend a sci-fi movie"
dotnet run --project tools/ai-playground -- stream "Name three heist movies"
dotnet run --project tools/ai-playground -- tools "Найди фантастику дешевле 4 долларов и добавь самый новый фильм в корзину"
dotnet run --project tools/ai-playground -- json "Skräckfilmer från 1970-talet"
dotnet run --project tools/ai-playground -- embed
dotnet run --project tools/ai-playground -- bench-chat --chat-model qwen/qwen3.5-9b --reasoning None --out bench.md
dotnet run --project tools/ai-playground -- bench-embed --embedding-model text-embedding-nomic-embed-text-v1.5 \
  --query-prefix "search_query: " --document-prefix "search_document: "
```

| Command | What it shows |
|---|---|
| `chat` | One request: answer, model, token usage, estimated cost, finish reason. |
| `stream` | Streaming: time to first token and tokens per second. |
| `tools` | Tool calling with a loop written by hand (ADR-0006): the model asks for `search_movies` / `add_to_cart` / …, the program runs fake tools and sends the results back. |
| `json` | Structured output: a `SearchIntent` record → JSON Schema in `response_format` → validated object. |
| `embed` | Cosine similarity matrix of the same phrases in English, Russian and Swedish. |
| `bench-chat` | Model benchmark: 10 tool-call prompts, 10 JSON prompts, en/ru/sv answers, speed. |
| `bench-embed` | Embedding benchmark: Russian/Swedish/English queries against English overviews, translation pairs. |

Options: `--chat-profile openrouter`, `--chat-model <id>`, `--reasoning None|Low|Medium|High`, `--embedding-model <id>`,
`--query-prefix`, `--document-prefix`, `--out <file>`, `--raw` (log the HTTP requests and responses, with SSE events),
and any setting as `--AI:<key>=<value>`.

OpenRouter: `dotnet user-secrets set AI:ChatProfiles:openrouter:ApiKey <key> --project src/FilmSearch.Api`, then
`--chat-profile openrouter`.

Results of the I05 benchmark: [evals/reports/2026-11-17_I05_model-benchmark.md](../../evals/reports/2026-11-17_I05_model-benchmark.md).
