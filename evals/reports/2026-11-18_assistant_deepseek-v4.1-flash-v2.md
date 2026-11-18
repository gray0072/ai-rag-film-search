# Assistant scenario evaluation: deepseek-v4.1-flash-v2

| | |
|---|---|
| Suite / label | `assistant` / `deepseek-v4.1-flash-v2` |
| Started (UTC) | 2026-11-18 14:45:52Z |
| Commit | `bb6b3b2+dirty` |
| Command | `--suite assistant --label deepseek-v4.1-flash-v2 --chat-profile openrouter --repeat 3 --parallel 6 --judge-profile openrouter --judge-model openai/gpt-6-luna` |
| Dataset | `evals/datasets/assistant.scenarios.jsonl` version `69582c00152b`, 34 cases, repeat 3 |
| Database | fresh container pgvector/pgvector:pg17, seed generated 2026-11-17 19:13:07Z |
| Embeddings | profile `local`: text-embedding-embeddinggemma-300m (768 dims, query prefix "task: search result \| query: ", document prefix "title: none \| text: "); text `movie-profile-v1`; locales en |
| Chat | profile `openrouter`: deepseek/deepseek-v4.1-flash-1017 (reasoning None) |
| Prompts | `assistant-system.v2` |
| Search parameters | `Semantic:MaxResults=100`, `Hybrid:RrfK=20`, `Hybrid:KeywordWeight=1`, `Hybrid:RelaxedKeywordWeight=0.1`, `Hybrid:SemanticWeight=1`, `Hybrid:CandidatesPerList=50`, `Hybrid:SimilarTitles=AverageVector` |
| Assistant parameters | `MaxToolIterations=6`, `SearchResultLimit=6`, `HistoryMaxMessages=12`, `HistoryTokenBudget=3000` |
| Judge | profile `eval-judge`: openai/gpt-6-luna; prompt `assistant-judge.v1` |

## Summary

A case passes when every check of every turn passes. Pass rate = share of cases that passed, per repeat; mean ± standard deviation over the repeats.

| Metric | Value |
|---|---|
| Cases / runs / turns | 34 / 102 / 144 |
| **Pass rate** | **83% ± 4** |
| Cases: pass every repeat / flaky / fail every repeat | 27 / 3 / 4 |
| **Grounding violation rate** (turns naming a movie no tool returned / turns naming movies) | **10.8%** (22 of 497 titles) |
| Judge mean (1–5) | 4.39 ± 0.10 |
| Judge: relevance | 4.55 ± 0.21 |
| Judge: explanations | 3.90 ± 0.10 |
| Judge: tone | 4.96 ± 0.07 |
| Judge: constraints | 4.14 ± 0.15 |
| Judge failures (invalid output) | 53 |
| Tokens per turn (input / output) | 6324 / 218 |
| Tokens in total (input / output) | 910625 / 31383 |
| Estimated cost of the assistant (without the judge) | $0.0485 |
| Model calls per turn | 2.18 |
| Turn latency p50 / p95 | 3.5 s / 10.4 s |
| Iteration limit hits | 0 |
| Runs with a provider error | 0 |

## Checks

Pass rate of each deterministic check over the turns it applies to.

| Check | Turns | Pass rate |
|---|---:|---:|
| `answered` | 144 | 100% |
| `tools` | 144 | 94% |
| `movies` | 24 | 100% |
| `cards` | 60 | 95% |
| `cart` | 39 | 100% |
| `confirmation` | 39 | 95% |
| `language` | 144 | 100% |
| `text` | 36 | 86% |
| `grounding` | 144 | 90% |
| `no-order` | 144 | 100% |
| `iterations` | 144 | 100% |

## By locale

| Group | Cases | Pass rate | Grounding violations | Judge mean |
|---|---:|---:|---:|---:|
| en | 14 | 90% ± 4 | 2% | 4.50 |
| ru | 10 | 57% ± 12 | 31% | 4.13 |
| sv | 10 | 100% ± 0 | 0% | 4.43 |

## By tag

