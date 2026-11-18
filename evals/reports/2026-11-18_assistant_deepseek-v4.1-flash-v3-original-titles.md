# Assistant scenario evaluation: deepseek-v4.1-flash-v3-original-titles

| | |
|---|---|
| Suite / label | `assistant` / `deepseek-v4.1-flash-v3-original-titles` |
| Started (UTC) | 2026-11-18 15:58:25Z |
| Commit | `bb6b3b2+dirty` |
| Command | `--suite assistant --label deepseek-v4.1-flash-v3-original-titles --chat-profile openrouter --repeat 3 --parallel 6 --judge-profile openrouter --judge-model openai/gpt-6-luna` |
| Dataset | `evals/datasets/assistant.scenarios.jsonl` version `69582c00152b`, 34 cases, repeat 3 |
| Database | fresh container pgvector/pgvector:pg17, seed generated 2026-11-17 19:13:07Z |
| Embeddings | profile `local`: text-embedding-embeddinggemma-300m (768 dims, query prefix "task: search result \| query: ", document prefix "title: none \| text: "); text `movie-profile-v1`; locales en |
| Chat | profile `openrouter`: deepseek/deepseek-v4.1-flash-1017 (reasoning None) |
| Prompts | `assistant-system.v3` |
| Search parameters | `Semantic:MaxResults=100`, `Hybrid:RrfK=20`, `Hybrid:KeywordWeight=1`, `Hybrid:RelaxedKeywordWeight=0.1`, `Hybrid:SemanticWeight=1`, `Hybrid:CandidatesPerList=50`, `Hybrid:SimilarTitles=AverageVector` |
| Assistant parameters | `MaxToolIterations=6`, `SearchResultLimit=6`, `HistoryMaxMessages=12`, `HistoryTokenBudget=3000` |
| Judge | profile `eval-judge`: openai/gpt-6-luna; prompt `assistant-judge.v1` |

## Summary

A case passes when every check of every turn passes. Pass rate = share of cases that passed, per repeat; mean ± standard deviation over the repeats.

| Metric | Value |
|---|---|
| Cases / runs / turns | 34 / 102 / 144 |
| **Pass rate** | **92% ± 2** |
| Cases: pass every repeat / flaky / fail every repeat | 31 / 1 / 2 |
| **Grounding violation rate** (turns naming a movie no tool returned / turns naming movies) | **3.8%** (5 of 494 titles) |
| Judge mean (1–5) | 4.33 ± 0.06 |
| Judge: relevance | 4.63 ± 0.07 |
| Judge: explanations | 3.82 ± 0.16 |
| Judge: tone | 4.89 ± 0.09 |
| Judge: constraints | 3.96 ± 0.12 |
| Judge failures (invalid output) | 1 |
| Tokens per turn (input / output) | 6972 / 221 |
| Tokens in total (input / output) | 1003938 / 31801 |
| Estimated cost of the assistant (without the judge) | $0.0520 |
| Model calls per turn | 2.20 |
| Turn latency p50 / p95 | 3.2 s / 9.1 s |
| Iteration limit hits | 0 |
| Runs with a provider error | 0 |

## Checks

Pass rate of each deterministic check over the turns it applies to.

| Check | Turns | Pass rate |
|---|---:|---:|
| `answered` | 144 | 100% |
| `tools` | 144 | 97% |
| `movies` | 24 | 100% |
| `cards` | 60 | 100% |
| `cart` | 39 | 100% |
| `confirmation` | 39 | 97% |
| `language` | 144 | 100% |
| `text` | 36 | 94% |
| `grounding` | 144 | 97% |
| `no-order` | 144 | 100% |
| `iterations` | 144 | 100% |

## By locale

| Group | Cases | Pass rate | Grounding violations | Judge mean |
|---|---:|---:|---:|---:|
| en | 14 | 100% ± 0 | 0% | 4.46 |
| ru | 10 | 73% ± 6 | 12% | 4.14 |
| sv | 10 | 100% ± 0 | 0% | 4.33 |

## By tag

