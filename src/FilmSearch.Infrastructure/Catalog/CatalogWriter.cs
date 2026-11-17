using System.Security.Cryptography;
using System.Text.Json;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Localization;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FilmSearch.Infrastructure.Catalog;

/// <summary>
/// Upserts imported movies by TMDB id. Each batch is one transaction holding a PostgreSQL advisory lock,
/// so two writers (two API replicas seeding on startup, or the seed and a TMDB sync) never insert the same movie twice.
/// A movie whose <see cref="Movie.SourceHash"/> matches the import is left as is; only its prices are rechecked.
/// Created and changed movies are passed to every <see cref="ICatalogChangeHandler"/> inside the same transaction.
/// </summary>
internal sealed partial class CatalogWriter(
    AppDbContext db,
    TimeProvider time,
    IEnumerable<ICatalogChangeHandler> changeHandlers,
    ILogger<CatalogWriter> logger) : ICatalogWriter
{
    /// <summary>Key of the transaction-level advisory lock that serializes catalog upserts.</summary>
    private const long UpsertLockKey = 0x46_53_43_41_54; // "FSCAT"

    public async Task UpsertGenresAsync(IReadOnlyList<GenreImport> genres, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(ct);

            var existing = await db.Set<Genre>().ToDictionaryAsync(g => g.Id, ct);
            foreach (var import in genres)
            {
                var nameEn = Name(import, Locales.English) ?? import.Slug;
                if (!existing.TryGetValue(import.Id, out var genre))
                {
                    genre = new Genre { Id = import.Id, Slug = import.Slug, NameEn = nameEn, NameRu = nameEn, NameSv = nameEn };
                    db.Add(genre);
                }

                genre.Slug = import.Slug;
                genre.NameEn = nameEn;
                genre.NameRu = Name(import, Locales.Russian) ?? nameEn;
                genre.NameSv = Name(import, Locales.Swedish) ?? nameEn;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, cancellationToken);

        static string? Name(GenreImport genre, string locale) =>
            genre.Names.TryGetValue(locale, out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;
    }

    public async Task<UpsertResult> UpsertMoviesAsync(IReadOnlyList<MovieImport> movies, MovieSource source, CancellationToken cancellationToken)
    {
        if (movies.Count == 0)
        {
            return UpsertResult.Empty;
        }

        // The last occurrence of a TMDB id wins, like a sequence of single upserts would.
        var imports = movies.GroupBy(m => m.TmdbId).Select(g => g.Last()).ToList();
        foreach (var import in imports)
        {
            if (!import.Translations.TryGetValue(Locales.English, out var en) || string.IsNullOrWhiteSpace(en.Title))
            {
                throw new ArgumentException($"Movie {import.TmdbId} has no English title.", nameof(movies));
            }
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(ct);

            var (result, changedIds) = await UpsertBatchAsync(imports, source, ct);

            await db.SaveChangesAsync(ct);
            if (changedIds.Count > 0)
            {
                foreach (var handler in changeHandlers)
                {
                    await handler.OnMoviesChangedAsync(changedIds, ct);
                }
            }

            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    /// <returns>The counts and the ids of created movies and movies whose data changed (not only prices).</returns>
    private async Task<(UpsertResult Result, List<Guid> ChangedIds)> UpsertBatchAsync(
        List<MovieImport> imports,
        MovieSource source,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var tmdbIds = imports.Select(m => (int?)m.TmdbId).ToList();
        var existing = await db.Set<Movie>()
            .Include(m => m.Translations)
            .Include(m => m.Genres)
            .Include(m => m.Credits)
            .Include(m => m.Offers)
            .Where(m => tmdbIds.Contains(m.TmdbId))
            .AsSplitQuery()
            .ToDictionaryAsync(m => m.TmdbId!.Value, ct);

        var genres = await db.Set<Genre>().ToDictionaryAsync(g => g.Id, ct);
        var people = await LoadPeopleAsync(imports, ct);

        int created = 0, updated = 0, unchanged = 0;
        var changedIds = new List<Guid>();
        foreach (var import in imports)
        {
            var hash = ComputeHash(import);
            var isNew = !existing.TryGetValue(import.TmdbId, out var movie);
            if (movie is null)
            {
                movie = new Movie
                {
                    Id = Guid.CreateVersion7(now),
                    TmdbId = import.TmdbId,
                    Title = string.Empty,
                    OriginalTitle = string.Empty,
                    OriginalLanguage = string.Empty,
                    Source = source,
                    CreatedAt = now,
                };
                db.Add(movie);
            }

            var dataChanged = movie.SourceHash != hash;
            if (dataChanged)
            {
                Apply(movie, import, hash, genres, people);
            }

            var pricesChanged = SyncOffers(movie, today, now);
            if (isNew || dataChanged)
            {
                changedIds.Add(movie.Id);
            }

            if (isNew)
            {
                movie.UpdatedAt = now;
                created++;
            }
            else if (dataChanged || pricesChanged)
            {
                movie.UpdatedAt = now;
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        LogBatch(logger, imports.Count, created, updated, unchanged);
        return (new UpsertResult(created, updated, unchanged), changedIds);
    }

    private void Apply(Movie movie, MovieImport import, string hash, Dictionary<int, Genre> genres, PeopleIndex people)
    {
        var en = import.Translations[Locales.English];

        movie.ImdbId = Clean(import.ImdbId);
        movie.Title = en.Title!.Trim();
        movie.OriginalTitle = Clean(import.OriginalTitle) ?? movie.Title;
        movie.OriginalLanguage = import.OriginalLanguage.Trim().ToLowerInvariant();
        movie.ReleaseDate = import.ReleaseDate;
        movie.Year = import.ReleaseDate?.Year;
        movie.RuntimeMinutes = import.RuntimeMinutes is > 0 ? import.RuntimeMinutes : null;
        movie.Overview = Clean(en.Overview);
        movie.Tagline = Clean(en.Tagline);
        movie.PosterPath = Clean(import.PosterPath);
        movie.BackdropPath = Clean(import.BackdropPath);
        movie.VoteAverage = Math.Round(import.VoteAverage, 1);
        movie.VoteCount = import.VoteCount;
        movie.Popularity = import.Popularity;
        movie.Keywords = [.. import.Keywords.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        movie.SourceHash = hash;

        SyncTranslations(movie, import);
        SyncGenres(movie, import, genres);
        SyncCredits(movie, import, people);
    }

    private static void SyncTranslations(Movie movie, MovieImport import)
    {
        var wanted = new Dictionary<string, TranslationImport>(StringComparer.Ordinal);
        foreach (var (key, value) in import.Translations)
        {
            var locale = key.Trim().ToLowerInvariant();
            var translation = new TranslationImport(Clean(value.Title), Clean(value.Overview), Clean(value.Tagline));
            if (!Locales.All.Contains(locale) || translation is { Title: null, Overview: null, Tagline: null })
            {
                continue;
            }

            wanted[locale] = translation;
        }

        movie.Translations.RemoveAll(t => !wanted.ContainsKey(t.Locale));
        foreach (var (locale, value) in wanted)
        {
            var translation = movie.Translations.Find(t => t.Locale == locale);
            if (translation is null)
            {
                translation = new MovieTranslation { MovieId = movie.Id, Locale = locale };
                movie.Translations.Add(translation);
            }

            translation.Title = value.Title;
            translation.Overview = value.Overview;
            translation.Tagline = value.Tagline;
        }
    }

    private void SyncGenres(Movie movie, MovieImport import, Dictionary<int, Genre> genres)
    {
        var wanted = new HashSet<int>();
        foreach (var id in import.GenreIds)
        {
            if (genres.ContainsKey(id))
            {
                wanted.Add(id);
            }
            else
            {
                LogUnknownGenre(logger, import.TmdbId, id);
            }
        }

        movie.Genres.RemoveAll(g => !wanted.Contains(g.Id));
        foreach (var id in wanted.Where(id => !movie.Genres.Exists(g => g.Id == id)))
        {
            movie.Genres.Add(genres[id]);
        }
    }

    private static void SyncCredits(Movie movie, MovieImport import, PeopleIndex people)
    {
        // One row per (person, role): an actor with two characters keeps the first one.
        var wanted = new Dictionary<(Guid PersonId, CreditRole Role), CreditImport>();
        foreach (var credit in import.Credits)
        {
            var person = people.Get(credit);
            wanted.TryAdd((person.Id, credit.Role), credit);
        }

        movie.Credits.RemoveAll(c => !wanted.ContainsKey((c.PersonId, c.Role)));
        foreach (var ((personId, role), credit) in wanted)
        {
            var row = movie.Credits.Find(c => c.PersonId == personId && c.Role == role);
            if (row is null)
            {
                row = new MovieCredit { MovieId = movie.Id, PersonId = personId, Person = people.Get(credit), Role = role };
                movie.Credits.Add(row);
            }

            row.Character = Clean(credit.Character);
            row.Order = credit.Order;
        }
    }

    /// <summary>Creates or updates the Buy and Rent offers from <see cref="PricingRule"/>. Returns true when anything changed.</summary>
    private static bool SyncOffers(Movie movie, DateOnly today, DateTimeOffset now)
    {
        var changed = false;
        foreach (var price in PricingRule.Calculate(movie.Year, movie.VoteCount, today))
        {
            var offer = movie.Offers.Find(o => o.Type == price.Type);
            if (offer is null)
            {
                offer = new Offer { Id = Guid.CreateVersion7(now), MovieId = movie.Id, Type = price.Type, Currency = price.Currency };
                movie.Offers.Add(offer);
                changed = true;
            }

            if (offer.PriceMinor != price.PriceMinor
                || offer.Currency != price.Currency
                || offer.RentalHours != price.RentalHours
                || !offer.IsActive)
            {
                offer.PriceMinor = price.PriceMinor;
                offer.Currency = price.Currency;
                offer.RentalHours = price.RentalHours;
                offer.IsActive = true;
                changed = true;
            }
        }

        return changed;
    }

    private async Task<PeopleIndex> LoadPeopleAsync(List<MovieImport> imports, CancellationToken ct)
    {
        var credits = imports.SelectMany(m => m.Credits).ToList();
        var tmdbIds = credits.Where(c => c.PersonTmdbId is not null).Select(c => c.PersonTmdbId).Distinct().ToList();
        var names = credits.Where(c => c.PersonTmdbId is null).Select(c => c.Name.Trim()).Distinct().ToList();

        var byTmdbId = await db.Set<Person>()
            .Where(p => tmdbIds.Contains(p.TmdbId))
            .ToDictionaryAsync(p => p.TmdbId!.Value, ct);
        var byName = (await db.Set<Person>()
                .Where(p => p.TmdbId == null && names.Contains(p.Name))
                .ToListAsync(ct))
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        return new PeopleIndex(db, time, byTmdbId, byName);
    }

    private Task<int> LockAsync(CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({UpsertLockKey})", ct);

    private static string ComputeHash(MovieImport import)
    {
        // Dictionary order is not significant: sort the translations so the same data always gives the same hash.
        var canonical = import with
        {
            Translations = new SortedDictionary<string, TranslationImport>(
                import.Translations.ToDictionary(), StringComparer.Ordinal),
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical, SeedJson.Options);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>People of one batch: found by TMDB id (or by name for hand-made data), created on first use.</summary>
    private sealed class PeopleIndex(
        AppDbContext db,
        TimeProvider time,
        Dictionary<int, Person> byTmdbId,
        Dictionary<string, Person> byName)
    {
        public Person Get(CreditImport credit)
        {
            var name = credit.Name.Trim();
            if (credit.PersonTmdbId is { } tmdbId)
            {
                if (!byTmdbId.TryGetValue(tmdbId, out var person))
                {
                    person = Create(tmdbId, name);
                    byTmdbId[tmdbId] = person;
                }

                person.Name = name;
                return person;
            }

            if (!byName.TryGetValue(name, out var named))
            {
                named = Create(null, name);
                byName[name] = named;
            }

            return named;
        }

        private Person Create(int? tmdbId, string name)
        {
            var person = new Person { Id = Guid.CreateVersion7(time.GetUtcNow()), TmdbId = tmdbId, Name = name };
            db.Add(person);
            return person;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Catalog upsert batch of {Count}: {Created} created, {Updated} updated, {Unchanged} unchanged")]
    private static partial void LogBatch(ILogger logger, int count, int created, int updated, int unchanged);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Movie {TmdbId} refers to unknown genre {GenreId}; the genre is skipped")]
    private static partial void LogUnknownGenre(ILogger logger, int tmdbId, int genreId);
}
