# Seed data

`movies.seed.json` is the catalog the API loads on first start (Development) and through `POST /api/admin/ingestion/seed`. It is committed, so the app runs without any API key. Spec: [08 Data ingestion §2](../../docs/spec/08-data-ingestion.md#2-seed-file).

## Current file

`"source": "hand-made"`: 34 well-known movies (classics, recent hits, Swedish, Russian, Japanese, Korean, French and Italian titles) with English, Russian and Swedish titles and short overviews written for this project. Ratings, vote counts and popularity are approximate, people have no TMDB ids, and there are no posters. It is a starter set until the file is rebuilt from TMDB (about 1000 movies).

## Rebuild from TMDB

1. Get a free API key: create an account on [themoviedb.org](https://www.themoviedb.org/), then Settings → API. Either the v3 API key or the v4 read access token works.
2. Store it in the tool's user secrets (never commit it):

   ```bash
   dotnet user-secrets set Tmdb:ApiKey <key> --project tools/seed-builder
   ```

   Or set the `TMDB__APIKEY` environment variable.
3. Run the builder from the repository root:

   ```bash
   dotnet run --project tools/seed-builder
   ```

   Options (command line, `--Name value`): `TopRated` (default 800), `Popular` (200, only movies not already in the top rated set and with at least `MinPopularVotes` = 300 votes), `Output` (default this file), `Refresh true` (drop the cache).
4. Review the diff, run `dotnet test`, commit the file. To load it into an existing dev database: `POST /api/admin/ingestion/seed` (upsert by TMDB id), or reset the database (see AGENTS.md).

The builder makes about 1000 detail requests, throttled to about 4 per second (TMDB allows more; spec 08 asks for about 40 per 10 seconds), and retries `429` / `5xx`. Every response is cached in `tools/seed-builder/.cache/` (ignored by git), so a stopped run resumes where it stopped. List pages are cached per day.

What it requests: `/genre/movie/list` (en-US, ru-RU, sv-SE), `/movie/top_rated` and `/movie/popular` pages, and `/movie/{id}?language=en-US&append_to_response=credits,keywords,translations,external_ids`. Mapping: `TmdbMovieMapper` in `src/FilmSearch.Infrastructure/Tmdb` (the same code the TMDB sync will use in I16).

## Format

```jsonc
{
  "version": 1,                       // SeedFile.CurrentVersion; the loader rejects other versions
  "source": "tmdb",                   // "tmdb" or "hand-made"
  "generatedAt": "2026-11-17T00:00:00+00:00",
  "genres": [
    { "id": 18, "slug": "drama", "names": { "en": "Drama", "ru": "Драма", "sv": "Drama" } }
  ],
  "movies": [                         // sorted by tmdbId, so a rebuild gives a readable diff
    {
      "tmdbId": 278,                  // upsert key
      "imdbId": "tt0111161",
      "originalTitle": "The Shawshank Redemption",
      "originalLanguage": "en",       // ISO 639-1
      "releaseDate": "1994-09-23",
      "runtimeMinutes": 142,
      "posterPath": "/abc.jpg",       // TMDB image path; the UI builds the URL. No images are committed.
      "voteAverage": 8.7,             // 0–10
      "voteCount": 28000,
      "popularity": 120.0,
      "genreIds": [18, 80],
      "keywords": ["prison", "friendship"],
      "translations": {               // "en" is required; "ru" and "sv" are optional, and so is every field in them
        "en": { "title": "...", "overview": "...", "tagline": "..." },
        "ru": { "title": "...", "overview": "..." },
        "sv": { "title": "..." }
      },
      "credits": [                    // directors, up to 5 writers, top 10 cast
        { "personTmdbId": 4027, "name": "Frank Darabont", "role": "director", "order": 0 },
        { "personTmdbId": 504, "name": "Tim Robbins", "role": "cast", "character": "Andy Dufresne", "order": 0 }
      ]
    }
  ]
}
```

C# records: `SeedFile` (`FilmSearch.Core.Ingestion`), `GenreImport`, `MovieImport`, `TranslationImport`, `CreditImport` (`FilmSearch.Core.Catalog`). JSON options: `SeedJson.Options` (camelCase, enums as camelCase strings, null fields omitted, UTF-8 letters unescaped).

Prices are not in the file: `PricingRule` computes them at load time.

## Attribution

Movie data in a TMDB-built file comes from [TMDB](https://www.themoviedb.org/). This product uses the TMDB API but is not endorsed or certified by TMDB. Use is non-commercial, and the file holds descriptive metadata only (no images, no trailers).