| Group | Cases | Pass rate | Grounding violations | Judge mean |
|---|---:|---:|---:|---:|
| buy-or-rent | 5 | 80% ± 0 | 0% | 4.22 |
| cart | 6 | 83% ± 0 | 0% | 4.38 |
| checkout | 8 | 100% ± 0 | 0% | 4.71 |
| clarify | 2 | 100% ± 0 | 0% | 5.00 |
| constraint | 8 | 92% ± 7 | 7% | 4.16 |
| details | 3 | 67% ± 0 | 33% | 4.08 |
| follow-up | 3 | 100% ± 0 | 0% | 4.28 |
| language | 1 | 100% ± 0 | 0% | 3.83 |
| no-results | 2 | 100% ± 0 | 0% | 5.00 |
| real | 3 | 67% ± 0 | 0% | 4.31 |
| recommend | 7 | 90% ± 8 | 10% | 4.13 |
| similar | 2 | 100% ± 0 | 0% | 4.13 |

## Cases

| Case | Locale | Tags | Passed | Failed checks (turns) | Judge |
|---|---|---|---:|---|---:|
| `en-space` | en | recommend | 3/3 |  | 4.42 |
| `en-thriller-90s-no-horror` | en | constraint | 3/3 |  | 3.58 |
| `en-real-before-sunrise-buy` | en | real, similar, checkout | 3/3 |  | 4.67 |
| `en-rent-first-family` | en | checkout, buy-or-rent | 3/3 |  | 4.58 |
| `en-add-keep-looking` | en | cart | 3/3 |  | 4.08 |
| `en-ask-buy-or-rent` | en | buy-or-rent, checkout | 3/3 |  | 4.50 |
| `en-details-inception` | en | details | 3/3 |  | 4.00 |
| `en-vague` | en | clarify | 3/3 |  | 5.00 |
| `en-no-documentaries` | en | no-results | 3/3 |  | 5.00 |
| `en-cart-view-pay` | en | cart, checkout | 3/3 |  | 5.00 |
| `en-remove-from-cart` | en | cart | 3/3 |  | 4.92 |
| `en-scifi-80s-follow-up` | en | follow-up, constraint | 3/3 |  | 4.33 |
| `en-real-just-one-after-stop` | en | real, follow-up, constraint | 3/3 |  | 4.50 |
| `en-switch-to-russian` | en | language | 3/3 |  | 3.83 |
| `ru-space` | ru | recommend | 3/3 |  | 3.58 |
| `ru-space-no-horror-cheap` | ru | follow-up, constraint | 3/3 |  | 4.00 |
| `ru-time-travel-comedy` | ru | recommend | 3/3 |  | 4.25 |
| `ru-real-evening-add-first` | ru | real, cart, buy-or-rent | 0/3 | `tools` ×4, `text` ×2, `confirmation` ×1 | 3.75 |
| `ru-miyazaki-rent-checkout` | ru | checkout, buy-or-rent | 3/3 |  | 4.75 |
| `ru-details-godfather` | ru | details | 0/3 | `grounding` ×3 | 4.25 |
| `ru-similar-matrix` | ru | similar | 3/3 |  | 3.58 |
| `ru-soviet-comedy` | ru | recommend, constraint | 1/3 | `grounding` ×2 | 4.67 |
| `ru-cart-pay` | ru | checkout | 3/3 |  | 4.92 |
| `ru-war-before-1970` | ru | constraint | 3/3 |  | 3.67 |
| `sv-space` | sv | recommend | 3/3 |  | 3.92 |
| `sv-bergman` | sv | recommend, constraint | 3/3 |  | 3.83 |
| `sv-christmas-family` | sv | recommend | 3/3 |  | 4.25 |
| `sv-80s-no-horror` | sv | constraint | 3/3 |  | 4.67 |
| `sv-western-buy` | sv | checkout | 3/3 |  | 4.00 |
| `sv-korean-add-rent` | sv | cart, buy-or-rent | 3/3 |  | 3.50 |
| `sv-details-titanic` | sv | details | 3/3 |  | 4.00 |
| `sv-no-documentaries` | sv | no-results | 3/3 |  | 5.00 |
| `sv-vague` | sv | clarify | 3/3 |  | 5.00 |
| `sv-cart-remove-pay` | sv | cart, checkout | 3/3 |  | 5.00 |

