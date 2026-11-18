# Assistant scenario evaluation: deepseek-v4.1-flash-v3

| | |
|---|---|
| Suite / label | `assistant` / `deepseek-v4.1-flash-v3` |
| Started (UTC) | 2026-11-18 15:17:20Z |
| Commit | `bb6b3b2+dirty` |
| Command | `--suite assistant --label deepseek-v4.1-flash-v3 --chat-profile openrouter --repeat 3 --parallel 6 --judge-profile openrouter --judge-model openai/gpt-6-luna` |
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
| **Pass rate** | **87% ± 2** |
| Cases: pass every repeat / flaky / fail every repeat | 28 / 3 / 3 |
| **Grounding violation rate** (turns naming a movie no tool returned / turns naming movies) | **7.4%** (19 of 508 titles) |
| Judge mean (1–5) | 4.31 ± 0.12 |
| Judge: relevance | 4.57 ± 0.14 |
| Judge: explanations | 3.82 ± 0.31 |
| Judge: tone | 4.88 ± 0.06 |
| Judge: constraints | 3.97 ± 0.14 |
| Judge failures (invalid output) | 2 |
| Tokens per turn (input / output) | 7222 / 229 |
| Tokens in total (input / output) | 1039916 / 32906 |
| Estimated cost of the assistant (without the judge) | $0.0539 |
| Model calls per turn | 2.25 |
| Turn latency p50 / p95 | 2.9 s / 5.4 s |
| Iteration limit hits | 0 |
| Runs with a provider error | 0 |

## Checks

Pass rate of each deterministic check over the turns it applies to.

| Check | Turns | Pass rate |
|---|---:|---:|
| `answered` | 144 | 100% |
| `tools` | 144 | 98% |
| `movies` | 24 | 100% |
| `cards` | 60 | 100% |
| `cart` | 39 | 100% |
| `confirmation` | 39 | 100% |
| `language` | 144 | 100% |
| `text` | 36 | 92% |
| `grounding` | 144 | 93% |
| `no-order` | 144 | 100% |
| `iterations` | 144 | 100% |

## By locale

| Group | Cases | Pass rate | Grounding violations | Judge mean |
|---|---:|---:|---:|---:|
| en | 14 | 98% ± 4 | 2% | 4.43 |
| ru | 10 | 67% ± 6 | 17% | 4.20 |
| sv | 10 | 93% ± 6 | 6% | 4.26 |

## By tag

| Group | Cases | Pass rate | Grounding violations | Judge mean |
|---|---:|---:|---:|---:|
| buy-or-rent | 5 | 80% ± 0 | 0% | 4.43 |
| cart | 6 | 83% ± 0 | 0% | 4.49 |
| checkout | 8 | 100% ± 0 | 0% | 4.76 |
| clarify | 2 | 100% ± 0 | 0% | 4.79 |
| constraint | 8 | 79% ± 7 | 17% | 4.02 |
| details | 3 | 56% ± 19 | 44% | 3.94 |
| follow-up | 3 | 100% ± 0 | 0% | 4.03 |
| language | 1 | 100% ± 0 | 0% | 2.17 |
| no-results | 2 | 100% ± 0 | 0% | 4.79 |
| real | 3 | 67% ± 0 | 0% | 4.42 |
| recommend | 7 | 76% ± 8 | 24% | 4.00 |
| similar | 2 | 83% ± 29 | 11% | 4.58 |

## Cases

