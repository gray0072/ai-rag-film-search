using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FilmSearch.Api.Modules.Commerce;

/// <summary>Body of <c>POST /api/cart/items</c>.</summary>
public sealed class AddCartItemRequest
{
    [Required]
    public Guid? MovieId { get; init; }

    /// <summary><c>buy</c> or <c>rent</c>.</summary>
    [Required]
    public OfferType? OfferType { get; init; }
}

/// <summary>Maps refused commerce operations to Problem Details with a machine-readable <c>code</c> (camelCase <see cref="CommerceError"/>).</summary>
internal static class CommerceProblems
{
    public static ProblemHttpResult From(CommerceException exception) => TypedResults.Problem(
        statusCode: exception.Error switch
        {
            CommerceError.MovieNotFound or CommerceError.OrderNotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict,
        },
        title: exception.Message,
        extensions: new Dictionary<string, object?> { ["code"] = JsonNamingPolicy.CamelCase.ConvertName(exception.Error.ToString()) });
}