| Group | Cases | Pass rate | Grounding violations | Judge mean |
|---|---:|---:|---:|---:|
| buy-or-rent | 5 | 60% ± 0 | 17% | 4.48 |
| cart | 6 | 83% ± 0 | 9% | 3.92 |
| checkout | 8 | 88% ± 0 | 7% | 4.77 |
| clarify | 2 | 100% ± 0 | 0% | 5.00 |
| constraint | 8 | 79% ± 7 | 7% | 4.11 |
| details | 3 | 78% ± 19 | 22% | 3.85 |
| follow-up | 3 | 67% ± 0 | 0% | 2.58 |
| language | 1 | 67% ± 58 | 17% | — |
| no-results | 2 | 100% ± 0 | 0% | 3.33 |
| real | 3 | 33% ± 0 | 20% | 4.67 |
| recommend | 7 | 90% ± 8 | 10% | 4.46 |
| similar | 2 | 50% ± 0 | 33% | 4.38 |

## Cases

| Case | Locale | Tags | Passed | Failed checks (turns) | Judge |
|---|---|---|---:|---|---:|
| `en-space` | en | recommend | 3/3 |  | 4.08 |
| `en-thriller-90s-no-horror` | en | constraint | 3/3 |  | 3.92 |
| `en-real-before-sunrise-buy` | en | real, similar, checkout | 3/3 |  | 4.67 |
| `en-rent-first-family` | en | checkout, buy-or-rent | 3/3 |  | 4.67 |
| `en-add-keep-looking` | en | cart | 3/3 |  | 4.17 |
| `en-ask-buy-or-rent` | en | buy-or-rent, checkout | 3/3 |  | 4.92 |
| `en-details-inception` | en | details | 3/3 |  | 4.63 |
| `en-vague` | en | clarify | 3/3 |  | 5.00 |
| `en-no-documentaries` | en | no-results | 3/3 |  | — |
| `en-cart-view-pay` | en | cart, checkout | 3/3 |  | — |
| `en-remove-from-cart` | en | cart | 3/3 |  | — |
| `en-scifi-80s-follow-up` | en | follow-up, constraint | 3/3 |  | — |
| `en-real-just-one-after-stop` | en | real, follow-up, constraint | 0/3 | `tools` ×3, `cards` ×3, `text` ×2 | — |
| `en-switch-to-russian` | en | language | 2/3 | `grounding` ×1 | — |
| `ru-space` | ru | recommend | 3/3 |  | — |
| `ru-space-no-horror-cheap` | ru | follow-up, constraint | 3/3 |  | 3.88 |
| `ru-time-travel-comedy` | ru | recommend | 3/3 |  | — |
| `ru-real-evening-add-first` | ru | real, cart, buy-or-rent | 0/3 | `grounding` ×3, `tools` ×5, `text` ×3, `confirmation` ×2 | — |
| `ru-miyazaki-rent-checkout` | ru | checkout, buy-or-rent | 0/3 | `grounding` ×3 | 4.67 |
| `ru-details-godfather` | ru | details | 1/3 | `grounding` ×2 | 3.33 |
| `ru-similar-matrix` | ru | similar | 0/3 | `grounding` ×3 | 4.08 |
| `ru-soviet-comedy` | ru | recommend, constraint | 1/3 | `grounding` ×2 | 5.00 |
| `ru-cart-pay` | ru | checkout | 3/3 |  | — |
| `ru-war-before-1970` | ru | constraint | 3/3 |  | — |
| `sv-space` | sv | recommend | 3/3 |  | — |
| `sv-bergman` | sv | recommend, constraint | 3/3 |  | — |
| `sv-christmas-family` | sv | recommend | 3/3 |  | — |
| `sv-80s-no-horror` | sv | constraint | 3/3 |  | — |
| `sv-western-buy` | sv | checkout | 3/3 |  | 4.92 |
| `sv-korean-add-rent` | sv | cart, buy-or-rent | 3/3 |  | 3.67 |
| `sv-details-titanic` | sv | details | 3/3 |  | 3.92 |
| `sv-no-documentaries` | sv | no-results | 3/3 |  | 5.00 |
| `sv-vague` | sv | clarify | 3/3 |  | 5.00 |
| `sv-cart-remove-pay` | sv | cart, checkout | 3/3 |  | — |

## Failed turns

### `en-real-just-one-after-stop` #1, turn 1

- **tools**: not called: search_movies
- **cards**: 0 cards, at least 1
- **text**: matches /what (kind|genre|mood|sort)/
- User: Just one, please.
- Tools: none
- Answer: I'd love to help — just let me know what kind of movie you're after (a genre, a mood, a time period, or something like a film you enjoyed), and I'll pick one for you.

### `en-real-just-one-after-stop` #2, turn 1

- **tools**: not called: search_movies
- **cards**: 0 cards, at least 1
- User: Just one, please.
- Tools: none
- Answer: I'd be happy to help — but I'm not sure which movie you mean. Could you tell me what you're looking for (a genre, a mood, or a movie it should be like)?