## Failed turns

### `ru-real-evening-add-first` #1, turn 2

- **tools**: called: add_to_cart
- User: Добавь первый в корзину, я ещё посмотрю.
- Tools: `add_to_cart` {"movieId":"01a1ef60-dd2c-74d8-b86d-6bc4fcfd68a3","offerType":"buy"}
- Cart: С любовью, Рози (buy)
- Answer: Готово — **С любовью, Рози** (2014) добавлена в корзину на покупку за 9.99 USD. Вы не уточнили, покупать или брать в аренду, поэтому я поставил покупку — если хотите аренду, скажите, и я поменяю. Смотрите дальше, а когда решите — просто напишите, и я покажу заказ для оплаты.

### `ru-real-evening-add-first` #2, turn 2

- **tools**: called: add_to_cart
- **text**: no match for /аренд|прокат/
- User: Добавь первый в корзину, я ещё посмотрю.
- Tools: `add_to_cart` {"movieId":"01a1ef60-dd2c-74d8-b86d-6bc4fcfd68a3","offerType":"buy"}
- Cart: С любовью, Рози (buy)
- Answer: Добавила **С любовью, Рози** (2014) в корзину на покупку за 9.99 USD — итого 9.99 USD. Смотрите спокойно дальше, а когда решите — скажите, и я оформлю оплату.

### `ru-real-evening-add-first` #3, turn 2

- **tools**: called: add_to_cart
- **text**: no match for /куп/
- User: Добавь первый в корзину, я ещё посмотрю.
- Tools: `add_to_cart` {"movieId":"01a1ef60-d5dd-709c-aef8-e62588c65ffe","offerType":"rent"}
- Cart: Дневник памяти (rent)
- Answer: Готово — **Дневник памяти** (2004) в корзине: аренда на 48 часов за 3.99 USD, итого 3.99 USD. Смотрите дальше спокойно — когда решите, скажите, и я оформлю заказ.

### `ru-real-evening-add-first` #3, turn 3

- **tools**: not called: add_to_cart; called: request_checkout
- **confirmation**: asked for checkout confirmation
- User: В аренду.
- Tools: `request_checkout` 
- Cart: Дневник памяти (rent)
- Answer: Всё оформлено как аренда: **Дневник памяти** (2004) — 48 часов за 3.99 USD, итого 3.99 USD. Проверьте заказ и нажмите **Перейти к оплате** — оплата пройдёт только после вашего клика.

### `ru-details-godfather` #1, turn 1

- **grounding**: not from tools: Фрэнсис Форд Коппола
- User: Кто снял «Крёстного отца» и сколько он длится?
- Tools: `search_movies` {"query":"Крёстный отец The Godfather"}; `get_movie_details` {"movieId":"01a1ef60-c755-7cac-8fb7-3251afff4717"}
- Answer: «Крёстного отца» (1972) снял **Фрэнсис Форд Коппола**, продолжительность — 175 минут (почти 3 часа). Сценарий он написал вместе с Марио Пьюзо, а в главных ролях Марлон Брандо и Аль Пачино. В каталоге фильм есть: покупка — 9.99 USD, аренда на 48 часов — 3.99 USD. Добавить его в корзину?

### `ru-details-godfather` #2, turn 1

- **grounding**: not from tools: Фрэнсис Форд Коппола
- User: Кто снял «Крёстного отца» и сколько он длится?
- Tools: `search_movies` {"query":"Крёстный отец The Godfather","limit":3}; `get_movie_details` {"movieId":"01a1ef60-c755-7cac-8fb7-3251afff4717"}
- Answer: «Крёстный отец» (1972) снял **Фрэнсис Форд Коппола**, а длится фильм **175 минут** (2 часа 55 минут). Сценарий Коппола написал вместе с Марио Пьюзо. В главных ролях Марлон Брандо и Аль Пачино. В нашем каталоге фильм можно купить за 9.99 USD или взять в аренду на 48 часов за 3.99 USD — если захотите, добавлю его в корзину.

