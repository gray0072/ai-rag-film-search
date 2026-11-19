using FilmSearch.Core.Assistant;
using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Assistant;

/// <summary>Conversations of the current owner in the <c>assistant</c> schema. Every query is filtered by the owner.</summary>
internal sealed class ConversationStore(AppDbContext db, IOwnerContext owner, TimeProvider time) : IConversationStore
{
    public Conversation Add(string locale, string profileName)
    {
        var now = time.GetUtcNow();
        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(now),
            OwnerId = owner.OwnerId,
            Locale = locale,
            ProfileName = profileName,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Add(conversation);
        return conversation;
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(int limit, CancellationToken cancellationToken) =>
        await db.Set<Conversation>()
            .AsNoTracking()
            .Where(c => c.OwnerId == owner.OwnerId)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Conversation>().SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == owner.OwnerId, cancellationToken);

    public async Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(Guid conversationId, int afterSeq, CancellationToken cancellationToken) =>
        await db.Set<ConversationMessage>()
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Seq > afterSeq)
            .Where(m => db.Set<Conversation>().Any(c => c.Id == m.ConversationId && c.OwnerId == owner.OwnerId))
            .OrderBy(m => m.Seq)
            .ToListAsync(cancellationToken);

    public void AddMessage(ConversationMessage message) => db.Add(message);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, MessageFeedback>> GetFeedbackAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0)
        {
            return new Dictionary<Guid, MessageFeedback>();
        }

        var ids = messageIds.ToList();
        return await db.Set<MessageFeedback>()
            .AsNoTracking()
            .Where(f => ids.Contains(f.MessageId))
            .ToDictionaryAsync(f => f.MessageId, cancellationToken);
    }

    public async Task<FeedbackResult> SaveFeedbackAsync(Guid messageId, int rating, string? comment, CancellationToken cancellationToken)
    {
        var message = await db.Set<ConversationMessage>()
            .AsNoTracking()
            .Where(m => m.Id == messageId)
            .Where(m => db.Set<Conversation>().Any(c => c.Id == m.ConversationId && c.OwnerId == owner.OwnerId))
            .Select(m => new { m.Role, m.ToolCallsJson })
            .SingleOrDefaultAsync(cancellationToken);

        if (message is null)
        {
            return FeedbackResult.MessageNotFound;
        }

        if (message.Role != MessageRole.Assistant || message.ToolCallsJson is not null)
        {
            return FeedbackResult.NotAnAnswer;
        }

        // Upsert: the latest rating wins, also when two clicks race.
        var now = time.GetUtcNow();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO assistant.message_feedback (message_id, rating, comment, created_at)
            VALUES ({messageId}, {rating}, {comment}, {now})
            ON CONFLICT (message_id) DO UPDATE
            SET rating = excluded.rating, comment = excluded.comment, created_at = excluded.created_at
            """,
            cancellationToken);

        return FeedbackResult.Saved;
    }

    public async Task<long> GetOwnerTokensSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        await db.Set<ConversationMessage>()
            .Where(m => m.Role == MessageRole.Assistant && m.CreatedAt >= since && m.InputTokens != null)
            .Where(m => db.Set<Conversation>().Any(c => c.Id == m.ConversationId && c.OwnerId == owner.OwnerId))
            .SumAsync(m => (m.InputTokens ?? 0) + (m.OutputTokens ?? 0), cancellationToken);

    public Task<string?> GetTraceIdAsync(Guid messageId, CancellationToken cancellationToken) =>
        db.Set<ConversationMessage>()
            .Where(m => m.Id == messageId)
            .Where(m => db.Set<Conversation>().Any(c => c.Id == m.ConversationId && c.OwnerId == owner.OwnerId))
            .Select(m => m.TraceId)
            .SingleOrDefaultAsync(cancellationToken);
}
