using System.ComponentModel.DataAnnotations;
using FilmSearch.Core.Assistant;

namespace FilmSearch.Api.Modules.Assistant;

/// <summary>Body of <c>POST /api/chat/conversations</c>.</summary>
public sealed class CreateConversationRequest
{
    /// <summary>Locale of movie texts when a message request has no Accept-Language header: en, ru or sv. Default: the request locale.</summary>
    [AllowedValues(null, "en", "ru", "sv")]
    public string? Locale { get; init; }
}

/// <summary>Body of <c>POST /api/chat/conversations/{id}/messages</c>.</summary>
public sealed class SendMessageRequest
{
    /// <summary>The user's message, 1–2000 characters.</summary>
    [Required]
    [StringLength(AssistantLimits.MaxMessageLength, MinimumLength = 1)]
    public string? Text { get; init; }
}

/// <summary>Body of <c>POST /api/chat/messages/{messageId}/feedback</c>.</summary>
public sealed class MessageFeedbackRequest
{
    /// <summary>1 (thumbs up) or -1 (thumbs down).</summary>
    [Required]
    [AllowedValues(1, -1)]
    public int? Rating { get; init; }

    /// <summary>Optional comment, up to 1000 characters.</summary>
    [StringLength(AssistantLimits.MaxFeedbackCommentLength)]
    public string? Comment { get; init; }
}
