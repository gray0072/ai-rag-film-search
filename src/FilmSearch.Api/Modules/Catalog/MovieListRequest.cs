using System.ComponentModel.DataAnnotations;
using FilmSearch.Core.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace FilmSearch.Api.Modules.Catalog;

/// <summary>Query string of <c>GET /api/movies</c> (spec 04). Validated at the boundary; invalid input → 400 Problem Details.</summary>
public sealed class MovieListRequest : IValidatableObject
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    [Range(1, 10_000)]
    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [Range(1, MaxPageSize)]
    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }

    // A string, not an enum: minimal APIs bind enums case-sensitively, and the API uses lowercase values.
    /// <summary><c>popularity</c> (default), <c>rating</c>, <c>year</c> or <c>title</c>. Case-insensitive.</summary>
    [StringLength(20)]
    [FromQuery(Name = "sort")]
    public string? Sort { get; init; }

    /// <summary>Comma-separated genre slugs; a movie matches any of them.</summary>
    [StringLength(500)]
    [FromQuery(Name = "genres")]
    public string? Genres { get; init; }

    [Range(1870, 2100)]
    [FromQuery(Name = "yearFrom")]
    public int? YearFrom { get; init; }

    [Range(1870, 2100)]
    [FromQuery(Name = "yearTo")]
    public int? YearTo { get; init; }

    [Range(0.0, 10.0)]
    [FromQuery(Name = "minRating")]
    public decimal? MinRating { get; init; }

    [Range(1, 1000)]
    [FromQuery(Name = "runtimeMin")]
    public int? RuntimeMin { get; init; }

    [Range(1, 1000)]
    [FromQuery(Name = "runtimeMax")]
    public int? RuntimeMax { get; init; }

    /// <summary>Original language, ISO 639-1 (e.g. <c>en</c>, <c>sv</c>).</summary>
    [StringLength(3, MinimumLength = 2)]
    [FromQuery(Name = "language")]
    public string? Language { get; init; }

    /// <summary>Maximum price in minor units (cents).</summary>
    [Range(0, 1_000_000)]
    [FromQuery(Name = "priceMax")]
    public long? PriceMax { get; init; }

    /// <summary><c>buy</c> or <c>rent</c>. Case-insensitive.</summary>
    [StringLength(10)]
    [FromQuery(Name = "offerType")]
    public string? OfferType { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (YearFrom > YearTo)
        {
            yield return new ValidationResult("yearFrom must not be greater than yearTo.", [nameof(YearFrom)]);
        }

        if (RuntimeMin > RuntimeMax)
        {
            yield return new ValidationResult("runtimeMin must not be greater than runtimeMax.", [nameof(RuntimeMin)]);
        }

        if (Sort is not null && !TryParse<MovieSort>(Sort, out _))
        {
            yield return new ValidationResult("sort must be one of: popularity, rating, year, title.", [nameof(Sort)]);
        }

        if (OfferType is not null && !TryParse<OfferType>(OfferType, out _))
        {
            yield return new ValidationResult("offerType must be one of: buy, rent.", [nameof(OfferType)]);
        }
    }

    public MovieListQuery ToQuery() => new(
        new MovieFilter
        {
            GenreSlugs = ParseSlugs(Genres),
            YearFrom = YearFrom,
            YearTo = YearTo,
            MinRating = MinRating,
            RuntimeMin = RuntimeMin,
            RuntimeMax = RuntimeMax,
            Language = string.IsNullOrWhiteSpace(Language) ? null : Language,
            PriceMax = PriceMax,
            OfferType = TryParse<OfferType>(OfferType, out var offerType) ? offerType : null,
        },
        TryParse<MovieSort>(Sort, out var sort) ? sort : MovieSort.Popularity,
        Page ?? 1,
        PageSize ?? DefaultPageSize);

    /// <summary>Case-insensitive enum parsing by name only (numbers such as <c>sort=1</c> are rejected).</summary>
    private static bool TryParse<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && !char.IsAsciiDigit(value.Trim()[0])
            && Enum.TryParse(value.Trim(), ignoreCase: true, out result)
            && Enum.IsDefined(result);
    }

    private static string[] ParseSlugs(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(slug => slug.ToLowerInvariant())
                .Distinct()];
}