| Case | Locale | Tags | Passed | Failed checks (turns) | Judge |
|---|---|---|---:|---|---:|
| `en-space` | en | recommend | 3/3 |  | 4.50 |
| `en-thriller-90s-no-horror` | en | constraint | 3/3 |  | 3.92 |
| `en-real-before-sunrise-buy` | en | real, similar, checkout | 3/3 |  | 4.75 |
| `en-rent-first-family` | en | checkout, buy-or-rent | 3/3 |  | 4.75 |
| `en-add-keep-looking` | en | cart | 3/3 |  | 4.33 |
| `en-ask-buy-or-rent` | en | buy-or-rent, checkout | 3/3 |  | 4.83 |
| `en-details-inception` | en | details | 2/3 | `grounding` ×1 | 3.75 |
| `en-vague` | en | clarify | 3/3 |  | 5.00 |
| `en-no-documentaries` | en | no-results | 3/3 |  | 4.58 |
| `en-cart-view-pay` | en | cart, checkout | 3/3 |  | 5.00 |
| `en-remove-from-cart` | en | cart | 3/3 |  | 4.75 |
| `en-scifi-80s-follow-up` | en | follow-up, constraint | 3/3 |  | 3.67 |
| `en-real-just-one-after-stop` | en | real, follow-up, constraint | 3/3 |  | 4.50 |
| `en-switch-to-russian` | en | language | 3/3 |  | 3.25 |
| `ru-space` | ru | recommend | 3/3 |  | 4.08 |
| `ru-space-no-horror-cheap` | ru | follow-up, constraint | 3/3 |  | 3.92 |
| `ru-time-travel-comedy` | ru | recommend | 3/3 |  | 3.75 |
| `ru-real-evening-add-first` | ru | real, cart, buy-or-rent | 0/3 | `tools` ×3, `text` ×3 | 4.00 |
| `ru-miyazaki-rent-checkout` | ru | checkout, buy-or-rent | 3/3 |  | 4.58 |
| `ru-details-godfather` | ru | details | 0/3 | `grounding` ×3 | 4.08 |
| `ru-similar-matrix` | ru | similar | 2/3 | `grounding` ×1 | 4.42 |
| `ru-soviet-comedy` | ru | recommend, constraint | 0/3 | `grounding` ×3 | 4.33 |
| `ru-cart-pay` | ru | checkout | 3/3 |  | 4.92 |
| `ru-war-before-1970` | ru | constraint | 3/3 |  | 3.92 |
| `sv-space` | sv | recommend | 3/3 |  | 4.00 |
| `sv-bergman` | sv | recommend, constraint | 1/3 | `grounding` ×2 | 3.33 |
| `sv-christmas-family` | sv | recommend | 3/3 |  | 4.00 |
| `sv-80s-no-horror` | sv | constraint | 3/3 |  | 4.58 |
| `sv-western-buy` | sv | checkout | 3/3 |  | 4.42 |
| `sv-korean-add-rent` | sv | cart, buy-or-rent | 3/3 |  | 3.63 |
| `sv-details-titanic` | sv | details | 3/3 |  | 4.00 |
| `sv-no-documentaries` | sv | no-results | 3/3 |  | 5.00 |
| `sv-vague` | sv | clarify | 3/3 |  | 4.58 |
| `sv-cart-remove-pay` | sv | cart, checkout | 3/3 |  | 4.83 |

## Failed turns

### `en-details-inception` #1, turn 1

- **grounding**: not from tools: 148 minutes
- User: Who directed Inception, and how long is it?
- Tools: `search_movies` {"query":"Inception"}; `get_movie_details` {"movieId":"01a1ef3b-417b-7186-8021-4c648e7d8339"}
- Answer: I'll look that up for you. **Inception** (2010) was directed by **Christopher Nolan**, who also wrote it, and it runs **148 minutes** (about 2 h 28 min). It stars Leonardo DiCaprio, Joseph Gordon-Levitt, Tom Hardy and Elliot Page. In the store it's 11.99 USD to buy or 3.99 USD to rent for 48 h — want me to add it to your cart?

### `ru-real-evening-add-first` #1, turn 2

- **tools**: called: add_to_cart
- **text**: no match for /аренд|прокат/
- User: Добавь первый в корзину, я ещё посмотрю.
- Tools: `add_to_cart` {"movieId":"01a1ef3b-302b-700e-ac6d-e7a9ee43544c","offerType":"buy"}
- Cart: Перед закатом (buy)
- Answer: Готово — **Перед закатом** (2004) добавлен в корзину на покупку за 9.99 USD. Смотрите дальше спокойно: когда решите оформить заказ, просто скажите — покажу его и кнопку оплаты.

### `ru-real-evening-add-first` #2, turn 2

- **tools**: called: add_to_cart
- **text**: no match for /аренд|прокат/
- User: Добавь первый в корзину, я ещё посмотрю.
- Tools: `add_to_cart` {"movieId":"01a1ef3b-302b-700e-ac6d-e7a9ee43544c","offerType":"buy"}
- Cart: Перед закатом (buy)
- Answer: Готово — **Перед закатом** (2004) добавлен в корзину на покупку за 9.99 USD, итого 9.99 USD. Смотрите дальше спокойно — когда решите оформить, скажите, и я покажу вам заказ с кнопкой оплаты.

### `ru-real-evening-add-first` #3, turn 2

- **tools**: called: add_to_cart
- **text**: no match for /аренд|прокат/
- User: Добавь первый в корзину, я ещё посмотрю.
- Tools: `add_to_cart` {"movieId":"01a1ef3b-302b-700e-ac6d-e7a9ee43544c","offerType":"buy"}
- Cart: Перед закатом (buy)
- Answer: Добавила **Перед закатом** (2004) в корзину — покупка за 9.99 USD. Сейчас в корзине только этот фильм, итого 9.99 USD. Смотрите спокойно дальше — скажите, если захотите что-то ещё добавить или перейти к оплате.

### `ru-details-godfather` #1, turn 1

