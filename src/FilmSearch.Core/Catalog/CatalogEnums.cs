namespace FilmSearch.Core.Catalog;

/// <summary>Where a movie came from.</summary>
public enum MovieSource
{
    Seed = 0,
    Tmdb = 1,
    Manual = 2,
}

public enum CreditRole
{
    Director = 0,
    Writer = 1,
    Cast = 2,
}

public enum OfferType
{
    Buy = 0,
    Rent = 1,
}

/// <summary>Sort order of the movie list (spec 04). The default is <see cref="Popularity"/>.</summary>
public enum MovieSort
{
    Popularity = 0,
    Rating = 1,
    Year = 2,
    Title = 3,
}
