namespace FilmSearch.Core.Owners;

/// <summary>
/// The owner of user-scoped resources (cart, orders, conversations) for the current request.
/// Before authentication (I17) it is the anonymous session id from the <c>fs_sid</c> cookie;
/// after it, the user id. Business code and AI tools take the owner only from here, never from input.
/// </summary>
public interface IOwnerContext
{
    Guid OwnerId { get; }
}