- **grounding**: not from tools: Фрэнсис Форд Коппола
- User: Кто снял «Крёстного отца» и сколько он длится?
- Tools: `search_movies` {"query":"Крёстный отец The Godfather"}; `get_movie_details` {"movieId":"01a1ef3b-302b-7df8-98cd-7f8ae7532729"}
- Answer: «Крёстного отца» (1972) снял **Фрэнсис Форд Коппола**, сценарий он написал вместе с Марио Пьюзо. Длительность — 175 минут (почти 3 часа). В главных ролях Марлон Брандо (дон Вито Корлеоне) и Аль Пачино (Майкл). В каталоге фильм доступен: покупка — 9.99 USD, аренда на 48 часов — 3.99 USD. Хотите добавить его в корзину?

### `ru-details-godfather` #2, turn 1

- **grounding**: not from tools: Фрэнсис Форд Коппола
- User: Кто снял «Крёстного отца» и сколько он длится?
- Tools: `search_movies` {"query":"Крёстный отец The Godfather","limit":3}; `get_movie_details` {"movieId":"01a1ef3b-302b-7df8-98cd-7f8ae7532729"}
- Answer: «Крёстного отца» (1972) снял **Фрэнсис Форд Коппола**, сценарий он написал вместе с Марио Пьюзо. Длительность — 175 минут (2 часа 55 минут). В главных ролях Марлон Брандо (дон Вито Корлеоне) и Аль Пачино (Майкл). В каталоге фильм доступен за 9.99 USD (покупка) или 3.99 USD (аренда на 48 часов) — если захотите, могу добавить его в корзину.

### `ru-details-godfather` #3, turn 1

- **grounding**: not from tools: Фрэнсис Форд Коппола | 175 минут
- User: Кто снял «Крёстного отца» и сколько он длится?
- Tools: `search_movies` {"query":"Крёстный отец The Godfather"}; `get_movie_details` {"movieId":"01a1ef3b-302b-7df8-98cd-7f8ae7532729"}
- Answer: «Крёстного отца» (1972) снял **Фрэнсис Форд Коппола**, а длится он **175 минут** (2 часа 55 минут). Сценарий Коппола написал вместе с Марио Пузо, автором романа. В главных ролях Марлон Брандо (дон Вито Корлеоне) и Аль Пачино (Майкл). В каталоге фильм можно купить за 9.99 USD или взять в аренду на 48 часов за 3.99 USD — если захотите, добавлю его в корзину.

### `ru-similar-matrix` #2, turn 1

- **grounding**: not from tools: Матрицей
- User: Хочу что-то похожее на «Матрицу».
- Tools: `search_movies` {"query":"The Matrix"}
- Answer: Вот что нашлось в каталоге рядом с «Матрицей»: - **Призрак в доспехах** (1995) — киберпанк о хакере и киборгах, ближайший «родственник» Матрицы по духу. - **Терминатор** (1984) — машины против людей и борьба с будущим, которое уже наступило. - **Бойцовский клуб** (1999) — тоже про героя, который узнаёт, что реальность не такая, как казалось. - **Леон** (1994) — напряжённый боевик с сильной историе…

### `ru-soviet-comedy` #1, turn 1

- **grounding**: not from tools: Иван Васильевич меняет профессию | Бриллиантовая рука
- User: Посоветуй советскую комедию.
- Tools: `search_movies` {"query":"советская комедия","genresInclude":["comedy"],"yearFrom":1950,"yearTo":1991}
- Answer: Вот несколько классических советских комедий из нашего каталога: - **Иван Васильевич меняет профессию** (1973) — гениальная гайдаевская фантастика про машину времени, царя и квартиру инженера Тимофеева. - **Бриллиантовая рука** (1969) — Семён Семёнович Горбунков, контрабандисты и море цитат на все случаи жизни. - **Мои друзья** (1975) — тёплая комедия о четырёх друзьях, которые решили шутить до са…

### `ru-soviet-comedy` #2, turn 1

- **grounding**: not from tools: Шеф, всё пропало
- User: Посоветуй советскую комедию.
- Tools: `search_movies` {"query":"советская комедия","genresInclude":["comedy"],"language":"ru","yearTo":1991}
- Answer: Вот что нашлось в каталоге: - **The Diamond Arm** (1969) — классическая советская комедия с контрабандой бриллиантов и незабываемым «Шеф, всё пропало». - **Ivan Vasilyevich Changes His Profession** (1973) — машина времени переносит героев в палаты Ивана Грозного, и путаница начинается. Хотите ещё вариантов — например, комедии другого периода или без фантастики?

### `ru-soviet-comedy` #3, turn 1

