# I16: search quality when the catalog grows (1000 → 1182 movies)

- **Date:** 2026-11-18, iteration I16
- **Question:** how do the search metrics change when the TMDB import adds movies the golden set does not know?
- **Runs:** [before](2026-11-18_search_i16-before-1000-seed.md) (fresh container, the committed seed of 1000 movies)
  and [after](2026-11-18_search_i16-after-1182-tmdb.md) (`--connection` to the dev database after the import).
  Same code, settings and models: `text-embedding-embeddinggemma-300m` (en vectors), `google/gemma-4-e4b` for query
  understanding, `--repeat 1`.

## How the catalog grew

`POST /api/admin/ingestion/tmdb { "list": "topRated", "pages": 50 }`, run by the Worker:

| | First run | Rerun (same evening) |
|---|---|---|
| List movies (50 pages, duplicates across pages removed) | 987 | 987 |
| Created / updated / unchanged | 180 / 807 / 0 | 0 / 2 / 985 |
| Failed / not found / skipped | 0 / 0 / 0 | 0 / 0 / 0 |
| TMDB requests (genres 3, list 50, details 987) | 1040 in 258 s (4.0 / s, the 40 per 10 s limit) | 1040 in 266 s |
| Embedding phase | failed: the LM Studio server was not running; the job ended `failed` with the counters, keyword search already found the new movies | 198 texts embedded (180 new + 18 changed texts), 984 unchanged, 14 s |

807 updates on the first run: the seed was generated a day earlier and vote counts / popularity moved, which changes
`SourceHash`. Only 18 of them changed the embedding text (overview, tagline or keywords edited at TMDB). The catalog
has 1182 movies, 1182 distinct TMDB ids (the dev database had 2 extra movies besides the seed).

## Results

Means over 105 cases. "After, judged" counts 13 new movies as relevant in 8 cases where they are clearly right
(below); everything else is as labelled.

| Mode | Metric | Before (1000) | After (1182), as labelled | After, judged |
|---|---|---|---|---|
| keyword | Recall@10 / MRR@10 | 0.296 / 0.251 | 0.287 / 0.246 | 0.285 / 0.253 |
| semantic | Recall@10 / MRR@10 / nDCG@10 | 0.865 / 0.794 / 0.784 | 0.855 / 0.770 / 0.769 | 0.861 / 0.805 / 0.777 |
| hybrid | Recall@10 / MRR@10 / nDCG@10 | 0.873 / 0.788 / 0.786 | 0.866 / 0.757 / 0.767 | 0.871 / 0.792 / 0.775 |
| hybrid+qu | Recall@10 / MRR@10 / nDCG@10 | 0.870 / 0.827 / 0.812 | 0.858 / 0.814 / 0.800 | 0.867 / 0.825 / 0.805 |

Latency p50: semantic 60 → 66 ms, hybrid 79 → 95 ms (the dev database also serves the running app; an exact scan
over 18 % more vectors).

### Cases that got worse (34 case × mode pairs in semantic, hybrid, hybrid+qu)

- **Relevant, not labelled** (judged relevant above): "films starring Tom Hanks" → *Cast Away*, *Philadelphia* at
  #1–#2; "Кубрик до 1970" → *The Killing* (1956); "Тарковский" → *The Sacrifice*; the mood case "heartwarming animated
  movie about family and memories" (en, ru, sv) → *When Marnie Was There*, *If Anything Happens I Love You*; "мультфильм
  для детей про драконов, только не страшный" → *How to Train Your Dragon 2*; "en rolig animerad film om djur" → *The
  Big Bad Fox and Other Tales*, *Lucky and Zorba*.
- **Plausible but not right:** "смешной фильм про ограбление, но не триллер" → *The Heist of the Century* (a heist
  thriller with jokes); "Tarantino westerns" → *The Searchers*; "лёгкая романтическая комедия … в Париже" → *The Young
  Girls of Rochefort*.
- **Noise:** cross-language plot queries pick up thematic neighbours (*The Warriors* for the Monterrey cumbia gang
  story, *Onibaba* for La Llorona, *Kin-dza-dza!* for the wormhole query, *Nightcrawler* for the dream thief in Swedish).
  These are real losses of a larger catalog: more movies share the vague words of a translated plot.

## Conclusion

- As labelled, an 18 % larger catalog costs about 0.01 Recall@10 and 0.01–0.03 MRR. Most of that is the golden set,
  not the search: it was written against the seed and does not know relevant newcomers. With the obvious ones counted,
  semantic and hybrid are on par with the 1000-movie numbers (MRR even a little higher, Recall@10 −0.002 to −0.004).
- The real cost shows in vague cross-language plot queries: more near neighbours compete. Hybrid search and query
  understanding keep ranking the right movie in the top 10 (Recall@10 holds), but lower.
- The golden set stays tied to the seed (`--suite validate` checks every relevant id against the seed), so the runs
  of later iterations stay comparable. Evaluating on a grown catalog needs relevance judgments for the new movies:
  a dataset field for "also relevant if present" ids, or LLM-judged relevance of unlabelled top results (stretch goal,
  noted as an I16 follow-up).
