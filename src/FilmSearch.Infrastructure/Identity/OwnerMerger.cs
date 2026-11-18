using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FilmSearch.Infrastructure.Identity;

/// <summary>Runs the login merge of every module in one transaction (FR-USR-2): all data moves, or none.</summary>
internal sealed partial class OwnerMerger(AppDbContext db, IEnumerable<IOwnerMergeHandler> handlers, ILogger<OwnerMerger> logger) : IOwnerMerger
{
    public async Task MergeAsync(Guid fromOwnerId, Guid toOwnerId, CancellationToken cancellationToken)
    {
        if (fromOwnerId == toOwnerId)
        {
            return;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var handler in handlers)
            {
                await handler.MergeAsync(fromOwnerId, toOwnerId, ct);
            }

            await transaction.CommitAsync(ct);
        }, cancellationToken);

        LogMerged(logger, fromOwnerId, toOwnerId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Merged the data of anonymous owner {AnonymousId} into user {UserId}")]
    private static partial void LogMerged(ILogger logger, Guid anonymousId, Guid userId);
}
