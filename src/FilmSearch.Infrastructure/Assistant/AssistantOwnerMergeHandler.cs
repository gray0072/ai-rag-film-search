using FilmSearch.Core.Assistant;
using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Assistant;

/// <summary>Login merge of the Assistant module (FR-USR-2): conversations and their checkout confirmations move to the user.</summary>
internal sealed class AssistantOwnerMergeHandler(AppDbContext db) : IOwnerMergeHandler
{
    public async Task MergeAsync(Guid fromOwnerId, Guid toOwnerId, CancellationToken cancellationToken)
    {
        await db.Set<Conversation>()
            .Where(c => c.OwnerId == fromOwnerId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.OwnerId, toOwnerId), cancellationToken);
        await db.Set<CheckoutConfirmation>()
            .Where(c => c.OwnerId == fromOwnerId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.OwnerId, toOwnerId), cancellationToken);
    }
}