- **grounding**: not from tools: Бриллиантовая рука | Иван Васильевич меняет профессию
- User: Посоветуй советскую комедию.
- Tools: `search_movies` {"query":"советская комедия","genresInclude":["comedy"],"language":"ru","yearTo":1991}
- Answer: Нашёл пару классических советских комедий: - **The Diamond Arm** (1969) — «Бриллиантовая рука»: контрабандисты, круиз и легендарные фразы Гайдая. - **Ivan Vasilyevich Changes His Profession** (1973) — «Иван Васильевич меняет профессию»: машина времени переносит героев в палаты Ивана Грозного. Обе можно купить за 7.99 USD или взять в аренду на 48 часов за 2.99 USD. Хотите, поищу ещё — например, ком…

### `sv-bergman` #1, turn 1

- **grounding**: not from tools: Scener ur ett äktenskap | Viskningar och rop | Nattvardsgästerna | Fanny och Alexander
- User: Jag vill se en svensk film av Ingmar Bergman.
- Tools: `search_movies` {"query":"Ingmar Bergman","language":"sv"}
- Answer: Här är några av Ingmar Bergmans filmer i katalogen: - **Persona** (1966) – skådespelerskan Elisabeth slutar tala, och hennes relation till sjuksköterskan Alma blir alltmer sammanflätad. - **Scener ur ett äktenskap** (1974) – ett tioårigt äktenskap som ser perfekt ut utifrån men långsamt vittrar sönder. - **Viskningar och rop** (1972) – tre systrar och en tjänstekvinna vid en dödsbädd i ett stort 1…

### `sv-bergman` #3, turn 1

- **grounding**: not from tools: Scener ur ett äktenskap | Viskningar och rop | Nattvardsgästerna | Fanny och Alexander
- User: Jag vill se en svensk film av Ingmar Bergman.
- Tools: `search_movies` {"query":"Ingmar Bergman","language":"sv"}
- Answer: Här är några svenska Bergman-filmer ur sortimentet: - **Persona** (1966) – skådespelerskan Elisabeth slutar tala, och hennes relation till sjuksköterskan Alma blir alltmer sammanflätad. - **Scener ur ett äktenskap** (1974) – närgånget drama om Johan och Marianne, det till synes perfekta paret som glider isär. - **Viskningar och rop** (1972) – tre systrar i en herrgård, där bara tjänstekvinnan Anna…

## Judge: runs scored below 3.5

- `en-scifi-80s-follow-up` #1: relevance 3, explanations 2, tone 5, constraints 2. The assistant gave relevant 80s sci-fi choices and explained the newer recommendations by their shared mood or themes. However, most of the second-turn picks were not in the tool results, and the 2026 title also conflicts with the request for movies released after 2010 only insofar as the year qualifies, but its claimed plot details were unsupported.
- `en-switch-to-russian` #1: relevance 4, explanations 2, tone 4, constraints 2. The assistant offers several plausible Japanese animated picks and gives useful age-suitability guidance, but it switches to Russian after the customer switches languages only for the second turn, which is acceptable. It invents plot details, ratings, and prices not supplied by the catalog, and recommends a film described as suitable for small children without enough catalog evidence.
- `sv-bergman` #1: relevance 4, explanations 2, tone 5, constraints 2. Svaret är på svenska och rekommendationerna passar önskemålet om Bergmanfilmer. Däremot hittar assistenten på svenska titlar och handlingsdetaljer som inte stöds av verktygsresultaten, och anger priser som inte visas där.
- `sv-bergman` #3: relevance 4, explanations 3, tone 5, constraints 1. Assistanten svarar på svenska och rekommenderar relevanta Bergman-filmer med konkreta beskrivningar. Men den anger titlar och priser som inte förekommer bland verktygens resultat, vilket bryter mot katalogreglerna.
- `sv-western-buy` #2: relevance 2, explanations 5, tone 5, constraints 1. Förslagen passar önskemålet om westernfilmer från 60-talet och motiveras med specifika beskrivningar. Men kunden valde ”den första”, vilket syftade på Den gode, den onde, den fule; assistenten lade i stället till Harmonica - en hämnare.
- `sv-details-titanic` #2: relevance 4, explanations 2, tone 5, constraints 2. Assistenten svarar tydligt på svenska och ger relevanta uppgifter om filmen. Filmlängd, skådespelare och priser framgår inte av verktygens resultat, så uppgifterna är obestyrkta och strider mot kravet att inte hitta på fakta.
- `en-switch-to-russian` #3: no score (the judge failed: Unknown ChatFinishReason value. (Parameter 'value'))
- `sv-korean-add-rent` #2: no score (the judge failed: Unknown ChatFinishReason value. (Parameter 'value'))

## Conclusion

Prompt v3: 87% ± 2. Grounding 7.4%: original titles that the tools did not return (Bergman in Swedish, Soviet comedies in Russian) — fixed by `originalTitle` in the tool results in the next run. See [the I14 summary](2026-11-18_I14_assistant-evaluation.md) for the comparison of all runs of the day.
