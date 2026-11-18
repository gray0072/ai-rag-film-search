# Recorded TMDB responses

Real TMDB API v3 responses recorded by `tools/seed-builder` (its `.cache`), trimmed to keep the files small:
translations only for `en`, `ru`, `sv`, the first 12 cast members, directors and writers only, no production
companies. `top_rated-p1.json` is a page built from the three movies plus one adult item (filtered by the import).

Used by the unit tests of `TmdbClient` / `TmdbImporter` and by the Worker integration tests (`TmdbStub`).
Data: TMDB (this product uses the TMDB API but is not endorsed or certified by TMDB).
