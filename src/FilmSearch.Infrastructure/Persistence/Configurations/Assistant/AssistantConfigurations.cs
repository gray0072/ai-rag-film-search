using FilmSearch.Core.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilmSearch.Infrastructure.Persistence.Configurations.Assistant;

// EF Core mapping of the Assistant module: tables in the "assistant" schema, enums as strings. Messages keep movie ids
// (uuid[]) without foreign keys: a removed movie must not delete or block chat history.

internal static class AssistantSchema
{
    public const string Name = "assistant";
}

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations", AssistantSchema.Name);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Title).HasMaxLength(AssistantLimits.MaxTitleLength + 1);
        builder.Property(c => c.Locale).HasMaxLength(8);
        builder.Property(c => c.ProfileName).HasMaxLength(100);
        builder.Property(c => c.EstimatedCostUsd).HasPrecision(18, 8);
        builder.Ignore(c => c.TotalTokens);

        builder.HasIndex(c => new { c.OwnerId, c.UpdatedAt });

        builder.HasMany<ConversationMessage>()
            .WithOne()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("messages", AssistantSchema.Name);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.ToolCallsJson).HasColumnType("jsonb");
        builder.Property(m => m.ToolCallId).HasMaxLength(200);
        builder.Property(m => m.Model).HasMaxLength(200);
        builder.Property(m => m.PromptVersion).HasMaxLength(100);
        builder.Property(m => m.EstimatedCostUsd).HasPrecision(18, 8);
        builder.Ignore(m => m.IsVisible);

        // Order of the history; also stops two writers from giving out the same position.
        builder.HasIndex(m => new { m.ConversationId, m.Seq }).IsUnique();

        builder.HasOne<MessageFeedback>()
            .WithOne()
            .HasForeignKey<MessageFeedback>(f => f.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MessageFeedbackConfiguration : IEntityTypeConfiguration<MessageFeedback>
{
    public void Configure(EntityTypeBuilder<MessageFeedback> builder)
    {
        builder.ToTable("message_feedback", AssistantSchema.Name, t => t.HasCheckConstraint("ck_message_feedback_rating", "rating IN (-1, 1)"));
        builder.HasKey(f => f.MessageId);
        builder.Property(f => f.Comment).HasMaxLength(AssistantLimits.MaxFeedbackCommentLength);
    }
}
