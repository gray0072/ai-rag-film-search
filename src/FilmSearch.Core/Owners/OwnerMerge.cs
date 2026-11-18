namespace FilmSearch.Core.Owners;

// Anonymous → user merge on login (FR-USR-2, I17). Before signing in, a visitor's cart, orders, library and
// conversations belong to the anonymous session id (fs_sid). When the visitor signs in, every module moves its data of
// that id to the user id. Each module does it for its own tables (no cross-module table access); the merger runs all
// handlers in one database transaction, so a login either moves everything or nothing.

/// <summary>Moves one module's data of an anonymous owner to a user. Registered by every module that has owner-scoped data.</summary>
public interface IOwnerMergeHandler
{
    /// <summary>
    /// Moves the data of <paramref name="fromOwnerId"/> to <paramref name="toOwnerId"/>. Runs inside the merger's
    /// transaction; must be idempotent (a repeated merge finds nothing left to move).
    /// </summary>
    Task MergeAsync(Guid fromOwnerId, Guid toOwnerId, CancellationToken cancellationToken);
}

/// <summary>Runs every <see cref="IOwnerMergeHandler"/> in one transaction.</summary>
public interface IOwnerMerger
{
    Task MergeAsync(Guid fromOwnerId, Guid toOwnerId, CancellationToken cancellationToken);
}