### `en-real-just-one-after-stop` #3, turn 1

- **tools**: not called: search_movies
- **cards**: 0 cards, at least 1
- **text**: matches /what (kind|genre|mood|sort)/
- User: Just one, please.
- Tools: none
- Answer: I'd be happy to help — but I'm not sure what you're looking for. What kind of movie would you like: a genre, a mood, or something similar to a film you like?

### `en-switch-to-russian` #2, turn 2

- **grounding**: not from tools: взрослые
- User: А есть что-нибудь из этого для маленьких детей?
- Tools: `search_movies` {"genresInclude":["animation","family"],"language":"ja","query":"gentle animated movie for small children"}
- Answer: Да, вот самые подходящие для малышей: - **Ponyo** (2008) — добрая история о мальчике и рыбке, которая хочет стать человеком; яркая, простая и очень детская. - **My Neighbor Totoro** (1988) — две сестры и лесные духи: спокойный, мягкий мультфильм без страшных сцен. - **Kiki's Delivery Service** (1989) — юная ведьма и её кот-помощник, тёплая и уютная сказка. Из прошлого списка «взрослые» вещи вроде …

### `ru-real-evening-add-first` #1, turn 1

- **grounding**: not from tools: Ла-Ла Ленда
- User: Хочу что-нибудь на вечер с женой, нам понравились «Перед рассветом» и «Ла-Ла Ленд».
- Tools: `search_movies` {"query":"романтическая история о встрече, разговорах и любви, музыкальная меланхолия","genresInclude":["romance","drama","music"],"minRating":7,"limit":8}
- Answer: Отличный выбор для вечера вдвоём — вот что подойдёт под настроение «Перед рассветом» и «Ла-Ла Ленда»: - **Перед закатом** (2004) — вторая часть той же трилогии: Джесс и Селин встречаются снова спустя девять лет, и весь фильм держится на их разговоре. - **Дневник памяти** (2004) — большая трогательная история любви, проверенной годами и разлукой. - **Любовь** (2012) — тихая, очень честная история п…

### `ru-real-evening-add-first` #1, turn 2

- **tools**: called: add_to_cart
- **text**: no match for /куп/
- User: Добавь первый в корзину, я ещё посмотрю.
- Tools: `add_to_cart` {"movieId":"01a1ef1e-5ec2-7d7a-b593-ad2e140c74f1","offerType":"rent"}
- Cart: Перед закатом (rent)
- Answer: Готово — **Перед закатом** (2004) добавлен в корзину как аренда на 48 часов за 2.99 USD. Спокойно смотрите дальше, а когда решите — скажите, и я покажу заказ для оплаты.

### `ru-real-evening-add-first` #1, turn 3

- **tools**: not called: add_to_cart; called: request_checkout
- **confirmation**: asked for checkout confirmation
- User: В аренду.
- Tools: `request_checkout` 
- Cart: Перед закатом (rent)
- Answer: Всё готово к оплате: **Перед закатом** (2004) — аренда на 48 часов, 2.99 USD. Проверьте заказ и нажмите кнопку **Go to payment** — оплата произойдёт только после вашего клика.

### `ru-real-evening-add-first` #2, turn 1

- **grounding**: not from tools: классического
- User: Хочу что-нибудь на вечер с женой, нам понравились «Перед рассветом» и «Ла-Ла Ленд».
- Tools: `search_movies` {"query":"romantic evening, love story, music, conversation, walking together","genresInclude":["romance","drama","music"],"minRating":7}
- Answer: Вот несколько вариантов для вечера вдвоём — с той же атмосферой разговоров, чувств и музыки: - **Перед закатом** (2004) — прямое продолжение «Перед рассветом»: Джесс и Селин встречаются снова спустя девять лет, и снова всё держится на их разговоре. - **Дневник памяти** (2004) — трогательная история любви сквозь годы, если хочется более эмоционального и «классического» романтического кино. - **Спеш…

### `ru-real-evening-add-first` #2, turn 2

- **tools**: called: add_to_cart
- **text**: no match for /аренд|прокат/
- User: Добавь первый в корзину, я ещё посмотрю.
- Tools: `add_to_cart` {"movieId":"01a1ef1e-5ec2-7d7a-b593-ad2e140c74f1","offerType":"buy"}
- Cart: Перед закатом (buy)
- Answer: Добавил **Перед закатом** (2004) в корзину — покупка за 9.99 USD. Смотрите дальше спокойно: когда решите, скажите, и я оформлю заказ. Если захотите ещё варианты — подберу.

