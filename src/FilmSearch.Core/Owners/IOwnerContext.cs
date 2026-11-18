namespace FilmSearch.Core.Owners;

/// <summary>
/// The owner of user-scoped resources (cart, orders, conversations) for the current request.
/// It is the user id of a signed-in user (I17), otherwise the anonymous session id from the <c>fs_sid</c> cookie;
/// the anonymous data moves to the user on login (<see cref="IOwnerMerger"/>).
/// Business code and AI tools take the owner only from here, never from input.
/// </summary>
public interface IOwnerContext
{
    Guid OwnerId { get; }
}