### `ru-details-godfather` #3, turn 1

- **grounding**: not from tools: Фрэнсис Форд Коппола
- User: Кто снял «Крёстного отца» и сколько он длится?
- Tools: `search_movies` {"query":"Крёстный отец The Godfather","limit":3}; `get_movie_details` {"movieId":"01a1ef60-c755-7cac-8fb7-3251afff4717"}
- Answer: «Крёстный отец» (1972) снял **Фрэнсис Форд Коппола**, а длится фильм **175 минут** (2 часа 55 минут). Сценарий Коппола написал вместе с Марио Пьюзо.

### `ru-soviet-comedy` #1, turn 1

- **grounding**: not from tools: Кавказскую пленницу
- User: Посоветуй советскую комедию.
- Tools: `search_movies` {"query":"советская комедия","genresInclude":["comedy"],"yearTo":1991}
- Answer: Вот что нашлось из советских комедий: - **Ivan Vasilyevich Changes His Profession** (1973) — классика Гайдая: машина времени переносит управдома и вора в палаты Ивана Грозного. - **The Diamond Arm** (1969) — Семён Семёнович с гипсом и бриллиантами, шайка контрабандистов и море цитат. - **Король комедии** (1982) — не советское, но если хочется чёрного юмора про шоу-бизнес, тоже подойдёт. Хотите ещё…

### `ru-soviet-comedy` #3, turn 1

- **grounding**: not from tools: Шеф, всё пропало
- User: Посоветуй советскую комедию.
- Tools: `search_movies` {"query":"советская комедия","genresInclude":["comedy"],"language":"ru","yearTo":1991}
- Answer: Вот что нашлось в каталоге: - **The Diamond Arm** (1969) — классическая советская комедия с погонями, контрабандой бриллиантов и незабываемым «Шеф, всё пропало». - **Ivan Vasilyevich Changes His Profession** (1973) — гайдаевская фантастическая комедия о машине времени, которая переносит героев в палаты Ивана Грозного. Каталог предложил только два варианта по этому запросу. Хотите, поищу ещё — напр…

## Judge: runs scored below 3.5

- `en-thriller-90s-no-horror` #3: relevance 3, explanations 3, tone 3, constraints 2. The assistant recommends several 1990s thrillers and gives specific reasons, but includes Misery despite the no-horror constraint and makes unsupported claims about ratings and plot details. The awkward typo also slightly weakens the otherwise clear tone.
- `en-switch-to-russian` #3: relevance 4, explanations 3, tone 2, constraints 3. The recommendations fit Japanese animation and then sensibly narrow to gentler choices for young children. The assistant gives useful explanations, but switches to Russian despite the customer’s English locale, and adds details like awards, plot points, runtimes, suitability, and prices that the tool results did not provide.
- `ru-similar-matrix` #3: relevance 3, explanations 2, tone 5, constraints 3. Ответ на русском и предлагает несколько тематически близких фильмов, особенно «Терминатора» и «Призрака в доспехах». Однако некоторые объяснения опираются на сведения, которых не было в описаниях каталога, а «Леон» и «Легенда о пианисте» подходят слабо.
- `sv-korean-add-rent` #2: relevance 2, explanations 4, tone 5, constraints 1. Förslagen i första svaret passar väl till önskemålet om koreanska thrillers och har korta motiveringar. Men när kunden bad om den andra titeln lade assistenten i stället till *Memories of Murder*; den upprepade också ett pris och hyresvillkor som inte fanns i verktygsresultaten.
- `sv-western-buy` #2: no score (the judge failed: HTTP 400 (: ))

## Conclusion

**Final run of the cloud model** (prompt v3, final code): 92% ± 2, judge 4.33, p50 3.2 s, $0.052 for 144 turns. Grounding violations 3.8%: two real rule-1 violations (it offered «Кавказская пленница», not in the catalog, from its own knowledge) and the transliterated director name. Remaining failure: buy vs rent not asked in `ru-real-evening-add-first`. See [the I14 summary](2026-11-18_I14_assistant-evaluation.md) for the comparison of all runs of the day.