### `ru-real-evening-add-first` #3, turn 1

- **grounding**: not from tools: Ла-Ла Ленда
- User: Хочу что-нибудь на вечер с женой, нам понравились «Перед рассветом» и «Ла-Ла Ленд».
- Tools: `search_movies` {"query":"romantic couple conversation walking through a city, love story","genresInclude":["romance","drama"],"limit":8}
- Answer: Отличный выбор для вечера вдвоём — вот что подойдёт под настроение «Перед рассветом» и «Ла-Ла Ленда»: - **Перед закатом** (2004) — прямое продолжение «Перед рассветом»: Джесс и Селин встречаются снова спустя девять лет и снова говорят обо всём на свете. - **Снова мы** (2021) — тихая, музыкальная история одной ночи о паре средних лет, которая через танец возвращает вкус к жизни. - **С любовью, Рози…

### `ru-real-evening-add-first` #3, turn 2

- **tools**: called: add_to_cart
- **text**: no match for /куп/
- User: Добавь первый в корзину, я ещё посмотрю.
- Tools: `add_to_cart` {"movieId":"01a1ef1e-5ec2-7d7a-b593-ad2e140c74f1","offerType":"rent"}
- Cart: Перед закатом (rent)
- Answer: Добавил **Перед закатом** (2004) в корзину — аренда на 48 часов за 2.99 USD. Смотрите спокойно, а когда решите оплатить, скажите — покажу заказ.

### `ru-real-evening-add-first` #3, turn 3

- **tools**: not called: add_to_cart; called: request_checkout
- **confirmation**: asked for checkout confirmation
- User: В аренду.
- Tools: `request_checkout` 
- Cart: Перед закатом (rent)
- Answer: Готово: в заказе **Перед закатом** (2004), аренда на 48 часов, итого 2.99 USD. Проверьте заказ и нажмите кнопку **Go to payment** — оплата происходит только после вашего клика.

### `ru-miyazaki-rent-checkout` #1, turn 1

- **grounding**: not from tools: детская
- User: Найди мультфильм Хаяо Миядзаки для детей.
- Tools: `search_movies` {"query":"Hayao Miyazaki animated film for children","genresInclude":["animation","family"]}
- Answer: Вот мультфильмы Миядзаки, которые отлично подойдут детям: - **Мой сосед Тоторо** (1988) — самая «детская» сказка о двух сёстрах и добрых лесных духах, без страшных сцен. - **Ведьмина служба доставки** (1989) — тёплая история о юной ведьме Кики, её коте и первой самостоятельной работе. - **Рыбка Поньо на утёсе** (2008) — милая дружба маленькой рыбки и пятилетнего мальчика, много моря и волшебства. …

### `ru-miyazaki-rent-checkout` #2, turn 1

- **grounding**: not from tools: детская | взрослая
- User: Найди мультфильм Хаяо Миядзаки для детей.
- Tools: `search_movies` {"query":"Hayao Miyazaki animated film for children","genresInclude":["animation","family"],"limit":8}
- Answer: Вот мультфильмы Миядзаки, которые отлично подойдут для детей: - **Мой сосед Тоторо** (1988) — самая «детская» история о двух сёстрах и добрых лесных духах, спокойная и волшебная. - **Рыбка Поньо на утёсе** (2008) — трогательная сказка о дружбе маленькой рыбки и пятилетнего мальчика. - **Ведьмина служба доставки** (1989) — добрая история о юной ведьме Кики, её коте и первой самостоятельной работе. …

### `ru-miyazaki-rent-checkout` #3, turn 1

- **grounding**: not from tools: детская
- User: Найди мультфильм Хаяо Миядзаки для детей.
- Tools: `search_movies` {"query":"Hayao Miyazaki animated film for children","genresInclude":["animation","family"]}
- Answer: Вот мультфильмы Миядзаки, которые отлично подойдут детям: - **Мой сосед Тоторо** (1988) — самая «детская» сказка студии: две сестры и добрые лесные духи, без страшных сцен. - **Ведьмина служба доставки** (1989) — тёплая история о юной ведьме Кики, которая учится самостоятельности. - **Рыбка Поньо на утёсе** (2008) — милая дружба малыша и рыбки-девочки, для самых маленьких. - **Небесный замок Лапут…

### `ru-details-godfather` #1, turn 1

