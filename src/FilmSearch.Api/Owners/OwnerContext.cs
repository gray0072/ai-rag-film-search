using FilmSearch.Core.Owners;

namespace FilmSearch.Api.Owners;

/// <summary>Scoped <see cref="IOwnerContext"/>. The owner is set once per request by <see cref="OwnerCookieMiddleware"/>.</summary>
public sealed class OwnerContext : IOwnerContext
{
    private Guid? _ownerId;

    public Guid OwnerId => _ownerId
        ?? throw new InvalidOperationException("The owner is not resolved for this request. Owner resolution runs only for /api endpoints.");

    public bool IsResolved => _ownerId.HasValue;

    /// <summary>The owner is the anonymous session id (<c>fs_sid</c>), not a signed-in user.</summary>
    public bool IsAnonymous { get; private set; }

    internal void Set(Guid ownerId, bool isAnonymous)
    {
        if (_ownerId.HasValue)
        {
            throw new InvalidOperationException("The owner is already set for this request.");
        }

        _ownerId = ownerId;
        IsAnonymous = isAnonymous;
    }
}
