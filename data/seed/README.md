# Seed data

`movies.seed.json` is the catalog the API loads on first start (Development) and through `POST /api/admin/ingestion/seed`. It is committed, so the app runs without any API key. Spec: [08 Data ingestion §2](../../docs/spec/08-data-ingestion.md#2-seed-file).

## Current file

`"source": "tmdb"`, built on 2026-11-17: 1000 movies (800 top rated + 200 popular), 19 genres. Russian titles for 976 movies and overviews for 982; Swedish titles for 483 and overviews for 857 (TMDB has fewer Swedish translations; missing fields fall back to English). Original languages: 635 English, 99 Japanese, 50 Italian, 47 French, 12 Swedish, 11 Russian and others. About 4.8 MB.

Before that (I03) the file was a hand-made starter set of 34 movies.

## Rebuild from TMDB

1. Get a free API key: create an account on [themoviedb.org](https://www.themoviedb.org/), then Settings → API. Either the v3 API key or the v4 read access token works.
2. Store it in the tool's user secrets (never commit it):

   ```bash
   dotnet user-secrets set Tmdb:ApiKey <key> --project tools/seed-builder
   ```

   Or set the `TMDB__APIKEY` environment variable. The read access token is preferred: it goes in the `Authorization` header, while a v3 key is added to the URL. On the owner's machine `Tmdb:ApiKey` holds the read access token and `Tmdb:ApiKeyV3` the v3 key (kept for reference, not read by the tool); list them with `dotnet user-secrets list --project tools/seed-builder`. User secrets live outside the repository (`%APPDATA%\Microsoft\UserSecrets\filmsearch-seed-builder-3b7e1a52\secrets.json`), so they cannot be committed by accident.
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