- **grounding**: not from tools: Фрэнсис Форд Коппола | 175 минут
- User: Кто снял «Крёстного отца» и сколько он длится?
- Tools: `search_movies` {"query":"Крёстный отец The Godfather","limit":3}; `get_movie_details` {"movieId":"01a1ef1e-5ec2-725b-9050-e7e1fa356f71"}
- Answer: «Крёстный отец» (1972) снял **Фрэнсис Форд Коппола**, а длится фильм **175 минут** (2 часа 55 минут). Сценарий Коппола написал вместе с Марио Пузо, автором романа. В главных ролях Марлон Брандо (дон Вито Корлеоне) и Аль Пачино (Майкл). В каталоге фильм доступен: покупка — 9.99 USD, аренда на 48 часов — 3.99 USD. Хотите взять его или подобрать что-то похожее?

### `ru-details-godfather` #2, turn 1

- **grounding**: not from tools: Фрэнсис Форд Коппола | 175 минут
- User: Кто снял «Крёстного отца» и сколько он длится?
- Tools: `search_movies` {"query":"Крёстный отец The Godfather","limit":5}; `get_movie_details` {"movieId":"01a1ef1e-5ec2-725b-9050-e7e1fa356f71"}
- Answer: «Крёстный отец» (1972) снял **Фрэнсис Форд Коппола**, а длится он **175 минут** (2 часа 55 минут). Сценарий Коппола написал вместе с Марио Пузо, автором романа. В главных ролях Марлон Брандо (дон Вито Корлеоне) и Аль Пачино (Майкл). В магазине фильм можно купить за 9.99 USD или взять в аренду на 48 часов за 3.99 USD — если захотите, добавлю его в корзину.

### `ru-similar-matrix` #1, turn 1

- **grounding**: not from tools: Матрицы | Кукловода
- User: Хочу что-то похожее на «Матрицу».
- Tools: `search_movies` {"query":"hacker discovers reality is a simulation, dystopian virtual world","genresInclude":["science-fiction","action"]}
- Answer: Вот несколько фильмов в духе «Матрицы» — с виртуальной реальностью, хакерами и вопросами о том, что реально: - **Матрица** (1999) — сама классика: хакер Нео узнаёт правду о реальности. - **Призрак в доспехах** (1995) — киберпанк про хакера-«Кукловода» и людей с кибернетическими телами. - **Бегущий по лезвию** (1982) — неонуар о репликантах и границе между человеком и машиной. - **Бегущий по лезвию…

### `ru-similar-matrix` #2, turn 1

- **grounding**: not from tools: Матрице | родственник | Матрицы | застрял в виртуальном мире
- User: Хочу что-то похожее на «Матрицу».
- Tools: `search_movies` {"query":"hacker discovers reality is a simulation, virtual world, chosen one","genresInclude":["science-fiction","action"],"limit":8}
- Answer: Вот что в каталоге ближе всего к «Матрице» по духу: - **Призрак в доспехах** (1995) — киберпанк о хакере и границах между человеком и машиной, самый близкий «родственник» «Матрицы». - **Бегущий по лезвию** (1982) — мрачный неоновый мир будущего и вопросы о том, что значит быть человеком. - **Бегущий по лезвию 2049** (2017) — продолжение той же вселенной, ещё более медитативное и визуально мощное. …

### `ru-similar-matrix` #3, turn 1

- **grounding**: not from tools: Матрице | хакерское
- User: Хочу что-то похожее на «Матрицу».
- Tools: `search_movies` {"query":"похоже на Матрицу: виртуальная реальность, хакер узнаёт правду о мире, киберпанк","genresInclude":["science-fiction","action"],"limit":6}
- Answer: Вот что в каталоге ближе всего к «Матрице» по духу — киберпанк, вопросы о реальности и техника против человека: - **Бегущий по лезвию** (1982) — классический киберпанк о том, что значит быть человеком, когда технологии стирают границу. - **Бегущий по лезвию 2049** (2017) — продолжение в том же мире: тайны, искусственные люди и поиск правды о себе. - **День разоблачения** (2026) — человечество узна…

### `ru-soviet-comedy` #1, turn 1

- **grounding**: not from tools: Шеф, всё пропало
- User: Посоветуй советскую комедию.
- Tools: `search_movies` {"query":"советская комедия","genresInclude":["comedy"],"language":"ru","yearTo":1991}
- Answer: Вот что нашлось в каталоге: - **The Diamond Arm** (1969) — классическая советская комедия с погонями, контрабандой бриллиантов и незабываемым «Шеф, всё пропало». - **Ivan Vasilyevich Changes His Profession** (1973) — комедия с машиной времени: Иван Грозный в советской квартире, а управдом — на царском троне. Хотите ещё варианты — например, комедии Гайдая или более поздние советские фильмы?

### `ru-soviet-comedy` #2, turn 1

- **grounding**: not from tools: Шеф, всё пропало
- User: Посоветуй советскую комедию.
- Tools: `search_movies` {"query":"советская комедия","genresInclude":["comedy"],"language":"ru","limit":8}
- Answer: Вот что нашлось в каталоге: - **The Diamond Arm** (1969) — классическая советская комедия с контрабандой бриллиантов и незабываемым «Шеф, всё пропало». - **Ivan Vasilyevich Changes His Profession** (1973) — машина времени переносит героев в палаты Ивана Грозного, и путаница начинается. Каталог по этому запросу небольшой — если хотите, могу поискать комедии без привязки к советскому кино или посмот…

## Judge: runs scored below 3.5

- `ru-details-godfather` #1: relevance 4, explanations 2, tone 5, constraints 2. Ответ на русском и прямо отвечает на вопрос, но детали о режиссёре, длительности, сценарии, актёрах и ценах не подтверждены данными инструментов. Предложение выбрать покупку или аренду уместно, однако объяснение и следование правилам каталога подрывают неподтверждённые сведения.
- `ru-details-godfather` #3: relevance 4, explanations 2, tone 5, constraints 2. Ответ дан по-русски и прямо отвечает на вопрос, но карточки каталога не содержат сведений о режиссёре, длительности, актёрах или ценах; эти детали не подтверждены данными инструментов. Предложение добавить фильм в корзину уместно, но цены выглядят выдуманными.
- `sv-korean-add-rent` #2: relevance 3, explanations 4, tone 4, constraints 2. Första svaret rekommenderar relevanta koreanska thrillers med konkreta förklaringar. I andra turen valde kunden den andra filmen (The Chaser), men assistenten lade till Memories of Murder och erbjuder betalning trots att kunden ville fortsätta leta.
- `sv-korean-add-rent` #3: relevance 2, explanations 4, tone 5, constraints 1. Första svaret rekommenderar relevanta koreanska thrillers och ger konkreta skäl, men vid tillägget i varukorgen valde assistenten fel film: kunden bad om den andra (The Chaser), men lades in Memories of Murder. Priset och hyresvillkoren anges utan stöd i verktygsresultatet, och assistenten nämner betalning i stället för att hänvisa till en checkout-knapp.
- `en-details-inception` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-no-documentaries` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-no-documentaries` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-no-documentaries` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-cart-view-pay` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-cart-view-pay` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-cart-view-pay` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-remove-from-cart` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-remove-from-cart` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-remove-from-cart` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-scifi-80s-follow-up` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-scifi-80s-follow-up` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-scifi-80s-follow-up` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-real-just-one-after-stop` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-real-just-one-after-stop` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-real-just-one-after-stop` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-switch-to-russian` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-switch-to-russian` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `en-switch-to-russian` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-space` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-space` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-space` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-space-no-horror-cheap` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-time-travel-comedy` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-time-travel-comedy` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-time-travel-comedy` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-real-evening-add-first` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-real-evening-add-first` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-real-evening-add-first` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-soviet-comedy` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-cart-pay` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-cart-pay` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-cart-pay` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-war-before-1970` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-war-before-1970` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `ru-war-before-1970` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-space` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-space` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-space` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-bergman` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-bergman` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-bergman` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-christmas-family` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-christmas-family` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-christmas-family` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-80s-no-horror` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-80s-no-horror` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-80s-no-horror` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-no-documentaries` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-vague` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-cart-remove-pay` #1: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-cart-remove-pay` #2: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)
- `sv-cart-remove-pay` #3: no score (the judge failed: HTTP 429 (: )

Rate limit exceeded: new-account-rpm/openai/gpt-6-luna-20260922. Rate limit reached: new accounts are limited to 20 requests per minute for this model. Please retry shortly.)

## Conclusion

Cloud model with prompt v2: 83% ± 4, p50 3.5 s. Grounding violation rate 10.8% was mostly false positives of the check (lower-case emphasis, Russian inflection, a Russian button label, runtimes); this led to the `GroundingCheck` tuning. 53 runs not judged (rate limit). See [the I14 summary](2026-11-18_I14_assistant-evaluation.md) for the comparison of all runs of the day.
